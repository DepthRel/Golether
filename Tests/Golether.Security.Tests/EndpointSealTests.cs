using Golether.Core.Networking;
using Golether.Security.Discovery;

namespace Golether.Security.Tests;

/// <summary>
/// Tests of <see cref="EndpointSeal"/>.
/// </summary>
public sealed class EndpointSealTests
{
    /// <summary>
    /// A text made by the Golether.AdminSettings utility for <c>203.0.113.7:47900</c>. The utility carries its own
    /// copy of the algorithm, so this text is what keeps the two from drifting apart.
    /// </summary>
    private const string SEALED_BY_UTILITY = "g1.TYycFZBaTC6E1Hl_DWmIu6Te1pjj-p2eClGhOTkJXtn7ntvwjJRQ9uC_bPEp";

    /// <summary>
    /// A sealed address opens back into the same address and does not show it.
    /// </summary>
    /// <param name="address">The address.</param>
    [Theory]
    [InlineData("203.0.113.7:47900")]
    [InlineData("meet.example.org:443")]
    [InlineData("[2001:db8::5]:47900")]
    public void Seal_OpensBack(string address)
    {
        var endpoint = PeerEndpoint.Parse(address);

        var text = EndpointSeal.Seal(endpoint);

        Assert.StartsWith(EndpointSeal.PREFIX, text, StringComparison.Ordinal);
        Assert.DoesNotContain(endpoint.Host, text, StringComparison.OrdinalIgnoreCase);
        Assert.True(EndpointSeal.TryOpen(text, out var opened));
        Assert.Equal(endpoint, opened);
    }

    /// <summary>
    /// The same address is sealed differently every time, so two published files cannot be compared.
    /// </summary>
    [Fact]
    public void Seal_IsDifferentEveryTime()
    {
        var endpoint = PeerEndpoint.Parse("203.0.113.7:47900");

        Assert.NotEqual(EndpointSeal.Seal(endpoint), EndpointSeal.Seal(endpoint));
    }

    /// <summary>
    /// What the owner's utility seals, the application opens.
    /// </summary>
    [Fact]
    public void TryOpen_ReadsWhatTheUtilitySealed()
    {
        Assert.True(EndpointSeal.TryOpen(SEALED_BY_UTILITY, out var endpoint));
        Assert.Equal(PeerEndpoint.Parse("203.0.113.7:47900"), endpoint);
    }

    /// <summary>
    /// A text that was changed, cut, belongs to another format or is not a seal at all opens nothing.
    /// </summary>
    /// <param name="text">The text from the network.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("203.0.113.7:47900")]
    [InlineData("g1.")]
    [InlineData("g1.AAAA")]
    [InlineData("g1.not base64url!")]
    [InlineData("g2.TYycFZBaTC6E1Hl_DWmIu6Te1pjj-p2eClGhOTkJXtn7ntvwjJRQ9uC_bPEp")]
    [InlineData("g1.TYycFZBaTC6E1Hl_DWmIu6Te1pjj-p2eClGhOTkJXtn7ntvwjJRQ9uC_bPEq")]
    [InlineData("g1.UYycFZBaTC6E1Hl_DWmIu6Te1pjj-p2eClGhOTkJXtn7ntvwjJRQ9uC_bPEp")]
    public void TryOpen_RejectsForeignText(string? text)
    {
        Assert.False(EndpointSeal.TryOpen(text, out _));
    }

    /// <summary>
    /// An overlong text is refused before anything is decoded.
    /// </summary>
    [Fact]
    public void TryOpen_RejectsOverlongText()
    {
        Assert.False(EndpointSeal.TryOpen(EndpointSeal.PREFIX + new string('A', EndpointSeal.MAX_LENGTH), out _));
    }
}
