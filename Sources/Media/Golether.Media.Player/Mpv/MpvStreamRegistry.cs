using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Golether.Media.Streaming.Caching;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// Exposes .NET streams to libmpv through the <c>golether://</c> protocol (<c>mpv_stream_cb_add_ro</c>).
/// </summary>
/// <remarks>
/// A stream factory is registered under a random token and addressed as <c>golether://media/&lt;token&gt;</c>. libmpv may
/// open the URI several times; each open gets its own stream from the factory.
/// </remarks>
public static unsafe class MpvStreamRegistry
{
    /// <summary>
    /// The protocol name.
    /// </summary>
    public const string Protocol = "golether";

    /// <summary>
    /// The URI prefix.
    /// </summary>
    public const string UriPrefix = "golether://media/";

    /// <summary>
    /// Stream factories by token.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Func<Stream>> Factories = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a stream factory.
    /// </summary>
    /// <param name="factory">Creates a new readable, seekable stream for each open.</param>
    /// <returns>The URI to pass to the player.</returns>
    public static Uri Register(Func<Stream> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        Factories[token] = factory;
        return new Uri(UriPrefix + token);
    }

    /// <summary>
    /// Removes a registration; streams already opened stay valid until the player closes them.
    /// </summary>
    /// <param name="uri">The URI returned by <see cref="Register"/>.</param>
    public static void Unregister(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (TryGetToken(uri.OriginalString, out var token))
        {
            Factories.TryRemove(token, out _);
        }
    }

    /// <summary>
    /// Installs the protocol on a player handle.
    /// </summary>
    /// <param name="handle">The mpv handle.</param>
    internal static void Install(nint handle)
        => LibMpv.Check(LibMpv.StreamCbAddRo(handle, Protocol, 0, &Open), "registering the golether:// protocol");

    /// <summary>
    /// Extracts the token of a URI.
    /// </summary>
    /// <param name="uri">The URI.</param>
    /// <param name="token">The token.</param>
    /// <returns><see langword="true"/> when the URI has the expected form.</returns>
    private static bool TryGetToken(string uri, out string token)
    {
        token = string.Empty;
        if (!uri.StartsWith(UriPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        token = uri[UriPrefix.Length..];
        return token.Length == 32;
    }

    /// <summary>
    /// Opens a stream for libmpv.
    /// </summary>
    /// <param name="userData">Unused.</param>
    /// <param name="uri">The UTF-8 URI.</param>
    /// <param name="info">The callbacks to fill in.</param>
    /// <returns>0 on success, <see cref="MpvError.LoadingFailed"/> otherwise.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Open(nint userData, byte* uri, MpvStreamCallbacks* info)
    {
        try
        {
            var text = Marshal.PtrToStringUTF8((nint)uri) ?? string.Empty;
            if (!TryGetToken(text, out var token) || !Factories.TryGetValue(token, out var factory))
            {
                return MpvError.LoadingFailed;
            }

            var stream = factory();
            info->Cookie = GCHandle.ToIntPtr(GCHandle.Alloc(stream));
            info->Read = &Read;
            info->Seek = &Seek;
            info->Size = &Size;
            info->Close = &Close;
            info->Cancel = &Cancel;
            return 0;
        }
        catch (Exception)
        {
            return MpvError.LoadingFailed;
        }
    }

    /// <summary>
    /// Reads from a stream.
    /// </summary>
    /// <param name="cookie">The stream handle.</param>
    /// <param name="buffer">The destination.</param>
    /// <param name="count">The capacity of the destination.</param>
    /// <returns>The number of bytes, 0 at the end, -1 on error.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Read(nint cookie, byte* buffer, ulong count)
    {
        try
        {
            var stream = (Stream)GCHandle.FromIntPtr(cookie).Target!;
            return stream.Read(new Span<byte>(buffer, (int)Math.Min(count, int.MaxValue)));
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>
    /// Seeks a stream.
    /// </summary>
    /// <param name="cookie">The stream handle.</param>
    /// <param name="offset">The absolute position.</param>
    /// <returns>The new position or <see cref="MpvError.Generic"/>.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Seek(nint cookie, long offset)
    {
        try
        {
            var stream = (Stream)GCHandle.FromIntPtr(cookie).Target!;
            return stream.Seek(offset, SeekOrigin.Begin);
        }
        catch (Exception)
        {
            return MpvError.Generic;
        }
    }

    /// <summary>
    /// Returns the stream size.
    /// </summary>
    /// <param name="cookie">The stream handle.</param>
    /// <returns>The size or <see cref="MpvError.Unsupported"/>.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Size(nint cookie)
    {
        try
        {
            return ((Stream)GCHandle.FromIntPtr(cookie).Target!).Length;
        }
        catch (Exception)
        {
            return MpvError.Unsupported;
        }
    }

    /// <summary>
    /// Closes a stream and releases its handle.
    /// </summary>
    /// <param name="cookie">The stream handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Close(nint cookie)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(cookie);
            (handle.Target as Stream)?.Dispose();
            handle.Free();
        }
        catch (Exception)
        {
            // Nothing can be reported to libmpv from close.
        }
    }

    /// <summary>
    /// Unblocks a pending read.
    /// </summary>
    /// <param name="cookie">The stream handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Cancel(nint cookie)
    {
        try
        {
            (GCHandle.FromIntPtr(cookie).Target as MediaReadStream)?.CancelPendingRead();
        }
        catch (Exception)
        {
            // Nothing can be reported to libmpv from cancel.
        }
    }
}
