namespace RecursiveAI.OneContact;

/// <summary>What every contact value type offers: its kind, canonical form and key.</summary>
public interface IContactValue
{
    /// <summary>Gets the kind of value.</summary>
    ContactKind Kind { get; }

    /// <summary>Gets the canonical form: faithful to the standard or the authority's style.</summary>
    string Canonical { get; }

    /// <summary>Gets the comparison key for the latest <see cref="KeyVersion"/>.</summary>
    string Key { get; }
}
