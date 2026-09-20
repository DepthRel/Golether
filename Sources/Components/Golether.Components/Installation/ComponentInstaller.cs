using System.Security.Cryptography;
using System.Text.Json;
using Golether.Components.Catalog;
using Golether.Components.GStreamer;
using Golether.Core.Data.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Golether.Components.Installation;

/// <summary>
/// Downloads, verifies and installs components into a directory of the current user, without administrator rights.
/// </summary>
/// <remarks>
/// Layout: <c>&lt;root&gt;/&lt;component&gt;/&lt;version&gt;/</c>; downloads are cached in <c>&lt;root&gt;/downloads</c>.
/// A component directory appears only when the installation succeeded (staging directory + rename).
/// </remarks>
public sealed class ComponentInstaller
{
    /// <summary>
    /// The JSON options of markers.
    /// </summary>
    internal static readonly JsonSerializerOptions MarkerJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// The installation root.
    /// </summary>
    private readonly string _root;

    /// <summary>
    /// The HTTP client.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Runs external tools.
    /// </summary>
    private readonly IToolRunner _tools;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Finds GStreamer installations already on the computer.
    /// </summary>
    private readonly IExistingGStreamerFinder? _existingGStreamer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentInstaller"/> class.
    /// </summary>
    /// <param name="root">The installation root.</param>
    /// <param name="http">The HTTP client (no timeout: large downloads).</param>
    /// <param name="tools">Runs external tools.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="existingGStreamer">Finds existing GStreamer installations; the registry finder on Windows when
    /// <see langword="null"/>.</param>
    public ComponentInstaller(
        string root,
        HttpClient http,
        IToolRunner tools,
        TimeProvider timeProvider,
        ILogger<ComponentInstaller>? logger = null,
        IExistingGStreamerFinder? existingGStreamer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? NullLogger<ComponentInstaller>.Instance;
        _existingGStreamer = existingGStreamer ?? (OperatingSystem.IsWindows() ? new WindowsGStreamerFinder() : null);
    }

    /// <summary>
    /// Gets the installation root.
    /// </summary>
    public string Root => _root;

    /// <summary>
    /// Returns the directory a package is installed to.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>The directory.</returns>
    public string GetInstallDirectory(ComponentPackage package)
        => Path.Combine(_root, package.Id.ToString().ToLowerInvariant(), package.Version);

    /// <summary>
    /// Downloads and installs a package; an existing installation of the same version is replaced.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="progress">Receives progress, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The installation directory.</returns>
    /// <exception cref="ComponentInstallException">The download, the check or the installation failed.</exception>
    public async Task<string> InstallAsync(ComponentPackage package, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!OperatingSystem.IsWindows())
        {
            throw new ComponentInstallException("Автоматическая установка компонентов доступна только в Windows.");
        }

        // A complete GStreamer already on the computer is reused: nothing to download, and the installer registration
        // of the user is left alone.
        ExistingGStreamer? existing = null;
        if (package.Format == PackageFormat.InnoSetup && _existingGStreamer is not null)
        {
            existing = _existingGStreamer.Find(package.RuntimeIdentifier)
                .FirstOrDefault(e => e.IsComplete && (e.Version is null || e.Version >= GStreamerBundle.MinimumVersion));
            if (existing is null && _existingGStreamer.HasInstallerRegistration())
            {
                throw new ComponentInstallException(
                    "На компьютере уже установлен GStreamer, но в нём не хватает нужных частей. Установите полный GStreamer 1.24 или новее " +
                    "с gstreamer.freedesktop.org (вариант «Complete») либо удалите старую версию и повторите.");
            }
        }

        var file = existing is null ? await DownloadAsync(package, progress, cancellationToken).ConfigureAwait(false) : null;
        var target = GetInstallDirectory(package);
        var staging = Path.Combine(_root, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report(new InstallProgress(InstallStage.Unpacking, package.Size, package.Size));
            if (existing is not null)
            {
                _logger.LogInformation("Using the GStreamer installation in {Root}", existing.Root);
                GStreamerBundle.Copy(existing.Root, Path.Combine(staging, "gstreamer"));
            }
            else if (package.Format == PackageFormat.SevenZip)
            {
                await ExtractLibMpvAsync(file!, staging, cancellationToken).ConfigureAwait(false);
            }
            else if (package.Format == PackageFormat.WindowsInstaller)
            {
                await ExtractAmneziaWgAsync(file!, staging, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await InstallGStreamerAsync(file!, staging, progress, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new InstallProgress(InstallStage.Finishing, package.Size, package.Size));
            var marker = new ComponentMarker(package.Id, package.Version, package.RuntimeIdentifier, package.Sha256, _timeProvider.GetUtcNow());
            await File.WriteAllBytesAsync(Path.Combine(staging, ComponentMarker.FileName), JsonSerializer.SerializeToUtf8Bytes(marker, MarkerJson), cancellationToken)
                .ConfigureAwait(false);

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(staging, target);
            progress?.Report(new InstallProgress(InstallStage.Completed, package.Size, package.Size));
            _logger.LogInformation("Installed {Component} {Version} to {Target}", package.Id, package.Version, target);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new ComponentInstallException($"Не удалось установить {ComponentCatalog.Describe(package.Id).Title}: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>
    /// Returns a verified package file from the cache or downloads it.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="progress">Receives progress.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The verified file.</returns>
    /// <exception cref="ComponentInstallException">The download failed or the file does not match.</exception>
    public async Task<string> DownloadAsync(ComponentPackage package, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        var downloads = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(downloads);
        var file = Path.Combine(downloads, package.FileName);
        if (File.Exists(file))
        {
            progress?.Report(new InstallProgress(InstallStage.Verifying, package.Size, package.Size));
            if (await HashFileAsync(file, cancellationToken).ConfigureAwait(false) == package.Sha256)
            {
                return file;
            }

            File.Delete(file);
        }

        if (package.Url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ComponentInstallException("Компоненты скачиваются только по HTTPS.");
        }

        var partial = file + ".partial";
        try
        {
            using var response = await _http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > package.Size)
                    {
                        throw new ComponentInstallException("Скачанный файл больше ожидаемого: загрузка остановлена.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(new InstallProgress(InstallStage.Downloading, total, package.Size));
                }

                progress?.Report(new InstallProgress(InstallStage.Verifying, total, package.Size));
                var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
                if (total != package.Size || actual != package.Sha256)
                {
                    throw new ComponentInstallException("Скачанный файл повреждён или подменён (контрольная сумма не совпала). Попробуйте ещё раз.");
                }
            }

            File.Move(partial, file, overwrite: true);
            return file;
        }
        catch (HttpRequestException ex)
        {
            throw new ComponentInstallException($"Не удалось скачать {package.FileName}: {ex.Message}. Проверьте подключение к интернету.", ex);
        }
        catch (IOException ex)
        {
            throw new ComponentInstallException($"Не удалось сохранить {package.FileName}: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    /// <summary>
    /// Computes the SHA-256 of a file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    internal static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Deletes a file or directory, ignoring failures.
    /// </summary>
    /// <param name="path">The path.</param>
    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file does no harm.
        }
    }

    /// <summary>
    /// Extracts <c>libmpv-2.dll</c> from a 7-Zip archive with the tar tool of Windows (libarchive).
    /// </summary>
    /// <param name="archive">The archive.</param>
    /// <param name="staging">The staging directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the library is extracted.</returns>
    private async Task ExtractLibMpvAsync(string archive, string staging, CancellationToken cancellationToken)
    {
        var tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
        if (!File.Exists(tar))
        {
            throw new InvalidOperationException("В системе нет tar.exe (Windows 10 1803 или новее).");
        }

        var code = await _tools.RunAsync(tar, ["-x", "-f", archive, "-C", staging, "libmpv-2.dll"], cancellationToken).ConfigureAwait(false);
        if (code != 0 || !File.Exists(Path.Combine(staging, "libmpv-2.dll")))
        {
            throw new InvalidOperationException($"tar.exe не смог распаковать архив (код {code}).");
        }
    }

    /// <summary>
    /// Takes the files of AmneziaWG out of its installer with <c>msiexec /a</c> (an administrative install). Nothing
    /// is registered in the system, no administrator rights are needed, and an AmneziaWG the user installed
    /// themselves is not touched: this copy lives in the data folder of Golether and is used only by it.
    /// </summary>
    /// <param name="installer">The <c>.msi</c> file.</param>
    /// <param name="staging">The staging directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the files are in place.</returns>
    private async Task ExtractAmneziaWgAsync(string installer, string staging, CancellationToken cancellationToken)
    {
        var msiexec = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");
        if (!File.Exists(msiexec))
        {
            throw new InvalidOperationException("В системе нет msiexec.exe.");
        }

        var unpacked = Path.Combine(staging, ".msi");
        Directory.CreateDirectory(unpacked);
        var code = await _tools.RunAsync(msiexec, ["/a", installer, "/qn", "TARGETDIR=" + unpacked], cancellationToken).ConfigureAwait(false);
        if (code != 0)
        {
            throw new InvalidOperationException($"msiexec не смог распаковать пакет (код {code}).");
        }

        // The package puts the files into an AmneziaWG folder and leaves a copy of the .msi beside it; only the
        // three files are kept.
        var target = Path.Combine(staging, AmneziaWgBundle.DirectoryName);
        Directory.CreateDirectory(target);
        foreach (var name in AmneziaWgBundle.Files)
        {
            var source = Directory.EnumerateFiles(unpacked, name, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException($"В пакете AmneziaWG нет файла {name}.");
            File.Copy(source, Path.Combine(target, name), overwrite: true);
        }

        Directory.Delete(unpacked, recursive: true);
        _logger.LogInformation("AmneziaWG unpacked to {Target}", target);
    }

    /// <summary>
    /// Installs GStreamer silently for the current user into a temporary directory, copies the needed runtime and
    /// removes the temporary installation again.
    /// </summary>
    /// <param name="installer">The installer.</param>
    /// <param name="staging">The staging directory.</param>
    /// <param name="progress">Receives progress.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the runtime is copied.</returns>
    private async Task InstallGStreamerAsync(string installer, string staging, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(_root, ".gst-setup-" + Guid.NewGuid().ToString("N"));
        var hadRegistryKey = OperatingSystem.IsWindows() && GStreamerRegistryKeyExists();
        try
        {
            var code = await _tools.RunAsync(
                installer,
                ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CURRENTUSER", "/NOICONS", "/DIR=" + temporary],
                cancellationToken).ConfigureAwait(false);
            if (code != 0 || !GStreamerBundle.IsComplete(temporary))
            {
                throw new InvalidOperationException($"Установщик GStreamer завершился с кодом {code}.");
            }

            progress?.Report(new InstallProgress(InstallStage.Finishing, 0, 0));
            var bytes = GStreamerBundle.Copy(temporary, Path.Combine(staging, "gstreamer"));
            _logger.LogInformation("GStreamer runtime reduced to {Megabytes} MB", bytes / (1024 * 1024));
        }
        finally
        {
            // The installer registers itself for the current user; its uninstaller removes that entry and the files.
            var uninstaller = Path.Combine(temporary, "unins000.exe");
            if (File.Exists(uninstaller))
            {
                try
                {
                    await _tools.RunAsync(uninstaller, ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"], CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    _logger.LogWarning(ex, "The temporary GStreamer installation could not be uninstalled");
                }
            }

            TryDelete(temporary);
            if (OperatingSystem.IsWindows() && !hadRegistryKey)
            {
                RemoveEmptyGStreamerRegistryKey();
            }
        }
    }

    /// <summary>
    /// Determines whether the per-user GStreamer registry key exists.
    /// </summary>
    /// <returns><see langword="true"/> when it exists.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool GStreamerRegistryKeyExists()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\GStreamer1.0");
        return key is not null;
    }

    /// <summary>
    /// Removes the per-user GStreamer registry key the uninstaller leaves behind, if it is empty.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void RemoveEmptyGStreamerRegistryKey()
    {
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\GStreamer1.0"))
            {
                if (key is null || key.SubKeyCount > 0 || key.ValueCount > 0)
                {
                    return;
                }
            }

            Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(@"Software\GStreamer1.0", throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _logger.LogDebug(ex, "The empty GStreamer registry key could not be removed");
        }
    }
}
