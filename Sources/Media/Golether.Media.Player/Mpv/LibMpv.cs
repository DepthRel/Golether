using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// Value formats of the libmpv client API (<c>mpv_format</c>).
/// </summary>
internal enum MpvFormat
{
    /// <summary>
    /// No data.
    /// </summary>
    None = 0,

    /// <summary>
    /// A zero-terminated UTF-8 string.
    /// </summary>
    String = 1,

    /// <summary>
    /// A boolean stored as <c>int</c>.
    /// </summary>
    Flag = 3,

    /// <summary>
    /// A signed 64-bit integer.
    /// </summary>
    Int64 = 4,

    /// <summary>
    /// A double.
    /// </summary>
    Double = 5,
}

/// <summary>
/// Event identifiers of the libmpv client API (<c>mpv_event_id</c>).
/// </summary>
internal enum MpvEventId
{
    /// <summary>
    /// No event (timeout or wakeup).
    /// </summary>
    None = 0,

    /// <summary>
    /// The player is shutting down.
    /// </summary>
    Shutdown = 1,

    /// <summary>
    /// A log message.
    /// </summary>
    LogMessage = 2,

    /// <summary>
    /// A file starts loading.
    /// </summary>
    StartFile = 6,

    /// <summary>
    /// A message sent with <c>script-message</c>, for example by an input binding.
    /// </summary>
    ClientMessage = 16,

    /// <summary>
    /// A file stopped playing.
    /// </summary>
    EndFile = 7,

    /// <summary>
    /// A file was loaded.
    /// </summary>
    FileLoaded = 8,

    /// <summary>
    /// Playback restarted after a seek or load.
    /// </summary>
    PlaybackRestart = 21,

    /// <summary>
    /// An observed property changed.
    /// </summary>
    PropertyChange = 22,
}

/// <summary>
/// Error codes of the libmpv client API used by stream callbacks.
/// </summary>
internal static class MpvError
{
    /// <summary>
    /// Loading failed (<c>MPV_ERROR_LOADING_FAILED</c>).
    /// </summary>
    public const int LoadingFailed = -13;

    /// <summary>
    /// The operation is unsupported (<c>MPV_ERROR_UNSUPPORTED</c>).
    /// </summary>
    public const int Unsupported = -18;

    /// <summary>
    /// A generic error (<c>MPV_ERROR_GENERIC</c>).
    /// </summary>
    public const int Generic = -20;
}

/// <summary>
/// <c>mpv_event</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEvent
{
    /// <summary>
    /// The event identifier.
    /// </summary>
    public MpvEventId EventId;

    /// <summary>
    /// The error code (for replies).
    /// </summary>
    public int Error;

    /// <summary>
    /// The user data of the request or observed property.
    /// </summary>
    public ulong ReplyUserData;

    /// <summary>
    /// The event-specific data.
    /// </summary>
    public nint Data;
}

/// <summary>
/// <c>mpv_event_property</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventProperty
{
    /// <summary>
    /// The property name.
    /// </summary>
    public nint Name;

    /// <summary>
    /// The value format; <see cref="MpvFormat.None"/> when the property is unavailable.
    /// </summary>
    public MpvFormat Format;

    /// <summary>
    /// A pointer to the value.
    /// </summary>
    public nint Data;
}

/// <summary>
/// <c>mpv_event_end_file</c> (leading fields).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventEndFile
{
    /// <summary>
    /// The reason (<c>mpv_end_file_reason</c>): 0 EOF, 2 stop, 3 quit, 4 error, 5 redirect.
    /// </summary>
    public int Reason;

    /// <summary>
    /// The error code when <see cref="Reason"/> is 4.
    /// </summary>
    public int Error;
}

/// <summary>
/// <c>mpv_event_client_message</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvEventClientMessage
{
    /// <summary>
    /// The number of arguments.
    /// </summary>
    public int NumArgs;

    /// <summary>
    /// The UTF-8 arguments.
    /// </summary>
    public byte** Args;
}

/// <summary>
/// <c>mpv_event_log_message</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventLogMessage
{
    /// <summary>
    /// The module prefix.
    /// </summary>
    public nint Prefix;

    /// <summary>
    /// The level name.
    /// </summary>
    public nint Level;

    /// <summary>
    /// The message text.
    /// </summary>
    public nint Text;

    /// <summary>
    /// The numeric level.
    /// </summary>
    public int LogLevel;
}

/// <summary>
/// <c>mpv_stream_cb_info</c>: callbacks of a custom stream.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvStreamCallbacks
{
    /// <summary>
    /// The opaque cookie passed to the callbacks.
    /// </summary>
    public nint Cookie;

    /// <summary>
    /// <c>int64_t read(void *cookie, char *buf, uint64_t nbytes)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, byte*, ulong, long> Read;

    /// <summary>
    /// <c>int64_t seek(void *cookie, int64_t offset)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, long, long> Seek;

    /// <summary>
    /// <c>int64_t size(void *cookie)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, long> Size;

    /// <summary>
    /// <c>void close(void *cookie)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, void> Close;

    /// <summary>
    /// <c>void cancel(void *cookie)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nint, void> Cancel;
}

/// <summary>
/// P/Invoke declarations of the libmpv client API (the subset Golether uses).
/// </summary>
internal static unsafe partial class LibMpv
{
    /// <summary>
    /// The logical library name resolved by <see cref="MpvLibraryResolver"/>.
    /// </summary>
    public const string LibraryName = "golether-libmpv";

    /// <summary>
    /// <c>mpv_client_api_version</c>.
    /// </summary>
    /// <returns>The API version: major in the upper 16 bits.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_client_api_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial ulong ClientApiVersion();

    /// <summary>
    /// <c>mpv_create</c>.
    /// </summary>
    /// <returns>The handle, or zero on failure.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint Create();

    /// <summary>
    /// <c>mpv_initialize</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <returns>An error code.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_initialize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Initialize(nint handle);

    /// <summary>
    /// <c>mpv_terminate_destroy</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "mpv_terminate_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TerminateDestroy(nint handle);

    /// <summary>
    /// <c>mpv_set_option_string</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="name">The option name.</param>
    /// <param name="value">The value.</param>
    /// <returns>An error code.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SetOptionString(nint handle, string name, string value);

    /// <summary>
    /// <c>mpv_set_property_string</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value.</param>
    /// <returns>An error code.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SetPropertyString(nint handle, string name, string value);

    /// <summary>
    /// <c>mpv_set_property</c> with <see cref="MpvFormat.Double"/>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="name">The property name.</param>
    /// <param name="format">Must be <see cref="MpvFormat.Double"/>.</param>
    /// <param name="value">The value.</param>
    /// <returns>An error code.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SetPropertyDouble(nint handle, string name, MpvFormat format, ref double value);

    /// <summary>
    /// <c>mpv_set_property</c> with <see cref="MpvFormat.Flag"/>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="name">The property name.</param>
    /// <param name="format">Must be <see cref="MpvFormat.Flag"/>.</param>
    /// <param name="value">The value, 0 or 1.</param>
    /// <returns>An error code.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SetPropertyFlag(nint handle, string name, MpvFormat format, ref int value);

    /// <summary>
    /// <c>mpv_command</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="args">A null-terminated array of UTF-8 strings.</param>
    /// <returns>An error code.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_command")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Command(nint handle, byte** args);

    /// <summary>
    /// <c>mpv_observe_property</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="userData">The identifier reported in events.</param>
    /// <param name="name">The property name.</param>
    /// <param name="format">The value format.</param>
    /// <returns>An error code.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int ObserveProperty(nint handle, ulong userData, string name, MpvFormat format);

    /// <summary>
    /// <c>mpv_request_log_messages</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="minLevel">The minimal level, for example <c>warn</c>.</param>
    /// <returns>An error code.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_request_log_messages", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int RequestLogMessages(nint handle, string minLevel);

    /// <summary>
    /// <c>mpv_wait_event</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="timeout">The timeout in seconds; negative waits forever.</param>
    /// <returns>A pointer to the event, valid until the next call.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_wait_event")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial MpvEvent* WaitEvent(nint handle, double timeout);

    /// <summary>
    /// <c>mpv_wakeup</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    [LibraryImport(LibraryName, EntryPoint = "mpv_wakeup")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Wakeup(nint handle);

    /// <summary>
    /// <c>mpv_error_string</c>.
    /// </summary>
    /// <param name="error">The error code.</param>
    /// <returns>A static UTF-8 string.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_error_string")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ErrorString(int error);

    /// <summary>
    /// <c>mpv_stream_cb_add_ro</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="protocol">The protocol name without <c>://</c>.</param>
    /// <param name="userData">The user data passed to <paramref name="open"/>.</param>
    /// <param name="open"><c>int open(void *user_data, char *uri, mpv_stream_cb_info *info)</c>.</param>
    /// <returns>An error code.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_stream_cb_add_ro", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int StreamCbAddRo(nint handle, string protocol, nint userData, delegate* unmanaged[Cdecl]<nint, byte*, MpvStreamCallbacks*, int> open);

    /// <summary>
    /// Formats an error code.
    /// </summary>
    /// <param name="error">The error code.</param>
    /// <returns>The description.</returns>
    public static string Describe(int error) => $"{Marshal.PtrToStringUTF8(ErrorString(error))} ({error})";

    /// <summary>
    /// Throws when a call failed.
    /// </summary>
    /// <param name="error">The error code.</param>
    /// <param name="operation">The operation for the message.</param>
    /// <exception cref="MpvException">The code is negative.</exception>
    public static void Check(int error, string operation)
    {
        if (error < 0)
        {
            throw new MpvException($"libmpv: {operation} failed: {Describe(error)}");
        }
    }

    /// <summary>
    /// Runs <c>mpv_command</c> with string arguments.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="args">The command and its arguments.</param>
    /// <returns>An error code.</returns>
    public static int Command(nint handle, params string[] args)
    {
        var pointers = new nint[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            }

            fixed (nint* array = pointers)
            {
                return Command(handle, (byte**)array);
            }
        }
        finally
        {
            foreach (var pointer in pointers)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }
}

/// <summary>
/// A libmpv call failed.
/// </summary>
public sealed class MpvException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MpvException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public MpvException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Locates the libmpv shared library of the current platform.
/// </summary>
public static class MpvLibraryResolver
{
    /// <summary>
    /// The environment variable with an explicit library path.
    /// </summary>
    public const string LibraryPathVariable = "GOLETHER_LIBMPV";

    /// <summary>
    /// Guards registration.
    /// </summary>
    private static readonly Lock Gate = new();

    /// <summary>
    /// The loaded library handle.
    /// </summary>
    private static nint _handle;

    /// <summary>
    /// Whether the resolver has been registered.
    /// </summary>
    private static bool _registered;

    /// <summary>
    /// Gets or sets a library path found by the component locator; it is tried first.
    /// </summary>
    public static string? PreferredPath { get; set; }

    /// <summary>
    /// Gets the file names tried on the current platform.
    /// </summary>
    public static IReadOnlyList<string> CandidateNames
        => OperatingSystem.IsWindows() ? ["libmpv-2.dll", "mpv-2.dll", "mpv-1.dll"]
            : OperatingSystem.IsMacOS() ? ["libmpv.2.dylib", "libmpv.dylib"]
            : ["libmpv.so.2", "libmpv.so"];

    /// <summary>
    /// Loads libmpv.
    /// </summary>
    /// <param name="error">The reason when the library was not found.</param>
    /// <returns><see langword="true"/> when libmpv is available.</returns>
    public static bool TryLoad(out string? error)
    {
        lock (Gate)
        {
            error = null;
            if (_handle != 0)
            {
                return true;
            }

            foreach (var candidate in EnumerateCandidates())
            {
                if (NativeLibrary.TryLoad(candidate, out _handle))
                {
                    break;
                }
            }

            if (_handle == 0)
            {
                error = "Компонент видео (libmpv) не установлен.";
                return false;
            }

            if (!_registered)
            {
                NativeLibrary.SetDllImportResolver(typeof(LibMpv).Assembly, Resolve);
                _registered = true;
            }

            var major = LibMpv.ClientApiVersion() >> 16;
            if (major < 2)
            {
                error = $"Нужна libmpv с client API 2.x, найдена {major}.x.";
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Resolves the logical library name.
    /// </summary>
    /// <param name="name">The requested library name.</param>
    /// <param name="assembly">The requesting assembly.</param>
    /// <param name="searchPath">The search path.</param>
    /// <returns>The handle, or zero for other libraries.</returns>
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        => name == LibMpv.LibraryName ? _handle : 0;

    /// <summary>
    /// Lists the paths and names to try.
    /// </summary>
    /// <returns>The candidates in order.</returns>
    private static IEnumerable<string> EnumerateCandidates()
    {
        if (!string.IsNullOrWhiteSpace(PreferredPath))
        {
            yield return PreferredPath;
        }

        var explicitPath = Environment.GetEnvironmentVariable(LibraryPathVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return explicitPath;
        }

        var baseDir = AppContext.BaseDirectory;
        foreach (var name in CandidateNames)
        {
            yield return Path.Combine(baseDir, name);
            yield return Path.Combine(baseDir, "native", name);
        }

        if (OperatingSystem.IsMacOS())
        {
            foreach (var prefix in new[] { "/opt/homebrew/lib", "/usr/local/lib" })
            {
                yield return Path.Combine(prefix, "libmpv.dylib");
            }
        }

        foreach (var name in CandidateNames)
        {
            yield return name;
        }
    }
}
