namespace Golether.Transports.Relay;

/// <summary>
/// CRC-32 (IEEE 802.3), used by the STUN FINGERPRINT attribute.
/// </summary>
internal static class Crc32
{
    /// <summary>
    /// The lookup table of the reflected polynomial 0xEDB88320.
    /// </summary>
    private static readonly uint[] Table = BuildTable();

    /// <summary>
    /// Computes the checksum.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The CRC-32.</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    /// <summary>
    /// Builds the lookup table.
    /// </summary>
    /// <returns>The table.</returns>
    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var i = 0u; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
