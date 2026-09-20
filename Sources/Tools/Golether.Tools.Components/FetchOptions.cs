using Golether.Components.Catalog;

namespace Golether.Tools.Components;

/// <summary>
/// Options of the <c>fetch</c> command.
/// </summary>
public sealed record FetchOptions
{
    /// <summary>
    /// Gets the runtime identifier.
    /// </summary>
    public string RuntimeIdentifier { get; init; } = ComponentCatalog.CurrentRuntimeIdentifier;

    /// <summary>
    /// Gets the output directory.
    /// </summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>
    /// Gets the cache directory.
    /// </summary>
    public string Cache { get; init; } = Path.Combine(Path.GetTempPath(), "golether-components");

    /// <summary>
    /// Gets the components.
    /// </summary>
    public IReadOnlyList<ComponentId> Components { get; init; } = [ComponentId.Video, ComponentId.Conference];

    /// <summary>
    /// Gets the parse error, or <see langword="null"/>.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Parses the arguments.
    /// </summary>
    /// <param name="args">The arguments after <c>fetch</c>.</param>
    /// <returns>The options; <see cref="Error"/> describes invalid input.</returns>
    public static FetchOptions Parse(IReadOnlyList<string> args)
    {
        var options = new FetchOptions();
        // Every option takes exactly one value.
        for (var i = 0; i < args.Count; i += 2)
        {
            var value = i + 1 < args.Count ? args[i + 1] : null;
            options = (args[i], value) switch
            {
                ("--rid", { } rid) => options with { RuntimeIdentifier = rid },
                ("--output", { } output) => options with { Output = Path.GetFullPath(output) },
                ("--cache", { } cache) => options with { Cache = Path.GetFullPath(cache) },
                ("--component", "video") => options with { Components = [ComponentId.Video] },
                ("--component", "conference") => options with { Components = [ComponentId.Conference] },
                ("--component", "all") => options,
                var (name, _) => options with { Error = $"unknown or incomplete argument '{name}'" },
            };

            if (options.Error is not null)
            {
                return options;
            }
        }

        return string.IsNullOrEmpty(options.Output) ? options with { Error = "--output is required" } : options;
    }
}
