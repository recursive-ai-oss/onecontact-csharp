using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;

namespace RecursiveAI.OneContact.Addresses.Tier2;

// Country inference without a hint. Several countries write their postcodes alike (four digits: Belgium, Austria, Switzerland, Liechtenstein, Luxembourg, Denmark, Norway,
// Slovenia, Hungary; five: France, Germany, Spain, Italy, Croatia, Mexico, Finland, Estonia, Lithuania, Sweden), so the postcode alone does not name the country.
// The text is read once as each country that can read it; the country is chosen only when the words and letters of the text belong to exactly one of those countries.
// Nothing is guessed: with no evidence, or with evidence for two countries, the text is refused as before.
internal static partial class Tier2Reader
{
    // A trial read costs a full parse, so the text must at least hold a digit (every postcode has one) and be of address size.
    private const int MaxInferenceLength = 600;

    private static bool TryInfer(string text, ParseDetail failure, out ParsedAddress? address, out ParseDetail detail)
    {
        address = null;
        detail = failure;
        if (text.Length > MaxInferenceLength || !text.AsSpan().ContainsAnyInRange('0', '9')
            || !(failure.PrimaryReason.Equals(ReasonCode.AddressCountryUndetermined) || failure.PrimaryReason.Equals(ReasonCode.AddressNoLastLine)))
        {
            return false;
        }

        // "Via Toledo 100, Napoli NA": a last line of locality and province code with no postcode. Only Italy reads such a line without a hint, and the code is the evidence.
        if (failure.PrimaryReason.Equals(ReasonCode.AddressNoLastLine) && TryInferProvinceLine(text, out address, out detail))
        {
            return true;
        }

        // Most text that reaches here is an address of the United States or Canada, which has no evidence of any country in it: the cheap look comes before the trial readings.
        if (!HasAnyEvidence(text))
        {
            return false;
        }

        List<(string Code, ParsedAddress Address, ParseDetail Detail)> readable = ReadCandidates(text);

        if (readable.Count == 0)
        {
            return false;
        }

        HashSet<string> chosen = Evidence(text, Universe(readable.Select(r => (r.Code, r.Address))), readable.ToDictionary(r => r.Code, r => ZoneOnlyDigit(r.Address, r.Code)));
        if (chosen.Count != 1)
        {
            return false;
        }

        string only = chosen.First();
        if (!readable.Exists(r => r.Code == only))
        {
            return false;
        }

        if (!LettersHold(text, only))
        {
            return false;
        }

        (string _, ParsedAddress found, ParseDetail foundDetail) = readable.First(r => r.Code == only);
        found.SourceText = text;
        found.Hint = Region.Unknown;
        address = found;
        detail = foundDetail;
        return true;
    }

    // The countries that can read the text, each with its reading: a trial read per candidate country, minus the readings that are plainly another country's text.
    private static List<(string Code, ParsedAddress Address, ParseDetail Detail)> ReadCandidates(string text)
    {
        List<(string Code, ParsedAddress Address, ParseDetail Detail)> readable = [];
        foreach (string code in T2Rules.TrialCountries)
        {
            Reader trial = new(text, Region.Parse(code)) { Inferring = true };
            if (trial.Run(out ParsedAddress? read, out ParseDetail readDetail) == OperationStatus.Done && read is not null && read.Country == Region.Parse(code)
                && !EndsWithOtherCountry(text, code) && ZoneAllows(read, code) && !LeavesAdminCode(read, code) && !LooksDutch(read, code))
            {
                readable.Add((code, read, readDetail));
            }
        }

        // Swedish postcodes are written with a space after the third digit (UPU Sweden sheet); five digits without it are as likely Finnish, where Swedish-speaking municipalities
        // (Aland, Nyland, Ostrobothnia) write Swedish street names, so such a text is not read as Swedish.
        readable.RemoveAll(r => r.Code == "SE" && r.Address.PostalCode is { } p && !p.Text.Contains(' ', StringComparison.Ordinal));

        // A reading that left a province or canton code in the locality loses to one that took it as the admin area, and a four-digit postcode followed by two capital
        // letters in a locality is a Dutch postcode, not a Belgian town (the text is then refused rather than read as the wrong country).
        HashSet<string> taken = [.. readable.Where(r => r.Address.AdminArea is not null).Select(r => r.Address.AdminArea!.Value.Text)];
        readable.RemoveAll(r => r.Address.AdminArea is null && LastWord(r.Address.Locality) is { } word && word.Length == 2 && taken.Contains(word));

        return readable;
    }

    private static bool TryInferProvinceLine(string text, out ParsedAddress? address, out ParseDetail detail)
    {
        address = null;
        detail = default!;
        Reader trial = new(text, Region.Parse("IT")) { Inferring = true };
        if (trial.Run(out ParsedAddress? read, out ParseDetail readDetail) != OperationStatus.Done || read is not { PostalCode: null, AdminArea: not null } || read.Country != Region.Parse("IT") || !LettersHold(text, "IT"))
        {
            return false;
        }

        read.SourceText = text;
        read.Hint = Region.Unknown;
        address = read;
        detail = readDetail;
        return true;
    }

    // Whether every letter of the text outside Latin-1 belongs to the language of the country: a text with a letter the country does not use is not its text.
    private static bool LettersHold(string text, string code)
    {
        InferenceTables.Instance.Letters.TryGetValue(code, out string? own);
        foreach (char raw in text)
        {
            if ((raw >= '\u0100' || raw == 'ß') && char.IsLetter(raw) && (own is null || !own.Contains(char.ToLowerInvariant(raw), StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    // Whether the text has a letter, a word or a postcode zone that any country's evidence lists.
    private static bool HasAnyEvidence(string text)
    {
        foreach (char raw in text)
        {
            if ((raw >= 'Ā' || raw == 'ß') && char.IsLetter(raw))
            {
                return true;
            }
        }

        for (int i = 0; i + 5 < text.Length; i++)
        {
            if (text[i] == '0' && char.IsAsciiDigit(text[i + 1]) && char.IsAsciiDigit(text[i + 2]) && text[i + 3] == ' ' && char.IsAsciiDigit(text[i + 4]) && char.IsAsciiDigit(text[i + 5])
                && (i == 0 || !char.IsAsciiDigit(text[i - 1])))
            {
                return true;
            }
        }

        foreach ((string key, bool numberAfter, bool numberBefore) in Words(text))
        {
            foreach (string part in key.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (InferenceTables.Instance.SupportsAny(part, numberAfter, numberBefore))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The countries a text may be: those that read it and every other country that writes a postcode in the same shape (four digits, five digits, three and two). A reader
    // can fail on text that is fine (a country's own odd line), so evidence is weighed against all the countries sharing the form, not only the ones that read it.
    private static List<string> Universe(IEnumerable<(string Code, ParsedAddress Address)> readable)
    {
        List<(string Code, ParsedAddress Address)> reads = [.. readable];
        HashSet<string> codes = new(reads.Select(r => r.Code), StringComparer.Ordinal);
        foreach ((string _, ParsedAddress address) in reads)
        {
            string shape = address.PostalCode is { } pc ? new string([.. pc.Standard.Select(c => char.IsAsciiDigit(c) ? 'd' : c)]) : string.Empty;
            foreach (T2Country country in T2Rules.Countries.Values)
            {
                bool sameShape = shape switch
                {
                    "dddd" => country.Postcode == PostcodeForm.FourDigits,
                    "ddddd" => country.Postcode == PostcodeForm.FiveDigits,
                    "ddd dd" => country.Postcode is PostcodeForm.Swedish or PostcodeForm.ThreeTwoDigits,
                    _ => false,
                };
                if (sameShape && !(country.Code is "GP" or "MQ" or "GF" or "RE" or "YT"))
                {
                    codes.Add(country.Code);
                }
            }
        }

        return [.. codes];
    }

    // The last word of a locality field, or null.
    private static string? LastWord(ParsedAddressField? locality) =>
        locality is { } l ? l.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() : null;

    // The text ends with the name of a country other than the one read ("... Slovenska republika" read as Slovenia): the name decides, so the other readings are not trials.
    private static bool EndsWithOtherCountry(string text, string code)
    {
        string[] words = T2Rules.Key(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int n = Math.Min(T2Rules.MaxCountryWords, words.Length - 1); n >= 1; n--)
        {
            if (T2Rules.CountryNames.TryGetValue(string.Join(' ', words.Skip(words.Length - n)), out string? named) && named != code)
            {
                return true;
            }
        }

        return false;
    }

    // Czech postcodes start with 1 to 7 and Slovak ones with 0, 8 or 9 (the first digit names the region of the former Czechoslovakia): a reading whose postcode starts otherwise is wrong.
    private static bool ZoneAllows(ParsedAddress read, string code)
    {
        if (read.PostalCode is not { } pc || !InferenceTables.Instance.Zones.TryGetValue(code, out string? zone))
        {
            return true;
        }

        char first = pc.Standard.FirstOrDefault(char.IsAsciiDigit);
        return zone.Contains(first, StringComparison.Ordinal);
    }

    private static bool ZoneOnlyDigit(ParsedAddress read, string code) =>
        read.PostalCode is { } pc && InferenceTables.Instance.ZoneOnly.TryGetValue(code, out string? only) && pc.Text.Contains(' ', StringComparison.Ordinal) && pc.Standard.FirstOrDefault(char.IsAsciiDigit) is var first && only.Contains(first, StringComparison.Ordinal);

    // Four digits and two capital letters ("4261 BK") are a Dutch postcode: a reading of another country that took the letters for the locality is not one.
    private static bool LooksDutch(ParsedAddress read, string code) =>
        code != "NL" && read.PostalCode is { Standard: { Length: 4 } } && read.Locality is { Text: { Length: 2 } letters } && letters.All(char.IsAsciiLetterUpper);

    // A locality that ends in a parenthesised or capital two-letter Italian province code, in a reading that is not Italian: the code was left in the locality.
    private static bool LeavesAdminCode(ParsedAddress read, string code)
    {
        if (code is "IT" or "SM" or "VA" || read.AdminArea is not null || LastWord(read.Locality) is not { } word)
        {
            return false;
        }

        string bare = word.Trim('(', ')');
        bool parenthesised = bare.Length < word.Length;
        return bare.Length == 2 && (parenthesised || bare.All(char.IsAsciiLetterUpper)) && T2Rules.ItProvinces.Contains(bare.ToUpperInvariant());
    }

    // The countries among <paramref name="readable"/> that have evidence in the text which no other of them has.
    private static HashSet<string> Evidence(string text, List<string> readable, Dictionary<string, bool> zoneVotes)
    {
        HashSet<string> voted = new(StringComparer.Ordinal);
        InferenceTables tables = InferenceTables.Instance;

        // Letters: a letter that only one of the countries uses.
        HashSet<char> seen = [];
        foreach (char raw in text)
        {
            if (!(raw >= '\u0100' || raw == 'ß') || !char.IsLetter(raw))
            {
                continue;
            }

            char c = char.ToLowerInvariant(raw);
            if (!seen.Add(c))
            {
                continue;
            }

            string? only = null;
            int users = 0;
            foreach (string code in readable)
            {
                if (tables.Letters.TryGetValue(code, out string? letters) && letters.Contains(c, StringComparison.Ordinal))
                {
                    only = code;
                    users++;
                }
            }

            if (users == 1)
            {
                voted.Add(only!);
            }
        }

        // Postcode zones: a first digit that only one of the countries uses (Slovakia's 0).
        foreach (string code in readable)
        {
            if (zoneVotes.TryGetValue(code, out bool zoneVote) && zoneVote)
            {
                voted.Add(code);
            }
        }

        // Words: whole words, word endings, abbreviations beside a number, and the places of a small country.
        foreach ((string key, bool numberAfter, bool numberBefore) in Words(text))
        {
            foreach (string part in key.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string? only = null;
                int users = 0;
                foreach (string code in readable)
                {
                    if (tables.Supports(code, part, numberAfter, numberBefore))
                    {
                        only = code;
                        users++;
                    }
                }

                if (users == 1)
                {
                    voted.Add(only!);
                }
            }
        }

        return voted;
    }

    // The words of the text in the form the tables use, each with whether a house number follows it right away ("g. 5", "tn 24": only spaces and a period between, no comma, and
    // at most four digits, so a postcode is not one) and whether a floor number precedes it ("3. sal").
    private static List<(string Key, bool NumberAfter, bool NumberBefore)> Words(string text)
    {
        List<(string, bool, bool)> words = [];
        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetter(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && (char.IsLetter(text[i]) || text[i] is '-' or '\'' or '\u2019' or '\u2013'))
            {
                i++;
            }

            int end = i;
            int after = end;
            if (after < text.Length && text[after] == '.')
            {
                after++;
            }

            while (after < text.Length && text[after] == ' ')
            {
                after++;
            }

            int digits = 0;
            while (after + digits < text.Length && char.IsAsciiDigit(text[after + digits]))
            {
                digits++;
            }

            bool numberAfter = digits is >= 1 and <= 4;
            int before = start - 1;
            while (before >= 0 && text[before] == ' ')
            {
                before--;
            }

            if (before >= 0 && text[before] == '.')
            {
                before--;
            }

            int floor = 0;
            while (before - floor >= 0 && char.IsAsciiDigit(text[before - floor]))
            {
                floor++;
            }

            words.Add((T2Rules.Key(text[start..end]), numberAfter, floor is >= 1 and <= 2));
        }

        return words;
    }

    private sealed class InferenceTables
    {
        internal static readonly InferenceTables Instance = new();

        private readonly Dictionary<string, HashSet<string>> _words = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _marked = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _after = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _places = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _suffixes = new(StringComparer.Ordinal);

        internal Dictionary<string, string> Letters { get; } = new(StringComparer.Ordinal);

        private readonly HashSet<string> _countries = new(StringComparer.Ordinal);

        internal Dictionary<string, string> Zones { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> ZoneOnly { get; } = new(StringComparer.Ordinal);

        private InferenceTables()
        {
            foreach (string[] r in RuleFile.Read("t2-inference.tsv"))
            {
                _countries.Add(r[1]);
                switch (r[0])
                {
                    case "letters":
                        Letters[r[1]] = r[2];
                        break;
                    case "zone":
                        Zones[r[1]] = r[2];
                        break;
                    case "zoneonly":
                        ZoneOnly[r[1]] = r[2];
                        break;
                    case "word":
                        Add(_words, r[1], r[2]);
                        break;
                    case "marked":
                        Add(_marked, r[1], r[2]);
                        break;
                    case "after":
                        Add(_after, r[1], r[2]);
                        break;
                    case "place":
                        Add(_places, r[1], r[2]);
                        break;
                    case "suffix":
                        if (!_suffixes.TryGetValue(r[1], out List<string>? list))
                        {
                            list = [];
                            _suffixes[r[1]] = list;
                        }

                        list.Add(r[2]);
                        break;
                    default:
                        throw new InvalidDataException("t2-inference.tsv: unknown kind '" + r[0] + "'.");
                }
            }
        }

        private static void Add(Dictionary<string, HashSet<string>> map, string country, string word)
        {
            if (!map.TryGetValue(country, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[country] = set;
            }

            set.Add(word);
        }

        // Whether the word is evidence for any country.
        internal bool SupportsAny(string key, bool numberAfter, bool numberBefore)
        {
            foreach (string country in _countries)
            {
                if (Supports(country, key, numberAfter, numberBefore))
                {
                    return true;
                }
            }

            return false;
        }

        // Whether the word is evidence for the country.
        internal bool Supports(string country, string key, bool numberAfter, bool numberBefore)
        {
            if (_words.TryGetValue(country, out HashSet<string>? w) && w.Contains(key))
            {
                return true;
            }

            if (_places.TryGetValue(country, out HashSet<string>? p) && p.Contains(key))
            {
                return true;
            }

            if (numberAfter && _marked.TryGetValue(country, out HashSet<string>? m) && m.Contains(key))
            {
                return true;
            }

            if (numberBefore && _after.TryGetValue(country, out HashSet<string>? f) && f.Contains(key))
            {
                return true;
            }

            if (_suffixes.TryGetValue(country, out List<string>? ends))
            {
                foreach (string end in ends)
                {
                    if (key.Length >= end.Length + 2 && key.EndsWith(end, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
