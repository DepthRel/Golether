using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Session;
using Golether.Sync.Protocol;
using Golether.UI.Services;

namespace Golether.UI.ViewModels;

/// <summary>
/// The pen: the strokes drawn over the video by everybody in the session. A stroke follows the cursor while the
/// button is held, stays for a couple of seconds after it is let go and then fades away. Every participant draws in
/// their own colour, the same one their avatar has.
/// </summary>
public sealed partial class DrawingViewModel : ObservableObject
{
    /// <summary>
    /// How long a finished stroke stays at full strength before it starts to fade. A short hold only keeps the line
    /// from blinking out under the cursor that just drew it.
    /// </summary>
    public static readonly TimeSpan StrokeHold = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How long the fading itself takes. Together with the hold a finished stroke is gone 1.5 seconds after the
    /// button was let go.
    /// </summary>
    public static readonly TimeSpan StrokeFade = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// How long a stroke nobody finishes is kept, in case the author dropped out mid-stroke.
    /// </summary>
    public static readonly TimeSpan AbandonedStroke = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How often the points collected from the cursor are sent.
    /// </summary>
    public static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// The number of strokes kept at once.
    /// </summary>
    public const int MaxStrokes = 24;

    /// <summary>
    /// The number of points kept in one stroke.
    /// </summary>
    public const int MaxStrokePoints = 2048;

    /// <summary>
    /// The smallest move of the cursor that adds a point, as a share of the picture.
    /// </summary>
    private const float MinStep = 0.002f;

    /// <summary>
    /// The session service.
    /// </summary>
    private readonly ISessionService _session;

    /// <summary>
    /// The UI dispatcher.
    /// </summary>
    private readonly IUiDispatcher _dispatcher;

    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The strokes by identifier, so pieces find their stroke.
    /// </summary>
    private readonly Dictionary<string, StrokeViewModel> _byId = [];

    /// <summary>
    /// The points of the stroke being drawn here that were not sent yet.
    /// </summary>
    private readonly List<StrokePoint> _pending = [];

    /// <summary>
    /// The stroke being drawn here, or <see langword="null"/>.
    /// </summary>
    private string? _localStrokeId;

    /// <summary>
    /// The last point of the stroke being drawn here.
    /// </summary>
    private StrokePoint _lastLocalPoint;

    /// <summary>
    /// When the collected points were sent last.
    /// </summary>
    private DateTimeOffset _lastSent;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingViewModel"/> class.
    /// </summary>
    /// <param name="session">The session service.</param>
    /// <param name="dispatcher">The UI dispatcher.</param>
    /// <param name="timeProvider">The time source, or <see langword="null"/> for the system clock.</param>
    public DrawingViewModel(ISessionService session, IUiDispatcher dispatcher, TimeProvider? timeProvider = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _session.DrawReceived += (_, update) => _dispatcher.Post(() => Apply(update));
        Strokes.CollectionChanged += (_, _) => HasStrokes = Strokes.Count > 0;
    }

    /// <summary>
    /// Gets the strokes shown over the video, oldest first.
    /// </summary>
    public ObservableCollection<StrokeViewModel> Strokes { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the pen is picked up, so the cursor draws over the video.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPenActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the pen can be picked up at all: only inside a session.
    /// </summary>
    [ObservableProperty]
    public partial bool CanDraw { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether there is anything to draw, so the view can stay out of the way.
    /// </summary>
    [ObservableProperty]
    public partial bool HasStrokes { get; set; }

    /// <summary>
    /// Raised on the UI thread when a stroke changed, so the view repaints itself.
    /// </summary>
    public event EventHandler? StrokesChanged;

    /// <summary>
    /// Picks the pen up or puts it down.
    /// </summary>
    [RelayCommand]
    public void TogglePen()
    {
        if (IsPenActive)
        {
            IsPenActive = false;
            return;
        }

        if (CanDraw)
        {
            IsPenActive = true;
        }
    }

    /// <summary>
    /// Starts a stroke where the button went down.
    /// </summary>
    /// <param name="x">The horizontal share of the picture, 0–1.</param>
    /// <param name="y">The vertical share of the picture, 0–1.</param>
    public void BeginStroke(double x, double y)
    {
        if (!IsPenActive || !CanDraw)
        {
            return;
        }

        FinishLocalStroke();
        _localStrokeId = DrawMessage.CreateStrokeId();
        _lastLocalPoint = Clamp(x, y);
        _pending.Clear();
        _pending.Add(_lastLocalPoint);
        _lastSent = _timeProvider.GetUtcNow();
        Send(StrokePhase.Start);
    }

    /// <summary>
    /// Adds a point where the cursor moved to.
    /// </summary>
    /// <param name="x">The horizontal share of the picture, 0–1.</param>
    /// <param name="y">The vertical share of the picture, 0–1.</param>
    public void ExtendStroke(double x, double y)
    {
        if (_localStrokeId is null)
        {
            return;
        }

        var point = Clamp(x, y);
        if (Math.Abs(point.X - _lastLocalPoint.X) < MinStep && Math.Abs(point.Y - _lastLocalPoint.Y) < MinStep)
        {
            return;
        }

        _lastLocalPoint = point;
        _pending.Add(point);
        var now = _timeProvider.GetUtcNow();
        if (_pending.Count >= DrawMessage.MaxPoints || now - _lastSent >= SendInterval)
        {
            _lastSent = now;
            Send(StrokePhase.Continue);
        }
    }

    /// <summary>
    /// Finishes the stroke when the button is let go, or when the pen leaves the picture.
    /// </summary>
    public void EndStroke() => FinishLocalStroke();

    /// <summary>
    /// Fades the finished strokes and removes the faded ones.
    /// </summary>
    public void Tick()
    {
        if (Strokes.Count == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var changed = false;
        for (var i = Strokes.Count - 1; i >= 0; i--)
        {
            var stroke = Strokes[i];
            if (stroke.EndedAt is null)
            {
                if (now - stroke.StartedAt >= AbandonedStroke)
                {
                    Remove(i);
                    changed = true;
                }

                continue;
            }

            var age = now - stroke.EndedAt.Value;
            if (age >= StrokeHold + StrokeFade)
            {
                Remove(i);
                changed = true;
                continue;
            }

            var opacity = Fade(age);
            if (Math.Abs(stroke.Opacity - opacity) > 0.001)
            {
                stroke.Opacity = opacity;
                changed = true;
            }
        }

        if (changed)
        {
            StrokesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// How strongly a finished stroke is drawn at a given age. The curve starts and ends gently, so the line melts
    /// away instead of stepping down.
    /// </summary>
    /// <param name="age">The time since the pen was lifted.</param>
    /// <returns>The strength, 0–1.</returns>
    public static double Fade(TimeSpan age)
    {
        if (age <= StrokeHold)
        {
            return 1;
        }

        var passed = (age - StrokeHold) / StrokeFade;
        if (passed >= 1)
        {
            return 0;
        }

        // Smoothstep: no jump at the start of the fading and no jump at its end.
        return 1 - (passed * passed * (3 - (2 * passed)));
    }

    /// <summary>
    /// Drops everything when a session ends.
    /// </summary>
    public void Reset()
    {
        _localStrokeId = null;
        _pending.Clear();
        _byId.Clear();
        Strokes.Clear();
        IsPenActive = false;
        StrokesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows a received piece of a stroke, whoever drew it.
    /// </summary>
    /// <param name="update">The piece.</param>
    public void Apply(StrokeUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!_byId.TryGetValue(update.StrokeId, out var stroke))
        {
            if (update.Phase == StrokePhase.End)
            {
                return;
            }

            stroke = new StrokeViewModel(update.StrokeId, update.Sender, ParticipantColors.For(update.Sender), update.IsLocal, _timeProvider.GetUtcNow());
            while (Strokes.Count >= MaxStrokes)
            {
                Remove(0);
            }

            _byId[stroke.Id] = stroke;
            Strokes.Add(stroke);
        }

        stroke.Append(update.Points);
        if (update.Phase == StrokePhase.End)
        {
            stroke.EndedAt = _timeProvider.GetUtcNow();
        }

        StrokesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Forgets a stroke at a position.
    /// </summary>
    /// <param name="index">The position in <see cref="Strokes"/>.</param>
    private void Remove(int index)
    {
        _byId.Remove(Strokes[index].Id);
        Strokes.RemoveAt(index);
    }

    /// <summary>
    /// Sends the last piece of the stroke drawn here and forgets it.
    /// </summary>
    private void FinishLocalStroke()
    {
        if (_localStrokeId is null)
        {
            return;
        }

        Send(StrokePhase.End);
        _localStrokeId = null;
        _pending.Clear();
    }

    /// <summary>
    /// Sends the collected points.
    /// </summary>
    /// <param name="phase">Which part of the stroke this is.</param>
    private void Send(StrokePhase phase)
    {
        if (_localStrokeId is not { } strokeId)
        {
            return;
        }

        if (_pending.Count == 0 && phase != StrokePhase.End)
        {
            return;
        }

        var points = _pending.ToArray();
        _pending.Clear();
        _ = SendAsync(strokeId, phase, points);
    }

    /// <summary>
    /// Sends a piece and swallows the errors: a lost stroke is not worth a message.
    /// </summary>
    /// <param name="strokeId">The stroke.</param>
    /// <param name="phase">Which part of the stroke this is.</param>
    /// <param name="points">The points.</param>
    /// <returns>A task that completes when the piece was sent.</returns>
    private async Task SendAsync(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points)
    {
        try
        {
            await _session.SendDrawAsync(strokeId, phase, points, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // The connection is gone; the session state will say so.
        }
    }

    /// <summary>
    /// Brings a point into the picture.
    /// </summary>
    /// <param name="x">The horizontal share.</param>
    /// <param name="y">The vertical share.</param>
    /// <returns>The point.</returns>
    private static StrokePoint Clamp(double x, double y)
        => new((float)Math.Clamp(x, 0, 1), (float)Math.Clamp(y, 0, 1));

    /// <summary>
    /// Puts the pen down when the session no longer allows drawing.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnCanDrawChanged(bool value)
    {
        if (!value)
        {
            IsPenActive = false;
        }
    }

    /// <summary>
    /// Finishes the stroke being drawn when the pen is put down.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnIsPenActiveChanged(bool value)
    {
        if (!value)
        {
            FinishLocalStroke();
        }
    }
}
