using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.UI.Services;

/// <summary>
/// <see cref="ISoundPlayer"/> with what every system already has: <c>PlaySound</c> of Windows, which plays from memory,
/// and on Linux and macOS a player program of the sound system, which plays a file.
/// </summary>
public sealed partial class SystemSoundPlayer : ISoundPlayer, IDisposable
{
    /// <summary>
    /// <c>SND_ASYNC</c>: <c>PlaySound</c> returns at once.
    /// </summary>
    private const uint SND_ASYNC = 0x0001;

    /// <summary>
    /// <c>SND_NODEFAULT</c>: a sound that cannot be played is silence, not the default beep of the system.
    /// </summary>
    private const uint SND_NODEFAULT = 0x0002;

    /// <summary>
    /// <c>SND_MEMORY</c>: the sound is a WAV file in memory.
    /// </summary>
    private const uint SND_MEMORY = 0x0004;

    /// <summary>
    /// The players tried on Linux, in order: PipeWire, PulseAudio, ALSA.
    /// </summary>
    private static readonly string[] _linuxPlayers = ["pw-play", "paplay", "aplay"];

    /// <summary>
    /// Where the sounds are written for the player programs.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Guards the state below.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The files written for the sounds played so far, by the sound.
    /// </summary>
    private readonly Dictionary<byte[], string> _files = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The sound <c>PlaySound</c> may still be reading, pinned until another one replaces it.
    /// </summary>
    private GCHandle _playing;

    /// <summary>
    /// The player program found, or <see langword="null"/>.
    /// </summary>
    private string? _program;

    /// <summary>
    /// Whether the player program has been looked for.
    /// </summary>
    private bool _searched;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemSoundPlayer"/> class.
    /// </summary>
    /// <param name="directory">Where the sounds are written for the player programs of Linux and macOS.</param>
    /// <param name="logger">The logger.</param>
    public SystemSoundPlayer(string directory, ILogger<SystemSoundPlayer>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _logger = logger ?? NullLogger<SystemSoundPlayer>.Instance;
    }

    /// <inheritdoc />
    public void Play(byte[] wave)
    {
        ArgumentNullException.ThrowIfNull(wave);
        try
        {
            lock (_gate)
            {
                if (OperatingSystem.IsWindows())
                {
                    PlayFromMemory(wave);
                }
                else
                {
                    PlayWithProgram(wave);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            _logger.LogDebug("A sound was not played: {Reason}", ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_playing.IsAllocated)
            {
                // The memory is released only after the system stopped reading it.
                _ = PlaySound(0, 0, 0);
                _playing.Free();
            }
        }
    }

    /// <summary>
    /// Plays a sound with <c>PlaySound</c>. Call under <see cref="_gate"/>.
    /// </summary>
    /// <param name="wave">The WAV file.</param>
    private void PlayFromMemory(byte[] wave)
    {
        if (!_playing.IsAllocated || !ReferenceEquals(_playing.Target, wave))
        {
            if (_playing.IsAllocated)
            {
                _ = PlaySound(0, 0, 0);
                _playing.Free();
            }

            _playing = GCHandle.Alloc(wave, GCHandleType.Pinned);
        }

        if (PlaySound(_playing.AddrOfPinnedObject(), 0, SND_MEMORY | SND_ASYNC | SND_NODEFAULT) == 0)
        {
            _logger.LogDebug("The system did not play a sound");
        }
    }

    /// <summary>
    /// Plays a sound with the player program of the sound system. Call under <see cref="_gate"/>.
    /// </summary>
    /// <param name="wave">The WAV file.</param>
    private void PlayWithProgram(byte[] wave)
    {
        if (FindProgram() is not { } program)
        {
            return;
        }

        if (!_files.TryGetValue(wave, out var file))
        {
            Directory.CreateDirectory(_directory);
            file = Path.Combine(_directory, $"sound-{_files.Count}.wav");
            File.WriteAllBytes(file, wave);
            _files[wave] = file;
        }

        // The arguments go as a list, never through a shell; the program is left to finish by itself.
        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileName(program) == "aplay")
        {
            start.ArgumentList.Add("-q");
        }

        start.ArgumentList.Add(file);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.Exited += (_, _) => process.Dispose();
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Finds the player program once: <c>afplay</c> on macOS, the first of <see cref="_linuxPlayers"/> elsewhere.
    /// </summary>
    /// <returns>The full path of the program, or <see langword="null"/> when the system has none.</returns>
    private string? FindProgram()
    {
        if (_searched)
        {
            return _program;
        }

        _searched = true;
        string[] candidates = OperatingSystem.IsMacOS() ? ["afplay"] : _linuxPlayers;
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathFullyQualified);
        _program = candidates.SelectMany(name => directories.Select(directory => Path.Combine(directory, name))).FirstOrDefault(File.Exists);
        if (_program is null)
        {
            _logger.LogInformation("No program plays sounds here ({Programs}); notification sounds are off", string.Join(", ", candidates));
        }

        return _program;
    }

    /// <summary>
    /// <c>PlaySoundW</c> of <c>winmm.dll</c>.
    /// </summary>
    /// <param name="sound">The sound, or zero to stop the one playing.</param>
    /// <param name="module">The module of a resource sound; zero.</param>
    /// <param name="flags">The <c>SND_*</c> flags.</param>
    /// <returns>Nonzero on success.</returns>
    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static partial int PlaySound(nint sound, nint module, uint flags);
}
