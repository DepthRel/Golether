using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Components.Catalog;
using Golether.Components.Installation;
using Golether.UI.Services;

namespace Golether.UI.ViewModels;

/// <summary>
/// A native component in the UI: its state and a one-click installation.
/// </summary>
public sealed partial class ComponentItemViewModel : ObservableObject
{
    /// <summary>
    /// The component service.
    /// </summary>
    private readonly IComponentService _components;

    /// <summary>
    /// The dialogs.
    /// </summary>
    private readonly IDialogService _dialogs;

    /// <summary>
    /// Cancels a running installation.
    /// </summary>
    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentItemViewModel"/> class.
    /// </summary>
    /// <param name="id">The component.</param>
    /// <param name="components">The component service.</param>
    /// <param name="dialogs">The dialogs.</param>
    public ComponentItemViewModel(ComponentId id, IComponentService components, IDialogService dialogs)
    {
        Id = id;
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        var description = ComponentCatalog.Describe(id);
        Title = description.Title;
        Purpose = char.ToUpper(description.Purpose[0], CultureInfo.CurrentCulture) + description.Purpose[1..];
        Refresh();
    }

    /// <summary>
    /// Gets the component.
    /// </summary>
    public ComponentId Id { get; }

    /// <summary>
    /// Gets the title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets what the component is for.
    /// </summary>
    public string Purpose { get; }

    /// <summary>
    /// Gets or sets the state text.
    /// </summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the component works.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the install button is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool CanInstall { get; set; }

    /// <summary>
    /// Gets or sets the caption of the install button.
    /// </summary>
    [ObservableProperty]
    public partial string InstallText { get; set; } = "Установить";

    /// <summary>
    /// Gets or sets a value indicating whether an installation runs.
    /// </summary>
    [ObservableProperty]
    public partial bool IsInstalling { get; set; }

    /// <summary>
    /// Gets or sets the progress, 0–100.
    /// </summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    /// <summary>
    /// Gets or sets the progress text.
    /// </summary>
    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the manual installation hint.
    /// </summary>
    [ObservableProperty]
    public partial string AdviceText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command to copy, or an empty string.
    /// </summary>
    [ObservableProperty]
    public partial string AdviceCommand { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether a command is offered.
    /// </summary>
    public bool HasAdviceCommand => AdviceCommand.Length > 0;

    /// <summary>
    /// Re-reads the state of the component.
    /// </summary>
    public void Refresh()
    {
        var status = _components.GetStatus(Id);
        IsAvailable = status.IsAvailable;
        CanInstall = status.CanInstall && !IsInstalling;
        StatusText = status.Source switch
        {
            ComponentSource.Bundled => "входит в поставку",
            ComponentSource.Installed => "установлен",
            ComponentSource.System => "найден в системе",
            _ => "не установлен",
        };
        InstallText = status.Package is { } package
            ? $"Установить ({FormatSize(package.Size)})"
            : "Установить";
        AdviceText = status.Advice?.Text ?? string.Empty;
        AdviceCommand = status.Advice?.Command ?? string.Empty;
        OnPropertyChanged(nameof(HasAdviceCommand));
    }

    /// <summary>
    /// Downloads and installs the component.
    /// </summary>
    /// <returns>A task that completes when the installation finished.</returns>
    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsInstalling)
        {
            return;
        }

        IsInstalling = true;
        CanInstall = false;
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<InstallProgress>(Report);
        try
        {
            await _components.InstallAsync(Id, progress, _cancellation.Token);
            ProgressText = "Готово";
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Установка отменена";
        }
        catch (ComponentInstallException ex)
        {
            ProgressText = string.Empty;
            await _dialogs.ShowErrorAsync(Title, ex.Message);
        }
        finally
        {
            IsInstalling = false;
            _cancellation.Dispose();
            _cancellation = null;
            Refresh();
        }
    }

    /// <summary>
    /// Cancels a running installation.
    /// </summary>
    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    /// <summary>
    /// Copies the installation command.
    /// </summary>
    /// <returns>A task that completes when the command is copied.</returns>
    [RelayCommand]
    private async Task CopyAdviceAsync()
    {
        if (HasAdviceCommand)
        {
            await _dialogs.CopyTextAsync(AdviceCommand);
            ProgressText = "Команда скопирована";
        }
    }

    /// <summary>
    /// Formats a size in megabytes.
    /// </summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The text, for example <c>31 МБ</c>.</returns>
    private static string FormatSize(long bytes) => $"{Math.Max(1, bytes / (1024 * 1024))} МБ";

    /// <summary>
    /// Shows installation progress.
    /// </summary>
    /// <param name="progress">The progress.</param>
    private void Report(InstallProgress progress)
    {
        Progress = progress.Fraction * 100;
        ProgressText = progress.Stage switch
        {
            InstallStage.Downloading when progress.TotalBytes > 0 => $"Загрузка {progress.Bytes / (1024 * 1024)} из {progress.TotalBytes / (1024 * 1024)} МБ",
            InstallStage.Verifying => "Проверка контрольной суммы…",
            InstallStage.Unpacking => "Распаковка…",
            InstallStage.Finishing => "Подготовка файлов…",
            InstallStage.Completed => "Готово",
            _ => "Загрузка…",
        };
    }
}
