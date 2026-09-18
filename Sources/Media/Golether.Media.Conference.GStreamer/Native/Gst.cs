using System.Runtime.InteropServices;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// <c>GValue</c>: a type tag and two 64-bit data slots. Must be zeroed before <c>g_value_init</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GValue
{
    /// <summary>
    /// The type of the value.
    /// </summary>
    public nuint GType;

    /// <summary>
    /// The first data slot.
    /// </summary>
    public ulong Data0;

    /// <summary>
    /// The second data slot.
    /// </summary>
    public ulong Data1;
}

/// <summary>
/// <c>GSignalQuery</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GSignalQuery
{
    /// <summary>
    /// The signal identifier.
    /// </summary>
    public uint SignalId;

    /// <summary>
    /// The signal name.
    /// </summary>
    public nint SignalName;

    /// <summary>
    /// The owner type.
    /// </summary>
    public nuint InstanceType;

    /// <summary>
    /// The signal flags.
    /// </summary>
    public int SignalFlags;

    /// <summary>
    /// The return type.
    /// </summary>
    public nuint ReturnType;

    /// <summary>
    /// The number of parameters.
    /// </summary>
    public uint ParameterCount;

    /// <summary>
    /// The parameter types.
    /// </summary>
    public nint ParameterTypes;
}

/// <summary>
/// <c>GError</c> (leading fields).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GError
{
    /// <summary>
    /// The error domain.
    /// </summary>
    public uint Domain;

    /// <summary>
    /// The error code.
    /// </summary>
    public int Code;

    /// <summary>
    /// The UTF-8 message.
    /// </summary>
    public nint Message;
}

/// <summary>
/// <c>GList</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GList
{
    /// <summary>
    /// The element.
    /// </summary>
    public nint Data;

    /// <summary>
    /// The next node.
    /// </summary>
    public nint Next;

    /// <summary>
    /// The previous node.
    /// </summary>
    public nint Previous;
}

/// <summary>
/// <c>GstMapInfo</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GstMapInfo
{
    /// <summary>
    /// The mapped memory object.
    /// </summary>
    public nint Memory;

    /// <summary>
    /// The map flags.
    /// </summary>
    public int Flags;

    /// <summary>
    /// A pointer to the data.
    /// </summary>
    public nint Data;

    /// <summary>
    /// The valid size.
    /// </summary>
    public nuint Size;

    /// <summary>
    /// The maximum size.
    /// </summary>
    public nuint MaxSize;

    /// <summary>
    /// User data (4 pointers).
    /// </summary>
    public nint UserData0;

    /// <summary>
    /// User data.
    /// </summary>
    public nint UserData1;

    /// <summary>
    /// User data.
    /// </summary>
    public nint UserData2;

    /// <summary>
    /// User data.
    /// </summary>
    public nint UserData3;

    /// <summary>
    /// Reserved (4 pointers).
    /// </summary>
    public nint Reserved0;

    /// <summary>
    /// Reserved.
    /// </summary>
    public nint Reserved1;

    /// <summary>
    /// Reserved.
    /// </summary>
    public nint Reserved2;

    /// <summary>
    /// Reserved.
    /// </summary>
    public nint Reserved3;
}

/// <summary>
/// <c>GstState</c>.
/// </summary>
internal enum GstState
{
    /// <summary>
    /// No pending state.
    /// </summary>
    VoidPending = 0,

    /// <summary>
    /// Resources released.
    /// </summary>
    Null = 1,

    /// <summary>
    /// Resources allocated.
    /// </summary>
    Ready = 2,

    /// <summary>
    /// Prerolled.
    /// </summary>
    Paused = 3,

    /// <summary>
    /// Running.
    /// </summary>
    Playing = 4,
}

/// <summary>
/// P/Invoke declarations of GLib, GObject and GStreamer (the subset Golether uses).
/// </summary>
internal static unsafe partial class Gst
{
    /// <summary>
    /// The logical name of GLib.
    /// </summary>
    public const string GLib = "golether-glib";

    /// <summary>
    /// The logical name of GObject.
    /// </summary>
    public const string GObject = "golether-gobject";

    /// <summary>
    /// The logical name of the GStreamer core.
    /// </summary>
    public const string Core = "golether-gstreamer";

    /// <summary>
    /// The logical name of the app library.
    /// </summary>
    public const string App = "golether-gstapp";

    /// <summary>
    /// The logical name of the SDP library.
    /// </summary>
    public const string Sdp = "golether-gstsdp";

    /// <summary>
    /// The logical name of the WebRTC library.
    /// </summary>
    public const string WebRtc = "golether-gstwebrtc";

    /// <summary>
    /// <c>GST_CLOCK_TIME_NONE</c>.
    /// </summary>
    public const ulong ClockTimeNone = ulong.MaxValue;

    /// <summary>
    /// <c>GST_MESSAGE_EOS</c>.
    /// </summary>
    public const int MessageEos = 1 << 0;

    /// <summary>
    /// <c>GST_MESSAGE_ERROR</c>.
    /// </summary>
    public const int MessageError = 1 << 1;

    /// <summary>
    /// <c>GST_MESSAGE_WARNING</c>.
    /// </summary>
    public const int MessageWarning = 1 << 2;

    /// <summary>
    /// The offset of <c>GstMessage.type</c> (after the 64-byte <c>GstMiniObject</c>).
    /// </summary>
    public const int MessageTypeOffset = 64;

    /// <summary>
    /// The offset of <c>GstBuffer.pts</c> (after <c>GstMiniObject</c> and the pool pointer).
    /// </summary>
    public const int BufferPtsOffset = 72;

    /// <summary>
    /// <c>G_TYPE_UINT</c>.
    /// </summary>
    public const nuint TypeUInt = 7 << 2;

    /// <summary>
    /// <c>G_TYPE_INT</c>.
    /// </summary>
    public const nuint TypeInt = 6 << 2;

    /// <summary>
    /// <c>G_TYPE_STRING</c>.
    /// </summary>
    public const nuint TypeString = 16 << 2;

    // ---- GLib ----

    /// <summary><c>g_free</c>.</summary>
    /// <param name="memory">The memory.</param>
    [LibraryImport(GLib, EntryPoint = "g_free")]
    public static partial void Free(nint memory);

    /// <summary><c>g_error_free</c>.</summary>
    /// <param name="error">The error.</param>
    [LibraryImport(GLib, EntryPoint = "g_error_free")]
    public static partial void ErrorFree(GError* error);

    /// <summary><c>g_list_free</c>.</summary>
    /// <param name="list">The list.</param>
    [LibraryImport(GLib, EntryPoint = "g_list_free")]
    public static partial void ListFree(GList* list);

    // ---- GObject ----

    /// <summary><c>g_object_unref</c>.</summary>
    /// <param name="instance">The object.</param>
    [LibraryImport(GObject, EntryPoint = "g_object_unref")]
    public static partial void ObjectUnref(nint instance);

    /// <summary><c>g_value_init</c>.</summary>
    /// <param name="value">The zeroed value.</param>
    /// <param name="type">The type.</param>
    /// <returns>The value.</returns>
    [LibraryImport(GObject, EntryPoint = "g_value_init")]
    public static partial GValue* ValueInit(GValue* value, nuint type);

    /// <summary><c>g_value_unset</c>.</summary>
    /// <param name="value">The value.</param>
    [LibraryImport(GObject, EntryPoint = "g_value_unset")]
    public static partial void ValueUnset(GValue* value);

    /// <summary><c>g_value_set_object</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="instance">The object.</param>
    [LibraryImport(GObject, EntryPoint = "g_value_set_object")]
    public static partial void ValueSetObject(GValue* value, nint instance);

    /// <summary><c>g_value_set_boxed</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="boxed">The boxed pointer (copied).</param>
    [LibraryImport(GObject, EntryPoint = "g_value_set_boxed")]
    public static partial void ValueSetBoxed(GValue* value, nint boxed);

    /// <summary><c>g_value_set_uint</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="number">The number.</param>
    [LibraryImport(GObject, EntryPoint = "g_value_set_uint")]
    public static partial void ValueSetUInt(GValue* value, uint number);

    /// <summary><c>g_value_set_string</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="text">The text (copied).</param>
    [LibraryImport(GObject, EntryPoint = "g_value_set_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void ValueSetString(GValue* value, string text);

    /// <summary><c>g_value_set_int</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="number">The number.</param>
    [LibraryImport(GObject, EntryPoint = "g_value_set_int")]
    public static partial void ValueSetInt(GValue* value, int number);

    /// <summary><c>g_value_dup_object</c> (returns a new reference).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The object or zero.</returns>
    [LibraryImport(GObject, EntryPoint = "g_value_dup_object")]
    public static partial nint ValueDupObject(GValue* value);

    /// <summary><c>g_value_get_uint64</c>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number.</returns>
    [LibraryImport(GObject, EntryPoint = "g_value_get_uint64")]
    public static partial ulong ValueGetUInt64(GValue* value);

    /// <summary><c>g_bytes_get_type</c>.</summary>
    /// <returns>The <c>GBytes</c> boxed type.</returns>
    [LibraryImport(GObject, EntryPoint = "g_bytes_get_type")]
    public static partial nuint BytesGetType();

    /// <summary><c>g_bytes_new</c> (copies the data).</summary>
    /// <param name="data">The data.</param>
    /// <param name="size">The size.</param>
    /// <returns>The bytes.</returns>
    [LibraryImport(GLib, EntryPoint = "g_bytes_new")]
    public static partial nint BytesNew(byte* data, nuint size);

    /// <summary><c>g_bytes_get_data</c>.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="size">Receives the size.</param>
    /// <returns>The data (owned by the bytes).</returns>
    [LibraryImport(GLib, EntryPoint = "g_bytes_get_data")]
    public static partial byte* BytesGetData(nint bytes, nuint* size);

    /// <summary><c>g_bytes_unref</c>.</summary>
    /// <param name="bytes">The bytes.</param>
    [LibraryImport(GLib, EntryPoint = "g_bytes_unref")]
    public static partial void BytesUnref(nint bytes);

    /// <summary><c>g_object_ref</c>.</summary>
    /// <param name="instance">The object.</param>
    /// <returns>The same object.</returns>
    [LibraryImport(GObject, EntryPoint = "g_object_ref")]
    public static partial nint ObjectRef(nint instance);

    /// <summary><c>g_value_get_enum</c>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The enum value.</returns>
    [LibraryImport(GObject, EntryPoint = "g_value_get_enum")]
    public static partial int ValueGetEnum(GValue* value);

    /// <summary><c>g_value_get_string</c> (the value keeps ownership).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The UTF-8 text or zero.</returns>
    [LibraryImport(GObject, EntryPoint = "g_value_get_string")]
    public static partial nint ValueGetString(GValue* value);

    /// <summary><c>g_object_get_property</c>; an all-zero value is initialized with the property type (GLib 2.60+).</summary>
    /// <param name="instance">The object.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value.</param>
    [LibraryImport(GObject, EntryPoint = "g_object_get_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void ObjectGetProperty(nint instance, string name, GValue* value);

    /// <summary><c>g_signal_query</c>.</summary>
    /// <param name="signalId">The signal.</param>
    /// <param name="query">The result.</param>
    [LibraryImport(GObject, EntryPoint = "g_signal_query")]
    public static partial void SignalQuery(uint signalId, GSignalQuery* query);

    /// <summary><c>g_value_get_boxed</c>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The boxed pointer (not owned).</returns>
    [LibraryImport(GObject, EntryPoint = "g_value_get_boxed")]
    public static partial nint ValueGetBoxed(GValue* value);

    /// <summary><c>g_signal_lookup</c>.</summary>
    /// <param name="name">The signal name.</param>
    /// <param name="type">The instance type.</param>
    /// <returns>The signal identifier, 0 when unknown.</returns>
    [LibraryImport(GObject, EntryPoint = "g_signal_lookup", StringMarshalling = StringMarshalling.Utf8)]
    public static partial uint SignalLookup(string name, nuint type);

    /// <summary><c>g_signal_emitv</c>.</summary>
    /// <param name="instanceAndParams">The instance followed by the parameters.</param>
    /// <param name="signalId">The signal.</param>
    /// <param name="detail">The detail quark.</param>
    /// <param name="returnValue">The return value or null.</param>
    [LibraryImport(GObject, EntryPoint = "g_signal_emitv")]
    public static partial void SignalEmitv(GValue* instanceAndParams, uint signalId, uint detail, GValue* returnValue);

    /// <summary><c>g_signal_connect_data</c>.</summary>
    /// <param name="instance">The object.</param>
    /// <param name="signal">The detailed signal name.</param>
    /// <param name="handler">The C callback.</param>
    /// <param name="data">The user data.</param>
    /// <param name="destroy">The destroy notify, or zero.</param>
    /// <param name="flags">The connect flags.</param>
    /// <returns>The handler identifier.</returns>
    [LibraryImport(GObject, EntryPoint = "g_signal_connect_data", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nuint SignalConnectData(nint instance, string signal, nint handler, nint data, nint destroy, int flags);

    /// <summary><c>g_signal_handler_disconnect</c>.</summary>
    /// <param name="instance">The object.</param>
    /// <param name="handlerId">The handler.</param>
    [LibraryImport(GObject, EntryPoint = "g_signal_handler_disconnect")]
    public static partial void SignalHandlerDisconnect(nint instance, nuint handlerId);

    // ---- GStreamer core ----

    /// <summary><c>gst_init_check</c>.</summary>
    /// <param name="argc">Null.</param>
    /// <param name="argv">Null.</param>
    /// <param name="error">Receives an error.</param>
    /// <returns>Non-zero on success.</returns>
    [LibraryImport(Core, EntryPoint = "gst_init_check")]
    public static partial int InitCheck(nint argc, nint argv, GError** error);

    /// <summary><c>gst_version_string</c>.</summary>
    /// <returns>A newly allocated string.</returns>
    [LibraryImport(Core, EntryPoint = "gst_version_string")]
    public static partial nint VersionString();

    /// <summary><c>gst_parse_launch</c>.</summary>
    /// <param name="description">The pipeline description.</param>
    /// <param name="error">Receives an error.</param>
    /// <returns>The pipeline (floating reference sunk by the caller's ownership).</returns>
    [LibraryImport(Core, EntryPoint = "gst_parse_launch", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint ParseLaunch(string description, GError** error);

    /// <summary><c>gst_parse_bin_from_description</c>.</summary>
    /// <param name="description">The bin description.</param>
    /// <param name="ghostUnlinkedPads">Non-zero to ghost unlinked pads.</param>
    /// <param name="error">Receives an error.</param>
    /// <returns>The bin.</returns>
    [LibraryImport(Core, EntryPoint = "gst_parse_bin_from_description", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint ParseBinFromDescription(string description, int ghostUnlinkedPads, GError** error);

    /// <summary><c>gst_object_unref</c>.</summary>
    /// <param name="instance">The object.</param>
    [LibraryImport(Core, EntryPoint = "gst_object_unref")]
    public static partial void GstObjectUnref(nint instance);

    /// <summary><c>gst_mini_object_unref</c>.</summary>
    /// <param name="instance">The mini object.</param>
    [LibraryImport(Core, EntryPoint = "gst_mini_object_unref")]
    public static partial void MiniObjectUnref(nint instance);

    /// <summary><c>gst_bin_get_by_name</c>.</summary>
    /// <param name="bin">The bin.</param>
    /// <param name="name">The element name.</param>
    /// <returns>A new reference, or zero.</returns>
    [LibraryImport(Core, EntryPoint = "gst_bin_get_by_name", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint BinGetByName(nint bin, string name);

    /// <summary><c>gst_bin_add</c>.</summary>
    /// <param name="bin">The bin.</param>
    /// <param name="element">The element (floating reference taken).</param>
    /// <returns>Non-zero on success.</returns>
    [LibraryImport(Core, EntryPoint = "gst_bin_add")]
    public static partial int BinAdd(nint bin, nint element);

    /// <summary><c>gst_element_set_state</c>.</summary>
    /// <param name="element">The element.</param>
    /// <param name="state">The state.</param>
    /// <returns>The state change result (0 is failure).</returns>
    [LibraryImport(Core, EntryPoint = "gst_element_set_state")]
    public static partial int ElementSetState(nint element, GstState state);

    /// <summary><c>gst_element_sync_state_with_parent</c>.</summary>
    /// <param name="element">The element.</param>
    /// <returns>Non-zero on success.</returns>
    [LibraryImport(Core, EntryPoint = "gst_element_sync_state_with_parent")]
    public static partial int ElementSyncStateWithParent(nint element);

    /// <summary><c>gst_element_get_static_pad</c>.</summary>
    /// <param name="element">The element.</param>
    /// <param name="name">The pad name.</param>
    /// <returns>A new reference, or zero.</returns>
    [LibraryImport(Core, EntryPoint = "gst_element_get_static_pad", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint ElementGetStaticPad(nint element, string name);

    /// <summary><c>gst_element_get_bus</c>.</summary>
    /// <param name="element">The pipeline.</param>
    /// <returns>A new reference.</returns>
    [LibraryImport(Core, EntryPoint = "gst_element_get_bus")]
    public static partial nint ElementGetBus(nint element);

    /// <summary><c>gst_pad_link</c>.</summary>
    /// <param name="source">The source pad.</param>
    /// <param name="sink">The sink pad.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(Core, EntryPoint = "gst_pad_link")]
    public static partial int PadLink(nint source, nint sink);

    /// <summary><c>gst_pad_query_caps</c>.</summary>
    /// <param name="pad">The pad.</param>
    /// <param name="filter">A filter or zero.</param>
    /// <returns>New caps.</returns>
    [LibraryImport(Core, EntryPoint = "gst_pad_query_caps")]
    public static partial nint PadQueryCaps(nint pad, nint filter);

    /// <summary><c>gst_caps_get_structure</c>.</summary>
    /// <param name="caps">The caps.</param>
    /// <param name="index">The index.</param>
    /// <returns>The structure (not owned).</returns>
    [LibraryImport(Core, EntryPoint = "gst_caps_get_structure")]
    public static partial nint CapsGetStructure(nint caps, uint index);

    /// <summary><c>gst_caps_to_string</c>.</summary>
    /// <param name="caps">The caps.</param>
    /// <returns>A newly allocated string.</returns>
    [LibraryImport(Core, EntryPoint = "gst_caps_to_string")]
    public static partial nint CapsToString(nint caps);

    /// <summary><c>gst_pad_get_current_caps</c>.</summary>
    /// <param name="pad">The pad.</param>
    /// <returns>New caps or zero.</returns>
    [LibraryImport(Core, EntryPoint = "gst_pad_get_current_caps")]
    public static partial nint PadGetCurrentCaps(nint pad);

    /// <summary><c>gst_caps_get_size</c>.</summary>
    /// <param name="caps">The caps.</param>
    /// <returns>The number of structures.</returns>
    [LibraryImport(Core, EntryPoint = "gst_caps_get_size")]
    public static partial uint CapsGetSize(nint caps);

    /// <summary><c>gst_structure_get_string</c>.</summary>
    /// <param name="structure">The structure.</param>
    /// <param name="field">The field.</param>
    /// <returns>The string (not owned) or zero.</returns>
    [LibraryImport(Core, EntryPoint = "gst_structure_get_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint StructureGetString(nint structure, string field);

    /// <summary><c>gst_structure_get_int</c>.</summary>
    /// <param name="structure">The structure.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns>Non-zero when found.</returns>
    [LibraryImport(Core, EntryPoint = "gst_structure_get_int", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int StructureGetInt(nint structure, string field, out int value);

    /// <summary><c>gst_structure_get_value</c>.</summary>
    /// <param name="structure">The structure.</param>
    /// <param name="field">The field.</param>
    /// <returns>The value (not owned) or null.</returns>
    [LibraryImport(Core, EntryPoint = "gst_structure_get_value", StringMarshalling = StringMarshalling.Utf8)]
    public static partial GValue* StructureGetValue(nint structure, string field);

    /// <summary><c>gst_structure_free</c>.</summary>
    /// <param name="structure">The structure.</param>
    [LibraryImport(Core, EntryPoint = "gst_structure_free")]
    public static partial void StructureFree(nint structure);

    /// <summary><c>gst_structure_get_type</c>.</summary>
    /// <returns>The boxed type.</returns>
    [LibraryImport(Core, EntryPoint = "gst_structure_get_type")]
    public static partial nuint StructureGetType();

    /// <summary><c>gst_structure_foreach</c>.</summary>
    /// <param name="structure">The structure.</param>
    /// <param name="callback"><c>gboolean callback(GQuark field, const GValue *value, gpointer data)</c>.</param>
    /// <param name="data">The user data.</param>
    /// <returns>Non-zero when the callback never stopped the iteration.</returns>
    [LibraryImport(Core, EntryPoint = "gst_structure_foreach")]
    public static partial int StructureForeach(nint structure, delegate* unmanaged[Cdecl]<uint, GValue*, nint, int> callback, nint data);

    /// <summary><c>gst_value_get_structure</c>.</summary>
    /// <param name="value">A value holding a structure.</param>
    /// <returns>The structure (not owned).</returns>
    [LibraryImport(Core, EntryPoint = "gst_value_get_structure")]
    public static partial nint ValueGetStructure(GValue* value);

    /// <summary><c>gst_structure_get_name</c>.</summary>
    /// <param name="structure">The structure.</param>
    /// <returns>The name (not owned).</returns>
    [LibraryImport(Core, EntryPoint = "gst_structure_get_name")]
    public static partial nint StructureGetName(nint structure);

    /// <summary><c>gst_structure_to_string</c>.</summary>
    /// <param name="structure">The structure.</param>
    /// <returns>A new string (release with <c>g_free</c>).</returns>
    [LibraryImport(Core, EntryPoint = "gst_structure_to_string")]
    public static partial nint StructureToString(nint structure);

    /// <summary><c>gst_structure_get_double</c>.</summary>
    /// <param name="structure">The structure.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns>Non-zero when found.</returns>
    [LibraryImport(Core, EntryPoint = "gst_structure_get_double", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int StructureGetDouble(nint structure, string field, out double value);

    /// <summary><c>gst_pad_get_type</c>.</summary>
    /// <returns>The pad type.</returns>
    [LibraryImport(Core, EntryPoint = "gst_pad_get_type")]
    public static partial nuint PadGetType();

    /// <summary><c>gst_util_set_object_arg</c>: sets a property from its string form.</summary>
    /// <param name="instance">The object.</param>
    /// <param name="name">The property.</param>
    /// <param name="value">The value.</param>
    [LibraryImport(Core, EntryPoint = "gst_util_set_object_arg", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void UtilSetObjectArg(nint instance, string name, string value);

    /// <summary><c>gst_bus_timed_pop_filtered</c>.</summary>
    /// <param name="bus">The bus.</param>
    /// <param name="timeout">The timeout in nanoseconds.</param>
    /// <param name="types">The message type mask.</param>
    /// <returns>A message or zero.</returns>
    [LibraryImport(Core, EntryPoint = "gst_bus_timed_pop_filtered")]
    public static partial nint BusTimedPopFiltered(nint bus, ulong timeout, int types);

    /// <summary><c>gst_message_parse_error</c>.</summary>
    /// <param name="message">The message.</param>
    /// <param name="error">Receives the error.</param>
    /// <param name="debug">Receives debug text.</param>
    [LibraryImport(Core, EntryPoint = "gst_message_parse_error")]
    public static partial void MessageParseError(nint message, GError** error, nint* debug);

    /// <summary><c>gst_message_parse_warning</c>.</summary>
    /// <param name="message">The message.</param>
    /// <param name="error">Receives the warning.</param>
    /// <param name="debug">Receives debug text.</param>
    [LibraryImport(Core, EntryPoint = "gst_message_parse_warning")]
    public static partial void MessageParseWarning(nint message, GError** error, nint* debug);

    /// <summary><c>gst_promise_new</c>.</summary>
    /// <returns>The promise.</returns>
    [LibraryImport(Core, EntryPoint = "gst_promise_new")]
    public static partial nint PromiseNew();

    /// <summary><c>gst_promise_wait</c>.</summary>
    /// <param name="promise">The promise.</param>
    /// <returns>The result: 2 means replied.</returns>
    [LibraryImport(Core, EntryPoint = "gst_promise_wait")]
    public static partial int PromiseWait(nint promise);

    /// <summary><c>gst_promise_interrupt</c>: ends a pending wait.</summary>
    /// <param name="promise">The promise.</param>
    [LibraryImport(Core, EntryPoint = "gst_promise_interrupt")]
    public static partial void PromiseInterrupt(nint promise);

    /// <summary><c>gst_promise_get_reply</c>.</summary>
    /// <param name="promise">The promise.</param>
    /// <returns>The reply (owned by the promise) or zero.</returns>
    [LibraryImport(Core, EntryPoint = "gst_promise_get_reply")]
    public static partial nint PromiseGetReply(nint promise);

    /// <summary><c>gst_promise_get_type</c>.</summary>
    /// <returns>The boxed type.</returns>
    [LibraryImport(Core, EntryPoint = "gst_promise_get_type")]
    public static partial nuint PromiseGetType();

    /// <summary><c>gst_sample_get_buffer</c>.</summary>
    /// <param name="sample">The sample.</param>
    /// <returns>The buffer (not owned).</returns>
    [LibraryImport(Core, EntryPoint = "gst_sample_get_buffer")]
    public static partial nint SampleGetBuffer(nint sample);

    /// <summary><c>gst_sample_get_caps</c>.</summary>
    /// <param name="sample">The sample.</param>
    /// <returns>The caps (not owned).</returns>
    [LibraryImport(Core, EntryPoint = "gst_sample_get_caps")]
    public static partial nint SampleGetCaps(nint sample);

    /// <summary><c>gst_buffer_map</c>.</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="info">Receives the mapping.</param>
    /// <param name="flags">1 for read.</param>
    /// <returns>Non-zero on success.</returns>
    [LibraryImport(Core, EntryPoint = "gst_buffer_map")]
    public static partial int BufferMap(nint buffer, GstMapInfo* info, int flags);

    /// <summary><c>gst_buffer_unmap</c>.</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="info">The mapping.</param>
    [LibraryImport(Core, EntryPoint = "gst_buffer_unmap")]
    public static partial void BufferUnmap(nint buffer, GstMapInfo* info);

    /// <summary><c>gst_buffer_new_allocate</c>.</summary>
    /// <param name="allocator">Zero for the default allocator.</param>
    /// <param name="size">The size.</param>
    /// <param name="parameters">Zero for defaults.</param>
    /// <returns>The buffer.</returns>
    [LibraryImport(Core, EntryPoint = "gst_buffer_new_allocate")]
    public static partial nint BufferNewAllocate(nint allocator, nuint size, nint parameters);

    /// <summary><c>gst_buffer_fill</c>.</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="source">The data.</param>
    /// <param name="size">The size.</param>
    /// <returns>The number of bytes copied.</returns>
    [LibraryImport(Core, EntryPoint = "gst_buffer_fill")]
    public static partial nuint BufferFill(nint buffer, nuint offset, byte* source, nuint size);

    /// <summary><c>gst_device_monitor_new</c>.</summary>
    /// <returns>The monitor.</returns>
    [LibraryImport(Core, EntryPoint = "gst_device_monitor_new")]
    public static partial nint DeviceMonitorNew();

    /// <summary><c>gst_device_monitor_add_filter</c>.</summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="classes">The device classes, for example <c>Video/Source</c>.</param>
    /// <param name="caps">Caps or zero.</param>
    /// <returns>The filter identifier.</returns>
    [LibraryImport(Core, EntryPoint = "gst_device_monitor_add_filter", StringMarshalling = StringMarshalling.Utf8)]
    public static partial uint DeviceMonitorAddFilter(nint monitor, string classes, nint caps);

    /// <summary><c>gst_device_monitor_get_devices</c>.</summary>
    /// <param name="monitor">The monitor.</param>
    /// <returns>A list of new device references.</returns>
    [LibraryImport(Core, EntryPoint = "gst_device_monitor_get_devices")]
    public static partial GList* DeviceMonitorGetDevices(nint monitor);

    /// <summary><c>gst_device_get_display_name</c>.</summary>
    /// <param name="device">The device.</param>
    /// <returns>A newly allocated string.</returns>
    [LibraryImport(Core, EntryPoint = "gst_device_get_display_name")]
    public static partial nint DeviceGetDisplayName(nint device);

    /// <summary><c>gst_device_get_properties</c>.</summary>
    /// <param name="device">The device.</param>
    /// <returns>A new structure or zero.</returns>
    [LibraryImport(Core, EntryPoint = "gst_device_get_properties")]
    public static partial nint DeviceGetProperties(nint device);

    // ---- app ----

    /// <summary><c>gst_app_sink_pull_sample</c>.</summary>
    /// <param name="sink">The appsink.</param>
    /// <returns>A new sample or zero.</returns>
    [LibraryImport(App, EntryPoint = "gst_app_sink_pull_sample")]
    public static partial nint AppSinkPullSample(nint sink);

    /// <summary><c>gst_app_src_push_buffer</c> (takes ownership of the buffer).</summary>
    /// <param name="source">The appsrc.</param>
    /// <param name="buffer">The buffer.</param>
    /// <returns>The flow result (0 is OK).</returns>
    [LibraryImport(App, EntryPoint = "gst_app_src_push_buffer")]
    public static partial int AppSrcPushBuffer(nint source, nint buffer);

    /// <summary><c>gst_app_src_set_caps</c>.</summary>
    /// <param name="source">The appsrc.</param>
    /// <param name="caps">The caps (not taken).</param>
    [LibraryImport(App, EntryPoint = "gst_app_src_set_caps")]
    public static partial void AppSrcSetCaps(nint source, nint caps);

    // ---- SDP and WebRTC ----

    /// <summary><c>gst_sdp_message_new_from_text</c>.</summary>
    /// <param name="text">The SDP text.</param>
    /// <param name="message">Receives the message.</param>
    /// <returns>0 on success.</returns>
    [LibraryImport(Sdp, EntryPoint = "gst_sdp_message_new_from_text", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SdpMessageNewFromText(string text, nint* message);

    /// <summary><c>gst_sdp_message_free</c>.</summary>
    /// <param name="message">The message.</param>
    [LibraryImport(Sdp, EntryPoint = "gst_sdp_message_free")]
    public static partial int SdpMessageFree(nint message);

    /// <summary><c>gst_sdp_message_as_text</c>.</summary>
    /// <param name="message">The message.</param>
    /// <returns>A newly allocated string.</returns>
    [LibraryImport(Sdp, EntryPoint = "gst_sdp_message_as_text")]
    public static partial nint SdpMessageAsText(nint message);

    /// <summary><c>gst_webrtc_session_description_new</c> (takes ownership of the SDP message).</summary>
    /// <param name="type">1 offer, 3 answer.</param>
    /// <param name="sdp">The SDP message.</param>
    /// <returns>The description.</returns>
    [LibraryImport(WebRtc, EntryPoint = "gst_webrtc_session_description_new")]
    public static partial nint SessionDescriptionNew(int type, nint sdp);

    /// <summary><c>gst_webrtc_session_description_free</c>.</summary>
    /// <param name="description">The description.</param>
    [LibraryImport(WebRtc, EntryPoint = "gst_webrtc_session_description_free")]
    public static partial void SessionDescriptionFree(nint description);

    /// <summary><c>gst_webrtc_session_description_get_type</c>.</summary>
    /// <returns>The boxed type.</returns>
    [LibraryImport(WebRtc, EntryPoint = "gst_webrtc_session_description_get_type")]
    public static partial nuint SessionDescriptionGetType();

    /// <summary>
    /// Converts a UTF-8 string owned by GLib and frees it.
    /// </summary>
    /// <param name="text">The string or zero.</param>
    /// <returns>The managed string.</returns>
    public static string TakeString(nint text)
    {
        if (text == 0)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUTF8(text) ?? string.Empty;
        }
        finally
        {
            Free(text);
        }
    }

    /// <summary>
    /// Converts and frees a <c>GError</c>.
    /// </summary>
    /// <param name="error">The error or null.</param>
    /// <returns>The message.</returns>
    public static string TakeError(GError* error)
    {
        if (error == null)
        {
            return "unknown error";
        }

        var message = Marshal.PtrToStringUTF8(error->Message) ?? "unknown error";
        ErrorFree(error);
        return message;
    }

    /// <summary>
    /// Returns the runtime type of a GObject instance (<c>G_TYPE_FROM_INSTANCE</c>).
    /// </summary>
    /// <param name="instance">The instance.</param>
    /// <returns>The type.</returns>
    public static nuint TypeOf(nint instance) => **(nuint**)instance;
}
