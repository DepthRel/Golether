using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using Golether.Localization;
using Golether.UI.Services;

namespace Golether.UI.ViewModels;

/// <summary>
/// The language selector: the languages offered and the one in use.
/// </summary>
public sealed partial class LanguageViewModel : ObservableObject
{
    /// <summary>
    /// Applies and stores the choice.
    /// </summary>
    private readonly ILanguageService _service;

    /// <summary>
    /// Shows a message when the choice cannot be stored.
    /// </summary>
    private readonly IDialogService? _dialogs;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageViewModel"/> class.
    /// </summary>
    /// <param name="service">The language service, already initialized: the selector shows the language in use.</param>
    /// <param name="flags">Finds the flag of a language by its code, or <see langword="null"/> to show no flags.</param>
    /// <param name="dialogs">Shows a message when the choice cannot be stored, or <see langword="null"/>.</param>
    public LanguageViewModel(ILanguageService service, Func<string, IImage?>? flags = null, IDialogService? dialogs = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dialogs = dialogs;
        Options = [.. service.Languages.Select(l => new LanguageOption(l, flags?.Invoke(l.Code)))];
        Selected = Options.FirstOrDefault(o => o.Code == service.Current.Code) ?? Options[0];
    }

    /// <summary>
    /// Gets the languages offered.
    /// </summary>
    public IReadOnlyList<LanguageOption> Options { get; }

    /// <summary>
    /// Gets or sets the language in use.
    /// </summary>
    [ObservableProperty]
    public partial LanguageOption Selected { get; set; }

    /// <summary>
    /// Switches the interface to the language the user picked and stores the choice.
    /// </summary>
    /// <param name="value">The picked language.</param>
    partial void OnSelectedChanged(LanguageOption value)
    {
        if (value is null || value.Code == _service.Current.Code)
        {
            return;
        }

        _ = ApplyAsync(value);
    }

    /// <summary>
    /// Applies a language; a failure to store it does not undo the switch, the language is used until the end of the
    /// session and the user is told.
    /// </summary>
    /// <param name="option">The language.</param>
    /// <returns>A task that completes when the language is stored.</returns>
    private async Task ApplyAsync(LanguageOption option)
    {
        try
        {
            await _service.SelectAsync(option.Code, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (_dialogs is not null)
            {
                await _dialogs.ShowErrorAsync(Texts.Get("Language.Error.Title"), ex.Message);
            }
        }
    }
}
