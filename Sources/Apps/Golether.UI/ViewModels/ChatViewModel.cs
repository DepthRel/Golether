using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Data.Enums;
using Golether.Session;
using Golether.Sync.Protocol;
using Golether.UI.Services;

namespace Golether.UI.ViewModels;

/// <summary>
/// The session chat and the reactions over the video. Messages travel over the encrypted session channel through the
/// host and are not stored anywhere.
/// </summary>
public sealed partial class ChatViewModel : ObservableObject
{
    /// <summary>
    /// The number of lines kept.
    /// </summary>
    public const int MaxLines = 200;

    /// <summary>
    /// The number of reactions shown at once.
    /// </summary>
    public const int MaxReactions = 12;

    /// <summary>
    /// How long a reaction floats over the video.
    /// </summary>
    public static readonly TimeSpan ReactionLifetime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The session service.
    /// </summary>
    private readonly ISessionService _session;

    /// <summary>
    /// The UI dispatcher.
    /// </summary>
    private readonly IUiDispatcher _dispatcher;

    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Picks the horizontal lanes of reactions.
    /// </summary>
    private int _nextLane;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatViewModel"/> class.
    /// </summary>
    /// <param name="session">The session service.</param>
    /// <param name="dispatcher">The UI dispatcher.</param>
    /// <param name="timeProvider">The time source, or <see langword="null"/> for the system clock.</param>
    public ChatViewModel(ISessionService session, IUiDispatcher dispatcher, TimeProvider? timeProvider = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _session.ChatReceived += (_, entry) => _dispatcher.Post(() => Add(entry));
        Reactions.CollectionChanged += (_, _) => HasReactions = Reactions.Count > 0;
    }

    /// <summary>
    /// Gets the reactions that can be sent.
    /// </summary>
    public IReadOnlyList<string> AvailableReactions => ChatMessage.Reactions;

    /// <summary>
    /// Gets the chat lines, oldest first.
    /// </summary>
    public ObservableCollection<ChatLineViewModel> Lines { get; } = [];

    /// <summary>
    /// Gets the reactions floating over the video.
    /// </summary>
    public ObservableCollection<FloatingReactionViewModel> Reactions { get; } = [];

    /// <summary>
    /// Gets or sets the text being typed.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the chat can be seen (set by the window); hidden lines count as unread.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of lines that arrived while the chat was hidden.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header), nameof(HasUnread))]
    public partial int Unread { get; set; }

    /// <summary>
    /// Gets a value indicating whether hidden lines arrived.
    /// </summary>
    public bool HasUnread => Unread > 0;

    /// <summary>
    /// Gets the header of the chat.
    /// </summary>
    public string Header => Unread > 0 ? $"ЧАТ · {Unread}" : "ЧАТ";

    /// <summary>
    /// Gets or sets a value indicating whether the chat has lines.
    /// </summary>
    [ObservableProperty]
    public partial bool HasLines { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether reactions float over the video.
    /// </summary>
    [ObservableProperty]
    public partial bool HasReactions { get; set; }

    /// <summary>
    /// Raised on the UI thread after a line was added, so the view can scroll to it.
    /// </summary>
    public event EventHandler? LineAdded;

    /// <summary>
    /// Clears the chat when a session ends.
    /// </summary>
    public void Reset()
    {
        Lines.Clear();
        Reactions.Clear();
        Draft = string.Empty;
        Unread = 0;
        HasLines = false;
    }

    /// <summary>
    /// Removes reactions that have floated away.
    /// </summary>
    public void Tick()
    {
        var now = _timeProvider.GetUtcNow();
        for (var i = Reactions.Count - 1; i >= 0; i--)
        {
            if (now - Reactions[i].ShownAt >= ReactionLifetime)
            {
                Reactions.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Clears the unread counter when the chat becomes visible.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            Unread = 0;
        }
    }

    /// <summary>
    /// Sends the typed text.
    /// </summary>
    /// <returns>A task that completes when the text was sent.</returns>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Draft;
        if (ChatMessage.Normalize(ChatKind.Text, text) is null)
        {
            return;
        }

        Draft = string.Empty;
        try
        {
            await _session.SendChatAsync(ChatKind.Text, text, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            Draft = text;
            AddSystem("Сообщение не отправлено: нет связи с ведущим.");
        }
    }

    /// <summary>
    /// Checks whether there is something to send.
    /// </summary>
    /// <returns><see langword="true"/> when the draft has text.</returns>
    private bool CanSend() => !string.IsNullOrWhiteSpace(Draft);

    /// <summary>
    /// Sends a reaction.
    /// </summary>
    /// <param name="reaction">The reaction.</param>
    /// <returns>A task that completes when the reaction was sent.</returns>
    [RelayCommand]
    private async Task ReactAsync(string? reaction)
    {
        if (reaction is null || ChatMessage.Normalize(ChatKind.Reaction, reaction) is null)
        {
            return;
        }

        try
        {
            await _session.SendChatAsync(ChatKind.Reaction, reaction, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // A reaction is not worth an error message.
        }
    }

    /// <summary>
    /// Shows a received message.
    /// </summary>
    /// <param name="entry">The message.</param>
    private void Add(ChatEntry entry)
    {
        if (entry.Kind == ChatKind.Reaction)
        {
            while (Reactions.Count >= MaxReactions)
            {
                Reactions.RemoveAt(0);
            }

            Reactions.Add(new FloatingReactionViewModel(entry.Text, entry.SenderName, _nextLane++ % 4, _timeProvider.GetUtcNow()));
            return;
        }

        AddLine(new ChatLineViewModel(entry.Id, entry.SenderName, entry.Text, entry.Time, entry.IsLocal, false));
        if (!IsExpanded && !entry.IsLocal)
        {
            Unread++;
        }
    }

    /// <summary>
    /// Shows a note of this device.
    /// </summary>
    /// <param name="text">The text.</param>
    private void AddSystem(string text)
        => AddLine(new ChatLineViewModel(string.Empty, string.Empty, text, _timeProvider.GetLocalNow(), true, true));

    /// <summary>
    /// Appends a line and trims the history.
    /// </summary>
    /// <param name="line">The line.</param>
    private void AddLine(ChatLineViewModel line)
    {
        Lines.Add(line);
        while (Lines.Count > MaxLines)
        {
            Lines.RemoveAt(0);
        }

        HasLines = true;
        LineAdded?.Invoke(this, EventArgs.Empty);
    }
}
