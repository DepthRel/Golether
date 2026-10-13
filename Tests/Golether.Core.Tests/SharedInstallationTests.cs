using Golether.Core.Configuration;

namespace Golether.Core.Tests;

/// <summary>
/// Tests of <see cref="AppDataPaths"/> for an installation shared by every user of the computer.
/// </summary>
public sealed class SharedInstallationTests
{
    /// <summary>
    /// With the shared marker the data goes to the folder of the user: the installation itself (Program Files) is
    /// not written to. Without it the data stays inside the installation.
    /// </summary>
    /// <param name="shared">Whether the installation is shared.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_PutsDataOfASharedInstallationIntoTheProfile(bool shared)
    {
        var install = Directory.CreateTempSubdirectory("golether-install-");
        var profile = Path.Combine(install.FullName, "profile", "Golether");
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(install.FullName, "app"));
            File.WriteAllText(Path.Combine(install.FullName, AppDataPaths.InstallationMarkerFileName), "installation");
            if (shared)
            {
                File.WriteAllText(Path.Combine(install.FullName, AppDataPaths.SharedInstallationMarkerFileName), "shared");
            }

            var paths = AppDataPaths.Resolve(app.FullName, _ => null, () => profile);

            Assert.Equal(shared ? profile : Path.Combine(install.FullName, AppDataPaths.DataFolderName), paths.Root);
        }
        finally
        {
            install.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The environment variable still wins over a shared installation: development and tests rely on it.
    /// </summary>
    [Fact]
    public void Resolve_PrefersTheEnvironmentVariable()
    {
        var install = Directory.CreateTempSubdirectory("golether-install-");
        try
        {
            File.WriteAllText(Path.Combine(install.FullName, AppDataPaths.InstallationMarkerFileName), "installation");
            File.WriteAllText(Path.Combine(install.FullName, AppDataPaths.SharedInstallationMarkerFileName), "shared");
            var chosen = Path.Combine(install.FullName, "chosen");

            var paths = AppDataPaths.Resolve(install.FullName, name => name == AppDataPaths.DataDirectoryVariable ? chosen : null, () => "unused");

            Assert.Equal(chosen, paths.Root);
        }
        finally
        {
            install.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The folder of the user is where earlier versions kept the data, so a shared installation finds it in place.
    /// </summary>
    [Fact]
    public void UserDirectory_IsTheLegacyOne()
    {
        Assert.Equal(AppDataPaths.GetLegacyUserDirectory(), AppDataPaths.GetUserDirectory());
        Assert.EndsWith("Golether", AppDataPaths.GetUserDirectory(), StringComparison.Ordinal);
    }
}
