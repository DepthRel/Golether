using System.Net;
using Golether.Core.Enums;
using Golether.Web.Server.Contracts;
using Golether.Web.Server.Infrastructure;

namespace Golether.Web.Server.Tests;

/// <summary>
/// Tests of <see cref="ServerJournal"/>: a bounded journal in memory that a reader follows by the number of the last
/// event it has.
/// </summary>
public sealed class ServerJournalTests
{
    /// <summary>
    /// A reader gets only what it has not seen yet, the oldest first.
    /// </summary>
    [Fact]
    public void Read_ReturnsTheEventsAfterTheLastOneSeen()
    {
        var journal = new ServerJournal(TimeProvider.System);
        journal.Record(JournalEvent.ServerStarted, detail: "1.0.0");
        journal.Record(JournalEvent.HostRegistered, new IPEndPoint(IPAddress.Parse("203.0.113.7"), 51234), room: "room-1");
        journal.Record(JournalEvent.HostLeft, new IPEndPoint(IPAddress.Parse("203.0.113.7"), 51234), room: "room-1");

        var all = journal.Read(0);
        Assert.Equal([1L, 2L, 3L], all.Entries.Select(e => e.Sequence));
        Assert.Equal(3, all.Last);
        Assert.Equal("203.0.113.7:51234", all.Entries[1].Address);
        Assert.Equal(ServerJournal.TagOf("room-1"), all.Entries[1].Room);
        Assert.Equal(8, all.Entries[1].Room!.Length);

        var rest = journal.Read(2);
        Assert.Equal(JournalEvent.HostLeft, Assert.Single(rest.Entries).Event);
        Assert.Empty(journal.Read(3).Entries);
    }

    /// <summary>
    /// The journal keeps a limited number of events and forgets the oldest; one answer carries a limited number.
    /// </summary>
    [Fact]
    public void Journal_IsBounded()
    {
        var journal = new ServerJournal(TimeProvider.System);
        for (var i = 0; i < ServerJournal.CAPACITY + 10; i++)
        {
            journal.Record(JournalEvent.MeetingRequested);
        }

        var first = journal.Read(0);
        Assert.Equal(ServerContract.JOURNAL_PAGE_SIZE, first.Entries.Count);
        Assert.Equal(11, first.Entries[0].Sequence);
        Assert.Equal(ServerJournal.CAPACITY + 10, first.Last);
    }

    /// <summary>
    /// A reader that is ahead of the journal knew an earlier run of the server and gets everything from the start.
    /// </summary>
    [Fact]
    public void Read_AfterARestart_StartsOver()
    {
        var journal = new ServerJournal(TimeProvider.System);
        journal.Record(JournalEvent.ServerStarted);

        Assert.Single(journal.Read(500).Entries);
    }

    /// <summary>
    /// The answer travels as JSON with the events by name, and the monitor reads it back unchanged.
    /// </summary>
    [Fact]
    public void Page_TravelsAsJsonWithNames()
    {
        var journal = new ServerJournal(TimeProvider.System);
        journal.Record(JournalEvent.MeetingFailed, reason: RendezvousError.Timeout);

        var json = System.Text.Json.JsonSerializer.Serialize(journal.Read(0), ServerContract.Json);
        Assert.Contains("\"MeetingFailed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Timeout\"", json, StringComparison.Ordinal);
        var back = System.Text.Json.JsonSerializer.Deserialize<JournalPage>(json, ServerContract.Json)!;
        Assert.Equal(journal.Read(0).Entries, back.Entries);
    }
}
