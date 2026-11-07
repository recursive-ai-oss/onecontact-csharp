using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact.Tests;

// The phone-number finder scans for candidates by hand; the pattern it replaced stays in the code as the reference, and these tests hold the
// two to the same answers on random text over the characters that matter (digits of several scripts, brackets, plus signs, separators,
// extension labels).
public class PhoneCandidateScannerTests
{
    private static readonly string[] Pieces =
    [
        "0", "5", "9", "12", "2024561111", "\u0661\u0662", "\uFF11\uFF12", "\U0001D7CE", "a", "e", "x", "X", "xx", "ext", "ext.", "t", "(", ")", "[", "]",
        "+", "\uFF0B", "\uFF08", "\uFF09", "\uFF3B", "-", "\u2013", "\u2212", "\u30FC", "\uFF0D", "/", ".", ",", ";", "#", "~", "\uFF5E", " ", "  ", "\u00A0", "\u3000",
        "\t", "\n", "\u200B", ":", "\u00E9", "\uD83D\uDE00", "\uD83D", "\uDE00", "int", "\u0434\u043E\u0431", "anexo", "ext=",
    ];

    [Fact]
    public void The_scan_finds_the_same_candidates_as_the_pattern_on_random_text()
    {
        var rng = new Random(20261107);
        int found = 0;
        for (int i = 0; i < 400_000; i++)
        {
            int pieces = rng.Next(0, 24);
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < pieces; k++)
            {
                sb.Append(Pieces[rng.Next(Pieces.Length)]);
            }

            string text = sb.ToString();
            int from = text.Length == 0 ? 0 : rng.Next(0, text.Length + 1);
            bool expected = PhoneNumberMatcher.Candidate.Find(text, from, default, out int es, out int ee);
            bool actual = PhoneCandidateScanner.Find(text, from, out int s, out int e);
            Assert.True(expected == actual && (!expected || (es == s && ee == e)), $"text {Escape(text)} from {from}: pattern {expected} [{es},{ee}) scan {actual} [{s},{e})");
            found += expected ? 1 : 0;
        }

        Assert.True(found > 100_000);
    }

    [Fact]
    public void The_scan_follows_the_pattern_on_long_digit_runs()
    {
        foreach (int digits in new[] { 19, 20, 21, 40, 41, 419, 420, 421, 430, 1000 })
        {
            foreach (string glue in new[] { string.Empty, "-", "  ", "     ", ")" })
            {
                string text = "a " + string.Join(glue, Enumerable.Repeat(new string('7', 3), digits / 3 + 1)) + new string('8', digits % 3) + " x12 b";
                bool expected = PhoneNumberMatcher.Candidate.Find(text, 0, default, out int es, out int ee);
                bool actual = PhoneCandidateScanner.Find(text, 0, out int s, out int e);
                Assert.True(expected == actual && es == s && ee == e, $"{digits} digits, glue '{glue}': pattern [{es},{ee}) scan [{s},{e})");
            }
        }
    }

    private static string Escape(string s) => string.Concat(s.Select(c => c is >= ' ' and < '\u007F' ? c.ToString() : $"\\u{(int)c:X4}"));
}
