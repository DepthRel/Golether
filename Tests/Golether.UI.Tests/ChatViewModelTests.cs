using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Session;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using NSubstitute.ExceptionExtensions;
using NSubstitute;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="ChatViewModel"/>.
/// </summary>
public sealed class ChatViewModelTests
{
    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId Guest = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// The session.
    /// </summary>
    private readonly ISessionService _session = Substitute.For<ISessionService>();

    /// <summary>
    /// The test clock.
    /// </summary>
    private readonly TestTime _time = new();

    /// <summary>
    /// The model.
    /// </summary>
    private readonly ChatViewModel _chat;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatViewModelTests"/> class.
    /// </summary>
    public ChatViewModelTests()
    {
        var dispatcher = Substitute.For<IUiDispatcher>();
        dispatcher.When(d => d.Post(Arg.Any<Action>())).Do(call => call.Arg<Action>()());
        _chat = new ChatViewModel(_session, dispatcher, _time);
    }

    /// <summary>
    /// The draft is sent and cleared; an empty draft cannot be sent; a failure keeps the text and explains it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Send_SendsTheDraft()
    {
        Assert.False(_chat.SendCommand.CanExecute(null));
        _chat.Draft = "  Когда начнём? ";
        Assert.True(_chat.SendCommand.CanExecute(null));

        await _chat.SendCommand.ExecuteAsync(null);

        await _session.Received(1).SendChatAsync(ChatKind.Text, "  Когда начнём? ", Arg.Any<CancellationToken>());
        Assert.Empty(_chat.Draft);
        Assert.Empty(_chat.Lines);

        _session.SendChatAsync(ChatKind.Text, "ещё", Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("разрыв"));
        _chat.Draft = "ещё";
        await _chat.SendCommand.ExecuteAsync(null);
        Assert.Equal("ещё", _chat.Draft);
        Assert.True(Assert.Single(_chat.Lines).IsSystem);
    }

    /// <summary>
    /// Received lines are listed; while the chat is hidden, lines of others are counted as unread.
    /// </summary>
    [Fact]
    public void Lines_CountUnreadWhileHidden()
    {
        var added = 0;
        _chat.LineAdded += (_, _) => added++;
        Receive(ChatKind.Text, "привет", local: false);
        Assert.Equal("Марина", _chat.Lines[0].SenderText);
        Assert.True(_chat.HasLines);

        _chat.IsExpanded = false;
        Receive(ChatKind.Text, "ты тут?", local: false);
        Receive(ChatKind.Text, "я тут", local: true);
        Assert.Equal(1, _chat.Unread);
        Assert.Equal("ЧАТ · 1", _chat.Header);
        Assert.Equal("Вы", _chat.Lines[^1].SenderText);

        _chat.IsExpanded = true;
        Assert.Equal(0, _chat.Unread);
        Assert.Equal("ЧАТ", _chat.Header);
        Assert.Equal(3, added);

        for (var i = 0; i < ChatViewModel.MaxLines + 5; i++)
        {
            Receive(ChatKind.Text, $"строка {i}", local: false);
        }

        Assert.Equal(ChatViewModel.MaxLines, _chat.Lines.Count);
        Assert.Equal($"строка {ChatViewModel.MaxLines + 4}", _chat.Lines[^1].Text);

        _chat.Reset();
        Assert.Empty(_chat.Lines);
        Assert.False(_chat.HasLines);
    }

    /// <summary>
    /// Reactions float for a while in different lanes and never go to the chat log.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Reactions_FloatAndExpire()
    {
        await _chat.ReactCommand.ExecuteAsync("🔥");
        await _chat.ReactCommand.ExecuteAsync("🐍");
        await _session.Received(1).SendChatAsync(ChatKind.Reaction, "🔥", Arg.Any<CancellationToken>());
        await _session.DidNotReceive().SendChatAsync(ChatKind.Reaction, "🐍", Arg.Any<CancellationToken>());

        Receive(ChatKind.Reaction, "🔥", local: false);
        _time.Advance(TimeSpan.FromSeconds(1));
        Receive(ChatKind.Reaction, "👏", local: true);

        Assert.Empty(_chat.Lines);
        Assert.True(_chat.HasReactions);
        Assert.Equal([0d, 64d], _chat.Reactions.Select(r => r.Offset));

        _time.Advance(TimeSpan.FromSeconds(2));
        _chat.Tick();
        Assert.Equal("👏", Assert.Single(_chat.Reactions).Emoji);
        _time.Advance(TimeSpan.FromSeconds(1));
        _chat.Tick();
        Assert.False(_chat.HasReactions);

        for (var i = 0; i < ChatViewModel.MaxReactions + 3; i++)
        {
            Receive(ChatKind.Reaction, "👍", local: false);
        }

        Assert.Equal(ChatViewModel.MaxReactions, _chat.Reactions.Count);
    }

    /// <summary>
    /// Simulates a message relayed by the host.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="text">The text.</param>
    /// <param name="local">Whether this device sent it.</param>
    private void Receive(ChatKind kind, string text, bool local)
        => _session.ChatReceived += Raise.Event<EventHandler<ChatEntry>>(
            _session,
            new ChatEntry("0A", Guest, "Марина", kind, text, _time.GetLocalNow(), local));

    /// <summary>
    /// A clock moved by the test.
    /// </summary>
    private sealed class TestTime : TimeProvider
    {
        /// <summary>
        /// The current time.
        /// </summary>
        private DateTimeOffset _now = new(2026, 9, 17, 20, 0, 0, TimeSpan.Zero);

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => _now;

        /// <summary>
        /// Moves the time forward.
        /// </summary>
        /// <param name="value">The step.</param>
        public void Advance(TimeSpan value) => _now += value;
    }
}
