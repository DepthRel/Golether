using System.Buffers.Binary;
using Golether.Core.Identity;

namespace Golether.Media.Conference;

/// <summary>
/// A change of the speaking state of a participant.
/// </summary>
/// <param name="PeerId">The participant, or <see langword="default"/> for this device.</param>
/// <param name="IsSpeaking">Whether the participant speaks now.</param>
public readonly record struct SpeakingChange(PeerId PeerId, bool IsSpeaking);

/// <summary>
/// Tells from audio levels who is speaking.
/// </summary>
/// <remarks>
/// A participant starts speaking when enough loud audio arrives within a short window, and stops after a hold time
/// without loud audio. The hold keeps the indicator steady between words. Thread-safe.
/// </remarks>
public sealed class VoiceActivityDetector
{
    /// <summary>
    /// The loudness above which audio counts as speech, in dBFS.
    /// </summary>
    private readonly double _thresholdDb;

    /// <summary>
    /// How long loud audio is needed before the indicator lights up.
    /// </summary>
    private readonly TimeSpan _attack;

    /// <summary>
    /// How long the indicator stays after the last loud audio.
    /// </summary>
    private readonly TimeSpan _hold;

    /// <summary>
    /// Guards <see cref="_states"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The state of each participant.
    /// </summary>
    private readonly Dictionary<PeerId, State> _states = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="VoiceActivityDetector"/> class.
    /// </summary>
    /// <param name="thresholdDb">The speech threshold in dBFS (−42 by default).</param>
    /// <param name="attack">The loud time needed to start (60 ms by default).</param>
    /// <param name="hold">The time the state lasts after the last loud audio (450 ms by default).</param>
    public VoiceActivityDetector(double thresholdDb = -42, TimeSpan? attack = null, TimeSpan? hold = null)
    {
        _thresholdDb = thresholdDb;
        _attack = attack ?? TimeSpan.FromMilliseconds(60);
        _hold = hold ?? TimeSpan.FromMilliseconds(450);
    }

    /// <summary>
    /// Computes the RMS level of 16-bit little-endian samples.
    /// </summary>
    /// <param name="pcm">The samples.</param>
    /// <returns>The level in dBFS; <see cref="double.NegativeInfinity"/> for silence.</returns>
    public static double LevelDb(ReadOnlySpan<byte> pcm)
    {
        var count = pcm.Length / 2;
        if (count == 0)
        {
            return double.NegativeInfinity;
        }

        double sum = 0;
        for (var i = 0; i < count; i++)
        {
            double sample = BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * 2)..]);
            sum += sample * sample;
        }

        var rms = Math.Sqrt(sum / count) / 32768.0;
        return rms > 0 ? 20 * Math.Log10(rms) : double.NegativeInfinity;
    }

    /// <summary>
    /// Processes a block of audio of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="pcm">16-bit little-endian samples.</param>
    /// <param name="duration">The duration of the block.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The new state when it changed, otherwise <see langword="null"/>.</returns>
    public SpeakingChange? Process(PeerId peer, ReadOnlySpan<byte> pcm, TimeSpan duration, TimeSpan now)
        => ProcessLevel(peer, LevelDb(pcm), duration, now);

    /// <summary>
    /// Processes a measured level of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="levelDb">The level in dBFS.</param>
    /// <param name="duration">The duration the level covers.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The new state when it changed, otherwise <see langword="null"/>.</returns>
    public SpeakingChange? ProcessLevel(PeerId peer, double levelDb, TimeSpan duration, TimeSpan now)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(peer, out var state))
            {
                state = new State();
                _states[peer] = state;
            }

            if (levelDb < _thresholdDb)
            {
                // Quiet audio breaks a starting phrase but keeps a running one until the hold ends.
                if (!state.Speaking)
                {
                    state.Loud = TimeSpan.Zero;
                }

                return null;
            }

            state.LastLoud = now;
            state.Loud += duration;
            if (state.Speaking || state.Loud < _attack)
            {
                return null;
            }

            state.Speaking = true;
            return new SpeakingChange(peer, true);
        }
    }

    /// <summary>
    /// Ends the speaking state of participants without loud audio for the hold time.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>The participants who stopped speaking.</returns>
    public IReadOnlyList<SpeakingChange> Expire(TimeSpan now)
    {
        lock (_gate)
        {
            var ended = new List<SpeakingChange>();
            foreach (var (peer, state) in _states)
            {
                if (state.Speaking && now - state.LastLoud >= _hold)
                {
                    state.Speaking = false;
                    state.Loud = TimeSpan.Zero;
                    ended.Add(new SpeakingChange(peer, false));
                }
            }

            return ended;
        }
    }

    /// <summary>
    /// Forgets a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <returns>A change to not speaking when the participant was speaking.</returns>
    public SpeakingChange? Forget(PeerId peer)
    {
        lock (_gate)
        {
            return _states.Remove(peer, out var state) && state.Speaking ? new SpeakingChange(peer, false) : null;
        }
    }

    /// <summary>
    /// The state of one participant.
    /// </summary>
    private sealed class State
    {
        /// <summary>
        /// Gets or sets a value indicating whether the participant speaks.
        /// </summary>
        public bool Speaking { get; set; }

        /// <summary>
        /// Gets or sets the loud time of the current phrase.
        /// </summary>
        public TimeSpan Loud { get; set; }

        /// <summary>
        /// Gets or sets the time of the last loud audio.
        /// </summary>
        public TimeSpan LastLoud { get; set; }
    }
}
