using Microsoft.Extensions.Time.Testing;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Security.Invites;

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
        Assert.StartsWith(Invite.LINK_PREFIX, invite.ToLink(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The rendezvous server and the room survive the link; a link without them has none.
    /// </summary>
    [Fact]
    public void Link_CarriesRendezvous()
    {
        var point = new RendezvousPoint(PeerEndpoint.Parse("meet.example.org:47900"), RendezvousPoint.CreateRoom());
        var invite = Sample() with { Rendezvous = point };

        Assert.Equal(point, Invite.ParseLink(invite.ToLink()).Rendezvous);
        Assert.Null(Invite.ParseLink(Sample().ToLink()).Rendezvous);
        Assert.DoesNotContain("meet", DecodePayload(Sample().ToLink()), StringComparison.Ordinal);
    }

    /// <summary>
    /// A rendezvous part that is incomplete or malformed makes the link damaged instead of being dropped silently.
    /// </summary>
    /// <param name="meet">The server field.</param>
    /// <param name="room">The room field.</param>
    [Theory]
    [InlineData("meet.example.org:47900", null)]
    [InlineData(null, "AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("meet.example.org", "AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("meet.example.org:47900", "short")]
    public void Link_RejectsBrokenRendezvous(string? meet, string? room)
    {
        var payload = System.Text.Json.Nodes.JsonNode.Parse(DecodePayload(Sample().ToLink()))!.AsObject();
        payload["meet"] = meet;
        payload["room"] = room;
        var link = Invite.LINK_PREFIX + System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()));

        Assert.Throws<FormatException>(() => Invite.ParseLink(link));
    }

    /// <summary>
    /// Decodes the JSON payload of a link.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <returns>The JSON text.</returns>
    private static string DecodePayload(string link)
        => System.Text.Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(link.AsSpan(Invite.LINK_PREFIX.Length)));

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
    /// One invitation serves the whole company: the token is accepted again and again until its lifetime ends.
    /// </summary>
    [Fact]
    public void Registry_TokenServesTheWholeCompany()
    {
        var time = new FakeTimeProvider();
        var registry = new InviteRegistry(time);
        var (token, _) = registry.Issue(TimeSpan.FromMinutes(15));

        for (var guest = 0; guest < 4; guest++)
        {
            Assert.Equal(InviteCheckResult.Accepted, registry.TryUse(token, out _));
        }

        time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(InviteCheckResult.Expired, registry.TryUse(token, out _));
        Assert.Equal(InviteCheckResult.Unknown, registry.TryUse(token, out _));
    }

    /// <summary>
    /// A leaked link cannot pester the host for ever: an invitation brings a limited number of requests.
    /// </summary>
    [Fact]
    public void Registry_LimitsTheRequestsOfOneInvitation()
    {
        var registry = new InviteRegistry(new FakeTimeProvider());
        var (token, _) = registry.Issue();

        for (var attempt = 0; attempt < InviteRegistry.MAX_USES; attempt++)
        {
            Assert.Equal(InviteCheckResult.Accepted, registry.TryUse(token, out _));
        }

        Assert.Equal(InviteCheckResult.Expired, registry.TryUse(token, out _));
        Assert.Equal(InviteCheckResult.Unknown, registry.TryUse(token, out _));
    }

    /// <summary>
    /// Invitations are numbered in the order they are made, which is how "made after a guest was removed" is told.
    /// </summary>
    [Fact]
    public void Registry_NumbersInvitationsInOrder()
    {
        var registry = new InviteRegistry(new FakeTimeProvider());
        Assert.Equal(0, registry.LastSequence);

        var (first, _) = registry.Issue();
        var barrier = registry.LastSequence;
        var (second, _) = registry.Issue();

        Assert.Equal(InviteCheckResult.Accepted, registry.TryUse(first, out var firstNumber));
        Assert.Equal(InviteCheckResult.Accepted, registry.TryUse(second, out var secondNumber));
        Assert.True(firstNumber <= barrier);
        Assert.True(secondNumber > barrier);
        Assert.Equal(secondNumber, registry.LastSequence);
        Assert.Equal(InviteCheckResult.Unknown, registry.TryUse(Invite.CreateToken(), out var unknown));
        Assert.Equal(0, unknown);
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
        Assert.Equal(InviteCheckResult.Unknown, registry.TryUse(token, out _));
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

        Assert.Equal(InviteCheckResult.Expired, registry.TryUse(token, out _));
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
        Assert.Equal(InviteCheckResult.Unknown, registry.TryUse(token, out _));
        Assert.Equal(InviteCheckResult.Unknown, registry.TryUse(null, out _));
        Assert.Equal(InviteCheckResult.Unknown, registry.TryUse("short", out _));
        Assert.Equal(InviteCheckResult.Unknown, registry.TryUse(Invite.CreateToken(), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Issue(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Issue(TimeSpan.FromDays(8)));
    }

    /// <summary>
    /// An expiry time that cannot be turned into a date makes a damaged link, not a failure of another kind.
    /// </summary>
    /// <param name="expires">The expiry time written into the link.</param>
    [Theory]
    [InlineData("9000000000000000000")]
    [InlineData("253402300800")]
    [InlineData("-1")]
    public void Link_RejectsImpossibleExpiry(string expires)
    {
        var json = DecodePayload(Sample().ToLink()).Replace("\"expires\":2000000000", "\"expires\":" + expires, StringComparison.Ordinal);
        Assert.Contains(expires, json, StringComparison.Ordinal);
        var link = Invite.LINK_PREFIX + System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Throws<FormatException>(() => Invite.ParseLink(link));
    }
}
