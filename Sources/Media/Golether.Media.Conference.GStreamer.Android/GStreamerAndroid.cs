using Android.Content;
using Android.Runtime;
using Android.Systems;
using Java.Lang;

namespace Golether.Media.Conference.GStreamer.Android;

/// <summary>
/// Starts GStreamer on Android: all of it is one library with the plugins inside, and it needs the application context.
/// </summary>
public static class GStreamerAndroid
{
    /// <summary>
    /// The library with GStreamer and its plugins.
    /// </summary>
    private const string LIBRARY = "gstreamer_android";

    /// <summary>
    /// The Java class that starts GStreamer; it comes with the library and has no C# binding.
    /// </summary>
    private const string JAVA_CLASS = "org/freedesktop/gstreamer/GStreamer";

    /// <summary>
    /// The JNI signature of its <c>init</c> method.
    /// </summary>
    private const string INIT_SIGNATURE = "(Landroid/content/Context;)V";

    /// <summary>
    /// Loads the library and initializes GStreamer; call it once before the first pipeline, on the main thread: from a
    /// thread of .NET without Java frames Android looks for the library among the system ones.
    /// </summary>
    /// <param name="context">The application context.</param>
    public static void Initialize(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // .NET sets LANG=en-US, which GLib takes for a character set. It then cannot word system errors, and libnice
        // stops the process on the first full socket. Only setenv reaches native code, Environment does not.
        Os.Setenv("CHARSET", "UTF-8", true);

        JavaSystem.LoadLibrary(LIBRARY);

        var javaClass = JNIEnv.FindClass(JAVA_CLASS);
        try
        {
            var init = JNIEnv.GetStaticMethodID(javaClass, "init", INIT_SIGNATURE);
            JNIEnv.CallStaticVoidMethod(javaClass, init, new JValue(context));
        }
        finally
        {
            JNIEnv.DeleteGlobalRef(javaClass);
        }
    }
}
