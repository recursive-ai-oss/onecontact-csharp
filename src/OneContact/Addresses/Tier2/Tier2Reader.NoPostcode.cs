using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Addresses.Tier2;

// Last lines with no postcode, which web pages of Ireland and Italy often have: Irish text ends in a post town, a postal district ("Dublin 8") or a county
// (UPU Ireland sheet: "CO. CORK"), Italian text in the locality and the province code (UPU Italy sheet: "ROMA RM"). They are read only when the country is given,
// and the line before must be a delivery line, so a bare name is never taken for a locality on its own.
internal static partial class Tier2Reader
{
    private sealed partial class Reader
    {
        // A province code written after a comma on its own ("80134 Napoli, NA") belongs to the postcode line before it.
        private static void MergeTrailingProvince(List<Unit> units, string? known)
        {
            if (known is not ("IT" or null) || units.Count < 2)
            {
                return;
            }

            List<Tok> last = units[^1].Toks;
            if (last.Count != 1 || ItProvinceCode(last[0]) is null)
            {
                return;
            }

            List<Tok> prev = units[^2].Toks;
            int pc = prev.FindIndex(t => t.Text.Length == 5 && t.Text.All(char.IsAsciiDigit));
            if (pc < 0 || pc >= prev.Count - 1)
            {
                return;
            }

            prev.Add(last[0]);
            units.RemoveAt(units.Count - 1);
        }

        // A Portuguese postcode (four digits, a dash, three digits) or an Andorran one (AD and three digits) is the end of the address even with no locality after it:
        // "Rua das Flores 25, 1200-195". Both forms belong to one country only.
        private static bool FindPostcodeAlone(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (known is not (null or "PT" or "AD"))
            {
                return false;
            }

            int u = units.Count - 1;
            List<Tok> toks = units[u].Toks;
            PostcodeMatch? m = MatchAt(toks, toks.Count - 1, known);
            if (m is null || m.Length != 1 || m.Candidates is not (["PT"] or ["AD"]) || units.Count < 2 && toks.Count < 2)
            {
                return false;
            }

            unitIndex = u;
            tokenIndex = toks.Count - 1;
            match = m;
            return true;
        }

        private bool FindNoPostcode(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (units.Count < 2 || known is not ("IE" or "IT"))
            {
                return false;
            }

            int u = units.Count - 1;
            List<Tok> last = units[u].Toks;
            T2Country country = T2Rules.Countries[known];
            if (known == "IT")
            {
                // "Via Toledo 100, Napoli NA": the locality, then the province code.
                if (last.Count < 2 || last.Count > 7 || ItProvinceCode(last[^1]) is null || last.Take(last.Count - 1).Any(t => t.Text.Any(char.IsAsciiDigit)) || !IsDeliveryLine(units[u - 1], country))
                {
                    return false;
                }

                match = new PostcodeMatch(0, string.Empty, ["IT"], null);
                unitIndex = u;
                return true;
            }

            // Ireland, with the country given: the evidence for the country is the hint, so a bare name is never read as a post town.
            if (Inferring)
            {
                return false;
            }

            List<Tok>? county = null;
            int co = last.FindIndex(IsCountyWord);
            if (co > 0 && (co < last.Count - 1 || IsGluedCounty(last[co])) && HasDeliveryBefore(units, u, country))
            {
                // "Dundalk Co. Louth"
                county = last.GetRange(co, last.Count - co);
                units[u] = new Unit(last.GetRange(0, co));
            }
            else if (u >= 2 && (IsCountyWord(last[0]) && (last.Count > 1 || IsGluedCounty(last[0])) || IsBareCounty("IE", last)) && PlaceFits(units[u - 1].Toks, true, "IE"))
            {
                // "Dundalk, Co. Louth", "Ballincollig, Cork"
                county = last;
                units.RemoveAt(u);
                u--;
            }
            else if (!HasDeliveryBefore(units, u, country) || !PlaceFits(last, true, "IE") || IsBareCounty("IE", last)
                || last.Any(t => t.Text.Any(char.IsAsciiDigit)) && !IsPostalDistrict(last))
            {
                return false;
            }

            _county = county;
            match = new PostcodeMatch(0, string.Empty, ["IE"], null);
            unitIndex = u;
            return true;
        }

        // "Dublin 8", "Dublin 6W": the postal districts of Dublin (UPU Ireland sheet: "DUBLIN 24").
        private static bool IsPostalDistrict(List<Tok> toks) =>
            toks.Count == 2 && toks[0].Key == "DUBLIN" && toks[1].Text.Length <= 3 && toks[1].Text.TrimEnd('W', 'w').All(char.IsAsciiDigit) && toks[1].Text.Length > 0;

        // Whether a delivery line or a unit line ("Unit 4") stands before the unit at <paramref name="u"/>.
        private bool HasDeliveryBefore(List<Unit> units, int u, T2Country country)
        {
            for (int i = 0; i < u; i++)
            {
                if (!units[i].Leading && ParseDeliveryLine(units[i].Toks, country.Code, country, allowStreet: true) is { Kind: StreetLineKind.Street or StreetLineKind.PostOfficeBox or StreetLineKind.SecondaryOnly })
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsDeliveryLine(Unit unit, T2Country country) =>
            !unit.Leading && ParseDeliveryLine(unit.Toks, country.Code, country, allowStreet: true) is { Kind: StreetLineKind.Street or StreetLineKind.PostOfficeBox };
    }
}
