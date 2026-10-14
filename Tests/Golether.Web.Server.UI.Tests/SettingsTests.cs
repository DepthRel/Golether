using System.Text.Json.Nodes;
using Golether.Web.Server.Options;
using Golether.Web.Server.UI.Services;
using Golether.Web.Server.UI.ViewModels;
using NSubstitute;

namespace Golether.Web.Server.UI.Tests;

/// <summary>
/// Tests of the settings: where they are found, how they are read and written, and how the form checks them.
/// </summary>
[Collection(TextsCollection.Name)]
public sealed class SettingsTests : IDisposable
{
    /// <summary>
    /// A folder of the test with a server layout in it.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"golether-monitor-{Guid.NewGuid():N}");

    /// <summary>
    /// The settings file of the test.
    /// </summary>
    private readonly string _file;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsTests"/> class.
    /// </summary>
    public SettingsTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        Directory.CreateDirectory(Path.Combine(_root, "monitor"));
        _file = Path.Combine(_root, "config", "appsettings.json");
        File.WriteAllText(_file, """
            {
              "Rendezvous": { "ListenAddress": "0.0.0.0", "Port": 47900, "EnableStun": true, "AnswerTimeout": "00:00:20" },
              "Urls": "http://0.0.0.0:47980",
              "Logging": { "LogLevel": { "Default": "Information" } }
            }
            """);
    }

    /// <summary>
    /// The monitor lies in <c>monitor/</c> of the server folder and finds the settings one level up.
    /// </summary>
    [Fact]
    public void Paths_FindTheSettingsNextToTheMonitor()
        => Assert.Equal(_file, ServerPaths.Find(Path.Combine(_root, "monitor")).SettingsFile);

    /// <summary>
    /// The settings of the monitor live next to those of the server, in a file of their own; an empty language means
    /// "the language of the system"; a file that cannot be written is reported, not thrown.
    /// </summary>
    [Fact]
    public void MonitorSettings_LiveNextToTheServerSettings()
    {
        var preferences = MonitorPreferences.NextTo(ServerPaths.Find(Path.Combine(_root, "monitor")));
        Assert.Equal(Path.Combine(_root, "config", "monitor.json"), preferences.Path);
        Assert.Null(preferences.LoadLanguage());

        File.WriteAllText(preferences.Path, """{ "language": "" }""");
        Assert.Null(preferences.LoadLanguage());

        Assert.True(preferences.SaveLanguage("en"));
        Assert.Equal("en", preferences.LoadLanguage());
        Assert.Contains("47900", File.ReadAllText(_file), StringComparison.Ordinal);

        // A file where the folder should be: the file of the monitor cannot be written there.
        File.WriteAllText(Path.Combine(_root, "blocked"), string.Empty);
        Assert.False(new MonitorPreferences(Path.Combine(_root, "blocked", "monitor.json")).SaveLanguage("en"));
    }

    /// <summary>
    /// The file is read with the defaults for what it does not say; the status address listening everywhere is
    /// reached on this computer.
    /// </summary>
    [Fact]
    public void Store_ReadsTheFile()
    {
        var store = new ServerSettingsStore(_file, Substitute.For<IServiceManager>());

        var settings = store.Load();

        Assert.Equal(47900, settings.Rendezvous.Port);
        Assert.Equal(TimeSpan.FromSeconds(20), settings.Rendezvous.AnswerTimeout);
        Assert.Equal(new RendezvousServerOptions().MaxRooms, settings.Rendezvous.MaxRooms);
        Assert.Equal("http://0.0.0.0:47980", settings.StatusUrls);
        Assert.Equal("Information", settings.LogLevel);
        Assert.Equal(new Uri("http://127.0.0.1:47980/"), store.StatusUrl());
    }

    /// <summary>
    /// Writing changes only what the monitor edits: the status address and the default level of the log are written,
    /// the levels of single sources and everything else stay.
    /// </summary>
    [Fact]
    public void Render_KeepsWhatTheMonitorDoesNotEdit()
    {
        var store = new ServerSettingsStore(_file, Substitute.For<IServiceManager>());
        var settings = store.Load();
        File.WriteAllText(_file, File.ReadAllText(_file).Replace("\"Default\": \"Information\"", "\"Default\": \"Information\", \"Microsoft.AspNetCore\": \"Warning\"", StringComparison.Ordinal));
        var changed = settings with { Rendezvous = settings.Rendezvous with { Port = 48000, MaxRooms = 7 }, StatusUrls = "http://0.0.0.0:48080", LogLevel = "Debug" };

        var root = (JsonObject)JsonNode.Parse(ServerSettingsStore.Render(File.ReadAllText(_file), changed))!;

        Assert.Equal(48000, root["Rendezvous"]!["Port"]!.GetValue<int>());
        Assert.Equal(7, root["Rendezvous"]!["MaxRooms"]!.GetValue<int>());
        Assert.Equal("00:00:20", root["Rendezvous"]!["AnswerTimeout"]!.GetValue<string>());
        Assert.Null(root["Rendezvous"]!["ParsedListenAddress"]);
        Assert.Equal("http://0.0.0.0:48080", root["Urls"]!.GetValue<string>());
        Assert.Equal("Debug", root["Logging"]!["LogLevel"]!["Default"]!.GetValue<string>());
        Assert.Equal("Warning", root["Logging"]!["LogLevel"]!["Microsoft.AspNetCore"]!.GetValue<string>());
    }

    /// <summary>
    /// With a service the new file is put in place by the service manager (one question to the user), from a copy
    /// that is removed afterwards; without one the file is written directly.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Save_GoesThroughTheServiceWhenThereIsOne()
    {
        var service = Substitute.For<IServiceManager>();
        string? staged = null;
        service.ReplaceSettingsAndRestartAsync(Arg.Do<string>(s => staged = s), _file, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var store = new ServerSettingsStore(_file, service);
        var settings = store.Load();

        await store.SaveAsync(settings with { StatusUrls = "http://127.0.0.1:48001" }, serviceInstalled: true, TestContext.Current.CancellationToken);
        Assert.NotNull(staged);
        Assert.False(File.Exists(staged));
        Assert.Equal("http://0.0.0.0:47980", store.Load().StatusUrls);

        await store.SaveAsync(settings with { StatusUrls = "http://127.0.0.1:48002" }, serviceInstalled: false, TestContext.Current.CancellationToken);
        Assert.Equal("http://127.0.0.1:48002", store.Load().StatusUrls);
        Assert.Equal(new Uri("http://127.0.0.1:48002/"), store.StatusUrl());
        await service.ReceivedWithAnyArgs(1).ReplaceSettingsAndRestartAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The form refuses a field that is not a number and a value the server would refuse, in words; it accepts a
    /// valid change and tells that something changed.
    /// </summary>
    [Fact]
    public void Form_ChecksTheFieldsByTheRulesOfTheServer()
    {
        var form = new SettingsViewModel();
        form.Show(new ServerSettings(new RendezvousServerOptions(), "http://127.0.0.1:47980", "Information"));
        Assert.False(form.IsChanged);

        form.Port = "сорок";
        Assert.True(form.IsChanged);
        Assert.False(form.TryValidate(out _));
        Assert.Contains("Порт", form.Problems, StringComparison.Ordinal);

        form.Port = "70000";
        Assert.False(form.TryValidate(out _));
        Assert.Contains("Rendezvous:Port", form.Problems, StringComparison.Ordinal);

        form.Port = "48000";
        form.AnswerTimeout = "30";
        Assert.True(form.TryValidate(out var settings));
        Assert.Equal(48000, settings.Rendezvous.Port);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.Rendezvous.AnswerTimeout);
        Assert.Empty(form.Problems);

        form.StatusUrls = "127.0.0.1";
        Assert.False(form.TryValidate(out _));
        Assert.Contains("127.0.0.1", form.Problems, StringComparison.Ordinal);
        form.StatusUrls = "http://+:47980;http://127.0.0.1:47981";
        form.LogLevel = "Debug";
        Assert.True(form.TryValidate(out settings));
        Assert.Equal("Debug", settings.LogLevel);
    }

    /// <summary>
    /// Every level of the log has words in every language.
    /// </summary>
    /// <param name="code">The language.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void LogLevels_HaveWordsInEveryLanguage(string code)
    {
        var pack = Golether.Localization.LanguageCatalog.LoadEmbedded().Find(code)!;
        Assert.DoesNotContain(ServerSettings.LogLevels, level => !pack.TryGet("Monitor.LogLevel." + level, out _));
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
