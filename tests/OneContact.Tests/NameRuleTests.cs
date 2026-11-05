using System.Globalization;
using System.Text.Json;
using RecursiveAI.OneContact.Companies;
using RecursiveAI.OneContact.Keys;
using RecursiveAI.OneContact.Names;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact.Tests;

/// <summary>Rule cases for person and company names, read from testdata/names (written for this repository, synthetic names only).</summary>
public class NameRuleTests
{
    private static string DataPath(string file) => Path.Combine(AppContext.BaseDirectory, "testdata", "names", file);

    private static IEnumerable<string[]> Rows(string file, int columns)
    {
        foreach (string line in File.ReadLines(DataPath(file)))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] c = line.Split('|');
            Assert.True(c.Length == columns, $"{file}: expected {columns} columns in: {line}");
            yield return c;
        }
    }

    internal static NameHints Hints(string hint)
    {
        if (hint == "-")
        {
            return default;
        }

        Region country = Region.Unknown;
        string? language = null;
        foreach (string p in hint.Split('/'))
        {
            if (p.Length == 2 && char.IsUpper(p[0]))
            {
                country = Region.Parse(p);
            }
            else
            {
                language = p;
            }
        }

        return new NameHints(country, language);
    }

    private static string Empty(string cell) => cell == "_" ? string.Empty : cell;

    [Fact]
    public void Person_cases()
    {
        int count = 0;
        List<string> failures = [];
        foreach (string[] c in Rows("person-cases.psv", 14))
        {
            count++;
            NameHints hints = Hints(c[1]);
            NameParseResult r = NameParsing.Split(c[2], hints);
            string expectedClass = c[3];
            if (!string.Equals(r.Class.ToString(), expectedClass, StringComparison.OrdinalIgnoreCase) || r.Persons.Count != 1)
            {
                failures.Add($"{c[0]}: {c[2]}: class {r.Class}, persons {r.Persons.Count}");
                continue;
            }

            PersonName p = r.Persons[0];
            string[] got = [p.Prefix.Text, p.Given.Text, p.Middle.Text, p.Particle.Text, p.Family.Text, p.SecondFamily.Text, p.Suffix.Text, p.ProfessionalSuffix.Text, p.Nickname.Text, p.Canonical];
            for (int i = 0; i < got.Length; i++)
            {
                if (got[i] != Empty(c[4 + i]))
                {
                    failures.Add($"{c[0]}: {c[2]}: part {i} is '{got[i]}', expected '{Empty(c[4 + i])}'");
                }
            }

            // PersonName.Parse gives the same value as the split.
            Assert.Equal(p.Canonical, PersonName.Parse(c[2], hints).Canonical);
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(count >= 120, $"only {count} person cases");
    }

    [Fact]
    public void Multiple_and_undecided_cases()
    {
        List<string> failures = [];
        foreach (string[] c in Rows("multi-cases.psv", 5))
        {
            NameParseResult r = NameParsing.Split(c[2], Hints(c[1]));
            if (!string.Equals(r.Class.ToString(), c[3], StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{c[0]}: '{c[2]}': class {r.Class}, expected {c[3]}");
                continue;
            }

            if (c[3] is "multiple" or "person")
            {
                string got = string.Join(" // ", r.Persons.Select(p => p.Canonical));
                if (got != c[4])
                {
                    failures.Add($"{c[0]}: '{c[2]}': '{got}', expected '{c[4]}'");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Company_cases()
    {
        int count = 0;
        List<string> failures = [];
        foreach (string[] c in Rows("company-cases.psv", 10))
        {
            count++;
            NameHints hints = Hints(c[1]);
            CompanyName name = CompanyName.Parse(c[2], hints);
            void Check(string what, string got, string expected)
            {
                if (expected != "*" && got != Empty(expected))
                {
                    failures.Add($"{c[0]}: {c[2]}: {what} is '{got}', expected '{Empty(expected)}'");
                }
            }

            Check("article", name.LeadingArticle.Text, c[3]);
            Check("base", name.Base.Text, c[4]);
            Check("form", name.LegalForm?.Text ?? string.Empty, c[5]);
            string elf = name.LegalForm is null ? string.Empty : name.LegalForm.IsAmbiguous ? "?" : name.LegalForm.ElfCode;
            Check("elf", elf, c[6]);
            Check("trading", name.TradingAs.Text, c[7]);
            Check("care of", name.CareOf.Text, c[8]);
            Check("canonical", name.Canonical, c[9]);

            // Several candidates are listed exactly when no code was chosen.
            if (name.LegalForm is { IsAmbiguous: true } form)
            {
                Assert.True(form.Candidates.Count > 1, $"{c[2]}: ambiguous without candidates");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(count >= 50, $"only {count} company cases");
    }

    [Fact]
    public void Every_alias_names_a_listed_form()
    {
        ElfTable table = ElfTable.Instance;
        HashSet<string> names = [];
        foreach (ElfRow row in table.Rows)
        {
            names.Add(row.Country + "|" + Names_Folded(row.Name));
        }

        foreach ((string country, string alias, string name) in ElfAliases.All)
        {
            Assert.True(names.Contains(country + "|" + Names_Folded(name)), $"alias {alias} in {country} names '{name}', which is not in the list");
        }
    }

    private static string Names_Folded(string text) => new([.. text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD).Where(char.IsLetterOrDigit)]);

    [Fact]
    public void Gold_set_company_forms_are_found_in_every_tier_language()
    {
        // The synthetic gold set (testdata/names/gold.tsv) says which words are the legal form; the rules must find them.
        List<string> failures = [];
        int count = 0;
        foreach (string line in File.ReadLines(DataPath("gold.tsv")).Skip(1))
        {
            string[] c = line.Split('\t');
            if (c[3] != "company" || c[6] != "1")
            {
                continue;
            }

            count++;
            string[] words = c[4].Split(' ');
            string[] labels = c[5].Split(' ');
            string expectedForm = string.Join(' ', words.Where((_, i) => labels[i] == "L"));
            CompanyName name = CompanyName.Parse(c[4], Hints(c[2]));
            if (name.LegalForm is null || !string.Equals(name.LegalForm.Text, expectedForm, StringComparison.Ordinal))
            {
                failures.Add($"{c[1]}: {c[4]}: form '{name.LegalForm?.Text}', expected '{expectedForm}'");
            }
            else if (c[7] != "?" && c[7].Length > 0 && name.LegalForm.ElfCode != c[7])
            {
                failures.Add($"{c[1]}: {c[4]}: ELF '{name.LegalForm.ElfCode}', expected '{c[7]}'");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(count > 100);
    }

    [Fact]
    public void Offsets_index_the_original_input()
    {
        const string text = "  Dr.   Jane  \"JJ\"  van   der Berg ,  PhD ";
        Assert.Equal(System.Buffers.OperationStatus.Done, PersonName.TryParse(text, out PersonName p, out ParseDetail detail));
        Assert.Equal("Dr.", text.Substring(p.Prefix.Span.Start, p.Prefix.Span.Length));
        Assert.Equal("Jane", text.Substring(p.Given.Span.Start, p.Given.Span.Length));
        Assert.Equal("JJ", text.Substring(p.Nickname.Span.Start, p.Nickname.Span.Length));
        Assert.Equal("van   der", text.Substring(p.Particle.Span.Start, p.Particle.Span.Length));
        Assert.Equal("van der", p.Particle.Text);
        Assert.Equal("Berg", text.Substring(p.Family.Span.Start, p.Family.Span.Length));
        Assert.Equal("PhD", text.Substring(p.ProfessionalSuffix.Span.Start, p.ProfessionalSuffix.Span.Length));
        Assert.Equal("Dr.   Jane  \"JJ\"  van   der Berg ,  PhD", text.Substring(p.Source.Start, p.Source.Length));
        foreach (ParsedField f in detail.Fields)
        {
            Assert.Equal(DecisionSource.Rule, f.Source);
            Assert.Equal(1f, f.Confidence);
            Assert.InRange(f.Span.Start, 0, text.Length);
        }

        // Casing restoration keeps the offsets of the input, not of the restored text.
        const string shout = "  JOHN   MCDONALD ";
        PersonName q = PersonName.Parse(shout);
        Assert.Equal("John McDonald", q.Canonical);
        Assert.Equal("MCDONALD", shout.Substring(q.Family.Span.Start, q.Family.Span.Length));
        Assert.True(q.Flags.HasFlag(NameFacts.CasingRestored));

        // Company parts.
        const string company = "  The Acme   Widgets , Inc. c/o  Jane Roe ";
        CompanyName c = CompanyName.Parse(company);
        Assert.Equal("Acme   Widgets", company.Substring(c.Base.Span.Start, c.Base.Span.Length));
        Assert.Equal("Inc.", company.Substring(c.LegalForm!.Span.Start, c.LegalForm.Span.Length));
        Assert.Equal("Jane Roe", company.Substring(c.CareOf.Span.Start, c.CareOf.Span.Length));
        Assert.Equal("The", company.Substring(c.LeadingArticle.Span.Start, c.LeadingArticle.Span.Length));
    }

    [Fact]
    public void Multiple_people_keep_offsets_into_the_input_and_share_the_family_name_where_it_was_written()
    {
        const string text = "John and Jane Smith";
        NameParseResult r = NameParsing.Split(text);
        Assert.Equal(NameClass.Multiple, r.Class);
        Assert.Equal(2, r.Persons.Count);
        foreach (PersonName p in r.Persons)
        {
            Assert.Equal("Smith", text.Substring(p.Family.Span.Start, p.Family.Span.Length));
        }

        Assert.Equal("John", text.Substring(r.Persons[0].Given.Span.Start, r.Persons[0].Given.Span.Length));
        Assert.Equal("Jane", text.Substring(r.Persons[1].Given.Span.Start, r.Persons[1].Given.Span.Length));
        Assert.Contains(r.Detail.Fields, f => f.Name == "person2.given");
    }

    [Fact]
    public void Parse_refuses_what_is_not_one_person()
    {
        ContactParseException company = Assert.Throws<ContactParseException>(() => PersonName.Parse("Acme Holdings Ltd"));
        Assert.Equal(ReasonCode.PersonNotAPerson, company.Reason);
        ContactParseException many = Assert.Throws<ContactParseException>(() => PersonName.Parse("John and Jane Smith"));
        Assert.Equal(ReasonCode.PersonMultiple, many.Reason);
        Assert.Throws<ContactParseException>(() => PersonName.Parse("   "));
        Assert.Throws<ContactParseException>(() => PersonName.Parse("Dr."));
        Assert.False(PersonName.TryParse("12345", out _));
        Assert.True(PersonName.TryParse("Madonna", out PersonName one));
        Assert.True(one.Flags.HasFlag(NameFacts.SingleWord));
        Assert.Throws<ContactParseException>(() => CompanyName.Parse(" "));
    }

    [Fact]
    public void Results_do_not_depend_on_the_current_culture()
    {
        string[] inputs = ["TITLE İSTANBUL IŞIK", "DR. MARÍA JOSÉ GARCÍA LÓPEZ", "SIA ĒRĪTE", "ΑΝΝΑ ΠΑΠΑΣ", "Acme Ìnc. D/B/A ĪNC"];
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            List<string> reference = [];
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string s in inputs)
            {
                reference.Add(NameParsing.Split(s).Class + "|" + Describe(s));
            }

            foreach (string culture in new[] { "tr-TR", "az-Latn-AZ", "lt-LT", "de-DE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                for (int i = 0; i < inputs.Length; i++)
                {
                    Assert.Equal(reference[i], NameParsing.Split(inputs[i]).Class + "|" + Describe(inputs[i]));
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        static string Describe(string s)
        {
            NameParseResult r = NameParsing.Split(s);
            return string.Join(";", r.Persons.Select(p => p.Canonical + "/" + p.Key)) + "/" + r.Company.Canonical + "/" + r.Company.Key;
        }
    }

    [Fact]
    public void Json_round_trips_the_canonical_form()
    {
        PersonName p = PersonName.Parse("Smith, Robert \"Bob\" Jr.");
        string json = JsonSerializer.Serialize(p, ContactJsonContext.Default.PersonName);
        PersonName back = JsonSerializer.Deserialize(json, ContactJsonContext.Default.PersonName);
        Assert.Equal(p.Canonical, back.Canonical);
        Assert.Equal(p.Key, back.Key);

        CompanyName c = CompanyName.Parse("The Acme Aktiengesellschaft t/a Roadrunner", NameHints.ForCountry(Region.Parse("DE")));
        string cj = JsonSerializer.Serialize(c, ContactJsonContext.Default.CompanyName);
        CompanyName cb = JsonSerializer.Deserialize(cj, ContactJsonContext.Default.CompanyName);
        Assert.Equal(c.Canonical, cb.Canonical);
        Assert.Equal(c.Key, cb.Key);
    }

    [Fact]
    public void Person_key_v1()
    {
        // docs/name-keys.md: every rule has a case here, and a published version never changes.
        void Same(string a, string b) => Assert.Equal(PersonName.Parse(a).GetKey(KeyVersion.V1), PersonName.Parse(b).GetKey(KeyVersion.V1));
        void Differ(string a, string b) => Assert.NotEqual(PersonName.Parse(a).GetKey(KeyVersion.V1), PersonName.Parse(b).GetKey(KeyVersion.V1));

        Assert.Equal("robert smith", PersonName.Parse("Bob Smith").Key);
        Assert.Equal("robert smith", PersonName.Parse("Dr. Robert \"Bobby\" Smith Jr., PhD").Key);
        Assert.Equal("robert j smith", PersonName.Parse("Robert James Smith").Key);
        Assert.Equal("elizabeth obrien", PersonName.Parse("Liz O'Brien").Key);
        Assert.Equal("jose garcialopez", PersonName.Parse("José García López", NameHints.ForCountry(Region.Parse("ES"))).Key);
        Assert.Equal("anna vanderberg", PersonName.Parse("Anna van der Berg").Key);
        Same("Anna van der Berg", "ANNA VANDERBERG");
        Same("Mary-Anne Smith", "Maryanne Smith");
        Same("Hans Müller", "Hans Muller");
        Same("Smith, John", "Mr John Smith");
        Differ("John Smith", "John Q. Smith");
        Differ("John Smith", "Jane Smith");
        Differ("Al Smith", "Alan Smith");
        Differ("Sam Smith", "Samuel Smith");
        Assert.Equal(string.Empty, default(PersonName).Key);
        Assert.Equal(PersonName.Parse("John Smith").Key, PersonName.Parse("John Smith").GetKey(KeyVersion.V1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PersonKey.Compute(PersonName.Parse("John Smith"), (KeyVersion)99));
    }

    [Fact]
    public void Company_key_v1()
    {
        void Same(string a, string b) => Assert.Equal(CompanyName.Parse(a).GetKey(KeyVersion.V1), CompanyName.Parse(b).GetKey(KeyVersion.V1));
        void Differ(string a, string b) => Assert.NotEqual(CompanyName.Parse(a).GetKey(KeyVersion.V1), CompanyName.Parse(b).GetKey(KeyVersion.V1));

        Assert.Equal("acme muster|8Z6G", CompanyName.Parse("The ACME Muster GmbH & Co. KG").Key);
        Same("Acme Holdings Ltd", "ACME HOLDINGS LIMITED");
        Same("Acme Inc.", "Acme Incorporated");
        Same("Acme Corp", "The Acme Corporation");
        Same("Smith & Sons", "Smith and Sons");
        Same("Muster Aktiengesellschaft", "Muster AG");
        Same("Müller GmbH", "Muller GmbH");
        Same("Acme Ltd d/b/a Roadrunner", "Acme Ltd");
        Same("Acme Corp c/o John Smith", "Acme Corp");
        Differ("Acme Ltd", "Acme Inc");
        Differ("Acme", "Acme Ltd");
        Differ("Acme Pty Ltd", "Acme Ltd");
        Assert.Equal(string.Empty, default(CompanyName).Key);
        Assert.Throws<ArgumentOutOfRangeException>(() => CompanyKey.Compute(CompanyName.Parse("Acme"), (KeyVersion)99));
    }

    [Fact]
    public void Hostile_inputs_do_not_throw()
    {
        string[] inputs =
        [
            new string('A', 100_000), string.Join(' ', Enumerable.Repeat("von", 5000)), new string('"', 2000), "((((((((((", "\"\"\"\"", "'a' 'b' 'c'",
            "&", "&&&&", " and and and ", ",,,,", ";;;;", "\n\n\n", "\ud800", "a\u0000b", string.Join(" & ", Enumerable.Repeat("X", 3000)),
            "c/o c/o c/o", "d/b/a", "t/a t/a", "Ltd Ltd Ltd Ltd", "The The The", "Dr. Dr. Dr. Dr. Dr.", "Jr. Jr. Jr.", "y y y y", "O'", "-", ".", "Mc", "Van",
        ];
        foreach (string s in inputs)
        {
            NameParseResult r = NameParsing.Split(s);
            Assert.NotNull(r.Detail);
            _ = PersonName.TryParse(s, out _);
            _ = CompanyName.TryParse(s, out _);
        }
    }
}
