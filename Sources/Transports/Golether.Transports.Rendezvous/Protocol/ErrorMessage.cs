using Golether.Core.Enums;

namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// The server refuses a request; the connection is closed after it.
/// </summary>
/// <param name="Error">The reason.</param>
public sealed record ErrorMessage(RendezvousError Error) : RendezvousMessage;
