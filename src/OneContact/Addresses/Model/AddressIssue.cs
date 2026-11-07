namespace RecursiveAI.OneContact;

/// <summary>What is wrong with one field of an address, found by structural validation.</summary>
public enum AddressIssueKind
{
    /// <summary>The country does not use this field, so it should be empty.</summary>
    UnexpectedField = 0,

    /// <summary>The country requires this field and it is empty.</summary>
    MissingRequiredField = 1,

    /// <summary>The value is not one of the known values for the field (a subdivision or country that is not in the data).</summary>
    UnknownValue = 2,

    /// <summary>The value does not have the shape the country uses (a postal code that does not match the country's pattern).</summary>
    InvalidFormat = 3,

    /// <summary>The value is well formed but does not fit the values chosen for the fields above it (a ZIP code that does not belong to the state).</summary>
    MismatchingValue = 4,
}

/// <summary>One structural problem with an address: which field and what kind. Validation checks structure only and never looks anything up.</summary>
/// <param name="Field">The field the problem is about.</param>
/// <param name="Kind">What is wrong with it.</param>
public readonly record struct AddressIssue(AddressField Field, AddressIssueKind Kind);
