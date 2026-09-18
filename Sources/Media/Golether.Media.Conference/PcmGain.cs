using System.Buffers.Binary;

namespace Golether.Media.Conference;

/// <summary>
/// Changes the loudness of 16-bit PCM: the local volume of a participant's voice.
/// </summary>
public static class PcmGain
{
    /// <summary>
    /// The highest allowed volume (200 %).
    /// </summary>
    public const double MaxVolume = 2.0;

    /// <summary>
    /// Brings a volume into the allowed range.
    /// </summary>
    /// <param name="volume">The volume, 1 for unchanged.</param>
    /// <returns>The volume between 0 and <see cref="MaxVolume"/>.</returns>
    public static double Clamp(double volume) => double.IsFinite(volume) ? Math.Clamp(volume, 0, MaxVolume) : 1;

    /// <summary>
    /// Scales signed 16-bit little-endian samples in place, saturating at the limits.
    /// </summary>
    /// <param name="pcm">The samples.</param>
    /// <param name="volume">The volume, 1 for unchanged.</param>
    public static void Apply(Span<byte> pcm, double volume)
    {
        volume = Clamp(volume);
        if (volume == 1)
        {
            return;
        }

        if (volume == 0)
        {
            pcm.Clear();
            return;
        }

        // Fixed point with 12 fractional bits keeps the loop free of floating point.
        var factor = (int)Math.Round(volume * 4096);
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(pcm[i..]);
            var scaled = (sample * factor) >> 12;
            BinaryPrimitives.WriteInt16LittleEndian(pcm[i..], (short)Math.Clamp(scaled, short.MinValue, short.MaxValue));
        }
    }
}
