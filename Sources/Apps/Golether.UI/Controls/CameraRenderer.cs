using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Golether.Core.Identity;
using Golether.Media.Conference;
using Golether.UI.ViewModels;

namespace Golether.UI.Controls;

/// <summary>
/// Turns camera frames into bitmaps of the participant tiles at up to 15 frames per second.
/// </summary>
/// <remarks>
/// Frames arrive on GStreamer threads; only the latest frame per participant is kept. On the UI thread each tile gets
/// one of two bitmaps in turn, so the image control sees a new source and redraws.
/// </remarks>
public sealed class CameraRenderer : IDisposable
{
    /// <summary>
    /// The conferencing backend.
    /// </summary>
    private readonly IConferenceMedia _conference;

    /// <summary>
    /// The view model.
    /// </summary>
    private readonly MainWindowViewModel _viewModel;

    /// <summary>
    /// The latest frames by participant (<see langword="default"/> is this device).
    /// </summary>
    private readonly Dictionary<PeerId, VideoFrame> _latest = [];

    /// <summary>
    /// The bitmap pairs by participant.
    /// </summary>
    private readonly Dictionary<PeerId, Surfaces> _surfaces = [];

    /// <summary>
    /// Guards <see cref="_latest"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The render timer.
    /// </summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(66) };

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraRenderer"/> class.
    /// </summary>
    /// <param name="conference">The conferencing backend.</param>
    /// <param name="viewModel">The view model.</param>
    public CameraRenderer(IConferenceMedia conference, MainWindowViewModel viewModel)
    {
        _conference = conference ?? throw new ArgumentNullException(nameof(conference));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _conference.VideoFrameReceived += OnFrame;
        _timer.Tick += (_, _) => Render();
        _timer.Start();
    }

    /// <summary>
    /// Stops rendering.
    /// </summary>
    public void Dispose()
    {
        _timer.Stop();
        _conference.VideoFrameReceived -= OnFrame;
        foreach (var surface in _surfaces.Values)
        {
            surface.Dispose();
        }

        _surfaces.Clear();
    }

    /// <summary>
    /// Keeps the latest frame.
    /// </summary>
    /// <param name="sender">The backend.</param>
    /// <param name="frame">The frame.</param>
    private void OnFrame(object? sender, ParticipantVideoFrame frame)
    {
        lock (_gate)
        {
            _latest[frame.PeerId] = frame.Frame;
        }
    }

    /// <summary>
    /// Copies pending frames into the tile bitmaps.
    /// </summary>
    private void Render()
    {
        KeyValuePair<PeerId, VideoFrame>[] pending;
        lock (_gate)
        {
            pending = _latest.ToArray();
            _latest.Clear();
        }

        foreach (var (peer, frame) in pending)
        {
            var tile = peer.IsEmpty
                ? _viewModel.Participants.FirstOrDefault(p => p.IsLocal)
                : _viewModel.Participants.FirstOrDefault(p => p.PeerId == peer);
            if (tile is null || (peer.IsEmpty && _viewModel.CameraOff))
            {
                continue;
            }

            if (!_surfaces.TryGetValue(peer, out var surface) || !surface.Fits(frame))
            {
                surface?.Dispose();
                surface = new Surfaces(frame.Width, frame.Height);
                _surfaces[peer] = surface;
            }

            tile.CameraImage = surface.Draw(frame);
        }
    }

    /// <summary>
    /// Two bitmaps of the same size used alternately.
    /// </summary>
    private sealed class Surfaces : IDisposable
    {
        /// <summary>
        /// The bitmaps.
        /// </summary>
        private readonly WriteableBitmap[] _bitmaps;

        /// <summary>
        /// The index of the bitmap drawn last.
        /// </summary>
        private int _current;

        /// <summary>
        /// Initializes a new instance of the <see cref="Surfaces"/> class.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        public Surfaces(int width, int height)
        {
            Width = width;
            Height = height;
            _bitmaps = [Create(), Create()];

            WriteableBitmap Create() => new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }

        /// <summary>
        /// Gets the width.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Checks whether a frame has the size of the bitmaps.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns><see langword="true"/> when the sizes match.</returns>
        public bool Fits(VideoFrame frame) => frame.Width == Width && frame.Height == Height;

        /// <summary>
        /// Draws a frame into the spare bitmap.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns>The bitmap to show.</returns>
        public WriteableBitmap Draw(VideoFrame frame)
        {
            _current = 1 - _current;
            var bitmap = _bitmaps[_current];
            using var buffer = bitmap.Lock();
            var rows = Math.Min(Height, frame.Height);
            var span = frame.Pixels.Span;
            for (var y = 0; y < rows; y++)
            {
                var source = span.Slice(y * frame.Stride, Math.Min(frame.Stride, buffer.RowBytes));
                Marshal.Copy(source.ToArray(), 0, buffer.Address + (y * buffer.RowBytes), source.Length);
            }

            return bitmap;
        }

        /// <summary>
        /// Releases the bitmaps.
        /// </summary>
        public void Dispose()
        {
            foreach (var bitmap in _bitmaps)
            {
                bitmap.Dispose();
            }
        }
    }
}
