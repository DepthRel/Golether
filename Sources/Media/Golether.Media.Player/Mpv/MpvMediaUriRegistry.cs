using Golether.Media.Streaming.Caching;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// <see cref="IMediaUriRegistry"/> over the <c>golether://</c> protocol of libmpv.
/// </summary>
public sealed class MpvMediaUriRegistry : IMediaUriRegistry
{
    /// <inheritdoc />
    public Uri Register(Func<Stream> factory) => MpvStreamRegistry.Register(factory);

    /// <inheritdoc />
    public void Unregister(Uri uri) => MpvStreamRegistry.Unregister(uri);
}
