using Avalonia.Headless;

namespace Golether.Web.Server.UI.Tests;

/// <summary>
/// The one Avalonia session of the tests of the monitor. Windows are made on its thread, and a test that changes the
/// language of the process changes it there too, as the application does: the texts bound in a window are then told
/// on the thread that owns the window, whatever thread the dispatcher of Avalonia took first.
/// </summary>
internal static class HeadlessSession
{
    /// <summary>
    /// The session, started by the first test that needs it.
    /// </summary>
    private static readonly Lazy<HeadlessUnitTestSession> _session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

    /// <summary>
    /// Gets the session.
    /// </summary>
    public static HeadlessUnitTestSession Instance => _session.Value;
}
