using System.Globalization;
using System.Text;

namespace OneContact.Data.AddressTier2B;

/// <summary>
/// Street-type words, their written-out forms and abbreviations, and the casing of upper-case register names, for the countries of group B.
/// The Catastro writes a two-letter type code in front of the name; the ANNCSU writes the type as the first word of an upper-case name.
/// </summary>
internal static class LexB
{
    // Catastro street-type codes (Dirección General del Catastro, INSPIRE address data): code -> Spanish, Catalan, Galician word.
    private static readonly Dictionary<string, (string Es, string Ca, string Gl)> CatastroTypes = new(StringComparer.Ordinal)
    {
        ["CL"] = ("Calle", "Carrer", "Rúa"),
        ["AV"] = ("Avenida", "Avinguda", "Avenida"),
        ["PZ"] = ("Plaza", "Plaça", "Praza"),
        ["PL"] = ("Plaza", "Plaça", "Praza"),
        ["PS"] = ("Paseo", "Passeig", "Paseo"),
        ["CM"] = ("Camino", "Camí", "Camiño"),
        ["CR"] = ("Carretera", "Carretera", "Estrada"),
        ["TR"] = ("Travesía", "Travessera", "Travesía"),
        ["UR"] = ("Urbanización", "Urbanització", "Urbanización"),
        ["RD"] = ("Ronda", "Ronda", "Ronda"),
        ["GV"] = ("Gran Vía", "Gran Via", "Gran Vía"),
        ["CJ"] = ("Callejón", "Carreró", "Calella"),
        ["PJ"] = ("Pasaje", "Passatge", "Pasaxe"),
        ["CS"] = ("Cuesta", "Costa", "Costa"),
        ["AL"] = ("Alameda", "Alameda", "Alameda"),
        ["BO"] = ("Barrio", "Barri", "Barrio"),
        ["BJ"] = ("Bajada", "Baixada", "Baixada"),
        ["PQ"] = ("Parque", "Parc", "Parque"),
        ["SD"] = ("Senda", "Sender", "Sendeiro"),
        ["AC"] = ("Acceso", "Accés", "Acceso"),
        ["GL"] = ("Glorieta", "Rotonda", "Glorieta"),
        ["RB"] = ("Rambla", "Rambla", "Rambla"),
        ["CO"] = ("Colonia", "Colònia", "Colonia"),
        ["RU"] = ("Rúa", "Carrer", "Rúa"),
        ["LG"] = ("Lugar", "Lloc", "Lugar"),
        ["PD"] = ("Partida", "Partida", "Partida"),
    };

    internal static (string Es, string Ca, string Gl)? CatastroType(string code) => CatastroTypes.TryGetValue(code, out (string, string, string) t) ? t : null;

    // Italian street types (the first word of an ANNCSU odonimo), written the way a postal address writes them.
    private static readonly HashSet<string> ItalianTypes = new(StringComparer.Ordinal)
    {
        "via", "viale", "piazza", "piazzale", "corso", "largo", "vicolo", "strada", "contrada", "borgo", "vico", "lungotevere", "lungomare", "salita",
        "discesa", "traversa", "rione", "calle", "campo", "fondamenta", "passaggio", "stradone", "vialetto", "galleria", "rampa", "sentiero", "piazzetta",
        "corte", "cortile", "lungarno", "vicoletto", "località", "frazione", "regione", "strada", "stradella", "viottolo", "circonvallazione", "lungolago",
        "lungofiume", "lungopo", "lungadige", "riviera", "ponte", "scalinata",
    };

    internal static bool IsItalianType(string lowerWord) => ItalianTypes.Contains(lowerWord);

    private static readonly Dictionary<string, string[]> Abbr = new(StringComparer.Ordinal)
    {
        // Spanish and Catalan (Correos and the Generalitat's usual forms)
        ["Calle"] = ["C/", "Cl.", "C."], ["Avenida"] = ["Avda.", "Av.", "Av"], ["Plaza"] = ["Pza.", "Pl.", "Plza."], ["Paseo"] = ["Pº", "P.º", "Pso."],
        ["Camino"] = ["Cno.", "Cmno."], ["Carretera"] = ["Ctra.", "Crta."], ["Travesía"] = ["Trav.", "Tvia."], ["Ronda"] = ["Rda."],
        ["Urbanización"] = ["Urb."], ["Carrer"] = ["C/", "C."], ["Avinguda"] = ["Av.", "Avda."], ["Plaça"] = ["Pl.", "Plça."], ["Passeig"] = ["Pg.", "Pg"],
        ["Camí"] = ["Cm."], ["Rúa"] = ["R/", "R."], ["Praza"] = ["Pza.", "Pr."], ["Estrada"] = ["Estr."],
        // Italian
        ["Via"] = ["V."], ["Viale"] = ["V.le", "Vle"], ["Piazza"] = ["P.zza", "P.za", "Pza"], ["Piazzale"] = ["P.le", "P.le"], ["Corso"] = ["C.so", "Cso"],
        ["Largo"] = ["L.go", "Lgo"], ["Vicolo"] = ["V.lo", "Vc."], ["Strada"] = ["Str."], ["Contrada"] = ["C.da", "Cda"], ["Località"] = ["Loc."], ["Frazione"] = ["Fraz."],
        ["Lungomare"] = ["L.mare"], ["Lungotevere"] = ["L.tevere"], ["Borgo"] = ["B.go"],
        // Portuguese
        ["Rua"] = ["R."], ["Travessa"] = ["Trav.", "Tv."], ["Praça"] = ["Pç.", "Pr."], ["Alameda"] = ["Al."], ["Calçada"] = ["Cç."],
        ["Urbanização"] = ["Urb."], ["Beco"] = ["Bc."], ["Caminho"] = ["Cam."],
        // English
        ["Road"] = ["Rd"], ["Street"] = ["St"], ["Avenue"] = ["Ave"], ["Drive"] = ["Dr"], ["Lane"] = ["Ln"], ["Close"] = ["Cl"], ["Crescent"] = ["Cres"],
        ["Gardens"] = ["Gdns"], ["Terrace"] = ["Terr"], ["Place"] = ["Pl"], ["Court"] = ["Ct"], ["Square"] = ["Sq"], ["Park"] = ["Pk"],
        // Catalan (Andorra), Maltese
    };

    internal static string[] Abbreviations(string type) => Abbr.TryGetValue(type, out string[]? a) ? a : [];

    /// <summary>English street types a British, Irish or Crown-dependency road name ends with (the type follows the name).</summary>
    internal static readonly HashSet<string> EnglishTypes = new(StringComparer.Ordinal)
    {
        "Road", "Street", "Lane", "Close", "Avenue", "Drive", "Gardens", "Way", "Crescent", "Court", "Grove", "Place", "Terrace", "Hill", "Park", "Walk",
        "Rise", "Mews", "Row", "Square", "Green", "Gate", "Path", "Parade", "Gardens", "View", "Mount", "Wynd", "Brae", "Vale", "Heights", "Lawn", "Lawns",
        "Quay", "Mall", "Circus", "Embankment", "Fields", "Hey", "Meadow", "Orchard", "Ride", "Villas", "Bank", "Cottages", "Rd", "St", "Ave",
    };

    private static readonly Dictionary<string, HashSet<string>> SmallWords = new(StringComparer.Ordinal)
    {
        ["es"] = ["de", "del", "la", "las", "los", "el", "y", "e", "a", "al", "en"],
        ["ca"] = ["de", "del", "la", "les", "els", "el", "i", "d'", "l'", "dels", "al"],
        ["gl"] = ["de", "do", "da", "das", "dos", "e", "o", "a"],
        ["it"] = ["di", "del", "della", "dei", "delle", "degli", "dello", "da", "e", "in", "al", "alla", "ai", "alle", "agli", "allo", "sul", "sulla", "nel", "nella", "a", "con"],
        ["de"] = ["von", "zu", "der", "dem", "und", "am", "im", "an"],
        ["pt"] = ["de", "da", "do", "das", "dos", "e", "a", "o"],
    };

    /// <summary>
    /// Title case for an upper-case register name: every word capitalized except the language's small words (not the first), Roman numerals
    /// kept upper-case, and the letter after an apostrophe or hyphen capitalized.
    /// </summary>
    internal static string TitleCase(string text, string lang)
    {
        SmallWords.TryGetValue(lang, out HashSet<string>? small);
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        StringBuilder sb = new();
        for (int w = 0; w < words.Length; w++)
        {
            string raw = words[w];
            string lower = raw.ToLowerInvariant();
            if (w > 0)
            {
                sb.Append(' ');
            }

            if (w > 0 && small is not null && small.Contains(lower))
            {
                sb.Append(lower);
                continue;
            }

            if (raw.Length > 1 && raw.All(c => c is 'I' or 'V' or 'X' or 'L' or 'C' or 'D' or 'M') && !(small?.Contains(lower) ?? false) && raw.Length <= 6)
            {
                sb.Append(raw);
                continue;
            }

            bool startOfPart = true;
            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
                // A short article or preposition before an apostrophe ("dell'", "l'", "d'") stays lower-case in the middle of a name.
                if (startOfPart)
                {
                    int apos = lower.IndexOf('\'', i);
                    bool article = apos > i && apos - i <= 5 && w > 0 && small is not null && small.Contains(lower[i..(apos + 1)]);
                    article |= apos > i && apos - i <= 5 && w > 0 && lang == "it" && lower[i..apos] is "dell" or "all" or "nell" or "sull" or "dall" or "un" or "l" or "d";
                    if (article)
                    {
                        sb.Append(lower, i, apos + 1 - i);
                        i = apos;
                        startOfPart = true;
                        continue;
                    }

                    sb.Append(char.ToUpperInvariant(c));
                    startOfPart = false;
                }
                else
                {
                    sb.Append(c);
                    startOfPart = c is '\'' or '-' or '’';
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>The text without diacritics, upper-cased, letters and digits only: the join key between registers that spell a place differently.</summary>
    internal static string PlaceKey(string text)
    {
        StringBuilder sb = new();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }

        return sb.ToString();
    }

    internal static string[] CountryNames(string cc, string lang) => (cc, lang) switch
    {
        ("GB", "cy") => ["Y Deyrnas Unedig", "United Kingdom", "UK"],
        ("GB", _) => ["United Kingdom", "UK", "Great Britain", "U.K.", "GB"],
        ("JE", _) => ["Jersey", "Channel Islands", "Jersey, Channel Islands"],
        ("GG", _) => ["Guernsey", "Channel Islands", "Guernsey, Channel Islands"],
        ("IM", _) => ["Isle of Man", "British Isles", "Isle Of Man"],
        ("IE", _) => ["Ireland", "Republic of Ireland", "Éire", "IRELAND"],
        ("ES", "ca") => ["Espanya", "España", "Spain"],
        ("ES", "gl") => ["España", "Spain"],
        ("ES", _) => ["España", "Spain", "ESPAÑA", "Espanya"],
        ("IT", _) => ["Italia", "Italy", "ITALIA"],
        ("PT", _) => ["Portugal", "PORTUGAL"],
        ("MT", "mt") => ["Malta", "MALTA"],
        ("MT", _) => ["Malta", "MALTA", "Republic of Malta"],
        ("SM", _) => ["San Marino", "Repubblica di San Marino", "SAN MARINO"],
        ("VA", _) => ["Città del Vaticano", "Vaticano", "Vatican City", "Vatican City State", "Stato della Città del Vaticano"],
        ("AD", _) => ["Andorra", "Principat d'Andorra", "ANDORRA", "Principauté d'Andorre", "Principado de Andorra"],
        _ => ["", ""],
    };
}
