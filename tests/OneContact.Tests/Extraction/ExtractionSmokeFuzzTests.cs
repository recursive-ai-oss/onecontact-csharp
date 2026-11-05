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
    ];

    [Fact]
    public void Mutated_pages_never_throw_and_keep_their_offsets_inside_the_input()
    {
        double seconds = double.TryParse(Environment.GetEnvironmentVariable("ONECONTACT_FUZZ_SECONDS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : 3;
        List<byte[]> seeds = [.. ExtractionFixtures.PageFiles().Where(f => ExtractionFixtures.EncodingOf(f).CodePage == 65001).Select(File.ReadAllBytes)];
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
