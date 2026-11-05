using System.Diagnostics.CodeAnalysis;

namespace RecursiveAI.OneContact;

/// <summary>
/// A contact value of a kind found by <see cref="Contacts.Normalize(string, Region)"/>: one of the six value types, with the
/// shared canonical form and key. <c>default</c> is an empty value of kind <see cref="ContactKind.Unknown"/>.
/// </summary>
public readonly struct ContactValue : IEquatable<ContactValue>, IContactValue
{
    // Holds a boxed PhoneNumber, EmailAddress, WebAddress, PostalAddress, PersonName or CompanyName.
    private readonly object? _value;

    private ContactValue(ContactKind kind, object value)
    {
        Kind = kind;
        _value = value;
    }

    /// <summary>Gets which kind of value this holds.</summary>
    public ContactKind Kind { get; }

    /// <summary>Gets the canonical form of the held value; empty for the default value.</summary>
    public string Canonical => _value switch
    {
        PhoneNumber v => v.Canonical,
        EmailAddress v => v.Canonical,
        WebAddress v => v.Canonical,
        PostalAddress v => v.Canonical,
        PersonName v => v.Canonical,
        CompanyName v => v.Canonical,
        _ => string.Empty,
    };

    /// <summary>Gets the comparison key of the held value for the latest <see cref="KeyVersion"/>; empty for the default value.</summary>
    public string Key => _value switch
    {
        PhoneNumber v => v.Key,
        EmailAddress v => v.Key,
        WebAddress v => v.Key,
        PostalAddress v => v.Key,
        PersonName v => v.Key,
        CompanyName v => v.Key,
        _ => string.Empty,
    };

    /// <summary>Wraps a <see cref="PhoneNumber"/> that was parsed already, so a result can carry values of any kind.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="ContactValue"/> of kind <see cref="Kind"/> for it.</returns>
    public static ContactValue From(PhoneNumber value) => new(ContactKind.Phone, value);

    /// <summary>Wraps a <see cref="EmailAddress"/> that was parsed already, so a result can carry values of any kind.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="ContactValue"/> of kind <see cref="Kind"/> for it.</returns>
    public static ContactValue From(EmailAddress value) => new(ContactKind.Email, value);

    /// <summary>Wraps a <see cref="WebAddress"/> that was parsed already, so a result can carry values of any kind.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="ContactValue"/> of kind <see cref="Kind"/> for it.</returns>
    public static ContactValue From(WebAddress value) => new(ContactKind.Web, value);

    /// <summary>Wraps a <see cref="PostalAddress"/> that was parsed already, so a result can carry values of any kind.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="ContactValue"/> of kind <see cref="Kind"/> for it.</returns>
    public static ContactValue From(PostalAddress value) => new(ContactKind.Address, value);

    /// <summary>Wraps a <see cref="PersonName"/> that was parsed already, so a result can carry values of any kind.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="ContactValue"/> of kind <see cref="Kind"/> for it.</returns>
    public static ContactValue From(PersonName value) => new(ContactKind.Person, value);

    /// <summary>Wraps a <see cref="CompanyName"/> that was parsed already, so a result can carry values of any kind.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="ContactValue"/> of kind <see cref="Kind"/> for it.</returns>
    public static ContactValue From(CompanyName value) => new(ContactKind.Company, value);

    /// <summary>Gets the held phone number.</summary>
    /// <param name="value">The number, when <see cref="Kind"/> is <see cref="ContactKind.Phone"/>.</param>
    /// <returns><see langword="true"/> when this holds a phone number.</returns>
    public bool TryGetPhone(out PhoneNumber value)
    {
        if (_value is PhoneNumber v)
        {
            value = v;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the held email address.</summary>
    /// <param name="value">The address, when <see cref="Kind"/> is <see cref="ContactKind.Email"/>.</param>
    /// <returns><see langword="true"/> when this holds an email address.</returns>
    public bool TryGetEmail(out EmailAddress value)
    {
        if (_value is EmailAddress v)
        {
            value = v;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the held web address.</summary>
    /// <param name="value">The address, when <see cref="Kind"/> is <see cref="ContactKind.Web"/>.</param>
    /// <returns><see langword="true"/> when this holds a web address.</returns>
    public bool TryGetWeb(out WebAddress value)
    {
        if (_value is WebAddress v)
        {
            value = v;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the held postal address.</summary>
    /// <param name="value">The address, when <see cref="Kind"/> is <see cref="ContactKind.Address"/>.</param>
    /// <returns><see langword="true"/> when this holds a postal address.</returns>
    public bool TryGetAddress(out PostalAddress value)
    {
        if (_value is PostalAddress v)
        {
            value = v;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the held person name.</summary>
    /// <param name="value">The name, when <see cref="Kind"/> is <see cref="ContactKind.Person"/>.</param>
    /// <returns><see langword="true"/> when this holds a person name.</returns>
    public bool TryGetPerson(out PersonName value)
    {
        if (_value is PersonName v)
        {
            value = v;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the held company name.</summary>
    /// <param name="value">The name, when <see cref="Kind"/> is <see cref="ContactKind.Company"/>.</param>
    /// <returns><see langword="true"/> when this holds a company name.</returns>
    public bool TryGetCompany(out CompanyName value)
    {
        if (_value is CompanyName v)
        {
            value = v;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Compares kind and canonical form.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> when both hold the same kind with equal canonical forms.</returns>
    public bool Equals(ContactValue other) =>
        Kind == other.Kind && string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ContactValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, string.GetHashCode(Canonical, StringComparison.Ordinal));

    /// <summary>Returns the canonical form.</summary>
    /// <returns><see cref="Canonical"/>.</returns>
    public override string ToString() => Canonical;

    /// <summary>Compares kind and canonical form.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(ContactValue left, ContactValue right) => left.Equals(right);

    /// <summary>Compares kind and canonical form.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when different.</returns>
    public static bool operator !=(ContactValue left, ContactValue right) => !left.Equals(right);
}
