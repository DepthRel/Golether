using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Security.Invites;
using Microsoft.Extensions.Time.Testing;

namespace Golether.Security.Tests;

/// <summary>
/// Tests of <see cref="Invite"/> and <see cref="InviteRegistry"/>.
/// </summary>
public sealed class InviteTests
{
    /// <summary>
    /// Creates a sample invitation.
    /// </summary>
    /// <returns>The invitation.</returns>
    private static Invite Sample() => new()
    {
        HostPeerId = PeerId.Parse(new string('c', 64)),
        HostName = "Вы",
        SessionName = "Вечер кино",
        Endpoints = [PeerEndpoint.Parse("192.168.1.10:47800"), PeerEndpoint.Parse("[2001:db8::5]:47800")],
        Token = Invite.CreateToken(),
        ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000),
    };

    /// <summary>
    /// A link is parsed back into the same invitation.
    /// </summary>
    [Fact]
    public void Link_RoundTrips()
    {
        var invite = Sample();

        var parsed = Invite.ParseLink("  " + invite.ToLink() + "\n");

        Assert.Equal(invite.HostPeerId, parsed.HostPeerId);
        Assert.Equal(invite.SessionName, parsed.SessionName);
        Assert.Equal(invite.Endpoints, parsed.Endpoints);
        Assert.Equal(invite.Token, parsed.Token);
        Assert.Equal(invite.ExpiresAt, parsed.ExpiresAt);
        Assert.StartsWith(Invite.LinkPrefix, invite.ToLink(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Foreign or damaged links are rejected.
    /// </summary>
    /// <param name="link">The link.</param>
    [Theory]
    [InlineData("")]
    [InlineData("https://example.org")]
    [InlineData("golether://join/v1/")]
    [InlineData("golether://join/v1/!!!")]
    [InlineData("golether://join/v1/e30")]
    public void Link_RejectsGarbage(string link) => Assert.Throws<FormatException>(() => Invite.ParseLink(link));

    /// <summary>
    /// A token is accepted once and then unknown.
    /// </summary>
    [Fact]
    public void Registry_TokenIsSingleUse()
    {
        var registry = new InviteRegistry(new FakeTimeProvider());
        var (token, _) = registry.Issue();

        Assert.Equal(InviteCheckResult.Accepted, registry.TryConsume(token));
        Assert.Equal(InviteCheckResult.Unknown, registry.TryConsume(token));
    }

    /// <summary>
    /// An expired token is reported as expired and removed.
    /// </summary>
    [Fact]
    public void Registry_TokenExpires()
    {
        var time = new FakeTimeProvider();
        var registry = new InviteRegistry(time);
        var (token, _) = registry.Issue(TimeSpan.FromMinutes(1));

        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(0, registry.ActiveCount);
        Assert.Equal(InviteCheckResult.Unknown, registry.TryConsume(token));
    }

    /// <summary>
    /// An expired token that is still stored is reported as expired.
    /// </summary>
    [Fact]
    public void Registry_ReportsExpiredToken()
    {
        var time = new FakeTimeProvider();
        var registry = new InviteRegistry(time);
        var (token, _) = registry.Issue(TimeSpan.FromMinutes(1));

        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(InviteCheckResult.Expired, registry.TryConsume(token));
    }

    /// <summary>
    /// Unknown, malformed and revoked tokens are rejected; lifetimes are bounded.
    /// </summary>
    [Fact]
    public void Registry_RejectsInvalidInput()
    {
        var registry = new InviteRegistry(new FakeTimeProvider());
        var (token, _) = registry.Issue();

        Assert.True(registry.Revoke(token));
        Assert.Equal(InviteCheckResult.Unknown, registry.TryConsume(token));
        Assert.Equal(InviteCheckResult.Unknown, registry.TryConsume(null));
        Assert.Equal(InviteCheckResult.Unknown, registry.TryConsume("short"));
        Assert.Equal(InviteCheckResult.Unknown, registry.TryConsume(Invite.CreateToken()));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Issue(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Issue(TimeSpan.FromDays(8)));
    }
}
