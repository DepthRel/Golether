using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Golether.Web.Server.Enums;
using Golether.Web.Server.Infrastructure;
using Golether.Web.Server.Options;

namespace Golether.Web.Server.Tests;

/// <summary>
/// Tests of <see cref="DefaultSettings"/>: a server without its settings file still starts with the usual settings,
/// and the file is written again for the administrator.
/// </summary>
public sealed class DefaultSettingsTests : IDisposable
{
    /// <summary>
    /// A folder of the test.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "golether-defaults-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// The built-in defaults are the settings the packages carry: valid, with the status page on this computer.
    /// </summary>
    [Fact]
    public void Defaults_AreTheSettingsOfThePackages()
    {
        using var stream = DefaultSettings.Open();
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        Assert.Equal("http://127.0.0.1:47980", configuration["Urls"]);
        var options = configuration.GetSection(RendezvousServerOptions.SECTION).Get<RendezvousServerOptions>()!;
        Assert.Empty(options.Validate());
        Assert.Equal(47900, options.Port);
        Assert.Equal(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, DefaultSettings.FILE_NAME)).Trim(),
            new StreamReader(DefaultSettings.Open()).ReadToEnd().Trim());
    }

    /// <summary>
    /// A missing file is written with the defaults, the folder included; an existing file is never touched.
    /// </summary>
    [Fact]
    public void MissingFile_IsWrittenAndAnExistingOneKept()
    {
        var config = Path.Combine(_root, "config");

        Assert.Equal(SettingsFileState.Created, DefaultSettings.EnsureFile(config, out var problem));
        Assert.Null(problem);
        var file = Path.Combine(config, DefaultSettings.FILE_NAME);
        using (JsonDocument.Parse(File.ReadAllText(file)))
        {
        }

        File.WriteAllText(file, """{ "Rendezvous": { "Port": 48000 } }""");
        Assert.Equal(SettingsFileState.Existed, DefaultSettings.EnsureFile(config, out _));
        Assert.Contains("48000", File.ReadAllText(file), StringComparison.Ordinal);
    }

    /// <summary>
    /// A folder that cannot be written does not stop the server: it is told to run with the defaults.
    /// </summary>
    [Fact]
    public void UnwritableFolder_LeavesTheDefaults()
    {
        // A file where the folder should be: nothing can be created inside it, on every system.
        Directory.CreateDirectory(_root);
        var blocked = Path.Combine(_root, "config");
        File.WriteAllText(blocked, "not a folder");

        Assert.Equal(SettingsFileState.Missing, DefaultSettings.EnsureFile(blocked, out var problem));
        Assert.NotNull(problem);
    }

    /// <summary>
    /// In a package whose config folder was deleted the settings still belong in config: the launcher in the root
    /// shows that the folder is a package.
    /// </summary>
    [Fact]
    public void Package_WithoutConfigFolder_StillUsesConfig()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
        File.WriteAllText(Path.Combine(_root, OperatingSystem.IsWindows() ? "golether-server.exe" : "golether-server"), string.Empty);

        Assert.Equal(Path.Combine(_root, "config"), PackageLayout.FindConfigDirectory(bin));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
