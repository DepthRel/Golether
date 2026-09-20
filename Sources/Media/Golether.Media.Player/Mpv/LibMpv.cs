using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Golether.Media.Player.Mpv;

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
    /// <c>mpv_get_property_string</c>.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="name">The property name.</param>
    /// <returns>A UTF-8 string to release with <see cref="Free"/>, or zero when the property is unavailable.</returns>
    [LibraryImport(LibraryName, EntryPoint = "mpv_get_property_string", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint GetPropertyString(nint handle, string name);

    /// <summary>
    /// <c>mpv_free</c>.
    /// </summary>
    /// <param name="data">Memory returned by libmpv.</param>
    [LibraryImport(LibraryName, EntryPoint = "mpv_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Free(nint data);

    /// <summary>
    /// Reads a property as text.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or <see langword="null"/> when it is unavailable.</returns>
    public static string? GetString(nint handle, string name)
    {
        var value = GetPropertyString(handle, name);
        if (value == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(value);
        }
        finally
        {
            Free(value);
        }
    }

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
