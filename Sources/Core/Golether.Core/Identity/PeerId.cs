using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Golether.Core.Identity;

/// <summary>
/// Identifier of a participant device: the lowercase hexadecimal SHA-256 fingerprint of the device public key
/// (DER-encoded SubjectPublicKeyInfo).
/// </summary>
/// <remarks>
/// The identifier is derived from the key, so a peer cannot claim an identifier without owning the private key:
/// transports compute it from the authenticated TLS certificate, never from data sent by the peer.
/// </remarks>
[JsonConverter(typeof(PeerIdJsonConverter))]
public readonly record struct PeerId
{
    /// <summary>
    /// The number of hexadecimal characters in an identifier (SHA-256, 32 bytes).
    /// </summary>
    public const int HexLength = 64;

    /// <summary>
    /// The identifier value; <see langword="null"/> for <see langword="default"/> instances.
    /// </summary>
    private readonly string? _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerId"/> struct from a validated value.
    /// </summary>
    /// <param name="value">The lowercase hexadecimal fingerprint.</param>
    private PeerId(string value) => _value = value;

    /// <summary>
    /// Gets a value indicating whether the identifier is the uninitialized <see langword="default"/> value.
    /// </summary>
    public bool IsEmpty => _value is null;

    /// <summary>
    /// Gets the lowercase hexadecimal fingerprint.
    /// </summary>
    /// <exception cref="InvalidOperationException">The identifier is <see langword="default"/>.</exception>
    public string Value => _value ?? throw new InvalidOperationException("The peer identifier is not initialized.");

    /// <summary>
    /// Derives the identifier from a DER-encoded SubjectPublicKeyInfo.
    /// </summary>
    /// <param name="subjectPublicKeyInfo">The encoded public key.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="ArgumentException">The key is empty.</exception>
    public static PeerId FromSubjectPublicKeyInfo(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (subjectPublicKeyInfo.IsEmpty)
        {
            throw new ArgumentException("The public key must not be empty.", nameof(subjectPublicKeyInfo));
        }

        return new PeerId(Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo)));
    }

    /// <summary>
    /// Parses an identifier.
    /// </summary>
    /// <param name="value">64 hexadecimal characters in any case.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="FormatException">The value is not a valid identifier.</exception>
    public static PeerId Parse(string value)
        => TryParse(value, out var id)
            ? id
            : throw new FormatException($"A peer identifier must consist of {HexLength} hexadecimal characters.");

    /// <summary>
    /// Tries to parse an identifier.
    /// </summary>
    /// <param name="value">64 hexadecimal characters in any case.</param>
    /// <param name="id">The parsed identifier, or <see langword="default"/> on failure.</param>
    /// <returns><see langword="true"/> when the value is valid.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out PeerId id)
    {
        id = default;
        if (value is null || value.Length != HexLength || !value.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        id = new PeerId(value.ToLowerInvariant());
        return true;
    }

    /// <summary>
    /// Formats the first 16 hexadecimal characters in four groups for display and verbal comparison,
    /// for example <c>3FA9-1C2B-77D0-E415</c>.
    /// </summary>
    /// <returns>The short form, or an empty string for <see langword="default"/>.</returns>
    public string ToShortString()
    {
        if (_value is null)
        {
            return string.Empty;
        }

        var upper = _value.ToUpperInvariant();
        return $"{upper[..4]}-{upper[4..8]}-{upper[8..12]}-{upper[12..16]}";
    }

    /// <summary>
    /// Returns the full lowercase fingerprint.
    /// </summary>
    /// <returns>The fingerprint, or an empty string for <see langword="default"/>.</returns>
    public override string ToString() => _value ?? string.Empty;
}
