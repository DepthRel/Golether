using System.ComponentModel;
using Avalonia.Threading;
using Golether.Localization;

namespace Golether.Web.Server.UI.Localization;

/// <summary>
/// The texts of the monitor as a bindable source: <c>[Key]</c> gives the text in the current language, and every
/// binding is told when the language changes, so the window changes its language in place.
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    /// <summary>
    /// The name of the indexer for change notifications.
    /// </summary>
    private const string IndexerName = "Item";

    /// <summary>
    /// The name of the indexer with brackets, which some bindings listen to.
    /// </summary>
    private const string IndexerBracketsName = "Item[]";

    /// <summary>
    /// The localizer.
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

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the source over the localizer of the monitor.
    /// </summary>
    public static LocalizationSource Instance { get; } = new(Texts.Localizer);

    /// <summary>
    /// Gets a text in the current language.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The text.</returns>
    public string this[string key] => _localizer.Get(key);

    /// <summary>
    /// Tells the bindings that every text changed.
    /// </summary>
    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerBracketsName));
    }
}
