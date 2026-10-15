namespace Golether.Conventions.Tests;

/// <summary>
/// The assertion of the convention tests.
/// </summary>
internal static class ConventionAssert
{
    /// <summary>
    /// Fails with the whole list of findings, so every one of them can be found from the message.
    /// </summary>
    /// <param name="findings">The places that break the rule, as <c>path</c> or <c>path:line</c>.</param>
    /// <param name="rule">What the places break.</param>
    public static void None(IEnumerable<string> findings, string rule)
    {
        var all = findings.ToList();

        if (all.Count > 0)
        {
            Assert.Fail(rule + Environment.NewLine + string.Join(Environment.NewLine, all));
        }
    }
}
