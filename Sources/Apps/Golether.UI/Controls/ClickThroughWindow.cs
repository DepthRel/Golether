using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Golether.UI.Controls;

/// <summary>
/// Lets mouse input pass through a decorative native window (the reaction overlay above the video) on Windows.
/// </summary>
/// <remarks>
/// The window answers hit tests with <c>HTTRANSPARENT</c>, so clicks reach the video window below it (it belongs to
/// the same UI thread), and it never takes the activation.
/// </remarks>
public static partial class ClickThroughWindow
{
    /// <summary>
    /// <c>GWLP_WNDPROC</c>.
    /// </summary>
    private const int WindowProcIndex = -4;

    /// <summary>
    /// <c>WM_NCHITTEST</c>.
    /// </summary>
    private const uint HitTest = 0x0084;

    /// <summary>
    /// <c>WM_MOUSEACTIVATE</c>.
    /// </summary>
    private const uint MouseActivate = 0x0021;

    /// <summary>
    /// <c>WM_NCDESTROY</c>.
    /// </summary>
    private const uint NonClientDestroy = 0x0082;

    /// <summary>
    /// <c>HTTRANSPARENT</c>.
    /// </summary>
    private const nint Transparent = -1;

    /// <summary>
    /// <c>MA_NOACTIVATE</c>.
    /// </summary>
    private const nint NoActivate = 3;

    /// <summary>
    /// The original window procedures.
    /// </summary>
    private static readonly ConcurrentDictionary<nint, nint> Previous = new();

    /// <summary>
    /// Makes a window created on the UI thread transparent for the mouse. Does nothing on other systems or when the
    /// window is already handled.
    /// </summary>
    /// <param name="window">The native window handle.</param>
    /// <returns><see langword="true"/> when the window is (now) click-through.</returns>
    public static unsafe bool Apply(nint window)
    {
        if (!OperatingSystem.IsWindows() || window == 0)
        {
            return false;
        }

        if (Previous.ContainsKey(window))
        {
            return true;
        }

        var procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProc;
        var previous = SetWindowLongPtr(window, WindowProcIndex, procedure);
        if (previous == 0)
        {
            return false;
        }

        Previous[window] = previous;
        return true;
    }

    /// <summary>
    /// Answers the result of a message for a click-through window, or <see langword="null"/> to pass it on.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The result or <see langword="null"/>.</returns>
    public static nint? Intercept(uint message) => message switch
    {
        HitTest => Transparent,
        MouseActivate => NoActivate,
        _ => null,
    };

    /// <summary><c>SetWindowLongPtrW</c>.</summary>
    /// <param name="window">The window.</param>
    /// <param name="index">The value index.</param>
    /// <param name="value">The new value.</param>
    /// <returns>The previous value.</returns>
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint window, int index, nint value);

    /// <summary><c>CallWindowProcW</c>.</summary>
    /// <param name="previous">The original procedure.</param>
    /// <param name="window">The window.</param>
    /// <param name="message">The message.</param>
    /// <param name="wParam">The first parameter.</param>
    /// <param name="lParam">The second parameter.</param>
    /// <returns>The result.</returns>
    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static partial nint CallWindowProc(nint previous, nint window, uint message, nint wParam, nint lParam);

    /// <summary>
    /// The replacement window procedure.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="message">The message.</param>
    /// <param name="wParam">The first parameter.</param>
    /// <param name="lParam">The second parameter.</param>
    /// <returns>The result.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (!Previous.TryGetValue(window, out var previous))
        {
            return 0;
        }

        if (Intercept(message) is { } result)
        {
            return result;
        }

        var value = CallWindowProc(previous, window, message, wParam, lParam);
        if (message == NonClientDestroy)
        {
            Previous.TryRemove(window, out _);
        }

        return value;
    }
}
