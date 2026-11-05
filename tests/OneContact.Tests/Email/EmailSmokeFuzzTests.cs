using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace RecursiveAI.OneContact.Tests;

/// <summary>
/// A seeded mutation smoke run of the email parser: corpus inputs are mutated with hostile characters and every result is checked for
/// the invariants that must always hold. It runs for a few seconds by default; set ONECONTACT_FUZZ_SECONDS for a longer run.
/// </summary>
public sealed class EmailSmokeFuzzTests
{
    private const string Hostile = "\"\\()<>[]@.,:;\r\n\t \0\u0001\u007f\u00e9\u3002\u200d\u05d0-_+=?!/%{}|^`~'*#$&";

    [Fact]
    public void Mutated_corpus_inputs_never_throw_and_keep_their_invariants()
    {
        double seconds = double.TryParse(Environment.GetEnvironmentVariable("ONECONTACT_FUZZ_SECONDS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : 3;
        string[] seeds =
        [
            "\"J. Smith\" <j.smith@example.com>", "a@b.example", "Pete(A nice \\) chap) <pete(his account)@silly.test(his host)>", "a@[IPv6:2001:db8::1]",
            "\u7528\u6237@\u4f8b\u5b50.\u6d4b\u8bd5", "=?utf-8?q?J=C3=BCrgen?= <j@example.com>", "<@a.example,@b.example:u@example.com>", "a@xn--bcher-kva.example",
        ];
        Random random = new(20261105);
        Stopwatch clock = Stopwatch.StartNew();
        long runs = 0;
        long accepted = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            StringBuilder sb = new(seeds[random.Next(seeds.Length)]);
            int edits = random.Next(1, 6);
            for (int i = 0; i < edits; i++)
            {
                int at = random.Next(sb.Length + 1);
                switch (random.Next(3))
                {
                    case 0:
                        sb.Insert(at, Hostile[random.Next(Hostile.Length)]);
                        break;
                    case 1 when sb.Length > 0:
                        sb.Remove(Math.Min(at, sb.Length - 1), 1);
                        break;
                    default:
                        if (sb.Length > 0)
                        {
                            sb[Math.Min(at, sb.Length - 1)] = random.Next(8) == 0 ? (char)random.Next(0xD800, 0xE000) : Hostile[random.Next(Hostile.Length)];
                        }

                        break;
                }
            }

            string input = sb.ToString();
            OperationStatus status = Email.EmailParser.TryParse(input, out EmailAddress value, out ParseDetail detail);
            runs++;
            if (status != OperationStatus.Done)
            {
                Assert.False(detail.PrimaryReason.IsNone, $"no reason for failure of '{input}'");
                continue;
            }

            accepted++;
            Assert.True(value.Source.Start >= 0 && value.Source.End <= input.Length, $"source out of range for '{input}'");
            foreach (ParsedField f in detail.Fields)
            {
                Assert.True(f.Span.Start >= 0 && f.Span.End <= input.Length, $"{f.Name} out of range for '{input}'");
            }

            Assert.True(EmailAddress.TryParse(value.Canonical, out EmailAddress again) && again.Canonical == value.Canonical, $"canonical not stable for '{input}'");
            Assert.True(!string.IsNullOrEmpty(value.Key), $"empty key for '{input}'");
        }

        Assert.True(runs > 0);
        Assert.True(accepted > 0, "the mutations never produced a valid address; the run proves nothing");
        string? dir = Environment.GetEnvironmentVariable("ONECONTACT_TEST_SUMMARY");
        if (!string.IsNullOrEmpty(dir))
        {
            File.AppendAllText(Path.Combine(dir, "email-conformance.txt"), $"smoke-fuzz: {runs} executions in {clock.Elapsed.TotalSeconds:F0}s, {accepted} accepted, 0 failures\n");
        }
    }
}
