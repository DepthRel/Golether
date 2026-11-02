using Android.Content;
using Android.Media;
using Golether.Client.Services;

namespace Golether.Mobile.Android;

/// <summary>
/// <see cref="ISoundPlayer"/> on a <see cref="SoundPool"/>: a sound is read once and then plays at once. The sounds play
/// as media, as the film and the voices do, so the volume of media governs them and they go to the same headphones.
/// </summary>
/// <param name="context">The application context.</param>
internal sealed class AndroidSoundPlayer(Context context) : ISoundPlayer
{
    /// <summary>
    /// How many sounds may sound at once.
    /// </summary>
    private const int MAX_STREAMS = 2;

    /// <summary>
    /// Guards the state below: the sound may be asked for from any thread.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The numbers the pool gave the sounds, by the sound.
    /// </summary>
    private readonly Dictionary<byte[], int> _samples = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The numbers of the sounds the pool has read and can play.
    /// </summary>
    private readonly HashSet<int> _ready = [];

    /// <summary>
    /// The pool, once the first sound was asked for.
    /// </summary>
    private SoundPool? _pool;

    /// <inheritdoc />
    public void Play(byte[] wave)
    {
        ArgumentNullException.ThrowIfNull(wave);

        try
        {
            lock (_gate)
            {
                var pool = _pool ??= CreatePool();

                if (_samples.TryGetValue(wave, out var sample))
                {
                    if (_ready.Contains(sample))
                    {
                        pool.Play(sample, 1, 1, 1, 0, 1);
                    }

                    return;
                }

                // The pool reads files: the sound is written once into the cache, and played when it has been read.
                var path = Path.Combine(context.CacheDir!.AbsolutePath, $"sound-{_samples.Count}.wav");
                File.WriteAllBytes(path, wave);
                _samples[wave] = pool.Load(path, 1);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Java.Lang.Exception)
        {
            // A notification is never worth breaking anything for.
        }
    }

    /// <summary>
    /// Creates the pool of the sounds; a sound read for the first time plays at once, it was asked for.
    /// </summary>
    /// <returns>The pool.</returns>
    private SoundPool CreatePool()
    {
        var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Sonification)!.Build();
        var pool = new SoundPool.Builder().SetMaxStreams(MAX_STREAMS)!.SetAudioAttributes(attributes)!.Build()!;

        pool.LoadComplete += (_, e) =>
        {
            if (e.Status != 0)
            {
                return;
            }

            lock (_gate)
            {
                _ready.Add(e.SampleId);
            }

            pool.Play(e.SampleId, 1, 1, 1, 0, 1);
        };

        return pool;
    }
}
