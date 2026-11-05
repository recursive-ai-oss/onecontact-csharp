using System.Text;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Tests;

public sealed class ExtractionFixtureTests
{
    [Fact]
    public void The_fixture_set_has_at_least_sixty_pages()
    {
        Assert.True(ExtractionFixtures.PageFiles().Count() >= 60);
    }

    [Fact]
    public void Every_page_gives_its_golden_records()
    {
        string? update = Environment.GetEnvironmentVariable("ONECONTACT_UPDATE_GOLDEN");
        var failures = new List<string>();
        foreach (string file in ExtractionFixtures.PageFiles())
        {
            string name = ExtractionFixtures.NameOf(file);
            ExtractResult result = new ContactExtractor { Sources = ExtractionSources.Markup }.ExtractHtml(File.ReadAllBytes(file));
            string actual = ExtractionFixtures.Dump(result);
            if (!string.IsNullOrEmpty(update))
            {
                Directory.CreateDirectory(update);
                File.WriteAllText(Path.Combine(update, name + ".txt"), actual, new UTF8Encoding(false));
                continue;
            }

            string golden = Path.Combine(ExtractionFixtures.Root, "golden", name + ".txt");
            if (!File.Exists(golden))
            {
                failures.Add(name + ": no golden file");
                continue;
            }

            if (File.ReadAllText(golden) != actual)
            {
                failures.Add(name + ": differs from its golden file");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Every_golden_file_has_a_page()
    {
        string[] pages = [.. ExtractionFixtures.PageFiles().Select(ExtractionFixtures.NameOf)];
        foreach (string golden in Directory.EnumerateFiles(Path.Combine(ExtractionFixtures.Root, "golden")))
        {
            Assert.Contains(Path.GetFileNameWithoutExtension(golden), pages);
        }
    }

    // Done when 2: the bytes at a field's offsets are its raw value, for UTF-8 with multibyte text, windows-1252 and UTF-16 pages.
    [Fact]
    public void Offsets_name_the_raw_value_in_the_original_bytes()
    {
        int checkedFields = 0;
        foreach (string file in ExtractionFixtures.PageFiles())
        {
            byte[] bytes = File.ReadAllBytes(file);
            Encoding encoding = ExtractionFixtures.EncodingOf(file);
            ExtractResult result = new ContactExtractor { Sources = ExtractionSources.Markup }.ExtractHtml(bytes);
            Assert.Equal(OffsetUnit.Bytes, result.OffsetUnit);
            foreach (ExtractedField f in AllFields(result))
            {
                if (!f.HasSpan)
                {
                    continue;
                }

                Assert.True(f.Span.Start >= 0 && f.Span.End <= bytes.Length, $"{file}: span outside the input");
                // HTML parsing turns CR LF and CR into LF before anything is read; the raw value is in that form.
                string at = encoding.GetString(bytes, f.Span.Start, f.Span.Length).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                Assert.True(at == f.Raw, $"{ExtractionFixtures.NameOf(file)}: bytes at {f.Span.Start}+{f.Span.Length} are \"{at}\", raw value is \"{f.Raw}\"");
                checkedFields++;
            }

            foreach (ContactRecord r in result.Records.Where(r => r.HasSpan))
            {
                Assert.True(r.Span.Start >= 0 && r.Span.End <= bytes.Length);
            }
        }

        Assert.True(checkedFields > 150, $"only {checkedFields} fields were checked");
    }

    [Fact]
    public void Multibyte_pages_have_byte_offsets_that_differ_from_char_offsets()
    {
        string file = ExtractionFixtures.PageFiles().Single(f => ExtractionFixtures.NameOf(f) == "encoding-utf8-multibyte");
        byte[] bytes = File.ReadAllBytes(file);
        ExtractResult result = new ContactExtractor { Sources = ExtractionSources.Markup }.ExtractHtml(bytes);
        ExtractedField zoe = result.Fields.Single(f => f.Raw.StartsWith("Zo", StringComparison.Ordinal));
        string text = Encoding.UTF8.GetString(bytes);
        int charOffset = text.IndexOf("Zo\u00eb", StringComparison.Ordinal);
        Assert.True(zoe.Span.Start > charOffset, "the byte offset is past the char offset because earlier text is multibyte");
        Assert.Equal(zoe.Raw, Encoding.UTF8.GetString(bytes, zoe.Span.Start, zoe.Span.Length));
    }

    [Fact]
    public void A_byte_order_mark_is_inside_the_byte_offsets()
    {
        string file = ExtractionFixtures.PageFiles().Single(f => ExtractionFixtures.NameOf(f) == "encoding-utf8-bom");
        byte[] bytes = File.ReadAllBytes(file);
        ExtractedField name = new ContactExtractor { Sources = ExtractionSources.Markup }.ExtractHtml(bytes).Records.Single().Name!;
        Assert.Equal("B\u00f6mb Name", Encoding.UTF8.GetString(bytes, name.Span.Start, name.Span.Length));
    }

    // For a string the offsets are chars of the string as given, including its carriage returns.
    [Fact]
    public void String_input_has_char_offsets_into_the_original_string_even_with_crlf()
    {
        foreach (string file in ExtractionFixtures.PageFiles())
        {
            if (ExtractionFixtures.NameOf(file).EndsWith(".utf16le", StringComparison.Ordinal) || ExtractionFixtures.NameOf(file).EndsWith(".cp1252", StringComparison.Ordinal))
            {
                continue;
            }

            string html = new UTF8Encoding(false).GetString(File.ReadAllBytes(file)).TrimStart('\uFEFF');
            ExtractResult result = new ContactExtractor { Sources = ExtractionSources.Markup }.ExtractHtml(html);
            Assert.Equal(OffsetUnit.Chars, result.OffsetUnit);
            foreach (ExtractedField f in AllFields(result).Where(f => f.HasSpan))
            {
                string at = html.Substring(f.Span.Start, f.Span.Length).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                Assert.True(at == f.Raw, $"{ExtractionFixtures.NameOf(file)}: chars at {f.Span.Start}+{f.Span.Length} are \"{at}\", raw value is \"{f.Raw}\"");
            }
        }
    }

    [Fact]
    public void Bytes_and_string_inputs_agree_on_values()
    {
        foreach (string file in ExtractionFixtures.PageFiles().Where(f => ExtractionFixtures.EncodingOf(f).CodePage == 65001))
        {
            byte[] bytes = File.ReadAllBytes(file);
            string html = new UTF8Encoding(false).GetString(bytes).TrimStart('\uFEFF');
            string[] fromBytes = [.. AllFields(new ContactExtractor { Sources = ExtractionSources.Markup }.ExtractHtml(bytes)).Select(f => f.Kind + "|" + f.Text)];
            string[] fromString = [.. AllFields(new ContactExtractor { Sources = ExtractionSources.Markup }.ExtractHtml(html)).Select(f => f.Kind + "|" + f.Text)];
            Assert.Equal(fromBytes, fromString);
        }
    }

    internal static IEnumerable<ExtractedField> AllFields(ExtractResult result)
    {
        foreach (ExtractedField f in result.Fields)
        {
            yield return f;
            foreach (ExtractedField d in f.Duplicates)
            {
                yield return d;
            }
        }
    }
}
