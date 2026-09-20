namespace Golether.Sync.Protocol;

/// <summary>
/// The host switches devices of a participant off. Only switching off is possible: the participant decides whether
/// to switch them on again.
/// </summary>
/// <param name="Microphone">Whether the microphone is switched off.</param>
/// <param name="Camera">Whether the camera is switched off.</param>
public sealed record ModerationMessage(bool Microphone, bool Camera) : SessionMessage;
