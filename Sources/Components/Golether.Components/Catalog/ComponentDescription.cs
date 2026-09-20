namespace Golether.Components.Catalog;

/// <summary>
/// User-facing description of a component.
/// </summary>
/// <param name="Title">The short name.</param>
/// <param name="Purpose">What the component is needed for.</param>
/// <param name="License">The license of the upstream project.</param>
/// <param name="Source">The upstream project.</param>
public sealed record ComponentDescription(string Title, string Purpose, string License, string Source);
