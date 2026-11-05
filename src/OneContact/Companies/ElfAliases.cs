namespace RecursiveAI.OneContact.Companies;

/// <summary>
/// Spellings of legal forms that people write every day and the ISO 20275 list does not carry (it has no abbreviation for the Czech
/// "s.r.o." or the Portuguese "Lda", for instance). Each entry ties a spelling in a country to the name of a listed form in that
/// country; a name that is not in the list is skipped, so a newer list that renames a form drops the alias instead of breaking the
/// load. These are our own additions, written from the legal forms' published names, not part of the GLEIF data.
/// </summary>
internal static class ElfAliases
{
    internal static readonly (string Country, string Alias, string Name)[] All =
    [
        ("CZ", "s.r.o.", "Společnost s ručením omezeným"),
        ("CZ", "spol. s r.o.", "Společnost s ručením omezeným"),
        ("CZ", "a.s.", "Akciová společnost"),
        ("CZ", "v.o.s.", "Veřejná obchodní společnost"),
        ("CZ", "k.s.", "Společnost komanditní"),
        ("CZ", "z.s.", "Spolek"),
        ("CZ", "o.p.s.", "Obecně prospěšná společnost"),
        ("SK", "s.r.o.", "Spoločnosť s ručením obmedzeným"),
        ("SK", "a.s.", "Akciová spoločnosť"),
        ("SK", "v.o.s.", "Verejná obchodná spoločnosť"),
        ("SK", "k.s.", "Komanditná spoločnosť"),
        ("PT", "Lda", "Sociedade por Quotas"),
        ("PT", "Lda.", "Sociedade por Quotas"),
        ("PT", "Limitada", "Sociedade por Quotas"),
        ("PT", "Unipessoal Lda", "Sociedade Unipessoal por Quotas"),
        ("PT", "S.A.", "Sociedade Anónima"),
        ("PT", "SA", "Sociedade Anónima"),
        ("IE", "DAC", "Designated Activity Company"),
        ("ES", "S.A.", "Sociedad Anonima"),
        ("ES", "SA", "Sociedad Anonima"),
        ("DE", "e. V.", "eingetragener Verein"),
        ("FR", "SARL", "Société à responsabilité limitée (sans autre indication)"),
        ("FR", "S.A.R.L.", "Société à responsabilité limitée (sans autre indication)"),
    ];
}
