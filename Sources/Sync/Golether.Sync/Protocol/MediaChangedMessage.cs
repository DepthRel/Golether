using Golether.Core.Media;

namespace Golether.Sync.Protocol;

/// <summary>
/// The host shares another media file (or stops sharing).
/// </summary>
/// <param name="Media">The media, or <see langword="null"/>.</param>
public sealed record MediaChangedMessage(MediaDescriptor? Media) : SessionMessage;
