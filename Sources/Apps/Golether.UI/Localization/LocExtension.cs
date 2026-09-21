using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace Golether.UI.Localization;

/// <summary>
/// The markup extension that puts a localized text into XAML: <c>Text="{loc:Loc Main.Title}"</c>. With
/// <see cref="Arg"/> the text is a format string: <c>{loc:Loc Key=Main.Device, Arg={Binding Fingerprint}}</c>. The
/// text follows the language the user chose without reopening the window.
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

    /// <summary>
    /// Gets or sets the value put in place of <c>{0}</c>, or <see langword="null"/> when the text has no placeholder.
    /// </summary>
    public BindingBase? Arg { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var text = new Binding($"[{Key}]") { Mode = BindingMode.OneWay, Source = LocalizationSource.Instance };
        if (Arg is null)
        {
            return text;
        }

        return new MultiBinding
        {
            Bindings = [text, Arg],
            Converter = new FuncMultiValueConverter<object?, string>(values =>
            {
                var parts = values.ToArray();
                return parts is [string format, var value] ? string.Format(Golether.Localization.Texts.Localizer.Culture, format, value) : string.Empty;
            }),
        };
    }
}
