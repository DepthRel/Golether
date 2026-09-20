using System.Runtime.InteropServices;

namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// Helpers for <c>GstSample</c> and <c>GstBuffer</c>.
/// </summary>
internal static unsafe class Samples
{
    /// <summary>
    /// <c>GST_FLOW_OK</c>.
    /// </summary>
    public const int FlowOk = 0;

    /// <summary>
    /// Pulls a sample from an appsink and gives the mapped data to a callback.
    /// </summary>
    /// <param name="appSink">The appsink.</param>
    /// <param name="consumer">Receives the data (valid only during the call) and the caps.</param>
    public static void Pull(nint appSink, SampleConsumer consumer)
    {
        var sample = Gst.AppSinkPullSample(appSink);
        if (sample == 0)
        {
            return;
        }

        try
        {
            var buffer = Gst.SampleGetBuffer(sample);
            GstMapInfo map;
            if (buffer == 0 || Gst.BufferMap(buffer, &map, 1) == 0)
            {
                return;
            }

            try
            {
                consumer(new ReadOnlySpan<byte>((byte*)map.Data, checked((int)map.Size)), Gst.SampleGetCaps(sample));
            }
            finally
            {
                Gst.BufferUnmap(buffer, &map);
            }
        }
        finally
        {
            Gst.MiniObjectUnref(sample);
        }
    }

    /// <summary>
    /// Pushes a copy of data into an appsrc; the copy has no timestamps, so the appsrc stamps it.
    /// </summary>
    /// <param name="appSource">The appsrc.</param>
    /// <param name="data">The data.</param>
    /// <returns><see langword="true"/> when the appsrc accepted the buffer.</returns>
    public static bool Push(nint appSource, ReadOnlySpan<byte> data)
    {
        var buffer = Gst.BufferNewAllocate(0, (nuint)data.Length, 0);
        if (buffer == 0)
        {
            return false;
        }

        fixed (byte* source = data)
        {
            Gst.BufferFill(buffer, 0, source, (nuint)data.Length);
        }

        // A fresh buffer has no timestamps (GST_CLOCK_TIME_NONE); do-timestamp on the appsrc stamps it.
        Marshal.WriteInt64(buffer, Gst.BufferPtsOffset, unchecked((long)Gst.ClockTimeNone));
        Marshal.WriteInt64(buffer, Gst.BufferPtsOffset + 8, unchecked((long)Gst.ClockTimeNone));
        return Gst.AppSrcPushBuffer(appSource, buffer) == FlowOk;
    }

    /// <summary>
    /// Reads the video size from caps.
    /// </summary>
    /// <param name="caps">The caps.</param>
    /// <returns>The width and height, zero when unknown.</returns>
    public static (int Width, int Height) VideoSize(nint caps)
    {
        if (caps == 0 || Gst.CapsGetSize(caps) == 0)
        {
            return (0, 0);
        }

        var structure = Gst.CapsGetStructure(caps, 0);
        Gst.StructureGetInt(structure, "width", out var width);
        Gst.StructureGetInt(structure, "height", out var height);
        return (width, height);
    }

    /// <summary>
    /// Reads a string field of the first caps structure.
    /// </summary>
    /// <param name="caps">The caps.</param>
    /// <param name="field">The field.</param>
    /// <returns>The value or an empty string.</returns>
    public static string CapsString(nint caps, string field)
    {
        if (caps == 0 || Gst.CapsGetSize(caps) == 0)
        {
            return string.Empty;
        }

        var value = Gst.StructureGetString(Gst.CapsGetStructure(caps, 0), field);
        return value == 0 ? string.Empty : Marshal.PtrToStringUTF8(value) ?? string.Empty;
    }
}
