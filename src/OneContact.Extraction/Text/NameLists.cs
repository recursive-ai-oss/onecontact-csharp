using System.Collections.Frozen;

namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>
/// The common given names and family names (US Census 2010 and SSA lists, public domain; see <c>NameListData</c>) beside the core
/// parser's own given names. They are evidence for the free-text name rules: a run of capitalized words that begins with a given name
/// or ends in a family name is more likely a person than one that does neither. A word in them is never a name on its own.
/// </summary>
internal static class NameLists
{
    private static FrozenSet<string> Words(string block, int take) =>
        block.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Take(take).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The commonest US given names.</summary>
    public static readonly FrozenSet<string> Given = Words(NameListData.GivenNames, int.MaxValue);

    /// <summary>The few hundred commonest given names, which a bare "Given Word" pair may rest on.</summary>
    public static readonly FrozenSet<string> StrongGiven = Words(NameListData.GivenNames, 600);

    /// <summary>The commonest family names.</summary>
    public static readonly FrozenSet<string> Family = Words(NameListData.FamilyNames, int.MaxValue);

    /// <summary>Given names that are also everyday words; they count only with a cue (a greeting or "spoke with") in front of them.</summary>
    public static readonly FrozenSet<string> AmbiguousGiven = new[]
    {
        "will", "bill", "mark", "pat", "sue", "rich", "frank", "max", "may", "june", "april", "rose", "grace", "hope", "joy", "faith", "don", "art",
        "ray", "jack", "dawn", "lane", "sandy", "penny", "ginger", "holly", "iris", "olive", "jan", "gene", "bob", "chuck", "guy", "earl", "pearl",
        "sky", "summer", "autumn", "winter", "dean", "drew", "ken", "kit", "rob", "ted", "van", "wade", "cliff", "clay", "grant", "heath", "glen",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Given names that are mostly an everyday word when they begin a capitalized phrase ("Will Call", "Rose Hill").</summary>
    public static readonly FrozenSet<string> WordGiven = new[]
    {
        "will", "rich", "may", "june", "april", "rose", "hope", "joy", "faith", "art", "dawn", "lane", "sandy", "penny", "ginger", "holly", "iris", "olive", "pearl",
        "sky", "summer", "autumn", "winter", "grant", "clay", "cliff", "heath", "glen", "wade", "guy", "earl", "kit", "van", "bill", "dean", "drew", "grace",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Words that follow a person's name when a sentence says what the person did.</summary>
    public static readonly FrozenSet<string> PersonVerbs = new[]
    {
        "said", "says", "told", "tells", "asked", "asks", "answered", "answers", "replied", "responded", "wrote", "writes", "called", "calls", "emailed",
        "emails", "texted", "texts", "phoned", "messaged", "mentioned", "explained", "confirmed", "helped", "helps", "assisted", "showed", "met", "visited",
        "stopped", "came", "went", "joined", "arrived", "handled", "handles", "returned", "was", "is", "will", "has", "had", "offered", "recommended",
        "suggested", "promised", "needs", "wants", "thinks", "knows",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Interjections and openers that stand before a comma and a name ("Sure, Jim"): "Last, First" does not apply to them.</summary>
    public static readonly FrozenSet<string> Openers = new[]
    {
        "sure", "well", "great", "okay", "ok", "yes", "no", "right", "good", "perfect", "awesome", "nice", "wow", "oh", "hey", "hi", "hello", "thanks",
        "sorry", "exactly", "absolutely", "cheers", "welcome", "congrats", "congratulations", "alright", "fine", "cool", "thank", "bye", "goodbye",
        "morning", "afternoon", "evening", "night", "indeed", "agreed", "yep", "yeah", "nope", "please", "again", "also", "now", "then", "so", "anyway",
        "actually", "honestly", "meanwhile", "finally", "first", "second", "third", "next", "last", "later", "today", "tomorrow", "yesterday",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Words before a colon that introduce a person ("From:", "Attendees:", "Ref:"), folded.</summary>
    public static readonly FrozenSet<string> HeaderLabels = new[]
    {
        "from", "to", "cc", "bcc", "replyto", "author", "by", "attn", "attention", "owner", "customer", "patient", "client",
        "caller", "signed", "sender", "recipient", "speaker", "host", "presenter", "attendees", "attendee", "present", "participants", "team", "guests", "members",
        "staff", "ref", "referral", "referredby", "assignedto", "handler", "agent", "rep", "representative", "advisor", "technician", "driver", "nurse", "doctor",
        "physician", "therapist", "tenant", "resident", "buyer", "seller", "applicant", "witness", "reporter", "requester", "preparedby", "submittedby", "postedby",
        "writtenby", "salesrep", "pointofcontact", "poc", "spokewith", "metwith", "withsender",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Words that say what a person is to the writer; they stand in front of the name and are not part of it.</summary>
    public static readonly FrozenSet<string> FrontLabels = new[]
    {
        "customer", "client", "patient", "caller", "guest", "member", "tenant", "resident", "buyer", "seller", "vendor", "contact", "name", "rep", "agent", "attendee",
        "borrower", "applicant", "insured", "sender", "recipient", "requester", "witness", "spoke", "talked",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Words that end a line about a person without being part of the name.</summary>
    public static readonly FrozenSet<string> TrailWords = new[] { "re", "fwd", "fw", "ref", "cc", "att", "attn", "regarding", "about" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Small words that take a person as their object, which count when the name is also one the lists know.</summary>
    public static readonly FrozenSet<string> WeakCues = new[] { "to", "with", "by", "for", "from", "per", "via", "and", "thanks", "of", "told", "tell", "asked", "ask", "see", "saw", "met" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Verbs of answering and writing to the writer, which take a person as their subject and rarely a business.</summary>
    public static readonly FrozenSet<string> SpeechVerbs = new[]
    {
        "answered", "replied", "responded", "wrote", "called", "emailed", "texted", "phoned", "messaged",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Words of time that are not a family name.</summary>
    public static readonly FrozenSet<string> TimeWords = new[]
    {
        "today", "tomorrow", "yesterday", "tonight", "later", "again", "now", "soon", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
        "morning", "afternoon", "evening", "week", "month", "year", "about", "regarding", "when", "after", "before", "because", "since", "until", "while", "then",
        "and", "but", "the", "for", "that", "this", "with", "from", "just", "also", "too", "please", "thanks", "asap", "etc",
    }.ToFrozenSet(StringComparer.Ordinal);
}
