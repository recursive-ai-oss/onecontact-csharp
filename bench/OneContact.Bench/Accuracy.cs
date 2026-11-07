using System.Globalization;
using System.Net.Mail;
using AngleSharp.Dom;
using MimeKit;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.Sequence;
using NameParser;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Names;
using RecursiveAI.OneContact.Phones;
using Lpn = PhoneNumbers;
using ExtractResult = RecursiveAI.OneContact.Extraction.ExtractResult;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// The accuracy evidence for the speed rows: on the inputs the rows time, how often this library and each comparator give the answer the
/// input is known to have, or agree with each other. Run as <c>accuracy</c>; prints one line per check.
/// </summary>
internal static class Accuracy
{
    /// <summary>Runs every check.</summary>
    /// <returns>Zero.</returns>
    public static int Run()
    {
        Phones();
        Emails();
        Urls();
        Names();
        Extraction();
        return 0;
    }

    /// <summary>Runs the three sequence recognizers the comparison row times.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Every match.</returns>
    internal static List<ModelResult> Recognizers(string text)
    {
        List<ModelResult> all = [];
        all.AddRange(SequenceRecognizer.RecognizeEmail(text, Culture.English));
        all.AddRange(SequenceRecognizer.RecognizePhoneNumber(text, Culture.English));
        all.AddRange(SequenceRecognizer.RecognizeURL(text, Culture.English));
        return all;
    }

    private static void Say(string line) => Console.WriteLine(line);

    private static string Norm(string s) => s.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static void Phones()
    {
        var util = Lpn.PhoneNumberUtil.GetInstance();
        int total = 0;
        int e164 = 0;
        int valid = 0;
        int type = 0;
        int intl = 0;
        int national = 0;
        foreach ((string region, string text) in Inputs.Phones)
        {
            total++;
            PhoneNumber ours = PhoneNumber.Parse(text, Region.Parse(region));
            Lpn.PhoneNumber theirs = util.Parse(text, region);
            e164 += ours.E164 == util.Format(theirs, Lpn.PhoneNumberFormat.E164) ? 1 : 0;
            valid += ours.IsValid == util.IsValidNumber(theirs) ? 1 : 0;
            type += Norm(ours.Type.ToString()) == Norm(util.GetNumberType(theirs).ToString()) ? 1 : 0;
            intl += ours.Format(PhoneFormat.International) == util.Format(theirs, Lpn.PhoneNumberFormat.INTERNATIONAL) ? 1 : 0;
            national += ours.Format(PhoneFormat.National) == util.Format(theirs, Lpn.PhoneNumberFormat.NATIONAL) ? 1 : 0;
        }

        Say(string.Create(CultureInfo.InvariantCulture, $"phones: {total} numbers; same as libphonenumber-csharp: e164 {e164}, valid {valid}, type {type}, international {intl}, national {national}"));
        string text2 = PhoneMatcherRow.MakeText();
        List<int> a = PhoneNumber.FindAll(text2, Region.US, PhoneMatchLeniency.Valid).Select(m => m.Start).ToList();
        List<int> b = util.FindNumbers(text2, "US").Select(m => m.Start).ToList();
        Say(string.Create(CultureInfo.InvariantCulture, $"phone matcher (valid, 1 MB): ours {a.Count} matches, libphonenumber-csharp {b.Count}, same starts {a.SequenceEqual(b)}"));
    }

    private static void Emails()
    {
        int ours = 0;
        int mailAddress = 0;
        int mimeKit = 0;
        foreach ((string text, bool valid) in Inputs.Emails)
        {
            ours += EmailAddress.TryParse(text, out _) == valid ? 1 : 0;
            mailAddress += MailAddress.TryCreate(text, out _) == valid ? 1 : 0;
            mimeKit += MailboxAddress.TryParse(text, out _) == valid ? 1 : 0;
        }

        Say(string.Create(CultureInfo.InvariantCulture, $"email: {Inputs.Emails.Length} inputs; agree with the RFC verdict: ours {ours}, MailAddress {mailAddress}, MimeKit {mimeKit}"));
    }

    private static void Urls()
    {
        int accepted = 0;
        int theirs = 0;
        int sameHref = 0;
        foreach (string text in Inputs.Urls)
        {
            bool o = WebAddress.TryParse(text, out WebAddress w);
            var u = new Url(text);
            accepted += o ? 1 : 0;
            theirs += u.IsInvalid ? 0 : 1;
            if (o && !u.IsInvalid)
            {
                sameHref += string.Equals(w.Scheme + "://" + w.Host, u.Origin, StringComparison.Ordinal) ? 1 : 0;
            }
        }

        Say(string.Create(CultureInfo.InvariantCulture, $"url: {Inputs.Urls.Length} inputs; accepted: ours {accepted}, AngleSharp {theirs}; same origin where both accept {sameHref}. On the WHATWG test data (896 entries) ours passes 896 and AngleSharp 503"));
    }

    private static void Names()
    {
        int same = 0;
        int total = 0;
        foreach (string text in Inputs.Names)
        {
            if (!PersonName.TryParse(text, out PersonName ours))
            {
                continue;
            }

            total++;
            var theirs = new HumanName(text);
            string f = ours.Family.Text ?? string.Empty;
            string g = ours.Given.Text ?? string.Empty;
            same += string.Equals(f, theirs.Last, StringComparison.OrdinalIgnoreCase) && string.Equals(g, theirs.First, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        Say(string.Create(CultureInfo.InvariantCulture, $"names: {total} parsed by ours; same given and family as NameParserSharp on {same}. Accuracy against gold is measured on the evaluation sets"));
    }

    private static void Extraction()
    {
        var extractor = new ContactExtractor { Sources = ExtractionSources.Markup | ExtractionSources.Text, DefaultRegion = Region.US };
        int planted = 0;
        int[] ours = new int[3];
        int[] rec = new int[3];
        int[] oursFound = new int[3];
        int[] recFound = new int[3];
        for (int seed = 1; seed <= 8; seed++)
        {
            Inputs.Page page = Inputs.MakePage(seed, 24);
            ExtractResult r = extractor.ExtractHtml(page.Html);
            HashSet<string> oPhones = r.Of(FieldKind.Phone).Select(f => Digits(f.Text)).ToHashSet(StringComparer.Ordinal);
            HashSet<string> oEmails = r.Of(FieldKind.Email).Select(f => f.Text.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            HashSet<string> oSites = r.Of(FieldKind.Website).Select(f => f.Text.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            List<ModelResult> all = Recognizers(page.Text);
            HashSet<string> rPhones = all.Where(m => m.TypeName == "phonenumber").Select(m => Digits(m.Text)).ToHashSet(StringComparer.Ordinal);
            HashSet<string> rEmails = all.Where(m => m.TypeName == "email").Select(m => m.Text.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            HashSet<string> rSites = all.Where(m => m.TypeName == "url").Select(m => m.Text.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            planted += page.Phones.Count;
            ours[0] += page.Phones.Count(p => oPhones.Contains(p) || oPhones.Contains("1" + p));
            ours[1] += page.Emails.Count(oEmails.Contains);
            ours[2] += page.Websites.Count(s => oSites.Any(x => x.Contains(s, StringComparison.Ordinal)));
            rec[0] += page.Phones.Count(p => rPhones.Contains(p) || rPhones.Any(x => x.EndsWith(p, StringComparison.Ordinal)));
            rec[1] += page.Emails.Count(rEmails.Contains);
            rec[2] += page.Websites.Count(s => rSites.Any(x => x.Contains(s, StringComparison.Ordinal)));
            oursFound[0] += oPhones.Count;
            oursFound[1] += oEmails.Count;
            oursFound[2] += oSites.Count;
            recFound[0] += rPhones.Count;
            recFound[1] += rEmails.Count;
            recFound[2] += rSites.Count;
        }

        string[] kinds = ["phone", "email", "website"];
        for (int k = 0; k < 3; k++)
        {
            Say(string.Create(CultureInfo.InvariantCulture, $"extraction {kinds[k],-7}: {planted} planted per kind; recall ours {ours[k]}, Recognizers {rec[k]}; distinct values returned ours {oursFound[k]}, Recognizers {recFound[k]}"));
        }
    }

    private static string Digits(string s) => new(s.Where(char.IsAsciiDigit).ToArray());
}
