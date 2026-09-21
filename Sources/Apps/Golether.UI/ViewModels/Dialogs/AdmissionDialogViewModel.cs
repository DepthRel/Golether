using Golether.Localization;
using Golether.Security.Admission;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// The admission dialog: who wants to join and the code to compare.
/// </summary>
public sealed class AdmissionDialogViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdmissionDialogViewModel"/> class.
    /// </summary>
    /// <param name="request">The request.</param>
    public AdmissionDialogViewModel(AdmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Title = Texts.Format("Admission.Title", request.DisplayName);
        Words = request.VerificationCode.Words;
        Number = request.VerificationCode.Number.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        Fingerprint = request.PeerId.ToShortString();
        Explanation = Texts.Get(request.IsKnownContact ? "Admission.KnownDevice" : "Admission.NewDevice");
    }

    /// <summary>
    /// Gets the title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the explanation.
    /// </summary>
    public string Explanation { get; }

    /// <summary>
    /// Gets the code words.
    /// </summary>
    public IReadOnlyList<string> Words { get; }

    /// <summary>
    /// Gets the code number.
    /// </summary>
    public string Number { get; }

    /// <summary>
    /// Gets the device fingerprint.
    /// </summary>
    public string Fingerprint { get; }
}
