using System.Text.Json;
using Golether.Core.Identity;

namespace Golether.Core.Tests;

/// <summary>
/// Tests of <see cref="PeerId"/>.
/// </summary>
public sealed class PeerIdTests
{
    /// <summary>
    /// A valid identifier is parsed case-insensitively and normalized to lowercase.
    /// </summary>
    [Fact]
    public void Parse_NormalizesToLowercase()
    {
        var id = PeerId.Parse(new string('A', 64));

        Assert.Equal(new string('a', 64), id.Value);
        Assert.False(id.IsEmpty);
    }

    /// <summary>
    /// Values of wrong length or with non-hexadecimal characters are rejected.
    /// </summary>
    /// <param name="value">The value.</param>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void TryParse_RejectsInvalidValues(string value)
    {
        Assert.False(PeerId.TryParse(value, out _));
        Assert.Throws<FormatException>(() => PeerId.Parse(value));
    }

    /// <summary>
    /// The identifier is the SHA-256 of the public key, so equal keys give equal identifiers.
    /// </summary>
    [Fact]
    public void FromSubjectPublicKeyInfo_IsDeterministic()
    {
        var key = new byte[] { 1, 2, 3, 4 };

        var first = PeerId.FromSubjectPublicKeyInfo(key);
        var second = PeerId.FromSubjectPublicKeyInfo(key);

        Assert.Equal(first, second);
        Assert.Equal("9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a", first.Value);
    }

    /// <summary>
    /// The short form shows four groups of the first 16 characters.
    /// </summary>
    [Fact]
    public void ToShortString_FormatsFourGroups()
    {
        var id = PeerId.Parse("3fa91c2b77d0e415" + new string('0', 48));

        Assert.Equal("3FA9-1C2B-77D0-E415", id.ToShortString());
        Assert.Equal(string.Empty, default(PeerId).ToShortString());
    }

    /// <summary>
    /// A default identifier has no value.
    /// </summary>
    [Fact]
    public void Default_IsEmpty()
    {
        var id = default(PeerId);

        Assert.True(id.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => id.Value);
    }

    /// <summary>
    /// The identifier is serialized as a JSON string and invalid strings are rejected.
    /// </summary>
    [Fact]
    public void Json_RoundTripsAndValidates()
    {
        var id = PeerId.Parse(new string('b', 64));

        var json = JsonSerializer.Serialize(id);
        Assert.Equal($"\"{id.Value}\"", json);
        Assert.Equal(id, JsonSerializer.Deserialize<PeerId>(json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PeerId>("\"nope\""));
    }
}
