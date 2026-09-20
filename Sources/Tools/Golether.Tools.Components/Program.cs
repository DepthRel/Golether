using Golether.Components.Catalog;
using Golether.Components.Installation;
using Golether.Core.Data.Enums;
using Microsoft.Extensions.Logging;

namespace Golether.Tools.Components;

/// <summary>
/// Entry point of <c>golether-components</c>.
/// </summary>
public static class Program
{
    /// <summary>
    /// The usage text.
    /// </summary>
    private const string Usage = """
        golether-components: prepares native components for packaging.

        Usage:
          golether-components fetch --output <dir> [--rid <win-x64|win-arm64>] [--cache <dir>] [--component video|conference|all]
          golether-components list

        fetch     downloads the pinned packages, verifies SHA-256 and writes the runtime files into <dir>:
                  video      -> <dir>/libmpv-2.dll
                  conference -> <dir>/gstreamer/...
                  Finished installations in the cache are reused.
        list      prints the catalog.
        """;

    /// <summary>
    /// Runs the tool.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns><c>0</c> on success, <c>1</c> on failure, <c>2</c> on a usage error.</returns>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        if (args[0] == "list")
        {
            foreach (var package in ComponentCatalog.Packages)
            {
                Console.WriteLine($"{package.Id,-11} {package.RuntimeIdentifier,-10} {package.Version,-10} {package.Size / (1024 * 1024),5} MB  {package.Url}");
            }

            return 0;
        }

        var options = args[0] == "fetch" ? FetchOptions.Parse(args.Skip(1).ToArray()) : new FetchOptions { Error = "unknown command" };
        if (options.Error is not null)
        {
            Console.Error.WriteLine($"error: {options.Error}");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Golether-Components/1.0");
        var installer = new ComponentInstaller(options.Cache, http, new ProcessToolRunner(), TimeProvider.System, loggerFactory.CreateLogger<ComponentInstaller>());
        try
        {
            foreach (var id in options.Components)
            {
                var package = ComponentCatalog.Find(id, options.RuntimeIdentifier)
                    ?? throw new ComponentInstallException($"The catalog has no {id} package for {options.RuntimeIdentifier}.");
                var installed = installer.GetInstallDirectory(package);
                if (!File.Exists(Path.Combine(installed, ComponentMarker.FileName)))
                {
                    Console.WriteLine($"==> {id} {package.Version} ({package.RuntimeIdentifier}): {package.Url}");
                    var lastDecile = -1;
                    installed = await installer.InstallAsync(package, new Progress<InstallProgress>(p =>
                    {
                        var decile = (int)(p.Fraction * 10);
                        if (decile != lastDecile)
                        {
                            lastDecile = decile;
                            Console.WriteLine($"    {p.Stage} {decile * 10}%");
                        }
                    }), CancellationToken.None);
                }

                CopyInto(id, installed, options.Output);
                Console.WriteLine($"    {id} -> {options.Output}");
            }

            return 0;
        }
        catch (ComponentInstallException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Copies the runtime files of an installed component into the output directory.
    /// </summary>
    /// <param name="id">The component.</param>
    /// <param name="installed">The installation directory.</param>
    /// <param name="output">The output directory.</param>
    private static void CopyInto(ComponentId id, string installed, string output)
    {
        Directory.CreateDirectory(output);
        if (id == ComponentId.Video)
        {
            File.Copy(Path.Combine(installed, "libmpv-2.dll"), Path.Combine(output, "libmpv-2.dll"), overwrite: true);
            return;
        }

        var source = Path.Combine(installed, "gstreamer");
        var target = Path.Combine(output, "gstreamer");
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
