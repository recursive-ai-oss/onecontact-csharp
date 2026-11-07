namespace OneContact.Data.Names;

/// <summary>Builds the language-and-country units of the Tier 2 person-name set from the open lists and from our own short lists where a country has no open list.</summary>
internal static class Tier2Units
{
    private static WeightedList<GivenName> GivenPool(params (NameSet Set, double Share)[] parts)
    {
        List<(GivenName, double)> e = [];
        foreach ((NameSet set, double share) in parts)
        {
            double total = set.Names.Sum(n => n.Count);
            foreach (NameCount n in set.Names)
            {
                e.Add((new GivenName(n.Name, n.Sex), n.Count / total * share));
            }
        }

        return new WeightedList<GivenName>(e, true);
    }

    private static WeightedList<string> FamilyPool(params (NameSet Set, double Share)[] parts)
    {
        // The same family name may come from two sources; each draw is independent, so the entries simply add up.
        List<(string, double)> e = [];
        foreach ((NameSet set, double share) in parts)
        {
            double total = set.Names.Sum(n => n.Count);
            foreach (NameCount n in set.Names)
            {
                e.Add((n.Name, n.Count / total * share));
            }
        }

        return new WeightedList<string>(e, true);
    }

    /// <summary>Our own short lists (templates/own/*.tsv: kind, language, value), one name each, no sex.</summary>
    private static NameSet Own(string templateDir, string kind, string lang, string id)
    {
        string code = lang == "nb" ? "no" : lang;
        SortedSet<string> names = new(StringComparer.Ordinal);
        long read = 0;
        foreach (string file in Directory.GetFiles(Path.Combine(templateDir, "own"), "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach (string line in File.ReadAllLines(file))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] c = line.Split('\t');
                if (c.Length >= 3 && c[1] == code && (c[0] == kind || (kind == "family" && c[0] == "surname")))
                {
                    read++;
                    names.Add(c[2]);
                }
            }
        }

        return new NameSet { Id = id, Names = names.Select(n => new NameCount(n, '-', 1)).ToList(), RowsRead = read };
    }

    internal static List<Tier2Unit> Build(string dataDir, string templateDir, List<NameSet> sets)
    {
        NameSet Reg(NameSet s)
        {
            sets.Add(s);
            return s;
        }

        string d = dataDir;

        // Poland (PESEL register statistics)
        NameSet plGm = Reg(Tier2Sources.PlGiven(d, 'M', "pl/given-m-2026-01-20.xlsx", 100));
        NameSet plGf = Reg(Tier2Sources.PlGiven(d, 'F', "pl/given-f-2026-01-20.xlsx", 100));
        NameSet plFm = Reg(Tier2Sources.PlFamily(d, 'M', "pl/surname-m-2026-01-20.xlsx", 100));
        NameSet plFf = Reg(Tier2Sources.PlFamily(d, 'F', "pl/surname-f-2026-01-20.xlsx", 100));
        NameSet plGiven = new() { Id = "pl-given", Names = plGm.Names.Concat(plGf.Names).ToList() };
        NameSet plFamilyAll = new() { Id = "pl-family", Names = plFm.Names.Concat(plFf.Names).ToList() };

        NameSet fiG = Reg(Tier2Sources.FiGiven(d, 100));
        NameSet fiF = Reg(Tier2Sources.FiFamily(d, 100));
        NameSet lvG = Reg(Tier2Sources.LvGiven(d, 50));
        NameSet ieGb = Reg(Tier2Sources.IeNames(d, "ie/VSA50.csv", 'M', false, 1990, 20));
        NameSet ieGg = Reg(Tier2Sources.IeNames(d, "ie/VSA60.csv", 'F', false, 1990, 20));
        NameSet ieF = Reg(Tier2Sources.IeNames(d, "ie/VSA110.csv", '-', true, 2000, 10));
        NameSet beGm = Reg(Tier2Sources.BeNames(d, "be/TA_POP_MALE_2026.zip", 'M', false, 200));
        NameSet beGf = Reg(Tier2Sources.BeNames(d, "be/TA_POP_FEMALE_2026.zip", 'F', false, 200));
        NameSet beF = Reg(Tier2Sources.BeNames(d, "be/TA_POP_LST_2026.zip", '-', true, 200));
        NameSet atG = Reg(Tier2Sources.AtGiven(d, 1984, 50));
        NameSet chF = Reg(Tier2Sources.ChFamily(d, 100));
        NameSet chG = Reg(Tier2Sources.ChGiven(d, 1940, 20));
        NameSet deG = Reg(Tier2Sources.DeGiven(d, 1));
        NameSet gbG = Reg(Tier2Sources.GbGiven(d, 5));
        NameSet gbF = Reg(Tier2Sources.GbFamily(d, 10));
        NameSet auG = Reg(Tier2Sources.AuGiven(d, 1980, 50));
        NameSet noG = Reg(Tier2Sources.NoGiven(d, 200));
        NameSet noF = Reg(Tier2Sources.NoFamily(d, 200));
        NameSet seG = Reg(Tier2Sources.SeGiven(d, 100));
        NameSet seF = Reg(Tier2Sources.SeFamily(d, 100));
        NameSet siG = Reg(Tier2Sources.SiGiven(d, 50));
        NameSet siF = Reg(Tier2Sources.SiFamily(d, 50));
        NameSet dkGm = Reg(Tier2Sources.DkRanked(d, "Fornavne - m|1000 mest", 'M', false, "dk-given-M", 1000));
        NameSet dkGf = Reg(Tier2Sources.DkRanked(d, "Fornavne - kvinder|1000 mest", 'F', false, "dk-given-F", 1000));
        NameSet dkF = Reg(Tier2Sources.DkRanked(d, "Efternavne|5000 mest", '-', true, "dk-family", 5000));

        // France (INSEE), read through the pools of the US/CA set.
        NamePools basic = NamePools.Load(d, Path.Combine(AppContext.BaseDirectory, "templates"));

        NameSet beG = new() { Id = "be-given", Names = beGm.Names.Concat(beGf.Names).ToList() };
        NameSet ieG = new() { Id = "ie-given", Names = ieGb.Names.Concat(ieGg.Names).ToList() };
        NameSet dkG = new() { Id = "dk-given", Names = dkGm.Names.Concat(dkGf.Names).ToList() };

        // Our own short lists, for the languages that have no open list.
        Dictionary<string, (NameSet Given, NameSet Family)> own = new(StringComparer.Ordinal);
        foreach (string lang in new[] { "nl", "it", "es", "pt", "ca", "mt", "is", "et", "lv", "lt", "cs", "sk", "hr", "hu", "ro" })
        {
            own[lang] = (Reg(Own(templateDir, "given", lang, "own-given-" + lang)), Reg(Own(templateDir, "family", lang, "own-family-" + lang)));
        }

        List<Tier2Unit> units = [];

        void Add(string lang, string region, double weight, WeightedList<GivenName> given, WeightedList<string> family, string inputs, bool open,
            bool twoFamily = false, bool familyFirst = false, WeightedList<string>? famF = null, WeightedList<string>? famM = null)
        {
            units.Add(new Tier2Unit
            {
                Lang = lang, Region = region, Weight = weight, Given = given, Family = family, FamilyF = famF, FamilyM = famM, TwoFamilyNames = twoFamily,
                FamilyFirst = familyFirst, Source = open ? "name-lists" : "templates", Inputs = inputs,
            });
        }

        // German-speaking countries. Only Austria and Zurich/Rostock publish given names and only the FSO publishes family names, so the Swiss
        // family-name list stands in for Germany and Austria too (recorded in the provenance log).
        WeightedList<string> chFamily = FamilyPool((chF, 1));
        Add("de", "DE", 7, GivenPool((deG, 0.3), (atG, 0.4), (chG, 0.3)), chFamily, "de-given+at-given+ch-given; ch-family (proxy)", true);
        Add("de", "AT", 3.5, GivenPool((atG, 0.8), (deG, 0.2)), chFamily, "at-given+de-given; ch-family (proxy)", true);
        Add("de", "CH", 4, GivenPool((chG, 0.7), (atG, 0.3)), chFamily, "ch-given+at-given; ch-family", true);
        Add("de", "LI", 0.5, GivenPool((chG, 0.5), (atG, 0.5)), chFamily, "ch-given+at-given; ch-family", true);

        // Dutch and French in Belgium use the Statbel lists; the Netherlands, Luxembourg and Monaco have no open list, so they use them as a proxy.
        WeightedList<GivenName> beGiven = GivenPool((beG, 1));
        WeightedList<string> beFamily = FamilyPool((beF, 1));
        Add("nl", "BE", 4, beGiven, beFamily, "be-given; be-family", true);
        Add("nl", "NL", 5, GivenPool((beG, 0.8), (own["nl"].Given, 0.2)), FamilyPool((beF, 0.8), (own["nl"].Family, 0.2)), "be-given+own-given-nl; be-family+own-family-nl (proxy)", true);
        Add("fr", "BE", 2, beGiven, beFamily, "be-given; be-family", true);
        Add("fr", "FR", 6, basic.FrGivenInsee, basic.FrFamilyInsee, "insee-given; insee-family", true);
        Add("fr", "CH", 1.5, basic.FrGivenInsee, FamilyPool((chF, 0.5)), "insee-given; ch-family", true);
        Add("fr", "LU", 0.5, basic.FrGivenInsee, basic.FrFamilyInsee, "insee-given; insee-family (proxy)", true);
        Add("fr", "MC", 0.3, basic.FrGivenInsee, basic.FrFamilyInsee, "insee-given; insee-family (proxy)", true);

        // Southern Europe: our own lists (no open list found).
        Add("it", "IT", 6, GivenPool((own["it"].Given, 1)), FamilyPool((own["it"].Family, 1)), "own-given-it; own-family-it", false);
        Add("es", "ES", 8, GivenPool((own["es"].Given, 1)), FamilyPool((own["es"].Family, 1)), "own-given-es; own-family-es", false, twoFamily: true);
        Add("es", "MX", 3, GivenPool((own["es"].Given, 1)), FamilyPool((own["es"].Family, 1)), "own-given-es; own-family-es", false, twoFamily: true);
        Add("pt", "PT", 4, GivenPool((own["pt"].Given, 1)), FamilyPool((own["pt"].Family, 1)), "own-given-pt; own-family-pt", false, twoFamily: true);
        Add("ca", "ES", 1.5, GivenPool((own["ca"].Given, 1)), FamilyPool((own["ca"].Family, 1)), "own-given-ca; own-family-ca", false, twoFamily: true);
        Add("mt", "MT", 1, GivenPool((own["mt"].Given, 1)), FamilyPool((own["mt"].Family, 1)), "own-given-mt; own-family-mt", false);

        // British Isles and Australia.
        WeightedList<GivenName> gbGiven = GivenPool((gbG, 1));
        WeightedList<string> gbFamily = FamilyPool((gbF, 0.7), (ieF, 0.3));
        Add("en", "GB", 7, gbGiven, FamilyPool((gbF, 1)), "gb-given; gb-family (Scotland)", true);
        Add("en", "IE", 3, GivenPool((ieG, 1)), FamilyPool((ieF, 0.7), (gbF, 0.3)), "ie-given; ie-family+gb-family", true);
        Add("en", "AU", 4, GivenPool((auG, 0.7), (gbG, 0.3)), gbFamily, "au-given+gb-given; gb-family+ie-family (proxy)", true);
        Add("en", "JE", 0.4, gbGiven, gbFamily, "gb-given; gb-family+ie-family (proxy)", true);
        Add("en", "GG", 0.3, gbGiven, gbFamily, "gb-given; gb-family+ie-family (proxy)", true);
        Add("en", "IM", 0.3, gbGiven, gbFamily, "gb-given; gb-family+ie-family (proxy)", true);

        // Nordic and Baltic.
        Add("da", "DK", 4, GivenPool((dkG, 1)), FamilyPool((dkF, 1)), "dk-given (rank-weighted); dk-family (rank-weighted)", true);
        Add("sv", "SE", 4, GivenPool((seG, 1)), FamilyPool((seF, 1)), "se-given; se-family", true);
        Add("nb", "NO", 4, GivenPool((noG, 1)), FamilyPool((noF, 1)), "no-given; no-family", true);
        Add("fi", "FI", 4, GivenPool((fiG, 1)), FamilyPool((fiF, 1)), "fi-given; fi-family", true);
        Add("is", "IS", 1, GivenPool((own["is"].Given, 1)), FamilyPool((own["is"].Family, 1)), "own-given-is; own-family-is", false);
        Add("et", "EE", 1.5, GivenPool((own["et"].Given, 1)), FamilyPool((own["et"].Family, 1)), "own-given-et; own-family-et", false);
        Add("lv", "LV", 2, GivenPool((lvG, 1)), FamilyPool((own["lv"].Family, 1)), "lv-given; own-family-lv", true);
        Add("lt", "LT", 1.5, GivenPool((own["lt"].Given, 1)), FamilyPool((own["lt"].Family, 1)), "own-given-lt; own-family-lt", false);

        // Central Europe and the Balkans.
        Add("pl", "PL", 7, GivenPool((plGiven, 1)), FamilyPool((plFamilyAll, 1)), "pl-given; pl-family (feminine and masculine lists paired by sex)", true,
            famF: FamilyPool((plFf, 1)), famM: FamilyPool((plFm, 1)));
        Add("cs", "CZ", 3, GivenPool((own["cs"].Given, 1)), FamilyPool((own["cs"].Family, 1)), "own-given-cs; own-family-cs", false);
        Add("sk", "SK", 2, GivenPool((own["sk"].Given, 1)), FamilyPool((own["sk"].Family, 1)), "own-given-sk; own-family-sk", false);
        Add("sl", "SI", 2.5, GivenPool((siG, 1)), FamilyPool((siF, 1)), "si-given; si-family", true);
        Add("hr", "HR", 2.5, GivenPool((own["hr"].Given, 1)), FamilyPool((own["hr"].Family, 1)), "own-given-hr; own-family-hr", false);
        Add("hu", "HU", 3, GivenPool((own["hu"].Given, 1)), FamilyPool((own["hu"].Family, 1)), "own-given-hu; own-family-hu", false, familyFirst: true);
        Add("ro", "RO", 3, GivenPool((own["ro"].Given, 1)), FamilyPool((own["ro"].Family, 1)), "own-given-ro; own-family-ro", false);

        return units;
    }
}
