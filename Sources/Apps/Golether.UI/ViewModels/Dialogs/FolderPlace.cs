namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// A place the folder browser opens at once: a folder of the user or a disk.
/// </summary>
/// <param name="Name">The name shown.</param>
/// <param name="Path">The full path.</param>
public sealed record FolderPlace(string Name, string Path);
