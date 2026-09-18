using System.Buffers.Binary;

namespace Golether.Media.Conference.GStreamer.Tests;

/// <summary>
/// Tests of <see cref="PcmGain"/>.
/// </summary>
public sealed class PcmGainTests
{
    /// <summary>
    /// Samples are scaled, saturate at the limits, and silence clears them.
    /// </summary>
    [Fact]
    public void Apply_ScalesAndSaturates()
    {
        short[] input = [1000, -1000, 20000, -20000, 0];
        Assert.Equal([500, -500, 10000, -10000, 0], Scale(input, 0.5));
        Assert.Equal([2000, -2000, short.MaxValue, short.MinValue, 0], Scale(input, 2));
        Assert.Equal(input, Scale(input, 1));
        Assert.Equal([0, 0, 0, 0, 0], Scale(input, 0));
        Assert.Equal([2000, -2000, short.MaxValue, short.MinValue, 0], Scale(input, 7));
        Assert.Equal(input, Scale(input, double.NaN));
    }

    /// <summary>
    /// An odd trailing byte is left alone.
    /// </summary>
    [Fact]
    public void Apply_IgnoresTrailingByte()
    {
        byte[] data = [0xE8, 0x03, 0x7F];
        PcmGain.Apply(data, 0.5);
        Assert.Equal([0xF4, 0x01, 0x7F], data);
    }

    /// <summary>
    /// Scales a copy of the samples.
    /// </summary>
    /// <param name="samples">The samples.</param>
    /// <param name="volume">The volume.</param>
    /// <returns>The scaled samples.</returns>
    private static short[] Scale(short[] samples, double volume)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        }

        PcmGain.Apply(bytes, volume);
        return Enumerable.Range(0, samples.Length).Select(i => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2))).ToArray();
    }
}
