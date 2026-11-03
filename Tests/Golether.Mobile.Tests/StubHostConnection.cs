using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Media.Streaming.Gallery;
using Golether.Mobile.Services;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Mobile.Tests;

/// <summary>
/// A session the phone hosts as a test wants it, without a network: it records what the user asked of it.
/// </summary>
internal sealed class StubHostConnection : IHostConnection
{
    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public event EventHandler<AvatarUpdate>? AvatarReceived;

    /// <summary>
    /// Gets or sets the state of the session.
    /// </summary>
    public SessionSnapshot? Snapshot { get; set; }

    /// <inheritdoc />
    public bool IsReconnecting => false;

    /// <inheritdoc />
    public string ReconnectMessage => string.Empty;

    /// <inheritdoc />
    public string? EndReason => null;

    /// <summary>
    /// Gets the name the session was started with, or <see langword="null"/> before the start.
    /// </summary>
    public string? StartedAs { get; private set; }

    /// <summary>
    /// Gets or sets the failure of the start, or <see langword="null"/> for a start that works.
    /// </summary>
    public Exception? StartFailure { get; set; }

    /// <summary>
    /// Gets the films shown, by their paths.
    /// </summary>
    public List<string> Films { get; } = [];

    /// <summary>
    /// Gets the folders shown, with whether their metadata was left out.
    /// </summary>
    public List<(GalleryFolder Folder, bool StripMetadata)> Folders { get; } = [];

    /// <summary>
    /// Gets the devices switched off: the participant, the microphone, the camera.
    /// </summary>
    public List<(PeerId Peer, bool Microphone, bool Camera)> SwitchedOff { get; } = [];

    /// <summary>
    /// Gets the participants removed.
    /// </summary>
    public List<PeerId> Removed { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the session was ended.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public Task StartAsync(string sessionName, string displayName, CancellationToken cancellationToken)
    {
        if (StartFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        StartedAs = sessionName;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShareMediaAsync(string path, CancellationToken cancellationToken)
    {
        Films.Add(path);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowGalleryAsync(GalleryFolder folder, bool stripMetadata, CancellationToken cancellationToken)
    {
        Folders.Add((folder, stripMetadata));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Invite CreateInvite(TimeSpan lifetime) => new()
    {
        HostPeerId = PeerId.Parse(new string('a', 64)),
        HostName = "Катя",
        SessionName = StartedAs ?? string.Empty,
        Endpoints = [PeerEndpoint.Parse("192.168.1.20:47800")],
        Token = Invite.CreateToken(),
        ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
    };

    /// <inheritdoc />
    public Task SwitchOffParticipantDevicesAsync(PeerId peerId, bool microphone, bool camera)
    {
        SwitchedOff.Add((peerId, microphone, camera));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveParticipantAsync(PeerId peerId)
    {
        Removed.Add(peerId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public SessionSnapshot? GetSnapshot() => Snapshot;

    /// <inheritdoc />
    public Task RequestAsync(PlaybackRequest request) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetAvatarAsync(byte[]? image) => Task.CompletedTask;

    /// <inheritdoc />
    public void RetryNow()
    {
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Tells that the session changed.
    /// </summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Delivers the picture of a participant.
    /// </summary>
    /// <param name="update">The picture.</param>
    public void RaiseAvatar(AvatarUpdate update) => AvatarReceived?.Invoke(this, update);
}
