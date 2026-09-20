namespace Golether.UI.ViewModels;

/// <summary>
/// The severity of an indicator, mapped to a color by the view.
/// </summary>
public enum IndicatorLevel
{
    /// <summary>
    /// No value.
    /// </summary>
    Neutral = 0,

    /// <summary>
    /// Healthy.
    /// </summary>
    Good = 1,

    /// <summary>
    /// Needs attention.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Critical.
    /// </summary>
    Critical = 3,
}
