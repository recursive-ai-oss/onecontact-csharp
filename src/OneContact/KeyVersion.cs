namespace RecursiveAI.OneContact;

/// <summary>
/// The version of the key rules. Keys are aggressive normalizations meant for matching; any change to a rule ships as a new
/// version, and a published version's output never changes, so stored keys can be recomputed for a named version.
/// </summary>
public enum KeyVersion
{
    /// <summary>The first key rules. Each rule is documented in <c>docs/</c>.</summary>
    V1 = 1,
}
