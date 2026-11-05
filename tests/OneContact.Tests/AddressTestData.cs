using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RecursiveAI.OneContact.Addresses.Structural;
using RecursiveAI.OneContact.Data;
using RecursiveAI.OneContact.Ingest;
using RecursiveAI.OneContact.Ingest.LibAddressInput;

namespace RecursiveAI.OneContact.Tests;

/// <summary>Builds address data sets for tests by running the real ingest tool, over the pinned file or over a variant of it.</summary>
internal static class AddressTestData
{
    /// <summary>The pinned upstream data file, fetched by tools/fetch/libaddressinput.sh into ONECONTACT_UPSTREAM (or .upstream at the repository root).</summary>
    internal static string PinnedFile()
    {
        string? root = Environment.GetEnvironmentVariable("ONECONTACT_UPSTREAM");
        if (!string.IsNullOrEmpty(root))
        {
            return Path.Combine(root, "libaddressinput", "testdata", "countryinfo.txt");
        }

        for (DirectoryInfo? dir = new(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, ".upstream", "libaddressinput", "testdata", "countryinfo.txt");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("The pinned address data is not fetched: run tools/fetch/libaddressinput.sh.");
    }

    /// <summary>Runs the ingest tool over a data file's text and loads the tables it writes.</summary>
    internal static AddressDataSet Compile(string countryInfoText, string workDirectory, out byte[] image)
    {
        string files = Path.Combine(workDirectory, "files");
        Directory.CreateDirectory(Path.Combine(files, "testdata"));
        byte[] bytes = new UTF8Encoding(false).GetBytes(countryInfoText);
        File.WriteAllBytes(Path.Combine(files, "testdata", "countryinfo.txt"), bytes);
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        PinFile pin = PinFile.Parse(
        [
            "source libaddressinput",
            "licence Apache-2.0",
            "repo https://example.invalid/test",
            "ref test",
            "urlbase https://example.invalid/test/",
            $"file testdata/countryinfo.txt {hash}",
        ]);
        IngestOutput output = IngestRunner.Run(pin, files, Path.Combine(workDirectory, "out"), new LibAddressInputSource());
        image = File.ReadAllBytes(output.TablesPath);
        CompiledTableFile file = CompiledTableFile.Read(image);
        return Load(file);
    }

    internal static AddressDataSet Load(CompiledTableFile file)
    {
        file.TryGetTable("props", out ReadOnlyMemory<byte> props);
        file.TryGetTable("strings", out ReadOnlyMemory<byte> strings);
        file.TryGetTable("nodes", out ReadOnlyMemory<byte> nodes);
        return AddressDataSet.Load(props, strings, nodes);
    }

    /// <summary>
    /// The pinned data with the layout, required-field and Latin-layout entries of every country replaced by a table of country
    /// entries (the one the reference implementation reports for its own built-in data). The reference takes those three entries
    /// from its built-in table and everything else (subdivisions, postal code patterns) from the data it is given, so this is the
    /// data it effectively reads.
    /// </summary>
    internal static string WithBuiltInLayouts(string countryInfoText, IReadOnlyDictionary<string, JsonElement> builtIn)
    {
        StringBuilder result = new();
        foreach (string line in countryInfoText.Split('\n'))
        {
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                result.Append(line).Append('\n');
                continue;
            }

            string key = line[..eq];
            string[] parts = key.Split('/');
            bool countryNode = key.StartsWith("data/", StringComparison.Ordinal) && parts.Length == 2;
            if (!countryNode)
            {
                result.Append(line).Append('\n');
                continue;
            }

            string code = parts[1];
            int variant = code.IndexOf("--", StringComparison.Ordinal);
            if (variant >= 0)
            {
                code = code[..variant];
            }

            JsonObject node = JsonNode.Parse(line[(eq + 1)..])!.AsObject();
            JsonElement source = builtIn.TryGetValue(code, out JsonElement e) ? e : builtIn["ZZ"];
            JsonElement defaults = builtIn["ZZ"];
            foreach (string property in new[] { "fmt", "lfmt", "require" })
            {
                node.Remove(property);
                if (source.TryGetProperty(property, out JsonElement value))
                {
                    node[property] = StripLandmarkCodes(value.GetString()!);
                }
                else if (property != "lfmt" && defaults.TryGetProperty(property, out JsonElement fallback))
                {
                    node[property] = StripLandmarkCodes(fallback.GetString()!);
                }
            }

            result.Append(key).Append('=').Append(node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })).Append('\n');
        }

        return result.ToString().TrimEnd('\n') + "\n";
    }

    // The reference's own table has landmark codes (%T, %F, %L) in a few formats; they place no field the library knows.
    private static string StripLandmarkCodes(string format) => format.Replace("%T", string.Empty, StringComparison.Ordinal).Replace("%F", string.Empty, StringComparison.Ordinal).Replace("%L", string.Empty, StringComparison.Ordinal);
}
