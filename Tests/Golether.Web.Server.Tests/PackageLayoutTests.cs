namespace Golether.Web.Server.Tests;

/// <summary>
/// Tests of <see cref="PackageLayout"/>: where the server looks for its settings.
/// </summary>
public sealed class PackageLayoutTests : IDisposable
{
    /// <summary>
    /// A temporary folder standing for the root of a package.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "golether-layout-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// A program in the bin folder of a package reads the settings from config next to it.
    /// </summary>
    [Fact]
    public void Package_UsesConfigNextToBin()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
        var config = Directory.CreateDirectory(Path.Combine(_root, "config")).FullName;

        Assert.Equal(config, PackageLayout.FindConfigDirectory(bin));
        Assert.Equal(config, PackageLayout.FindConfigDirectory(bin + Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Without a config folder, or outside a bin folder (a build from the sources), the settings stay next to the
    /// program.
    /// </summary>
    [Fact]
    public void Build_UsesProgramFolder()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
        Assert.Equal(bin, PackageLayout.FindConfigDirectory(bin));

        Directory.CreateDirectory(Path.Combine(_root, "config"));
        var build = Directory.CreateDirectory(Path.Combine(_root, "net10.0")).FullName;
        Assert.Equal(build, PackageLayout.FindConfigDirectory(build));
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
