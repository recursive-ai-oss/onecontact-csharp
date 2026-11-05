namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>A name stated after a label such as "Name:" or "Company:".</summary>
/// <param name="ValueStart">Index of the first character of the name.</param>
/// <param name="ValueEnd">Index past the last character of the name.</param>
/// <param name="Company">True for a company label, false for a person label.</param>
internal readonly record struct LabelHit(int ValueStart, int ValueEnd, bool Company);

/// <summary>
/// Reads the names that a line states itself: "Name: Jane Doe", "Contact person: Jan Kowalski", "Company: Acme Ltd". The label is what
/// makes the value certain; a name with no label is left to a model. A value must look like a name (a person's has two to five words
/// that begin with capital letters, and no digits) so that "Name: see below" gives nothing.
/// </summary>
internal static class LabelCandidates
{
    private static readonly string[] PersonLabels =
    [
        "name", "full name", "contact name", "contact person", "attn", "attention", "person", "nom", "nombre", "naam", "navn", "imi\u0119 i nazwisko",
    ];

    private static readonly string[] CompanyLabels =
    [
        "company", "company name", "organization", "organisation", "employer", "business", "business name", "firm", "soci\u00e9t\u00e9", "empresa", "firma", "bedrijf",
    ];

    private static readonly string[] Particles = ["van", "von", "de", "der", "den", "da", "di", "du", "la", "le", "del", "della", "bin", "al", "dos", "das", "ten", "ter"];

    public static bool TryFind(string text, int start, int end, out LabelHit hit)
    {
        hit = default;
        int i = start;
        while (i < end && text[i] is ' ' or '\t' or '\u00A0' or '-' or '*' or '\u2022' or '\u2013')
        {
            i++;
        }

        int colon = text.IndexOf(':', i, end - i);
        if (colon < 0 || colon - i > 24)
        {
            return false;
        }

        string label = text.Substring(i, colon - i).Trim();
        bool person = Array.Exists(PersonLabels, l => label.Equals(l, StringComparison.OrdinalIgnoreCase));
        bool company = !person && Array.Exists(CompanyLabels, l => label.Equals(l, StringComparison.OrdinalIgnoreCase));
        if (!person && !company)
        {
            return false;
        }

        int vs = colon + 1;
        while (vs < end && text[vs] is ' ' or '\t' or '\u00A0')
        {
            vs++;
        }

        int ve = end;
        while (ve > vs && text[ve - 1] is ' ' or '\t' or '\u00A0' or ',' or ';')
        {
            ve--;
        }

        if (ve - vs < 2 || ve - vs > 80)
        {
            return false;
        }

        string value = text.Substring(vs, ve - vs);
        if (value.Contains('@', StringComparison.Ordinal) || value.Contains("://", StringComparison.Ordinal) || !LooksLikeName(value, company))
        {
            return false;
        }

        hit = new LabelHit(vs, ve, company);
        return true;
    }

    private static bool LooksLikeName(string value, bool company)
    {
        string[] words = value.Split([' ', '\u00A0'], StringSplitOptions.RemoveEmptyEntries);
        if (company)
        {
            return words.Length is >= 1 and <= 8 && char.IsLetterOrDigit(words[0][0]);
        }

        if (words.Length is < 2 or > 5)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (char.IsDigit(c) || c is '(' or ')' or '/' or '\\' or '_' or '<' or '>' or '=' or '#')
            {
                return false;
            }
        }

        // The first and last words begin with a capital letter; words between may be particles ("de", "van der").
        if (!char.IsUpper(words[0][0]) || !char.IsUpper(words[^1][0]))
        {
            return false;
        }

        for (int i = 1; i < words.Length - 1; i++)
        {
            if (!char.IsUpper(words[i][0]) && !Array.Exists(Particles, p => p.Equals(words[i], StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }
}
