namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// Helpers for writing values into a pipeline description.
/// </summary>
internal static class PipelineText
{
    /// <summary>
    /// Quotes a value for a pipeline description.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The quoted value.</returns>
    public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
