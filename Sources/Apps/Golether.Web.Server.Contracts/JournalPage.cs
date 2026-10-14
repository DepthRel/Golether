namespace Golether.Web.Server.Contracts;

/// <summary>
/// A part of the journal of the server: the events after a given number.
/// </summary>
/// <param name="Started">
/// When this run of the server started. A different value than before means the server was restarted and its
/// numbering began again.
/// </param>
/// <param name="Entries">The events, the oldest first.</param>
/// <param name="Last">The number of the newest event the server has, or 0 when it has none.</param>
public sealed record JournalPage(DateTimeOffset Started, IReadOnlyList<JournalEntry> Entries, long Last);
