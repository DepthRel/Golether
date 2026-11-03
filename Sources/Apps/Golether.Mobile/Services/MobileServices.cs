using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Golether.Client.Enums;
using Golether.Client.Services;
using Golether.Core.Configuration;
using Golether.Core.Data;
using Golether.Core.Data.Migrations.SQLite.Runner;
using Golether.Core.Data.Stores;
using Golether.Core.Playback;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Conference.GStreamer;
using Golether.Media.Player.Mpv;
using Golether.Media.Streaming.Gallery;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Transports.PortMapping;

namespace Golether.Mobile.Services;

/// <summary>
/// The composition of the mobile client: the data of the application, the device key and the player.
/// </summary>
public sealed class MobileServices : IMobileServices
{
    /// <summary>
    /// The file of the plugin registry of GStreamer in the cache folder.
    /// </summary>
    private const string REGISTRY_FILE = "gst-registry.bin";

    /// <summary>
    /// How long the volume of a voice must stay unchanged to be written.
    /// </summary>
    private static readonly TimeSpan _voiceVolumeSaveDelay = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// The folder of the track files of the user in the cache folder.
    /// </summary>
    private const string TRACK_FILES_FOLDER = "tracks";

    /// <summary>
    /// Keeps the services of the data alive.
    /// </summary>
    private readonly ServiceProvider _provider;

    /// <summary>
    /// The system.
    /// </summary>
    private readonly IMobilePlatform _platform;

    /// <summary>
    /// The folder of the JPEG files of a shown folder in the cache folder.
    /// </summary>
    private const string GALLERY_FOLDER = "gallery";

    /// <summary>
    /// How the application names itself to the page of the project and to the routers.
    /// </summary>
    private const string USER_AGENT = "Golether/1.0";

    /// <summary>
    /// The cameras and voices, once the first session needed them.
    /// </summary>
    private IConferenceMedia? _conference;

    /// <summary>
    /// The client of the web, once the first hosted session needed it.
    /// </summary>
    private HttpClient? _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobileServices"/> class.
    /// </summary>
    /// <param name="provider">The services of the data.</param>
    /// <param name="platform">The system.</param>
    /// <param name="identity">The device key.</param>
    /// <param name="player">The player.</param>
    /// <param name="language">The language of the application.</param>
    /// <param name="avatarsDirectory">The folder of the picture of the user.</param>
    private MobileServices(ServiceProvider provider, IMobilePlatform platform, DeviceIdentity identity, MpvPlayer player, ILanguageService language, string avatarsDirectory)
    {
        _provider = provider;
        _platform = platform;
        Identity = identity;
        Player = player;
        Controls = new BackgroundPlayerControls(player, provider.GetRequiredService<ILogger<BackgroundPlayerControls>>());
        TrackFiles = new ImportedTrackFiles(Path.Combine(platform.CacheDirectory, TRACK_FILES_FOLDER));
        VoiceVolumes = new VoiceVolumeStore(Settings, _voiceVolumeSaveDelay);
        Avatars = new AvatarStore(avatarsDirectory);
        Language = language;
    }

    /// <inheritdoc />
    public DeviceIdentity Identity { get; }

    /// <summary>
    /// Gets the player.
    /// </summary>
    public MpvPlayer Player { get; }

    /// <inheritdoc />
    MpvPlayer? IMobileServices.Player => Player;

    /// <inheritdoc />
    public IPlaybackController Playback => Player;

    /// <inheritdoc />
    public BackgroundPlayerControls Controls { get; }

    /// <inheritdoc />
    public ImportedTrackFiles TrackFiles { get; }

    /// <inheritdoc />
    public VoiceVolumeStore VoiceVolumes { get; }

    /// <inheritdoc />
    public IAvatarStore Avatars { get; }

    /// <inheritdoc />
    public ILanguageService Language { get; }

    /// <inheritdoc />
    public ILoggerFactory LoggerFactory => _provider.GetRequiredService<ILoggerFactory>();

    /// <inheritdoc />
    public ISettingsStore Settings => _provider.GetRequiredService<ISettingsStore>();

    /// <summary>
    /// Prepares the data folder, migrates the database, loads the device key and creates the player.
    /// </summary>
    /// <param name="platform">The system.</param>
    /// <param name="features">The paid features: without subtitles the player does not show those of a file.</param>
    /// <returns>The services.</returns>
    /// <exception cref="InvalidOperationException">The player could not start; the message explains why.</exception>
    public static async Task<MobileServices> CreateAsync(IMobilePlatform platform, IFeatureAccess features)
    {
        var paths = AppDataPaths.Resolve(getEnvironmentVariable: name => name == AppDataPaths.DATA_DIRECTORY_VARIABLE ? platform.DataDirectory : null);
        paths.EnsureCreated();
        SqliteDatabaseMigrator.MigrateUp(paths.ConnectionString);

        var provider = new ServiceCollection()
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Information))
            .AddSingleton(TimeProvider.System)
            .AddGoletherData(paths.ConnectionString)
            .BuildServiceProvider();

        var language = new LanguageService(Texts.Localizer, provider.GetRequiredService<ISettingsStore>());
        await language.InitializeAsync(CancellationToken.None);

        var store = new RecoveringDeviceIdentityStore(
            new FileDeviceIdentityStore(paths.IdentityDirectory, platform.Secrets, TimeProvider.System),
            provider.GetRequiredService<ILogger<RecoveringDeviceIdentityStore>>());
        var identity = store.LoadOrCreate();

        platform.PrepareMedia();
        var options = new MpvPlayerOptions
        {
            UseRenderApi = true,

            // Decoded frames go to the drawing as images of the graphics, without a copy through the processor for
            // every frame; a device without that falls back to the copy.
            HardwareDecoding = "mediacodec,mediacodec-copy",
            LowPower = true,
            DemuxerCacheSize = "64MiB",
            SubtitlesAtStart = features.IsAvailable(PaidFeature.Subtitles),
        };
        if (!MpvPlayer.TryCreate(options, provider.GetRequiredService<ILogger<MpvPlayer>>(), out var player, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var services = new MobileServices(provider, platform, identity, player!, language, paths.AvatarsDirectory);
        services.TrackFiles.Clear();
        GalleryCatalog.DeleteLeftovers(Path.Combine(platform.CacheDirectory, GALLERY_FOLDER));

        return services;
    }

    /// <inheritdoc />
    public IParticipantConnection CreateConnection(IConferenceMedia conference, CurrentSession current, string displayName)
        => new ParticipantConnection(this, conference, current, displayName);

    /// <inheritdoc />
    public IHostConnection CreateHost(IConferenceMedia conference, CurrentSession current, IAdmissionPrompt prompt)
    {
        _http ??= CreateHttpClient();

        return new HostConnection(
            this,
            conference,
            current,
            prompt,
            new RendezvousDirectory(_http, Settings, logger: LoggerFactory.CreateLogger<RendezvousDirectory>()),
            PortMapper.CreateDefault(_http, LoggerFactory),
            _provider.GetRequiredService<IContactStore>(),
            Path.Combine(_platform.CacheDirectory, GALLERY_FOLDER));
    }

    /// <summary>
    /// Creates the client of the web for the page of the project and the routers; one serves the whole application.
    /// </summary>
    /// <returns>The client.</returns>
    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);

        return http;
    }

    /// <inheritdoc />
    public async Task<IConferenceMedia> GetConferenceAsync()
    {
        if (_conference is { } ready)
        {
            return ready;
        }

        try
        {
            _platform.PrepareConference();

            var options = new GStreamerConferenceOptions
            {
                RegistryFile = Path.Combine(_platform.CacheDirectory, REGISTRY_FILE),
                DefaultCamera = _platform.GetCamera,
            };
            var logger = LoggerFactory.CreateLogger<GStreamerConferenceMedia>();
            _conference = await Task.Run(() => new GStreamerConferenceMedia(options, logger));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _conference = new UnavailableConferenceMedia(ex.Message);
        }

        return _conference;
    }
}
