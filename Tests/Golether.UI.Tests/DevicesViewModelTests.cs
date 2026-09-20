using System.Collections.Concurrent;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Media.Conference;
using Golether.UI.ViewModels;
using NSubstitute;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="DevicesViewModel"/>.
/// </summary>
public sealed class DevicesViewModelTests
{
    /// <summary>
    /// The first camera.
    /// </summary>
    private static readonly CaptureDevice FrontCamera = new(@"\\?\usb#front", "Front camera", CaptureDeviceKind.Camera);

    /// <summary>
    /// The second camera.
    /// </summary>
    private static readonly CaptureDevice DeskCamera = new(@"\\?\usb#desk", "Desk camera", CaptureDeviceKind.Camera);

    /// <summary>
    /// The microphone.
    /// </summary>
    private static readonly CaptureDevice Headset = new("{0.0.1.00000000}.{headset}", "Headset", CaptureDeviceKind.Microphone);

    /// <summary>
    /// The devices backend.
    /// </summary>
    private readonly ICaptureDeviceSelector _selector = Substitute.For<ICaptureDeviceSelector>();

    /// <summary>
    /// The settings kept in memory.
    /// </summary>
    private readonly MemorySettings _settings = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DevicesViewModelTests"/> class.
    /// </summary>
    public DevicesViewModelTests()
    {
        _selector.IsAvailable.Returns(true);
        _selector.GetDevices().Returns([DeskCamera, Headset, FrontCamera]);
    }

    /// <summary>
    /// The lists hold the system default first and the devices of their kind; a choice is saved and applied.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Choice_IsListedSavedAndApplied()
    {
        var viewModel = new DevicesViewModel(_selector, _settings);

        await viewModel.RefreshAsync();
        Assert.Equal(["Как в системе", "Desk camera", "Front camera"], viewModel.Cameras.Select(c => c.Name));
        Assert.Equal(["Как в системе", "Headset"], viewModel.Microphones.Select(c => c.Name));
        Assert.Same(DeviceOption.SystemDefault, viewModel.SelectedCamera);

        viewModel.SelectedCamera = viewModel.Cameras[2];
        await WaitUntilAsync(() => _settings.Values.ContainsKey(DevicesViewModel.CameraSetting));

        Assert.Equal(FrontCamera.Id, _settings.Values[DevicesViewModel.CameraSetting]);
        await _selector.Received().SelectDevicesAsync(FrontCamera, null, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A saved device is selected again on the next start; a device that disappeared falls back to the default.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SavedChoice_IsRestoredOrFallsBack()
    {
        _settings.Values[DevicesViewModel.CameraSetting] = DeskCamera.Id;
        _settings.Values[DevicesViewModel.MicrophoneSetting] = "unplugged";
        var viewModel = new DevicesViewModel(_selector, _settings);

        await viewModel.RefreshAsync();

        Assert.Equal(DeskCamera, viewModel.SelectedCamera!.Device);
        Assert.Same(DeviceOption.SystemDefault, viewModel.SelectedMicrophone);
        await _selector.Received(1).SelectDevicesAsync(DeskCamera, null, Arg.Any<CancellationToken>());
        Assert.Equal("unplugged", _settings.Values[DevicesViewModel.MicrophoneSetting]);
    }

    /// <summary>
    /// Polls a condition.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    /// <summary>
    /// <see cref="ISettingsStore"/> in memory.
    /// </summary>
    private sealed class MemorySettings : ISettingsStore
    {
        /// <summary>
        /// Gets the values.
        /// </summary>
        public ConcurrentDictionary<string, string> Values { get; } = new();

        /// <inheritdoc />
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

        /// <inheritdoc />
        public Task SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
    }
}
