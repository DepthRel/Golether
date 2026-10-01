using Golether.Core.Configuration;
using Golether.Core.Media;
using Golether.Core.Networking;
using Golether.Core.Session;

namespace Golether.Core.Tests;

/// <summary>
/// Tests of <see cref="PeerEndpoint"/>, <see cref="MediaDescriptor"/>, <see cref="ParticipantInfo"/> and
/// <see cref="AppDataPaths"/>.
/// </summary>
public sealed class ValueTypesTests
{
    /// <summary>
    /// Host names, IPv4 and bracketed IPv6 endpoints are parsed and formatted back.
    /// </summary>
    /// <param name="text">The endpoint.</param>
    [Theory]
    [InlineData("example.org:47800")]
    [InlineData("203.0.113.24:1")]
    [InlineData("[2001:db8::1]:65535")]
    public void PeerEndpoint_RoundTrips(string text)
    {
        Assert.True(PeerEndpoint.TryParse(text, out var endpoint));
        Assert.Equal(text, endpoint.ToString());
    }

    /// <summary>
    /// Malformed endpoints are rejected.
    /// </summary>
    /// <param name="text">The endpoint.</param>
    [Theory]
    [InlineData("")]
    [InlineData("host")]
    [InlineData("host:0")]
    [InlineData("host:70000")]
    [InlineData("2001:db8::1:80")]
    [InlineData("bad host:80")]
    [InlineData("-bad.org:80")]
    [InlineData("[not-v6]:80")]
    public void PeerEndpoint_RejectsInvalid(string text) => Assert.False(PeerEndpoint.TryParse(text, out _));

    /// <summary>
    /// Chunk count and lengths follow from the file length.
    /// </summary>
    [Fact]
    public void MediaDescriptor_ComputesChunks()
    {
        var media = new MediaDescriptor { FileName = "a.mkv", Length = (2 * MediaDescriptor.DefaultChunkSize) + 10, QuickId = new string('a', 64) };

        Assert.Equal(3, media.ChunkCount);
        Assert.Equal(MediaDescriptor.DefaultChunkSize, media.GetChunkLength(0));
        Assert.Equal(10, media.GetChunkLength(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => media.GetChunkLength(3));
        media.Validate();
    }

    /// <summary>
    /// Descriptors with path components or bad identifiers are rejected.
    /// </summary>
    [Fact]
    public void MediaDescriptor_ValidateRejectsUnsafeValues()
    {
        var valid = new MediaDescriptor { FileName = "a.mkv", Length = 1, QuickId = new string('a', 64) };

        Assert.Throws<ArgumentException>(() => (valid with { FileName = "../a.mkv" }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { QuickId = "zz" }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { ChunkSize = 10 }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { Length = -1 }).Validate());
    }

    /// <summary>
    /// Display names are trimmed, cleaned from control characters and shortened.
    /// </summary>
    [Fact]
    public void DisplayName_IsNormalized()
    {
        Assert.Equal("Марина", ParticipantInfo.NormalizeDisplayName("  Мар\nина \t"));
        Assert.Equal("Участник", ParticipantInfo.NormalizeDisplayName("\r\n"));
        Assert.Equal(ParticipantInfo.MaxDisplayNameLength, ParticipantInfo.NormalizeDisplayName(new string('x', 200)).Length);
    }

    /// <summary>
    /// The environment variable overrides the data directory.
    /// </summary>
    [Fact]
    public void AppDataPaths_UsesEnvironmentOverride()
    {
        var root = Path.Combine(Path.GetTempPath(), "golether-test-" + Guid.NewGuid().ToString("N"));
        var paths = AppDataPaths.Resolve(AppContext.BaseDirectory, name => name == AppDataPaths.DataDirectoryVariable ? root : null);

        Assert.Equal(Path.GetFullPath(root), paths.Root);
        Assert.Equal(Path.Combine(paths.Root, "golether.db"), paths.DatabaseFile);
    }

    /// <summary>
    /// The installation marker puts the data into the installation root, also from inside a macOS bundle.
    /// </summary>
    /// <param name="appRelative">The application directory relative to the installation root.</param>
    [Theory]
    [InlineData("app")]
    [InlineData("Golether.app/Contents/MacOS")]
    public void AppDataPaths_UsesInstallationRoot(string appRelative)
    {
        var install = Directory.CreateTempSubdirectory("golether-install-");
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(install.FullName, appRelative));
            File.WriteAllText(Path.Combine(install.FullName, AppDataPaths.InstallationMarkerFileName), "installation");

            var paths = AppDataPaths.Resolve(app.FullName, _ => null);
            paths.EnsureCreated();

            Assert.Equal(Path.Combine(install.FullName, "data"), paths.Root);
            Assert.True(Directory.Exists(paths.IdentityDirectory));
            Assert.StartsWith(paths.Root, paths.ComponentsDirectory, StringComparison.Ordinal);
            Assert.StartsWith(paths.Root, paths.GStreamerRegistryFile, StringComparison.Ordinal);
        }
        finally
        {
            install.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Without the marker the data stays next to the application, never in the user profile.
    /// </summary>
    [Fact]
    public void AppDataPaths_WithoutMarker_UsesApplicationDirectory()
    {
        var app = Directory.CreateTempSubdirectory("golether-bin-");
        try
        {
            var paths = AppDataPaths.Resolve(app.FullName, _ => null);

            Assert.Equal(Path.Combine(app.FullName, "data"), paths.Root);
        }
        finally
        {
            app.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A read-only installation fails with an explanation for the user.
    /// </summary>
    [Fact]
    public void AppDataPaths_ReadOnlyLocation_ExplainsWhatToDo()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "golether-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "a file where the data folder should be");
        try
        {
            var paths = new AppDataPaths(Path.Combine(blocker, "data"));

            var error = Assert.Throws<UnauthorizedAccessException>(paths.EnsureCreated);
            Assert.Contains("Переместите папку Golether", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    /// <summary>
    /// Data of an earlier version is moved into the installation once and the old folder disappears.
    /// </summary>
    [Fact]
    public void AppDataPaths_AdoptsLegacyDataOnce()
    {
        var temp = Directory.CreateTempSubdirectory("golether-legacy-");
        try
        {
            var legacy = Directory.CreateDirectory(Path.Combine(temp.FullName, "profile"));
            File.WriteAllText(Path.Combine(legacy.FullName, "golether.db"), "db");
            Directory.CreateDirectory(Path.Combine(legacy.FullName, "identity"));
            File.WriteAllText(Path.Combine(legacy.FullName, "identity", "device.key"), "key");
            var paths = new AppDataPaths(Path.Combine(temp.FullName, "install", "data"));
            paths.EnsureCreated();

            Assert.True(paths.AdoptLegacyData(legacy.FullName));
            Assert.Equal("key", File.ReadAllText(Path.Combine(paths.IdentityDirectory, "device.key")));
            Assert.True(File.Exists(paths.DatabaseFile));
            Assert.False(Directory.Exists(legacy.FullName));

            Directory.CreateDirectory(legacy.FullName);
            File.WriteAllText(Path.Combine(legacy.FullName, "golether.db"), "other");
            Assert.False(paths.AdoptLegacyData(legacy.FullName));
            Assert.Equal("db", File.ReadAllText(paths.DatabaseFile));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The rule for a shared file name is the same on every system: a name that is fine where the host runs (a colon
    /// or a question mark on Linux) is accepted everywhere, and only control characters and path separators are not.
    /// </summary>
    [Fact]
    public void MediaDescriptor_FileNameRuleDoesNotDependOnTheSystem()
    {
        var valid = new MediaDescriptor { FileName = "a.mkv", Length = 1, QuickId = new string('a', 64) };

        (valid with { FileName = "Film: part 2? <final> \"cut\" | 1*.mkv" }).Validate();
        Assert.Throws<ArgumentException>(() => (valid with { FileName = "a\\b.mkv" }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { FileName = "a\nb.mkv" }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { FileName = new string('a', MediaDescriptor.MaxFileNameLength + 1) }).Validate());
    }

    /// <summary>
    /// The host makes the name of the file it shares acceptable to everybody before sending it.
    /// </summary>
    [Fact]
    public void MediaDescriptor_NormalizesTheSharedFileName()
    {
        Assert.Equal("Film: 2.mkv", MediaDescriptor.NormalizeFileName("Film: 2.mkv"));
        Assert.Equal("a_b_c.mkv", MediaDescriptor.NormalizeFileName("a\\b/c.mkv"));
        Assert.Equal("a_b", MediaDescriptor.NormalizeFileName(" a\tb "));
        Assert.Equal("_", MediaDescriptor.NormalizeFileName(null));
        Assert.Equal(MediaDescriptor.MaxFileNameLength, MediaDescriptor.NormalizeFileName(new string('x', 400)).Length);
        foreach (var name in new[] { "a\\b/c.mkv", "  ", new string('x', 999), "ok.mp4" })
        {
            new MediaDescriptor { FileName = MediaDescriptor.NormalizeFileName(name), Length = 1, QuickId = new string('a', 64) }.Validate();
        }
    }

    /// <summary>
    /// The numbers of a status that came from another device are bounded: values at the limits of the types would
    /// overflow as soon as the host compares or negates them.
    /// </summary>
    [Fact]
    public void ParticipantStatus_SanitizeBoundsTheNumbers()
    {
        var hostile = new ParticipantStatus
        {
            PeerId = Golether.Core.Identity.PeerId.Parse(new string('b', 64)),
            Position = TimeSpan.MinValue,
            Drift = TimeSpan.MinValue,
            CacheAhead = TimeSpan.MaxValue,
            RoundTripMilliseconds = int.MaxValue,
            BytesFromPeers = -5,
            BytesToPeers = long.MinValue,
            IsBuffering = true,
        };

        var clean = hostile.Sanitize();

        Assert.Equal(-ParticipantStatus.MaxSpan, clean.Position);
        Assert.Equal(-ParticipantStatus.MaxSpan, clean.Drift);
        Assert.Equal(ParticipantStatus.MaxSpan, clean.CacheAhead);
        Assert.Equal(ParticipantStatus.MaxRoundTripMilliseconds, clean.RoundTripMilliseconds);
        Assert.Equal(0, clean.BytesFromPeers);
        Assert.Equal(0, clean.BytesToPeers);
        Assert.True(clean.IsBuffering);

        // The arithmetic the host does with a status no longer fails.
        Assert.True(-clean.Drift > TimeSpan.Zero);
        Assert.True((clean.Position!.Value - TimeSpan.FromHours(1)).Duration() > TimeSpan.Zero);

        var ordinary = new ParticipantStatus { PeerId = hostile.PeerId, Position = TimeSpan.FromMinutes(5), Drift = TimeSpan.FromMilliseconds(-40), RoundTripMilliseconds = 30 };
        Assert.Equal(ordinary, ordinary.Sanitize());
        Assert.Null((ordinary with { Position = null, RoundTripMilliseconds = null }).Sanitize().Position);
    }
}
