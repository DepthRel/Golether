using Golether.Core.Identity;
using Golether.Sync.Protocol;

namespace Golether.UI.ViewModels;

/// <summary>
/// One stroke over the video: the points in the colour of the participant who drew it.
/// </summary>
public sealed class StrokeViewModel
{
    /// <summary>
    /// The points, as shares of the picture.
    /// </summary>
    private readonly List<StrokePoint> _points = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokeViewModel"/> class.
    /// </summary>
    /// <param name="id">The stroke identifier.</param>
    /// <param name="author">The participant who draws it.</param>
    /// <param name="color">The colour as <c>#RRGGBB</c>.</param>
    /// <param name="isLocal">Whether this device draws it.</param>
    /// <param name="startedAt">When it started.</param>
    public StrokeViewModel(string id, PeerId author, string color, bool isLocal, DateTimeOffset startedAt)
    {
        Id = id;
        Author = author;
        Color = color;
        IsLocal = isLocal;
        StartedAt = startedAt;
    }

    /// <summary>
    /// Gets the stroke identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the participant who draws it.
    /// </summary>
    public PeerId Author { get; }

    /// <summary>
    /// Gets the colour as <c>#RRGGBB</c>.
    /// </summary>
    public string Color { get; }

    /// <summary>
    /// Gets a value indicating whether this device draws it.
    /// </summary>
    public bool IsLocal { get; }

    /// <summary>
    /// Gets when the stroke started.
    /// </summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Gets or sets when the pen was lifted, or <see langword="null"/> while it is still drawn.
    /// </summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Gets or sets how strongly the stroke is drawn, 0–1.
    /// </summary>
    public double Opacity { get; set; } = 1;

    /// <summary>
    /// Gets the points of the stroke, as shares of the picture.
    /// </summary>
    public IReadOnlyList<StrokePoint> Points => _points;

    /// <summary>
    /// Adds received points.
    /// </summary>
    /// <param name="points">The points.</param>
    public void Append(IReadOnlyList<StrokePoint> points)
    {
        if (points is null || points.Count == 0)
        {
            return;
        }

        _points.AddRange(points);
        if (_points.Count > DrawingViewModel.MaxStrokePoints)
        {
            _points.RemoveRange(0, _points.Count - DrawingViewModel.MaxStrokePoints);
        }
    }
}
