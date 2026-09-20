using Golether.Core.Session;

namespace Golether.Session;

/// <summary>
/// A participant as shown in the UI.
/// </summary>
/// <param name="Info">The participant.</param>
/// <param name="Status">The latest status, or <see langword="null"/>.</param>
/// <param name="IsLocal">Whether the entry is this device.</param>
public sealed record ParticipantView(ParticipantInfo Info, ParticipantStatus? Status, bool IsLocal);
