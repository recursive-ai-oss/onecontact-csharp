using RecursiveAI.OneContact.Addresses.Standardization;

namespace RecursiveAI.OneContact.Addresses.Tier2;

// Pages label an address ("Cím: 1053 Budapest, ...", "Adres: ul. ...", "Sitz: ...") and sometimes put it in brackets. The label says what the line is and is no part of the
// address, so it is left out of every field and of the span of the address; a colon that does not follow a label stays where it is. A listed label is dropped at the start of
// any line; at the start of the text a single word ending in a colon is, because a text that opens with "Word:" opens with a label.
internal static partial class Tier2Reader
{
    private static readonly Lazy<HashSet<string>> AddressLabels = new(() => [.. RuleFile.Read("t2-address-labels.tsv").Select(r => r[0])]);

    private sealed partial class Reader
    {
        private static void DropAddressLabels(List<Unit> units)
        {
            for (int n = 0; n < units.Count; n++)
            {
                Unit u = units[n];

                // The label is one to three words, the last of which carries the colon: "Levelezési cím:" is two tokens, "Cím:" one.
                for (int words = Math.Min(3, u.Toks.Count); words >= 1; words--)
                {
                    if (!u.Toks[words - 1].Text.EndsWith(':') || u.Toks.Take(words - 1).Any(t => t.Text.EndsWith(':')))
                    {
                        continue;
                    }

                    string key = T2Rules.Key(string.Join(' ', u.Toks.Take(words).Select(t => t.Text.TrimEnd(':'))));
                    bool plainWords = u.Toks.Take(words).All(t => t.Text.TrimEnd(':').All(c => char.IsLetter(c) || c == '-'));
                    if (AddressLabels.Value.Contains(key) || (n == 0 && words == 1 && plainWords))
                    {
                        u.Toks = [.. u.Toks.Skip(words)];
                        break;
                    }
                }
            }

            units.RemoveAll(u => u.Toks.Count == 0);
        }

        // "(1053 Budapest, Kossuth Lajos utca 5.)": brackets around the whole address are no part of it. Offsets stay those of the original text; only the bracket characters go.
        private static void DropOuterBrackets(List<Unit> units)
        {
            if (units.Count == 0 || units[0].Toks.Count == 0 || units[^1].Toks.Count == 0)
            {
                return;
            }

            Tok first = units[0].Toks[0];
            Tok last = units[^1].Toks[^1];
            if (!(first.Text.Length > 1 && last.Text.Length > 1 && ((first.Text[0] == '(' && last.Text[^1] == ')') || (first.Text[0] == '[' && last.Text[^1] == ']'))))
            {
                return;
            }

            char open = first.Text[0];
            char close = last.Text[^1];
            int opens = units.Sum(u => u.Toks.Sum(t => t.Text.Count(c => c == open)));
            int closes = units.Sum(u => u.Toks.Sum(t => t.Text.Count(c => c == close)));
            if (opens != 1 || closes != 1)
            {
                return;
            }

            units[0].Toks[0] = new Tok(first.Text[1..], first.Start + 1);
            Tok tail = units[^1].Toks[^1];
            units[^1].Toks[^1] = new Tok(tail.Text[..^1], tail.Start);
        }
    }
}
