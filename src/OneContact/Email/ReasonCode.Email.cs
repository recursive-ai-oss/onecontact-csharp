namespace RecursiveAI.OneContact;

public readonly partial struct ReasonCode
{
    /// <summary>There is no <c>@</c> between a local part and a domain.</summary>
    public static ReasonCode EmailMissingAt { get; } = new(ContactKind.Email, 1, "missing-at", "an address needs an @ between the local part and the domain");

    /// <summary>The local part is empty.</summary>
    public static ReasonCode EmailEmptyLocalPart { get; } = new(ContactKind.Email, 2, "empty-local-part", "the part before the @ is empty");

    /// <summary>The domain is empty.</summary>
    public static ReasonCode EmailEmptyDomain { get; } = new(ContactKind.Email, 3, "empty-domain", "the part after the @ is empty");

    /// <summary>The local part is not an RFC 5322 dot-atom, quoted string or obsolete local part.</summary>
    public static ReasonCode EmailInvalidLocalPart { get; } = new(ContactKind.Email, 4, "invalid-local-part", "the local part has a character or dot that is not allowed unquoted");

    /// <summary>The domain is not a valid host name or domain literal.</summary>
    public static ReasonCode EmailInvalidDomain { get; } = new(ContactKind.Email, 5, "invalid-domain", "the domain is not a valid host name");

    /// <summary>The display name is not an RFC 5322 phrase.</summary>
    public static ReasonCode EmailInvalidDisplayName { get; } = new(ContactKind.Email, 6, "invalid-display-name", "the display name has a character that needs quoting");

    /// <summary>A quoted string is not closed or has a character it cannot hold.</summary>
    public static ReasonCode EmailInvalidQuotedString { get; } = new(ContactKind.Email, 7, "invalid-quoted-string", "a quoted string is not closed");

    /// <summary>A comment is not closed, nests too deeply or has a character it cannot hold.</summary>
    public static ReasonCode EmailInvalidComment { get; } = new(ContactKind.Email, 8, "invalid-comment", "a comment is not closed");

    /// <summary>An angle bracket is not matched.</summary>
    public static ReasonCode EmailUnbalancedAngle { get; } = new(ContactKind.Email, 9, "unbalanced-angle", "an angle bracket is not matched");

    /// <summary>Text follows the address: a second address, a group or other content.</summary>
    public static ReasonCode EmailTrailingText { get; } = new(ContactKind.Email, 10, "trailing-text", "there is text after the address");

    /// <summary>A domain literal is not a valid IPv4 or IPv6 address literal.</summary>
    public static ReasonCode EmailInvalidAddressLiteral { get; } = new(ContactKind.Email, 11, "invalid-address-literal", "the domain literal is not a valid address literal");

    /// <summary>The domain fails IDNA processing (UTS 46 with IDNA2008 strictness).</summary>
    public static ReasonCode EmailDomainIdnaFailed { get; } = new(ContactKind.Email, 12, "domain-idna-failed", "the domain is not a valid internationalized domain name");

    /// <summary>The last domain label is all digits (RFC 3696 section 2).</summary>
    public static ReasonCode EmailNumericTopLevelDomain { get; } = new(ContactKind.Email, 13, "numeric-top-level-domain", "the last label of a domain cannot be all digits");

    /// <summary>A control character or an unpaired surrogate where it cannot appear.</summary>
    public static ReasonCode EmailInvalidCharacter { get; } = new(ContactKind.Email, 14, "invalid-character", "the text has a control character or an unpaired surrogate");

    /// <summary>The local part is longer than 64 octets (RFC 5321 section 4.5.3.1.1). A warning.</summary>
    public static ReasonCode EmailLocalPartTooLong { get; } = new(ContactKind.Email, 20, "local-part-too-long", "the local part is longer than 64 octets");

    /// <summary>The domain is longer than 255 octets (RFC 5321 section 4.5.3.1.2). A warning.</summary>
    public static ReasonCode EmailDomainTooLong { get; } = new(ContactKind.Email, 21, "domain-too-long", "the domain is longer than 255 octets");

    /// <summary>A domain label is longer than 63 octets (RFC 1035 section 2.3.4). A warning.</summary>
    public static ReasonCode EmailLabelTooLong { get; } = new(ContactKind.Email, 22, "label-too-long", "a domain label is longer than 63 octets");

    /// <summary>The address is longer than 254 octets (RFC 3696 erratum 1690). A warning.</summary>
    public static ReasonCode EmailAddressTooLong { get; } = new(ContactKind.Email, 23, "address-too-long", "the address is longer than 254 octets");

    /// <summary>An obsolete RFC 5322 form was read. Info.</summary>
    public static ReasonCode EmailObsoleteSyntax { get; } = new(ContactKind.Email, 24, "obsolete-syntax", "the address uses a form RFC 5322 marks obsolete");

    /// <summary>The domain has one label. Info.</summary>
    public static ReasonCode EmailSingleLabelDomain { get; } = new(ContactKind.Email, 25, "single-label-domain", "the domain has a single label");

    /// <summary>The domain looks like a mistyped provider domain. Info; the address is never changed.</summary>
    public static ReasonCode EmailPossibleTypo { get; } = new(ContactKind.Email, 26, "possible-typo", "the domain looks like a mistyped well-known provider");

    /// <summary>A domain literal is valid in RFC 5322 but is not an address literal SMTP can carry (RFC 5321 section 4.1.3). A warning.</summary>
    public static ReasonCode EmailNotSmtpLiteral { get; } = new(ContactKind.Email, 27, "not-smtp-literal", "the domain literal is not an address literal SMTP can carry");
}
