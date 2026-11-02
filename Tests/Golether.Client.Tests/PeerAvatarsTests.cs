using Golether.Client.Services;
using Golether.Core.Identity;
using Golether.Session;

namespace Golether.Client.Tests;

/// <summary>
/// Tests of <see cref="PeerAvatars"/>: the bytes come from other devices and must never break the screen.
/// </summary>
public sealed class PeerAvatarsTests
{
    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId _peer = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// A picture is kept for a tile that appears later.
    /// </summary>
    [Fact]
    public void Apply_KeepsThePicture()
    {
        var avatars = new PeerAvatars(bytes => bytes);

        var image = avatars.Apply(new AvatarUpdate(_peer, [1, 2], false));

        Assert.NotNull(image);
        Assert.Same(image, avatars.Find(_peer));
    }

    /// <summary>
    /// A participant who took the picture away has none any more.
    /// </summary>
    [Fact]
    public void Apply_WithoutAPicture_ForgetsTheOldOne()
    {
        var avatars = new PeerAvatars(bytes => bytes);
        avatars.Apply(new AvatarUpdate(_peer, [1, 2], false));

        var image = avatars.Apply(new AvatarUpdate(_peer, null, false));

        Assert.Null(image);
        Assert.Null(avatars.Find(_peer));
    }

    /// <summary>
    /// Bytes that are no picture are not shown, and they do not fail.
    /// </summary>
    [Fact]
    public void Apply_WithBrokenBytes_ShowsNothing()
    {
        var avatars = new PeerAvatars(_ => throw new ArgumentException("no picture"));

        var image = avatars.Apply(new AvatarUpdate(_peer, [1, 2], false));

        Assert.Null(image);
        Assert.Null(avatars.Find(_peer));
    }

    /// <summary>
    /// The pictures do not outlive the session.
    /// </summary>
    [Fact]
    public void Clear_ForgetsEverybody()
    {
        var avatars = new PeerAvatars(bytes => bytes);
        avatars.Apply(new AvatarUpdate(_peer, [1, 2], false));

        avatars.Clear();

        Assert.Null(avatars.Find(_peer));
    }
}
