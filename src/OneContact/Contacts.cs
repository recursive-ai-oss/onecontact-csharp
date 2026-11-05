using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace RecursiveAI.OneContact;

/// <summary>Verbs that work across the value types.</summary>
public static class Contacts
{
    /// <summary>Finds out what kind of value the text is and parses it as that kind.</summary>
    /// <param name="text">The text. Surrounding white space is ignored.</param>
    /// <param name="defaultRegion">The region that national phone numbers belong to; <see cref="Region.Unknown"/> accepts international numbers only.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException">The kind could not be told (<see cref="ReasonCode.UnrecognizedKind"/>) or the text is not valid for its kind.</exception>
    public static ContactValue Normalize(string text, Region defaultRegion = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        OperationStatus status = TryNormalize(text, defaultRegion, out ContactValue value, out ParseDetail detail);
        if (status != OperationStatus.Done)
        {
            ContactKind kind = ContactKindDetector.Detect(text);
            throw ContactParseException.FromDetail(kind, detail, text.Length);
        }

        return value;
    }

    /// <summary>Tries to find out what kind of value the text is and parse it as that kind.</summary>
    /// <param name="text">The text.</param>
    /// <param name="defaultRegion">The region that national phone numbers belong to.</param>
    /// <param name="value">The value, or the default value when this returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the text was normalized.</returns>
    public static bool TryNormalize([NotNullWhen(true)] string? text, Region defaultRegion, out ContactValue value)
    {
        if (text is null)
        {
            value = default;
            return false;
        }

        return TryNormalize(text.AsSpan(), defaultRegion, out value, out _) == OperationStatus.Done;
    }

    /// <summary>The low-level form of <see cref="Normalize(string, Region)"/>: a status and a <see cref="ParseDetail"/> instead of an exception.</summary>
    /// <param name="text">The text.</param>
    /// <param name="defaultRegion">The region that national phone numbers belong to.</param>
    /// <param name="value">The value, or the default value when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">Issues and fields, as for the kind's own parse; for an unrecognized kind, <see cref="ReasonCode.UnrecognizedKind"/>.</param>
    /// <returns><see cref="OperationStatus.Done"/> or <see cref="OperationStatus.InvalidData"/>.</returns>
    public static OperationStatus TryNormalize(ReadOnlySpan<char> text, Region defaultRegion, out ContactValue value, out ParseDetail detail)
    {
        switch (ContactKindDetector.Detect(text))
        {
            case ContactKind.Phone:
                OperationStatus phone = PhoneNumber.TryParse(text, defaultRegion, out PhoneNumber p, out detail);
                value = phone == OperationStatus.Done ? ContactValue.From(p) : default;
                return phone;
            case ContactKind.Email:
                OperationStatus email = EmailAddress.TryParse(text, out EmailAddress e, out detail);
                value = email == OperationStatus.Done ? ContactValue.From(e) : default;
                return email;
            case ContactKind.Web:
                OperationStatus web = WebAddress.TryParse(text, out WebAddress w, out detail);
                value = web == OperationStatus.Done ? ContactValue.From(w) : default;
                return web;
            default:
                value = default;
                detail = ParseDetail.Failure(
                    text.Trim().IsEmpty ? ReasonCode.Empty : ReasonCode.UnrecognizedKind,
                    new SourceSpan(0, text.Length));
                return OperationStatus.InvalidData;
        }
    }

    /// <summary>Tells whether two values of the same type have the same comparison key (latest <see cref="KeyVersion"/>). Canonical forms are not consulted.</summary>
    /// <typeparam name="T">A contact value type.</typeparam>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns><see langword="true"/> when both keys are equal and not empty.</returns>
    public static bool SameKey<T>(T a, T b)
        where T : struct, IContactValue =>
        KeysMatch(a.Key, b.Key);

    /// <summary>Tells whether two values (of any kinds) have the same kind and the same comparison key.</summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns><see langword="true"/> when the kinds match and the keys are equal and not empty.</returns>
    public static bool SameKey(ContactValue a, ContactValue b) =>
        a.Kind == b.Kind && KeysMatch(a.Key, b.Key);

    /// <summary>Normalizes both texts and tells whether they have the same kind and the same comparison key.</summary>
    /// <param name="a">The first text.</param>
    /// <param name="b">The second text.</param>
    /// <param name="defaultRegion">The region that national phone numbers belong to.</param>
    /// <returns><see langword="false"/> when either text cannot be normalized or the keys differ.</returns>
    public static bool SameKey(string? a, string? b, Region defaultRegion = default) =>
        TryNormalize(a, defaultRegion, out ContactValue va) &&
        TryNormalize(b, defaultRegion, out ContactValue vb) &&
        SameKey(va, vb);

    // An empty key means "no value", and two of those are not the same thing.
    private static bool KeysMatch(string a, string b) =>
        a.Length != 0 && string.Equals(a, b, StringComparison.Ordinal);
}
