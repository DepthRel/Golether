using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Golether.Tunnels.AmneziaWG.Configuration;

namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// Keeps the engine and the configurations of the tunnel services in a folder of their own that only administrators
/// and the system can change; the configurations there are readable by them alone.
/// </summary>
public sealed class ProtectedTunnelFiles : ITunnelEngineGuard
{
    /// <summary>
    /// The largest configuration file that is read; the application writes a few hundred bytes.
    /// </summary>
    private const int MAX_CONFIGURATION_BYTES = 64 * 1024;

    /// <summary>
    /// The folder of the engine inside the protected folder.
    /// </summary>
    private const string ENGINE_DIRECTORY = "engine";

    /// <summary>
    /// The folder of the configurations inside the protected folder.
    /// </summary>
    private const string CONFIGURATION_DIRECTORY = "configurations";

    /// <summary>
    /// The protected folder.
    /// </summary>
    private readonly string _root;

    /// <summary>
    /// The SHA-256 of every file of the engine by its name, or <see langword="null"/> when no engine of this processor
    /// is known.
    /// </summary>
    private readonly IReadOnlyDictionary<string, string>? _engineHashes;

    /// <summary>
    /// The folders only administrators can write to; an engine there is started as it is.
    /// </summary>
    private readonly IReadOnlyList<string> _trustedRoots;

    /// <summary>
    /// Whether the folders get their own access rules; off only where the files may keep the rights of the folder
    /// they lie in (tests).
    /// </summary>
    private readonly bool _restrictAccess;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtectedTunnelFiles"/> class.
    /// </summary>
    /// <param name="root">The protected folder, in a place only administrators can create folders in.</param>
    /// <param name="engineHashes">The SHA-256 of every file of the engine by its name, or <see langword="null"/>.</param>
    /// <param name="trustedRoots">The folders only administrators can write to.</param>
    /// <param name="restrictAccess">Whether the folders get their own access rules (Windows only).</param>
    public ProtectedTunnelFiles(string root, IReadOnlyDictionary<string, string>? engineHashes, IReadOnlyList<string> trustedRoots, bool restrictAccess = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(trustedRoots);

        _root = Path.GetFullPath(root);
        _engineHashes = engineHashes;
        _trustedRoots = [.. trustedRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(Path.GetFullPath)];
        _restrictAccess = restrictAccess && OperatingSystem.IsWindows();
    }

    /// <summary>
    /// Gets the protected folder of this computer: a folder of its own in Program Files, which only administrators can
    /// create.
    /// </summary>
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Golether Tunnel");

    /// <summary>
    /// Gets the folders of programs installed by an administrator: an engine installed there is trusted as it is.
    /// </summary>
    public static IReadOnlyList<string> DefaultTrustedRoots =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    ];

    /// <inheritdoc />
    public string PrepareEngine(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        var source = Path.GetFullPath(executable);
        if (_trustedRoots.Any(root => IsInside(source, root)))
        {
            return source;
        }

        var name = Path.GetFileName(source);
        if (_engineHashes is null || !_engineHashes.ContainsKey(name))
        {
            throw new TunnelControlException(TunnelHelper.EncodeEngineChanged(name));
        }

        try
        {
            var target = PrepareDirectory(ENGINE_DIRECTORY, readableByUsers: true);
            foreach (var (file, hash) in _engineHashes)
            {
                Stage(Path.Combine(Path.GetDirectoryName(source)!, file), Path.Combine(target, file), hash);
            }

            return Path.Combine(target, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TunnelControlException(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public string PrepareConfiguration(string interfaceName, string configuration)
    {
        TunnelNames.Validate(interfaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        AwgConfiguration parsed;
        try
        {
            var file = new FileInfo(configuration);
            if (!file.Exists || file.Length > MAX_CONFIGURATION_BYTES || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new TunnelControlException(TunnelHelper.EncodeConfigurationRejected());
            }

            // Only what the application knows is written back: a line added by another program (a command to run
            // when the tunnel comes up, for example) does not reach the service.
            parsed = AwgConfiguration.Parse(File.ReadAllText(configuration));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            // The words of the error may quote the file, which the user may not be allowed to read: only the fact goes back.
            throw new TunnelControlException(TunnelHelper.EncodeConfigurationRejected(), ex);
        }

        try
        {
            var target = Path.Combine(PrepareDirectory(CONFIGURATION_DIRECTORY, readableByUsers: false), interfaceName + ".conf");
            File.WriteAllText(target, parsed.Render());
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TunnelControlException(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public void ForgetConfiguration(string interfaceName)
    {
        TunnelNames.Validate(interfaceName);

        try
        {
            var configurations = Path.Combine(_root, CONFIGURATION_DIRECTORY);
            File.Delete(Path.Combine(configurations, interfaceName + ".conf"));

            // With the last tunnel gone nothing needs the engine any more: nothing of Golether stays in Program Files.
            // The next tunnel copies and checks it again.
            if (!Directory.Exists(configurations) || Directory.EnumerateFiles(configurations, "*.conf").Any())
            {
                return;
            }

            foreach (var folder in new[] { Path.Combine(_root, ENGINE_DIRECTORY), configurations })
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }

            if (!Directory.EnumerateFileSystemEntries(_root).Any())
            {
                Directory.Delete(_root);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The service is gone already; what is left behind (an engine still being released) is changed by
            // administrators only and is tidied by the next tunnel that goes down.
        }
    }

    /// <summary>
    /// Builds the access rules of a protected folder: full rights for the system and the administrators only, and
    /// reading for the users when they may start what lies there. Nothing is inherited from the folder above.
    /// </summary>
    /// <param name="readableByUsers">Whether the users may read and start the files.</param>
    /// <returns>The rules.</returns>
    [SupportedOSPlatform("windows")]
    public static DirectorySecurity CreateAccessRules(bool readableByUsers)
    {
        const InheritanceFlags INHERITED = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, INHERITED, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, INHERITED, PropagationFlags.None, AccessControlType.Allow));
        if (readableByUsers)
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, INHERITED, PropagationFlags.None, AccessControlType.Allow));
        }

        return security;
    }

    /// <summary>
    /// Tells whether a path lies inside a folder.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <param name="folder">The full path of the folder.</param>
    /// <returns><see langword="true"/> when the path is in the folder or below it.</returns>
    private static bool IsInside(string path, string folder)
        => path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Computes the SHA-256 of a file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// Puts a file of the engine into the protected folder, unless the right one is there already. The copy is
    /// checked where nobody else can change it, so what is checked is what runs.
    /// </summary>
    /// <param name="source">The file the application found.</param>
    /// <param name="target">Its place in the protected folder.</param>
    /// <param name="hash">Its known SHA-256.</param>
    /// <exception cref="TunnelControlException">The file differs from the known one.</exception>
    private static void Stage(string source, string target, string hash)
    {
        if (File.Exists(target) && string.Equals(HashOf(target), hash, StringComparison.Ordinal))
        {
            return;
        }

        var temporary = target + ".new";
        File.Copy(source, temporary, overwrite: true);
        if (!string.Equals(HashOf(temporary), hash, StringComparison.Ordinal))
        {
            File.Delete(temporary);
            throw new TunnelControlException(TunnelHelper.EncodeEngineChanged(Path.GetFileName(target)));
        }

        File.Move(temporary, target, overwrite: true);
    }

    /// <summary>
    /// Creates a folder inside the protected folder and gives it its access rules.
    /// </summary>
    /// <param name="name">The name of the folder.</param>
    /// <param name="readableByUsers">Whether the users may read and start the files.</param>
    /// <returns>The full path of the folder.</returns>
    /// <exception cref="TunnelControlException">The folder is a link to somewhere else.</exception>
    private string PrepareDirectory(string name, bool readableByUsers)
    {
        foreach (var path in new[] { _root, Path.Combine(_root, name) })
        {
            var directory = new DirectoryInfo(path);
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // A link would carry the files of a service somewhere its owner chose.
                throw new TunnelControlException($"{path} is a link, not a folder.");
            }

            if (!directory.Exists)
            {
                directory.Create();
            }
        }

        var folder = Path.Combine(_root, name);
        if (_restrictAccess && OperatingSystem.IsWindows())
        {
            new DirectoryInfo(folder).SetAccessControl(CreateAccessRules(readableByUsers));
        }

        return folder;
    }
}
