using System.Security.Cryptography;
using Golether.Security.Identity;
using Golether.Security.Secrets;
using Microsoft.Extensions.Time.Testing;

namespace Golether.Security.Tests;

/// <summary>
/// Tests of <see cref="DeviceIdentity"/>, <see cref="PeerCertificates"/> and <see cref="FileDeviceIdentityStore"/>.
/// </summary>
public sealed class IdentityTests
{
    /// <summary>
    /// A signature verifies against the public certificate and yields the device identifier.
    /// </summary>
    [Fact]
    public void Signature_VerifiesAndIdentifiesSigner()
    {
        using var identity = DeviceIdentity.CreateNew(TimeProvider.System);
        var data = "hello"u8.ToArray();

        var signature = identity.Sign(data);

        Assert.True(PeerCertificates.TryVerify(identity.PublicCertificateDer, data, signature, out var signer));
        Assert.Equal(identity.PeerId, signer);
    }

    /// <summary>
    /// Tampered data, a foreign certificate or garbage are rejected.
    /// </summary>
    [Fact]
    public void Signature_RejectsTamperingAndForeignKeys()
    {
        using var identity = DeviceIdentity.CreateNew(TimeProvider.System);
        using var other = DeviceIdentity.CreateNew(TimeProvider.System);
        var signature = identity.Sign("hello"u8);

        Assert.False(PeerCertificates.TryVerify(identity.PublicCertificateDer, "hellO"u8, signature, out _));
        Assert.False(PeerCertificates.TryVerify(other.PublicCertificateDer, "hello"u8, signature, out _));
        Assert.False(PeerCertificates.TryVerify([1, 2, 3], "hello"u8, signature, out _));
        Assert.False(PeerCertificates.TryVerify(identity.PublicCertificateDer, "hello"u8, [], out _));
    }

    /// <summary>
    /// Two identities differ and each survives a PKCS#12 round trip.
    /// </summary>
    [Fact]
    public void Pkcs12_RoundTripKeepsIdentifier()
    {
        using var first = DeviceIdentity.CreateNew(TimeProvider.System);
        using var second = DeviceIdentity.CreateNew(TimeProvider.System);
        using var restored = DeviceIdentity.FromPkcs12(first.ExportPkcs12());

        Assert.NotEqual(first.PeerId, second.PeerId);
        Assert.Equal(first.PeerId, restored.PeerId);
    }

    /// <summary>
    /// The store creates the identity once and loads the same identity later.
    /// </summary>
    [Fact]
    public void Store_CreatesThenLoadsSameIdentity()
    {
        var directory = Directory.CreateTempSubdirectory("golether-identity-");
        try
        {
            var store = new FileDeviceIdentityStore(directory.FullName, new FilePermissionSecretProtector(), new FakeTimeProvider());

            using var created = store.LoadOrCreate();
            using var loaded = store.LoadOrCreate();

            Assert.Equal(created.PeerId, loaded.PeerId);
            Assert.True(File.Exists(store.FilePath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A corrupted identity file is reported and left untouched.
    /// </summary>
    [Fact]
    public void Store_ReportsCorruptedFile()
    {
        var directory = Directory.CreateTempSubdirectory("golether-identity-");
        try
        {
            var store = new FileDeviceIdentityStore(directory.FullName, new FilePermissionSecretProtector(), TimeProvider.System);
            File.WriteAllBytes(store.FilePath, [1, 2, 3]);

            Assert.Throws<CryptographicException>(store.LoadOrCreate);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(store.FilePath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The platform protector restores what it protected.
    /// </summary>
    [Fact]
    public void DefaultProtector_RoundTrips()
    {
        var protector = SecretProtectors.CreateDefault();
        var secret = RandomNumberGenerator.GetBytes(64);

        Assert.Equal(secret, protector.Unprotect(protector.Protect(secret)));
    }
}
