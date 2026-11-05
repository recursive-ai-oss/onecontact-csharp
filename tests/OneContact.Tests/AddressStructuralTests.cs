using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Structural;
using RecursiveAI.OneContact.Data;

namespace RecursiveAI.OneContact.Tests;

public sealed class AddressStructuralTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "onecontact-addr-" + Guid.NewGuid().ToString("N"));

    public AddressStructuralTests() => Directory.CreateDirectory(_work);

    public void Dispose() => Directory.Delete(_work, recursive: true);

    private static PostalAddress Us(string? admin = "CA", string? locality = "Mountain View", string? zip = "94043", params string[] lines) =>
        PostalAddress.Create(Region.Parse("US"), admin, locality, postalCode: zip, streetLines: lines.Length == 0 ? ["1600 Amphitheatre Parkway"] : lines);

    // --- the compiled data ---

    [Fact]
    public void The_embedded_tables_are_what_the_ingest_tool_makes_of_the_pinned_file_again()
    {
        string pinned = File.ReadAllText(AddressTestData.PinnedFile());
        _ = AddressTestData.Compile(pinned, Path.Combine(_work, "a"), out byte[] first);
        _ = AddressTestData.Compile(pinned, Path.Combine(_work, "b"), out byte[] second);
        Assert.Equal(first, second);

        using Stream resource = typeof(PostalAddress).Assembly.GetManifestResourceStream("OneContact.Data.libaddressinput.ocd")!;
        using MemoryStream embedded = new();
        resource.CopyTo(embedded);
        Assert.Equal(first, embedded.ToArray());
    }

    [Fact]
    public void The_data_describes_the_countries_the_pinned_file_lists()
    {
        Assert.Equal(AddressDataSet.Embedded.Countries.Count, AddressRegions.All.Count);
        Assert.True(AddressRegions.All.Count >= 240);
        Assert.Contains(Region.Parse("US"), AddressRegions.All);
        Assert.Contains(Region.Parse("XK"), AddressRegions.All);
        Assert.DoesNotContain(Region.Parse("ZZ"), AddressRegions.All);
    }

    [Fact]
    public void Truncated_and_damaged_tables_are_refused_with_InvalidDataException_and_nothing_else()
    {
        string pinned = File.ReadAllText(AddressTestData.PinnedFile());
        _ = AddressTestData.Compile(pinned, Path.Combine(_work, "t"), out byte[] image);
        CompiledTableFile file = CompiledTableFile.Read(image);
        file.TryGetTable("props", out ReadOnlyMemory<byte> props);
        file.TryGetTable("strings", out ReadOnlyMemory<byte> strings);
        file.TryGetTable("nodes", out ReadOnlyMemory<byte> nodes);

        // Every prefix of the small tables and a spread of prefixes of the large ones.
        foreach (int length in Lengths(strings.Length))
        {
            Assert.Throws<InvalidDataException>(() => AddressDataSet.Load(props, strings[..length], nodes));
        }

        foreach (int length in Lengths(nodes.Length))
        {
            Assert.Throws<InvalidDataException>(() => AddressDataSet.Load(props, strings, nodes[..length]));
        }

        // Single bytes changed: either still a valid data set or refused cleanly.
        Random random = new(20260105);
        byte[] damagedNodes = nodes.ToArray();
        byte[] damagedStrings = strings.ToArray();
        for (int i = 0; i < 600; i++)
        {
            bool inNodes = random.Next(2) == 0;
            byte[] target = inNodes ? damagedNodes : damagedStrings;
            int at = random.Next(target.Length);
            byte old = target[at];
            target[at] = (byte)random.Next(256);
            try
            {
                _ = AddressDataSet.Load(props, damagedStrings, damagedNodes);
            }
            catch (InvalidDataException)
            {
            }

            target[at] = old;
        }
    }

    private static IEnumerable<int> Lengths(int total)
    {
        for (int i = 0; i < total && i < 48; i++)
        {
            yield return i;
        }

        for (int i = 48; i < total; i += Math.Max(1, total / 400))
        {
            yield return i;
        }

        yield return total - 1;
    }

    [Fact]
    public void A_short_mutation_run_over_the_compiled_tables_never_escapes_InvalidDataException()
    {
        // The run length is a few seconds here; ONECONTACT_FUZZ_SECONDS lengthens it for a smoke run.
        double seconds = double.TryParse(Environment.GetEnvironmentVariable("ONECONTACT_FUZZ_SECONDS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : 3;
        string pinned = File.ReadAllText(AddressTestData.PinnedFile());
        _ = AddressTestData.Compile(pinned, Path.Combine(_work, "f"), out byte[] image);
        CompiledTableFile file = CompiledTableFile.Read(image);
        file.TryGetTable("props", out ReadOnlyMemory<byte> props);
        file.TryGetTable("strings", out ReadOnlyMemory<byte> strings);
        file.TryGetTable("nodes", out ReadOnlyMemory<byte> nodes);

        Random random = new(7);
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        long runs = 0;
        long accepted = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            byte[] p = props.ToArray();
            byte[] st = strings.ToArray();
            byte[] n = nodes.ToArray();
            int edits = 1 + random.Next(4);
            for (int e = 0; e < edits; e++)
            {
                byte[] target = random.Next(3) switch { 0 => p, 1 => st, _ => n };
                if (target.Length == 0)
                {
                    continue;
                }

                // Small tables get hit more often at the front, where the counts and offsets are.
                int at = random.Next(2) == 0 ? random.Next(Math.Min(target.Length, 64)) : random.Next(target.Length);
                target[at] = random.Next(4) == 0 ? (byte)0xFF : (byte)random.Next(256);
            }

            try
            {
                AddressDataSet set = AddressDataSet.Load(p, st, n);
                accepted++;

                // A damaged set that loads must still answer lookups without escaping.
                _ = set.Find("data/US");
                _ = set.Countries;
                _ = StructuralValidator.Validate(set, new AddressParts(Region.Parse("US"), null, "CA", "x", string.Empty, "94043", string.Empty, string.Empty, string.Empty, ["a"]));
            }
            catch (InvalidDataException)
            {
            }

            runs++;
        }

        Assert.True(runs > 0);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "address-tables-fuzz.txt"), $"seconds={clock.Elapsed.TotalSeconds:F1} runs={runs} loaded={accepted}\n");
    }

    // --- the postal code pattern dialect ---

    [Fact]
    public void Every_pattern_in_the_data_compiles_and_matches_like_a_regular_expression_engine()
    {
        AddressDataSet data = AddressDataSet.Embedded;
        HashSet<string> patterns = new(StringComparer.Ordinal);
        HashSet<string> samples = new(StringComparer.Ordinal) { string.Empty, "0", "A", "12345", "AB1 2CD", "999077", "800", "ASCN 1ZZ" };
        foreach (string key in data.Keys())
        {
            AddressNode node = data.Find(key)!;
            foreach (NodeProperty property in new[] { NodeProperty.Zip, NodeProperty.Xzip })
            {
                if (node[property] is { } p)
                {
                    patterns.Add(p);
                }
            }

            foreach (string p in node.List(NodeProperty.SubZips).Concat(node.List(NodeProperty.SubXzips)))
            {
                patterns.Add(p);
            }

            foreach (NodeProperty property in new[] { NodeProperty.Zipex, NodeProperty.SubZipexs })
            {
                if (node[property] is { } examples)
                {
                    foreach (string group in examples.Split('~'))
                    {
                        foreach (string item in group.Split(',', ':'))
                        {
                            samples.Add(item);
                        }
                    }
                }
            }
        }

        // Variants of the examples: shorter, longer, other case, with a different character in the middle.
        foreach (string s in samples.ToArray())
        {
            if (s.Length > 1)
            {
                samples.Add(s[..^1]);
                samples.Add(s + "1");
                samples.Add(s + "A");
                samples.Add(s.ToLowerInvariant());
                samples.Add(s[..(s.Length / 2)] + "-" + s[(s.Length / 2)..]);
                samples.Add(s[..(s.Length / 2)] + " " + s[(s.Length / 2)..]);
            }
        }

        Assert.True(patterns.Count > 1000);
        int compared = 0;
        foreach (string pattern in patterns)
        {
            PostalPattern compiled = PostalPattern.Compile(pattern);
            Regex whole = new($"^(?:{pattern})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Regex prefix = new($"^(?:{pattern})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (string sample in samples)
            {
                Assert.True(whole.IsMatch(sample) == compiled.IsMatch(sample, ignoreCase: true), $"whole: {pattern} on '{sample}'");
                Assert.True(prefix.IsMatch(sample) == compiled.IsPrefixMatch(sample, ignoreCase: true), $"prefix: {pattern} on '{sample}'");
                compared++;
            }
        }

        Assert.True(compared > 100_000);
    }

    [Theory]
    [InlineData("^a$")]
    [InlineData("(?=a)")]
    [InlineData("(?<n>a)")]
    [InlineData("a{2")]
    [InlineData("a{3,2}")]
    [InlineData("(a")]
    [InlineData("a)")]
    [InlineData("[a")]
    [InlineData("[z-a]")]
    [InlineData("*a")]
    [InlineData("a*?")]
    [InlineData("\\p{L}")]
    [InlineData("\\1")]
    [InlineData("a{101}")]
    public void Patterns_outside_the_dialect_are_refused(string pattern) =>
        Assert.Throws<FormatException>(() => PostalPattern.Compile(pattern));

    [Fact]
    public void A_pathological_pattern_cannot_run_away()
    {
        PostalPattern p = PostalPattern.Compile("(a*)*b");
        Assert.False(p.IsMatch(new string('a', 5000), ignoreCase: false));
    }

    // --- validation ---

    [Fact]
    public void A_sound_US_address_has_no_issues()
    {
        Assert.Empty(Us().Validate());
        Assert.Empty(Us(admin: "california").Validate());
        Assert.Empty(Us(zip: "94043-1234").Validate());
    }

    [Fact]
    public void Issues_name_the_field_and_the_kind()
    {
        Assert.Equal([new AddressIssue(AddressField.AdminArea, AddressIssueKind.UnknownValue)], Us(admin: "XX").Validate());
        Assert.Equal([new AddressIssue(AddressField.AdminArea, AddressIssueKind.MissingRequiredField)], Us(admin: null).Validate());
        Assert.Equal([new AddressIssue(AddressField.PostalCode, AddressIssueKind.InvalidFormat)], Us(zip: "9404").Validate());
        Assert.Equal([new AddressIssue(AddressField.PostalCode, AddressIssueKind.MismatchingValue)], Us(zip: "10001").Validate());
        Assert.Equal([new AddressIssue(AddressField.PostalCode, AddressIssueKind.MissingRequiredField)], Us(zip: "  ").Validate());
        Assert.Equal([new AddressIssue(AddressField.StreetAddress, AddressIssueKind.MissingRequiredField)], PostalAddress.Create(Region.Parse("US"), "CA", "Mountain View", postalCode: "94043").Validate());

        PostalAddress withSorting = PostalAddress.Create(Region.Parse("US"), "CA", "Mountain View", postalCode: "94043", sortingCode: "9", streetLines: ["1 Main"]);
        Assert.Equal([new AddressIssue(AddressField.SortingCode, AddressIssueKind.UnexpectedField)], withSorting.Validate());
    }

    [Fact]
    public void Checking_stops_at_the_first_wrong_level_so_the_levels_below_are_not_judged()
    {
        // Brazil lists cities under each state: an unknown state hides the question whether the city belongs to it.
        PostalAddress a = PostalAddress.Create(Region.Parse("BR"), "Nowhere", "Nowhere City", dependentLocality: "x", postalCode: "40301-110", streetLines: ["Rua 1"]);
        Assert.Equal([new AddressIssue(AddressField.AdminArea, AddressIssueKind.UnknownValue)], a.Validate());
    }

    [Fact]
    public void A_country_the_data_does_not_describe_is_unknown_and_none_is_missing()
    {
        Assert.Contains(new AddressIssue(AddressField.Country, AddressIssueKind.UnknownValue), PostalAddress.Create(Region.Parse("ZZ"), locality: "x", streetLines: ["a"]).Validate());
        Assert.Contains(new AddressIssue(AddressField.Country, AddressIssueKind.MissingRequiredField), default(PostalAddress).Validate());
        Assert.Equal(Region.Unknown, default(PostalAddress).Country);
    }

    [Fact]
    public void A_state_leads_on_to_its_own_postal_code_prefix_only_by_key_or_Latin_name()
    {
        // The state is recognised by its name, but the prefix check needs the data node, found by key or Latin name.
        Assert.Empty(Us(admin: "California", zip: "10001").Validate());
        Assert.Equal(AddressIssueKind.MismatchingValue, Assert.Single(Us(admin: "CA", zip: "10001").Validate()).Kind);
    }

    [Fact]
    public void Extended_postal_code_patterns_of_a_subdivision_replace_the_country_pattern()
    {
        // Macau's code is 999078 and the extended pattern says so, though the country's pattern would accept any six digits.
        PostalAddress ok = PostalAddress.Create(Region.Parse("CN"), "澳门", locality: "x", postalCode: "999078", streetLines: ["a"]);
        PostalAddress other = PostalAddress.Create(Region.Parse("CN"), "澳门", locality: "x", postalCode: "266033", streetLines: ["a"]);
        Assert.DoesNotContain(ok.Validate(), i => i.Field == AddressField.PostalCode);
        Assert.Contains(new AddressIssue(AddressField.PostalCode, AddressIssueKind.InvalidFormat), other.Validate());
    }

    [Fact]
    public void White_space_is_the_characters_up_to_the_space_only()
    {
        Assert.Empty(Us(admin: " \t CA \r\n").Validate());
        Assert.Equal(AddressIssueKind.UnknownValue, Assert.Single(Us(admin: "\u00a0CA").Validate()).Kind);
    }

    // --- layout ---

    [Fact]
    public void The_label_follows_the_country_layout_and_leaves_out_what_is_empty()
    {
        Assert.Equal(["Pat Jones", "Acme", "1 Main Street", "Mountain View CA 94043"],
            PostalAddress.Create(Region.Parse("US"), "CA", "Mountain View", postalCode: "94043", organization: "Acme", recipient: "Pat Jones", streetLines: ["1 Main Street"]).GetLabelLines());
        Assert.Equal(["1 Main Street", "CA 94043"], PostalAddress.Create(Region.Parse("US"), "CA", postalCode: "94043", streetLines: ["1 Main Street"]).GetLabelLines());
        Assert.Equal("1 Main Street, Mountain View CA 94043", Us(lines: "1 Main Street").ToSingleLine());
        Assert.Equal(Us(lines: "1 Main Street").ToSingleLine(), Us(lines: "1 Main Street").Canonical);
    }

    [Fact]
    public void Layouts_put_the_fields_where_the_country_does_and_keep_its_own_text()
    {
        PostalAddress de = PostalAddress.Create(Region.Parse("DE"), locality: "Berlin", postalCode: "10115", streetLines: ["Invalidenstr. 1"]);
        Assert.Equal(["Invalidenstr. 1", "10115 Berlin"], de.GetLabelLines());
        PostalAddress gg = PostalAddress.Create(Region.Parse("GG"), locality: "St Peter Port", postalCode: "GY1 1AA", streetLines: ["1 High St"]);
        Assert.Equal(["1 High St", "St Peter Port", "GUERNSEY", "GY1 1AA"], gg.GetLabelLines());
    }

    [Fact]
    public void A_Latin_script_layout_is_used_when_the_language_names_Latin_or_when_asked()
    {
        PostalAddress jp = PostalAddress.Create(Region.Parse("JP"), "Tokyo", postalCode: "100-0001", streetLines: ["1-1 Chiyoda"], recipient: "Taro", language: "ja");
        Assert.Equal(["〒100-0001", "Tokyo", "1-1 Chiyoda", "Taro"], jp.GetLabelLines());
        Assert.Equal(["Taro", "1-1 Chiyoda", "Tokyo", "100-0001"], jp.GetLabelLines(AddressScript.Latin));
        PostalAddress latin = PostalAddress.Create(Region.Parse("JP"), "Tokyo", postalCode: "100-0001", streetLines: ["1-1 Chiyoda"], recipient: "Taro", language: "JA_Latn");
        Assert.Equal("ja-latn", latin.Language);
        Assert.Equal(["Taro", "1-1 Chiyoda", "Tokyo", "100-0001"], latin.GetLabelLines());

        // A country without a Latin layout keeps its own.
        Assert.Equal(["1 Main Street", "Mountain View CA 94043"], Us(lines: "1 Main Street").GetLabelLines(AddressScript.Latin));
    }

    // --- coverage, regions and free text ---

    [Theory]
    [InlineData("US", AddressCoverage.Full)]
    [InlineData("CA", AddressCoverage.Full)]
    [InlineData("DE", AddressCoverage.Full)]
    [InlineData("GF", AddressCoverage.Full)]
    [InlineData("VA", AddressCoverage.Full)]
    [InlineData("GR", AddressCoverage.Structural)]
    [InlineData("BG", AddressCoverage.Structural)]
    [InlineData("CY", AddressCoverage.Structural)]
    [InlineData("JP", AddressCoverage.Structural)]
    [InlineData("NZ", AddressCoverage.Structural)]
    public void Coverage_is_full_for_the_tier_countries_and_structural_for_the_rest(string code, AddressCoverage expected) =>
        Assert.Equal(expected, PostalAddress.Create(Region.Parse(code)).Coverage);

    [Fact]
    public void Free_text_is_refused_for_countries_that_are_only_structural_and_the_reason_says_so()
    {
        OperationStatus status = PostalAddress.TryParse("1-1 Chiyoda, Tokyo 100-0001", Region.Parse("JP"), out PostalAddress value, out ParseDetail detail);
        Assert.Equal(OperationStatus.InvalidData, status);
        Assert.Equal(ReasonCode.AddressFreeTextRefused, detail.PrimaryReason);
        Assert.Equal(default, value);

        status = PostalAddress.TryParse("x", default, out _, out detail);
        Assert.Equal(OperationStatus.InvalidData, status);
        Assert.Equal(ReasonCode.AddressCountryRequired, detail.PrimaryReason);
    }

    [Fact]
    public void Region_information_exposes_the_structure()
    {
        Assert.True(AddressRegions.TryGet(Region.Parse("US"), out AddressRegionInfo? us));
        Assert.Equal("UNITED STATES", us!.Name);
        Assert.Equal("state", us.AdminAreaType);
        Assert.Equal("zip", us.PostalCodeType);
        Assert.Equal([AddressField.Recipient, AddressField.Organization, AddressField.StreetAddress, AddressField.Locality, AddressField.AdminArea, AddressField.PostalCode], us.FieldOrder);
        Assert.Equal([AddressField.StreetAddress, AddressField.Locality, AddressField.AdminArea, AddressField.PostalCode], us.RequiredFields);
        Assert.Contains("95014", us.PostalCodeExamples);
        Assert.True(us.Subdivisions.Count >= 50);
        Assert.True(us.TryMatchSubdivision(" calif", out _) is false);
        Assert.True(us.TryMatchSubdivision(" california ", out AddressSubdivision? california));
        Assert.Equal("CA", california!.Key);
        Assert.NotNull(california.PostalCodePrefix);

        Assert.True(AddressRegions.TryGet(Region.Parse("JP"), out AddressRegionInfo? jp));
        Assert.True(jp!.TryMatchSubdivision("hokkaido", out AddressSubdivision? hokkaido));
        Assert.Equal("北海道", hokkaido!.Key);
        Assert.NotEmpty(jp.LatinFieldOrder);

        Assert.False(AddressRegions.TryGet(Region.Parse("ZZ"), out _));
    }

    [Theory]
    [InlineData("us", "US")]
    [InlineData(" united states ", "US")]
    [InlineData("UNITED KINGDOM", "GB")]
    [InlineData("germany", "DE")]
    public void Countries_are_found_by_code_or_name(string text, string code)
    {
        Assert.True(AddressRegions.TryFindByName(text, out Region region));
        Assert.Equal(code, region.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("united")]
    [InlineData("zz")]
    public void Texts_that_name_no_country_find_none(string text) => Assert.False(AddressRegions.TryFindByName(text, out _));

    [Fact]
    public void Results_do_not_depend_on_the_current_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            PostalAddress a = PostalAddress.Create(Region.Parse("US"), "ca", "Mountain View", postalCode: "94043", streetLines: ["1 Main"]);
            Assert.Empty(a.Validate());
            Assert.Equal(["1 Main", "Mountain View ca 94043"], a.GetLabelLines());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_field_model_keeps_what_was_given()
    {
        PostalAddress a = PostalAddress.Create(Region.Parse("FR"), "Île-de-France", "Paris", "8e", "75008", "CEDEX 08", "Acme", "Pat", ["92 avenue des Champs-Élysées", "Bât. A"], "fr");
        Assert.Equal("FR", a.Country.Code);
        Assert.Equal("Île-de-France", a.AdminArea);
        Assert.Equal("Paris", a.Locality);
        Assert.Equal("8e", a.DependentLocality);
        Assert.Equal("75008", a.PostalCode);
        Assert.Equal("CEDEX 08", a.SortingCode);
        Assert.Equal("Acme", a.Organization);
        Assert.Equal("Pat", a.Recipient);
        Assert.Equal(["92 avenue des Champs-Élysées", "Bât. A"], a.StreetLines);
        Assert.Equal("fr", a.Language);
    }
}
