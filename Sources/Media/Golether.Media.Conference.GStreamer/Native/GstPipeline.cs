using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Golether.Media.Conference.GStreamer.Native;

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
