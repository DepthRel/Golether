using NSubstitute;
using Golether.Client.Services;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;

namespace Golether.Client.Tests;

/// <summary>
/// Tests of <see cref="VoiceVolumeStore"/>: the volume of a voice outlives the session, and a moving slider does not
/// write to the disk at every step.
/// </summary>
public sealed class VoiceVolumeStoreTests
{
    /// <summary>
    /// The participant.
    /// </summary>
    private static readonly PeerId _peer = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();

    /// <summary>
    /// Only the last value of a moving slider is written.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Save_WritesOnlyTheLastValue()
    {
        var store = new VoiceVolumeStore(_settings, TimeSpan.FromMilliseconds(50));

        var first = store.SaveAsync(_peer, 30);
        var last = store.SaveAsync(_peer, 70.5);
        await Task.WhenAll(first, last);

        await _settings.Received(1).SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _settings.Received(1).SetAsync(VoiceVolumeStore.SETTING_PREFIX + _peer.Value, "70.5", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A stored value is read back whatever the language of the system writes numbers in.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Read_ReturnsTheStoredVolume()
    {
        _settings.GetAsync(VoiceVolumeStore.SETTING_PREFIX + _peer.Value, Arg.Any<CancellationToken>()).Returns("70.5");

        var volume = await new VoiceVolumeStore(_settings, TimeSpan.Zero).ReadAsync(_peer);

        Assert.Equal(70.5, volume);
    }

    /// <summary>
    /// Nothing stored, or something unreadable, leaves the volume as it is.
    /// </summary>
    /// <param name="stored">The stored text.</param>
    /// <returns>A task that completes when the test ends.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("loud")]
    public async Task Read_WithoutAVolume_ReturnsNothing(string? stored)
    {
        _settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(stored);

        Assert.Null(await new VoiceVolumeStore(_settings, TimeSpan.Zero).ReadAsync(_peer));
    }
}
