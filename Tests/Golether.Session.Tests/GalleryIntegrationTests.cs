using System.Buffers.Binary;
using System.Text;
using NSubstitute;
using Golether.Core.Data.Enums;
using Golether.Core.Enums;
using Golether.Core.Networking;
using Golether.Core.Time;
using Golether.Media.Player.Simulation;
using Golether.Media.Streaming.Caching;
using Golether.Media.Streaming.Gallery;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Security.Invites;
using Golether.Sync.Protocol;
using Golether.Transports.Tls;

namespace Golether.Session.Tests;

/// <summary>
/// End-to-end tests of a shown folder: a host and a participant over loopback TLS.
/// </summary>
public sealed class GalleryIntegrationTests : IAsyncLifetime
{
    /// <summary>
    /// Transport options: a free port.
    /// </summary>
    private static readonly TlsTransportOptions _transport = new() { Port = 0, HandshakeTimeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Session options with short intervals to keep the test fast.
    /// </summary>
    private static readonly SessionOptions _options = new()
    {
        TickInterval = TimeSpan.FromMilliseconds(50),
        StatusInterval = TimeSpan.FromMilliseconds(200),
    };

    /// <summary>
    /// What the host's small picture of every picture is, so the test sees it arrive.
    /// </summary>
    private static readonly byte[] _thumbnail = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    /// <summary>
    /// The host device.
    /// </summary>
    private readonly DeviceIdentity _hostIdentity = DeviceIdentity.CreateNew(TimeProvider.System);

    /// <summary>
    /// The participant device.
    /// </summary>
    private readonly DeviceIdentity _guestIdentity = DeviceIdentity.CreateNew(TimeProvider.System);

    /// <summary>
    /// The shown folder.
    /// </summary>
    private readonly string _folder = Directory.CreateTempSubdirectory("golether-gallery-session-").FullName;

    /// <summary>
    /// The folder where the host keeps the files of a showing.
    /// </summary>
    private readonly string _work = Directory.CreateTempSubdirectory("golether-gallery-work-").FullName;

    /// <summary>
    /// The host player.
    /// </summary>
    private readonly SimulatedPlayer _hostPlayer = new(SystemMonotonicClock.Instance, TimeSpan.FromHours(2));

    /// <summary>
    /// The participant player.
    /// </summary>
    private readonly SimulatedPlayer _guestPlayer = new(SystemMonotonicClock.Instance, TimeSpan.FromHours(2));

    /// <summary>
    /// The host session.
    /// </summary>
    private HostSession _host = null!;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        var prompt = Substitute.For<IAdmissionPrompt>();
        prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>()).Returns(true);
        var thumbnails = Substitute.For<IThumbnailRenderer>();
        thumbnails.Render(Arg.Any<Stream>()).Returns(_thumbnail);
        var listener = new TlsPeerListener(_hostIdentity, _transport);
        listener.Start();
        _host = new HostSession(
            _hostIdentity,
            listener,
            new InviteRegistry(TimeProvider.System),
            new AdmissionService(prompt, new AdmissionOptions(), TimeProvider.System),
            contacts: null,
            _hostPlayer,
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Отпуск",
            "Вы",
            _options,
            thumbnails: thumbnails,
            galleryDirectory: _work);
        _host.Start();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _hostPlayer.DisposeAsync();
        await _guestPlayer.DisposeAsync();
        _hostIdentity.Dispose();
        _guestIdentity.Dispose();
        Directory.Delete(_folder, recursive: true);
        Directory.Delete(_work, recursive: true);
    }

    /// <summary>
    /// A participant who joins a showing receives the list, a small picture and a picture without the place it was
    /// taken at; its choice goes to everybody with its authenticated name, and looking alone is seen by the host.
    /// When the host closes the showing, it ends for the participant too.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Folder_IsShownLookedThroughAndClosed()
    {
        var token = TestContext.Current.CancellationToken;
        var photo = Jpeg("GPS: 48.8584 N, 2.2945 E");
        await File.WriteAllBytesAsync(Path.Combine(_folder, "IMG_2.jpg"), photo, token);
        await File.WriteAllBytesAsync(Path.Combine(_folder, "IMG_10.jpg"), Jpeg("another place"), token);
        await _host.ShowGalleryAsync(GalleryFolder.Describe(_folder, cancellationToken: token), stripMetadata: true, progress: null, token);

        await using var guest = await JoinAsync(token);
        await WaitUntilAsync(() => guest.GetSnapshot().Gallery is { Items: [{ }, { }] }, token);

        var gallery = guest.GetSnapshot().Gallery!;
        Assert.Equal(_host.GetSnapshot().Gallery!.CatalogId, gallery.CatalogId);
        Assert.Equal(["IMG_2.jpg", "IMG_10.jpg"], gallery.Items.Select(i => i!.FileName));

        // The picture arrives over the media streams, checked piece by piece, without its EXIF block.
        var received = await guest.LoadPictureAsync(gallery.CatalogId, 0, token);
        Assert.Equal(await _host.LoadPictureAsync(gallery.CatalogId, 0, token), received);
        Assert.DoesNotContain("GPS", Encoding.ASCII.GetString(received), StringComparison.Ordinal);
        Assert.Equal(photo[^4..], received[^4..]);
        Assert.Equal(_thumbnail, await guest.LoadThumbnailAsync(gallery.CatalogId, 1, token));

        // The participant puts the second picture on everybody's screen.
        await guest.ShowItemAsync(gallery.CatalogId, 1, token);
        await WaitUntilAsync(() => guest.GetSnapshot().Gallery?.Stage == 1, token);
        Assert.Equal(_guestIdentity.PeerId, _host.GetSnapshot().Gallery!.StageOrigin);

        // A choice of a picture of another showing changes nothing.
        await guest.ShowItemAsync(new string('f', 32), 0, token);
        await Task.Delay(300, token);
        Assert.Equal(1, _host.GetSnapshot().Gallery!.Stage);

        // Looking alone is seen by the host in the status of the participant.
        guest.SetGalleryView(0);
        await WaitUntilAsync(
            () => _host.GetSnapshot().Participants.Any(p => p.Info.PeerId == _guestIdentity.PeerId && p.Status?.GalleryView == 0),
            token);

        await _host.CloseGalleryAsync();
        await WaitUntilAsync(() => guest.GetSnapshot().Gallery is null, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.LoadPictureAsync(gallery.CatalogId, 0, token));
    }

    /// <summary>
    /// A video of a nested folder put on the screen by a participant becomes the film of everybody, and the folder
    /// stays shown; a picture put back there pauses the film, which waits under it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Video_OfTheFolderBecomesTheFilm()
    {
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllBytesAsync(Path.Combine(_folder, "IMG_1.jpg"), Jpeg("first"), token);
        Directory.CreateDirectory(Path.Combine(_folder, "Day 2"));
        await File.WriteAllBytesAsync(Path.Combine(_folder, "Day 2", "clip.mp4"), Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray(), token);
        await _host.ShowGalleryAsync(GalleryFolder.Describe(_folder, cancellationToken: token), stripMetadata: true, progress: null, token);
        await using var guest = await JoinAsync(token);
        await WaitUntilAsync(() => guest.GetSnapshot().Gallery is { Items: [{ }, { }] }, token);

        var gallery = guest.GetSnapshot().Gallery!;
        Assert.Equal(0, gallery.Stage);
        Assert.Equal(GalleryItemKind.Video, gallery.Items[1]!.Kind);
        Assert.Equal("Day 2", gallery.Items[1]!.Folder);
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.LoadPictureAsync(gallery.CatalogId, 1, token));

        await guest.ShowItemAsync(gallery.CatalogId, 1, token);
        await WaitUntilAsync(() => guest.GetSnapshot() is { Gallery.Stage: 1, Media.FileName: "clip.mp4" }, token);
        Assert.Equal(Path.Combine(_folder, "Day 2", "clip.mp4"), _host.SharedFilePath);

        await _host.RequestAsync(new PlaybackRequest(PlaybackRequestKind.Play, null), token);
        await _host.ShowItemAsync(gallery.CatalogId, 0, token);
        await WaitUntilAsync(() => guest.GetSnapshot() is { Gallery.Stage: 0, Playback.State: PlayState.Paused }, token);
        Assert.Equal("clip.mp4", guest.GetSnapshot().Media!.FileName);
    }

    /// <summary>
    /// Joins the host as a participant.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The joined participant.</returns>
    private async Task<ParticipantSession> JoinAsync(CancellationToken cancellationToken)
    {
        // A film the host shares is played from an address of the registry; any new one does here.
        var uris = Substitute.For<IMediaUriRegistry>();
        uris.Register(Arg.Any<Func<Stream>>()).Returns(_ => new Uri("golether://media/" + Guid.NewGuid().ToString("N")));
        var guest = new ParticipantSession(
            _guestIdentity,
            new TlsPeerConnector(_guestIdentity, _transport),
            _guestPlayer,
            uris,
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Марина",
            _options);
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), cancellationToken);
        return guest;
    }

    /// <summary>
    /// Builds a JPEG whose EXIF block carries a text in place of the place it was taken at.
    /// </summary>
    /// <param name="secret">The text.</param>
    /// <returns>The JPEG.</returns>
    private static byte[] Jpeg(string secret)
    {
        var exif = Encoding.ASCII.GetBytes("Exif\0\0MM\0*\0\0\0\x08\0\0" + secret);
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(exif.Length + 2));
        var scan = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 200)).ToArray();
        return [0xFF, 0xD8, 0xFF, 0xE1, .. length, .. exif, 0xFF, 0xDA, 0x00, 0x04, 0x00, 0x00, .. scan, 0xFF, 0xD9];
    }

    /// <summary>
    /// Polls a condition.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    /// <exception cref="TimeoutException">The condition did not hold within 15 seconds.</exception>
    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(25, cancellationToken);
        }
    }
}
