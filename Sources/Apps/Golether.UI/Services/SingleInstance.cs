using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Golether.UI.Services;

/// <summary>
/// Keeps one running copy of the application per user and data folder: a second start hands its arguments (an
/// invitation link, a file) over to the first one and ends.
/// </summary>
/// <remarks>
/// A named mutex tells who is first; a named pipe open to the current user only carries the arguments. What arrives
/// through the pipe is treated like anything typed by a stranger: it is limited in size and only ever interpreted as
/// an invitation or a path, by the same code that handles the command line.
/// </remarks>
public sealed partial class SingleInstance : IDisposable
{
    /// <summary>
    /// The largest message accepted, in bytes: an invitation is at most 4096 characters.
    /// </summary>
    public const int MAX_MESSAGE_BYTES = 32 * 1024;

    /// <summary>
    /// The largest number of arguments accepted in one message.
    /// </summary>
    public const int MAX_ARGUMENTS = 8;

    /// <summary>
    /// How long a second copy waits for the first one to take its arguments.
    /// </summary>
    private static readonly TimeSpan _forwardTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The name shared by the mutex and the pipe.
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// Held for as long as this copy is the running one.
    /// </summary>
    private readonly Mutex _mutex;

    /// <summary>
    /// Stops the listening loop.
    /// </summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// Guards <see cref="_handler"/> and <see cref="_pending"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The arguments that arrived before anybody was ready to take them (the window is still being prepared).
    /// </summary>
    private readonly List<string[]> _pending = [];

    /// <summary>
    /// Takes the arguments of later starts, or <see langword="null"/> until the window is ready.
    /// </summary>
    private Action<string[]>? _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstance"/> class.
    /// </summary>
    /// <param name="name">The name shared by the mutex and the pipe.</param>
    /// <param name="mutex">The mutex this copy owns.</param>
    private SingleInstance(string name, Mutex mutex)
    {
        _name = name;
        _mutex = mutex;
    }

    /// <summary>
    /// Builds the name of the mutex and the pipe: one per user and data folder, so a development copy and an
    /// installed one do not answer for each other.
    /// </summary>
    /// <param name="dataRoot">The data folder of this copy.</param>
    /// <returns>The name.</returns>
    public static string GetName(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var identity = Environment.UserName + "|" + Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)).ToUpperInvariant();
        return "Golether-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)), 0, 12);
    }

    /// <summary>
    /// Becomes the running copy, or finds out that another one already is.
    /// </summary>
    /// <param name="name">The name from <see cref="GetName"/>.</param>
    /// <returns>The running copy, listening for later starts; <see langword="null"/> when another copy runs.</returns>
    public static SingleInstance? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var mutex = new Mutex(initiallyOwned: true, name, out var first);
        if (!first)
        {
            mutex.Dispose();
            return null;
        }

        var instance = new SingleInstance(name, mutex);
        _ = Task.Run(() => instance.ListenAsync(instance._stopping.Token));
        return instance;
    }

    /// <summary>
    /// Hands the arguments of this start over to the running copy.
    /// </summary>
    /// <param name="name">The name from <see cref="GetName"/>.</param>
    /// <param name="arguments">The command-line arguments.</param>
    /// <returns><see langword="true"/> when the running copy took them.</returns>
    public static bool Forward(string name, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(_forwardTimeout);

            // The running copy is about to come to the front; Windows lets it only when the process the user has
            // just started allows it.
            if (OperatingSystem.IsWindows())
            {
                AllowSetForegroundWindow(ANY_PROCESS);
            }

            pipe.Write(JsonSerializer.SerializeToUtf8Bytes(arguments.Take(MAX_ARGUMENTS).ToArray()));
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Names who takes the arguments of later starts and hands over those that arrived meanwhile.
    /// </summary>
    /// <param name="handler">Takes the arguments; called on a background thread.</param>
    public void SetHandler(Action<string[]> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        string[][] waiting;
        lock (_gate)
        {
            _handler = handler;
            waiting = [.. _pending];
            _pending.Clear();
        }

        foreach (var arguments in waiting)
        {
            handler(arguments);
        }
    }

    /// <summary>
    /// Reads the arguments out of a message.
    /// </summary>
    /// <param name="message">The bytes that arrived.</param>
    /// <returns>The arguments, or <see langword="null"/> when the message is not a short list of strings.</returns>
    public static string[]? ParseMessage(ReadOnlySpan<byte> message)
    {
        if (message.Length is 0 or > MAX_MESSAGE_BYTES)
        {
            return null;
        }

        try
        {
            var arguments = JsonSerializer.Deserialize<string?[]>(message);
            return arguments is { Length: <= MAX_ARGUMENTS } && arguments.All(a => a is not null) ? [.. arguments.Select(a => a!)] : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The source itself is left to the collector: the loop may still be reading its token.
        _stopping.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread than the one that took it: the system frees it when the process ends.
        }

        _mutex.Dispose();
    }

    /// <summary>
    /// Accepts the later starts one by one.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the loop is stopped.</returns>
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _name,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                // A sender that connects and says nothing must not hold the pipe for ever.
                using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                reading.CancelAfter(_forwardTimeout);
                var buffer = new byte[MAX_MESSAGE_BYTES + 1];
                var read = await pipe.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, reading.Token).ConfigureAwait(false);
                if (ParseMessage(buffer.AsSpan(0, read)) is { } arguments)
                {
                    Deliver(arguments);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                // One broken connection is not a reason to stop listening; the pause keeps a failing pipe from
                // spinning the processor.
                await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Gives the arguments to the handler, or keeps them until there is one.
    /// </summary>
    /// <param name="arguments">The arguments of a later start.</param>
    private void Deliver(string[] arguments)
    {
        Action<string[]>? handler;
        lock (_gate)
        {
            handler = _handler;
            if (handler is null)
            {
                _pending.Add(arguments);
                return;
            }
        }

        handler(arguments);
    }

    /// <summary>
    /// The value of <c>ASFW_ANY</c>: any process may come to the front.
    /// </summary>
    private const int ANY_PROCESS = -1;

    /// <summary>
    /// Lets another process bring its window to the front.
    /// </summary>
    /// <param name="processId">The process, or <see cref="ANY_PROCESS"/>.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
