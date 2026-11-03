using Golether.Core.Identity;
using Golether.Media.Streaming.Gallery;
using Golether.Security.Invites;

namespace Golether.Mobile.Services;

/// <summary>
/// A session this device hosts: what it shows, who is invited and who stays.
/// </summary>
public interface IHostConnection : ISessionConnection
{
    /// <summary>
    /// Starts the session on the network of the device: finds the rendezvous server and begins to listen.
    /// </summary>
    /// <param name="sessionName">The name of the session.</param>
    /// <param name="displayName">The name of the host.</param>
    /// <param name="cancellationToken">Stops starting.</param>
    /// <returns>A task that completes when participants may knock.</returns>
    /// <exception cref="InvalidOperationException">The session could not start; the message says why.</exception>
    Task StartAsync(string sessionName, string displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Shows a film to everybody.
    /// </summary>
    /// <param name="path">The path of the file (see <see cref="Media.Streaming.Files.OpenedFiles"/>).</param>
    /// <param name="cancellationToken">Stops loading.</param>
    /// <returns>A task that completes when everybody was told.</returns>
    Task ShareMediaAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Shows the pictures and videos of a folder to everybody.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="stripMetadata">Whether the place, the camera and the comments are left out of the pictures.</param>
    /// <param name="cancellationToken">Stops preparing.</param>
    /// <returns>A task that completes when everybody was told.</returns>
    Task ShowGalleryAsync(GalleryFolder folder, bool stripMetadata, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an invitation into the session.
    /// </summary>
    /// <param name="lifetime">How long the invitation lets people in.</param>
    /// <returns>The invitation.</returns>
    /// <exception cref="InvalidOperationException">The device has no network to put into an invitation.</exception>
    Invite CreateInvite(TimeSpan lifetime);

    /// <summary>
    /// Switches devices of a participant off.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns>A task that completes when the request was sent.</returns>
    Task SwitchOffParticipantDevicesAsync(PeerId peerId, bool microphone, bool camera);

    /// <summary>
    /// Removes a participant from the session; coming back takes a new invitation.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <returns>A task that completes when the participant was removed.</returns>
    Task RemoveParticipantAsync(PeerId peerId);
}
