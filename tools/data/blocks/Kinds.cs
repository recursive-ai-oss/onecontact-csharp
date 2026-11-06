using System.Globalization;

namespace OneContact.Data.Blocks;

internal sealed class Member
{
    internal PersonRec P = null!;
    internal string? Title;
    internal string? Dept;
    internal Phone? Phone;
    internal string? Email;
    internal bool FreeMail;
}

/// <summary>The people, company and contact values behind one block. Both views of a block are rendered from the same one.</summary>
internal sealed class Logical
{
    internal Loc Loc;
    internal CompanyRec Co = null!;
    internal string Domain = "";
    internal string Slug = "";
    internal List<Member> People = [];
    internal List<AddressRec> Addrs = [];
    internal Phone Main = null!;
    internal Phone? Fax;
    internal Phone? Mobile;
    internal string MainEmail = "";
    internal string RoleEmail = "";
}

internal static class Kind
{
    internal const string Footer = "footer";
    internal const string Contact = "contact-us";
    internal const string Staff = "staff-list";
    internal const string Signature = "signature";
    internal const string Card = "business-card";
    internal const string Note = "crm-note";
    internal const string Ugc = "ugc";
    internal const string Multi = "multi-location";
    internal const string NegNumeric = "negative-numeric";
    internal const string NegProse = "negative-prose";
    internal const string NegPage = "negative-page";
    internal const string NegTalk = "negative-talk";

    internal static readonly (string Name, double Weight, bool Page, bool Crm)[] All =
    [
        (Footer, 10, true, false),
        (Contact, 12, true, true),
        (Staff, 12, true, true),
        (Signature, 14, true, true),
        (Card, 6, true, true),
        (Note, 14, false, true),
        (Ugc, 10, false, true),
        (Multi, 6, true, true),
        (NegNumeric, 6, true, true),
        (NegProse, 5, true, true),
        (NegPage, 28, true, false),
        (NegTalk, 5, false, true),
    ];

    internal static bool IsNegative(string kind) => kind is NegNumeric or NegProse or NegPage or NegTalk;

    /// <summary>Kinds that sit inside the page or text around them (navigation, prose, product lists) in some of their renderings.</summary>
    internal static bool Wrapped(string kind) => kind is Footer or Contact or Staff or Signature or Card or Multi or Note or Ugc;
}

/// <summary>Everything that turns a <see cref="Logical"/> into text with spans, one method per block kind.</summary>
internal sealed class Renderer
{
    private readonly Pools _pools;
    private readonly FieldGen _fg;

    internal Renderer(Pools pools, FieldGen fg)
    {
        _pools = pools;
        _fg = fg;
    }

    // ---------------------------------------------------------------- the data behind a block

    private static T PickFrom<T>(Dictionary<(string, Loc), List<T>> d, string split, Loc loc, Prng r, Func<T, bool>? ok = null)
    {
        if (!d.TryGetValue((split, loc), out List<T>? l) || l.Count < 50)
        {
            l = d.Where(kv => kv.Key.Item1 == split).OrderByDescending(kv => kv.Value.Count).First().Value;
        }

        for (int i = 0; i < 6; i++)
        {
            T t = l[r.Int(l.Count)];
            if (ok is null || ok(t))
            {
                return t;
            }
        }

        return l[r.Int(l.Count)];
    }

    internal Logical Make(Prng r, string split, Loc loc, string kind)
    {
        bool crmish = kind is Kind.Note or Kind.Ugc or Kind.Signature;
        double noisy = kind is Kind.Note or Kind.Ugc ? 0.25 : 0.04;
        Logical l = new() { Loc = loc };
        l.Co = PickFrom(_pools.Companies, split, loc, r, c => !c.Noisy || r.Chance(noisy));
        l.Domain = _fg.Domain(r, loc, l.Co.Base);
        l.Slug = FieldGen.Fold(l.Co.Base.Split(' ')[0]) + (r.Chance(0.3) ? "-" + FieldGen.Fold(l.Co.Base.Split(' ')[^1]) : "");
        int people = kind switch
        {
            Kind.Staff => 3 + r.Int(10),
            Kind.Signature or Kind.Card => 1,
            Kind.Note or Kind.Ugc => 1 + (r.Chance(0.3) ? 1 : 0),
            Kind.Contact => r.Chance(0.25) ? 1 : 0,
            _ => 0,
        };
        for (int i = 0; i < people; i++)
        {
            Member m = new() { P = PickFrom(_pools.Persons, split, loc, r, p => !p.Noisy || r.Chance(noisy)) };
            LexSet lex = LexSet.Of(loc);
            m.Title = r.Chance(0.85) ? r.Pick(lex.Titles) : null;
            m.Dept = r.Chance(0.2) ? r.Pick(lex.Departments) : null;
            m.FreeMail = r.Chance(crmish ? 0.35 : 0.04);
            string mailbox = _fg.Mailbox(r, m.P, m.FreeMail);
            m.Email = mailbox + "@" + (m.FreeMail ? _fg.FreeDomain(r, loc) : l.Domain);
            m.Phone = _fg.Phone(r, loc, r.Chance(0.15));
            l.People.Add(m);
        }

        int addrs = kind == Kind.Multi ? 2 + r.Int(4) : (r.Chance(0.2) ? 2 : 1);
        for (int i = 0; i < addrs; i++)
        {
            l.Addrs.Add(PickFrom(_pools.Addresses, split, loc, r));
        }

        l.Main = _fg.Phone(r, loc, r.Chance(0.1));
        l.Fax = r.Chance(0.3) ? _fg.Phone(r, loc, false) : null;
        l.Mobile = r.Chance(0.3) ? _fg.Phone(r, loc, false) : null;
        LexSet lx = LexSet.Of(loc);
        l.RoleEmail = r.Pick(lx.Roles) + "@" + l.Domain;
        l.MainEmail = l.RoleEmail;
        return l;
    }

    // ---------------------------------------------------------------- items

    private void PhoneItem(Doc d, Logical l, Phone ph, bool fax = false, string? label = null, double labelChance = 0.8)
    {
        LexSet lex = LexSet.Of(l.Loc);
        d.Next();
        if (label is not null || d.R.Chance(labelChance))
        {
            string lab = label ?? (fax ? d.R.Pick(lex.FaxLabels) : d.R.Pick(lex.PhoneLabels));
            d.Raw(d.Case(lab)).Raw(d.R.Pick(new[] { ": ", " ", ": ", " - ", " | ", "." }));
        }

        string shown = _fg.FormatPhone(ph, d.R, l.Loc, lex, true, out bool obf);
        if (obf)
        {
            d.Field("phone", ph.Digits, shown: shown, mode: "o");
        }
        else
        {
            d.Field("phone", shown);
        }
    }

    private void EmailItem(Doc d, Logical l, string email, double labelChance = 0.7, bool inline = false)
    {
        LexSet lex = LexSet.Of(l.Loc);
        if (!inline)
        {
            d.Next();
        }

        if (!inline && d.R.Chance(labelChance))
        {
            d.Raw(d.Case(d.R.Pick(lex.EmailLabels))).Raw(d.R.Pick(new[] { ": ", " ", ": ", " - " }));
        }

        string shown = FieldGen.ShowEmail(email, d.R, true, out string mode);
        d.Field("email", email, shown: shown, mode: mode);
    }

    private void WebItem(Doc d, Logical l, double labelChance = 0.6, bool inline = false, Member? who = null)
    {
        LexSet lex = LexSet.Of(l.Loc);
        if (!inline)
        {
            d.Next();
            if (d.R.Chance(labelChance))
            {
                d.Raw(d.Case(d.R.Pick(lex.WebLabels))).Raw(d.R.Pick(new[] { ": ", " ", ": ", " - " }));
            }
        }

        d.Field("website", FieldGen.Web(d.R, l.Domain, l.Slug, who is null ? null : FieldGen.Fold(who.P.Given) + FieldGen.Fold(who.P.Family)));
    }

    private void AddressItem(Doc d, Logical l, AddressRec a, double labelChance = 0.35, bool inline = false)
    {
        LexSet lex = LexSet.Of(l.Loc);
        if (!inline)
        {
            d.Next();
            if (d.R.Chance(labelChance))
            {
                d.Raw(d.Case(d.R.Pick(lex.AddressLabels))).Raw(d.R.Pick(new[] { ": ", " ", ": ", " - " }));
                if (d.Lines && d.R.Chance(0.4))
                {
                    d.Raw("\n");
                }
            }
        }

        d.Field("address", a.Value, name: true);
    }

    private static void PersonItem(Doc d, Member m, bool inline = false)
    {
        if (!inline)
        {
            d.Next();
        }

        d.Field("person", m.P.Text, name: true);
    }

    private static void CompanyItem(Doc d, Logical l, bool inline = false)
    {
        if (!inline)
        {
            d.Next();
        }

        d.Field("company", l.Co.Text, name: true);
    }

    internal static string NumberLine(Prng r, LexSet lex, Loc loc)
    {
        string D(int n)
        {
            char[] c = new char[n];
            for (int i = 0; i < n; i++)
            {
                c[i] = (char)('0' + r.Int(10));
            }

            return new string(c);
        }

        string Lead01(int n) => (char)('0' + r.Int(2)) + D(n - 1);
        string Money() => (loc == Loc.CaFr ? "" : "$") + (1 + r.Int(4000)).ToString(loc == Loc.CaFr ? "#,0" : "#,0", CultureInfo.InvariantCulture) + "." + D(2) + (loc == Loc.CaFr ? " $" : "");
        string Date() => r.Weighted([30, 25, 25, 20]) switch
        {
            0 => $"{2015 + r.Int(12)}-{1 + r.Int(12):D2}-{1 + r.Int(28):D2}",
            1 => $"{1 + r.Int(12):D2}/{1 + r.Int(28):D2}/{2015 + r.Int(12)}",
            2 => $"{1 + r.Int(28)} {lex.Months[r.Int(12)]} {2015 + r.Int(12)}",
            _ => $"{1 + r.Int(28):D2}.{1 + r.Int(12):D2}.{2015 + r.Int(12)}",
        };
        bool fr = loc == Loc.CaFr;
        return r.Int(22) switch
        {
            0 => (fr ? "Commande n° " : "Order #") + Lead01(10),
            1 => (fr ? "Numéro de commande : " : "Order number: ") + $"ORD-{2015 + r.Int(12)}-{D(6)}",
            2 => (fr ? "Suivi : " : "Tracking number: ") + "1Z" + D(3) + "AA" + D(10),
            3 => (fr ? "Suivi : " : "Tracking: ") + Lead01(12),
            4 => (fr ? "Facture " : "Invoice ") + $"{2015 + r.Int(12)}-{D(4)}",
            5 => $"SKU {(char)('A' + r.Int(26))}{(char)('A' + r.Int(26))}-{D(4)}-{(char)('A' + r.Int(26))}{(char)('A' + r.Int(26))}",
            6 => $"Version {r.Int(9)}.{r.Int(30)}.{r.Int(20)}.{r.Int(5000)}",
            7 => $"IP {10 + r.Int(200)}.{r.Int(256)}.{r.Int(256)}.{r.Int(256)}",
            8 => (fr ? "Publié le " : "Posted ") + Date(),
            9 => (fr ? "Mis à jour : " : "Updated ") + $"{2015 + r.Int(12)}-{1 + r.Int(12):D2}-{1 + r.Int(28):D2} {r.Int(24):D2}:{r.Int(60):D2}",
            10 => (fr ? "Total : " : "Total: ") + Money(),
            11 => (fr ? "Prix : " : "Price: ") + Money(),
            12 => $"ISBN 978-{r.Int(10)}-{D(2)}-{D(6)}-{r.Int(10)}",
            13 => $"Ref. {(char)('A' + r.Int(26))}{(char)('A' + r.Int(26))}{D(8)}",
            14 => (fr ? "Dossier n° " : "Case ID ") + D(7),
            15 => $"{45 + r.Int(4)}.{D(4)}, -{75 + r.Int(5)}.{D(4)}",
            16 => $"Model X-{D(3)}",
            17 => $"Serial {D(4)}-{D(4)}-{D(4)}",
            18 => (fr ? "Page " : "Page ") + $"{1 + r.Int(20)} / {20 + r.Int(80)}",
            19 => (fr ? "Réf. " : "Ref ") + $"{Lead01(3)}-{D(3)}-{D(4)}",
            20 => (fr ? "Compte " : "Account ") + $"0{D(2)}-{D(3)}-{D(4)}",
            _ => $"{(fr ? "Code postal seulement :" : "Store #")} {D(4)}",
        };
    }

    private static string Prose(Prng r, LexSet lex)
    {
        if (r.Chance(0.25))
        {
            return r.Pick(lex.Facts);
        }

        return r.Pick(lex.Subjects) + " " + r.Pick(lex.Predicates).Replace("{t}", r.Pick(lex.Topics), StringComparison.Ordinal) + ".";
    }

    private static void Distract(Doc d, Logical l, double p)
    {
        if (d.R.Chance(p))
        {
            LexSet lex = LexSet.Of(l.Loc);
            d.Noise(d.R.Chance(0.5) ? NumberLine(d.R, lex, l.Loc) : Prose(d.R, lex));
        }
    }

    // ---------------------------------------------------------------- block kinds

    internal void Footer(Doc d, Logical l)
    {
        LexSet lex = LexSet.Of(l.Loc);
        Prng r = d.R;
        int nav = 2 + r.Int(5);
        for (int i = 0; i < nav; i++)
        {
            d.Noise(r.Pick(lex.Nav));
        }

        d.Next().StartRecord();
        if (r.Chance(0.6))
        {
            d.Raw("© " + (2012 + r.Int(15)).ToString(CultureInfo.InvariantCulture) + " ");
            d.Field("company", l.Co.Text, name: true);
            d.Raw(". " + d.Case(lex.Rights));
        }
        else
        {
            d.Field("company", l.Co.Text, name: true);
        }

        List<Action> items = [];
        if (r.Chance(0.9))
        {
            items.Add(() => AddressItem(d, l, l.Addrs[0], 0.35));
        }

        items.Add(() => PhoneItem(d, l, l.Main));
        if (l.Fax is not null)
        {
            items.Add(() => PhoneItem(d, l, l.Fax, fax: true));
        }

        if (r.Chance(0.7))
        {
            items.Add(() => EmailItem(d, l, l.MainEmail));
        }

        if (r.Chance(0.5))
        {
            items.Add(() => WebItem(d, l));
        }

        if (r.Chance(0.3))
        {
            Shuffle(items, r);
        }

        foreach (Action a in items)
        {
            a();
        }

        int legal = 1 + r.Int(4);
        for (int i = 0; i < legal; i++)
        {
            d.Noise(r.Pick(lex.Legal));
        }
    }

    internal void ContactUs(Doc d, Logical l)
    {
        LexSet lex = LexSet.Of(l.Loc);
        Prng r = d.R;
        d.Noise(r.Pick(lex.Headings));
        d.Next().StartRecord();
        if (r.Chance(0.7))
        {
            CompanyItem(d, l, inline: true);
        }
        else
        {
            d.Raw(string.Empty);
            d.Raw(d.Case(r.Pick(lex.Headings)));
        }

        foreach (AddressRec a in l.Addrs)
        {
            AddressItem(d, l, a, 0.5);
        }

        PhoneItem(d, l, l.Main, labelChance: 0.9);
        if (l.Fax is not null)
        {
            PhoneItem(d, l, l.Fax, fax: true);
        }

        if (l.Mobile is not null && r.Chance(0.4))
        {
            PhoneItem(d, l, l.Mobile);
        }

        EmailItem(d, l, l.MainEmail, 0.8);
        if (r.Chance(0.5))
        {
            WebItem(d, l);
        }

        foreach (Member m in l.People)
        {
            PersonItem(d, m);
            if (m.Title is not null)
            {
                d.Raw(", " + m.Title);
            }

            if (m.Email is not null && r.Chance(0.6))
            {
                EmailItem(d, l, m.Email, 0.4);
            }
        }

        if (r.Chance(0.5))
        {
            d.Noise(l.Loc == Loc.CaFr ? "Heures d'ouverture : lundi au vendredi, 9 h à 17 h" : "Hours: Monday to Friday, 9 am to 5 pm");
        }

        Distract(d, l, 0.15);
    }

    internal void Staff(Doc d, Logical l)
    {
        LexSet lex = LexSet.Of(l.Loc);
        Prng r = d.R;
        d.Noise(r.Pick(lex.TeamHeadings));
        if (r.Chance(0.4))
        {
            d.NextGroup().StartRecord();
            CompanyItem(d, l, inline: true);
            if (r.Chance(0.6))
            {
                AddressItem(d, l, l.Addrs[0], 0.3);
            }
        }

        foreach (Member m in l.People)
        {
            d.NextGroup().StartRecord();
            PersonItem(d, m, inline: true);
            if (m.Title is not null)
            {
                d.Noise(m.Title);
                if (!d.Lines)
                {
                    d.Next().Raw(m.Title);
                }
            }

            if (m.Dept is not null && r.Chance(0.5))
            {
                d.Noise(m.Dept);
            }

            if (r.Chance(0.65))
            {
                PhoneItem(d, l, m.Phone!, labelChance: 0.5);
            }

            if (r.Chance(0.75))
            {
                EmailItem(d, l, m.Email!, 0.3);
            }
        }

        Distract(d, l, 0.1);
    }

    internal void Signature(Doc d, Logical l)
    {
        LexSet lex = LexSet.Of(l.Loc);
        Prng r = d.R;
        Member m = l.People[0];
        if (r.Chance(0.7))
        {
            d.Noise(r.Pick(lex.Closings));
        }

        d.Next().StartRecord();
        d.Field("person", m.P.Text, name: true);
        if (m.Title is not null)
        {
            if (d.Lines)
            {
                d.Next().Raw(m.Title);
            }
            else if (r.Chance(0.6))
            {
                d.Next().Raw(m.Title);
            }
        }

        if (r.Chance(0.85))
        {
            CompanyItem(d, l);
        }

        if (r.Chance(0.4))
        {
            AddressItem(d, l, l.Addrs[0], 0.2);
        }

        PhoneItem(d, l, m.Phone!, labelChance: 0.7);
        if (l.Mobile is not null && r.Chance(0.4))
        {
            PhoneItem(d, l, l.Mobile, label: r.Pick(new[] { lex.PhoneLabels[l.Loc == Loc.CaFr ? 5 : 11], lex.PhoneLabels[l.Loc == Loc.CaFr ? 5 : 12] }));
        }

        EmailItem(d, l, m.Email!, 0.5);
        if (r.Chance(0.5))
        {
            WebItem(d, l, 0.4, who: m);
        }

        Distract(d, l, 0.08);
    }

    internal void Card(Doc d, Logical l)
    {
        LexSet lex = LexSet.Of(l.Loc);
        Prng r = d.R;
        Member m = l.People[0];
        d.Next().StartRecord();
        bool companyFirst = r.Chance(0.6);
        if (companyFirst)
        {
            d.Field("company", l.Co.Text, name: true);
        }

        PersonItem(d, m, inline: !companyFirst);
        if (m.Title is not null)
        {
            d.Next().Raw(m.Title);
        }

        if (!companyFirst && r.Chance(0.8))
        {
            CompanyItem(d, l);
        }

        AddressItem(d, l, l.Addrs[0], 0.15);
        PhoneItem(d, l, m.Phone!, labelChance: 0.9);
        if (l.Fax is not null)
        {
            PhoneItem(d, l, l.Fax, fax: true);
        }

        EmailItem(d, l, m.Email!, 0.7);
        WebItem(d, l, 0.6, who: m);
    }

    internal void Multi(Doc d, Logical l)
    {
        LexSet lex = LexSet.Of(l.Loc);
        Prng r = d.R;
        d.Noise(r.Pick(lex.LocationHeadings));
        if (r.Chance(0.5))
        {
            d.Next();
            d.Field("company", l.Co.Text, name: true);
        }

        foreach (AddressRec a in l.Addrs)
        {
            d.NextGroup().StartRecord();
            if (r.Chance(0.7))
            {
                d.Raw(d.Case(r.Pick(lex.OfficeNames)));
                d.Raw(d.Lines ? "\n" : r.Pick(new[] { ": ", " - " }));
            }
            else
            {
                d.Raw("");
            }

            AddressItem(d, l, a, 0.2, inline: true);
            PhoneItem(d, l, _fg.Phone(r, l.Loc, false), labelChance: 0.7);
            if (r.Chance(0.3))
            {
                PhoneItem(d, l, _fg.Phone(r, l.Loc, false), fax: true);
            }

            if (r.Chance(0.35))
            {
                EmailItem(d, l, r.Pick(lex.Roles) + "@" + l.Domain, 0.5);
            }
        }

        Distract(d, l, 0.1);
    }

    internal void Note(Doc d, Logical l, bool ugc)
    {
        LexSet lex = LexSet.Of(l.Loc);
        Prng r = d.R;
        int notes = ugc ? 1 + (r.Chance(0.25) ? 1 : 0) : 1 + r.Int(3);
        string[] templates = ugc ? lex.Ugc : lex.Notes;
        for (int i = 0; i < notes; i++)
        {
            d.NextGroup().StartRecord();
            if (r.Chance(0.12))
            {
                Member g = l.People[0];
                d.Raw(r.Pick(lex.Greetings) + " ");
                d.Field("person", g.P.Given);
                d.Raw(", ");
            }

            string t = templates[r.Int(templates.Length)];
            if (t.Contains("{P2}", StringComparison.Ordinal) && l.People.Count < 2)
            {
                t = t.Replace("{P2}", "{P}", StringComparison.Ordinal);
            }

            Expand(d, l, t);
            if (r.Chance(0.12))
            {
                d.Raw(" " + NumberLine(r, lex, l.Loc));
            }
        }
    }

    private void Expand(Doc d, Logical l, string t)
    {
        Prng r = d.R;
        int i = 0;
        while (i < t.Length)
        {
            int open = t.IndexOf('{', i);
            if (open < 0)
            {
                d.Raw(t[i..]);
                break;
            }

            d.Raw(t[i..open]);
            int close = t.IndexOf('}', open);
            string slot = t[(open + 1)..close];
            i = close + 1;
            switch (slot)
            {
                case "P": PersonItem(d, l.People[0], inline: true); break;
                case "P2": PersonItem(d, l.People[1], inline: true); break;
                case "C": CompanyItem(d, l, inline: true); break;
                case "T": InlinePhone(d, l, l.People[0].Phone ?? l.Main); break;
                case "T2": InlinePhone(d, l, l.Fax ?? l.Main); break;
                case "E": EmailItem(d, l, l.People[0].Email!, inline: true); break;
                case "W": WebItem(d, l, inline: true); break;
                case "A": AddressItem(d, l, l.Addrs[0], inline: true); break;
                default: throw new InvalidOperationException("unknown slot " + slot);
            }
        }
    }

    private void InlinePhone(Doc d, Logical l, Phone ph)
    {
        string shown = _fg.FormatPhone(ph, d.R, l.Loc, LexSet.Of(l.Loc), true, out bool obf);
        if (obf)
        {
            d.Field("phone", ph.Digits, shown: shown, mode: "o");
        }
        else
        {
            d.Field("phone", shown);
        }
    }

    internal void NegNumeric(Doc d, Logical l)
    {
        LexSet lex = LexSet.Of(l.Loc);
        int n = 3 + d.R.Int(6);
        for (int i = 0; i < n; i++)
        {
            d.Next().Raw(d.R.Chance(0.75) ? NumberLine(d.R, lex, l.Loc) : Prose(d.R, lex));
        }
    }

    internal void NegProse(Doc d, Logical l)
    {
        LexSet lex = LexSet.Of(l.Loc);
        int n = 2 + d.R.Int(4);
        for (int i = 0; i < n; i++)
        {
            d.Next().Raw(Prose(d.R, lex));
        }
    }

    private static void Shuffle<T>(List<T> items, Prng r)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = r.Int(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
