using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Golether.Components.GStreamer;

/// <summary>
/// <see cref="IExistingGStreamerFinder"/> reading the registry and the environment variables the official Windows
/// installer writes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGStreamerFinder : IExistingGStreamerFinder
{
    /// <summary>
    /// The Inno Setup application identifier of the official installers.
    /// </summary>
    public const string InstallerAppId = "c20a66dc-b249-4e6d-a68a-d0f836b2b3cf_is1";

    /// <inheritdoc />
    public IReadOnlyList<ExistingGStreamer> Find(string runtimeIdentifier)
    {
        var (registryArch, variable) = runtimeIdentifier switch
        {
            "win-arm64" => ("arm64", "GSTREAMER_1_0_ROOT_MSVC_ARM64"),
            "win-x86" => ("x86", "GSTREAMER_1_0_ROOT_MSVC_X86"),
            _ => ("x86_64", "GSTREAMER_1_0_ROOT_MSVC_X86_64"),
        };

        var candidates = new List<(string Root, string? Version)>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"Software\GStreamer1.0\{registryArch}");
            if (key?.GetValue("InstallDir") is string dir)
            {
                candidates.Add((dir, key.GetValue("Version") as string));
            }
        }

        foreach (var target in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.Process })
        {
            if (Environment.GetEnvironmentVariable(variable, target) is { Length: > 0 } dir)
            {
                candidates.Add((dir, null));
            }
        }

        return candidates
            .Where(c => Directory.Exists(c.Root))
            .Select(c => (Root: Path.GetFullPath(c.Root).TrimEnd('\\'), c.Version))
            .DistinctBy(c => c.Root, StringComparer.OrdinalIgnoreCase)
            .Select(c => new ExistingGStreamer(
                c.Root,
                System.Version.TryParse(c.Version, out var version) ? version : null,
                GStreamerBundle.IsComplete(c.Root)))
            .OrderByDescending(c => c.IsComplete && (c.Version is null || c.Version >= GStreamerBundle.MinimumVersion))
            .ThenByDescending(c => c.Version)
            .ToArray();
    }

    /// <inheritdoc />
    public bool HasInstallerRegistration()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerAppId}");
            if (key is not null)
            {
                return true;
            }
        }

        return false;
    }
}
