namespace Golether.UI.Services;

/// <summary>
/// The sound of a film clapperboard that tells about a new chat message: a short dry wooden clack built into the
/// program (<c>Assets/Sounds/clapperboard.wav</c>, made by the project itself).
/// </summary>
public static class ClapperboardSound
{
    /// <summary>
    /// The name of the built-in file.
    /// </summary>
    private const string RESOURCE_NAME = "Golether.UI.Sounds.clapperboard.wav";

    /// <summary>
    /// The length of the header of a WAV file in bytes.
    /// </summary>
    private const int HEADER_LENGTH = 44;

    /// <summary>
    /// Reads the sound out of the program.
    /// </summary>
    /// <returns>A complete WAV file.</returns>
    /// <exception cref="InvalidOperationException">The sound is not built into the program or is not a WAV file.</exception>
    public static byte[] Load()
    {
        using var stream = typeof(ClapperboardSound).Assembly.GetManifestResourceStream(RESOURCE_NAME)
            ?? throw new InvalidOperationException($"The sound {RESOURCE_NAME} is not built into the program.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var wave = copy.ToArray();

        // A broken build must not hand the sound system something it would read as a sound.
        if (wave.Length <= HEADER_LENGTH || !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wave.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidOperationException($"The sound {RESOURCE_NAME} is not a WAV file.");
        }

        return wave;
    }
}
