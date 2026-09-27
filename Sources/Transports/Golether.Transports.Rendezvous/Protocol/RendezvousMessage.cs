using System.Text.Json.Serialization;

namespace Golether.Transports.Rendezvous.Protocol;

/// <summary>
/// A message between the rendezvous server and a device. Messages are JSON objects with the <c>$t</c> type
/// discriminator, one per length-prefixed frame (see <see cref="RendezvousChannel"/>).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$t")]
[JsonDerivedType(typeof(ServerHelloMessage), "server-hello")]
[JsonDerivedType(typeof(RegisterMessage), "register")]
[JsonDerivedType(typeof(RegisteredMessage), "registered")]
[JsonDerivedType(typeof(ConnectMessage), "connect")]
[JsonDerivedType(typeof(IncomingMessage), "incoming")]
[JsonDerivedType(typeof(AcceptMessage), "accept")]
[JsonDerivedType(typeof(PunchMessage), "punch")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
[JsonDerivedType(typeof(PingMessage), "ping")]
[JsonDerivedType(typeof(PongMessage), "pong")]
public abstract record RendezvousMessage;
