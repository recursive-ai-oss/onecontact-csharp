namespace RecursiveAI.OneContact;

/// <summary>Facts found about an <see cref="EmailAddress"/>. Flags report; they never change the value.</summary>
[Flags]
public enum EmailFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The local part names a role (<c>info</c>, <c>sales</c>) rather than a person. The list is documented in <c>docs/email.md</c>.</summary>
    RoleAccount = 1,

    /// <summary>The domain is a free mailbox provider (the list of <c>docs/email.md</c>).</summary>
    FreeProvider = 2,

    /// <summary>The domain, or a parent of it, is on the disposable-address list (the CC0 <c>disposable-email-domains</c> list).</summary>
    Disposable = 4,

    /// <summary>The text used a form RFC 5322 section 4 marks obsolete: white space or comments around dots, a mixed quoted and unquoted local part, a route, an unquoted dot in a display name, or obsolete control characters. The address was still read.</summary>
    ObsoleteSyntax = 8,

    /// <summary>The local part has non-ASCII characters, so delivery needs SMTPUTF8 (RFC 6531).</summary>
    InternationalizedLocalPart = 16,

    /// <summary>The domain has non-ASCII labels or A-labels (<c>xn--</c>). The canonical domain is the A-label form; <see cref="EmailAddress.UnicodeDomain"/> gives the U-label form.</summary>
    InternationalizedDomain = 32,

    /// <summary>The local part was written as a quoted string.</summary>
    QuotedLocalPart = 64,

    /// <summary>The domain is a domain literal (<c>[192.0.2.1]</c>, <c>[IPv6:...]</c>).</summary>
    DomainLiteral = 128,

    /// <summary>The input had a display name in front of an angle-bracketed address.</summary>
    HasDisplayName = 256,

    /// <summary>The input had a comment (RFC 5322 section 3.2.2). Comments are not part of the canonical form.</summary>
    HasComment = 512,

    /// <summary>A length limit of RFC 5321 section 4.5.3.1 or RFC 1035 is exceeded: local part 64 octets, domain 255 octets, a label 63 octets, or the whole address 254 octets (RFC 3696 erratum 1690).</summary>
    ExceedsLengthLimit = 1024,

    /// <summary>The domain has a single label (<c>user@localhost</c>).</summary>
    SingleLabelDomain = 2048,

    /// <summary>The domain looks like a mistyped well-known provider; <see cref="EmailAddress.TypoSuggestion"/> says which. The address is never changed.</summary>
    PossibleTypo = 4096,
}
