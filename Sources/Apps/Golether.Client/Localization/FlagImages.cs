using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Golether.Client.Localization;

/// <summary>
/// Finds the flag of a language: the image <c>Assets/Flags/&lt;language code&gt;.png</c> of an application. A new
/// language brings its flag as a file with its code and needs no code.
/// </summary>
public static class FlagImages
{
    /// <summary>
    /// Loads the flag of a language.
    /// </summary>
    /// <param name="application">The name of the assembly that holds the images.</param>
    /// <param name="code">The language code.</param>
    /// <returns>The flag, or <see langword="null"/> when the application has no image for the language.</returns>
    public static IImage? Load(string application, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        try
        {
            var uri = new Uri($"avares://{application}/Assets/Flags/{Uri.EscapeDataString(code)}.png");
            if (!AssetLoader.Exists(uri))
            {
                return null;
            }

            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
