using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Media.Conference;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using Golether.UI.ViewModels.Dialogs;
using NSubstitute;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of the settings view model.
/// </summary>
public sealed class SettingsViewModelTests
{
    /// <summary>
    /// A chosen avatar for the tests.
    /// </summary>
    private static readonly AvatarSelection Chosen = new([1, 2, 3], [4, 5, 6], new AvatarFraming(2, 10, -5));

    /// <summary>
    /// Builds a view model over the given actions.
    /// </summary>
    /// <param name="pick">Returns the chosen avatar.</param>
    /// <param name="apply">Records applied avatars.</param>
    /// <param name="openTunnels">Records tunnel dialog requests.</param>
    /// <param name="canConfigureTunnels">Whether tunnels may be configured.</param>
    /// <returns>The view model.</returns>
    private static SettingsViewModel Create(
        Func<Task<AvatarSelection?>> pick,
        Func<AvatarSelection, Task> apply,
        Func<Task>? openTunnels = null,
        bool canConfigureTunnels = true)
        => new(
            language: null,
            devices: null,
            conferenceAvailable: true,
            conferenceNotice: string.Empty,
            canChooseAvatar: true,
            canConfigureTunnels: canConfigureTunnels,
            avatarPreview: null,
            pickAvatar: pick,
            applyAvatar: apply,
            openTunnels: openTunnels ?? (() => Task.CompletedTask));

    /// <summary>
    /// Builds a device chooser over substitutes, with the system default selected.
    /// </summary>
    /// <param name="devices">The devices the backend reports.</param>
    /// <returns>The chooser.</returns>
    private static DevicesViewModel CreateDevices(params CaptureDevice[] devices)
    {
        var selector = Substitute.For<ICaptureDeviceSelector>();
        selector.IsAvailable.Returns(true);
        selector.GetDevices().Returns(devices);
        return new DevicesViewModel(selector, Substitute.For<ISettingsStore>());
    }

    /// <summary>
    /// Builds a view model over a device chooser.
    /// </summary>
    /// <param name="devices">The device chooser.</param>
    /// <returns>The view model.</returns>
    private static SettingsViewModel CreateOver(DevicesViewModel devices)
        => new(
            language: null,
            devices: devices,
            conferenceAvailable: true,
            conferenceNotice: string.Empty,
            canChooseAvatar: true,
            canConfigureTunnels: true,
            avatarPreview: null,
            pickAvatar: () => Task.FromResult<AvatarSelection?>(null),
            applyAvatar: _ => Task.CompletedTask,
            openTunnels: () => Task.CompletedTask);

    /// <summary>
    /// A box that drops its selection while its list is brought up to date gets the choice back at once, so the field
    /// does not blink. Nothing was picked by hand, so applying leaves the chooser alone.
    /// </summary>
    [Fact]
    public async Task DroppedChoice_IsPutBackAtOnce()
    {
        var devices = CreateDevices();
        var model = CreateOver(devices);
        Assert.Same(DeviceOption.SystemDefault, model.SelectedCamera);
        Assert.Same(DeviceOption.SystemDefault, model.SelectedMicrophone);

        // This is what a box does while its items change: it writes an empty choice back.
        model.SelectedCamera = null;
        model.SelectedMicrophone = null;

        // No refresh, no wait: the choice is already back.
        Assert.Same(DeviceOption.SystemDefault, model.SelectedCamera);
        Assert.Same(DeviceOption.SystemDefault, model.SelectedMicrophone);

        await model.ApplyAsync();
        Assert.Same(DeviceOption.SystemDefault, devices.SelectedCamera);
    }

    /// <summary>
    /// A device that really is gone from the list is not forced back: the box is left empty and the choice falls back
    /// to the system default when the lists settle.
    /// </summary>
    [Fact]
    public void ChoiceOfAVanishedDevice_IsNotForcedBack()
    {
        var camera = new CaptureDevice("cam-1", "Веб-камера", CaptureDeviceKind.Camera);
        var devices = CreateDevices(camera);
        var model = CreateOver(devices);
        var chosen = new DeviceOption(camera.Name, camera);
        model.SelectedCamera = chosen;

        // The camera is unplugged: it leaves the list, and the box drops it.
        devices.Cameras.Remove(chosen);
        model.SelectedCamera = null;

        Assert.Null(model.SelectedCamera);
    }

    /// <summary>
    /// Reading the device list adds the devices without emptying it, so the boxes never lose what they show.
    /// </summary>
    [Fact]
    public async Task DeviceList_IsUpdatedWithoutBeingEmptied()
    {
        var camera = new CaptureDevice("cam-1", "Веб-камера", CaptureDeviceKind.Camera);
        var devices = CreateDevices(camera);
        var emptied = false;
        devices.Cameras.CollectionChanged += (_, e) =>
            emptied |= e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset;

        await devices.RefreshAsync();

        Assert.False(emptied, "The list must be brought up to date in place, not cleared.");
        Assert.Equal([DeviceOption.SystemDefault, new DeviceOption(camera.Name, camera)], devices.Cameras);
        Assert.Same(DeviceOption.SystemDefault, devices.SelectedCamera);
    }

    /// <summary>
    /// A device the user picked is not overwritten by the arriving list, and it is applied only on apply.
    /// </summary>
    [Fact]
    public async Task PickedDevice_SurvivesTheListAndIsAppliedOnApply()
    {
        var devices = CreateDevices();
        var model = CreateOver(devices);
        var chosen = new DeviceOption("Веб-камера", new CaptureDevice("cam-1", "Веб-камера", CaptureDeviceKind.Camera));

        model.SelectedCamera = chosen;
        model.SyncDevices();
        Assert.Same(chosen, model.SelectedCamera);
        Assert.Same(DeviceOption.SystemDefault, devices.SelectedCamera);

        await model.ApplyAsync();
        Assert.Same(chosen, devices.SelectedCamera);
    }

    /// <summary>
    /// Choosing an avatar changes nothing until the settings are applied; applying hands it over once.
    /// </summary>
    [Fact]
    public async Task Avatar_IsAppliedOnlyOnApply()
    {
        var applied = new List<AvatarSelection>();
        var model = Create(() => Task.FromResult<AvatarSelection?>(Chosen), s =>
        {
            applied.Add(s);
            return Task.CompletedTask;
        });

        await model.ChangeAvatarCommand.ExecuteAsync(null);
        Assert.Empty(applied);

        await model.ApplyAsync();
        Assert.Same(Chosen, Assert.Single(applied));

        // Applying again does not send the same avatar twice.
        await model.ApplyAsync();
        Assert.Single(applied);
    }

    /// <summary>
    /// Closing without applying leaves the previous avatar in place, which is what "cancel" is for.
    /// </summary>
    [Fact]
    public async Task Avatar_IsDiscardedWhenNotApplied()
    {
        var applied = new List<AvatarSelection>();
        var model = Create(() => Task.FromResult<AvatarSelection?>(Chosen), s =>
        {
            applied.Add(s);
            return Task.CompletedTask;
        });

        await model.ChangeAvatarCommand.ExecuteAsync(null);

        // The window is closed with "cancel": ApplyAsync is never called.
        Assert.Empty(applied);
    }

    /// <summary>
    /// A cancelled chooser leaves nothing to apply, and the tunnel gate follows the session.
    /// </summary>
    [Fact]
    public async Task CancelledChooser_AppliesNothing_AndTunnelsGateFollowsSession()
    {
        var applied = new List<AvatarSelection>();
        var opened = false;
        var model = Create(
            () => Task.FromResult<AvatarSelection?>(null),
            s =>
            {
                applied.Add(s);
                return Task.CompletedTask;
            },
            () =>
            {
                opened = true;
                return Task.CompletedTask;
            },
            canConfigureTunnels: false);

        await model.ChangeAvatarCommand.ExecuteAsync(null);
        await model.ApplyAsync();
        Assert.Empty(applied);

        Assert.False(model.CanConfigureTunnels);
        Assert.True(model.CanChooseAvatar);
        await model.OpenTunnelsCommand.ExecuteAsync(null);
        Assert.True(opened);
    }
}
