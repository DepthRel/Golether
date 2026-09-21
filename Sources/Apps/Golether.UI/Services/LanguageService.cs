using System.Globalization;
using Golether.Core.Data.Stores;
using Golether.Localization;

namespace Golether.UI.Services;

/// <summary>
/// Keeps the language of the user interface in the settings of the database.
/// </summary>
public sealed class LanguageService : ILanguageService
{
    /// <summary>
    /// The setting with the language code.
    /// </summary>
    public const string Setting = "ui.language";

    /// <summary>
    /// The localizer that switches the texts.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings;

    /// <summary>
    /// Returns the culture of the operating system.
    /// </summary>
    private readonly Func<CultureInfo> _systemCulture;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="systemCulture">Returns the culture of the operating system; the UI culture of the user by default.</param>
    public LanguageService(ILocalizer localizer, ISettingsStore settings, Func<CultureInfo>? systemCulture = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _systemCulture = systemCulture ?? (() => CultureInfo.CurrentUICulture);
    }

    /// <inheritdoc />
    public IReadOnlyList<LanguageInfo> Languages => _localizer.Languages;

    /// <inheritdoc />
    public LanguageInfo Current => _localizer.Current;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var stored = await _settings.GetAsync(Setting, cancellationToken).ConfigureAwait(true);
        var known = _localizer.Languages.FirstOrDefault(l => string.Equals(l.Code, stored?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (known is not null)
        {
            _localizer.SetLanguage(known.Code);
            return;
        }

        // The first start (or a stored language that is gone): the operating system decides, once, and the result is
        // stored like any choice of the user.
        await SelectAsync(_localizer.Detect(_systemCulture()).Code, cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task SelectAsync(string code, CancellationToken cancellationToken)
    {
        _localizer.SetLanguage(code);
        await _settings.SetAsync(Setting, _localizer.Current.Code, cancellationToken).ConfigureAwait(true);
    }
}
