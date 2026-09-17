using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// A GStreamer call failed.
/// </summary>
public sealed class GstException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GstException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public GstException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// An argument of an action signal.
/// </summary>
/// <param name="Type">The GType.</param>
/// <param name="Pointer">The boxed pointer (boxed types).</param>
/// <param name="Number">The number (<see cref="Gst.TypeUInt"/>).</param>
/// <param name="Text">The text (<see cref="Gst.TypeString"/>).</param>
internal readonly record struct SignalArg(nuint Type, nint Pointer, uint Number, string? Text)
{
    /// <summary>
    /// Creates a boxed argument.
    /// </summary>
    /// <param name="type">The boxed type.</param>
    /// <param name="pointer">The pointer (may be zero).</param>
    /// <returns>The argument.</returns>
    public static SignalArg Boxed(nuint type, nint pointer) => new(type, pointer, 0, null);

    /// <summary>
    /// Creates an unsigned integer argument.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <returns>The argument.</returns>
    public static SignalArg UInt(uint number) => new(Gst.TypeUInt, 0, number, null);

    /// <summary>
    /// Creates a signed integer argument.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <returns>The argument.</returns>
    public static SignalArg Int(int number) => new(Gst.TypeInt, 0, unchecked((uint)number), null);

    /// <summary>
    /// Creates a string argument.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The argument.</returns>
    public static SignalArg String(string text) => new(Gst.TypeString, 0, 0, text);
}

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

/// <summary>
/// A pipeline built from a description, with a bus reader thread and cached element references.
/// </summary>
internal sealed unsafe class GstPipeline : IDisposable
{
    /// <summary>
    /// The bus poll interval in nanoseconds.
    /// </summary>
    private const ulong PollNanoseconds = 100_000_000;

    /// <summary>
    /// Element references by name.
    /// </summary>
    private readonly Dictionary<string, nint> _elements = new(StringComparer.Ordinal);

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The bus.
    /// </summary>
    private readonly nint _bus;

    /// <summary>
    /// The bus reader.
    /// </summary>
    private readonly Thread _busThread;

    /// <summary>
    /// Whether the pipeline is disposed.
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GstPipeline"/> class.
    /// </summary>
    /// <param name="name">The name used in logs.</param>
    /// <param name="description">The <c>gst-launch</c> description.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="GstException">The description is invalid or an element is missing.</exception>
    public GstPipeline(string name, string description, ILogger logger)
    {
        Name = name;
        _logger = logger;
        GError* error = null;
        Handle = Gst.ParseLaunch(description, &error);
        if (error != null)
        {
            var message = Gst.TakeError(error);
            if (Handle != 0)
            {
                Gst.GstObjectUnref(Handle);
            }

            throw new GstException($"{name}: {message}");
        }

        if (Handle == 0)
        {
            throw new GstException($"{name}: the pipeline could not be created.");
        }

        _bus = Gst.ElementGetBus(Handle);
        _busThread = new Thread(ReadBus) { IsBackground = true, Name = $"gst bus {name}" };
        _busThread.Start();
    }

    /// <summary>
    /// Raised on the bus thread when the pipeline reports an error.
    /// </summary>
    public event EventHandler<string>? Failed;

    /// <summary>
    /// Gets the pipeline name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the pipeline handle.
    /// </summary>
    public nint Handle { get; }

    /// <summary>
    /// Returns a named element (the reference is kept until disposal).
    /// </summary>
    /// <param name="name">The element name.</param>
    /// <returns>The element.</returns>
    /// <exception cref="GstException">The element does not exist.</exception>
    public nint Element(string name)
    {
        lock (_elements)
        {
            if (!_elements.TryGetValue(name, out var element))
            {
                element = Gst.BinGetByName(Handle, name);
                if (element == 0)
                {
                    throw new GstException($"{Name}: no element '{name}'.");
                }

                _elements[name] = element;
            }

            return element;
        }
    }

    /// <summary>
    /// Sets the pipeline to playing.
    /// </summary>
    /// <exception cref="GstException">The state change failed.</exception>
    public void Play()
    {
        if (Gst.ElementSetState(Handle, GstState.Playing) == 0)
        {
            throw new GstException($"{Name}: the pipeline could not start (device busy or missing?).");
        }
    }

    /// <summary>
    /// Stops the pipeline and releases all references.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Gst.ElementSetState(Handle, GstState.Null);
        _busThread.Join(TimeSpan.FromSeconds(2));
        lock (_elements)
        {
            foreach (var element in _elements.Values)
            {
                Gst.GstObjectUnref(element);
            }

            _elements.Clear();
        }

        Gst.GstObjectUnref(_bus);
        Gst.GstObjectUnref(Handle);
    }

    /// <summary>
    /// Reads error and warning messages until disposal.
    /// </summary>
    private void ReadBus()
    {
        while (!_disposed)
        {
            var message = Gst.BusTimedPopFiltered(_bus, PollNanoseconds, Gst.MessageError | Gst.MessageWarning);
            if (message == 0)
            {
                continue;
            }

            try
            {
                var type = Marshal.ReadInt32(message, Gst.MessageTypeOffset);
                GError* error = null;
                nint debug = 0;
                if (type == Gst.MessageError)
                {
                    Gst.MessageParseError(message, &error, &debug);
                    var text = Gst.TakeError(error);
                    _logger.LogWarning("GStreamer {Pipeline}: {Error} ({Debug})", Name, text, Gst.TakeString(debug));
                    Failed?.Invoke(this, text);
                }
                else
                {
                    Gst.MessageParseWarning(message, &error, &debug);
                    _logger.LogDebug("GStreamer {Pipeline} warning: {Warning} ({Debug})", Name, Gst.TakeError(error), Gst.TakeString(debug));
                }
            }
            finally
            {
                Gst.MiniObjectUnref(message);
            }
        }
    }
}
