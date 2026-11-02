using Golether.Client.Services;
using Golether.Security.Identity;
using Golether.Security.Secrets;

namespace Golether.Client.Tests;

/// <summary>
/// Tests of <see cref="RecoveringDeviceIdentityStore"/>: a mobile client whose device key cannot be read starts over.
/// </summary>
public sealed class RecoveringDeviceIdentityStoreTests : IDisposable
{
    /// <summary>
    /// The folder of the key.
    /// </summary>
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("golether-identity-");

    /// <summary>
    /// A readable key stays the same between starts.
    /// </summary>
    [Fact]
    public void ReadableKey_Stays()
    {
        var first = Create().LoadOrCreate();

        var second = Create().LoadOrCreate();

        Assert.Equal(first.PeerId, second.PeerId);
    }

    /// <summary>
    /// A key that cannot be read (restored on another device, the key of the system is gone) is replaced by a new one,
    /// which stays from then on.
    /// </summary>
    [Fact]
    public void UnreadableKey_IsReplaced()
    {
        var old = Create().LoadOrCreate();
        File.WriteAllBytes(Path.Combine(_folder.FullName, FileDeviceIdentityStore.FILE_NAME), [1, 2, 3]);

        var replaced = Create().LoadOrCreate();
        var again = Create().LoadOrCreate();

        Assert.NotEqual(old.PeerId, replaced.PeerId);
        Assert.Equal(replaced.PeerId, again.PeerId);
    }

    /// <inheritdoc />
    public void Dispose() => _folder.Delete(recursive: true);

    /// <summary>
    /// Creates the store over the test folder.
    /// </summary>
    /// <returns>The store.</returns>
    private RecoveringDeviceIdentityStore Create()
        => new(new FileDeviceIdentityStore(_folder.FullName, new PlainProtector(), TimeProvider.System));

    /// <summary>
    /// A protector that keeps the bytes as they are.
    /// </summary>
    private sealed class PlainProtector : ISecretProtector
    {
        /// <inheritdoc />
        public string Description => "plain";

        /// <inheritdoc />
        public byte[] Protect(ReadOnlySpan<byte> plain) => plain.ToArray();

        /// <inheritdoc />
        public byte[] Unprotect(ReadOnlySpan<byte> protectedData) => protectedData.ToArray();
    }
}
