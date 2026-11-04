using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Tunnels.AmneziaWG.Control;

namespace Golether.Tunnels.AmneziaWG.Tests;

/// <summary>
/// Tests of <see cref="ProtectedTunnelFiles"/>: what a tunnel service with administrator rights is made of.
/// </summary>
public sealed class ProtectedTunnelFilesTests : IDisposable
{
    /// <summary>
    /// The folder of a test.
    /// </summary>
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("golether-protected-");

    /// <summary>
    /// The engine the application found: a folder of the user, as the data folder is.
    /// </summary>
    private readonly string _engineFolder;

    /// <summary>
    /// The protected folder of the test.
    /// </summary>
    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtectedTunnelFilesTests"/> class.
    /// </summary>
    public ProtectedTunnelFilesTests()
    {
        _engineFolder = Directory.CreateDirectory(Path.Combine(_directory.FullName, "data", "amneziawg")).FullName;
        _root = Path.Combine(_directory.FullName, "protected");
        foreach (var name in new[] { "amneziawg.exe", "awg.exe", "wintun.dll" })
        {
            File.WriteAllText(Path.Combine(_engineFolder, name), "official " + name);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>
    /// The official files are copied into the protected folder and started from there; a second change uses the
    /// copies already there.
    /// </summary>
    [Fact]
    public void Engine_IsStartedFromTheCheckedCopy()
    {
        var files = Create(OfficialHashes());

        var engine = files.PrepareEngine(Path.Combine(_engineFolder, "amneziawg.exe"));
        var again = files.PrepareEngine(Path.Combine(_engineFolder, "amneziawg.exe"));

        Assert.Equal(Path.Combine(_root, "engine", "amneziawg.exe"), engine);
        Assert.Equal(engine, again);
        Assert.Equal("official wintun.dll", File.ReadAllText(Path.Combine(_root, "engine", "wintun.dll")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "engine"), "*.new"));
    }

    /// <summary>
    /// A file swapped by another program stops the change before anything runs with administrator rights, and no
    /// copy of it stays behind.
    /// </summary>
    [Fact]
    public void Engine_WithASwappedFile_IsRefused()
    {
        var files = Create(OfficialHashes());
        File.WriteAllText(Path.Combine(_engineFolder, "wintun.dll"), "planted");

        var error = Assert.Throws<TunnelControlException>(() => files.PrepareEngine(Path.Combine(_engineFolder, "amneziawg.exe")));

        Assert.Equal(TunnelHelper.EncodeEngineChanged("wintun.dll"), error.Message);
        Assert.False(File.Exists(Path.Combine(_root, "engine", "wintun.dll")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "engine"), "*.new"));
    }

    /// <summary>
    /// Without the hashes of the processor nothing from the data folder is started; an engine installed where only
    /// administrators write is started as it is.
    /// </summary>
    [Fact]
    public void Engine_WithoutHashes_OnlyFromATrustedFolder()
    {
        var trusted = Directory.CreateDirectory(Path.Combine(_directory.FullName, "Program Files", "AmneziaWG")).FullName;
        var installed = Path.Combine(trusted, "amneziawg.exe");
        var files = new ProtectedTunnelFiles(_root, null, [Path.GetDirectoryName(trusted)!], restrictAccess: false);

        Assert.Equal(installed, files.PrepareEngine(installed));
        Assert.Throws<TunnelControlException>(() => files.PrepareEngine(Path.Combine(_engineFolder, "amneziawg.exe")));
    }

    /// <summary>
    /// The configuration is written anew with only what the application knows: a command added by another program
    /// does not reach the service.
    /// </summary>
    [Fact]
    public void Configuration_KeepsOnlyKnownKeys()
    {
        var files = Create(OfficialHashes());
        var source = Path.Combine(_directory.FullName, "golether0.conf");
        var written = HostTunnelInterface.Create().BuildConfiguration([]).Render();
        File.WriteAllText(source, written.Replace("[Interface]\n", "[Interface]\nPostUp = calc.exe\n", StringComparison.Ordinal));

        var target = files.PrepareConfiguration("golether0", source);

        Assert.Equal(Path.Combine(_root, "configurations", "golether0.conf"), target);
        Assert.Equal(written, File.ReadAllText(target));
        Assert.DoesNotContain("PostUp", File.ReadAllText(target), StringComparison.Ordinal);

        files.ForgetConfiguration("golether0");
        Assert.False(File.Exists(target));
    }

    /// <summary>
    /// The engine stays while a tunnel needs it; with the last tunnel down nothing of Golether is left in the
    /// protected folder.
    /// </summary>
    [Fact]
    public void LastTunnelDown_LeavesNothingBehind()
    {
        var files = Create(OfficialHashes());
        var source = Path.Combine(_directory.FullName, "golether0.conf");
        File.WriteAllText(source, HostTunnelInterface.Create().BuildConfiguration([]).Render());
        files.PrepareEngine(Path.Combine(_engineFolder, "amneziawg.exe"));
        files.PrepareConfiguration("golether0", source);
        files.PrepareConfiguration("golether1", source);

        files.ForgetConfiguration("golether0");
        Assert.True(File.Exists(Path.Combine(_root, "engine", "amneziawg.exe")));

        files.ForgetConfiguration("golether1");
        Assert.False(Directory.Exists(_root));
    }

    /// <summary>
    /// A file that is no configuration of the application, or too large to be one, is refused with a fact that does
    /// not quote it: the user might not be allowed to read the file it points to.
    /// </summary>
    [Fact]
    public void Configuration_ThatIsNotOurs_IsRefused()
    {
        var files = Create(OfficialHashes());
        var broken = Path.Combine(_directory.FullName, "golether0.conf");
        File.WriteAllText(broken, "secret line without a section");
        var large = Path.Combine(_directory.FullName, "golether1.conf");
        File.WriteAllText(large, new string('#', 70 * 1024));

        var error = Assert.Throws<TunnelControlException>(() => files.PrepareConfiguration("golether0", broken));
        Assert.Throws<TunnelControlException>(() => files.PrepareConfiguration("golether1", large));

        Assert.Equal(TunnelHelper.EncodeConfigurationRejected(), error.Message);
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The protected folders inherit nothing: only the system and the administrators have full rights, the users may
    /// only read and start the engine, and the configurations (they hold a private key) are not readable by them.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void AccessRules_LeaveTheUsersOnlyReading()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Access rules of folders exist on Windows only.");
            return;
        }

        var engine = ProtectedTunnelFiles.CreateAccessRules(readableByUsers: true);
        var configurations = ProtectedTunnelFiles.CreateAccessRules(readableByUsers: false);

        Assert.True(engine.AreAccessRulesProtected);
        Assert.Equal(
            [WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.BuiltinUsersSid],
            Rules(engine).Select(rule => Known((SecurityIdentifier)rule.IdentityReference)));
        Assert.Equal(FileSystemRights.ReadAndExecute, Rules(engine)[2].FileSystemRights & ~FileSystemRights.Synchronize);
        Assert.Equal(2, Rules(configurations).Length);
    }

    /// <summary>
    /// Reads the rules of a folder.
    /// </summary>
    /// <param name="security">The rules.</param>
    /// <returns>The access rules in their order.</returns>
    [SupportedOSPlatform("windows")]
    private static FileSystemAccessRule[] Rules(DirectorySecurity security)
        => [.. security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()];

    /// <summary>
    /// Names a well-known identity.
    /// </summary>
    /// <param name="sid">The identity.</param>
    /// <returns>Its kind.</returns>
    [SupportedOSPlatform("windows")]
    private static WellKnownSidType Known(SecurityIdentifier sid)
        => new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.BuiltinUsersSid }
            .Single(kind => sid.IsWellKnown(kind));

    /// <summary>
    /// Creates the protected files of the test, which keep the rights of the temporary folder.
    /// </summary>
    /// <param name="hashes">The known hashes of the engine.</param>
    /// <returns>The protected files.</returns>
    private ProtectedTunnelFiles Create(IReadOnlyDictionary<string, string> hashes)
        => new(_root, hashes, [Path.Combine(_directory.FullName, "Program Files")], restrictAccess: false);

    /// <summary>
    /// Computes the hashes of the engine as it was put into the data folder.
    /// </summary>
    /// <returns>The hashes by file name.</returns>
    private Dictionary<string, string> OfficialHashes()
        => Directory.GetFiles(_engineFolder).ToDictionary(
            path => Path.GetFileName(path),
            path => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(path)))),
            StringComparer.OrdinalIgnoreCase);
}
