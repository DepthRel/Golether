using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Golether.Web.Server.UI.ViewModels;

/// <summary>
/// The widths of the columns of the journal. The header changes them when the user drags a divider, and every row
/// follows, so the table stays aligned. The widths are shares of the width of the table, so every column stays
/// visible however narrow the window is; the details take the rest (four shares).
/// </summary>
public sealed partial class JournalColumns : ObservableObject
{
    /// <summary>
    /// Gets or sets the width of the time.
    /// </summary>
    [ObservableProperty]
    public partial GridLength Time { get; set; } = new(1.5, GridUnitType.Star);

    /// <summary>
    /// Gets or sets the width of the address.
    /// </summary>
    [ObservableProperty]
    public partial GridLength Address { get; set; } = new(1.6, GridUnitType.Star);

    /// <summary>
    /// Gets or sets the width of the event.
    /// </summary>
    [ObservableProperty]
    public partial GridLength Event { get; set; } = new(2.4, GridUnitType.Star);
}
