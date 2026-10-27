using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Media.Player.Enums;
using Golether.Media.Streaming.Gallery;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// Decodes one frame with a separate, windowless libmpv instance that writes it as a JPEG (its "image" video
/// output). libmpv decodes everything the film player does, so the host can show HEIC, AVIF and TIFF photographs and
/// small pictures of videos; participants receive plain JPEG files and need nothing of it.
/// </summary>
public sealed class MpvFrameExtractor : IFrameExtractor
{
    /// <summary>
    /// The most frames decoded at once: every one is a player of its own.
    /// </summary>
    private const int PARALLELISM = 2;

    /// <summary>
    /// The quality of the written JPEG.
    /// </summary>
    private const string JPEG_QUALITY = "90";

    /// <summary>
    /// How long one wait for a player event lasts, in seconds, so a cancellation is noticed.
    /// </summary>
    private const double POLL_SECONDS = 0.25;

    /// <summary>
    /// How long one frame may take: a large TIFF or a seek deep into a long video.
    /// </summary>
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);


    /// <summary>
    /// Limits the frames decoded at once.
    /// </summary>
    private readonly SemaphoreSlim _slots = new(PARALLELISM);

    /// <summary>
    /// The time source of the timeout.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MpvFrameExtractor"/> class.
    /// </summary>
    /// <param name="time">The time source of the timeout; the system clock when <see langword="null"/>.</param>
    /// <param name="logger">The logger.</param>
    public MpvFrameExtractor(TimeProvider? time = null, ILogger<MpvFrameExtractor>? logger = null)
    {
        _time = time ?? TimeProvider.System;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public bool IsAvailable => MpvLibraryResolver.TryLoad(out _);

    /// <inheritdoc />
    public async Task<bool> ExtractAsync(string source, string target, double position, int maxSide, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSide);
        if (!double.IsFinite(position) || position is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (!File.Exists(source) || !IsAvailable)
        {
            return false;
        }

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var limit = new CancellationTokenSource(_timeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
            return await Task.Run(() => Extract(Path.GetFullPath(source), Path.GetFullPath(target), position, maxSide, linked.Token), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>
    /// Builds the options of the player that writes the frame.
    /// </summary>
    /// <param name="output">The folder the player writes into.</param>
    /// <param name="position">Where the frame is taken, from 0 to 1.</param>
    /// <param name="maxSide">The longest side, or 0 to keep the size.</param>
    /// <returns>The options in the order they are set.</returns>
    private static List<(string Name, string Value)> Options(string output, double position, int maxSide)
    {
        var options = new List<(string Name, string Value)>
        {
            ("terminal", "no"),
            ("config", "no"),
            ("idle", "yes"),
            ("keep-open", "no"),
            ("load-scripts", "no"),
            ("ytdl", "no"),
            ("osc", "no"),
            ("input-default-bindings", "no"),
            ("ao", "null"),
            ("aid", "no"),
            ("sid", "no"),
            ("sub-auto", "no"),
            ("audio-file-auto", "no"),
            ("hr-seek", "yes"),
            ("frames", "1"),
            ("vo", "image"),
            ("vo-image-format", "jpg"),
            ("vo-image-jpeg-quality", JPEG_QUALITY),
            ("vo-image-outdir", output),
        };

        if (position > 0)
        {
            options.Add(("start", (position * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%"));
        }

        if (maxSide > 0)
        {
            // Smaller only, never larger; the brackets keep the commas and quotes from mpv's own option syntax.
            var side = maxSide.ToString(CultureInfo.InvariantCulture);
            options.Add(("vf", $"lavfi=[scale=w='min(iw,{side})':h='min(ih,{side})':force_original_aspect_ratio=decrease]"));
        }

        return options;
    }

    /// <summary>
    /// Decodes the frame with a player of its own and moves the written JPEG to its place.
    /// </summary>
    /// <param name="source">The file.</param>
    /// <param name="target">The JPEG to write.</param>
    /// <param name="position">Where the frame is taken, from 0 to 1.</param>
    /// <param name="maxSide">The longest side, or 0 to keep the size.</param>
    /// <param name="cancellationToken">Cancelled on timeout or when the frame is not needed any more.</param>
    /// <returns><see langword="true"/> when the JPEG was written.</returns>
    private bool Extract(string source, string target, double position, int maxSide, CancellationToken cancellationToken)
    {
        var output = Path.Combine(Path.GetDirectoryName(target)!, "." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            return Decode(source, output, position, maxSide, cancellationToken) && Collect(output, target);
        }
        finally
        {
            try
            {
                Directory.Delete(output, recursive: true);
            }
            catch (IOException)
            {
                // A file still held: removed with the leftovers of the showing.
            }
        }
    }

    /// <summary>
    /// Runs a player of its own that writes the frame into a folder.
    /// </summary>
    /// <param name="source">The file.</param>
    /// <param name="output">The folder the player writes into.</param>
    /// <param name="position">Where the frame is taken, from 0 to 1.</param>
    /// <param name="maxSide">The longest side, or 0 to keep the size.</param>
    /// <param name="cancellationToken">Cancelled on timeout or when the frame is not needed any more.</param>
    /// <returns><see langword="true"/> when the player reached the end of the file; it is gone by then, so everything it
    /// wrote is written.</returns>
    private unsafe bool Decode(string source, string output, double position, int maxSide, CancellationToken cancellationToken)
    {
        var handle = LibMpv.Create();
        if (handle == 0)
        {
            return false;
        }

        try
        {
            foreach (var (name, value) in Options(output, position, maxSide))
            {
                var result = LibMpv.SetOptionString(handle, name, value);
                if (result < 0)
                {
                    _logger.LogWarning("The frame player refused {Option}: {Error}", name, LibMpv.Describe(result));
                    return false;
                }
            }

            if (LibMpv.Initialize(handle) < 0 || LibMpv.Command(handle, "loadfile", source) < 0)
            {
                return false;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var ev = LibMpv.WaitEvent(handle, POLL_SECONDS);
                if (ev->EventId == MpvEventId.Shutdown)
                {
                    return false;
                }

                if (ev->EventId == MpvEventId.EndFile)
                {
                    return true;
                }
            }

            _logger.LogInformation("No frame of {File} within the time", Path.GetFileName(source));
            return false;
        }
        finally
        {
            LibMpv.TerminateDestroy(handle);
        }
    }

    /// <summary>
    /// Moves the JPEG the player wrote to its place.
    /// </summary>
    /// <param name="output">The folder the player wrote into.</param>
    /// <param name="target">The place of the JPEG.</param>
    /// <returns><see langword="true"/> when there was a JPEG.</returns>
    private static bool Collect(string output, string target)
    {
        var written = Directory.EnumerateFiles(output, "*.jpg").Order(StringComparer.Ordinal).FirstOrDefault();
        if (written is null || new FileInfo(written).Length == 0)
        {
            return false;
        }

        File.Move(written, target, overwrite: true);
        return true;
    }
}
