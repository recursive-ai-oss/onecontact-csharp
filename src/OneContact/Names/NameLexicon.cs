using System.Collections.Frozen;

namespace RecursiveAI.OneContact.Names;

/// <summary>
/// The word lists the name rules read: titles, suffixes, particles, connectors, company words and nicknames. They are written out
/// here as explicit statics (nothing is read by reflection) and are our own lists, compiled from general usage and from the
/// naming conventions of each tier language; they are not taken from any other project's lists.
/// </summary>
internal static class NameLexicon
{
    // Titles: "form|languages". "*" means every language; otherwise the title counts only when a language hint (or the default
    // language of the country hint) is one of the listed ones, because the form is also an ordinary word or initial elsewhere.
    // Forms are written the way people write them; matching folds case, marks and punctuation.
    private static readonly string[] TitleSource =
    [
        // English and shared Latin-script titles
        "mr|*", "mrs|*", "ms|*", "miss|*", "mx|*", "dr|*", "prof|*", "professor|*", "sir|*", "dame|*", "lord|*", "lady|*",
        "rev|*", "reverend|*", "hon|*", "honorable|*", "honourable|*", "sen|*", "senator|*", "rep|*", "gov|*", "gen|*", "col|*",
        "capt|*", "cpt|*", "lt|*", "maj|*", "sgt|*", "cpl|*", "adm|*", "cdr|*", "cmdr|*", "pvt|*", "pastor|*", "rabbi|*", "imam|*",
        "bishop|*", "archbishop|*", "cardinal|*", "father|*", "fr|*", "judge|en", "ing|*", "mag|*", "sr|*", "sra|*", "srta|*",
        "dra|*", "eng|*", "engo|*", "enga|*", "arq|*", "profa|*", "arch|*", "doc|*",

        // French
        "m|fr", "mme|*", "mlle|*", "mm|fr", "me|fr", "monsieur|*", "madame|*", "mademoiselle|*", "maitre|fr", "abbe|fr",
        "pere|fr", "soeur|fr", "pr|fr,et", "mgr|*",

        // Spanish, Portuguese, Catalan, Galician, Basque
        "don|es,ca,gl,pt,it", "dom|pt", "dona|es,ca,gl,pt", "dna|es,ca,gl,ro", "d|es,gl", "lic|*", "licda|*", "licenciado|*", "senor|*", "senora|*",
        "senyor|ca", "senyora|ca", "jauna|eu", "andrea|eu", "jn|eu",

        // German, Dutch, Luxembourgish
        "herr|*", "frau|*", "fraulein|*", "hr|de,da,no,sv,is,et,lb,nl", "dipl|*", "med|de", "rer|de", "nat|de", "jur|de", "phil|de", "hc|de",
        "habil|de", "univ|de,sl,hr", "dkfm|de", "dhr|nl", "mevr|nl", "mw|nl", "mevrouw|nl", "deheer|nl", "drs|nl", "ir|nl", "bc|nl,cs,sk",
        "har|lb", "madamm|lb",

        // Italian
        "sig|it", "sigra|it", "signor|it", "signora|it", "signorina|it", "dott|it", "dottssa|it", "dssa|it", "profssa|it",
        "avv|*", "geom|it", "rag|it", "on|it", "cav|it", "comm|it",

        // Nordic and Baltic
        "fru|sv,da,no,is", "froken|sv", "frk|da,no", "herra|fi", "rouva|fi", "neiti|fi", "ins|fi", "cand|da,no,sv",
        "kgs|lv", "kdze|lv", "ponas|lt", "ponia|lt", "p|lt", "proua|et", "harra|et",

        // Polish, Czech, Slovak, Slovenian, Croatian, Hungarian, Romanian, Maltese, Irish
        "pan|pl,cs,sk", "pani|pl,cs,sk", "panna|pl", "mgr|*", "inz|pl,sl,hr,cs,sk", "lek|pl", "hab|pl", "ks|pl", "mec|pl",
        // Polish professional titles as the bars and chambers print them: adwokat (adw.), radca prawny (r.pr.), the habilitated doctor's "dr hab." and the associate professor's "prof. nadzw.".
        "adw|*", "adwokat|pl", "rpr|*", "radcaprawny|pl", "nadzw|pl",
        "slecna|cs,sk", "mudr|cs,sk", "judr|cs,sk", "phdr|cs,sk", "rndr|cs,sk", "mvdr|cs,sk", "paeddr|cs,sk", "thdr|cs,sk", "dis|cs,sk",
        "g|sl,hr", "ga|sl", "gospod|sl", "gospa|sl", "gda|hr", "gdja|hr", "gospodin|hr", "gospoda|hr", "sc|hr",
        "id|hu", "ifj|hu", "ozv|hu", "dl|ro", "domnul|ro", "doamna|ro", "domnisoara|ro", "conf|ro", "lect|ro", "av|ro",
        "sur|mt", "sinjura|mt", "sinjorina|mt", "perit|mt", "antuasal|ga", "uasal|ga", "antathair|ga", "antollamh|ga", "antsiur|ga", "ollamh|ga", "bean|ga",
    ];

    // Titles that need the language hint to count; filled by the table above.
    private static readonly FrozenDictionary<string, string[]> Titles = BuildTitles();

    // Generational suffixes (Jr., III) and the unambiguous professional ones.
    private static readonly FrozenSet<string> Generational = new[]
    {
        "jr", "sr", "junior", "senior", "ii", "iii", "iv", "vi", "vii", "viii", "ix", "2nd", "3rd", "4th", "5th", "fils", "filho", "neto",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Professional = new[]
    {
        "phd", "md", "dds", "dmd", "dvm", "jd", "esq", "esquire", "cpa", "mba", "llm", "llb", "mph", "msc", "bsc", "mpa", "mfa", "dphil", "pmp",
        "cfa", "cfp", "edd", "psyd", "frcs", "mrcs", "aia", "faia", "dmin", "pharmd", "dpm", "dc", "msw", "lcsw", "cissp",
    }.ToFrozenSet(StringComparer.Ordinal);

    // Short forms that are also names or words; they count as suffixes only after a comma or when written with dots ("M.A.").
    private static readonly FrozenSet<string> AmbiguousProfessional = new[]
    {
        "ma", "ba", "bs", "ms", "do", "pe", "rn", "pa", "np", "ed",
    }.ToFrozenSet(StringComparer.Ordinal);

    // The letter casing of titles and suffixes written in capitals when casing is restored (letters only, in order).
    private static readonly FrozenDictionary<string, string> SpecialCasing = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["phd"] = "PhD", ["md"] = "MD", ["dds"] = "DDS", ["dmd"] = "DMD", ["dvm"] = "DVM", ["jd"] = "JD", ["cpa"] = "CPA", ["mba"] = "MBA",
        ["llm"] = "LLM", ["llb"] = "LLB", ["msc"] = "MSc", ["bsc"] = "BSc", ["mph"] = "MPH", ["mpa"] = "MPA", ["mfa"] = "MFA", ["dphil"] = "DPhil",
        ["pmp"] = "PMP", ["cfa"] = "CFA", ["cfp"] = "CFP", ["edd"] = "EdD", ["psyd"] = "PsyD", ["pharmd"] = "PharmD", ["dmin"] = "DMin",
        ["ii"] = "II", ["iii"] = "III", ["iv"] = "IV", ["vi"] = "VI", ["vii"] = "VII", ["viii"] = "VIII", ["ix"] = "IX",
        ["mudr"] = "MUDr", ["judr"] = "JUDr", ["phdr"] = "PhDr", ["rndr"] = "RNDr", ["mvdr"] = "MVDr", ["paeddr"] = "PaedDr", ["thdr"] = "ThDr",
        ["ma"] = "MA", ["ba"] = "BA", ["bs"] = "BS", ["ms"] = "MS", ["do"] = "DO", ["pe"] = "PE", ["rn"] = "RN", ["np"] = "NP",
        ["dipling"] = "DiplIng", ["dis"] = "DiS", ["dott"] = "Dott", ["dottssa"] = "Dott.ssa", ["sigra"] = "Sig.ra",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Particles that stand between the given names and the family name and belong to the family name.
    private static readonly FrozenSet<string> Particles = new[]
    {
        "van", "von", "de", "der", "den", "del", "della", "dei", "degli", "dal", "dalla", "dello", "di", "da", "das", "do", "dos", "du", "des",
        "la", "le", "les", "lo", "los", "las", "af", "av", "zu", "zur", "zum", "ter", "ten", "te", "tot", "op", "het",
    }.ToFrozenSet(StringComparer.Ordinal);

    // Irish particles, only with a Irish hint ("Ó" and "Ní" are letters of ordinary words elsewhere).
    private static readonly FrozenSet<string> IrishParticles = new[] { "o", "ni", "nic", "ui", "mhic" }.ToFrozenSet(StringComparer.Ordinal);

    // Words that join two names: "and" in the tier languages. Case-insensitive ones first, then the one-letter ones.
    private static readonly FrozenSet<string> Connectors = new[]
    {
        "and", "und", "et", "og", "och", "ja", "es", "un", "ir", "agus", "eta", "en", "in", "si", "an", "&",
    }.ToFrozenSet(StringComparer.Ordinal);

    // One-letter connectors are also initials: they count only in lower case (or in capitals when a hint names their language).
    private static readonly string[] SingleLetterConnectors = ["y", "e", "i", "a", "u"];

    // Words that make a text a company when no legal form is found. Folded forms.
    private static readonly FrozenSet<string> CompanyWordSet = new[]
    {
        "associates", "group", "groupe", "gruppe", "grupo", "gruppo", "holding", "holdings", "partners", "partnership", "services", "service",
        "solutions", "consulting", "consultants", "consultancy", "enterprises", "enterprise", "industries", "industry", "international", "intl",
        "technologies", "technology", "tech", "systems", "software", "labs", "laboratories", "laboratory", "bank", "trust", "foundation",
        "fund", "capital", "ventures", "insurance", "assurance", "realty", "properties", "construction", "builders", "engineering",
        "electric", "electronics", "motors", "logistics", "transport", "transportation", "pharmacy", "pharma", "clinic", "hospital",
        "university", "college", "school", "academy", "institute", "church", "ministry", "ministries", "association", "society", "club",
        "union", "council", "committee", "department", "agency", "authority", "bureau", "office", "offices", "studio", "studios", "design",
        "media", "marketing", "communications", "publishing", "press", "restaurant", "cafe", "hotel", "bakery", "pizzeria", "shop", "store",
        "market", "supermarket", "garage", "salon", "law", "firm", "attorneys", "solicitors", "lawyers", "advocates", "accountants",
        "architects", "surgery", "dental", "dentists", "veterinary", "plumbing", "heating", "roofing", "cleaning", "landscaping", "pizza", "grill", "bistro", "tavern", "pub", "brewery", "kitchen", "boutique", "emporium", "sons",
        "son", "sohne", "soehne", "sohn", "brothers", "bros", "daughters", "verlag", "handel", "stiftung", "verein", "gesellschaft", "kanzlei", "praxis", "apotheke",
        "werkstatt", "technik", "dienstleistungen", "beratung", "institut", "universitat", "hochschule", "schule", "gemeinde", "societe",
        "entreprise", "cabinet", "cie", "etablissements", "ets", "mairie", "ecole", "universite", "hopital", "pharmacie", "boulangerie",
        "empresa", "sociedad", "fundacion", "asociacion", "ayuntamiento", "universidad", "farmacia", "clinica", "despacho", "estudio",
        "constructora", "inmobiliaria", "servicios", "consultoria", "hermanos", "hnos", "sociedade", "fundacao", "associacao", "universidade",
        "servicos", "irmaos", "bedrijf", "stichting", "vereniging", "gemeente", "universiteit", "ziekenhuis", "apotheek", "kantoor",
        "diensten", "adviesbureau", "societa", "fondazione", "associazione", "comune", "universita", "ospedale", "azienda", "ditta",
        "fratelli", "foretag", "forening", "kommun", "universitet", "sjukhus", "apotek", "stiftelse", "yhtio", "yhdistys", "kaupunki",
        "sairaala", "apteekki", "toimisto", "firma", "fundacja", "stowarzyszenie", "urzad", "uniwersytet", "szpital", "apteka",
        "przedsiebiorstwo", "biuro", "nadace", "sdruzeni", "urad", "univerzita", "nemocnice", "lekarna", "kancelar", "podjetje", "drustvo",
        "poduzece", "udruga", "ceg", "alapitvany", "egyesulet", "szolgaltato", "companie", "societate", "fundatia", "asociatia", "universitate",
        "spital", "uznemums", "biedriba", "imone", "asociacija", "ettevote", "uhing", "kumpanija", "cuideachta", "eagraiocht",
    }.ToFrozenSet(StringComparer.Ordinal);

    // Company words that are also surnames: "Henry Law", "John Church". They do not make a short "Given Family" text a company.
    private static readonly FrozenSet<string> SurnameCompanyWords = new[]
    {
        "law", "church", "trust", "union", "club", "market", "press", "bank", "school", "college", "son", "sons", "design", "studio",
    }.ToFrozenSet(StringComparer.Ordinal);

    // Formal name: its nicknames and spelling variants. Names whose short forms belong to more than one name (Al, Chris, Sam, Pat, Alex,
    // Jo, Terry, Max, Jan) are left out on purpose: the key must not join different people.
    private static readonly string[] NicknameSource =
    [
        "robert:bob,bobby,rob,robbie,robby", "william:bill,billy,will,willy,willie,liam", "richard:rick,ricky,dick,rich,richie",
        "james:jim,jimmy,jamie,jimbo", "john:jack,johnny,jonny", "michael:mike,mikey,mick,mickey",
        "thomas:tom,tommy", "charles:charlie,chuck,chas", "joseph:joe,joey",
        "david:dave,davey,davy", "daniel:dan,danny", "matthew:matt,matty", "anthony:tony,antony",
        "andrew:andy,drew", "christopher:topher,kit", "stephen:steve,stevie,steven", "edward:ed,eddie,eddy,ted,ned",
        "theodore:theo", "kenneth:ken,kenny", "timothy:tim,timmy", "gregory:greg", "jeffrey:jeff", "benjamin:ben,benny,benji",
        "nicholas:nick,nicky", "patrick:paddy", "peter:pete", "raymond:ray", "ronald:ron,ronnie", "donald:don,donnie",
        "lawrence:larry,laurence", "francis:frank,frankie", "frederick:fred,freddie,freddy",
        "henry:harry,hank,hal", "alfred:alf,alfie", "walter:walt,wally", "leonard:lenny,len", "gerald:gerry",
        "douglas:doug", "philip:phil,phillip", "vincent:vince,vinny", "louis:lou", "russell:russ", "eugene:gene",
        "herbert:herb", "bernard:bernie", "nathan:nate",
        "elizabeth:liz,lizzie,beth,betty,betsy,eliza,libby,bessie", "katherine:kate,katie,kathy,kat,katy,kitty,catherine,kathryn,cathy",
        "margaret:maggie,meg,peggy,marge,margie", "jennifer:jen,jenny,jenn", "patricia:trish,tricia,patty", "deborah:deb,debbie,debra",
        "susan:sue,susie,suzy,suzie", "barbara:barb,barbie,babs", "rebecca:becky,becca", "jessica:jess,jessie", "victoria:vicky,vicki,tori",
        "abigail:abby,abbie", "christine:chrissy,christina", "dorothy:dot,dottie,dolly", "eleanor:ellie,nell", "jacqueline:jackie",
        "josephine:josie", "judith:judy", "virginia:ginny", "mary:molly,polly",

        // Short forms inside one language only: a Spanish "Jose" is not an English "Joseph", a German "Hans" not an English "John".
        "jose:pepe,pepito", "francisco:paco,pancho,curro", "manuel:manolo,manu", "jesus:chuy", "rafael:rafa", "ignacio:nacho",
        "enrique:quique", "guadalupe:lupe", "dolores:lola,lolita",
        "giuseppe:beppe,peppe", "giovanni:gianni", "francesco:franco", "luigi:gigi", "alessandro:sandro", "antonio:tonino",
        "johannes:hans,johann,hannes", "friedrich:fritz", "heinrich:heinz", "wilhelm:willi",
    ];

    // Given names that are not formal names above, for telling "John & Jane" (two people) from "Johnson & Johnson" (a company).
    private static readonly string[] ExtraGivenNames =
    [
        "jane", "anna", "emma", "olivia", "sophia", "isabella", "mia", "charlotte", "amelia", "harper", "evelyn", "emily", "grace", "lily",
        "laura", "sarah", "linda", "karen", "nancy", "lisa", "betty", "helen", "sandra", "donna", "carol", "ruth", "sharon", "michelle",
        "kimberly", "amy", "angela", "melissa", "brenda", "pamela", "nicole", "samantha", "katherine", "christina", "maria", "marie",
        "paul", "mark", "george", "steven", "brian", "kevin", "jason", "scott", "eric", "jacob", "ryan", "adam", "luke", "oliver", "noah",
        "liam", "lucas", "hugo", "pierre", "jacques", "luc", "marc", "sophie", "camille", "julie", "claire", "carlos", "luis", "miguel",
        "pedro", "pablo", "juan", "ana", "isabel", "carmen", "sofia", "lucia", "hans", "peter", "klaus", "jurgen", "anke", "petra", "sabine",
        "jan", "piet", "kees", "sanne", "lars", "erik", "olof", "ingrid", "sven", "mikael", "matti", "juha", "anna", "janusz", "krzysztof",
        "tomasz", "pavel", "martin", "petr", "jiri", "marko", "luka", "janos", "laszlo", "istvan", "andrei", "mihai", "ioana", "joao",
        "marco", "giulia", "chiara", "francesca", "paolo", "luca", "sean", "aoife", "niamh", "ciaran", "padraig", "seamus",
    ];

    // Second words of Spanish compound given names ("Juan Carlos", "Maria Jose"): when the middle word of three is one of these,
    // the first two words are the given name and only the last is the family name.
    private static readonly FrozenSet<string> CompoundGivenSecond = new[]
    {
        "jose", "maria", "carlos", "luis", "antonio", "manuel", "angel", "miguel", "francisco", "javier", "jesus", "juan", "pedro", "pablo",
        "ana", "isabel", "carmen", "guadalupe", "alberto", "eduardo", "fernando", "ramon", "rafael", "jorge", "ignacio", "andres", "alfonso",
        "victoria", "teresa", "dolores", "pilar", "elena", "paula", "lucia", "rosa", "cristina", "del", "de",
        "josep", "joan", "lluis", "xose", "mari", "anxo",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly (FrozenDictionary<string, string> ToFormal, FrozenSet<string> Given) NicknameData = BuildNicknames();
    private static readonly FrozenDictionary<string, string> NicknameToFormal = NicknameData.ToFormal;
    private static readonly FrozenSet<string> GivenNameSet = NicknameData.Given;

    private static FrozenDictionary<string, string[]> BuildTitles()
    {
        Dictionary<string, List<string>> map = new(StringComparer.Ordinal);
        foreach (string entry in TitleSource)
        {
            int bar = entry.IndexOf('|', StringComparison.Ordinal);
            string key = NameFolding.LettersAndDigits(entry.AsSpan(0, bar));
            if (!map.TryGetValue(key, out List<string>? langs))
            {
                langs = [];
                map[key] = langs;
            }

            foreach (string lang in entry[(bar + 1)..].Split(','))
            {
                if (!langs.Contains(lang))
                {
                    langs.Add(lang);
                }
            }
        }

        Dictionary<string, string[]> frozen = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<string>> pair in map)
        {
            frozen[pair.Key] = [.. pair.Value];
        }

        return frozen.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static (FrozenDictionary<string, string>, FrozenSet<string>) BuildNicknames()
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (string entry in NicknameSource)
        {
            int colon = entry.IndexOf(':', StringComparison.Ordinal);
            string formal = entry[..colon];
            names.Add(formal);
            foreach (string nick in entry[(colon + 1)..].Split(','))
            {
                names.Add(nick);

                // First entry wins, so a short form listed twice keeps its first (most common) meaning.
                map.TryAdd(nick, formal);
            }
        }

        foreach (string extra in ExtraGivenNames)
        {
            names.Add(extra);
        }

        return (map.ToFrozenDictionary(StringComparer.Ordinal), names.ToFrozenSet(StringComparer.Ordinal));
    }

    /// <summary>The languages a hint stands for: the language itself, or the languages of the country.</summary>
    internal static string[] Languages(NameHints hints)
    {
        if (!string.IsNullOrWhiteSpace(hints.Language))
        {
            return [hints.Language.Trim().ToLowerInvariant()];
        }

        return hints.Country.IsUnknown ? [] : CountryLanguages(hints.Country.Code);
    }

    private static string[] CountryLanguages(string country) => country switch
    {
        "AT" or "DE" or "LI" => ["de"],
        "CH" => ["de", "fr", "it"],
        "LU" => ["lb", "fr", "de"],
        "BE" => ["nl", "fr", "de"],
        "NL" => ["nl"],
        "FR" or "MC" or "GF" or "GP" or "MQ" or "YT" or "RE" or "BL" or "MF" or "PM" => ["fr"],
        "ES" => ["es", "ca", "eu", "gl"],
        "AD" => ["ca"],
        "MX" or "AR" or "CO" or "CL" or "PE" or "VE" or "EC" or "GT" or "CU" or "BO" or "DO" or "HN" or "PY" or "SV" or "NI" or "CR" or "PA" or "UY" or "PR" => ["es"],
        "PT" or "BR" => ["pt"],
        "IT" or "SM" or "VA" => ["it"],
        "SE" => ["sv"],
        "DK" => ["da"],
        "NO" => ["no"],
        "FI" => ["fi", "sv"],
        "IS" => ["is"],
        "PL" => ["pl"],
        "CZ" => ["cs"],
        "SK" => ["sk"],
        "SI" => ["sl"],
        "HR" => ["hr"],
        "HU" => ["hu"],
        "RO" => ["ro"],
        "LV" => ["lv"],
        "LT" => ["lt"],
        "EE" => ["et"],
        "MT" => ["mt", "en"],
        "IE" => ["en", "ga"],
        "CA" => ["en", "fr"],
        _ => ["en"],
    };

    /// <summary>Returns whether the languages include <paramref name="language"/>.</summary>
    internal static bool Has(string[] languages, string language) => Array.IndexOf(languages, language) >= 0;

    /// <summary>Returns whether the languages include Spanish or one of the languages of Spain, which put two family names in a name.</summary>
    internal static bool TwoFamilyNames(string[] languages) =>
        Has(languages, "es") || Has(languages, "ca") || Has(languages, "gl") || Has(languages, "eu");

    /// <summary>Returns whether the name order is family first without a comma.</summary>
    internal static bool FamilyFirst(string[] languages) => Has(languages, "hu");

    /// <summary>Returns whether casing restoration writes particles in lower case: true unless there is no hint, the only hint is English, or the language is Irish (whose "Ó" and "Ní" are capitals).</summary>
    internal static bool LowerParticles(string[] languages) =>
        languages.Length > 0 && !(languages.Length == 1 && languages[0] == "en") && !Has(languages, "ga");

    /// <summary>Returns whether the folded form is a title in the given languages.</summary>
    internal static bool IsTitle(string folded, string[] languages)
    {
        if (!Titles.TryGetValue(folded, out string[]? langs))
        {
            return false;
        }

        foreach (string l in langs)
        {
            if (l == "*" || Has(languages, l))
            {
                return true;
            }
        }

        return false;
    }

    // Titles written as several words: only these are looked up across word boundaries, so that initials ("M. S") never add up to a title ("Ms").
    private static readonly FrozenSet<string> MultiWordTitles = new[] { "deheer", "radcaprawny", "antuasal", "antathair", "antollamh", "antsiur" }.ToFrozenSet(StringComparer.Ordinal);

    internal static bool IsMultiWordTitle(string folded, string[] languages) => MultiWordTitles.Contains(folded) && IsTitle(folded, languages);

    internal static bool IsGenerational(string folded) => Generational.Contains(folded);

    internal static bool IsProfessional(string folded, bool writtenWithDotsOrAfterComma) =>
        Professional.Contains(folded) || (writtenWithDotsOrAfterComma && AmbiguousProfessional.Contains(folded));

    internal static bool IsParticle(string folded, string[] languages) =>
        Particles.Contains(folded) || (Has(languages, "ga") && IrishParticles.Contains(folded));

    internal static bool IsConnector(ReadOnlySpan<char> token, string folded, string[] languages)
    {
        if (token.SequenceEqual("&"))
        {
            return true;
        }

        if (token.Length == 1 && Array.IndexOf(SingleLetterConnectors, folded) >= 0)
        {
            if (char.IsLower(token[0]))
            {
                return true;
            }

            return NameFolding.IsAllUpper(token) && (Has(languages, "es") || Has(languages, "pt") || Has(languages, "ca"));
        }

        if (!Connectors.Contains(folded))
        {
            return false;
        }

        // "es" (Hungarian "es"), "en" (Dutch) and "ja" (Finnish, Estonian) are ordinary words or names elsewhere.
        return folded switch
        {
            "es" => Has(languages, "hu"),
            "en" => Has(languages, "nl"),
            "ja" => Has(languages, "fi") || Has(languages, "et"),
            "ir" => Has(languages, "lt"),
            "in" => Has(languages, "sl"),
            "si" => Has(languages, "ro"),
            "an" => Has(languages, "lb"),
            _ => true,
        };
    }

    internal static bool IsCompanyWord(string folded) => CompanyWordSet.Contains(folded);

    internal static bool IsSurnameCompanyWord(string folded) => SurnameCompanyWords.Contains(folded);

    internal static bool IsGivenName(string folded) => GivenNameSet.Contains(folded);

    internal static bool IsCompoundGivenSecond(string folded) => CompoundGivenSecond.Contains(folded);

    /// <summary>The formal name a given name or nickname stands for in the person key; the name itself when it has none.</summary>
    internal static string FormalName(string folded) => NicknameToFormal.TryGetValue(folded, out string? formal) ? formal : folded;

    /// <summary>The special letter casing of a title or suffix (letters only), or <see langword="null"/>.</summary>
    internal static string? SpecialCase(string folded) => SpecialCasing.TryGetValue(folded, out string? text) ? text : null;
}
