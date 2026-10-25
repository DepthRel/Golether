using Avalonia;
using Golether.UI.Controls;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of the way <see cref="BorderChase"/> runs around a border.
/// </summary>
public sealed class BorderChaseTests
{
    /// <summary>
    /// A button-sized rectangle with rounded corners.
    /// </summary>
    private static readonly Rect _rect = new(1, 1, 40, 20);

    /// <summary>
    /// The radius of its corners.
    /// </summary>
    private const double RADIUS = 5;

    /// <summary>
    /// The way around is the straight sides and a whole circle of the corners.
    /// </summary>
    [Fact]
    public void Perimeter_AddsSidesAndCorners()
    {
        Assert.Equal(2 * 30 + 2 * 10 + 2 * Math.PI * 5, BorderChase.Perimeter(_rect, RADIUS), 6);
        Assert.Equal(120, BorderChase.Perimeter(_rect, 0), 6);
    }

    /// <summary>
    /// The way starts where the top-left corner ends and goes clockwise; it wraps around both ways.
    /// </summary>
    [Fact]
    public void PointAt_FollowsTheBorderClockwise()
    {
        var arc = Math.PI * RADIUS / 2;
        var perimeter = BorderChase.Perimeter(_rect, RADIUS);

        AssertPoint(new Point(6, 1), BorderChase.PointAt(_rect, RADIUS, 0));
        AssertPoint(new Point(21, 1), BorderChase.PointAt(_rect, RADIUS, 15));
        AssertPoint(new Point(36 + RADIUS * Math.Cos(-Math.PI / 4), 6 + RADIUS * Math.Sin(-Math.PI / 4)), BorderChase.PointAt(_rect, RADIUS, 30 + arc / 2));
        AssertPoint(new Point(41, 6), BorderChase.PointAt(_rect, RADIUS, 30 + arc));
        AssertPoint(new Point(41, 16), BorderChase.PointAt(_rect, RADIUS, 40 + arc));
        AssertPoint(new Point(36, 21), BorderChase.PointAt(_rect, RADIUS, 40 + 2 * arc));
        AssertPoint(new Point(6, 21), BorderChase.PointAt(_rect, RADIUS, 70 + 2 * arc));
        AssertPoint(new Point(1, 16), BorderChase.PointAt(_rect, RADIUS, 70 + 3 * arc));
        AssertPoint(new Point(1, 6), BorderChase.PointAt(_rect, RADIUS, 80 + 3 * arc));
        AssertPoint(new Point(6, 1), BorderChase.PointAt(_rect, RADIUS, perimeter));
        AssertPoint(new Point(1, 6), BorderChase.PointAt(_rect, RADIUS, -arc));
        AssertPoint(new Point(41, 1), BorderChase.PointAt(_rect, 0, 40));
    }

    /// <summary>
    /// The head moves by the share of a lap that passed and stays within one lap; no time or no lap keeps it.
    /// </summary>
    [Fact]
    public void Advance_MovesByTheTimeThatPassed()
    {
        var lap = TimeSpan.FromSeconds(3);

        Assert.Equal(0.5, BorderChase.Advance(0, TimeSpan.FromSeconds(1.5), lap), 6);
        Assert.Equal(0.1, BorderChase.Advance(0.9, TimeSpan.FromSeconds(0.6), lap), 6);
        Assert.Equal(0.25, BorderChase.Advance(0.25, TimeSpan.Zero, lap), 6);
        Assert.Equal(0.25, BorderChase.Advance(0.25, TimeSpan.FromSeconds(-1), lap), 6);
        Assert.Equal(0.25, BorderChase.Advance(0.25, TimeSpan.FromSeconds(1), TimeSpan.Zero), 6);
    }

    /// <summary>
    /// Checks two points are the same within rounding.
    /// </summary>
    /// <param name="expected">The expected point.</param>
    /// <param name="actual">The actual point.</param>
    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }
}
