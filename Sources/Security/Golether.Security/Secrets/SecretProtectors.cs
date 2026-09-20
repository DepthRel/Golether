namespace Golether.Security.Secrets;

/// <summary>
/// Chooses the secret protector of the current platform.
/// </summary>
public static class SecretProtectors
{
    /// <summary>
    /// Creates the default protector: DPAPI on Windows, file permissions elsewhere.
    /// </summary>
    /// <returns>The protector.</returns>
    public static ISecretProtector CreateDefault()
        => OperatingSystem.IsWindows() ? new DpapiSecretProtector() : new FilePermissionSecretProtector();

    /// <summary>
    /// Writes protected data to a file readable only by the current user.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="data">The protected data.</param>
    public static void WriteOwnerOnlyFile(string path, ReadOnlySpan<byte> data)
    {
        var temp = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temp, options))
        {
            stream.Write(data);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }
}
