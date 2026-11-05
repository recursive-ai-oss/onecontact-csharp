namespace RecursiveAI.OneContact;

/// <summary>Facts found about a <see cref="CompanyName"/>. Flags report; they never change the value.</summary>
[Flags]
public enum CompanyFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>A legal form was found.</summary>
    HasLegalForm = 1,

    /// <summary>The legal form fits several ELF codes and none was chosen; <c>LegalForm.Candidates</c> lists them.</summary>
    LegalFormAmbiguous = 2,

    /// <summary>Every legal form that fits is marked inactive in the ELF list.</summary>
    LegalFormInactive = 4,

    /// <summary>The legal form stood before the name ("AB Volvo").</summary>
    LegalFormBeforeName = 8,

    /// <summary>The text has a trading name ("d/b/a", "t/a").</summary>
    HasTradingName = 16,

    /// <summary>The text has a care-of part ("c/o").</summary>
    HasCareOf = 32,

    /// <summary>The name starts with "The".</summary>
    HasLeadingArticle = 64,
}
