using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Data.Enums;
using Golether.Media.Player;

namespace Golether.UI.ViewModels;

/// <summary>
/// One entry of the track menu.
/// </summary>
public sealed partial class TrackOptionViewModel : ObservableObject
{
    /// <summary>
    /// The owner.
    /// </summary>
    private readonly TracksViewModel _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrackOptionViewModel"/> class.
    /// </summary>
    /// <param name="owner">The owner.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="track">The track, or <see langword="null"/> for "off".</param>
    /// <param name="label">The label.</param>
    /// <param name="isSelected">Whether the entry is the current choice.</param>
    public TrackOptionViewModel(TracksViewModel owner, MediaTrackKind kind, MediaTrack? track, string label, bool isSelected)
    {
        _owner = owner;
        Kind = kind;
        Track = track;
        Label = label;
        IsSelected = isSelected;
    }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public MediaTrackKind Kind { get; }

    /// <summary>
    /// Gets the track, or <see langword="null"/> for "off".
    /// </summary>
    public MediaTrack? Track { get; }

    /// <summary>
    /// Gets the track identifier, or <see langword="null"/> for "off".
    /// </summary>
    public long? Id => Track?.Id;

    /// <summary>
    /// Gets the label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets a value indicating whether the entry is the current choice.
    /// </summary>
    public bool IsSelected { get; }

    /// <summary>
    /// Selects the entry.
    /// </summary>
    [RelayCommand]
    private void Select() => _owner.Select(this);
}
