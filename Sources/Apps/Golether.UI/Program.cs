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
        // The elevated tunnel helper changes one tunnel and exits without showing a window.
        if (TunnelHelper.IsHelperInvocation(args))
        {
            return TunnelHelper.Run(args, new ProcessRunner(), new AwgCliOptions { ConfigDirectory = "." }.WindowsExecutable);
        }

        // The elevated firewall helper adds or removes the inbound rule of this copy and exits.
        if (WindowsFirewall.IsHelperInvocation(args))
        {
            return WindowsFirewall.Run(args);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
