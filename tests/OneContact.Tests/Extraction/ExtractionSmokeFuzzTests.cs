using System.Diagnostics;
using System.Text;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Tests;

/// <summary>
/// A seeded mutation smoke run of the markup readers, over the fixture pages: bytes are flipped, cut, repeated and spliced, with
/// pieces of JSON-LD and microdata syntax thrown in. Nothing may throw, and every offset must lie inside the input. It runs for a few
/// seconds by default; set ONECONTACT_FUZZ_SECONDS for a longer run.
/// </summary>
public sealed class ExtractionSmokeFuzzTests
{
    private static readonly string[] Pieces =
    [
        "<script type=\"application/ld+json\">", "</script>", "{\"@type\":\"Person\",\"name\":\"", "\"worksFor\":{\"@id\":\"#a\"}", "\"@graph\":[", "\\u00",
        "itemscope ", "itemprop=\"name\" ", "itemtype=\"https://schema.org/Organization\" ", "vocab=\"https://schema.org/\" ", "typeof=\"Person\" ", "property=\"email\" ",
        "class=\"h-card p-org\" ", "class=\"vcard fn org\" ", "<address>", "</address>", "<a href=\"mailto:", "<a href=\"tel:", "&#", "&amp", "<br>", "hidden ", "style=\"display:none\" ",
        "\r\n", "\0", "\u00e9", "\uFFFD",
        " [at] ", " (dot) ", " at ", " dot ", "@", "[.]", "%40", "&#64;", "\u200b", "<span>@</span>", "<bdo dir=\"rtl\">", "style=\"direction:rtl;unicode-bidi:bidi-override\" ",
        "data-cfemail=\"4b", "/cdn-cgi/l/email-protection#", "style=\"visibility:hidden\" ", "style=\"position:absolute;left:-9999px\" ", "<style>p{display:none}</style>",
        "Tel: +1 (202) 555-0100 ext. 12", "100 Main Street<br>Springfield, IL 62701", "Toronto, ON M5V 2T6", "PO Box 12, Madison, WI 53703", "www.example.com/x", "Name: Jo Example",
    ];

    [Fact]
    public void Mutated_pages_never_throw_and_keep_their_offsets_inside_the_input()
    {
        double seconds = double.TryParse(Environment.GetEnvironmentVariable("ONECONTACT_FUZZ_SECONDS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : 3;
        List<byte[]> seeds =
        [
            .. ExtractionFixtures.PageFiles().Where(f => ExtractionFixtures.EncodingOf(f).CodePage == 65001).Select(File.ReadAllBytes),
            .. ExtractionFixtures.TextPageFiles().Where(f => ExtractionFixtures.EncodingOf(f).CodePage == 65001).Select(File.ReadAllBytes),
        ];
        var random = new Random(20261105);
        var extractor = new ContactExtractor();
        Stopwatch clock = Stopwatch.StartNew();
        long runs = 0;
        long fields = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            byte[] input = Mutate(seeds, random);
            ExtractResult result;
            try
            {
                result = extractor.ExtractHtml(input);
            }
            catch (Exception e)
            {
                string repro = Path.Combine(Path.GetTempPath(), $"extraction-finding-{runs}.html");
                File.WriteAllBytes(repro, input);
                throw new InvalidOperationException($"{e.GetType().Name}: {e.Message} (input saved to {repro})", e);
            }

            foreach (ExtractedField f in result.Fields.SelectMany(f => f.Duplicates.Prepend(f)))
            {
                Assert.True(!f.HasSpan || (f.Span.Start >= 0 && f.Span.Length >= 0 && f.Span.End <= input.Length), "a span lies outside the input");
                Assert.NotNull(f.Raw);
                fields++;
            }

            foreach (ContactRecord r in result.Records)
            {
                Assert.True(!r.HasSpan || (r.Span.Start >= 0 && r.Span.End <= input.Length));
            }

            _ = ExtractionFixtures.Dump(result);
            runs++;
        }

        Assert.True(runs > 0);
        Console.WriteLine($"extraction smoke fuzz: {runs} executions, {fields} fields, {seconds} s");
    }

    // The same for plain strings: mutated notes never throw, and every span is a real slice of the string.
    [Fact]
    public void Mutated_notes_never_throw_and_keep_their_offsets_inside_the_string()
    {
        double seconds = double.TryParse(Environment.GetEnvironmentVariable("ONECONTACT_FUZZ_SECONDS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s) ? s / 4 : 1;
        List<string> seeds = [.. ExtractionFixtures.NoteFiles().Select(f => new UTF8Encoding(false).GetString(File.ReadAllBytes(f)))];
        var random = new Random(20261106);
        var extractor = new ContactExtractor { DefaultRegion = Region.US };
        Stopwatch clock = Stopwatch.StartNew();
        long runs = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            string input = seeds[random.Next(seeds.Count)];
            for (int i = random.Next(1, 6); i > 0 && input.Length > 0; i--)
            {
                int at = random.Next(input.Length);
                input = random.Next(4) switch
                {
                    0 => input[..at] + (char)random.Next(0x10000) + input[(at + 1)..],
                    1 => input[..at],
                    2 => input[..at] + Pieces[random.Next(Pieces.Length)] + input[at..],
                    _ => input[..at] + seeds[random.Next(seeds.Count)] + input[at..],
                };
            }

            ExtractResult result = extractor.ExtractText(input);
            foreach (ExtractedField f in result.Fields.SelectMany(f => f.Duplicates.Prepend(f)))
            {
                Assert.True(!f.HasSpan || (f.Span.Start >= 0 && f.Span.End <= input.Length && input.Substring(f.Span.Start, f.Span.Length) == f.Raw), "a span is not the raw text");
            }

            runs++;
        }

        Assert.True(runs > 0);
        Console.WriteLine($"extraction note fuzz: {runs} executions, {seconds} s");
    }

    private static byte[] Mutate(List<byte[]> seeds, Random random)
    {
        byte[] input = [.. seeds[random.Next(seeds.Count)]];
        int edits = random.Next(1, 6);
        for (int i = 0; i < edits && input.Length > 0; i++)
        {
            int at = random.Next(input.Length);
            switch (random.Next(5))
            {
                case 0:
                    input[at] = (byte)random.Next(256);
                    break;
                case 1:
                    input = input[..at];
                    break;
                case 2:
                    byte[] piece = Encoding.UTF8.GetBytes(Pieces[random.Next(Pieces.Length)]);
                    input = [.. input[..at], .. piece, .. input[at..]];
                    break;
                case 3:
                    byte[] other = seeds[random.Next(seeds.Count)];
                    input = [.. input[..at], .. other[random.Next(other.Length)..]];
                    break;
                default:
                    input = [.. input[..at], .. input[at..].Take(random.Next(1, 60)), .. input[at..]];
                    break;
            }
        }

        return input;
    }
}
