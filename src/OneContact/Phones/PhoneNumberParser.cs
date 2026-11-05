using System.Buffers;

namespace RecursiveAI.OneContact.Phones;

/// <summary>
/// The parsing seam for <see cref="PhoneNumber"/>. PLACEHOLDER: this body only trims white space and keeps the text as the canonical form,
/// so the shared contracts (offsets, equality, keys, JSON, <see cref="Contacts"/>) have something to run on. The unit that
/// implements Phone parsing replaces this file's body and keeps the signature.
/// </summary>
internal static class PhoneNumberParser
{
    internal static OperationStatus TryParse(ReadOnlySpan<char> input, Region defaultRegion, out PhoneNumber value, out ParseDetail detail)
    {
        ReadOnlySpan<char> trimmed = input.Trim();
        if (trimmed.IsEmpty)
        {
            value = default;
            detail = ParseDetail.Failure(ReasonCode.Empty, new SourceSpan(0, input.Length));
            return OperationStatus.InvalidData;
        }

        int start = input.Length - input.TrimStart().Length;

        // A number that does not start with '+' is national, and a national number means nothing without its region.
        if (trimmed[0] != '+' && defaultRegion.IsUnknown)
        {
            value = default;
            detail = ParseDetail.Failure(ReasonCode.PhoneRegionRequired, new SourceSpan(start, trimmed.Length));
            return OperationStatus.InvalidData;
        }

        value = new PhoneNumber(trimmed.ToString(), new SourceSpan(start, trimmed.Length), PhoneFacts.None);
        detail = ParseDetail.Empty;
        return OperationStatus.Done;
    }
}
