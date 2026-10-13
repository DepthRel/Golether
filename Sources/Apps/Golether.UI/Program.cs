using Avalonia;
using Golether.Transports.PortMapping;
using Golether.Tunnels.AmneziaWG.Control;
using Golether.UI.Services;

namespace Golether.UI;

/// <summary>
/// Entry point of the Golether desktop application.
/// </summary>
public static class Program
{
    /// <summary>
    /// Starts the application.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        // The elevated tunnel helper changes one tunnel and exits without showing a window. It calls the same
        // AmneziaWG as the application itself: the one carried in the data folder when it is there.
        if (TunnelHelper.IsHelperInvocation(args))
        {
            return TunnelHelper.Run(args, new ProcessRunner(), ResolveAmneziaWg());
        }

        // The elevated firewall helper adds or removes the inbound rule of this copy and exits.
        if (WindowsFirewall.IsHelperInvocation(args))
        {
            return WindowsFirewall.Run(args);
        }

        // One copy per user: a later start (an invitation link clicked in the browser, a file opened with Golether)
        // hands its arguments over to the running copy and ends.
        using var instance = BecomeTheOnlyInstance(args, out var handedOver);
        if (handedOver)
        {
            return 0;
        }

        Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Gets the object that receives the arguments of later starts, or <see langword="null"/> when this copy could
    /// not become the only one and runs on its own.
    /// </summary>
    public static SingleInstance? Instance { get; private set; }

    /// <summary>
    /// Becomes the running copy or, when there is one already, gives it the arguments of this start.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="handedOver"><see langword="true"/> when the running copy took the arguments and this one must end.</param>
    /// <returns>The running copy, or <see langword="null"/>.</returns>
    private static SingleInstance? BecomeTheOnlyInstance(string[] args, out bool handedOver)
    {
        handedOver = false;
        try
        {
            var name = SingleInstance.GetName(Core.Configuration.AppDataPaths.Resolve().Root);
            if (SingleInstance.TryAcquire(name) is { } instance)
            {
                return instance;
            }

            // A running copy that does not answer (it hangs or is closing) must not keep the user out: this copy
            // then starts as usual.
            handedOver = SingleInstance.Forward(name, args);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException or PlatformNotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine($"Golether: the single-instance check failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Returns the AmneziaWG the tunnel helper must call: the copy in the data folder, or the one installed in the
    /// system when there is none.
    /// </summary>
    /// <returns>The path of <c>amneziawg.exe</c>.</returns>
    private static string ResolveAmneziaWg()
    {
        try
        {
            if (Services.AppServices.FindAmneziaWg(Core.Configuration.AppDataPaths.Resolve()) is { } carried)
            {
                return carried;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall back to the system installation below.
        }

        return new AwgCliOptions { ConfigDirectory = "." }.WindowsExecutable;
    }

    /// <summary>
    /// Configures Avalonia; also used by the designer.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
