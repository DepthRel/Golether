using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Golether.UI.ViewModels;

namespace Golether.UI.Controls;

/// <summary>
/// Moves the timeline slider smoothly: between position updates it keeps pace with playback, and seeks glide.
/// </summary>
public sealed class TimelineAnimator
{
    /// <summary>
    /// The clock of the animation.
    /// </summary>
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    /// The shown position in seconds.
    /// </summary>
    private readonly MotionSmoother _motion = new() { Minimum = 0 };

    /// <summary>
    /// The slider.
    /// </summary>
    private readonly Slider _slider;

    /// <summary>
    /// The view model.
    /// </summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>
    /// Whether an animation frame is requested.
    /// </summary>
    private bool _frameRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineAnimator"/> class.
    /// </summary>
    /// <param name="slider">The slider to move.</param>
    public TimelineAnimator(Slider slider)
    {
        _slider = slider;
    }

    /// <summary>
    /// Follows the position of a view model.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    public void Attach(MainWindowViewModel viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
        Retarget(jump: true);
    }

    /// <summary>
    /// Shows a position at once (the user released the slider there).
    /// </summary>
    /// <param name="seconds">The position.</param>
    public void JumpTo(double seconds)
    {
        _motion.SetTarget(seconds, Rate, _clock.Elapsed.TotalSeconds, jump: true);
        RequestFrame();
    }

    /// <summary>
    /// Gets the speed of the position in seconds per second.
    /// </summary>
    private double Rate => _viewModel is { IsAdvancing: true } ? 1 : 0;

    /// <summary>
    /// Updates the motion when the position, duration or state changes.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The change.</param>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.PositionSeconds)
            or nameof(MainWindowViewModel.IsPlaying)
            or nameof(MainWindowViewModel.IsAdvancing)
            or nameof(MainWindowViewModel.DurationSeconds))
        {
            Retarget(jump: false);
        }
    }

    /// <summary>
    /// Moves towards the position of the view model.
    /// </summary>
    /// <param name="jump">Whether to show it at once.</param>
    private void Retarget(bool jump)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        _motion.Maximum = viewModel.DurationSeconds;
        _motion.SetTarget(viewModel.PositionSeconds, Rate, _clock.Elapsed.TotalSeconds, jump);
        RequestFrame();
    }

    /// <summary>
    /// Shows the current value and asks for the next frame while the value moves.
    /// </summary>
    private void OnFrame()
    {
        _frameRequested = false;
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (!viewModel.IsScrubbing)
        {
            _slider.Value = _motion.ValueAt(now);
        }

        if (_motion.IsMoving(now))
        {
            RequestFrame();
        }
    }

    /// <summary>
    /// Asks for an animation frame.
    /// </summary>
    private void RequestFrame()
    {
        if (_frameRequested)
        {
            return;
        }

        if (TopLevel.GetTopLevel(_slider) is not { } top)
        {
            // Not shown yet: place the value once, frames start when the slider is on screen.
            if (_viewModel is { IsScrubbing: false })
            {
                _slider.Value = _motion.ValueAt(_clock.Elapsed.TotalSeconds);
            }

            return;
        }

        _frameRequested = true;
        top.RequestAnimationFrame(_ => OnFrame());
    }
}
