namespace RecursiveAI.OneContact;

public readonly partial struct ReasonCode
{
    /// <summary>The text has nothing but a legal form ("Ltd") and no name.</summary>
    public static ReasonCode CompanyNoBase { get; } = new(ContactKind.Company, 1, "no-base", "the text has a legal form but no company name");

    /// <summary>The text only says <c>c/o</c> something: the care-of target was taken as the company.</summary>
    public static ReasonCode CompanyCareOfOnly { get; } = new(ContactKind.Company, 2, "care-of-only", "the text starts with c/o, so the name after it was taken as the company");

    /// <summary>The legal form fits several ELF codes and the rules cannot choose one; the candidates are on the legal form.</summary>
    public static ReasonCode CompanyLegalFormAmbiguous { get; } = new(ContactKind.Company, 3, "legal-form-ambiguous", "the legal form fits several ELF codes");

    /// <summary>The country hint has no such legal form, so every jurisdiction was considered.</summary>
    public static ReasonCode CompanyLegalFormOutsideCountry { get; } = new(ContactKind.Company, 4, "legal-form-outside-country", "the legal form is not one of the given country's");

    /// <summary>The only legal forms that fit are marked inactive in the list.</summary>
    public static ReasonCode CompanyLegalFormInactive { get; } = new(ContactKind.Company, 5, "legal-form-inactive", "the legal form is no longer active in its jurisdiction");

    /// <summary>The text names a trading name with nothing in it.</summary>
    public static ReasonCode CompanyEmptyTradingName { get; } = new(ContactKind.Company, 6, "empty-trading-name", "the text says trading as but names nothing");
}
