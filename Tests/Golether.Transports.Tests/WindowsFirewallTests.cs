using Golether.Core.Data.Enums;
using Golether.Transports.PortMapping;

namespace Golether.Transports.Tests;

/// <summary>
/// Tests of the inbound firewall rule of the application.
/// </summary>
public sealed class WindowsFirewallTests
{
    /// <summary>
    /// The helper runs with administrator rights, so it accepts only its two verbs and nothing else: no paths, no
    /// program names, nothing that could be aimed somewhere else.
    /// </summary>
    [Fact]
    public void Helper_AcceptsOnlyItsOwnVerbs()
    {
        Assert.True(WindowsFirewall.IsHelperInvocation([WindowsFirewall.Switch, WindowsFirewall.AllowVerb]));
        Assert.False(WindowsFirewall.IsHelperInvocation([]));
        Assert.False(WindowsFirewall.IsHelperInvocation(["--something-else", WindowsFirewall.AllowVerb]));

        Assert.True(WindowsFirewall.TryParse([WindowsFirewall.Switch, WindowsFirewall.AllowVerb], out var allow));
        Assert.Equal(WindowsFirewall.AllowVerb, allow);
        Assert.True(WindowsFirewall.TryParse([WindowsFirewall.Switch, WindowsFirewall.RemoveVerb], out var remove));
        Assert.Equal(WindowsFirewall.RemoveVerb, remove);

        Assert.False(WindowsFirewall.TryParse([WindowsFirewall.Switch], out _));
        Assert.False(WindowsFirewall.TryParse([WindowsFirewall.Switch, "drop"], out _));
        Assert.False(WindowsFirewall.TryParse([WindowsFirewall.Switch, WindowsFirewall.AllowVerb, "C:\\evil.exe"], out _));
        Assert.False(WindowsFirewall.TryParse(["--tunnel-helper", WindowsFirewall.AllowVerb], out _));
        Assert.Equal(WindowsFirewall.InvalidArguments, WindowsFirewall.Run([WindowsFirewall.Switch, "drop"]));
    }

    /// <summary>
    /// The arguments of the helper name the verb and nothing else.
    /// </summary>
    [Fact]
    public void Arguments_CarryTheVerbOnly()
        => Assert.Equal([WindowsFirewall.Switch, WindowsFirewall.AllowVerb], WindowsFirewall.BuildArguments(WindowsFirewall.AllowVerb));

    /// <summary>
    /// Reading the state needs no administrator rights and never throws: an unreadable firewall is reported as
    /// unknown, and the user is not bothered with a prompt for nothing.
    /// </summary>
    [Fact]
    public void Check_IsSafeToCallWithoutRights()
    {
        var state = WindowsFirewall.Check();
        Assert.True(Enum.IsDefined(state));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(FirewallState.Allowed, state);
        }

        // A program nobody has a rule for is either missing or unreadable, never "allowed".
        var unknown = WindowsFirewall.Check(Path.Combine(Path.GetTempPath(), "golether-no-such-program.exe"));
        Assert.NotEqual(OperatingSystem.IsWindows() ? FirewallState.Allowed : FirewallState.Missing, unknown);
    }
}
