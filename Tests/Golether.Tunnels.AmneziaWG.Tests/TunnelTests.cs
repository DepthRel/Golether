using Golether.Core.Networking;
using Golether.Security.Identity;
using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Tunnels.AmneziaWG.Control;
using Golether.Tunnels.AmneziaWG.Keys;
using Golether.Tunnels.AmneziaWG.Packages;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Golether.Tunnels.AmneziaWG.Tests;

/// <summary>
/// Tests of AmneziaWG keys, parameters, configuration files, packages and the tunnel controller.
/// </summary>
public sealed class TunnelTests
{
    /// <summary>
    /// Generated keys are valid and the public key can be derived from the private key.
    /// </summary>
    [Fact]
    public void Keys_AreValidAndConsistent()
    {
        var pair = AwgKeys.Generate();

        Assert.True(AwgKeys.IsValidKey(pair.PrivateKey));
        Assert.True(AwgKeys.IsValidKey(pair.PublicKey));
        Assert.Equal(pair.PublicKey, AwgKeys.GetPublicKey(pair.PrivateKey));
        Assert.DoesNotContain(pair.PrivateKey, pair.ToString(), StringComparison.Ordinal);
        Assert.False(AwgKeys.IsValidKey("abc"));
        Assert.Throws<FormatException>(() => AwgKeys.GetPublicKey("abc"));
    }

    /// <summary>
    /// Random obfuscation parameters satisfy the AmneziaWG rules.
    /// </summary>
    [Fact]
    public void Obfuscation_GeneratedParametersAreValid()
    {
        for (var i = 0; i < 200; i++)
        {
            SharedObfuscation.Generate().Validate();
            JunkParameters.Generate().Validate();
        }
    }

    /// <summary>
    /// Parameters that break the rules are rejected.
    /// </summary>
    [Fact]
    public void Obfuscation_InvalidParametersAreRejected()
    {
        Assert.Throws<FormatException>(() => new SharedObfuscation(10, 66, 5, 6, 7, 8).Validate());
        Assert.Throws<FormatException>(() => new SharedObfuscation(10, 20, 5, 5, 7, 8).Validate());
        Assert.Throws<FormatException>(() => new SharedObfuscation(10, 20, 1, 2, 3, 4).Validate());
        Assert.Throws<FormatException>(() => new SharedObfuscation(2000, 20, 5, 6, 7, 8).Validate());
        Assert.Throws<FormatException>(() => new JunkParameters(4, 100, 50).Validate());
        Assert.Throws<FormatException>(() => new JunkParameters(4, 10, 2000).Validate());
    }

    /// <summary>
    /// A rendered configuration parses back to the same configuration.
    /// </summary>
    [Fact]
    public void Configuration_RendersAndParses()
    {
        var host = HostTunnelInterface.Create("golether0");
        var peer = new AwgPeer
        {
            Comment = "Golether: Марина\nевил",
            PublicKey = AwgKeys.Generate().PublicKey,
            PresharedKey = AwgKeys.Generate().PublicKey,
            AllowedIps = [$"{host.AddressAt(2)}/32"],
            Endpoint = PeerEndpoint.Parse("203.0.113.24:51944"),
            PersistentKeepalive = 25,
        };
        var configuration = host.BuildConfiguration([peer]);

        var text = configuration.Render();
        var parsed = AwgConfiguration.Parse(text);

        Assert.Contains("[Interface]", text, StringComparison.Ordinal);
        Assert.Contains("# Golether: Марина", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\nевил\n", text, StringComparison.Ordinal);
        Assert.Equal(configuration.Interface, parsed.Interface);
        Assert.Equal(peer.PublicKey, parsed.Peers[0].PublicKey);
        Assert.Equal(peer.PresharedKey, parsed.Peers[0].PresharedKey);
        Assert.Equal(peer.AllowedIps, parsed.Peers[0].AllowedIps);
        Assert.Equal(peer.Endpoint, parsed.Peers[0].Endpoint);
        Assert.Contains("# Golether: Маринаевил\n", text, StringComparison.Ordinal);
        Assert.Equal(text, parsed.Render());
    }

    /// <summary>
    /// Broken configuration files are rejected with a clear message.
    /// </summary>
    /// <param name="text">The file text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("PrivateKey = x")]
    [InlineData("[Interface]\nAddress = 10.0.0.1/24")]
    [InlineData("[Interface]\nPrivateKey = abc\nAddress = 10.0.0.1/24")]
    [InlineData("[Interface]\n[Interface]")]
    public void Configuration_RejectsBrokenFiles(string text) => Assert.Throws<FormatException>(() => AwgConfiguration.Parse(text));

    /// <summary>
    /// The host assigns free addresses in its subnet.
    /// </summary>
    [Fact]
    public void HostInterface_AssignsFreeAddresses()
    {
        var host = HostTunnelInterface.Create();

        var first = host.NextParticipantAddress([]);
        var second = host.NextParticipantAddress([first]);

        Assert.EndsWith(".1", host.HostAddress, StringComparison.Ordinal);
        Assert.EndsWith(".2", first, StringComparison.Ordinal);
        Assert.EndsWith(".3", second, StringComparison.Ordinal);
        Assert.StartsWith("10.", host.SubnetBase, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => HostTunnelInterface.Create("bad name with spaces"));
    }

    /// <summary>
    /// A full offer/answer exchange gives both sides the same preshared key and verification code and valid
    /// configurations that point at each other.
    /// </summary>
    [Fact]
    public void Negotiation_ProducesMatchingConfigurations()
    {
        using var hostIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        using var guestIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var hostSide = new TunnelNegotiator(hostIdentity, time);
        var guestSide = new TunnelNegotiator(guestIdentity, time);
        var hostInterface = HostTunnelInterface.Create();

        var offer = hostSide.CreateOffer(hostInterface, "Вы", [PeerEndpoint.Parse("203.0.113.24:51944")], []);
        var acceptance = guestSide.AcceptOffer(offer.PackageText, "Алексей", [PeerEndpoint.Parse("198.51.100.7:40000")], 40000);
        var completion = hostSide.CompleteOffer(offer.Secrets, acceptance.AnswerText);

        var guestPeer = acceptance.Configuration.Peers[0];
        Assert.Equal(hostIdentity.PeerId, acceptance.HostPeerId);
        Assert.Equal(guestIdentity.PeerId, completion.ParticipantPeerId);
        Assert.Equal(acceptance.VerificationCode.ToString(), completion.VerificationCode.ToString());
        Assert.Equal(guestPeer.PresharedKey, completion.Peer.PresharedKey);
        Assert.Equal(hostInterface.Keys.PublicKey, guestPeer.PublicKey);
        Assert.Equal(AwgKeys.GetPublicKey(acceptance.Configuration.Interface.PrivateKey), completion.Peer.PublicKey);
        Assert.Equal($"{offer.Secrets.AssignedAddress}/32", acceptance.Configuration.Interface.Address);
        Assert.Equal([$"{offer.Secrets.AssignedAddress}/32"], completion.Peer.AllowedIps);
        Assert.Equal([$"{hostInterface.HostAddress}/32"], guestPeer.AllowedIps);
        Assert.Equal(hostInterface.Obfuscation, acceptance.Configuration.Interface.Obfuscation);
        Assert.Equal(PeerEndpoint.Parse("203.0.113.24:51944"), guestPeer.Endpoint);
        Assert.Equal(PeerEndpoint.Parse("198.51.100.7:40000"), completion.Peer.Endpoint);
        Assert.Equal("Алексей", completion.ParticipantName);
        hostInterface.BuildConfiguration([completion.Peer]).Validate();

        Assert.DoesNotContain(hostInterface.Keys.PrivateKey, offer.PackageText, StringComparison.Ordinal);
        Assert.DoesNotContain(guestPeer.PresharedKey!, offer.PackageText + acceptance.AnswerText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Modified, expired, own and mismatched packages are rejected.
    /// </summary>
    [Fact]
    public void Negotiation_RejectsInvalidPackages()
    {
        using var hostIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        using var guestIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var hostSide = new TunnelNegotiator(hostIdentity, time);
        var guestSide = new TunnelNegotiator(guestIdentity, time);
        var hostInterface = HostTunnelInterface.Create();
        var offer = hostSide.CreateOffer(hostInterface, "Вы", [], [], TimeSpan.FromHours(1));

        var dot = offer.PackageText.LastIndexOf('.');
        var tampered = offer.PackageText[..(dot - 3)] + (offer.PackageText[dot - 3] == 'A' ? 'B' : 'A') + offer.PackageText[(dot - 2)..];
        Assert.Throws<FormatException>(() => guestSide.AcceptOffer(tampered, "g", [], 40000));
        Assert.Throws<FormatException>(() => hostSide.AcceptOffer(offer.PackageText, "self", [], 40000));
        Assert.Throws<FormatException>(() => guestSide.AcceptOffer("hello", "g", [], 40000));
        Assert.Throws<FormatException>(() => guestSide.AcceptOffer(offer.PackageText.Replace(":offer:", ":answer:", StringComparison.Ordinal), "g", [], 40000));

        var acceptance = guestSide.AcceptOffer(offer.PackageText.Insert(40, "\n  "), "g", [], 40000);
        var otherOffer = hostSide.CreateOffer(hostInterface, "Вы", [], []);
        Assert.Throws<FormatException>(() => hostSide.CompleteOffer(otherOffer.Secrets, acceptance.AnswerText));

        time.Advance(TimeSpan.FromHours(2));
        Assert.Throws<FormatException>(() => guestSide.AcceptOffer(offer.PackageText, "g", [], 40000));
        Assert.Throws<FormatException>(() => hostSide.CompleteOffer(offer.Secrets, acceptance.AnswerText));
    }

    /// <summary>
    /// On Linux and macOS the controller writes the configuration and calls awg-quick with the file path.
    /// </summary>
    [Fact]
    public async Task Controller_UsesAwgQuick()
    {
        var directory = Directory.CreateTempSubdirectory("golether-awg-");
        try
        {
            var runner = Substitute.For<IProcessRunner>();
            runner.RunAsync(default!, default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new ProcessResult(0, string.Empty, string.Empty));
            var controller = new AwgCliTunnelController(runner, new AwgCliOptions { ConfigDirectory = directory.FullName, ElevationCommand = "pkexec" }, isWindows: false);
            var configuration = HostTunnelInterface.Create().BuildConfiguration([]);

            await controller.UpAsync("golether0", configuration, TestContext.Current.CancellationToken);

            var path = controller.GetConfigPath("golether0");
            Assert.Equal(configuration.Render(), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            await runner.Received(1).RunAsync("pkexec", Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "awg-quick", "up", path })), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
            Assert.Throws<ArgumentException>(() => controller.GetConfigPath("../evil"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// On Windows the controller installs the tunnel service and reports failures.
    /// </summary>
    [Fact]
    public async Task Controller_UsesWindowsServiceAndReportsErrors()
    {
        var directory = Directory.CreateTempSubdirectory("golether-awg-");
        try
        {
            var runner = Substitute.For<IProcessRunner>();
            runner.RunAsync(default!, default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new ProcessResult(1, string.Empty, "Access denied"));
            var options = new AwgCliOptions { ConfigDirectory = directory.FullName, WindowsExecutable = @"C:\AmneziaWG\amneziawg.exe" };
            var controller = new AwgCliTunnelController(runner, options, isWindows: true);

            var error = await Assert.ThrowsAsync<TunnelControlException>(() =>
                controller.UpAsync("golether0", HostTunnelInterface.Create().BuildConfiguration([]), TestContext.Current.CancellationToken));

            Assert.Contains("Access denied", error.Message, StringComparison.Ordinal);
            await runner.Received(1).RunAsync(options.WindowsExecutable, Arg.Is<IReadOnlyList<string>>(a => a[0] == "/installtunnelservice"), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The real process runner captures output and exit codes.
    /// </summary>
    [Fact]
    public async Task ProcessRunner_CapturesOutput()
    {
        var runner = new ProcessRunner();
        var result = OperatingSystem.IsWindows()
            ? await runner.RunAsync("cmd.exe", ["/c", "echo golether"], TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)
            : await runner.RunAsync("sh", ["-c", "echo golether"], TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Contains("golether", result.StandardOutput, StringComparison.Ordinal);
    }
}
