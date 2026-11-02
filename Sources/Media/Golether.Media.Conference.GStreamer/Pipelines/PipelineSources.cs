namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// Chooses the elements of the running platform.
/// </summary>
internal static class PipelineSources
{
    /// <summary>
    /// Returns the elements of the running platform.
    /// </summary>
    /// <param name="useTestSources">Whether test patterns replace the camera and the microphone.</param>
    /// <returns>The elements.</returns>
    public static IPipelineSources ForCurrentPlatform(bool useTestSources)
    {
        IPipelineSources platform = OperatingSystem.IsWindows() ? new WindowsPipelineSources()
            : OperatingSystem.IsMacOS() ? new MacPipelineSources()
            : OperatingSystem.IsAndroid() ? new AndroidPipelineSources()
            : new LinuxPipelineSources();

        return useTestSources ? new TestPipelineSources(platform) : platform;
    }
}
