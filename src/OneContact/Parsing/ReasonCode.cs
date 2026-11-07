namespace RecursiveAI.OneContact;

/// <summary>
/// Why a parse failed or what an issue is about. A reason code is a kind plus a number that is unique within the kind and stable
/// once released: numbers are never reused or renumbered, and new codes are only ever added, so code that switches on known
/// codes keeps working. Codes that apply to every kind use <see cref="ContactKind.Unknown"/>. Each kind's codes are static members
/// named for the kind (<c>ReasonCode.PhoneRegionRequired</c>) declared in that kind's own file.
/// </summary>
public readonly partial struct ReasonCode : IEquatable<ReasonCode>
{
    private readonly string? _name;
    private readonly string? _description;

    internal ReasonCode(ContactKind kind, ushort number, string name, string description)
    {
        Kind = kind;
        Number = number;
        _name = name;
        _description = description;
    }

    /// <summary>Gets the kind of value the code belongs to.</summary>
    public ContactKind Kind { get; }

    /// <summary>Gets the number, unique within <see cref="Kind"/>. Zero is the default value, meaning no reason.</summary>
    public ushort Number { get; }

    /// <summary>
    /// Gets the code as an enumeration member for <c>switch</c> and <c>case</c>: <c>case ReasonValue.PhoneTooShort:</c>. A code that this version
    /// does not list reads as an undefined value, so keep a default branch.
    /// </summary>
    public ReasonValue Value => (ReasonValue)(((int)Kind << 16) | Number);

    /// <summary>Gets the short lower-case name, such as <c>region-required</c>. Empty text for the default value.</summary>
    public string Name => _name ?? "none";

    /// <summary>Gets a short English sentence fragment saying what the reason means; for messages, not for matching.</summary>
    public string Description => _description ?? "no reason";

    /// <summary>Gets the identifier used in messages and logs: the kind and name, such as <c>phone.region-required</c>; just the name for codes shared by every kind.</summary>
    public string Id => Kind == ContactKind.Unknown ? Name : string.Concat(KindName(Kind), ".", Name);

    /// <summary>Gets a value indicating whether this is the default value, no reason.</summary>
    public bool IsNone => Number == 0 && Kind == ContactKind.Unknown;

    /// <summary>The input has no content (empty or only white space).</summary>
    public static ReasonCode Empty { get; } = new(ContactKind.Unknown, 1, "empty", "the input is empty");

    /// <summary>A region code is not two ASCII letters.</summary>
    public static ReasonCode InvalidRegion { get; } = new(ContactKind.Unknown, 2, "invalid-region", "a region is two ASCII letters, such as US");

    /// <summary>The text does not look like any kind of contact value.</summary>
    public static ReasonCode UnrecognizedKind { get; } = new(ContactKind.Unknown, 3, "unrecognized-kind", "the text does not look like a phone number, email address or web address");

    /// <summary>The operation is not available for this kind yet.</summary>
    public static ReasonCode NotImplemented { get; } = new(ContactKind.Unknown, 4, "not-implemented", "this is not available yet");

    /// <inheritdoc/>
    public bool Equals(ReasonCode other) => Kind == other.Kind && Number == other.Number;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ReasonCode other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => ((int)Kind << 16) | Number;

    /// <summary>Returns <see cref="Id"/>.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;

    /// <summary>Compares two codes by kind and number.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when they are the same code.</returns>
    public static bool operator ==(ReasonCode left, ReasonCode right) => left.Equals(right);

    /// <summary>Compares two codes by kind and number.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when they are different codes.</returns>
    public static bool operator !=(ReasonCode left, ReasonCode right) => !left.Equals(right);

    private static string KindName(ContactKind kind) => kind switch
    {
        ContactKind.Phone => "phone",
        ContactKind.Email => "email",
        ContactKind.Web => "web",
        ContactKind.Address => "address",
        ContactKind.Person => "person",
        ContactKind.Company => "company",
        _ => "unknown",
    };
}
