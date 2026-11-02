using Golether.Core.Data.Enums;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Client.Services;

/// <summary>
/// What the chat and the pen need from a session, whoever holds it.
/// </summary>
public interface ISessionMessages
{
    /// <summary>
    /// Raised for chat lines and reactions. Raised on a background thread.
    /// </summary>
    event EventHandler<ChatEntry>? ChatReceived;

    /// <summary>
    /// Raised for the strokes drawn over the video. Raised on a background thread.
    /// </summary>
    event EventHandler<StrokeUpdate>? DrawReceived;

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
}
