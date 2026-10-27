using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Identity;
using Golether.Localization;
using Golether.Session;
using Golether.UI.Services;

namespace Golether.UI.ViewModels;

/// <summary>
/// A folder the host shows: the common screen everybody sees, the strip and the grid of small pictures, and the
/// picture a person may look at alone. A click on an item puts it on everybody's screen, like a seek of the film; a
/// video put there becomes the film. Looking alone changes only this screen, and the others see where this person is.
/// </summary>
public sealed partial class GalleryViewModel : ObservableObject
{
    /// <summary>
    /// The longest side a picture is decoded at for the screen.
    /// </summary>
    public const int SCREEN_SIDE = 2560;

    /// <summary>
    /// The number of decoded pictures kept, so leafing back and forth does not fetch them again.
    /// </summary>
    private const int KEPT_PICTURES = 6;

    /// <summary>
    /// The longest side a small picture is decoded at: a little more than it takes in the grid, so a strip scrolled
    /// through two thousand items keeps a few hundred kilobytes for each at most.
    /// </summary>
    private const int THUMBNAIL_SIDE = 240;

    /// <summary>
    /// The waits before a small picture that did not arrive is asked for again: the host may have been busy.
    /// </summary>
    private static readonly TimeSpan[] _thumbnailRetries = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)];

    /// <summary>
    /// The session service.
    /// </summary>
    private readonly ISessionService _session;

    /// <summary>
    /// Decodes pictures.
    /// </summary>
    private readonly IPictureDecoder _decoder;

    /// <summary>
    /// The time source of the waits.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// The decoded pictures of the current showing, the most recent last.
    /// </summary>
    private readonly List<(int Index, Bitmap Image)> _kept = [];

    /// <summary>
    /// The pictures being fetched, so a picture asked for twice is fetched once.
    /// </summary>
    private readonly Dictionary<int, Task<Bitmap>> _fetching = [];

    /// <summary>
    /// The item each participant looks at, for the marks under the small pictures.
    /// </summary>
    private readonly Dictionary<PeerId, (GalleryItemViewModel Item, ParticipantItemViewModel Participant)> _marks = [];

    /// <summary>
    /// The identifier of the current showing, or <see langword="null"/>.
    /// </summary>
    private string? _catalogId;

    /// <summary>
    /// The number of items whose description has arrived, from the first one on.
    /// </summary>
    private int _described;

    /// <summary>
    /// Cancelled when the showing ends: its fetches are of no use any more.
    /// </summary>
    private CancellationTokenSource _showing = new();

    /// <summary>
    /// The number of the latest decision about the common screen that was taken in.
    /// </summary>
    private long _stageVersion = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="GalleryViewModel"/> class.
    /// </summary>
    /// <param name="session">The session service.</param>
    /// <param name="decoder">Decodes pictures.</param>
    /// <param name="time">The time source of the waits; the system clock when <see langword="null"/>.</param>
    public GalleryViewModel(ISessionService session, IPictureDecoder decoder, TimeProvider? time = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Raised when another item takes this screen: the strokes drawn over the previous one are cleared.
    /// </summary>
    public event EventHandler? PictureChanged;

    /// <summary>
    /// Gets a value indicating whether a folder is shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDraw))]
    public partial bool IsActive { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this device is the host, which may end the showing.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHost { get; private set; }

    /// <summary>
    /// Gets the name of the shown folder.
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the items of the strip and of the grid.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<GalleryItemViewModel> Items { get; private set; } = [];

    /// <summary>
    /// Gets the index of the item on everybody's screen.
    /// </summary>
    [ObservableProperty]
    public partial int StageIndex { get; private set; }

    /// <summary>
    /// Gets the picture this person looks at alone, or <see langword="null"/> while following the common screen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlone), nameof(ShownIndex), nameof(CanDraw))]
    public partial int? PersonalIndex { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this person looks at a picture alone.
    /// </summary>
    public bool IsAlone => PersonalIndex is not null;

    /// <summary>
    /// Gets a value indicating whether the pen may be used: strokes over a picture nobody else sees reach no one, and
    /// the grid covers the screen.
    /// </summary>
    public bool CanDraw => IsActive && !IsAlone && !IsGridOpen;

    /// <summary>
    /// Gets the index of the item on this screen.
    /// </summary>
    public int ShownIndex => PersonalIndex ?? StageIndex;

    /// <summary>
    /// Gets a value indicating whether a picture takes this screen; otherwise the film does (a video of the folder
    /// put on the screen, or the film that waits under a closed folder).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowsPicture { get; private set; }

    /// <summary>
    /// Gets the picture on this screen, or <see langword="null"/> while it is fetched or cannot be shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PictureAspect))]
    public partial Bitmap? ShownImage { get; private set; }

    /// <summary>
    /// Gets the width of the shown picture divided by its height, or zero: the strokes are laid over the picture.
    /// </summary>
    public double PictureAspect => ShownImage is { Size: { Width: > 0, Height: > 0 } size } ? size.Width / size.Height : 0;

    /// <summary>
    /// Gets a value indicating whether the picture on this screen is being fetched.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the picture on this screen could not be fetched or decoded.
    /// </summary>
    [ObservableProperty]
    public partial bool HasError { get; private set; }

    /// <summary>
    /// Gets the position of the item on this screen: "37 / 412".
    /// </summary>
    [ObservableProperty]
    public partial string CounterText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the name of the item on this screen, with its nested folder.
    /// </summary>
    [ObservableProperty]
    public partial string ShownName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the line shown while looking alone: what everybody else sees now.
    /// </summary>
    [ObservableProperty]
    public partial string AloneText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the grid of all items covers this screen. Only this screen changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDraw))]
    public partial bool IsGridOpen { get; private set; }

    /// <summary>
    /// Gets or sets the number of items in a row of the grid; the view sets it from its width.
    /// </summary>
    [ObservableProperty]
    public partial int GridColumns { get; set; } = 6;

    /// <summary>
    /// Gets the rows of the grid: only the rows in sight are made, so a folder of two thousand items stays light.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<IReadOnlyList<GalleryItemViewModel>> GridRows { get; private set; } = [];

    /// <summary>
    /// Takes in the state of the session: the showing, the common screen and where everybody looks.
    /// </summary>
    /// <param name="snapshot">The session, or <see langword="null"/> when there is none.</param>
    /// <param name="participants">The participants of the view, to mark under the small pictures.</param>
    public void Update(SessionSnapshot? snapshot, IReadOnlyList<ParticipantItemViewModel> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (snapshot?.Gallery is not { } gallery)
        {
            if (IsActive)
            {
                Stop(participants);
            }

            return;
        }

        IsHost = snapshot.IsHost;
        if (!string.Equals(gallery.CatalogId, _catalogId, StringComparison.Ordinal))
        {
            Start(gallery);
        }

        Describe(gallery);
        if (gallery.StageVersion != _stageVersion && gallery.Stage < Items.Count)
        {
            _stageVersion = gallery.StageVersion;
            Items[StageIndex].IsStage = false;
            StageIndex = gallery.Stage;
            Items[StageIndex].IsStage = true;
            if (PersonalIndex is null)
            {
                Present(StageIndex);
            }
        }

        AloneText = Texts.Format("Gallery.Alone.Text", Items[StageIndex].FullName);
        UpdateMarks(snapshot, participants);
    }

    /// <summary>
    /// Opens or closes the grid of all items.
    /// </summary>
    [RelayCommand]
    private void ToggleGrid()
    {
        if (IsActive)
        {
            IsGridOpen = !IsGridOpen;
        }
    }

    /// <summary>
    /// Closes the grid of all items.
    /// </summary>
    [RelayCommand]
    private void CloseGrid() => IsGridOpen = false;

    /// <summary>
    /// Puts an item on everybody's screen; looking alone and the grid end with it.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>A task that completes when the choice was sent.</returns>
    [RelayCommand]
    private async Task ShowToAllAsync(GalleryItemViewModel? item)
    {
        if (item is null || _catalogId is not { } catalog)
        {
            return;
        }

        IsGridOpen = false;
        if (PersonalIndex is not null)
        {
            ReturnToCommon();
        }

        try
        {
            await _session.ShowItemAsync(catalog, item.Index, _showing.Token);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException)
        {
            // The connection broke or the showing ended; the session tells about it on its own.
        }
    }

    /// <summary>
    /// Looks at a picture alone: only this screen changes. A video is watched together, so it is not opened alone.
    /// </summary>
    /// <param name="item">The picture.</param>
    [RelayCommand]
    private void OpenAlone(GalleryItemViewModel? item)
    {
        if (item is null || !IsActive || item.IsVideo)
        {
            return;
        }

        IsGridOpen = false;
        PersonalIndex = item.Index;
        _session.SetGalleryView(item.Index);
        Present(item.Index);
    }

    /// <summary>
    /// Goes back to the item everybody sees.
    /// </summary>
    [RelayCommand]
    private void ReturnToCommon()
    {
        if (PersonalIndex is null)
        {
            return;
        }

        PersonalIndex = null;
        _session.SetGalleryView(null);
        Present(StageIndex);
    }

    /// <summary>
    /// Puts the picture looked at alone on everybody's screen.
    /// </summary>
    /// <returns>A task that completes when the choice was sent.</returns>
    [RelayCommand]
    private Task ShowMineToAllAsync()
        => PersonalIndex is { } index && index < Items.Count ? ShowToAllAsync(Items[index]) : Task.CompletedTask;

    /// <summary>
    /// Goes to the next item: for everybody, or to the next picture only here while looking alone.
    /// </summary>
    /// <returns>A task that completes when the choice was sent.</returns>
    [RelayCommand]
    private Task NextAsync() => StepAsync(1);

    /// <summary>
    /// Goes to the previous item: for everybody, or to the previous picture only here while looking alone.
    /// </summary>
    /// <returns>A task that completes when the choice was sent.</returns>
    [RelayCommand]
    private Task PreviousAsync() => StepAsync(-1);

    /// <summary>
    /// Ends the showing of the folder, so everybody goes back to the film (host only).
    /// </summary>
    /// <returns>A task that completes when everybody was told.</returns>
    [RelayCommand]
    private async Task CloseAsync()
    {
        try
        {
            await _session.CloseGalleryAsync();
        }
        catch (InvalidOperationException)
        {
            // Not the host any more.
        }
    }

    /// <summary>
    /// Moves by items: for everybody, or only here while looking alone, where videos are passed over.
    /// </summary>
    /// <param name="delta">One forward, minus one back.</param>
    /// <returns>A task that completes when the choice was sent.</returns>
    private Task StepAsync(int delta)
    {
        if (!IsActive)
        {
            return Task.CompletedTask;
        }

        var target = ShownIndex + delta;
        while (PersonalIndex is not null && target >= 0 && target < Items.Count && Items[target].IsVideo)
        {
            target += delta;
        }

        if (target < 0 || target >= Items.Count)
        {
            return Task.CompletedTask;
        }

        if (PersonalIndex is not null)
        {
            OpenAlone(Items[target]);
            return Task.CompletedTask;
        }

        return ShowToAllAsync(Items[target]);
    }

    /// <summary>
    /// Rebuilds the rows of the grid when the items or the width change.
    /// </summary>
    /// <param name="value">The new number of items in a row.</param>
    partial void OnGridColumnsChanged(int value) => GridRows = Items.Chunk(Math.Max(1, value)).ToArray();

    /// <summary>
    /// Rebuilds the rows of the grid when the items change.
    /// </summary>
    /// <param name="value">The new items.</param>
    partial void OnItemsChanged(IReadOnlyList<GalleryItemViewModel> value) => GridRows = value.Chunk(Math.Max(1, GridColumns)).ToArray();

    /// <summary>
    /// Takes up a new showing.
    /// </summary>
    /// <param name="gallery">The showing.</param>
    private void Start(GallerySnapshot gallery)
    {
        _showing.Cancel();
        _showing.Dispose();
        _showing = new CancellationTokenSource();
        _kept.Clear();
        _fetching.Clear();
        _catalogId = gallery.CatalogId;
        _stageVersion = -1;
        _described = 0;
        Title = gallery.Title;
        Items = Enumerable.Range(0, gallery.Total)
            .Select(i => new GalleryItemViewModel(i, RequestThumbnail, ShowToAllAsync, OpenAlone))
            .ToArray();
        StageIndex = 0;
        PersonalIndex = null;
        ShownImage = null;
        IsGridOpen = false;
        IsActive = true;
    }

    /// <summary>
    /// Takes in the descriptions of the items that arrived since the last time, in order: an item opens a nested
    /// folder when its folder differs from that of the item before it.
    /// </summary>
    /// <param name="gallery">The showing.</param>
    private void Describe(GallerySnapshot gallery)
    {
        var start = _described;
        while (_described < Items.Count && _described < gallery.Items.Count && gallery.Items[_described] is { } item)
        {
            Items[_described].SetInfo(item, _described == 0 ? null : gallery.Items[_described - 1]?.Folder);
            _described++;
        }

        if (_described > start && StageIndex < _described)
        {
            UpdateShown();
        }
    }

    /// <summary>
    /// Ends the showing on this screen.
    /// </summary>
    /// <param name="participants">The participants, whose marks go away.</param>
    private void Stop(IReadOnlyList<ParticipantItemViewModel> participants)
    {
        _showing.Cancel();
        _kept.Clear();
        _fetching.Clear();
        _marks.Clear();
        _catalogId = null;
        foreach (var participant in participants)
        {
            participant.IsViewingAlone = false;
        }

        Items = [];
        PersonalIndex = null;
        ShownImage = null;
        IsLoading = false;
        HasError = false;
        IsGridOpen = false;
        IsActive = false;
        ShowsPicture = false;
    }

    /// <summary>
    /// Takes up what is on this screen now: a picture or the film.
    /// </summary>
    private void UpdateShown()
    {
        var item = Items[ShownIndex];
        ShowsPicture = IsActive && !item.IsVideo;
        ShownName = item.FullName;
    }

    /// <summary>
    /// Shows an item on this screen: a picture from the kept ones at once, otherwise once it is fetched; a video is
    /// played by the film player.
    /// </summary>
    /// <param name="index">The index of the item.</param>
    private void Present(int index)
    {
        foreach (var item in Items)
        {
            item.IsShown = item.Index == index;
        }

        CounterText = string.Format(CultureInfo.CurrentCulture, "{0} / {1}", index + 1, Items.Count);
        UpdateShown();
        PictureChanged?.Invoke(this, EventArgs.Empty);
        if (Items[index].IsVideo)
        {
            ShownImage = null;
            IsLoading = false;
            HasError = false;
            return;
        }

        _ = PresentAsync(index);
    }

    /// <summary>
    /// Fetches and shows a picture, then fetches the neighbouring pictures ahead of time.
    /// </summary>
    /// <param name="index">The index of the picture.</param>
    /// <returns>A task that completes when the picture is on the screen or failed.</returns>
    private async Task PresentAsync(int index)
    {
        var showing = _showing.Token;
        HasError = false;
        IsLoading = true;
        try
        {
            var image = await FetchAsync(index);
            if (!showing.IsCancellationRequested && ShownIndex == index)
            {
                ShownImage = image;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!showing.IsCancellationRequested && ShownIndex == index)
            {
                ShownImage = null;
                HasError = ex is not OperationCanceledException;
            }
        }
        finally
        {
            if (!showing.IsCancellationRequested && ShownIndex == index)
            {
                IsLoading = false;
            }
        }

        foreach (var neighbour in new[] { index + 1, index - 1 })
        {
            if (neighbour >= 0 && neighbour < Items.Count && !Items[neighbour].IsVideo && !showing.IsCancellationRequested)
            {
                _ = FetchAsync(neighbour).ContinueWith(t => t.Exception, TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// Returns a picture, decoded for the screen: a kept one, one already being fetched, or a new fetch.
    /// </summary>
    /// <param name="index">The index of the picture.</param>
    /// <returns>The picture.</returns>
    private Task<Bitmap> FetchAsync(int index)
    {
        var kept = _kept.FindIndex(k => k.Index == index);
        if (kept >= 0)
        {
            var found = _kept[kept];
            _kept.RemoveAt(kept);
            _kept.Add(found);
            return Task.FromResult(found.Image);
        }

        if (!_fetching.TryGetValue(index, out var fetching))
        {
            fetching = FetchNewAsync(_catalogId!, index, _showing.Token);

            // A fetch that failed at once has nothing to share, and kept here it would fail every later try.
            if (!fetching.IsCompleted)
            {
                _fetching[index] = fetching;
            }
        }

        return fetching;
    }

    /// <summary>
    /// Fetches a picture and decodes it.
    /// </summary>
    /// <param name="catalog">The showing.</param>
    /// <param name="index">The index of the picture.</param>
    /// <param name="cancellationToken">Cancelled when the showing ends.</param>
    /// <returns>The picture.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a picture that can be shown.</exception>
    private async Task<Bitmap> FetchNewAsync(string catalog, int index, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _session.LoadPictureAsync(catalog, index, cancellationToken);
            var image = await Task.Run(() => _decoder.Decode(bytes, SCREEN_SIDE), cancellationToken) ??
                throw new InvalidDataException("The picture cannot be decoded.");
            if (!cancellationToken.IsCancellationRequested)
            {
                _kept.Add((index, image));
                if (_kept.Count > KEPT_PICTURES)
                {
                    _kept.RemoveAt(0);
                }
            }

            return image;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _fetching.Remove(index);
            }
        }
    }

    /// <summary>
    /// Fetches the small picture of an item a view has just shown.
    /// </summary>
    /// <param name="item">The item.</param>
    private void RequestThumbnail(GalleryItemViewModel item) => _ = LoadThumbnailAsync(item, _catalogId, _showing.Token);

    /// <summary>
    /// Fetches and decodes a small picture; one that did not arrive is asked for again a few times, later and later.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="catalog">The showing.</param>
    /// <param name="cancellationToken">Cancelled when the showing ends.</param>
    /// <returns>A task that completes when the small picture is in place or given up.</returns>
    private async Task LoadThumbnailAsync(GalleryItemViewModel item, string? catalog, CancellationToken cancellationToken)
    {
        if (catalog is null)
        {
            return;
        }

        foreach (var wait in _thumbnailRetries.Prepend(TimeSpan.Zero))
        {
            try
            {
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _time, cancellationToken);
                }

                if (await _session.LoadThumbnailAsync(catalog, item.Index, cancellationToken) is { } bytes)
                {
                    // A small picture that arrived is not asked for again, even when it cannot be decoded.
                    var image = await Task.Run(() => _decoder.Decode(bytes, THUMBNAIL_SIDE), cancellationToken);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        item.SetThumbnail(image);
                    }

                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
                // Asked for again after the next wait; the name stays in place meanwhile.
            }
        }
    }

    /// <summary>
    /// Puts the marks of the participants under the items they look at.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    /// <param name="participants">The participants of the view.</param>
    private void UpdateMarks(SessionSnapshot snapshot, IReadOnlyList<ParticipantItemViewModel> participants)
    {
        foreach (var peer in _marks.Keys.Where(p => participants.All(v => v.PeerId != p)).ToArray())
        {
            _marks[peer].Item.Viewers.Remove(_marks[peer].Participant);
            _marks.Remove(peer);
        }

        foreach (var participant in participants)
        {
            var status = snapshot.Participants.FirstOrDefault(v => v.Info.PeerId == participant.PeerId)?.Status;
            var alone = status?.GalleryView is { } view && view < Items.Count ? view : (int?)null;
            participant.IsViewingAlone = alone is not null;
            var item = Items[alone ?? StageIndex];
            if (_marks.TryGetValue(participant.PeerId, out var mark))
            {
                if (ReferenceEquals(mark.Item, item))
                {
                    continue;
                }

                mark.Item.Viewers.Remove(participant);
            }

            item.Viewers.Add(participant);
            _marks[participant.PeerId] = (item, participant);
        }
    }
}
