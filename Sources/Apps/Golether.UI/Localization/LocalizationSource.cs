using System.ComponentModel;
using Avalonia.Threading;
using Golether.Localization;

namespace Golether.UI.Localization;

/// <summary>
/// Exposes the texts of the current language to bindings. Bound through an indexer, so every text on the screen
/// refreshes at once when the user switches the language.
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    /// <summary>
    /// The name Avalonia looks for in the change notification of an indexer: the name of the indexer property.
    /// </summary>
    private const string IndexerName = "Item";

    /// <summary>
    /// The name XAML frameworks other than Avalonia use for a change of every indexer value.
    /// </summary>
    private const string IndexerBracketsName = "Item[]";

    /// <summary>
    /// The localizer whose texts are exposed.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationSource"/> class.
    /// </summary>
    /// <param name="localizer">The localizer.</param>
    public LocalizationSource(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _localizer.LanguageChanged += (_, _) =>
        {
            // The language can be set while the application starts, off the UI thread; bindings are told on it.
            if (Dispatcher.UIThread.CheckAccess())
            {
                Notify();
            }
            else
            {
                Dispatcher.UIThread.Post(Notify);
            }
        };
    }

    /// <summary>
    /// Tells the bindings that every text changed.
    /// </summary>
    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerBracketsName));
    }

    /// <summary>
    /// Gets the source of the application, over the <see cref="Texts.Localizer"/> in use when it is first asked for.
    /// </summary>
    public static LocalizationSource Instance { get; } = new(Texts.Localizer);

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets a text.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The text in the current language.</returns>
    public string this[string key] => _localizer.Get(key);
}
