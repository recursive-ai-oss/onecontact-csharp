namespace RecursiveAI.OneContact;

/// <summary>
/// The known reasons as an enumeration, so a caller can <c>switch</c> on <see cref="ReasonCode.Value"/> with a compiler-checked <c>case</c>.
/// The numeric value is the contact kind times 65536 plus the code number; it is as stable as <see cref="ReasonCode.Number"/>. Members are
/// only ever added. A code that a later version adds reads as an undefined value in older code, so keep a default branch.
/// </summary>
public enum ReasonValue
{
    /// <summary>No reason: the default <see cref="ReasonCode"/>.</summary>
    None = 0,

    /// <summary>See <see cref="ReasonCode.Empty"/>.</summary>
    Empty = 1,

    /// <summary>See <see cref="ReasonCode.InvalidRegion"/>.</summary>
    InvalidRegion = 2,

    /// <summary>See <see cref="ReasonCode.UnrecognizedKind"/>.</summary>
    UnrecognizedKind = 3,

    /// <summary>See <see cref="ReasonCode.NotImplemented"/>.</summary>
    NotImplemented = 4,

    /// <summary>See <see cref="ReasonCode.PhoneRegionRequired"/>.</summary>
    PhoneRegionRequired = 65537,

    /// <summary>See <see cref="ReasonCode.PhoneNotANumber"/>.</summary>
    PhoneNotANumber = 65538,

    /// <summary>See <see cref="ReasonCode.PhoneInvalidCountryCode"/>.</summary>
    PhoneInvalidCountryCode = 65539,

    /// <summary>See <see cref="ReasonCode.PhoneTooShortAfterIdd"/>.</summary>
    PhoneTooShortAfterIdd = 65540,

    /// <summary>See <see cref="ReasonCode.PhoneTooShort"/>.</summary>
    PhoneTooShort = 65541,

    /// <summary>See <see cref="ReasonCode.PhoneTooLong"/>.</summary>
    PhoneTooLong = 65542,

    /// <summary>See <see cref="ReasonCode.PhoneUnknownRegion"/>.</summary>
    PhoneUnknownRegion = 65543,

    /// <summary>See <see cref="ReasonCode.EmailMissingAt"/>.</summary>
    EmailMissingAt = 131073,

    /// <summary>See <see cref="ReasonCode.EmailEmptyLocalPart"/>.</summary>
    EmailEmptyLocalPart = 131074,

    /// <summary>See <see cref="ReasonCode.EmailEmptyDomain"/>.</summary>
    EmailEmptyDomain = 131075,

    /// <summary>See <see cref="ReasonCode.EmailInvalidLocalPart"/>.</summary>
    EmailInvalidLocalPart = 131076,

    /// <summary>See <see cref="ReasonCode.EmailInvalidDomain"/>.</summary>
    EmailInvalidDomain = 131077,

    /// <summary>See <see cref="ReasonCode.EmailInvalidDisplayName"/>.</summary>
    EmailInvalidDisplayName = 131078,

    /// <summary>See <see cref="ReasonCode.EmailInvalidQuotedString"/>.</summary>
    EmailInvalidQuotedString = 131079,

    /// <summary>See <see cref="ReasonCode.EmailInvalidComment"/>.</summary>
    EmailInvalidComment = 131080,

    /// <summary>See <see cref="ReasonCode.EmailUnbalancedAngle"/>.</summary>
    EmailUnbalancedAngle = 131081,

    /// <summary>See <see cref="ReasonCode.EmailTrailingText"/>.</summary>
    EmailTrailingText = 131082,

    /// <summary>See <see cref="ReasonCode.EmailInvalidAddressLiteral"/>.</summary>
    EmailInvalidAddressLiteral = 131083,

    /// <summary>See <see cref="ReasonCode.EmailDomainIdnaFailed"/>.</summary>
    EmailDomainIdnaFailed = 131084,

    /// <summary>See <see cref="ReasonCode.EmailNumericTopLevelDomain"/>.</summary>
    EmailNumericTopLevelDomain = 131085,

    /// <summary>See <see cref="ReasonCode.EmailInvalidCharacter"/>.</summary>
    EmailInvalidCharacter = 131086,

    /// <summary>See <see cref="ReasonCode.EmailLocalPartTooLong"/>.</summary>
    EmailLocalPartTooLong = 131092,

    /// <summary>See <see cref="ReasonCode.EmailDomainTooLong"/>.</summary>
    EmailDomainTooLong = 131093,

    /// <summary>See <see cref="ReasonCode.EmailLabelTooLong"/>.</summary>
    EmailLabelTooLong = 131094,

    /// <summary>See <see cref="ReasonCode.EmailAddressTooLong"/>.</summary>
    EmailAddressTooLong = 131095,

    /// <summary>See <see cref="ReasonCode.EmailObsoleteSyntax"/>.</summary>
    EmailObsoleteSyntax = 131096,

    /// <summary>See <see cref="ReasonCode.EmailSingleLabelDomain"/>.</summary>
    EmailSingleLabelDomain = 131097,

    /// <summary>See <see cref="ReasonCode.EmailPossibleTypo"/>.</summary>
    EmailPossibleTypo = 131098,

    /// <summary>See <see cref="ReasonCode.EmailNotSmtpLiteral"/>.</summary>
    EmailNotSmtpLiteral = 131099,

    /// <summary>See <see cref="ReasonCode.WebHostMissing"/>.</summary>
    WebHostMissing = 196609,

    /// <summary>See <see cref="ReasonCode.WebInvalidHost"/>.</summary>
    WebInvalidHost = 196610,

    /// <summary>See <see cref="ReasonCode.WebInvalidPort"/>.</summary>
    WebInvalidPort = 196611,

    /// <summary>See <see cref="ReasonCode.WebInvalidUrl"/>.</summary>
    WebInvalidUrl = 196612,

    /// <summary>See <see cref="ReasonCode.AddressFreeTextRefused"/>.</summary>
    AddressFreeTextRefused = 262244,

    /// <summary>See <see cref="ReasonCode.AddressCountryRequired"/>.</summary>
    AddressCountryRequired = 262245,

    /// <summary>See <see cref="ReasonCode.AddressModelRequired"/>.</summary>
    AddressModelRequired = 262254,

    /// <summary>See <see cref="ReasonCode.AddressCountryUndetermined"/>.</summary>
    AddressCountryUndetermined = 262255,

    /// <summary>See <see cref="ReasonCode.AddressUnsupportedCountry"/>.</summary>
    AddressUnsupportedCountry = 262256,

    /// <summary>See <see cref="ReasonCode.AddressCountryConflict"/>.</summary>
    AddressCountryConflict = 262257,

    /// <summary>See <see cref="ReasonCode.AddressNoDeliveryLine"/>.</summary>
    AddressNoDeliveryLine = 262258,

    /// <summary>See <see cref="ReasonCode.AddressNoLastLine"/>.</summary>
    AddressNoLastLine = 262259,

    /// <summary>See <see cref="ReasonCode.AddressUnplacedText"/>.</summary>
    AddressUnplacedText = 262260,

    /// <summary>See <see cref="ReasonCode.AddressFieldsDisputed"/>.</summary>
    AddressFieldsDisputed = 262261,

    /// <summary>See <see cref="ReasonCode.PersonNotAPerson"/>.</summary>
    PersonNotAPerson = 327681,

    /// <summary>See <see cref="ReasonCode.PersonMultiple"/>.</summary>
    PersonMultiple = 327682,

    /// <summary>See <see cref="ReasonCode.PersonNoName"/>.</summary>
    PersonNoName = 327683,

    /// <summary>See <see cref="ReasonCode.PersonUnexpectedCharacters"/>.</summary>
    PersonUnexpectedCharacters = 327684,

    /// <summary>See <see cref="ReasonCode.PersonTooLong"/>.</summary>
    PersonTooLong = 327685,

    /// <summary>See <see cref="ReasonCode.PersonAmbiguousConnector"/>.</summary>
    PersonAmbiguousConnector = 327686,

    /// <summary>See <see cref="ReasonCode.PersonSingleWord"/>.</summary>
    PersonSingleWord = 327687,

    /// <summary>See <see cref="ReasonCode.PersonCasingRestored"/>.</summary>
    PersonCasingRestored = 327688,

    /// <summary>See <see cref="ReasonCode.PersonFamilyNameGuessed"/>.</summary>
    PersonFamilyNameGuessed = 327689,

    /// <summary>See <see cref="ReasonCode.PersonNoEvidence"/>.</summary>
    PersonNoEvidence = 327690,

    /// <summary>See <see cref="ReasonCode.CompanyNoBase"/>.</summary>
    CompanyNoBase = 393217,

    /// <summary>See <see cref="ReasonCode.CompanyCareOfOnly"/>.</summary>
    CompanyCareOfOnly = 393218,

    /// <summary>See <see cref="ReasonCode.CompanyLegalFormAmbiguous"/>.</summary>
    CompanyLegalFormAmbiguous = 393219,

    /// <summary>See <see cref="ReasonCode.CompanyLegalFormOutsideCountry"/>.</summary>
    CompanyLegalFormOutsideCountry = 393220,

    /// <summary>See <see cref="ReasonCode.CompanyLegalFormInactive"/>.</summary>
    CompanyLegalFormInactive = 393221,

    /// <summary>See <see cref="ReasonCode.CompanyEmptyTradingName"/>.</summary>
    CompanyEmptyTradingName = 393222,

    /// <summary>See <see cref="ReasonCode.CompanyTooLong"/>.</summary>
    CompanyTooLong = 393223,
}
