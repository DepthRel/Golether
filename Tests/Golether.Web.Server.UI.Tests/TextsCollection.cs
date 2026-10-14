namespace Golether.Web.Server.UI.Tests;

/// <summary>
/// The tests that read or change the language of the shared texts run one after another, so a test that switches the
/// language does not change the words another test is checking at the same moment.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TextsCollection
{
    /// <summary>
    /// The name of the collection.
    /// </summary>
    public const string Name = "Texts";
}
