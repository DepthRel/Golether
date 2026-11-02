using System.Security.Cryptography;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Golether.Security.Secrets;

namespace Golether.Security.Android;

/// <summary>
/// Protects secrets with AES-256-GCM under a key of the Android Keystore: the key never leaves the keystore, so a copy
/// of the data of the application is useless without this device.
/// </summary>
public sealed class AndroidKeystoreSecretProtector : ISecretProtector
{
    /// <summary>
    /// The provider of the keystore.
    /// </summary>
    private const string KEY_STORE = "AndroidKeyStore";

    /// <summary>
    /// The name of the key in the keystore.
    /// </summary>
    private const string KEY_ALIAS = "golether.secrets";

    /// <summary>
    /// The cipher.
    /// </summary>
    private const string TRANSFORMATION = "AES/GCM/NoPadding";

    /// <summary>
    /// The size of the key in bits.
    /// </summary>
    private const int KEY_BITS = 256;

    /// <summary>
    /// The size of the initialization vector in bytes; it is stored before the ciphertext.
    /// </summary>
    private const int IV_LENGTH = 12;

    /// <summary>
    /// The size of the authentication tag in bits.
    /// </summary>
    private const int TAG_BITS = 128;

    /// <inheritdoc />
    public string Description => "Android Keystore";

    /// <inheritdoc />
    public byte[] Protect(ReadOnlySpan<byte> plain)
    {
        var input = plain.ToArray();

        try
        {
            var cipher = Cipher.GetInstance(TRANSFORMATION)!;
            cipher.Init(Javax.Crypto.CipherMode.EncryptMode, GetOrCreateKey());
            var encrypted = cipher.DoFinal(input)!;
            var iv = cipher.GetIV()!;

            return [.. iv, .. encrypted];
        }
        catch (Java.Lang.Exception ex)
        {
            throw new CryptographicException("The Android Keystore could not protect the data.", ex);
        }
    }

    /// <inheritdoc />
    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        if (protectedData.Length < IV_LENGTH + (TAG_BITS / 8))
        {
            throw new CryptographicException("The protected data is too short.");
        }

        var iv = protectedData[..IV_LENGTH].ToArray();
        var encrypted = protectedData[IV_LENGTH..].ToArray();

        try
        {
            var cipher = Cipher.GetInstance(TRANSFORMATION)!;
            cipher.Init(Javax.Crypto.CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(TAG_BITS, iv));

            return cipher.DoFinal(encrypted)!;
        }
        catch (Java.Lang.Exception ex)
        {
            throw new CryptographicException("The Android Keystore could not read the data.", ex);
        }
    }

    /// <summary>
    /// Returns the key of the application, creating it on the first use.
    /// </summary>
    /// <returns>The key.</returns>
    private static IKey GetOrCreateKey()
    {
        var store = KeyStore.GetInstance(KEY_STORE)!;
        store.Load(null);
        if (store.GetKey(KEY_ALIAS, null) is { } existing)
        {
            return existing;
        }

        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KEY_STORE)!;
        generator.Init(new KeyGenParameterSpec.Builder(KEY_ALIAS, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            .SetKeySize(KEY_BITS)!
            .Build());

        return generator.GenerateKey()!;
    }
}
