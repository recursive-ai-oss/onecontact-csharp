using System.Buffers;

namespace RecursiveAI.OneContact.Companies;

/// <summary>
/// The parsing seam for <see cref="CompanyName"/>. PLACEHOLDER: this body only trims white space and keeps the text as the canonical form,
/// so the shared contracts (offsets, equality, keys, JSON, <see cref="Contacts"/>) have something to run on. The unit that
/// implements Company parsing replaces this file's body and keeps the signature.
/// </summary>
internal static class CompanyNameParser
{
    internal static OperationStatus TryParse(ReadOnlySpan<char> input, out CompanyName value, out ParseDetail detail)
    {
        ReadOnlySpan<char> trimmed = input.Trim();
        if (trimmed.IsEmpty)
        {
            value = default;
            detail = ParseDetail.Failure(ReasonCode.Empty, new SourceSpan(0, input.Length));
            return OperationStatus.InvalidData;
        }

        int start = input.Length - input.TrimStart().Length;

        value = new CompanyName(trimmed.ToString(), new SourceSpan(start, trimmed.Length), CompanyFacts.None);
        detail = ParseDetail.Empty;
        return OperationStatus.Done;
    }
}
