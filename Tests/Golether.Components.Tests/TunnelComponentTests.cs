using System.Net;
using System.Security.Cryptography;
using Golether.Components.Catalog;
using Golether.Components.Installation;
using Microsoft.Extensions.Time.Testing;

namespace Golether.Components.Tests;

/// <summary>
/// Tests of the AmneziaWG component: the application carries its own tunnel engine, so a session network needs
/// nothing installed beforehand and the AmneziaWG of the user is left alone.
/// </summary>
public sealed class TunnelComponentTests : IDisposable
{
    /// <summary>
    /// The name of the environment variable pointing at a folder with the real <c>.msi</c> packages. When it is set,
    /// the unpacking is checked against the real thing instead of a stand-in.
    /// </summary>
    private const string RealPackagesVariable = "GOLETHER_TEST_AWG_PACKAGES";

    /// <summary>
    /// The temporary working directory.
    /// </summary>
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("golether-awg-");

    /// <inheritdoc />
    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>
    /// The catalog carries the tunnel engine for both Windows architectures, pinned like every other component.
    /// </summary>
    [Fact]
    public void Catalog_CarriesTheEngine()
    {
        var packages = ComponentCatalog.Packages.Where(p => p.Id == ComponentId.Tunnel).ToArray();
        Assert.Equal(2, packages.Length);
        Assert.All(packages, package =>
        {
            Assert.Equal(PackageFormat.WindowsInstaller, package.Format);
            Assert.Equal(Uri.UriSchemeHttps, package.Url.Scheme);
            Assert.Equal(64, package.Sha256.Length);
            Assert.True(package.Size > 0);
            Assert.EndsWith(".msi", package.FileName, StringComparison.OrdinalIgnoreCase);
        });

        Assert.NotNull(ComponentCatalog.Find(ComponentId.Tunnel, "win-x64"));
        Assert.NotNull(ComponentCatalog.Find(ComponentId.Tunnel, "win-arm64"));
        Assert.Null(ComponentCatalog.Find(ComponentId.Tunnel, "linux-x64"));
        Assert.Contains("AmneziaWG", ComponentCatalog.Describe(ComponentId.Tunnel).Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three files of the package are taken out of the installer, put together and found afterwards; the copy of
    /// the <c>.msi</c> the administrative install leaves behind is not kept.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Installer_UnpacksTheEngine()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Компоненты устанавливаются только в Windows.");

        var content = RandomNumberGenerator.GetBytes(4096);
        var package = new ComponentPackage(
            ComponentId.Tunnel, "3.1.0", ComponentCatalog.CurrentRuntimeIdentifier,
            new Uri("https://example.invalid/amneziawg.msi"), Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            content.Length, PackageFormat.WindowsInstaller);
        var installer = CreateInstaller(new StaticContent(content), new FakeMsiExec());

        var target = await installer.InstallAsync(package, null, TestContext.Current.CancellationToken);

        var engine = Path.Combine(target, AmneziaWgBundle.DirectoryName);
        Assert.True(AmneziaWgBundle.IsComplete(engine));
        Assert.Equal(Path.Combine(engine, AmneziaWgBundle.Executable), AmneziaWgBundle.FindExecutable(target));
        Assert.Empty(Directory.GetFiles(engine, "*.msi"));
        Assert.False(Directory.Exists(Path.Combine(target, ".msi")));

        // The locator points the tunnel controller at this copy, not at an AmneziaWG of the system.
        var status = new ComponentLocator(Path.Combine(_directory.FullName, "no-bundle"), installer.Root, _ => false).GetStatus(ComponentId.Tunnel);
        Assert.Equal(ComponentSource.Installed, status.Source);
        Assert.Equal(Path.Combine(engine, AmneziaWgBundle.Executable), status.Path);
    }

    /// <summary>
    /// The same unpacking against the real packages of Amnezia. Runs only when the folder with them is given, so the
    /// suite does not need a download.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Installer_UnpacksTheRealPackage()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Компоненты устанавливаются только в Windows.");
        var folder = Environment.GetEnvironmentVariable(RealPackagesVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(folder), $"Задайте {RealPackagesVariable}, чтобы проверить настоящий пакет.");

        var package = ComponentCatalog.Find(ComponentId.Tunnel) ?? throw new InvalidOperationException("No package.");
        var source = Path.Combine(folder!, package.FileName);
        Assert.SkipWhen(!File.Exists(source), $"Нет файла {source}.");

        // The installer takes a cached file when its hash matches, so nothing is downloaded here.
        var installer = CreateInstaller(new StaticContent([]), new RealTools());
        var downloads = Path.Combine(installer.Root, "downloads");
        Directory.CreateDirectory(downloads);
        File.Copy(source, Path.Combine(downloads, package.FileName), overwrite: true);

        var target = await installer.InstallAsync(package, null, TestContext.Current.CancellationToken);

        var engine = Path.Combine(target, AmneziaWgBundle.DirectoryName);
        Assert.True(AmneziaWgBundle.IsComplete(engine), "В распакованном движке не хватает файлов.");
        Assert.All(AmneziaWgBundle.Files, name => Assert.True(new FileInfo(Path.Combine(engine, name)).Length > 100_000));
    }

    /// <summary>
    /// Without the component nothing is found, and a half-unpacked folder does not count as an engine.
    /// </summary>
    [Fact]
    public void Locator_IgnoresAnIncompleteEngine()
    {
        var bundled = Path.Combine(_directory.FullName, "native", AmneziaWgBundle.DirectoryName);
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, AmneziaWgBundle.Executable), "x");
        var locator = new ComponentLocator(Path.Combine(_directory.FullName, "native"), Path.Combine(_directory.FullName, "components"), _ => false);

        Assert.Equal(ComponentSource.Missing, locator.GetStatus(ComponentId.Tunnel).Source);
        Assert.Null(AmneziaWgBundle.FindExecutable(null));
        Assert.Null(AmneziaWgBundle.FindExecutable(Path.Combine(_directory.FullName, "nowhere")));
        Assert.False(AmneziaWgBundle.IsComplete(Path.Combine(_directory.FullName, "nowhere")));

        foreach (var name in AmneziaWgBundle.Files)
        {
            File.WriteAllText(Path.Combine(bundled, name), "x");
        }

        Assert.Equal(
            OperatingSystem.IsWindows() ? ComponentSource.Bundled : ComponentSource.Missing,
            locator.GetStatus(ComponentId.Tunnel).Source);
    }

    /// <summary>
    /// Creates an installer over the fakes.
    /// </summary>
    /// <param name="handler">The HTTP handler.</param>
    /// <param name="tools">The tool runner.</param>
    /// <returns>The installer.</returns>
    private ComponentInstaller CreateInstaller(HttpMessageHandler handler, IToolRunner tools)
        => new(Path.Combine(_directory.FullName, "components"), new HttpClient(handler), tools, new FakeTimeProvider());

    /// <summary>
    /// Serves fixed content.
    /// </summary>
    /// <param name="content">The content.</param>
    private sealed class StaticContent(byte[] content) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }

    /// <summary>
    /// Pretends to be <c>msiexec /a</c>: lays out the files the way the real package does, in a subfolder and next
    /// to a copy of the installer.
    /// </summary>
    private sealed class FakeMsiExec : IToolRunner
    {
        /// <inheritdoc />
        public Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Assert.EndsWith("msiexec.exe", fileName, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/a", arguments);
            Assert.Contains("/qn", arguments);
            var target = arguments.Single(a => a.StartsWith("TARGETDIR=", StringComparison.Ordinal))["TARGETDIR=".Length..];
            var inner = Path.Combine(target, "AmneziaWG");
            Directory.CreateDirectory(inner);
            foreach (var name in AmneziaWgBundle.Files)
            {
                File.WriteAllText(Path.Combine(inner, name), name);
            }

            File.WriteAllText(Path.Combine(target, "amneziawg-amd64-3.1.0.msi"), "copy of the installer");
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Runs the real tools.
    /// </summary>
    private sealed class RealTools : IToolRunner
    {
        /// <inheritdoc />
        public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            var info = new System.Diagnostics.ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException($"{fileName} did not start.");
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
    }
}
