namespace RecursiveAI.OneContact.Companies;

/// <summary>One entity legal form of the ISO 20275 / GLEIF ELF code list that a written legal form may stand for.</summary>
/// <param name="ElfCode">The four-character ELF code.</param>
/// <param name="Country">The ISO 3166-1 alpha-2 code of the jurisdiction the code belongs to.</param>
/// <param name="Name">The legal form's name in the jurisdiction's own language, as the list gives it.</param>
/// <param name="IsActive"><see langword="false"/> when the list marks the code inactive.</param>
public readonly record struct ElfCandidate(string ElfCode, string Country, string Name, bool IsActive);

/// <summary>
/// The legal form found in a company name: what was written, where, and which ELF code it is. When the written form fits several
/// codes ("Ltd", "SA", "GmbH" in several countries) and the hints do not single one out, <see cref="ElfCode"/> is empty and
/// <see cref="Candidates"/> lists them; nothing is guessed.
/// </summary>
/// <param name="Text">The legal form as written in the input.</param>
/// <param name="Span">Where it is in the original input, in UTF-16 code units.</param>
/// <param name="ElfCode">The ELF code, or an empty string when the form is ambiguous.</param>
/// <param name="Abbreviation">The canonical abbreviation used in the canonical form ("Aktiengesellschaft" gives "AG"); empty when the list has none for this form.</param>
/// <param name="Candidates">Every ELF code the form may stand for after the country hint was applied, in code order.</param>
/// <param name="IsPrefix"><see langword="true"/> when the form stood before the name ("AB Volvo", "SIA Rota").</param>
public sealed record CompanyLegalForm(string Text, SourceSpan Span, string ElfCode, string Abbreviation, IReadOnlyList<ElfCandidate> Candidates, bool IsPrefix)
{
    /// <summary>Gets a value indicating whether the written form fits several ELF codes and none was chosen.</summary>
    public bool IsAmbiguous => ElfCode.Length == 0;

    // The token that stands for the form in the company key; computed without any hint so the key never depends on one.
    internal string KeyToken { get; init; } = string.Empty;
}
