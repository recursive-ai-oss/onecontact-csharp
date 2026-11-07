using System.Collections.Frozen;

namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>
/// The word lists the name and organization rules lean on. Every list is ours, written for this reader: words of web page chrome that
/// look like names, words that introduce a name, the nouns that name a job, and the forms a legal name ends in. A job word is evidence
/// that the words next to it are a person; it is never read out as a field.
/// </summary>
internal static class NameVocabulary
{
    private static FrozenSet<string> Set(params string[] words) => words.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Words that begin a sentence or a phrase and are followed by a name; they are not part of it ("Call Mike Smith").</summary>
    public static readonly FrozenSet<string> Cues = Set(
        "please", "call", "calls", "contact", "contacts", "email", "e-mail", "mail", "text", "ask", "asked", "thanks", "thank", "hi", "hello", "hey", "dear",
        "attn", "attention", "regards", "sincerely", "cheers", "from", "meet", "tell", "see", "visit", "reach", "speak", "talk", "spoke", "talked",
        "with", "for", "to", "by", "via", "and", "or", "the", "a", "an", "our", "your", "my", "his", "her", "par", "best", "kind", "warm", "warmest", "yours", "truly", "faithfully", "respectfully", "also", "then",
        "send", "forward", "cc", "bcc", "re", "fwd", "note", "per", "thx", "ty", "greetings", "hola", "bonjour", "bonsoir", "salut");

    /// <summary>Words of page chrome, calendars, places and the like: a run holding one is not a person's or a company's name on its own.</summary>
    public static readonly FrozenSet<string> Stops = Set(
        "about", "us", "home", "privacy", "policy", "policies", "terms", "conditions", "service", "services", "product", "products", "career", "careers",
        "blog", "news", "faq", "faqs", "login", "log", "sign", "register", "read", "more", "learn", "shop", "cart", "search", "menu", "skip", "content",
        "main", "navigation", "copyright", "rights", "reserved", "all", "follow", "subscribe", "newsletter", "phone", "tel", "telephone", "fax", "mobile",
        "office", "hours", "open", "closed", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday", "january", "february",
        "march", "july", "september", "october", "november", "december", "free", "quote", "get", "started", "now",
        "today", "new", "view", "click", "here", "back", "next", "previous", "share", "tweet", "like", "facebook", "twitter", "linkedin",
        "instagram", "youtube", "pinterest", "team", "staff", "directory", "location", "locations", "address", "directions", "map", "web", "site",
        "website", "page", "sitemap", "accessibility", "cookie", "cookies", "settings", "support", "help", "customer", "customers", "client", "clients",
        "resources", "events", "gallery", "reviews", "review", "testimonials", "pricing", "plans", "request", "appointment", "appointments", "schedule",
        "book", "order", "buy", "account", "welcome", "you", "street", "avenue", "road", "suite", "floor", "drive", "lane", "boulevard", "highway",
        "box", "unit", "apt", "department", "dept", "division", "branch", "headquarters", "hq", "general", "information", "info", "inquiries", "inquiry",
        "sales", "billing", "payment", "payments", "shipping", "returns", "delivery", "warranty", "store", "stores", "online", "digital", "global",
        "international", "national", "american", "canadian", "united", "states", "canada", "america", "usa", 
        "city", "county", "state", "province", "north", "south", "east", "west", "downtown", 
        "view", 
        "center", "centre", "mall", "airport", "station", "school", "university", "college", "hospital", "church", "hotel", "restaurant", "cafe",
        "pizza", "grill", "bar", "market", "bank", "insurance", "realty", "estate", "homes", "properties", "property", "mortgage", "loan",
        "loans", "auto", "car", "cars", "truck", "trucks", "repair", "cleaning", "plumbing", "heating", "cooling", "roofing", "electric", "electrical",
        "dental", "medical", "health", "care", "clinic", "legal", "law", "attorney", "attorneys", "lawyer", "lawyers", "firm", "group", "company",
        "companies", "incorporated", "limited", "corporation", "associates", "partners", "solutions", "systems", "technologies", "technology",
        "consulting", "management", "marketing", "design", "studio", "agency", "foundation", "institute", "association", "society", "club",
        "just", "only", "very", "such", "many", "most", "some", "any", "each", "every", "other", "another",
        "this", "that", "these", "those", "there", "their", "they", "them", "we", "our", "ours", "it", "its", "is", "are", "was", "were", "be",
        "been", "have", "has", "had", "do", "does", "did", "would", "could", "should", "if", "when", "where", "what", "which", "who",
        "how", "why", "not", "no", "yes", "ok", "okay", "but", "so", "as", "at", "in", "on", "of", "off", "up", "down", "out", "over", "under",
        "again", "still", "even", "also", "too", "however", "thanks", "sorry", "please", "unfortunately", "highly",
        "recommend", "recommended", "reviewed", "posted", "updated", "published", "written", "sent", "received", "left", "called", "emailed",
        "sir", "madam", "everyone", "folks", "friends", "guys", "neighbor", "st", "strt", "rd", "ave", "av", "ct", "court", "circle", "cir", "cres", "crescent", "ln", "blvd", "boul", "hwy", "pkwy", "parkway", "trl", "trail", "terrace", "ter", "place", "pl", "way", "square", "sq", "loop", "chemin", "rue", "rang", "route", "rte", "concession", "calle", "carrera", "camino", "paseo", "sw", "se", "nw", "ne", "hotline", "accounts", "payable", "receivable", "payroll", "courriel", "adresse", "envoyez", "nous", "ligne", "téléphone", "tél", "sans", "frais", "bureau", "accueil", "soutien", "site", "internet", "web", "correo", "dirección", "teléfono", "telefon", "anschrift", "kontakt", "zentrale", "direct", "main", "helpline", "emergency", "reception", "front", "desk", "booking", "bookings", "reservations", "local", "nationwide", "toll", "line", "hotel", "voicemail", "message", "messages", "follow-up", "followup", "reminder", "notes", "note", "details", "description", "summary", "total",
        "price", "cost", "fee", "fees", "rate", "rates", "tax", "discount", "sale", "offer", "offers", "deal", "deals", "coupon", "promo", "code");

    /// <summary>Words that make a run of capitalized words the name of a business or an organization ("Soesbe Excavating", "Pioneer Investments").</summary>
    public static readonly FrozenSet<string> Industry = Set(
        "excavating", "excavation", "investments", "investment", "trading", "derivatives", "etf", "funds", "fund", "capital", "mezz", "reit", "partners",
        "holdings", "holding", "contractors", "contracting", "contractor", "landscaping", "landscapes", "roofing", "painting", "paving", "towing", "moving",
        "storage", "rentals", "rental", "repair", "repairs", "auto", "automotive", "motors", "tire", "tires", "pizza", "dental", "dentistry", "orthodontics",
        "chiropractic", "physiotherapy", "pharmacy", "veterinary", "insurance", "financial", "wealth", "advisors", "advisory", "realty", "mortgage", "title",
        "escrow", "lending", "properties", "development", "developments", "builders", "building", "homes", "remodeling", "renovations", "flooring",
        "cabinets", "windows", "glass", "electric", "electrical", "hvac", "heating", "cooling", "mechanical", "welding", "machine", "manufacturing",
        "fabrication", "supply", "supplies", "distribution", "wholesale", "imports", "exports", "trucking", "freight", "courier", "taxi", "travel",
        "tours", "hotel", "inn", "motel", "resort", "lodge", "spa", "salon", "barbershop", "fitness", "gym", "yoga", "studio", "studios", "school",
        "academy", "daycare", "childcare", "learning", "tutoring", "church", "ministries", "foundation", "society", "association", "council", "club",
        "league", "institute", "laboratories", "labs", "clinic", "medical", "wellness", "therapy", "counseling", "services", "solutions", "systems",
        "technologies", "software", "networks", "communications", "media", "productions", "entertainment", "photography", "printing", "graphics",
        "signs", "gallery", "bookstore", "bakery", "cafe", "coffee", "grill", "kitchen", "catering", "foods", "farms", "nursery", "gardens", "florist",
        "plumbing", "cleaning", "law", "offices", "attorneys", "lawyers", "accountants", "architects", "engineering", "engineers", "surveying",
        "consulting", "consultants", "associates", "group", "enterprises", "industries", "international", "logistics", "transport", "construction",
        "ventures", "company", "corporation", "incorporated", "limited", "brothers", "sons", "bros", "pllc", "hospital", "university", "college");

    /// <summary>Provinces, states and cities that are not also given names: a run holding one is a place.</summary>
    public static readonly FrozenSet<string> Places = Set(
        "quebec", "québec", "ontario", "alberta", "manitoba", "saskatchewan", "nova", "scotia", "brunswick", "newfoundland", "labrador", "yukon",
        "nunavut", "columbia", "laval", "montreal", "montréal", "toronto", "ottawa", "vancouver", "calgary", "edmonton", "winnipeg", "mississauga",
        "brampton", "hamilton", "gatineau", "longueuil", "sherbrooke", "alabama", "alaska", "arizona", "arkansas", "california", "colorado",
        "connecticut", "delaware", "florida", "hawaii", "idaho", "illinois", "iowa", "kansas", "kentucky", "louisiana", "maine", "maryland",
        "massachusetts", "michigan", "minnesota", "mississippi", "missouri", "nebraska", "nevada", "hampshire", "jersey", "mexico", "ohio", "oklahoma",
        "oregon", "pennsylvania", "rhode", "tennessee", "texas", "utah", "vermont", "washington", "wisconsin", "wyoming", "houston", "dallas",
        "chicago", "phoenix", "denver", "seattle", "boston", "atlanta", "miami", "detroit", "brooklyn", "manhattan", "bronx", "philadelphia",
        "baltimore", "cleveland", "pittsburgh", "cincinnati", "milwaukee", "minneapolis", "nashville", "memphis", "louisville", "tampa", "sacramento",
        "oakland", "honolulu", "anchorage", "albuquerque", "tucson", "omaha", "tulsa", "raleigh", "richmond", "buffalo", "rochester", "syracuse",
        "albany", "newark", "orlando", "jacksonville", "quebec", "island", "territories", "territory", "northwest", "prince");

    /// <summary>Two-letter codes of the states and provinces: a run that ends in one is the end of an address.</summary>
    public static readonly FrozenSet<string> RegionCodes = Set(
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT",
        "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY", "DC", "PR",
        "AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT");

    /// <summary>Nouns that name a job or a role. They tell a reader that the name beside them is a person's.</summary>
    public static readonly FrozenSet<string> Roles = Set(
        "manager", "director", "president", "owner", "ceo", "cfo", "cto", "coo", "cio", "cmo", "founder", "cofounder", "co-founder", "partner", "associate",
        "agent", "broker", "realtor", "attorney", "lawyer", "counsel", "advisor", "adviser", "consultant", "engineer", "developer", "designer",
        "analyst", "specialist", "coordinator", "assistant", "secretary", "receptionist", "representative", "rep", "supervisor", "officer",
        "administrator", "executive", "principal", "dentist", "doctor", "physician", "surgeon", "nurse", "therapist", "hygienist", "technician",
        "mechanic", "electrician", "plumber", "accountant", "cpa", "paralegal", "stylist", "instructor", "teacher", "professor", "dean", "chair",
        "chairman", "chairwoman", "chairperson", "treasurer", "clerk", "controller", "vp", "svp", "evp", "avp", "chief", "head", "lead", "mgr",
        "dir", "pres", "esquire", "esq", "pharmacist", "veterinarian", "vet", "optometrist", "chiropractor", "trainer", "coach", "realty", "banker",
        "underwriter", "auditor", "architect", "contractor", "estimator", "dispatcher", "operator", "buyer", "planner", "editor", "reporter",
        "publisher", "producer", "photographer", "writer", "author", "librarian", "pastor", "minister", "reverend", "rabbi", "imam", "chaplain",
        "sheriff", "mayor", "councilman", "councilwoman", "councillor", "senator", "representative", "commissioner", "superintendent", "principal",
        "intern", "apprentice", "foreman", "owner-operator", "proprietor", "volunteer", "trustee", "administrator",
        "comptable", "notaire", "avocat", "avocate", "propri\u00e9taire", "proprietaire", "technicien", "technicienne", "responsable", "directeur", "directrice",
        "pr\u00e9sident", "pr\u00e9sidente", "g\u00e9rant", "g\u00e9rante", "conseiller", "conseill\u00e8re", "courtier", "courti\u00e8re", "adjoint", "adjointe",
        "secr\u00e9taire", "r\u00e9ceptionniste", "repr\u00e9sentant", "repr\u00e9sentante", "infirmier", "infirmi\u00e8re", "dentiste", "m\u00e9decin", "pharmacien",
        "pharmacienne", "ing\u00e9nieur", "architecte", "coordonnateur", "coordonnatrice", "associ\u00e9", "associ\u00e9e", "fondateur", "fondatrice", "dirigeant",
        "vendeur", "vendeuse", "gerente", "directora", "abogado", "abogada", "contador", "presidente", "due\u00f1o", "propietario", "secretaria",
        "ingeniero", "m\u00e9dico", "vendedor", "asistente", "coordinador", "coordinadora", "representante", "arquitecto", "administrador", "administradora",
        "hygi\u00e9niste", "denturologiste", "optom\u00e9triste", "physioth\u00e9rapeute", "psychologue", "estimateur", "inspecteur",
        "d\u00e9veloppeur", "analyste", "consultante", "gestionnaire", "chef", "superviseur", "agente", "cuisinier", "m\u00e9canicien",
        "bookkeeper", "recruiter", "stylists", "barber", "esthetician", "notary", "appraiser", "inspector", "surveyor", "adjuster", "agent", "ae", "bdr", "sdr");

    /// <summary>Small words inside a job title ("Vice President of Sales", "Responsable du service").</summary>
    public static readonly FrozenSet<string> JobConnectors = Set(
        "of", "and", "the", "for", "to", "in", "at", "du", "de", "des", "la", "le", "les", "et", "\u00e0", "au", "aux", "en", "y", "e", "da", "do", "del", "della", "di", "und", "der", "die", "f\u00fcr", "&", "d", "l");

    /// <summary>The first word of a job title that is not itself a job noun ("Senior Sales Associate", "Vice President").</summary>
    public static readonly FrozenSet<string> RoleLeads = Set(
        "senior", "sr", "junior", "jr", "assistant", "associate", "executive", "chief", "head", "vice", "lead", "general", "managing", "regional",
        "account", "sales", "customer", "office", "project", "product", "marketing", "business", "licensed", "registered", "certified", "independent",
        "co", "deputy", "acting", "interim", "global", "district", "area", "branch", "store", "operations", "finance", "financial", "human", "it",
        "technical", "client", "member", "staff", "principal", "managing", "founding", "senior-level", "student", "clinical", "medical", "dental",
        "legal", "real", "estate", "mortgage", "loan", "insurance", "service", "services", "support", "relations", "development", "program",
        "community", "events", "creative", "art", "technology", "software", "systems", "network", "data", "quality", "safety", "facilities",
        "owner", "partner", "founder", "of", "and", "&", "the", "for", "to", "in", "at");

    /// <summary>Titles that stand in front of a person's name.</summary>
    public static readonly FrozenSet<string> Honorifics = Set(
        "mr", "mrs", "ms", "miss", "mx", "dr", "prof", "professor", "rev", "fr", "sir", "dame", "hon", "judge", "sgt", "capt", "lt", "col", "maj", "gen", "sen", "rep",
        "mme", "mlle", "herr", "frau", "sr", "sra", "srta", "don", "dona");

    /// <summary>Generational and professional endings written after a name.</summary>
    public static readonly FrozenSet<string> NameEndings = Set(
        "jr", "sr", "ii", "iii", "iv", "phd", "dds", "dmd", "esq", "cpa", "rn", "dvm", "mba", "cfp", "chfc", "aia", "leed");

    /// <summary>Lower-case words that sit inside a family name.</summary>
    public static readonly FrozenSet<string> Particles = Set(
        "van", "von", "de", "der", "den", "da", "di", "du", "la", "le", "del", "della", "bin", "al", "dos", "das", "ten", "ter", "el", "st", "y", "ibn", "ap", "af", "zu", "do", "e");

    /// <summary>Lines that close a note, in front of the writer's name.</summary>
    public static readonly FrozenSet<string> Closings = Set(
        "regards", "best regards", "kind regards", "warm regards", "warmest regards", "best wishes", "best", "thanks", "thank you", "many thanks", "thanks again",
        "sincerely", "yours sincerely", "yours truly", "yours faithfully", "respectfully", "cordially", "cheers", "talk soon", "take care", "with thanks",
        "thanks in advance", "sincerely yours", "all the best", "very truly yours", "thank you very much", "have a great day", "blessings", "gracias",
        "cordialement", "salutations", "amicalement", "mit freundlichen gr\u00fc\u00dfen", "saludos", "atentamente");

    /// <summary>The forms a legal name ends in, as the people who write company names write them (the core parser has the last word).</summary>
    public static readonly FrozenSet<string> LegalForms = Set(
        "inc", "incorporated", "llc", "llp", "lp", "ltd", "limited", "corp", "corporation", "co", "company", "plc", "gmbh", "ag", "sa", "sarl", "srl",
        "spa", "bv", "nv", "ab", "as", "oy", "pty", "pc", "pllc", "ltee", "lt\u00e9e", "ulc", "lllp", "pa", "sas", "sasu", "eurl", "kg", "ohg", "ug", "se",
        "sl", "slu", "sp", "zoo", "kft", "sro", "doo", "aps", "a/s", "bhd", "pte", "pvt", "nl", "cv", "vof", "mbh", "gbr", "eirl", "spol");

    /// <summary>Short forms that are ordinary words in lower case or ambiguous in title case: only the upper-case spelling counts.</summary>
    public static readonly FrozenSet<string> LegalFormsUpperOnly = Set("ag", "sa", "ab", "as", "oy", "nv", "bv", "lp", "pc", "pa", "se", "sl", "sp", "kg", "ug", "co", "nl", "cv", "sas");

    /// <summary>Words that, as the last word before a legal form is meant, belong to the ending itself ("Pty" before "Ltd").</summary>
    public static readonly FrozenSet<string> Connectors = Set("of", "and", "&", "the", "de", "du", "la", "le", "des", "von", "van", "der", "y", "et", "for", "n");
}
