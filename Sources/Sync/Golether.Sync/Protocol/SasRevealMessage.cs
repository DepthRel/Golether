namespace Golether.Sync.Protocol;

/// <summary>
/// The participant reveals the random number it committed to in the hello, once it knows the number of the host.
/// The host checks it against the commitment and only then computes the code and asks the host user.
/// </summary>
/// <param name="Nonce">The number of the participant: 64 lowercase hexadecimal digits.</param>
public sealed record SasRevealMessage(string? Nonce) : SessionMessage;
