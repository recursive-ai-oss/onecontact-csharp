using System.Globalization;
using System.Text;

namespace OneContact.Data.Blocks;

/// <summary>
/// The page around a contact block: navigation, headings, product and brand names, prose, testimonials, news, bylines, forms, cookie
/// banners and legal lines. None of it is labelled, so a model trained on it learns that a capitalised phrase, a person's name or a
/// number next to a street-like word is not a contact field just because it looks like one. Some lines are deliberate look-alikes:
/// people and companies that are mentioned but not offered as contacts, places named in prose, product names that read like businesses.
/// All words and templates here are written for this tool (no third-party text). The held-out share of every list (<see cref="Held"/>)
/// is used only by dev blocks, so dev measures chrome the training blocks never contained.
/// </summary>
internal sealed class Chrome
{
    private readonly Pools _pools;

    /// <summary>Pages of real visible text with no contact data on them, by split and kind ("web" or "ugc"). Empty unless a crawl folder is given.</summary>
    private readonly Dictionary<(string Split, string Kind), List<string[]>> _crawl = [];

    internal Chrome(Pools pools, string? crawlDir = null)
    {
        _pools = pools;
        if (crawlDir is null)
        {
            return;
        }

        foreach (string kind in new[] { "web", "ugc" })
        {
            string path = Path.Combine(crawlDir, kind + ".jsonl");
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (string line in File.ReadLines(path))
            {
                using System.Text.Json.JsonDocument j = System.Text.Json.JsonDocument.Parse(line);
                string host = j.RootElement.GetProperty("host").GetString()!;
                string[] lines = [.. j.RootElement.GetProperty("lines").EnumerateArray().Select(e => e.GetString()!)];
                // The split is fixed by the host, so no site is in both.
                string split = Prng.Hash(host, 0x63726177) % 100 < 90 ? "train" : "dev";
                if (!_crawl.TryGetValue((split, kind), out List<string[]>? l))
                {
                    _crawl[(split, kind)] = l = [];
                }

                l.Add(lines);
            }
        }
    }

    internal int CrawlPages => _crawl.Values.Sum(l => l.Count);

    /// <summary>Adds a few consecutive lines of a real page, when crawl pages are loaded.</summary>
    private bool RealLines(Doc d, string split, string kind)
    {
        if (!_crawl.TryGetValue((split, kind), out List<string[]>? pages) || pages.Count == 0)
        {
            return false;
        }

        string[] page = pages[d.R.Int(pages.Count)];
        int n = 2 + d.R.Int(7);
        int from = d.R.Int(Math.Max(1, page.Length - n));
        for (int i = from; i < Math.Min(page.Length, from + n); i++)
        {
            d.Line(page[i]);
        }

        return true;
    }

    /// <summary>Train blocks use the items whose index is not a multiple of five, dev blocks the others.</summary>
    private static bool Held(string split, int i) => split == "dev" ? i % 5 == 0 : i % 5 != 0;

    private static T Pick<T>(Prng r, string split, T[] items)
    {
        for (int tries = 0; tries < 64; tries++)
        {
            int i = r.Int(items.Length);
            if (Held(split, i))
            {
                return items[i];
            }
        }

        return items[r.Int(items.Length)];
    }

    // ---------------------------------------------------------------- the pools behind the words

    private static readonly string[] CitiesEn = ["Portland", "Austin", "Denver", "Columbus", "Raleigh", "Boise", "Tucson", "Omaha", "Albany", "Spokane", "Tampa", "Dayton", "Fresno", "Madison", "Richmond", "Savannah", "Toledo", "Reno", "Salem", "Akron", "Toronto", "Calgary", "Halifax", "Winnipeg", "Regina", "Kelowna", "Victoria", "London", "Hamilton", "Moncton", "Burlington", "Springfield", "Lancaster", "Fairfield", "Georgetown", "Franklin", "Clinton", "Greenville", "Bristol", "Oakland"];
    private static readonly string[] CitiesFr = ["Montréal", "Québec", "Laval", "Gatineau", "Sherbrooke", "Trois-Rivières", "Saguenay", "Lévis", "Longueuil", "Drummondville", "Granby", "Saint-Jérôme", "Rimouski", "Moncton", "Ottawa", "Toronto"];
    private static readonly string[] Regions = ["Ontario", "Texas", "Oregon", "Alberta", "Ohio", "Nevada", "Manitoba", "Colorado", "Florida", "Georgia", "Maine", "Nova Scotia", "British Columbia", "the Midwest", "the Pacific Northwest", "the Maritimes", "New England", "the Gulf Coast"];
    private static readonly string[] BrandHead = ["Bright", "North", "Summit", "Blue", "Iron", "Oak", "Maple", "Silver", "River", "Stone", "Harbor", "Red", "Pine", "Eagle", "Cedar", "Pioneer", "Clear", "Prairie", "Lake", "Sun", "Granite", "Copper", "Falcon", "Timber", "Willow", "Anchor", "Beacon", "Atlas", "Orbit", "Vertex"];
    private static readonly string[] BrandTail = ["path", "wind", "line", "works", "field", "stream", "point", "craft", "gate", "mark", "wood", "view", "forge", "bridge", "peak", "mill", "yard", "ridge", "side", "hub"];
    private static readonly string[] BrandKind = ["Outfitters", "Labs", "Studio", "Systems", "Foods", "Media", "Group", "Partners", "Supply", "Brewing", "Realty", "Dental", "Fitness", "Garage", "Collective", "Design", "Bakery", "Analytics", "Cycles", "Farms"];
    private static readonly string[] ProductAdj = ["Premium", "Classic", "Ultra", "Compact", "Heavy-Duty", "Eco", "Smart", "Pro", "Deluxe", "Rustic", "Portable", "Cordless", "Organic", "Vintage", "Waterproof", "Folding", "Adjustable", "Insulated", "Ergonomic", "Wireless"];
    private static readonly string[] ProductMat = ["Cedar", "Steel", "Oak", "Copper", "Canvas", "Bamboo", "Leather", "Ceramic", "Merino", "Granite", "Nylon", "Brass", "Hemp", "Walnut", "Titanium"];
    private static readonly string[] ProductItem = ["Jacket", "Drill", "Mug", "Backpack", "Lamp", "Cutting Board", "Kettle", "Boots", "Planter", "Tent", "Blender", "Desk", "Toolbox", "Sofa", "Helmet", "Router", "Speaker", "Bike Rack", "Water Bottle", "Door Mat"];
    private static readonly string[] Services = ["Residential Roofing", "Emergency Repairs", "Free Estimates", "Commercial Cleaning", "Tax Preparation", "Estate Planning", "Landscape Design", "Kitchen Remodeling", "Pest Control", "Family Dentistry", "Auto Detailing", "Payroll Services", "Home Inspections", "Pet Boarding", "Wedding Photography", "IT Support", "Plumbing & Heating", "Window Installation", "Personal Training", "Bookkeeping"];
    private static readonly string[] Topics = ["home insurance", "kitchen renovation", "tax planning", "winter tires", "garden tools", "small business loans", "pet care", "solar panels", "online banking", "first aid training", "trail maintenance", "car seats", "retirement savings", "water heaters", "spring cleaning", "remote work", "meal prep", "energy audits"];
    private static readonly string[] Months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private static readonly string[] Quotes =
    [
        "They finished the job a week early and left the place spotless.",
        "Friendly, fast and fair. I would not hesitate to recommend them.",
        "The best service we have had in twenty years of running the shop.",
        "Everything arrived on time and exactly as described.",
        "Our whole family loves it. We use it every single day.",
        "Honest advice and no pressure. That is rare these days.",
        "Five stars. The team answered every question patiently.",
        "I was nervous about the move, but they made it painless.",
        "Quality work at a price that made sense for our budget.",
        "We have been customers for years and have never been disappointed.",
    ];

    private static readonly string[] Sentences =
    [
        "{O} has been serving customers in {C} and {C2} since {Y}.",
        "{P}, who founded {O}, said the new {I} line would reach stores in {M}.",
        "According to {P}, demand for {I} in {R} has doubled over the past two years.",
        "Our guide to {T} covers the basics and a few things most people miss.",
        "Last {M}, {O} announced a partnership with {O2} to expand across {R}.",
        "If you are thinking about {T}, start by comparing at least three options.",
        "The {I} is available in {N} sizes and ships free on orders over ${N}0.",
        "{P} and {P2} will lead the workshop on {T} at the {C} community centre.",
        "Visitors to {C} can also stop by the market on the first Saturday of every month.",
        "In {Y}, the town of {C} voted to invest in {T} for local families.",
        "Reviewers praised the {I} for its battery life, build quality and price.",
        "We sat down with {P} to talk about how {T} is changing for small businesses.",
        "The {O} spring catalogue features over {N}00 new items, including the {I}.",
        "Traffic on Highway {N} was slow through {C} after an early morning collision.",
        "Roadwork on Main Street between {N}th Avenue and the river is expected to last until {M}.",
        "{O2} said revenue grew {N}% in the quarter, helped by sales of {I}.",
        "Thanks to everyone who came out to {C} last weekend; we raised ${N}00 for the food bank.",
        "Members can book the {I} online or ask at the front desk.",
        "Mayor {P} opened the new library in {C} on Tuesday morning.",
        "This article was written by {P} and reviewed by {P2} for accuracy.",
        "Before you buy a {I}, check the warranty and the return policy.",
        "The season opens in {M} with games in {C}, {C2} and {R}.",
    ];

    private static readonly string[] SentencesFr =
    [
        "{O} sert la clientèle de {C} et de {C2} depuis {Y}.",
        "Selon {P}, la demande pour {T} a doublé en deux ans.",
        "Notre guide sur {T} couvre l'essentiel et quelques détails souvent oubliés.",
        "En {M}, {O} a annoncé un partenariat avec {O2}.",
        "{P} et {P2} animeront l'atelier sur {T} au centre communautaire de {C}.",
        "Les visiteurs de {C} peuvent aussi passer au marché le premier samedi du mois.",
        "Le {I} est offert en {N} formats et la livraison est gratuite dès {N}0 $.",
        "Nous avons rencontré {P} pour parler de {T}.",
        "La circulation était lente sur la route {N} près de {C} ce matin.",
        "Merci à tous ceux qui sont venus à {C} la fin de semaine dernière.",
    ];

    private static readonly string[] Headlines =
    [
        "How to Choose the Right {I} for Your Home",
        "{N} Ways to Save on {T}",
        "Why {O} Is Expanding into {R}",
        "What You Need to Know About {T}",
        "{O} Named a Top Employer in {R}",
        "The Complete Guide to {T}",
        "{I}: Our Honest Review",
        "Spring Sale: Up to {N}0% Off",
        "New Arrivals This {M}",
        "Meet the Winners of the {C} Business Awards",
        "Frequently Asked Questions About {T}",
        "Community Spotlight: {C}",
        "Welcome to {O}",
        "Our Story",
        "Latest News from {C}",
    ];

    private static readonly string[] HeadlinesFr =
    [
        "Comment choisir le bon {I}",
        "{N} façons d'économiser sur {T}",
        "Pourquoi {O} s'installe à {C}",
        "Tout savoir sur {T}",
        "Le guide complet de {T}",
        "Soldes de printemps : jusqu'à {N}0 % de rabais",
        "Bienvenue chez {O}",
        "Notre histoire",
        "Dernières nouvelles de {C}",
    ];

    private static readonly string[] FormLabels = ["First name", "Last name", "Your name", "Email address *", "Phone number", "Company", "Message", "Subject", "Select a service", "How did you hear about us?", "Submit", "Send message", "Required fields are marked with an asterisk", "Search", "Enter your email", "Sign up", "Leave a comment", "Name *", "Website", "Post comment"];
    private static readonly string[] FormLabelsFr = ["Prénom", "Nom", "Votre nom", "Adresse courriel *", "Téléphone", "Entreprise", "Message", "Sujet", "Envoyer", "Recherche", "Inscrivez-vous", "Laisser un commentaire", "Les champs obligatoires sont marqués d'un astérisque"];
    private static readonly string[] Banners = ["We use cookies to improve your experience. By continuing to browse you accept our use of cookies.", "Accept all", "Reject all", "Manage preferences", "This site uses cookies. Learn more", "Got it", "Skip to main content", "Menu", "Close", "Share", "Tweet", "Pin it", "Print this page", "Read more", "Load more", "Add to cart", "Add to wishlist", "In stock", "Out of stock", "Free shipping", "Sort by", "Filter", "Next", "Previous", "Page 1 of 12", "Show more", "Back to results", "Compare", "Quick view", "Reviews", "Related products"];
    private static readonly string[] BannersFr = ["Nous utilisons des témoins pour améliorer votre expérience. En poursuivant, vous acceptez leur utilisation.", "Tout accepter", "Tout refuser", "Gérer les préférences", "Passer au contenu principal", "Menu", "Fermer", "Partager", "Lire la suite", "Voir plus", "Ajouter au panier", "En stock", "Livraison gratuite", "Trier par", "Suivant", "Précédent", "Produits associés"];
    // Lines that look like a fragment of a contact but are not one: roads and exits in news, service areas, places in sentences, file names with an
    // @ in them, social handles, ZIP lists. The model has to learn that a street word, a state code or an @ is not enough.
    private static readonly string[] LookAlikes =
    [
        "Highway {N}0 is closed between exit {N} and exit {N}2 until further notice.",
        "Take Interstate {N}5 north and follow the signs for {C}.",
        "Parking is available at Fifth and Main after 6 pm.",
        "Route {N}0 Diner: breakfast all day",
        "Free shipping to {C}, {S}",
        "Now serving {C}, {S} {Z} and nearby neighbourhoods",
        "Service area: {C}, {C2} and {R}",
        "Weather for {C}, {S}: {N}0°F and clear",
        "ZIP codes we deliver to: {Z}, {Z2}, {Z3}",
        "Gate {N}2, Terminal {N}",
        "Room {N}0{N}, Building {N}",
        "Meet at the trailhead on Mill Road at 8 am.",
        "{N}00 block of Elm Street",
        "Join us on {M} {N}{N} at the {C} Community Hall, doors open at 7 pm.",
        "Open {N} days a week in {C}, {S}",
        "logo@2x.png",
        "hero-banner@{N}x.jpg",
        "icon@{N}x.webp",
        "Follow @{B} on Instagram",
        "Tag us @{B} for a chance to win",
        "{B}.com",
        "Powered by {B}",
        "Download the {B} app",
        "Call for pricing",
        "Text STOP to unsubscribe",
        "Use code SAVE{N}0 at checkout",
        "Page {N} of {N}{N}",
        "Posted in {T} | Tagged {T} | {N} Comments",
    ];

    private static readonly string[] StateCodes = ["OR", "TX", "CO", "OH", "NC", "ID", "AZ", "NE", "NY", "WA", "FL", "WI", "VA", "GA", "NV", "ON", "AB", "NS", "MB", "SK", "BC", "QC"];

    private static readonly string[] Crumbs = ["Home", "Shop", "Products", "Blog", "Services", "News", "Resources", "Kitchen", "Outdoor", "Tools"];

    // ---------------------------------------------------------------- word makers

    private string Person(Prng r, string split, Loc loc)
    {
        List<PersonRec> l = Pool(_pools.Persons, split, loc);
        PersonRec p = l[r.Int(l.Count)];
        return p.Text;
    }

    private string Company(Prng r, string split, Loc loc)
    {
        List<CompanyRec> l = Pool(_pools.Companies, split, loc);
        CompanyRec c = l[r.Int(l.Count)];
        return c.Base;
    }

    private static List<T> Pool<T>(Dictionary<(string, Loc), List<T>> d, string split, Loc loc)
    {
        if (!d.TryGetValue((split, loc), out List<T>? l) || l.Count < 50)
        {
            l = d.Where(kv => kv.Key.Item1 == split).OrderByDescending(kv => kv.Value.Count).First().Value;
        }

        return l;
    }

    private static string City(Prng r, string split, Loc loc) => loc == Loc.CaFr ? Pick(r, split, CitiesFr) : Pick(r, split, CitiesEn);

    private string Brand(Prng r, string split, Loc loc)
    {
        // A made-up brand, a pool company's base name, or a "Brandname Kind" phrase: all read like a business and none is a contact.
        return r.Weighted([40, 35, 25]) switch
        {
            0 => Pick(r, split, BrandHead) + Pick(r, split, BrandTail),
            1 => Company(r, split, loc),
            _ => Pick(r, split, BrandHead) + Pick(r, split, BrandTail) + " " + Pick(r, split, BrandKind),
        };
    }

    private static string Item(Prng r, string split) =>
        r.Weighted([40, 30, 30]) switch
        {
            0 => Pick(r, split, ProductAdj) + " " + Pick(r, split, ProductItem),
            1 => Pick(r, split, ProductMat) + " " + Pick(r, split, ProductItem),
            _ => Pick(r, split, ProductAdj) + " " + Pick(r, split, ProductMat) + " " + Pick(r, split, ProductItem),
        };

    private string Fill(Prng r, string split, Loc loc, string template)
    {
        StringBuilder b = new();
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf('{', i);
            if (open < 0)
            {
                b.Append(template, i, template.Length - i);
                break;
            }

            b.Append(template, i, open - i);
            int close = template.IndexOf('}', open);
            string slot = template[(open + 1)..close];
            i = close + 1;
            b.Append(slot switch
            {
                "P" or "P2" => Person(r, split, loc),
                "O" or "O2" => Brand(r, split, loc),
                "C" or "C2" => City(r, split, loc),
                "R" => Pick(r, split, Regions),
                "I" => Item(r, split),
                "T" => Pick(r, split, Topics),
                "M" => loc == Loc.CaFr ? LexSet.Fr.Months[r.Int(12)] : Months[r.Int(12)],
                "Y" => (1985 + r.Int(40)).ToString(CultureInfo.InvariantCulture),
                "N" => (2 + r.Int(9)).ToString(CultureInfo.InvariantCulture),
                "S" => Pick(r, split, StateCodes),
                "Z" or "Z2" or "Z3" => (10000 + r.Int(89999)).ToString(CultureInfo.InvariantCulture),
                "B" => Pick(r, split, BrandHead).ToLowerInvariant() + Pick(r, split, BrandTail),
                _ => throw new InvalidOperationException("unknown slot " + slot),
            });
        }

        return b.ToString();
    }

    // ---------------------------------------------------------------- lines

    private string Sentence(Prng r, string split, Loc loc) =>
        Fill(r, split, loc, Pick(r, split, loc == Loc.CaFr ? SentencesFr : Sentences));

    private string Paragraph(Prng r, string split, Loc loc)
    {
        int n = 1 + r.Int(4);
        StringBuilder b = new();
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                b.Append(' ');
            }

            b.Append(Sentence(r, split, loc));
        }

        return b.ToString();
    }

    private string Headline(Prng r, string split, Loc loc) =>
        Fill(r, split, loc, Pick(r, split, loc == Loc.CaFr ? HeadlinesFr : Headlines));

    private static string Money(Prng r, Loc loc)
    {
        string v = (1 + r.Int(400)).ToString(CultureInfo.InvariantCulture) + (loc == Loc.CaFr ? "," : ".") + r.Int(100).ToString("D2", CultureInfo.InvariantCulture);
        return loc == Loc.CaFr ? v + " $" : loc == Loc.T2 ? v + " €" : "$" + v;
    }

    private static string DateText(Prng r, Loc loc)
    {
        LexSet lex = LexSet.Of(loc);
        int y = 2016 + r.Int(11);
        int day = 1 + r.Int(28);
        return loc == Loc.CaFr ? $"{day} {lex.Months[r.Int(12)]} {y}" : $"{lex.Months[r.Int(12)]} {day}, {y}";
    }

    /// <summary>Adds one chrome item (one to four lines) to the document.</summary>
    internal void Item(Doc d, string split, Loc loc)
    {
        Prng r = d.R;
        LexSet lex = LexSet.Of(loc);
        bool fr = loc == Loc.CaFr;
        if (_crawl.Count > 0 && r.Chance(0.5) && RealLines(d, split, d.Page ? "web" : "ugc"))
        {
            return;
        }

        switch (r.Weighted([10, 8, 22, 7, 8, 7, 5, 5, 6, 6, 4, 4, 4, 4, 14]))
        {
            case 0:
                for (int i = 0, n = 3 + r.Int(7); i < n; i++)
                {
                    d.Line(Pick(r, split, lex.Nav));
                }

                break;
            case 1:
                d.Line(Headline(r, split, loc));
                break;
            case 2:
                d.Line(Paragraph(r, split, loc));
                break;
            case 3:
                // A testimonial: the quote, then who said it. The person is a customer, not a contact.
                d.Line("\u201c" + Pick(r, split, Quotes) + "\u201d");
                d.Line(r.Chance(0.5) ? "\u2014 " + Person(r, split, loc) + ", " + City(r, split, loc) : Person(r, split, loc));
                break;
            case 4:
                // A product card.
                d.Line(Item(r, split));
                d.Line(Money(r, loc));
                d.Line(Pick(r, split, fr ? BannersFr : Banners));
                break;
            case 5:
                // A news list entry.
                d.Line(DateText(r, loc));
                d.Line(Fill(r, split, loc, Pick(r, split, fr ? HeadlinesFr : Headlines)));
                break;
            case 6:
                d.Line((fr ? "Par " : "By ") + Person(r, split, loc) + (r.Chance(0.6) ? " | " + DateText(r, loc) : ""));
                if (r.Chance(0.5))
                {
                    d.Line((1 + r.Int(40)).ToString(CultureInfo.InvariantCulture) + (fr ? " commentaires" : " comments"));
                }

                break;
            case 7:
                for (int i = 0, n = 2 + r.Int(5); i < n; i++)
                {
                    d.Line(Pick(r, split, fr ? FormLabelsFr : FormLabels));
                }

                break;
            case 8:
                d.Line(Pick(r, split, fr ? BannersFr : Banners));
                break;
            case 9:
                d.Line(fr
                    ? "Au service de " + City(r, split, loc) + ", " + City(r, split, loc) + " et des environs depuis " + (1985 + r.Int(40)).ToString(CultureInfo.InvariantCulture)
                    : "Proudly serving " + City(r, split, loc) + ", " + City(r, split, loc) + " and surrounding areas since " + (1985 + r.Int(40)).ToString(CultureInfo.InvariantCulture));
                break;
            case 10:
                for (int i = 0, n = 3 + r.Int(6); i < n; i++)
                {
                    d.Line(Pick(r, split, Services));
                }

                break;
            case 11:
                d.Line(Crumbs[r.Int(Crumbs.Length)] + " > " + Crumbs[r.Int(Crumbs.Length)] + " > " + Item(r, split));
                break;
            case 12:
                d.Line(Renderer.NumberLine(r, lex, loc));
                break;
            case 13:
                d.Line(Pick(r, split, lex.Legal));
                break;
            default:
                d.Line(Fill(r, split, loc, Pick(r, split, LookAlikes)));
                break;
        }
    }

    /// <summary>Adds items until about <paramref name="chars"/> characters of chrome have been added.</summary>
    internal void Fill(Doc d, string split, Loc loc, int chars)
    {
        int start = d.Position;
        int guard = 0;
        while (d.Position - start < chars && guard++ < 60)
        {
            Item(d, split, loc);
        }
    }

    /// <summary>A page or text with no contact block at all.</summary>
    internal void NegativePage(Doc d, string split, Loc loc)
    {
        Fill(d, split, loc, 400 + d.R.Int(2400));
    }

    /// <summary>Running text that mentions people and places the way news and chat do. The CRM-view negative.</summary>
    internal void NegativeTalk(Doc d, string split, Loc loc, int n)
    {
        for (int i = 0; i < n; i++)
        {
            d.Line(d.R.Chance(0.8) ? Sentence(d.R, split, loc) : Renderer.NumberLine(d.R, LexSet.Of(loc), loc));
        }
    }
}
