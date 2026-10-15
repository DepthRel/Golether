using Microsoft.Extensions.Time.Testing;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Security.Invites;

namespace Golether.Security.Tests;

/// <summary>
/// Tests of the two spellings of an invitation (the key and the web link) and of the lifetime limits.
/// </summary>
public sealed class InviteWebLinkTests
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
        Endpoints = [PeerEndpoint.Parse("192.168.1.10:47800")],
        Token = Invite.CreateToken(),
        ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000),
        Rendezvous = new RendezvousPoint(PeerEndpoint.Parse("meet.example.org:47900"), RendezvousPoint.CreateRoom()),
    };

    /// <summary>
    /// The web link carries the same invitation as the key, after the <c>#</c> of the project page.
    /// </summary>
    [Fact]
    public void WebLink_CarriesTheSameInvitation()
    {
        var invite = Sample();

        var link = invite.ToWebLink();
        var parsed = Invite.ParseLink(" " + link + "\n");

        Assert.StartsWith("https://", link, StringComparison.Ordinal);
        Assert.StartsWith(Invite.WEB_LINK_PREFIX, link, StringComparison.Ordinal);
        Assert.EndsWith("#", Invite.WEB_LINK_PREFIX, StringComparison.Ordinal);
        Assert.Equal(invite.ToLink()[Invite.LINK_PREFIX.Length..], link[Invite.WEB_LINK_PREFIX.Length..]);
        Assert.Equal(invite.HostPeerId, parsed.HostPeerId);
        Assert.Equal(invite.Token, parsed.Token);
        Assert.Equal(invite.Endpoints, parsed.Endpoints);
        Assert.Equal(invite.Rendezvous, parsed.Rendezvous);
        Assert.Equal(invite.ExpiresAt, parsed.ExpiresAt);
    }

    /// <summary>
    /// Two invitations of one session differ from their first characters: the one-time part is written first, so a
    /// new invitation looks new even in a field that shows only its beginning. The rest stays readable as before.
    /// </summary>
    [Fact]
    public void Link_ShowsItsNewPartFirst()
    {
        var first = Sample();
        var second = first with { Token = Invite.CreateToken() };

        var one = first.ToLink()[Invite.LINK_PREFIX.Length..];
        var other = second.ToLink()[Invite.LINK_PREFIX.Length..];

        Assert.NotEqual(one[..40], other[..40]);
        Assert.StartsWith("{\"token\":\"" + first.Token, System.Text.Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(one)), StringComparison.Ordinal);
        Assert.Equal(second.Token, Invite.ParseLink(second.ToLink()).Token);
        Assert.Equal(first.Endpoints, Invite.ParseLink(first.ToLink()).Endpoints);
    }

    /// <summary>
    /// A link written by an earlier version, with the token in the middle, is still read.
    /// </summary>
    [Fact]
    public void Link_OfAnEarlierVersionIsStillRead()
    {
        var token = Invite.CreateToken();
        var json = $$"""{"host":"{{new string('c', 64)}}","name":"Вы","session":"Вечер кино","endpoints":["192.168.1.10:47800"],"token":"{{token}}","expires":2000000000}""";
        var link = Invite.LINK_PREFIX + System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(json));

        var parsed = Invite.ParseLink(link);

        Assert.Equal(token, parsed.Token);
        Assert.Equal("Вечер кино", parsed.SessionName);
    }

    /// <summary>
    /// Only the two spellings look like an invitation; a look-alike on another site or scheme does not.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">Whether it looks like an invitation.</param>
    [Theory]
    [InlineData("golether://join/v1/abc", true)]
    [InlineData("  golether://join/v1/abc  ", true)]
    [InlineData("https://depthrel.github.io/Golether/join/#abc", true)]
    [InlineData("https://depthrel.github.io/Golether/join/abc", false)]
    [InlineData("https://example.org/Golether/join/#abc", false)]
    [InlineData("http://depthrel.github.io/Golether/join/#abc", false)]
    [InlineData("GOLETHER://join/v1/abc", false)]
    [InlineData("C:\\Films\\film.mkv", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeLink_KnowsBothSpellings(string? text, bool expected)
    {
        Assert.Equal(expected, Invite.LooksLikeLink(text));
    }

    /// <summary>
    /// A web link with a damaged invitation is refused like a damaged key.
    /// </summary>
    [Fact]
    public void WebLink_RejectsDamagedContent()
    {
        Assert.Throws<FormatException>(() => Invite.ParseLink(Invite.WEB_LINK_PREFIX + "not-an-invitation"));
        Assert.Throws<FormatException>(() => Invite.ParseLink("https://example.org/join/#" + Sample().ToLink()[Invite.LINK_PREFIX.Length..]));
    }

    /// <summary>
    /// No invitation lives longer than an hour, and the link lives a quarter of it.
    /// </summary>
    [Fact]
    public void Registry_LimitsLifetimeToOneHour()
    {
        var time = new FakeTimeProvider();
        var registry = new InviteRegistry(time);

        Assert.Equal(TimeSpan.FromHours(1), InviteRegistry.MaxLifetime);
        Assert.Equal(TimeSpan.FromMinutes(15), InviteRegistry.WebLinkLifetime);
        Assert.Equal(time.GetUtcNow() + TimeSpan.FromHours(1), registry.Issue(InviteRegistry.MaxLifetime).ExpiresAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Issue(InviteRegistry.MaxLifetime + TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Issue(TimeSpan.FromHours(24)));
    }
}
