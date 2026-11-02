using Avalonia.Media.Imaging;
using Golether.Core.Identity;
using Golether.Session;

namespace Golether.Client.Services;

/// <summary>
/// The pictures participants chose for themselves, kept for tiles that appear later.
/// </summary>
public sealed class PeerAvatars
{
    /// <summary>
    /// Turns the bytes of a picture into an image.
    /// </summary>
    private readonly Func<byte[], object?> _decode;

    /// <summary>
    /// The pictures of the participants.
    /// </summary>
    private readonly Dictionary<PeerId, object> _images = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerAvatars"/> class.
    /// </summary>
    /// <param name="decode">Turns bytes into an image, or <see langword="null"/> for a bitmap of Avalonia.</param>
    public PeerAvatars(Func<byte[], object?>? decode = null) => _decode = decode ?? (bytes => new Bitmap(new MemoryStream(bytes)));

    /// <summary>
    /// Takes the new picture of a participant.
    /// </summary>
    /// <param name="update">The picture.</param>
    /// <returns>The image, or <see langword="null"/> when the participant has none now.</returns>
    public object? Apply(AvatarUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        var image = Decode(update.Image);

        if (image is null)
        {
            _images.Remove(update.Peer);
        }
        else
        {
            _images[update.Peer] = image;
        }

        return image;
    }

    /// <summary>
    /// Finds the picture of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <returns>The image, or <see langword="null"/> when there is none.</returns>
    public object? Find(PeerId peer) => _images.GetValueOrDefault(peer);

    /// <summary>
    /// Forgets the pictures: the session is over.
    /// </summary>
    public void Clear() => _images.Clear();

    /// <summary>
    /// Decodes a picture. The bytes came from another device: one that cannot be decoded is simply not shown.
    /// </summary>
    /// <param name="bytes">The bytes, or <see langword="null"/>.</param>
    /// <returns>The image, or <see langword="null"/>.</returns>
    private object? Decode(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return _decode(bytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
