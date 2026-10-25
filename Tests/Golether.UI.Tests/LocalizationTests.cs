using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Golether.Components.Catalog;
using Golether.Components.Installation;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Player;
using Golether.Security.Invites;
using Golether.Session;
using Golether.UI.Services;
using Golether.UI.ViewModels;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of the choice, the storage and the use of the interface language.
/// </summary>
public sealed class LocalizationTests
{
    /// <summary>
    /// A device that hosts the session.
    /// </summary>
    private static readonly PeerId _host = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId _guest = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// Creates a localizer over the languages the application carries.
    /// </summary>
    /// <param name="code">The first language, or <see langword="null"/> for the default one.</param>
    /// <returns>The localizer.</returns>
    private static Localizer NewLocalizer(string? code = null) => new(LanguageCatalog.LoadEmbedded(), code);

    /// <summary>
    /// Creates the language service over a memory database.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="localizer">The localizer.</param>
    /// <param name="systemCulture">The culture of the operating system.</param>
    /// <returns>The service.</returns>
    private static LanguageService NewService(MemorySettings settings, Localizer localizer, string systemCulture)
        => new(localizer, settings, () => CultureInfo.GetCultureInfo(systemCulture));

    /// <summary>
    /// On the first start a Russian operating system gets the Russian interface, and the choice is stored.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FirstStart_OnRussianSystem_UsesRussianAndStoresIt()
    {
        var settings = new MemorySettings();
        var localizer = NewLocalizer();
        var service = NewService(settings, localizer, "ru-RU");

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal("ru", service.Current.Code);
        Assert.Equal("ru", localizer.Current.Code);
        Assert.Equal("ru", settings.Values[LanguageService.SETTING]);
    }

    /// <summary>
    /// On the first start a system in another language the application has gets that language, and the choice is
    /// stored.
    /// </summary>
    /// <param name="systemCulture">The culture of the operating system.</param>
    /// <param name="expected">The language chosen.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("fr-CA", "fr")]
    [InlineData("es-MX", "es")]
    [InlineData("pt-BR", "pt")]
    public async Task FirstStart_OnASystemWithAPack_UsesItsLanguage(string systemCulture, string expected)
    {
        var settings = new MemorySettings();
        var localizer = NewLocalizer("ru");
        var service = NewService(settings, localizer, systemCulture);

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal(expected, service.Current.Code);
        Assert.Equal(expected, settings.Values[LanguageService.SETTING]);
    }

    /// <summary>
    /// On the first start any operating system language the application does not have gets English, and the choice is
    /// stored.
    /// </summary>
    /// <param name="systemCulture">The culture of the operating system.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("en-US")]
    [InlineData("it-IT")]
    [InlineData("ja-JP")]
    [InlineData("zh-CN")]
    public async Task FirstStart_OnOtherSystems_UsesEnglishAndStoresIt(string systemCulture)
    {
        var settings = new MemorySettings();
        var localizer = NewLocalizer("ru");
        var service = NewService(settings, localizer, systemCulture);

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal("en", service.Current.Code);
        Assert.Equal("en", settings.Values[LanguageService.SETTING]);
    }

    /// <summary>
    /// The system is asked once: later starts take the stored language, whatever the system says now.
    /// </summary>
    /// <param name="stored">The stored language.</param>
    /// <param name="systemCulture">The culture of the operating system now.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData("en", "ru-RU")]
    [InlineData("ru", "en-US")]
    [InlineData("ru", "de-DE")]
    [InlineData("EN", "ru-RU")]
    public async Task LaterStarts_UseTheStoredLanguage(string stored, string systemCulture)
    {
        var settings = new MemorySettings();
        settings.Values[LanguageService.SETTING] = stored;
        var localizer = NewLocalizer();
        var service = NewService(settings, localizer, systemCulture);

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal(stored.ToLowerInvariant(), service.Current.Code);
        Assert.Equal(stored, settings.Values[LanguageService.SETTING]);
    }

    /// <summary>
    /// A stored language the application no longer has is treated like a first start.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task UnknownStoredLanguage_IsChosenAgainBySystem()
    {
        var settings = new MemorySettings();
        settings.Values[LanguageService.SETTING] = "xx";
        var service = NewService(settings, NewLocalizer(), "ru-RU");

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal("ru", service.Current.Code);
        Assert.Equal("ru", settings.Values[LanguageService.SETTING]);
    }

    /// <summary>
    /// A language the user picks is applied at once and stored for the next start.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Select_AppliesAndStoresTheLanguage()
    {
        var settings = new MemorySettings();
        var localizer = NewLocalizer();
        var service = NewService(settings, localizer, "en-US");
        await service.InitializeAsync(CancellationToken.None);

        await service.SelectAsync("ru", CancellationToken.None);

        Assert.Equal("ru", localizer.Current.Code);
        Assert.Equal("ru", settings.Values[LanguageService.SETTING]);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SelectAsync("xx", CancellationToken.None));
        Assert.Equal("ru", settings.Values[LanguageService.SETTING]);
    }

    /// <summary>
    /// The selector offers every language with its flag and shows the language in use, including the one the system
    /// chose on the first start.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Selector_ShowsTheChosenLanguageAndFlags()
    {
        var localizer = NewLocalizer();
        var service = NewService(new MemorySettings(), localizer, "ru-RU");
        await service.InitializeAsync(CancellationToken.None);

        var selector = new LanguageViewModel(service, code => code == "en" ? null : Substitute.For<Avalonia.Media.IImage>());

        Assert.Equal(["Deutsch", "English", "Español", "Français", "Português", "Русский"], selector.Options.Select(o => o.Name));
        Assert.Equal("ru", selector.Selected.Code);
        Assert.Null(selector.Options.Single(o => o.Code == "en").Flag);
        Assert.NotNull(selector.Options.Single(o => o.Code == "ru").Flag);
    }

    /// <summary>
    /// Picking a language in the selector switches the interface and stores the choice.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Selector_SwitchesTheInterfaceAndStoresTheChoice()
    {
        var settings = new MemorySettings();
        var localizer = NewLocalizer();
        var service = NewService(settings, localizer, "ru-RU");
        await service.InitializeAsync(CancellationToken.None);
        var selector = new LanguageViewModel(service);

        selector.Selected = selector.Options.Single(o => o.Code == "en");
        await Eventually(() => settings.Values[LanguageService.SETTING] == "en");

        Assert.Equal("en", localizer.Current.Code);
    }

    /// <summary>
    /// The texts composed by the view models are worded again when the language changes, without a restart.
    /// </summary>
    [Fact]
    public void ViewModel_WordsItsTextsAgainWhenTheLanguageChanges()
    {
        var localizer = NewLocalizer("ru");
        using var scope = Texts.Scope(localizer);
        var viewModel = NewViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var written = NewViewModel();
        written.NewSessionName = "Friday films";
        Assert.Equal("Пуск", viewModel.PlayButtonText);
        Assert.Equal("Микрофон вкл.", viewModel.MicrophoneText);
        Assert.Equal("Вечер кино", viewModel.NewSessionName);

        localizer.SetLanguage("en");

        Assert.Equal("Movie night", viewModel.NewSessionName);
        Assert.Equal("Friday films", written.NewSessionName);
        Assert.Equal("Play", viewModel.PlayButtonText);
        Assert.Equal("Microphone on", viewModel.MicrophoneText);
        Assert.Equal("Camera on", viewModel.CameraText);
        Assert.Equal("Install (29 MB)", viewModel.VideoComponent.InstallText);
        Assert.Contains(nameof(MainWindowViewModel.PlayButtonText), changed);
        Assert.Contains(nameof(MainWindowViewModel.VideoPlaceholder), changed);
    }

    /// <summary>
    /// The messages of the code follow the language: the event feed of the session in English.
    /// </summary>
    [Fact]
    public void SessionTexts_AreEnglishInEnglish()
    {
        using var scope = Texts.Scope(NewLocalizer("en"));
        var state = PlaybackState.Initial(_host, 0) with { Cause = PlaybackCause.Seek, Position = TimeSpan.FromSeconds(3905) };

        Assert.Equal("Marina seeks to 1:05:05", SessionTexts.Describe(state, "Marina"));
        Assert.Equal("The host declined the request. To try again, ask them for a new invitation.", SessionTexts.DescribeRejection(RejectReason.Declined));
    }

    /// <summary>
    /// A rejection is worded by the language of the receiver, whatever language the host uses: the wire message carries
    /// the reason and not a text.
    /// </summary>
    [Fact]
    public void Rejection_IsWordedByTheReceiver()
    {
        using (Texts.Scope(NewLocalizer("ru")))
        {
            Assert.Equal("Ведущий отклонил запрос. Чтобы попробовать ещё раз, попросите у него новое приглашение.", SessionTexts.DescribeRejection(RejectReason.Declined));
        }

        using (Texts.Scope(NewLocalizer("en")))
        {
            Assert.Equal("The host declined the request. To try again, ask them for a new invitation.", SessionTexts.DescribeRejection(RejectReason.Declined));
        }

        using (Texts.Scope(NewLocalizer("en")))
        {
            Assert.Equal("The host did not admit you.", SessionTexts.DescribeRejection((RejectReason)99));
        }
    }

    /// <summary>
    /// Errors raised deep in the code reach the user in the language of the interface.
    /// </summary>
    [Fact]
    public void ErrorMessages_FollowTheLanguage()
    {
        using (Texts.Scope(NewLocalizer("en")))
        {
            Assert.Equal("This is not a Golether invitation.", Assert.Throws<FormatException>(() => Invite.ParseLink("hello")).Message);
            Assert.Equal("The invitation is damaged.", Assert.Throws<FormatException>(() => Invite.ParseLink(Invite.LINK_PREFIX + "!!!")).Message);
        }

        using (Texts.Scope(NewLocalizer("ru")))
        {
            Assert.Equal("Это не приглашение Golether.", Assert.Throws<FormatException>(() => Invite.ParseLink("hello")).Message);
        }
    }

    /// <summary>
    /// Numbers, units and the names of the components follow the language too.
    /// </summary>
    [Fact]
    public void Formatting_FollowsTheLanguage()
    {
        using (Texts.Scope(NewLocalizer("en")))
        {
            Assert.Equal("1,240 ms", DisplayFormat.Ping(1240));
            Assert.Equal("−1.8 s", DisplayFormat.Drift(TimeSpan.FromSeconds(-1.84)));
            Assert.Equal("+90 ms", DisplayFormat.Drift(TimeSpan.FromMilliseconds(90)));
            Assert.Equal("64.2 GB", DisplayFormat.Size((long)(64.2 * 1024 * 1024 * 1024)));
            Assert.Equal("Video component (libmpv)", ComponentCatalog.Describe(ComponentId.Video).Title);
            Assert.Equal("Track 3 · SUBRIP · from file", new MediaTrack(3, MediaTrackKind.Subtitle, null, null, "subrip", null, false, true, false).DisplayName);
        }

        using (Texts.Scope(NewLocalizer("ru")))
        {
            Assert.Equal("−1,8 с", DisplayFormat.Drift(TimeSpan.FromSeconds(-1.84)));
            Assert.Equal("64,2 ГБ", DisplayFormat.Size((long)(64.2 * 1024 * 1024 * 1024)));
            Assert.Equal("Компонент видео (libmpv)", ComponentCatalog.Describe(ComponentId.Video).Title);
        }
    }

    /// <summary>
    /// The diagnostic report is worded in the language of the interface too.
    /// </summary>
    [Fact]
    public void DiagnosticReport_FollowsTheLanguage()
    {
        using var scope = Texts.Scope(NewLocalizer("en"));

        var text = DiagnosticReport.Build("0.1.0", "A249-B9CC", [], null, ["Player: works"], ["line"], new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));

        Assert.Contains("Golether report of 2026-09-21 12:00:00", text, StringComparison.Ordinal);
        Assert.Contains("Version: 0.1.0", text, StringComparison.Ordinal);
        Assert.Contains("Device: A249-B9CC", text, StringComparison.Ordinal);
        Assert.Contains("Session: not running.", text, StringComparison.Ordinal);
        Assert.Contains("Log (last 1 lines):", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The role of a participant and the waiting notice are worded in English.
    /// </summary>
    [Fact]
    public void ParticipantTexts_AreEnglishInEnglish()
    {
        using var scope = Texts.Scope(NewLocalizer("en"));
        var tile = new ParticipantItemViewModel(_host, (_, _, _) => Task.CompletedTask, (_, _) => { });
        tile.Update(new ParticipantView(new ParticipantInfo(_host, "Anna", true), new ParticipantStatus { PeerId = _host }, true), null);

        Assert.Equal("host · you", tile.RoleText);
        Assert.Equal("Restore sound", new ParticipantItemViewModel(_guest, (_, _, _) => Task.CompletedTask, (_, _) => { }) { VoiceVolume = 0 }.VoiceMuteText);
        Assert.Equal("Waiting for the participants to be ready…", MainWindowViewModel.DescribeWaiting(
            new SessionSnapshot { IsHost = true, State = SessionState.Active, SessionName = "Movie night", HostPeerId = _host, Participants = [] },
            null));
    }

    /// <summary>
    /// Every value of the enumerations the texts are chosen by has a text in every language, so no state shows a raw
    /// key.
    /// </summary>
    /// <param name="code">The language.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void EveryEnumerationValue_HasATextInEveryLanguage(string code)
    {
        var pack = LanguageCatalog.LoadEmbedded().Find(code)!;
        var missing = new List<string>();
        void Check(string prefix, IEnumerable<object> values)
        {
            missing.AddRange(values.Select(v => prefix + v).Where(key => !pack.TryGet(key, out _)));
        }

        Check("Session.Playback.", Enum.GetValues<PlaybackCause>().Cast<object>());
        Check("Session.Reject.", Enum.GetValues<RejectReason>().Cast<object>());
        Check("Component.Source.", Enum.GetValues<ComponentSource>().Cast<object>());
        Check("Component.Stage.", Enum.GetValues<InstallStage>().Cast<object>());
        Check("Log.Level.", Enum.GetValues<LogLevel>().Cast<object>());

        Assert.Empty(missing);
    }

    /// <summary>
    /// Creates a main view model over stubs.
    /// </summary>
    /// <returns>The view model.</returns>
    private static MainWindowViewModel NewViewModel()
    {
        var components = Substitute.For<IComponentService>();
        foreach (var id in new[] { ComponentId.Video, ComponentId.Conference })
        {
            // The size is fixed here: the pinned builds change, and the texts of the button must not change with them.
            var package = ComponentCatalog.Find(id, "win-x64")! with { Size = 29 * 1024 * 1024 };
            components.GetStatus(id).Returns(new ComponentStatus(id, ComponentSource.Missing, null, package, null));
        }

        return new MainWindowViewModel(
            Substitute.For<ISessionService>(),
            Substitute.For<IDialogService>(),
            Substitute.For<ISettingsStore>(),
            new InlineDispatcher(),
            _ => throw new NotSupportedException(),
            "A249-B9CC-3AAB-52AC",
            new UnavailableConferenceMedia("нет"),
            components);
    }

    /// <summary>
    /// Waits for a condition that a background task establishes.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }

        Assert.True(condition());
    }

    /// <summary>
    /// Runs posted work at once.
    /// </summary>
    private sealed class InlineDispatcher : IUiDispatcher
    {
        /// <inheritdoc />
        public void Post(Action action) => action();
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
