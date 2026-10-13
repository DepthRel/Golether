using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Security.Invites;
using Microsoft.Extensions.Time.Testing;

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
        Assert.StartsWith(Invite.WebLinkPrefix, link, StringComparison.Ordinal);
        Assert.EndsWith("#", Invite.WebLinkPrefix, StringComparison.Ordinal);
        Assert.Equal(invite.ToLink()[Invite.LinkPrefix.Length..], link[Invite.WebLinkPrefix.Length..]);
        Assert.Equal(invite.HostPeerId, parsed.HostPeerId);
        Assert.Equal(invite.Token, parsed.Token);
        Assert.Equal(invite.Endpoints, parsed.Endpoints);
        Assert.Equal(invite.Rendezvous, parsed.Rendezvous);
        Assert.Equal(invite.ExpiresAt, parsed.ExpiresAt);
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
        Assert.Throws<FormatException>(() => Invite.ParseLink(Invite.WebLinkPrefix + "not-an-invitation"));
        Assert.Throws<FormatException>(() => Invite.ParseLink("https://example.org/join/#" + Sample().ToLink()[Invite.LinkPrefix.Length..]));
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
