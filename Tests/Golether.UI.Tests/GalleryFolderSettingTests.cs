using NSubstitute;
using Golether.Components.Installation;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Media.Conference;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using Golether.UI.ViewModels.Dialogs;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of where the folder browser opens: where it was left last time, also after a restart.
/// </summary>
public sealed class GalleryFolderSettingTests : IDisposable
{
    /// <summary>
    /// The folder the browser was left in last time.
    /// </summary>
    private readonly string _root = Directory.CreateTempSubdirectory("golether-folder-setting-").FullName;

    /// <summary>
    /// The settings substitute.
    /// </summary>
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();

    /// <summary>
    /// The dialogs substitute.
    /// </summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// The browser opens in the folder stored by an earlier run; the folder it is left in, chosen or not, is stored
    /// once, and the next opening starts there without reading the settings again.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Browser_OpensWhereItWasLeft()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "Отпуск")).FullName;
        _settings.GetAsync(MainWindowViewModel.GALLERY_FOLDER_SETTING, Arg.Any<CancellationToken>()).Returns(_root);
        var opened = new List<string>();
        _dialogs.ChooseGalleryAsync(Arg.Any<FolderBrowserViewModel>()).Returns(call =>
        {
            // The host goes into a nested folder, if there is one, and closes the browser without a choice.
            var browser = call.Arg<FolderBrowserViewModel>();
            opened.Add(browser.CurrentPath);
            if (browser.Entries.FirstOrDefault(e => e.IsFolder) is { } folder)
            {
                browser.OpenEntryCommand.Execute(folder);
            }

            return Task.FromResult<GalleryChoice?>(null);
        });
        var session = Substitute.For<ISessionService>();
        session.IsHost.Returns(true);
        var viewModel = CreateViewModel(session);

        await viewModel.PickFolderCommand.ExecuteAsync(null);
        await viewModel.PickFolderCommand.ExecuteAsync(null);

        Assert.Equal([_root, nested], opened);
        await _settings.Received(1).GetAsync(MainWindowViewModel.GALLERY_FOLDER_SETTING, Arg.Any<CancellationToken>());
        await _settings.Received(1).SetAsync(MainWindowViewModel.GALLERY_FOLDER_SETTING, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _settings.Received(1).SetAsync(MainWindowViewModel.GALLERY_FOLDER_SETTING, nested, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Builds the main view model over substitutes.
    /// </summary>
    /// <param name="session">The session service.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(ISessionService session)
    {
        var components = Substitute.For<IComponentService>();
        foreach (var id in new[] { ComponentId.Video, ComponentId.Conference })
        {
            components.GetStatus(id).Returns(new ComponentStatus(id, ComponentSource.Bundled, "x", null, null));
        }

        return new MainWindowViewModel(
            session,
            _dialogs,
            _settings,
            new InlineDispatcher(),
            _ => throw new NotSupportedException(),
            "A249-B9CC",
            Substitute.For<IConferenceMedia>(),
            components);
    }

    /// <summary>
    /// Runs posted actions immediately.
    /// </summary>
    private sealed class InlineDispatcher : IUiDispatcher
    {
        /// <inheritdoc />
        public void Post(Action action) => action();
    }
}
