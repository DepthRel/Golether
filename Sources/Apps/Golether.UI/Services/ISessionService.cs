using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.UI.Services;

/// <summary>
/// Starts, joins and controls watch sessions for the UI.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Raised when the session view changed. Raised on a background thread.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Raised for the event feed. Raised on a background thread.
    /// </summary>
    event EventHandler<SessionEvent>? EventRaised;

    /// <summary>
    /// Raised for chat lines and reactions. Raised on a background thread.
    /// </summary>
    event EventHandler<ChatEntry>? ChatReceived;

    /// <summary>
    /// Raised for the strokes drawn over the video. Raised on a background thread.
    /// </summary>
    event EventHandler<StrokeUpdate>? DrawReceived;

    /// <summary>
    /// Gets the state of an automatic reconnection, or an empty string while the connection is fine.
    /// </summary>
    string ReconnectMessage { get; }

    /// <summary>
    /// Gets a value indicating whether a session is running.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets a value indicating whether this device hosts the session.
    /// </summary>
    bool IsHost { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a hosted session asks the router to open its port.
    /// </summary>
    bool OpenRouterPort { get; set; }

    /// <summary>
    /// Gets a value indicating whether the Windows firewall has no rule for this copy of the application, so
    /// participants cannot get in at all.
    /// </summary>
    bool FirewallBlocked { get; }

    /// <summary>
    /// Asks the firewall to let participants in. Shows the administrator prompt once; no service stays behind.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when incoming connections are allowed now.</returns>
    Task<bool> AllowFirewallAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts hosting a session.
    /// </summary>
    /// <param name="sessionName">The session name.</param>
    /// <param name="displayName">The host name.</param>
    /// <param name="port">The listening port.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the session listens.</returns>
    Task StartHostingAsync(string sessionName, string displayName, int port, CancellationToken cancellationToken);

    /// <summary>
    /// Shares a media file (host only).
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the file is shared.</returns>
    Task ShareMediaAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an invitation link (host only).
    /// </summary>
    /// <param name="additionalEndpoints">Addresses entered by the user, listed first.</param>
    /// <param name="lifetime">The lifetime.</param>
    /// <returns>The invitation.</returns>
    Invite CreateInvite(IReadOnlyList<PeerEndpoint> additionalEndpoints, TimeSpan lifetime);

    /// <summary>
    /// Joins a session.
    /// </summary>
    /// <param name="inviteLink">The invitation link.</param>
    /// <param name="displayName">The participant name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the host admitted this device.</returns>
    /// <exception cref="FormatException">The link is invalid.</exception>
    /// <exception cref="SessionJoinException">Joining failed.</exception>
    Task JoinAsync(string inviteLink, string displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Plays a local copy of the shared file (participant only).
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="false"/> when the file differs.</returns>
    Task<bool> UseLocalCopyAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a playback intent.
    /// </summary>
    /// <param name="request">The intent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the intent was sent.</returns>
    Task RequestAsync(PlaybackRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the current view, or <see langword="null"/> without session.
    /// </summary>
    /// <returns>The snapshot.</returns>
    SessionSnapshot? GetSnapshot();

    /// <summary>
    /// Switches devices of a participant off (host only).
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns>A task that completes when the request was sent.</returns>
    Task SwitchOffParticipantDevicesAsync(PeerId peerId, bool microphone, bool camera);

    /// <summary>
    /// Sends a chat line or a reaction to everybody in the session.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="text">The text or reaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the message was sent.</returns>
    Task SendChatAsync(ChatKind kind, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a piece of a stroke drawn over the video to everybody in the session.
    /// </summary>
    /// <param name="strokeId">The stroke.</param>
    /// <param name="phase">Which part of the stroke this is.</param>
    /// <param name="points">The new points.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the piece was sent.</returns>
    Task SendDrawAsync(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points, CancellationToken cancellationToken);

    /// <summary>
    /// Leaves or ends the session.
    /// </summary>
    /// <returns>A task that completes when the session is closed.</returns>
    Task LeaveAsync();
}
