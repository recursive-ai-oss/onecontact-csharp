namespace RecursiveAI.OneContact;

public readonly partial struct ReasonCode
{
    /// <summary>The phone number is national (it does not start with <c>+</c>) and no region was given to read it in.</summary>
    public static ReasonCode PhoneRegionRequired { get; } = new(ContactKind.Phone, 1, "region-required", "a national number needs a region to be read in");
}
