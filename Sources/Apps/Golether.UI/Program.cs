using Avalonia;
using Golether.Transports.PortMapping;
using Golether.Tunnels.AmneziaWG.Control;

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

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
