using System.Runtime.InteropServices;
using Java.Interop;

namespace Golether.Media.Player.Android;

/// <summary>
/// Connects FFmpeg to the Java side of Android, without which its MediaCodec decoders do not start.
/// </summary>
public static partial class MediaCodecSupport
{
    /// <summary>
    /// The library of the FFmpeg decoders inside the package.
    /// </summary>
    private const string LIBRARY = "libavcodec.so";

    /// <summary>
    /// Hands the Java VM of the process to FFmpeg; call it once before the first player is created.
    /// </summary>
    /// <exception cref="InvalidOperationException">FFmpeg refused the VM.</exception>
    public static void Initialize()
    {
        var result = SetJavaVm(JniRuntime.CurrentRuntime.InvocationPointer, 0);

        if (result < 0)
        {
            throw new InvalidOperationException($"FFmpeg refused the Java VM, error {result}.");
        }
    }

    /// <summary>
    /// Stores the Java VM in FFmpeg.
    /// </summary>
    /// <param name="vm">The Java VM.</param>
    /// <param name="logContext">The logging context of FFmpeg, not used.</param>
    /// <returns>Zero or an error code of FFmpeg.</returns>
    [LibraryImport(LIBRARY, EntryPoint = "av_jni_set_java_vm")]
    private static partial int SetJavaVm(nint vm, nint logContext);
}
