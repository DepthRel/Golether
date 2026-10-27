using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Session;
using Golether.Sync.Protocol;

namespace Golether.Sync.Tests;

/// <summary>
/// Tests of the messages of a shown folder: what a participant accepts from the host.
/// </summary>
public sealed class GalleryProtocolTests
{
    /// <summary>
    /// A well-formed identifier of a showing.
    /// </summary>
    private static readonly string _catalog = new('c', 32);

    /// <summary>
    /// Makes a picture with a well-formed key.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The picture.</returns>
    private static GalleryItem Item(string name = "IMG_0001.jpg") => new(name, 1000, new string('a', 64));

    /// <summary>
    /// A page whose numbers agree and whose pictures are acceptable passes; the end of a showing is understood.
    /// </summary>
    [Fact]
    public void Page_IsAcceptedWhenItsNumbersAgree()
    {
        var page = new GalleryMessage(_catalog, "Trip", 3, 2, [Item()]);
        Assert.Equal(page, page.Sanitize());

        var closed = new GalleryMessage(string.Empty, "ignored", 5, 0, [Item()]).Sanitize();
        Assert.Equal(string.Empty, closed!.CatalogId);
        Assert.Empty(closed.Items);
    }

    /// <summary>
    /// A page with a bad identifier, numbers that do not agree, too many pictures or a broken picture is dropped.
    /// </summary>
    [Fact]
    public void Page_IsDroppedWhenAnythingIsWrong()
    {
        Assert.Null(new GalleryMessage("not-hex", "x", 1, 0, [Item()]).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", 1, 1, [Item()]).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", 1, -1, [Item()]).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", GalleryItem.MAX_ITEMS + 1, 0, [Item()]).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", 500, 0, Enumerable.Repeat(Item(), GalleryMessage.PAGE_SIZE + 1).ToArray()).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", 1, 0, [Item("../../etc/passwd")]).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", 1, 0, [Item() with { Key = "short" }]).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", 1, 0, [Item() with { Length = GalleryItem.MAX_LENGTH + 1 }]).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", 1, 0, [null!]).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", 1, 0, [Item() with { Kind = (GalleryItemKind)7 }]).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", 1, 0, [Item() with { Folder = "../outside" }]).Sanitize());
        Assert.Null(new GalleryMessage(_catalog, "x", 1, 0, [Item() with { Folder = "a\\b" }]).Sanitize());
    }

    /// <summary>
    /// A video may be as large as a film; its nested folder is shown as it came when it is clean.
    /// </summary>
    [Fact]
    public void Page_AcceptsVideosAndNestedFolders()
    {
        var video = Item("clip.mp4") with { Kind = GalleryItemKind.Video, Length = 40L * 1024 * 1024 * 1024, Folder = "Day 2/Evening" };
        var page = new GalleryMessage(_catalog, "Trip", 1, 0, [video]);

        Assert.Equal(page, page.Sanitize());
    }

    /// <summary>
    /// A path of nested folders is made harmless to show: references to a parent, empty names and the other separator
    /// go, and a long path is cut.
    /// </summary>
    [Fact]
    public void Folder_IsNormalized()
    {
        Assert.Equal("a/b/c", GalleryItem.NormalizeFolder(@"..\a//b/./c/"));
        Assert.Equal(string.Empty, GalleryItem.NormalizeFolder(null));
        Assert.Equal("x_y", GalleryItem.NormalizeFolder("x\u0001y"));
        Assert.Equal(GalleryItem.MAX_FOLDER_LENGTH, GalleryItem.NormalizeFolder(new string('f', 300)).Length);
    }

    /// <summary>
    /// The title is only shown: control characters and separators are made harmless rather than refused.
    /// </summary>
    [Fact]
    public void Page_TitleIsNormalized()
    {
        var page = new GalleryMessage(_catalog, "a/b\u0001c", 1, 0, [Item()]).Sanitize();

        Assert.Equal("a_b_c", page!.Title);
    }

    /// <summary>
    /// A choice of a picture fits only the showing it names and only a picture it has.
    /// </summary>
    [Fact]
    public void Show_FitsOnlyItsShowing()
    {
        var show = new GalleryShowMessage(_catalog, 2, 7, null);

        Assert.True(show.Fits(_catalog, 3));
        Assert.False(show.Fits(_catalog, 2));
        Assert.False(show.Fits(new string('d', 32), 3));
        Assert.False(show.Fits(null, 3));
        Assert.False((show with { Index = -1 }).Fits(_catalog, 3));
    }

    /// <summary>
    /// A small picture must be a JPEG within the size limit; anything else is dropped to nothing.
    /// </summary>
    [Fact]
    public void Thumbnail_KeepsOnlyJpegs()
    {
        var jpeg = new byte[100];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        jpeg[2] = 0xFF;

        Assert.Equal(jpeg, new ThumbnailMessage(_catalog, 0, jpeg).Sanitize().Image);
        Assert.Null(new ThumbnailMessage(_catalog, 0, [1, 2, 3, 4]).Sanitize().Image);
        Assert.Null(new ThumbnailMessage(_catalog, 0, new byte[ThumbnailMessage.MAX_IMAGE_BYTES + 1]).Sanitize().Image);
    }

    /// <summary>
    /// The messages travel through the protocol and the flag of a capable participant goes with the hello.
    /// </summary>
    [Fact]
    public void Messages_SurviveTheProtocol()
    {
        var sender = PeerId.Parse(new string('b', 64));
        SessionMessage[] messages =
        [
            new GalleryMessage(_catalog, "Trip", 1, 0, [Item()]),
            new GalleryShowMessage(_catalog, 0, 3, sender),
            new ThumbnailMessage(_catalog, 0, null),
        ];

        foreach (var message in messages)
        {
            var back = SessionMessageChannel.Deserialize(SessionMessageChannel.Serialize(message));
            Assert.Equal(message.GetType(), back.GetType());
        }

        var page = (GalleryMessage)SessionMessageChannel.Deserialize(SessionMessageChannel.Serialize(messages[0]));
        Assert.Equal(Item(), page.Items.Single());

        var hello = (HelloMessage)SessionMessageChannel.Deserialize(SessionMessageChannel.Serialize(new HelloMessage(1, "Anna", null) { Gallery = true }));
        Assert.True(hello.Gallery);
    }

    /// <summary>
    /// A page of the largest size with the longest names still fits into one control message.
    /// </summary>
    [Fact]
    public void FullPage_FitsIntoOneMessage()
    {
        var name = new string('я', MediaDescriptor.MAX_FILE_NAME_LENGTH);
        var folder = new string('я', GalleryItem.MAX_FOLDER_LENGTH);
        var item = Item(name) with { Folder = folder, Kind = GalleryItemKind.Video, Length = long.MaxValue };
        var page = new GalleryMessage(_catalog, name, GalleryItem.MAX_ITEMS, 0, Enumerable.Repeat(item, GalleryMessage.PAGE_SIZE).ToArray());

        Assert.True(SessionMessageChannel.Serialize(page).Length <= SessionMessageChannel.MAX_MESSAGE_SIZE);
    }

    /// <summary>
    /// The picture a participant looks at alone is bounded like every number of a status.
    /// </summary>
    [Fact]
    public void Status_BoundsThePictureLookedAtAlone()
    {
        var status = new ParticipantStatus { PeerId = PeerId.Parse(new string('b', 64)), GalleryView = 5 };

        Assert.Equal(5, status.Sanitize().GalleryView);
        Assert.Null((status with { GalleryView = -1 }).Sanitize().GalleryView);
        Assert.Null((status with { GalleryView = GalleryItem.MAX_ITEMS }).Sanitize().GalleryView);
    }
}
