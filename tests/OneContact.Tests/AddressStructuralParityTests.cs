using System.IO.Compression;
using System.Text;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Structural;

namespace RecursiveAI.OneContact.Tests;

/// <summary>
/// Structural validation and layout against the reference implementation's recorded answers (testdata/addresses-structural,
/// produced by tools/Oracles/Addresses/make-reference.sh): every case must give the same problems per field, and the same label
/// lines when the library lays the address out in the format the reference reports.
/// </summary>
public sealed class AddressStructuralParityTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "testdata", "addresses-structural");

    private static readonly Dictionary<string, AddressField> Fields = new(StringComparer.Ordinal)
    {
        ["COUNTRY"] = AddressField.Country,
        ["ADMIN_AREA"] = AddressField.AdminArea,
        ["LOCALITY"] = AddressField.Locality,
        ["DEPENDENT_LOCALITY"] = AddressField.DependentLocality,
        ["POSTAL_CODE"] = AddressField.PostalCode,
        ["SORTING_CODE"] = AddressField.SortingCode,
        ["ORGANIZATION"] = AddressField.Organization,
        ["RECIPIENT"] = AddressField.Recipient,
        ["STREET_ADDRESS"] = AddressField.StreetAddress,
    };

    private static readonly Dictionary<string, AddressIssueKind> Kinds = new(StringComparer.Ordinal)
    {
        ["UNEXPECTED_FIELD"] = AddressIssueKind.UnexpectedField,
        ["MISSING_REQUIRED_FIELD"] = AddressIssueKind.MissingRequiredField,
        ["UNKNOWN_VALUE"] = AddressIssueKind.UnknownValue,
        ["INVALID_FORMAT"] = AddressIssueKind.InvalidFormat,
        ["MISMATCHING_VALUE"] = AddressIssueKind.MismatchingValue,
    };

    private static IEnumerable<string> ReadGz(string name)
    {
        using FileStream file = File.OpenRead(Path.Combine(Dir, name));
        using GZipStream gz = new(file, CompressionMode.Decompress);
        using StreamReader reader = new(gz, new UTF8Encoding(false));
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    private static string? Value(string text) => text == "\\N" ? null : text;

    private static AddressParts Parts(string[] f)
    {
        Region country = f[1] == "\\N" ? default : Region.Parse(f[1]);
        string[] lines = f[10] == "\\N" ? [] : f[10].Split('\u0001');
        return new AddressParts(country, Value(f[2]), Value(f[3]) ?? string.Empty, Value(f[4]) ?? string.Empty, Value(f[5]) ?? string.Empty, Value(f[6]) ?? string.Empty, Value(f[7]) ?? string.Empty, Value(f[8]) ?? string.Empty, Value(f[9]) ?? string.Empty, lines);
    }

    [Fact]
    public void Validation_and_layout_equal_the_reference_on_every_generated_case()
    {
        Dictionary<string, System.Text.Json.JsonElement> formats = [];
        foreach (string line in File.ReadAllLines(Path.Combine(Dir, "oracle-formats.tsv"), Encoding.UTF8))
        {
            int tab = line.IndexOf('\t', StringComparison.Ordinal);
            formats[line[..tab]] = System.Text.Json.JsonDocument.Parse(line[(tab + 1)..]).RootElement.Clone();
        }

        // The reference reads layouts and required fields from its own built-in table, so the library is given the pinned data
        // with those entries replaced by that table, compiled by the real ingest tool.
        string work = Path.Combine(Path.GetTempPath(), "onecontact-addr-" + Guid.NewGuid().ToString("N"));
        AddressDataSet data;
        try
        {
            string pinned = File.ReadAllText(AddressTestData.PinnedFile(), Encoding.UTF8);
            data = AddressTestData.Compile(AddressTestData.WithBuiltInLayouts(pinned, formats), work, out _);
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
        }

        List<string> cases = [.. ReadGz("cases.tsv.gz")];
        List<string> results = [.. ReadGz("oracle-results.tsv.gz")];
        Assert.Equal(cases.Count, results.Count);

        List<string> mismatches = [];
        int validationCompared = 0;
        int layoutCompared = 0;
        int layoutSkipped = 0;
        for (int i = 0; i < cases.Count; i++)
        {
            string[] c = cases[i].Split('\t');
            string[] r = results[i].Split('\t');
            Assert.Equal(c[0], r[0]);
            AddressParts parts = Parts(c);

            // Problems per field.
            HashSet<(AddressField, AddressIssueKind)> expected = [];
            if (r[1].Length > 0)
            {
                foreach (string item in r[1].Split(';'))
                {
                    int colon = item.IndexOf(':', StringComparison.Ordinal);
                    expected.Add((Fields[item[..colon]], Kinds[item[(colon + 1)..]]));
                }
            }

            HashSet<(AddressField, AddressIssueKind)> actual = [.. StructuralAddress.Validate(data, parts).Select(x => (x.Field, x.Kind))];
            validationCompared++;
            if (!expected.SetEquals(actual))
            {
                mismatches.Add($"{c[0]}\tproblems\texpected {Describe(expected)}\tgot {Describe(actual)}");
            }

            // Label lines in the reference's own format for the case.
            string? format = parts.Country.IsUnknown ? null : ReferenceFormat(formats, parts);
            if (format is null || format.Contains("%T", StringComparison.Ordinal) || format.Contains("%F", StringComparison.Ordinal) || format.Contains("%L", StringComparison.Ordinal))
            {
                layoutSkipped++;
                continue;
            }

            string[] expectedLines = r.Length > 2 && r[2].Length > 0 ? r[2].Split('\u0001') : [];
            string[] actualLines = [.. AddressLayoutRenderer.RenderLines(AddressFormat.Parse(format), parts)];
            layoutCompared++;
            if (!expectedLines.SequenceEqual(actualLines, StringComparer.Ordinal))
            {
                mismatches.Add($"{c[0]}\tlayout ({format})\texpected [{string.Join('|', expectedLines)}]\tgot [{string.Join('|', actualLines)}]");
            }
        }

        string report = Path.Combine(Path.GetTempPath(), "address-structural-mismatches.txt");
        File.WriteAllLines(report, mismatches);
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {cases.Count} cases differ (full list in {report}); first:\n{string.Join('\n', mismatches.Take(25))}");
        Assert.True(validationCompared > 25000);
        Assert.True(layoutCompared > 20000);
        _ = layoutSkipped;
    }

    private static string? ReferenceFormat(Dictionary<string, System.Text.Json.JsonElement> formats, AddressParts parts)
    {
        // The reference lays every region out by its own built-in table, not by the pinned data: its Latin layout is the table's
        // lfmt, or the default layout when the region has none (the library instead falls back to the region's own layout).
        string code = parts.Country.IsUnknown ? "ZZ" : parts.Country.Code;
        System.Text.Json.JsonElement region = formats.TryGetValue(code, out System.Text.Json.JsonElement e) ? e : formats["ZZ"];
        bool latin = ReferenceUsesLatin(parts.Language);
        string? text = null;
        if (latin && region.TryGetProperty("lfmt", out System.Text.Json.JsonElement l))
        {
            text = l.GetString();
        }
        else if (!latin && region.TryGetProperty("fmt", out System.Text.Json.JsonElement f))
        {
            text = f.GetString();
        }

        return text ?? formats["ZZ"].GetProperty("fmt").GetString();
    }

    // The reference recognizes only a two-part tag such as ja-Latn (either separator, any case) as Latin; the library reads any
    // language tag with a Latn script subtag, so zh-Hans-Latn is Latin too.
    private static bool ReferenceUsesLatin(string? language)
    {
        if (language is null)
        {
            return false;
        }

        string[] parts = language.Split('-', '_');
        return parts.Length == 2 && string.Equals(parts[1], "Latn", StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(IEnumerable<(AddressField Field, AddressIssueKind Kind)> items) =>
        "{" + string.Join(", ", items.OrderBy(x => x.Field).Select(x => $"{x.Field}:{x.Kind}")) + "}";
}
