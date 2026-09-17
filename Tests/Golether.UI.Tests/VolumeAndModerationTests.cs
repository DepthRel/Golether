using System.Collections.Concurrent;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Session;
using Golether.Media.Player;
using Golether.Session;
using Golether.UI.ViewModels;
using NSubstitute;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of the local volume and of the host's device switches on participant tiles.
/// </summary>
public sealed class VolumeAndModerationTests
{
    /// <summary>
    /// The player.
    /// </summary>
    private readonly ILocalPlayerControls _player = Substitute.For<ILocalPlayerControls>();

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();

    /// <summary>
    /// The toggle mutes without changing the level, and unmutes back to it.
    /// </summary>
    [Fact]
    public void ToggleMute_KeepsTheLevel()
    {
        var volume = new VolumeViewModel(_player, _settings) { Volume = 70 };

        volume.ToggleMuteCommand.Execute(null);

        Assert.True(volume.IsMuted);
        Assert.Equal(70, volume.Volume);
        Assert.True(volume.IsSilent);
        Assert.Equal("без звука", volume.Caption);
        _player.Received().SetMuted(true);

        volume.ToggleMuteCommand.Execute(null);

        Assert.False(volume.IsMuted);
        Assert.True(volume.IsLoud);
        Assert.Equal("70 %", volume.Caption);
        _player.Received().SetVolume(70);
        _player.Received().SetMuted(false);
    }

    /// <summary>
    /// Reaching 0 mutes; unmuting at 0 restores the last audible level; moving the scale unmutes.
    /// </summary>
    [Fact]
    public void ZeroMutes_AndUnmuteRestoresLevel()
    {
        var volume = new VolumeViewModel(_player, _settings) { Volume = 30 };
        Assert.True(volume.IsQuiet);

        volume.Volume = 0;
        Assert.True(volume.IsMuted);

        volume.ToggleMuteCommand.Execute(null);
        Assert.False(volume.IsMuted);
        Assert.Equal(30, volume.Volume);

        volume.ToggleMuteCommand.Execute(null);
        volume.Volume = 40;
        Assert.False(volume.IsMuted);
    }

    /// <summary>
    /// The stored level and mute state are restored and applied.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task StoredVolume_IsRestored()
    {
        _settings.GetAsync(VolumeViewModel.VolumeSetting, Arg.Any<CancellationToken>()).Returns("25");
        _settings.GetAsync(VolumeViewModel.MutedSetting, Arg.Any<CancellationToken>()).Returns("true");
        var volume = new VolumeViewModel(_player, _settings);

        await volume.LoadAsync();

        Assert.Equal(25, volume.Volume);
        Assert.True(volume.IsMuted);
        _player.Received().SetVolume(25);
        _player.Received().SetMuted(true);
        await _settings.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A tile offers only switching off, and only while the device is on.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Tile_SwitchesDevicesOffOnly()
    {
        var peer = PeerId.Parse(new string('a', 64));
        var requests = new ConcurrentQueue<(PeerId Peer, bool Microphone, bool Camera)>();
        var tile = new ParticipantItemViewModel(peer, (p, m, c) =>
        {
            requests.Enqueue((p, m, c));
            return Task.CompletedTask;
        });
        tile.Update(View(peer, microphoneOff: false, cameraOff: true), null);

        Assert.True(tile.SwitchOffMicrophoneCommand.CanExecute(null));
        Assert.False(tile.SwitchOffCameraCommand.CanExecute(null));
        await tile.SwitchOffMicrophoneCommand.ExecuteAsync(null);
        Assert.Equal((peer, true, false), Assert.Single(requests));

        tile.Update(View(peer, microphoneOff: true, cameraOff: true), null);
        Assert.False(tile.SwitchOffMicrophoneCommand.CanExecute(null));
        Assert.DoesNotContain(typeof(ParticipantItemViewModel).GetProperties(), p => p.Name.Contains("SwitchOn", StringComparison.Ordinal));
    }

    /// <summary>
    /// Creates a participant view.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="microphoneOff">Whether the microphone is off.</param>
    /// <param name="cameraOff">Whether the camera is off.</param>
    /// <returns>The view.</returns>
    private static ParticipantView View(PeerId peer, bool microphoneOff, bool cameraOff)
        => new(
            new ParticipantInfo(peer, "Марина", false),
            new ParticipantStatus { PeerId = peer, MicrophoneOff = microphoneOff, CameraOff = cameraOff },
            IsLocal: false);
}
