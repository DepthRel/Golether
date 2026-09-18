using Golether.Core.Identity;
using Golether.Session;
using Golether.Sync.Protocol;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using NSubstitute;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="DrawingViewModel"/>: the pen over the video.
/// </summary>
public sealed class DrawingViewModelTests
{
    /// <summary>
    /// This device.
    /// </summary>
    private static readonly PeerId Self = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// Another participant.
    /// </summary>
    private static readonly PeerId Guest = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// The session.
    /// </summary>
    private readonly ISessionService _session = Substitute.For<ISessionService>();

    /// <summary>
    /// The test clock.
    /// </summary>
    private readonly TestTime _time = new();

    /// <summary>
    /// The model.
    /// </summary>
    private readonly DrawingViewModel _drawing;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingViewModelTests"/> class.
    /// </summary>
    public DrawingViewModelTests()
    {
        var dispatcher = Substitute.For<IUiDispatcher>();
        dispatcher.When(d => d.Post(Arg.Any<Action>())).Do(call => call.Arg<Action>()());
        _drawing = new DrawingViewModel(_session, dispatcher, _time) { CanDraw = true };
    }

    /// <summary>
    /// The pen is picked up and put down by the same button, and only inside a session.
    /// </summary>
    [Fact]
    public void Pen_IsToggled()
    {
        _drawing.TogglePenCommand.Execute(null);
        Assert.True(_drawing.IsPenActive);
        _drawing.TogglePenCommand.Execute(null);
        Assert.False(_drawing.IsPenActive);

        _drawing.CanDraw = false;
        _drawing.TogglePenCommand.Execute(null);
        Assert.False(_drawing.IsPenActive);
    }

    /// <summary>
    /// Nothing is drawn or sent while the pen is put down.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task WithoutThePen_NothingIsSent()
    {
        _drawing.BeginStroke(0.5, 0.5);
        _drawing.ExtendStroke(0.6, 0.6);
        _drawing.EndStroke();

        await _session.DidNotReceiveWithAnyArgs().SendDrawAsync(default!, default, default!, TestContext.Current.CancellationToken);
        Assert.Empty(_drawing.Strokes);
    }

    /// <summary>
    /// A stroke of this device is sent as a start, a few pieces and an end; tiny moves of the cursor are left out.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LocalStroke_IsSentInPieces()
    {
        _drawing.IsPenActive = true;
        _drawing.BeginStroke(0.1, 0.1);
        await _session.Received(1).SendDrawAsync(Arg.Any<string>(), StrokePhase.Start, Arg.Is<IReadOnlyList<StrokePoint>>(p => p.Count == 1), Arg.Any<CancellationToken>());

        // A move of a fraction of a pixel is not worth a point.
        _drawing.ExtendStroke(0.1005, 0.1005);
        await _session.DidNotReceive().SendDrawAsync(Arg.Any<string>(), StrokePhase.Continue, Arg.Any<IReadOnlyList<StrokePoint>>(), Arg.Any<CancellationToken>());

        // The points are collected and sent together, not one message per move.
        _drawing.ExtendStroke(0.2, 0.2);
        _drawing.ExtendStroke(0.3, 0.3);
        await _session.DidNotReceive().SendDrawAsync(Arg.Any<string>(), StrokePhase.Continue, Arg.Any<IReadOnlyList<StrokePoint>>(), Arg.Any<CancellationToken>());

        _time.Advance(DrawingViewModel.SendInterval);
        _drawing.ExtendStroke(0.4, 0.4);
        await _session.Received(1).SendDrawAsync(Arg.Any<string>(), StrokePhase.Continue, Arg.Is<IReadOnlyList<StrokePoint>>(p => p.Count == 3), Arg.Any<CancellationToken>());

        _drawing.EndStroke();
        await _session.Received(1).SendDrawAsync(Arg.Any<string>(), StrokePhase.End, Arg.Any<IReadOnlyList<StrokePoint>>(), Arg.Any<CancellationToken>());

        // The same stroke identifier all the way, and a new one for the next stroke.
        var ids = _session.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ISessionService.SendDrawAsync))
            .Select(c => (string)c.GetArguments()[0]!)
            .Distinct();
        Assert.Single(ids);
    }

    /// <summary>
    /// Putting the pen down finishes the stroke being drawn, so it does not hang on everybody's screen.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task PuttingThePenDown_FinishesTheStroke()
    {
        _drawing.IsPenActive = true;
        _drawing.BeginStroke(0.1, 0.1);
        _drawing.IsPenActive = false;

        await _session.Received(1).SendDrawAsync(Arg.Any<string>(), StrokePhase.End, Arg.Any<IReadOnlyList<StrokePoint>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A received stroke is collected piece by piece and drawn in the colour of its author.
    /// </summary>
    [Fact]
    public void ReceivedStroke_IsCollected()
    {
        _drawing.Apply(new StrokeUpdate(Guest, "AB12", StrokePhase.Start, [new StrokePoint(0.1f, 0.1f)], false));
        _drawing.Apply(new StrokeUpdate(Guest, "AB12", StrokePhase.Continue, [new StrokePoint(0.2f, 0.2f), new StrokePoint(0.3f, 0.3f)], false));

        var stroke = Assert.Single(_drawing.Strokes);
        Assert.Equal(("AB12", Guest, false), (stroke.Id, stroke.Author, stroke.IsLocal));
        Assert.Equal(3, stroke.Points.Count);
        Assert.Null(stroke.EndedAt);
        Assert.True(_drawing.HasStrokes);

        // Every participant draws in their own colour, the one their avatar has.
        _drawing.Apply(new StrokeUpdate(Self, "CD34", StrokePhase.Start, [new StrokePoint(0.5f, 0.5f)], true));
        Assert.Equal(ParticipantColors.For(Guest), stroke.Color);
        Assert.Equal(ParticipantColors.For(Self), _drawing.Strokes[1].Color);
        Assert.True(_drawing.Strokes[1].IsLocal);

        // The end of a stroke nobody started is not worth a line.
        _drawing.Apply(new StrokeUpdate(Guest, "EF56", StrokePhase.End, [], false));
        Assert.Equal(2, _drawing.Strokes.Count);
    }

    /// <summary>
    /// A finished stroke is gone a second and a half after the button was let go, and it melts away instead of
    /// stepping down: the curve is gentle at both ends.
    /// </summary>
    [Fact]
    public void Fade_IsSmoothAndLastsOneAndAHalfSeconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1500), DrawingViewModel.StrokeHold + DrawingViewModel.StrokeFade);
        Assert.Equal(1, DrawingViewModel.Fade(TimeSpan.Zero));
        Assert.Equal(0, DrawingViewModel.Fade(TimeSpan.FromMilliseconds(1500)));
        Assert.Equal(0, DrawingViewModel.Fade(TimeSpan.FromSeconds(9)));

        // No jump at the start of the fading and none at its end: the first and last steps are the smallest.
        var step = DrawingViewModel.StrokeFade / 20;
        var values = Enumerable.Range(0, 21).Select(i => DrawingViewModel.Fade(DrawingViewModel.StrokeHold + (step * i))).ToArray();
        var drops = values.Zip(values.Skip(1), (a, b) => a - b).ToArray();
        Assert.All(drops, d => Assert.True(d >= 0, "Линия только гаснет, не разгорается."));
        Assert.True(drops[0] < drops[^ (drops.Length / 2)], "Начало затухания мягче середины.");
        Assert.True(drops[^1] < drops[drops.Length / 2], "Конец затухания мягче середины.");
    }

    /// <summary>
    /// A finished stroke holds for a moment, fades away and is then gone.
    /// </summary>
    [Fact]
    public void FinishedStroke_HoldsThenFades()
    {
        _drawing.Apply(new StrokeUpdate(Guest, "AB12", StrokePhase.Start, [new StrokePoint(0.1f, 0.1f)], false));
        _drawing.Apply(new StrokeUpdate(Guest, "AB12", StrokePhase.End, [new StrokePoint(0.2f, 0.2f)], false));
        var stroke = Assert.Single(_drawing.Strokes);
        Assert.Equal(1, stroke.Opacity);

        _time.Advance(DrawingViewModel.StrokeHold);
        _drawing.Tick();
        Assert.Equal(1, stroke.Opacity);

        _time.Advance(DrawingViewModel.StrokeFade / 2);
        _drawing.Tick();
        Assert.Equal(0.5, stroke.Opacity, 3);

        _time.Advance(DrawingViewModel.StrokeFade);
        _drawing.Tick();
        Assert.Empty(_drawing.Strokes);
        Assert.False(_drawing.HasStrokes);
    }

    /// <summary>
    /// A stroke nobody finishes (the author dropped out) is dropped after a while, while a stroke still being drawn
    /// stays at full strength.
    /// </summary>
    [Fact]
    public void UnfinishedStroke_IsNotFaded_ButIsDroppedEventually()
    {
        _drawing.Apply(new StrokeUpdate(Guest, "AB12", StrokePhase.Start, [new StrokePoint(0.1f, 0.1f)], false));
        _time.Advance(DrawingViewModel.StrokeHold + DrawingViewModel.StrokeFade);
        _drawing.Tick();
        Assert.Equal(1, Assert.Single(_drawing.Strokes).Opacity);

        _time.Advance(DrawingViewModel.AbandonedStroke);
        _drawing.Tick();
        Assert.Empty(_drawing.Strokes);
    }

    /// <summary>
    /// Only a limited number of strokes is kept, so a flood cannot fill the picture.
    /// </summary>
    [Fact]
    public void Strokes_AreLimited()
    {
        for (var i = 0; i < DrawingViewModel.MaxStrokes + 5; i++)
        {
            _drawing.Apply(new StrokeUpdate(Guest, $"AB{i:X4}", StrokePhase.Start, [new StrokePoint(0.1f, 0.1f)], false));
        }

        Assert.Equal(DrawingViewModel.MaxStrokes, _drawing.Strokes.Count);
        Assert.Equal("AB0005", _drawing.Strokes[0].Id);
    }

    /// <summary>
    /// The end of a session clears the picture and puts the pen down.
    /// </summary>
    [Fact]
    public void Reset_ClearsEverything()
    {
        _drawing.IsPenActive = true;
        _drawing.Apply(new StrokeUpdate(Guest, "AB12", StrokePhase.Start, [new StrokePoint(0.1f, 0.1f)], false));

        _drawing.Reset();

        Assert.Empty(_drawing.Strokes);
        Assert.False(_drawing.IsPenActive);

        // The identifier is forgotten too: the same one starts a new stroke.
        _drawing.Apply(new StrokeUpdate(Guest, "AB12", StrokePhase.Continue, [new StrokePoint(0.2f, 0.2f)], false));
        Assert.Single(Assert.Single(_drawing.Strokes).Points);
    }

    /// <summary>
    /// A time provider moved by the test.
    /// </summary>
    private sealed class TestTime : TimeProvider
    {
        /// <summary>
        /// The current time.
        /// </summary>
        private DateTimeOffset _now = new(2026, 9, 17, 20, 0, 0, TimeSpan.Zero);

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => _now;

        /// <summary>
        /// Moves the time forward.
        /// </summary>
        /// <param name="value">The step.</param>
        public void Advance(TimeSpan value) => _now += value;
    }
}
