namespace Golether.Core.Data.Enums;

/// <summary>
/// How the host treats participants that cannot keep up.
/// </summary>
public enum BufferingPolicy
{
    /// <summary>
    /// Pause everybody until lagging participants have buffered enough data.
    /// </summary>
    WaitForAll = 0,

    /// <summary>
    /// Keep playing; lagging participants catch up on their own.
    /// </summary>
    LetLaggardsCatchUp = 1,
}
