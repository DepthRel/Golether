namespace Golether.Sync.Protocol;

/// <summary>
/// The part of a stroke a message carries.
/// </summary>
public enum StrokePhase
{
    /// <summary>
    /// The pen touched the picture: a new stroke starts.
    /// </summary>
    Start = 0,

    /// <summary>
    /// The pen moves: more points of the same stroke.
    /// </summary>
    Continue = 1,

    /// <summary>
    /// The pen was lifted: the stroke is finished and starts to fade.
    /// </summary>
    End = 2,
}
