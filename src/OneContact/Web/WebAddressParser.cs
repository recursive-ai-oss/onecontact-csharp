using System.Buffers;

namespace RecursiveAI.OneContact.Web;

/// <summary>
/// The parsing seam for <see cref="WebAddress"/>. PLACEHOLDER: this body only trims white space and keeps the text as the canonical form,
/// so the shared contracts (offsets, equality, keys, JSON, <see cref="Contacts"/>) have something to run on. The unit that
/// implements Web parsing replaces this file's body and keeps the signature.
/// </summary>
internal static class WebAddressParser
{
    internal static OperationStatus TryParse(ReadOnlySpan<char> input, out WebAddress value, out ParseDetail detail)
    {
        ReadOnlySpan<char> trimmed = input.Trim();
        if (trimmed.IsEmpty)
        {
            value = default;
            detail = ParseDetail.Failure(ReasonCode.Empty, new SourceSpan(0, input.Length));
            return OperationStatus.InvalidData;
        }

        int start = input.Length - input.TrimStart().Length;

        value = new WebAddress(trimmed.ToString(), new SourceSpan(start, trimmed.Length), WebFacts.None);
        detail = ParseDetail.Empty;
        return OperationStatus.Done;
    }
}
