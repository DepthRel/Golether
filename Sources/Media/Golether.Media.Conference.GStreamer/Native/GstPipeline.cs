using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Golether.Media.Conference.GStreamer.Enums;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// A pipeline built from a description, with a bus reader thread and cached element references.
/// </summary>
internal sealed unsafe class GstPipeline : IDisposable
{
    /// <summary>
    /// The bus poll interval in nanoseconds.
    /// </summary>
    private const ulong POLL_NANOSECONDS = 100_000_000;

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
    /// Reads the system clock, the origin for <see cref="UseTimeline"/>.
    /// </summary>
    /// <returns>The time in nanoseconds.</returns>
    public static ulong SystemTime()
    {
        var clock = Gst.SystemClockObtain();
        try
        {
            return Gst.ClockGetTime(clock);
        }
        finally
        {
            Gst.GstObjectUnref(clock);
        }
    }

    /// <summary>
    /// Puts the pipeline on a timeline shared with other pipelines: the system clock, counted from the same moment.
    /// By default every pipeline counts time from its own start, and one with an audio device follows the clock of
    /// that device, so the timestamps of two pipelines cannot be compared. Call before <see cref="Play"/>.
    /// </summary>
    /// <param name="origin">The moment of the system clock the time is counted from (<see cref="SystemTime"/>).</param>
    public void UseTimeline(ulong origin)
    {
        var clock = Gst.SystemClockObtain();
        try
        {
            Gst.PipelineUseClock(Handle, clock);
        }
        finally
        {
            Gst.GstObjectUnref(clock);
        }

        // Without a start time the pipeline keeps the base time it is given instead of choosing its own.
        Gst.ElementSetStartTime(Handle, Gst.CLOCK_TIME_NONE);
        Gst.ElementSetBaseTime(Handle, origin);
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
    /// Stops the pipeline and reports it as failed, as an error on its bus would (tests).
    /// </summary>
    /// <param name="error">The error to report.</param>
    public void Break(string error)
    {
        Gst.ElementSetState(Handle, GstState.Null);
        Failed?.Invoke(this, error);
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
            var message = Gst.BusTimedPopFiltered(_bus, POLL_NANOSECONDS, Gst.MESSAGE_ERROR | Gst.MESSAGE_WARNING);
            if (message == 0)
            {
                continue;
            }

            try
            {
                var type = Marshal.ReadInt32(message, Gst.MESSAGE_TYPE_OFFSET);
                GError* error = null;
                nint debug = 0;
                if (type == Gst.MESSAGE_ERROR)
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
