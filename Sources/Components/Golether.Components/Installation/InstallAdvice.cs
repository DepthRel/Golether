using Golether.Components.Catalog;
using Golether.Core.Data.Enums;

namespace Golether.Components.Installation;

/// <summary>
/// How to install a component where Golether cannot do it itself: one command to copy into a terminal.
/// </summary>
/// <param name="Text">The explanation.</param>
/// <param name="Command">The command, or <see langword="null"/> when no package manager was recognised.</param>
public sealed record InstallAdvice(string Text, string? Command)
{
    /// <summary>
    /// Builds the advice for a component.
    /// </summary>
    /// <param name="id">The component.</param>
    /// <param name="os">The operating system.</param>
    /// <param name="osRelease">The content of <c>/etc/os-release</c> on Linux.</param>
    /// <returns>The advice.</returns>
    public static InstallAdvice For(ComponentId id, OsFamily os, string? osRelease)
    {
        var title = ComponentCatalog.Describe(id).Title;
        if (os == OsFamily.MacOS)
        {
            return new($"{title} ставится через Homebrew (https://brew.sh). Выполните в Терминале и перезапустите Golether:",
                id == ComponentId.Video ? "brew install mpv" : "brew install gstreamer");
        }

        if (os == OsFamily.Windows)
        {
            return new($"{title} для этой архитектуры Windows не устанавливается автоматически.", null);
        }

        var command = DetectDistribution(osRelease) switch
        {
            "debian" => id == ComponentId.Video
                ? "sudo apt install libmpv2"
                : "sudo apt install gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-plugins-bad gstreamer1.0-nice gstreamer1.0-pulseaudio",
            "fedora" => id == ComponentId.Video
                ? "sudo dnf install mpv-libs"
                : "sudo dnf install gstreamer1-plugins-base gstreamer1-plugins-good gstreamer1-plugins-bad-free libnice-gstreamer1",
            "arch" => id == ComponentId.Video
                ? "sudo pacman -S mpv"
                : "sudo pacman -S gst-plugins-base gst-plugins-good gst-plugins-bad libnice",
            "suse" => id == ComponentId.Video
                ? "sudo zypper install libmpv2"
                : "sudo zypper install gstreamer-plugins-base gstreamer-plugins-good gstreamer-plugins-bad libnice-gstreamer",
            _ => null,
        };

        return command is null
            ? new($"{title} ставится пакетным менеджером вашего дистрибутива (пакет libmpv или gstreamer с плагинами base, good, bad и nice).", null)
            : new($"{title} ставится из пакетов дистрибутива. Выполните в терминале и перезапустите Golether:", command);
    }

    /// <summary>
    /// Maps <c>/etc/os-release</c> to a distribution family.
    /// </summary>
    /// <param name="osRelease">The file content.</param>
    /// <returns><c>debian</c>, <c>fedora</c>, <c>arch</c>, <c>suse</c> or <see langword="null"/>.</returns>
    public static string? DetectDistribution(string? osRelease)
    {
        if (string.IsNullOrWhiteSpace(osRelease))
        {
            return null;
        }

        var ids = osRelease.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("ID=", StringComparison.Ordinal) || line.StartsWith("ID_LIKE=", StringComparison.Ordinal))
            .SelectMany(line => line[(line.IndexOf('=') + 1)..].Trim('"', '\'').Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(id => id.ToLowerInvariant())
            .ToArray();

        foreach (var id in ids)
        {
            switch (id)
            {
                case "debian" or "ubuntu" or "linuxmint" or "pop" or "elementary" or "astra":
                    return "debian";
                case "fedora" or "rhel" or "centos" or "rocky" or "almalinux" or "redos":
                    return "fedora";
                case "arch" or "manjaro" or "endeavouros":
                    return "arch";
                case "suse" or "opensuse" or "opensuse-leap" or "opensuse-tumbleweed":
                    return "suse";
            }
        }

        return null;
    }
}
