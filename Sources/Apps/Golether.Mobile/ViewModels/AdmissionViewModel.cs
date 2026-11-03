using System.Globalization;
using Golether.Localization;
using Golether.Security.Admission;

namespace Golether.Mobile.ViewModels;

/// <summary>
/// A participant who wants to come in, as the host sees them: the name and the code to compare aloud.
/// </summary>
public sealed class AdmissionViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdmissionViewModel"/> class.
    /// </summary>
    /// <param name="request">The request.</param>
    public AdmissionViewModel(AdmissionRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Title = Texts.Format("Admission.Title", request.DisplayName);
        CodeParts = [.. request.VerificationCode.Words, request.VerificationCode.Number.ToString("00", CultureInfo.InvariantCulture)];
        Fingerprint = request.PeerId.ToShortString();
        Explanation = Texts.Get(request.IsKnownContact ? "Admission.KnownDevice" : "Admission.NewDevice");
    }

    /// <summary>
    /// Gets the request.
    /// </summary>
    public AdmissionRequest Request { get; }

    /// <summary>
    /// Gets the title: who wants to come in.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets what the host is to do with the code.
    /// </summary>
    public string Explanation { get; }

    /// <summary>
    /// Gets the words and the number of the code.
    /// </summary>
    public IReadOnlyList<string> CodeParts { get; }

    /// <summary>
    /// Gets the short fingerprint of the device of the participant.
    /// </summary>
    public string Fingerprint { get; }
}
