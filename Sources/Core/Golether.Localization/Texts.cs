namespace Golether.Localization;

/// <summary>
/// The texts of the running application. Code that raises a message (exceptions, events, notices) has no view model
/// to ask, so it asks here; the message then follows the language the user chose.
/// </summary>
/// <remarks>
/// Until the application picks a language, the default one is used. The application replaces
/// <see cref="Localizer"/> once, at start-up. A piece of work that must speak another language than the rest of the
/// process, a test for one, runs inside <see cref="Scope"/>.
/// </remarks>
public static class Texts
{
    /// <summary>
    /// The localizer in use by the whole process.
    /// </summary>
    private static volatile ILocalizer _localizer = new Localizer(LanguageCatalog.LoadEmbedded());

    /// <summary>
    /// The localizer of the current flow of work, or <see langword="null"/> when it uses the one of the process.
    /// </summary>
    private static readonly AsyncLocal<ILocalizer?> Scoped = new();

    /// <summary>
    /// Gets or sets the localizer in use: the one of the current <see cref="Scope"/>, otherwise the one of the process.
    /// </summary>
    public static ILocalizer Localizer
    {
        get => Scoped.Value ?? _localizer;
        set => _localizer = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Makes the texts of the current flow of work, and of the work it starts, follow another localizer until the
    /// returned scope is disposed. Other flows are not affected.
    /// </summary>
    /// <param name="localizer">The localizer.</param>
    /// <returns>The scope to dispose to go back to the previous localizer.</returns>
    public static IDisposable Scope(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var previous = Scoped.Value;
        Scoped.Value = localizer;
        return new Restore(previous);
    }

    /// <summary>
    /// Returns a text.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The text.</returns>
    public static string Get(string key) => Localizer.Get(key);

    /// <summary>
    /// Returns a text with values put in place of <c>{0}</c>, <c>{1}</c>….
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="args">The values.</param>
    /// <returns>The text.</returns>
    public static string Format(string key, params object?[] args) => Localizer.Format(key, args);

    /// <summary>
    /// Ends a <see cref="Scope"/>.
    /// </summary>
    private sealed class Restore : IDisposable
    {
        /// <summary>
        /// The localizer of the flow before the scope, or <see langword="null"/>.
        /// </summary>
        private readonly ILocalizer? _previous;

        /// <summary>
        /// Initializes a new instance of the <see cref="Restore"/> class.
        /// </summary>
        /// <param name="previous">The localizer to go back to.</param>
        public Restore(ILocalizer? previous) => _previous = previous;

        /// <inheritdoc />
        public void Dispose() => Scoped.Value = _previous;
    }
}
