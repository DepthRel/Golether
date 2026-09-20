using System.Runtime.InteropServices;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// Emits GObject action signals without varargs.
/// </summary>
internal static unsafe class Signals
{
    /// <summary>
    /// Emits an action signal with no return value.
    /// </summary>
    /// <param name="instance">The object.</param>
    /// <param name="name">The signal name.</param>
    /// <param name="args">The arguments.</param>
    /// <exception cref="GstException">The signal does not exist.</exception>
    public static void Emit(nint instance, string name, params SignalArg[] args) => EmitCore(instance, name, null, args);

    /// <summary>
    /// Emits an action signal that returns an object.
    /// </summary>
    /// <param name="instance">The object.</param>
    /// <param name="name">The signal name.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>A new reference to the returned object (release with <c>g_object_unref</c>), or zero.</returns>
    /// <exception cref="GstException">The signal does not exist.</exception>
    public static nint EmitForObject(nint instance, string name, params SignalArg[] args)
    {
        GValue result = default;
        EmitCore(instance, name, &result, args);
        try
        {
            return Gst.ValueDupObject(&result);
        }
        finally
        {
            Gst.ValueUnset(&result);
        }
    }

    /// <summary>
    /// Reads an object property.
    /// </summary>
    /// <param name="instance">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>A new reference (release with <c>g_object_unref</c>), or zero.</returns>
    public static nint GetObject(nint instance, string name)
    {
        GValue value = default;
        Gst.ObjectGetProperty(instance, name, &value);
        try
        {
            return Gst.ValueDupObject(&value);
        }
        finally
        {
            Gst.ValueUnset(&value);
        }
    }

    /// <summary>
    /// Reads an enum property.
    /// </summary>
    /// <param name="instance">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value.</returns>
    public static int GetEnum(nint instance, string name)
    {
        GValue value = default;
        Gst.ObjectGetProperty(instance, name, &value);
        try
        {
            return Gst.ValueGetEnum(&value);
        }
        finally
        {
            Gst.ValueUnset(&value);
        }
    }

    /// <summary>
    /// Reads a 64-bit unsigned property.
    /// </summary>
    /// <param name="instance">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value.</returns>
    public static ulong GetUInt64(nint instance, string name)
    {
        GValue value = default;
        Gst.ObjectGetProperty(instance, name, &value);
        try
        {
            return Gst.ValueGetUInt64(&value);
        }
        finally
        {
            Gst.ValueUnset(&value);
        }
    }

    /// <summary>
    /// Reads a string property.
    /// </summary>
    /// <param name="instance">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The text or <see langword="null"/>.</returns>
    public static string? GetString(nint instance, string name)
    {
        GValue value = default;
        Gst.ObjectGetProperty(instance, name, &value);
        try
        {
            var text = Gst.ValueGetString(&value);
            return text == 0 ? null : Marshal.PtrToStringUTF8(text);
        }
        finally
        {
            Gst.ValueUnset(&value);
        }
    }

    /// <summary>
    /// Emits an action signal.
    /// </summary>
    /// <param name="instance">The object.</param>
    /// <param name="name">The signal name.</param>
    /// <param name="result">An all-zero value for the return value, or null when the signal returns nothing.</param>
    /// <param name="args">The arguments.</param>
    /// <exception cref="GstException">The signal does not exist.</exception>
    private static void EmitCore(nint instance, string name, GValue* result, SignalArg[] args)
    {
        var type = Gst.TypeOf(instance);
        var id = Gst.SignalLookup(name, type);
        if (id == 0)
        {
            throw new GstException($"Signal '{name}' not found.");
        }

        var values = stackalloc GValue[args.Length + 1];
        new Span<GValue>(values, args.Length + 1).Clear();
        Gst.ValueInit(&values[0], type);
        Gst.ValueSetObject(&values[0], instance);
        for (var i = 0; i < args.Length; i++)
        {
            var value = &values[i + 1];
            Gst.ValueInit(value, args[i].Type);
            if (args[i].Type == Gst.TypeUInt)
            {
                Gst.ValueSetUInt(value, args[i].Number);
            }
            else if (args[i].Type == Gst.TypeInt)
            {
                Gst.ValueSetInt(value, unchecked((int)args[i].Number));
            }
            else if (args[i].IsObject)
            {
                Gst.ValueSetObject(value, args[i].Pointer);
            }
            else if (args[i].Type == Gst.TypeString)
            {
                Gst.ValueSetString(value, args[i].Text ?? string.Empty);
            }
            else
            {
                Gst.ValueSetBoxed(value, args[i].Pointer);
            }
        }

        if (result != null)
        {
            GSignalQuery query = default;
            Gst.SignalQuery(id, &query);
            Gst.ValueInit(result, query.ReturnType);
        }

        try
        {
            Gst.SignalEmitv(values, id, 0, result);
        }
        finally
        {
            for (var i = 0; i <= args.Length; i++)
            {
                Gst.ValueUnset(&values[i]);
            }
        }
    }
}
