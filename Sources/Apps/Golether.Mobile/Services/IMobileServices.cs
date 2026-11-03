using Microsoft.Extensions.Logging;
using Golether.Client.Services;
using Golether.Core.Data.Stores;
using Golether.Core.Playback;
using Golether.Media.Conference;
using Golether.Media.Player.Mpv;
using Golether.Security.Admission;
using Golether.Security.Identity;

namespace Golether.Mobile.Services;

/// <summary>
/// What the screen and the session of the mobile client are composed of.
/// </summary>
public interface IMobileServices
{
    /// <summary>
    /// Gets the device key.
    /// </summary>
    DeviceIdentity Identity { get; }

    /// <summary>
    /// Gets the player the session drives.
    /// </summary>
    IPlaybackController Playback { get; }

    /// <summary>
    /// Gets the player the video view draws, when there is one.
    /// </summary>
    MpvPlayer? Player { get; }

    /// <summary>
    /// Gets the local controls of the player, which are safe to call from the UI thread.
    /// </summary>
    BackgroundPlayerControls Controls { get; }

    /// <summary>
    /// Gets the track files of the user.
    /// </summary>
    ImportedTrackFiles TrackFiles { get; }

    /// <summary>
    /// Gets the remembered volumes of the voices.
    /// </summary>
    VoiceVolumeStore VoiceVolumes { get; }

    /// <summary>
    /// Gets the picture of the user, shown to the others while the camera is off.
    /// </summary>
    IAvatarStore Avatars { get; }

    /// <summary>
    /// Gets the language of the application.
    /// </summary>
    ILanguageService Language { get; }

    /// <summary>
    /// Gets the logger factory.
    /// </summary>
    ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Gets the settings.
    /// </summary>
    ISettingsStore Settings { get; }

    /// <summary>
    /// Starts the cameras and voices on the first call; call it on the UI thread. A failure leaves the session without
    /// them, it does not stop it.
    /// </summary>
    /// <returns>The conference, unavailable when it could not start.</returns>
    Task<IConferenceMedia> GetConferenceAsync();

    /// <summary>
    /// Prepares the place of this device in a new session.
    /// </summary>
    /// <param name="conference">The cameras and voices.</param>
    /// <param name="current">What follows the session of the moment.</param>
    /// <param name="displayName">The name shown to others.</param>
    /// <returns>The connection, not connected yet.</returns>
    IParticipantConnection CreateConnection(IConferenceMedia conference, CurrentSession current, string displayName);

    /// <summary>
    /// Prepares a session this device hosts.
    /// </summary>
    /// <param name="conference">The cameras and voices.</param>
    /// <param name="current">What follows the session of the moment.</param>
    /// <param name="prompt">Asks the user whether a participant may come in.</param>
    /// <returns>The session, not started yet.</returns>
    IHostConnection CreateHost(IConferenceMedia conference, CurrentSession current, IAdmissionPrompt prompt);
}
