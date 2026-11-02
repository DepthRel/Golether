using NSubstitute;
using Golether.Core.Data.Enums;
using Golether.Media.Conference;
using Golether.Mobile.Services;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Mobile.Tests;

/// <summary>
/// Tests of <see cref="ParticipantConnection"/>: what it tells the screen and what it refuses. The way back itself runs
/// against a real host in the tests of <see cref="ParticipantReconnector"/>.
/// </summary>
public sealed class ParticipantConnectionTests : IDisposable
{
    /// <summary>
    /// The services.
    /// </summary>
    private readonly StubServices _services = new();

    /// <summary>
    /// The connection.
    /// </summary>
    private readonly ParticipantConnection _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticipantConnectionTests"/> class.
    /// </summary>
    public ParticipantConnectionTests()
        => _connection = new ParticipantConnection(_services, Substitute.For<IConferenceMedia>(), new CurrentSession(), "Катя");

    /// <inheritdoc />
    public void Dispose() => _services.Dispose();

    /// <summary>
    /// How the way back goes reaches the screen.
    /// </summary>
    [Fact]
    public void Report_TellsTheScreen()
    {
        var changes = 0;
        _connection.Changed += (_, _) => changes++;

        ((IReconnectionClient)_connection).Report("пробуем");

        Assert.Equal("пробуем", _connection.ReconnectMessage);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// A session this connection does not hold gets no ticket: the user may have left.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task GetTicket_ForAnotherSession_GivesNothing()
    {
        await using var stranger = _connection.CreateSession();

        Assert.Null(((IReconnectionClient)_connection).GetTicket(stranger));
    }

    /// <summary>
    /// A session that came back after the user left is not taken.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Adopt_AfterTheUserLeft_IsRefused()
    {
        await using var ended = _connection.CreateSession();
        await using var restored = _connection.CreateSession();
        var client = (IReconnectionClient)_connection;

        var taken = await client.AdoptAsync(ended, restored, null!, TestContext.Current.CancellationToken);

        Assert.False(taken);
        Assert.Null(_connection.GetSnapshot());
    }

    /// <summary>
    /// A return of the network outside the way back changes nothing.
    /// </summary>
    [Fact]
    public void RetryNow_OutsideTheWayBack_DoesNothing()
    {
        _connection.RetryNow();

        Assert.False(_connection.IsReconnecting);
    }

    /// <summary>
    /// Requests of a user who left are dropped without a failure.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Request_AfterLeaving_IsDropped()
    {
        await _connection.DisposeAsync();

        await _connection.RequestAsync(new PlaybackRequest(PlaybackRequestKind.Play, null));

        Assert.Null(_connection.GetSnapshot());
    }

    /// <summary>
    /// Closing twice is safe: the screen and the end of a session may both do it.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Dispose_Twice_IsSafe()
    {
        await _connection.DisposeAsync();
        await _connection.DisposeAsync();

        Assert.Null(_connection.GetSnapshot());
    }
}
