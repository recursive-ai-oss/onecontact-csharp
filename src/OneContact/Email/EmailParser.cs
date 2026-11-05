using System.Buffers;

namespace RecursiveAI.OneContact.Email;

/// <summary>One result of <see cref="EmailParser.ParseMany"/>.</summary>
/// <param name="Index">The position of the input in the sequence.</param>
/// <param name="Status"><see cref="OperationStatus.Done"/> or <see cref="OperationStatus.InvalidData"/>.</param>
/// <param name="Value">The parsed address; the default value when <paramref name="Status"/> is not Done.</param>
/// <param name="Detail">Issues and the offsets of the display name, local part and domain.</param>
public readonly record struct EmailParseResult(int Index, OperationStatus Status, EmailAddress Value, ParseDetail Detail);

/// <summary>The low-level email engine: span in, status and <see cref="ParseDetail"/> out, and a batch form. Nothing here depends on culture.</summary>
public static class EmailParser
{
    /// <summary>Parses one mailbox (<c>addr-spec</c> or <c>Name &lt;addr-spec&gt;</c>) without throwing.</summary>
    /// <param name="text">The text; surrounding white space is ignored. Offsets in <paramref name="detail"/> index this span.</param>
    /// <param name="value">The address, or the default value on failure.</param>
    /// <param name="detail">Issues (an error issue with a <see cref="ReasonCode"/> on failure; warnings and information otherwise) and the fields <c>display-name</c>, <c>local-part</c> and <c>domain</c> with their offsets, all decided by rules.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/>.</returns>
    public static OperationStatus TryParse(ReadOnlySpan<char> text, out EmailAddress value, out ParseDetail detail) =>
        EmailAddressParser.TryParse(text, out value, out detail);

    /// <summary>Parses a sequence of mailboxes, one result per input, in order. A <see langword="null"/> input is reported as empty.</summary>
    /// <param name="inputs">The texts.</param>
    /// <returns>The results, produced lazily.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is <see langword="null"/>.</exception>
    public static IEnumerable<EmailParseResult> ParseMany(IEnumerable<string?> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return ParseManyCore(inputs);
    }

    private static IEnumerable<EmailParseResult> ParseManyCore(IEnumerable<string?> inputs)
    {
        int index = 0;
        foreach (string? input in inputs)
        {
            OperationStatus status = EmailAddressParser.TryParse(input.AsSpan(), out EmailAddress value, out ParseDetail detail);
            yield return new EmailParseResult(index++, status, value, detail);
        }
    }
}
