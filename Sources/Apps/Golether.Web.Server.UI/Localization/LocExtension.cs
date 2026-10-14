using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Golether.Web.Server.UI.Localization;

/// <summary>
/// A text of the monitor in the current language: <c>{loc:Loc Key}</c>. It follows a change of the language at once.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocExtension"/> class.
    /// </summary>
    public LocExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocExtension"/> class.
    /// </summary>
    /// <param name="key">The key of the text.</param>
    public LocExtension(string key) => Key = key;

    /// <summary>
    /// Gets or sets the key of the text.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]") { Mode = BindingMode.OneWay, Source = LocalizationSource.Instance };
}
