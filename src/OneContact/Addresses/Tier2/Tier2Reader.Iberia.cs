namespace RecursiveAI.OneContact.Addresses.Tier2;

// Forms of Spanish, Portuguese, Italian, Sammarinese, Maltese and Andorran addresses as they are written on web pages, read from the posts' guides:
// CTT, Manual de Endereçamento (2014) asks that "Nº", "N." or "Nr" before the door number be left out; Correos and the UPU sheets write the street type in front
// of the name ("C/", "CL."), and the Catalan floor and door words are used in Andorra and Catalonia (UPU Andorra sheet).
internal static partial class Tier2Reader
{
    private sealed partial class Reader
    {
        // "Calle Mayor, nº 5", "Rua das Flores n.º 25", "Calle Mayor nº5", "CL.MAYOR 47": the number marker is left out, and a type glued to the name by its period is split.
        private static void DropNumberMarkers(List<Unit> units, T2Country country)
        {
            foreach (Unit unit in units)
            {
                List<Tok> toks = unit.Toks;
                List<Tok> result = new(toks.Count + 1);
                for (int i = 0; i < toks.Count; i++)
                {
                    Tok t = toks[i];
                    string low = t.Text.ToLowerInvariant();
                    int markerLength = low.StartsWith("n.º", StringComparison.Ordinal) || low.StartsWith("n.°", StringComparison.Ordinal) ? 3
                        : low.StartsWith("nº", StringComparison.Ordinal) || low.StartsWith("n°", StringComparison.Ordinal) ? 2 : 0;
                    if (markerLength > 0 && markerLength < t.Text.Length && char.IsAsciiDigit(t.Text[markerLength]))
                    {
                        result.Add(new Tok(t.Text[markerLength..], t.Start + markerLength));
                        continue;
                    }

                    if (IsNumberMarker(t, country) && i + 1 < toks.Count && IsNumberToken(toks[i + 1].Text))
                    {
                        continue;
                    }

                    int dot = t.Text.IndexOf('.', StringComparison.Ordinal);
                    if (dot is >= 1 and <= 5 && dot < t.Text.Length - 2 && t.Text[..dot].All(char.IsLetter) && char.IsLetter(t.Text[dot + 1])
                        && T2Rules.FindType(country, T2Rules.Key(t.Text[..dot])) is not null && T2Rules.FindType(country, T2Rules.Key(t.Text)) is null)
                    {
                        result.Add(new Tok(t.Text[..(dot + 1)], t.Start));
                        result.Add(new Tok(t.Text[(dot + 1)..], t.Start + dot + 1));
                        continue;
                    }

                    result.Add(t);
                }

                unit.Toks = result;
            }
        }

        private static bool IsNumberMarker(Tok t, T2Country country)
        {
            string low = t.Text.ToLowerInvariant().Replace(".", string.Empty, StringComparison.Ordinal);
            return low is "nº" or "n°" or "nr" or "nro" or "num" or "núm" or "numero" or "número" or "n" || country.Profile == "mt" && low == "no";
        }

        // Catalan ordinals of a floor and a door: 1r, 2n, 3r, 4t, 5è; 1a, 2a, 3a.
        private static bool IsCatalanOrdinal(string t) =>
            t.Length is 2 or 3 && char.IsAsciiDigit(t[0]) && (t.Length == 2 || char.IsAsciiDigit(t[1])) && t[^1] is 'r' or 'n' or 't' or 'a' or 'è';
    }
}
