using RecursiveAI.OneContact.Addresses.Standardization;

namespace RecursiveAI.OneContact.Addresses.Tier2;

// Pages label an address ("Cím: 1053 Budapest, ...", "Adres: ul. ...", "Sitz: ..."). The label says what the line is and is no part of the address, so it is left out of every
// field and of the span of the address; a colon that does not follow a listed label stays where it is.
internal static partial class Tier2Reader
{
    private static readonly Lazy<HashSet<string>> AddressLabels = new(() => [.. RuleFile.Read("t2-address-labels.tsv").Select(r => r[0])]);

    private sealed partial class Reader
    {
        private static void DropAddressLabels(List<Unit> units)
        {
            foreach (Unit u in units)
            {
                // The label is one to three words, the last of which carries the colon: "Levelezési cím:" is two tokens, "Cím:" one.
                for (int words = Math.Min(3, u.Toks.Count); words >= 1; words--)
                {
                    if (!u.Toks[words - 1].Text.EndsWith(':') || u.Toks.Take(words - 1).Any(t => t.Text.EndsWith(':')))
                    {
                        continue;
                    }

                    string key = T2Rules.Key(string.Join(' ', u.Toks.Take(words).Select(t => t.Text.TrimEnd(':'))));
                    if (AddressLabels.Value.Contains(key))
                    {
                        u.Toks = [.. u.Toks.Skip(words)];
                        break;
                    }
                }
            }

            units.RemoveAll(u => u.Toks.Count == 0);
        }
    }
}
