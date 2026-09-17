using System.Net;
using System.Security.Cryptography;
using Golether.Components.Catalog;
using Golether.Components.GStreamer;
using Golether.Components.Installation;
using Golether.Components.Pe;
using Microsoft.Extensions.Time.Testing;

namespace Golether.Components.Tests;

/// <summary>
/// Tests of the component catalog, installation hints, PE imports, the installer and the locator.
/// </summary>
public sealed class ComponentTests : IDisposable
{
    /// <summary>
    /// The temporary working directory.
    /// </summary>
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("golether-components-");

    /// <inheritdoc />
    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>
    /// Every catalog entry is pinned: HTTPS, SHA-256 and size.
    /// </summary>
    [Fact]
    public void Catalog_EntriesArePinned()
    {
        foreach (var package in ComponentCatalog.Packages)
        {
            Assert.Equal(Uri.UriSchemeHttps, package.Url.Scheme);
            Assert.Matches("^[0-9a-f]{64}$", package.Sha256);
            Assert.True(package.Size > 1_000_000);
        }

        Assert.NotNull(ComponentCatalog.Find(ComponentId.Video, "win-x64"));
        Assert.NotNull(ComponentCatalog.Find(ComponentId.Conference, "WIN-ARM64"));
        Assert.Null(ComponentCatalog.Find(ComponentId.Video, "linux-x64"));
        Assert.Matches("^(win|linux|osx)-", ComponentCatalog.CurrentRuntimeIdentifier);
    }

    /// <summary>
    /// Linux distributions are recognised through ID and ID_LIKE.
    /// </summary>
    /// <param name="osRelease">The os-release content.</param>
    /// <param name="expected">The expected family.</param>
    [Theory]
    [InlineData("NAME=\"Ubuntu\"\nID=ubuntu\nID_LIKE=debian", "debian")]
    [InlineData("ID=linuxmint\nID_LIKE=\"ubuntu debian\"", "debian")]
    [InlineData("ID=\"fedora\"", "fedora")]
    [InlineData("ID=rocky\nID_LIKE=\"rhel centos fedora\"", "fedora")]
    [InlineData("ID=manjaro\nID_LIKE=arch", "arch")]
    [InlineData("ID=\"opensuse-tumbleweed\"\nID_LIKE=\"opensuse suse\"", "suse")]
    [InlineData("ID=nixos", null)]
    [InlineData("", null)]
    public void Advice_DetectsDistribution(string osRelease, string? expected)
        => Assert.Equal(expected, InstallAdvice.DetectDistribution(osRelease));

    /// <summary>
    /// The hints contain the right package manager commands.
    /// </summary>
    [Fact]
    public void Advice_GivesCommands()
    {
        Assert.Equal("sudo apt install libmpv2", InstallAdvice.For(ComponentId.Video, OsFamily.Linux, "ID=debian").Command);
        Assert.Contains("gstreamer1.0-nice", InstallAdvice.For(ComponentId.Conference, OsFamily.Linux, "ID=ubuntu").Command, StringComparison.Ordinal);
        Assert.Equal("brew install gstreamer", InstallAdvice.For(ComponentId.Conference, OsFamily.MacOS, null).Command);
        Assert.Null(InstallAdvice.For(ComponentId.Video, OsFamily.Linux, "ID=nixos").Command);
    }

    /// <summary>
    /// The imports of a real Windows executable include the Windows kernel.
    /// </summary>
    [Fact]
    public void PeImports_ReadsRealExecutable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs a Windows executable.");

        var imports = PeImports.Read(Environment.ProcessPath!);

        Assert.Contains(imports, name => name.Equals("KERNEL32.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Throws<BadImageFormatException>(() => PeImports.Read(new MemoryStream(new byte[512])));
    }

    /// <summary>
    /// A verified download is unpacked, marked and found by the locator; a second installation uses the cache.
    /// </summary>
    [Fact]
    public async Task Installer_InstallsVerifiedPackage()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Automatic installation is Windows only.");
        var content = RandomNumberGenerator.GetBytes(4096);
        var handler = new StaticHandler(content);
        var package = Package(content, PackageFormat.SevenZip);
        var tools = new FakeTools();
        var installer = CreateInstaller(handler, tools, new FakeGStreamerFinder());

        var target = await installer.InstallAsync(package, null, TestContext.Current.CancellationToken);
        await installer.InstallAsync(package, null, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(target, "libmpv-2.dll")));
        Assert.True(File.Exists(Path.Combine(target, ComponentMarker.FileName)));
        Assert.Equal(1, handler.Requests);
        Assert.Contains(tools.Calls, call => call.EndsWith("tar.exe", StringComparison.OrdinalIgnoreCase));

        var locator = new ComponentLocator(Path.Combine(_directory.FullName, "no-bundle"), installer.Root, _ => false);
        var status = locator.GetStatus(ComponentId.Video);
        Assert.Equal(ComponentSource.Installed, status.Source);
        Assert.Equal(Path.Combine(target, "libmpv-2.dll"), status.Path);
    }

    /// <summary>
    /// A download with a wrong checksum or size is rejected and nothing is installed.
    /// </summary>
    [Fact]
    public async Task Installer_RejectsTamperedDownloads()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Automatic installation is Windows only.");
        var content = RandomNumberGenerator.GetBytes(4096);
        var installer = CreateInstaller(new StaticHandler(content), new FakeTools(), new FakeGStreamerFinder());

        var wrongHash = Package(content, PackageFormat.SevenZip) with { Sha256 = new string('0', 64) };
        var tooSmall = Package(content, PackageFormat.SevenZip) with { Size = 100 };

        var hashError = await Assert.ThrowsAsync<ComponentInstallException>(() => installer.InstallAsync(wrongHash, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ComponentInstallException>(() => installer.InstallAsync(tooSmall, null, TestContext.Current.CancellationToken));

        Assert.Contains("контрольная сумма", hashError.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(installer.GetInstallDirectory(wrongHash)));
        Assert.Empty(Directory.GetFiles(Path.Combine(installer.Root, "downloads")));
    }

    /// <summary>
    /// A complete GStreamer on the computer is reused without downloading; the bundle contains the dependency closure.
    /// </summary>
    [Fact]
    public async Task Installer_ReusesExistingGStreamer()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows executables.");
        var existing = CreateFakeGStreamer();
        var handler = new StaticHandler([]);
        var installer = CreateInstaller(handler, new FakeTools(), new FakeGStreamerFinder(new ExistingGStreamer(existing, new Version(1, 26), true)));

        var target = await installer.InstallAsync(Package([1], PackageFormat.InnoSetup), null, TestContext.Current.CancellationToken);

        var bundle = Path.Combine(target, "gstreamer");
        Assert.Equal(0, handler.Requests);
        Assert.True(GStreamerBundle.IsComplete(bundle));
        Assert.True(File.Exists(Path.Combine(bundle, "bin", "KERNEL32.dll")), "Imported libraries that ship with GStreamer are copied.");
        Assert.False(File.Exists(Path.Combine(bundle, "bin", "unused.dll")), "Libraries nobody imports are skipped.");
        Assert.Equal(ComponentSource.Installed, new ComponentLocator(_directory.FullName, installer.Root, _ => false).GetStatus(ComponentId.Conference).Source);
    }

    /// <summary>
    /// An incomplete registered GStreamer is not taken over by the silent installer.
    /// </summary>
    [Fact]
    public async Task Installer_RefusesToOverwriteRegisteredGStreamer()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Automatic installation is Windows only.");
        var handler = new StaticHandler([]);
        var finder = new FakeGStreamerFinder(new ExistingGStreamer(_directory.FullName, new Version(1, 20), false)) { Registered = true };
        var installer = CreateInstaller(handler, new FakeTools(), finder);

        var error = await Assert.ThrowsAsync<ComponentInstallException>(() =>
            installer.InstallAsync(Package([1], PackageFormat.InnoSetup), null, TestContext.Current.CancellationToken));

        Assert.Contains("уже установлен GStreamer", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Requests);
    }

    /// <summary>
    /// Bundled components win over installed ones; a damaged marker is ignored.
    /// </summary>
    [Fact]
    public void Locator_PrefersBundledAndIgnoresDamagedMarkers()
    {
        var bundled = Directory.CreateDirectory(Path.Combine(_directory.FullName, "native")).FullName;
        var installed = Directory.CreateDirectory(Path.Combine(_directory.FullName, "components", "video", "1")).FullName;
        File.WriteAllText(Path.Combine(installed, ComponentLocator.VideoLibraryNames[0]), "x");
        File.WriteAllText(Path.Combine(installed, ComponentMarker.FileName), "{ broken");
        var locator = new ComponentLocator(bundled, Path.Combine(_directory.FullName, "components"), _ => false, () => "ID=debian");

        var missing = locator.GetStatus(ComponentId.Video);
        File.WriteAllText(Path.Combine(bundled, ComponentLocator.VideoLibraryNames[0]), "x");
        var found = locator.GetStatus(ComponentId.Video);

        Assert.False(missing.IsAvailable);
        Assert.Equal(ComponentSource.Bundled, found.Source);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(missing.CanInstall);
        }
        else
        {
            Assert.NotNull(missing.Advice);
        }
    }

    /// <summary>
    /// Creates a package for the given content.
    /// </summary>
    /// <param name="content">The download content.</param>
    /// <param name="format">The format.</param>
    /// <returns>The package.</returns>
    private static ComponentPackage Package(byte[] content, PackageFormat format)
        => new(format == PackageFormat.SevenZip ? ComponentId.Video : ComponentId.Conference, "test", "win-x64",
            new Uri("https://example.org/package.bin"), Convert.ToHexStringLower(SHA256.HashData(content)), content.Length, format);

    /// <summary>
    /// Creates an installer over the fakes.
    /// </summary>
    /// <param name="handler">The HTTP handler.</param>
    /// <param name="tools">The tool runner.</param>
    /// <param name="finder">The GStreamer finder.</param>
    /// <returns>The installer.</returns>
    private ComponentInstaller CreateInstaller(HttpMessageHandler handler, IToolRunner tools, IExistingGStreamerFinder finder)
        => new(Path.Combine(_directory.FullName, "components"), new HttpClient(handler), tools, new FakeTimeProvider(), existingGStreamer: finder);

    /// <summary>
    /// Builds a fake GStreamer installation out of copies of a real executable.
    /// </summary>
    /// <returns>The installation root.</returns>
    private string CreateFakeGStreamer()
    {
        var root = Path.Combine(_directory.FullName, "gstreamer-existing");
        var executable = Environment.ProcessPath!;
        void Put(string relative) => Copy(executable, Path.Combine(root, relative));
        foreach (var plugin in GStreamerBundle.Plugins)
        {
            Put(Path.Combine(GStreamerBundle.PluginDirectory, plugin + ".dll"));
        }

        foreach (var library in GStreamerBundle.CoreLibraries)
        {
            Put(Path.Combine("bin", library));
        }

        Put(GStreamerBundle.ScannerPath);
        Put(Path.Combine("bin", "KERNEL32.dll"));
        Put(Path.Combine("bin", "unused.dll"));
        return root;
    }

    /// <summary>
    /// Copies a file, creating the directory.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="target">The target.</param>
    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }

    /// <summary>
    /// Serves fixed content and counts requests.
    /// </summary>
    /// <param name="content">The content.</param>
    private sealed class StaticHandler(byte[] content) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the number of requests.
        /// </summary>
        public int Requests { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }

    /// <summary>
    /// Pretends to extract archives by creating the expected file.
    /// </summary>
    private sealed class FakeTools : IToolRunner
    {
        /// <summary>
        /// Gets the programs that were run.
        /// </summary>
        public List<string> Calls { get; } = [];

        /// <inheritdoc />
        public Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Calls.Add(fileName);
            var output = arguments[arguments.ToList().IndexOf("-C") + 1];
            File.WriteAllText(Path.Combine(output, arguments[^1]), "library");
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Returns preset GStreamer installations.
    /// </summary>
    /// <param name="installations">The installations.</param>
    private sealed class FakeGStreamerFinder(params ExistingGStreamer[] installations) : IExistingGStreamerFinder
    {
        /// <summary>
        /// Gets or sets a value indicating whether an installer registration exists.
        /// </summary>
        public bool Registered { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<ExistingGStreamer> Find(string runtimeIdentifier) => installations;

        /// <inheritdoc />
        public bool HasInstallerRegistration() => Registered;
    }
}
