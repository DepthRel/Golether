using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Golether.Client.Services;
using Golether.Core.Data.Stores;
using Golether.Core.Playback;
using Golether.Media.Conference;
using Golether.Media.Player;
using Golether.Media.Player.Mpv;
using Golether.Mobile.Services;
using Golether.Security.Identity;

namespace Golether.Mobile.Tests;

/// <summary>
/// The services of the mobile client without the native libraries.
/// </summary>
internal sealed class StubServices : IMobileServices, IDisposable
{
    /// <summary>
    /// The folder of the track files of the test.
    /// </summary>
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "golether-mobile-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Whether the microphone of the conference is off.
    /// </summary>
    private bool _microphoneMuted;

    /// <summary>
    /// Whether the camera of the conference is off.
    /// </summary>
    private bool _cameraOff;

    /// <summary>
    /// Initializes a new instance of the <see cref="StubServices"/> class.
    /// </summary>
    public StubServices()
    {
        Identity = DeviceIdentity.CreateNew(TimeProvider.System);
        Playback = Substitute.For<IPlaybackController>();
        Playback.GetSnapshot().Returns(PlayerSnapshot.Empty);
        Player = Substitute.For<ILocalPlayerControls>();
        Controls = new BackgroundPlayerControls(Player, NullLogger<BackgroundPlayerControls>.Instance);
        TrackFiles = new ImportedTrackFiles(_folder);
        Settings = Substitute.For<ISettingsStore>();
        VoiceVolumes = new VoiceVolumeStore(Settings, TimeSpan.Zero);
        Language = Substitute.For<ILanguageService>();
        Language.Languages.Returns([]);

        Conference = Substitute.For<IConferenceMedia>();
        Conference.IsAvailable.Returns(true);
        Conference.MicrophoneMuted.Returns(_ => _microphoneMuted);
        Conference.CameraOff.Returns(_ => _cameraOff);
        Conference.When(c => c.SetMuted(Arg.Any<bool>(), Arg.Any<bool>())).Do(call =>
        {
            _microphoneMuted = call.ArgAt<bool>(0);
            _cameraOff = call.ArgAt<bool>(1);
        });
    }

    /// <summary>
    /// Gets the cameras and voices.
    /// </summary>
    public IConferenceMedia Conference { get; }

    /// <summary>
    /// Gets the player behind the local controls.
    /// </summary>
    public ILocalPlayerControls Player { get; }

    /// <summary>
    /// Gets the connection the next session gets.
    /// </summary>
    public StubConnection Connection { get; } = new();

    /// <inheritdoc />
    public DeviceIdentity Identity { get; }

    /// <inheritdoc />
    public IPlaybackController Playback { get; }

    /// <inheritdoc />
    MpvPlayer? IMobileServices.Player => null;

    /// <inheritdoc />
    public BackgroundPlayerControls Controls { get; }

    /// <inheritdoc />
    public ImportedTrackFiles TrackFiles { get; }

    /// <inheritdoc />
    public VoiceVolumeStore VoiceVolumes { get; }

    /// <inheritdoc />
    public ILanguageService Language { get; }

    /// <inheritdoc />
    public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

    /// <inheritdoc />
    public ISettingsStore Settings { get; }

    /// <inheritdoc />
    public Task<IConferenceMedia> GetConferenceAsync() => Task.FromResult(Conference);

    /// <inheritdoc />
    public IParticipantConnection CreateConnection(IConferenceMedia conference, CurrentSession current, string displayName) => Connection;

    /// <inheritdoc />
    public void Dispose()
    {
        Identity.Dispose();

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
