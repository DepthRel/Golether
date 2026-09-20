using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Golether.Core.Data.Enums;
using Golether.UI.Services;

namespace Golether.UI.Controls;

/// <summary>
/// The native child window libmpv renders into.
/// </summary>
/// <remarks>
/// <para>
/// Windows (HWND) and Linux X11 (XID) embed the video. On macOS libmpv opens its own window until the render API
/// backend is implemented. Avalonia controls cannot be drawn over this native area.
/// </para>
/// <para>
/// On Windows mpv creates its window disabled, so real mouse clicks reach this host window instead of mpv. The host
/// window is subclassed to report them through <see cref="VideoPointer"/>. Other systems report clicks through mpv.
/// </para>
/// </remarks>
public sealed partial class VideoHost : NativeControlHost
{
    /// <summary>
    /// <c>GWLP_WNDPROC</c>.
    /// </summary>
    private const int WindowProcIndex = -4;

    /// <summary>
    /// <c>WM_LBUTTONDOWN</c>.
    /// </summary>
    private const uint LeftButtonDown = 0x0201;

    /// <summary>
    /// <c>WM_LBUTTONDBLCLK</c>.
    /// </summary>
    private const uint LeftButtonDoubleClick = 0x0203;

    /// <summary>
    /// <c>WM_NCDESTROY</c>.
    /// </summary>
    private const uint NonClientDestroy = 0x0082;

    /// <summary>
    /// <c>SM_CXDOUBLECLK</c>.
    /// </summary>
    private const int DoubleClickWidthMetric = 36;

    /// <summary>
    /// <c>SM_CYDOUBLECLK</c>.
    /// </summary>
    private const int DoubleClickHeightMetric = 37;

    /// <summary>
    /// The hosts by subclassed window.
    /// </summary>
    private static readonly ConcurrentDictionary<nint, Subclass> Subclasses = new();

    /// <summary>
    /// Raised on the UI thread when the user clicks the video (Windows).
    /// </summary>
    public event EventHandler<VideoPointerAction>? VideoPointer;

    /// <summary>
    /// Gets or sets the player host to attach.
    /// </summary>
    public PlayerHost? Player { get; set; }

    /// <summary>
    /// Interprets a left button press: a second press close in time and place is a double click.
    /// </summary>
    /// <param name="previous">The previous press (time in milliseconds, position), or <see langword="null"/>.</param>
    /// <param name="time">The time of this press in milliseconds.</param>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <param name="doubleClickTime">The system double-click time in milliseconds.</param>
    /// <param name="width">The allowed horizontal distance (the whole rectangle width).</param>
    /// <param name="height">The allowed vertical distance (the whole rectangle height).</param>
    /// <returns>The action and the press to remember for the next one.</returns>
    public static (VideoPointerAction Action, (long Time, int X, int Y)? Remember) ClassifyPress(
        (long Time, int X, int Y)? previous, long time, int x, int y, int doubleClickTime, int width, int height)
    {
        if (previous is { } last && time >= last.Time && time - last.Time <= doubleClickTime
            && Math.Abs(x - last.X) <= width / 2 && Math.Abs(y - last.Y) <= height / 2)
        {
            return (VideoPointerAction.DoubleClick, null);
        }

        return (VideoPointerAction.Click, (time, x, y));
    }

    /// <inheritdoc />
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        if (OperatingSystem.IsWindows())
        {
            Subclass.Install(handle.Handle, this);
        }

        if (Player is { } player)
        {
            player.Attach(OperatingSystem.IsMacOS() ? 0 : handle.Handle);
        }

        return handle;
    }

    /// <inheritdoc />
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // libmpv must stop rendering before its window disappears.
        Player?.DetachAsync().GetAwaiter().GetResult();
        if (OperatingSystem.IsWindows())
        {
            Subclass.Remove(control.Handle);
        }

        base.DestroyNativeControlCore(control);
    }

    /// <summary>
    /// Raises <see cref="VideoPointer"/>.
    /// </summary>
    /// <param name="action">The action.</param>
    private void OnPointer(VideoPointerAction action) => VideoPointer?.Invoke(this, action);

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

    /// <summary><c>GetMessageTime</c>.</summary>
    /// <returns>The time of the current message in milliseconds.</returns>
    [LibraryImport("user32.dll")]
    private static partial int GetMessageTime();

    /// <summary><c>GetDoubleClickTime</c>.</summary>
    /// <returns>The double-click time in milliseconds.</returns>
    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    /// <summary><c>GetSystemMetrics</c>.</summary>
    /// <param name="index">The metric.</param>
    /// <returns>The value.</returns>
    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    /// <summary>
    /// A subclassed host window.
    /// </summary>
    /// <param name="Host">The control.</param>
    /// <param name="Previous">The original window procedure.</param>
    private sealed record Subclass(VideoHost Host, nint Previous)
    {
        /// <summary>
        /// Gets or sets the last press that may start a double click.
        /// </summary>
        public (long Time, int X, int Y)? LastPress { get; set; }

        /// <summary>
        /// Subclasses a window created on this (UI) thread.
        /// </summary>
        /// <param name="window">The window.</param>
        /// <param name="host">The control.</param>
        public static unsafe void Install(nint window, VideoHost host)
        {
            var procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProc;
            var previous = SetWindowLongPtr(window, WindowProcIndex, procedure);
            if (previous != 0)
            {
                Subclasses[window] = new Subclass(host, previous);
            }
        }

        /// <summary>
        /// Restores the original window procedure.
        /// </summary>
        /// <param name="window">The window.</param>
        public static void Remove(nint window)
        {
            if (Subclasses.TryRemove(window, out var subclass))
            {
                SetWindowLongPtr(window, WindowProcIndex, subclass.Previous);
            }
        }

        /// <summary>
        /// Reports left clicks and passes every message on.
        /// </summary>
        /// <param name="window">The window.</param>
        /// <param name="message">The message.</param>
        /// <param name="wParam">The first parameter.</param>
        /// <param name="lParam">The second parameter.</param>
        /// <returns>The result of the original procedure.</returns>
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static nint WindowProc(nint window, uint message, nint wParam, nint lParam)
        {
            if (!Subclasses.TryGetValue(window, out var subclass))
            {
                return 0;
            }

            if (message is LeftButtonDown or LeftButtonDoubleClick)
            {
                var x = (short)(lParam & 0xFFFF);
                var y = (short)((lParam >> 16) & 0xFFFF);
                var (action, remember) = ClassifyPress(
                    subclass.LastPress, (uint)GetMessageTime(), x, y, (int)GetDoubleClickTime(),
                    GetSystemMetrics(DoubleClickWidthMetric), GetSystemMetrics(DoubleClickHeightMetric));
                subclass.LastPress = remember;
                try
                {
                    subclass.Host.OnPointer(action);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    System.Diagnostics.Trace.WriteLine($"Golether video click handler failed: {ex}");
                }
            }

            var result = CallWindowProc(subclass.Previous, window, message, wParam, lParam);
            if (message == NonClientDestroy)
            {
                Subclasses.TryRemove(window, out _);
            }

            return result;
        }
    }
}
