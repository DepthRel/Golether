using Microsoft.Extensions.Logging;
using Golether.Core.Data.Stores;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Media.Conference;
using Golether.Media.Streaming.Gallery;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Transports.PortMapping;

namespace Golether.Session.Hosting;

/// <summary>
/// Everything a hosted session is made of: the device, the people and players around it, and the way to the network.
/// </summary>
public sealed class HostingSetup
{
    /// <summary>
    /// Gets the device key of the host.
    /// </summary>
    public required DeviceIdentity Identity { get; init; }

    /// <summary>
    /// Gets what asks the host whether a participant may come in.
    /// </summary>
    public required IAdmissionPrompt Prompt { get; init; }

    /// <summary>
    /// Gets the contacts the participants are remembered in, or <see langword="null"/>.
    /// </summary>
    public IContactStore? Contacts { get; init; }

    /// <summary>
    /// Gets the player of the host.
    /// </summary>
    public required IPlaybackController Player { get; init; }

    /// <summary>
    /// Gets the time source.
    /// </summary>
    public required TimeProvider TimeProvider { get; init; }

    /// <summary>
    /// Gets the logger factory.
    /// </summary>
    public required ILoggerFactory LoggerFactory { get; init; }

    /// <summary>
    /// Gets the cameras and voices, or <see langword="null"/>.
    /// </summary>
    public IConferenceMedia? Conference { get; init; }

    /// <summary>
    /// Gets the name of the session.
    /// </summary>
    public required string SessionName { get; init; }

    /// <summary>
    /// Gets the name of the host.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the port participants connect to; zero lets the system choose a free one.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Gets the options of the session, the largest number of people among them.
    /// </summary>
    public required SessionOptions Options { get; init; }

    /// <summary>
    /// Gets what makes the small pictures of a shown folder, or <see langword="null"/>.
    /// </summary>
    public IThumbnailRenderer? Thumbnails { get; init; }

    /// <summary>
    /// Gets what decodes HEIC, AVIF and TIFF pictures and frames of videos of a shown folder, or
    /// <see langword="null"/>.
    /// </summary>
    public IFrameExtractor? Frames { get; init; }

    /// <summary>
    /// Gets the folder where a shown folder keeps the JPEG files the host makes, or <see langword="null"/> for a folder
    /// of the system's temporary files.
    /// </summary>
    public string? GalleryDirectory { get; init; }

    /// <summary>
    /// Gets the rendezvous server and the room of the session, or <see langword="null"/> for a session without one.
    /// </summary>
    public RendezvousPoint? Rendezvous { get; init; }

    /// <summary>
    /// Gets what opens the ports on the router, or <see langword="null"/> to leave the router alone.
    /// </summary>
    public PortMapper? PortMapper { get; init; }
}
