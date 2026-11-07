using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Geo;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// Time of the first call of one kind and the process's peak working set afterwards, for a cold process. Run once per kind in a fresh process
/// as <c>first-use &lt;kind&gt;</c>: phone, phone-geo, email, url, address, name, company, extract-text, extract-html.
/// </summary>
internal static class FirstUse
{
    /// <summary>Runs one kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>Zero, or one for an unknown kind.</returns>
    public static int Run(string kind)
    {
        long before = Process.GetCurrentProcess().WorkingSet64;
        long startup = Stopwatch.GetTimestamp();
        switch (kind)
        {
            case "none": break;
            case "phone": PhoneNumber.Parse("(202) 456-1111", Region.US).Format(PhoneFormat.International); break;
            case "phone-geo": PhoneGeocoder.Default.GetDescription(PhoneNumber.Parse("(202) 456-1111", Region.US), "en"); PhoneTimeZoneMapper.Default.GetTimeZones(PhoneNumber.Parse("(202) 456-1111", Region.US)); break;
            case "email": EmailAddress.Parse("\"J. Smith\" <J.Smith+crm@GoogleMail.com>"); break;
            case "url": _ = WebAddress.Parse("www.Example.com/?utm_source=x").RegistrableDomain; break;
            case "address": PostalAddress.Parse("1600 Pennsylvania Ave NW, Washington, DC 20500"); break;
            case "name": PersonName.Parse("DR. MARIA JOSE GARCIA LOPEZ"); break;
            case "company": CompanyName.Parse("Acme Holdings GmbH"); break;
            case "extract-text": new ContactExtractor { DefaultRegion = Region.US }.ExtractText("Jane Smith, Acme Inc, 1600 Pennsylvania Ave NW, Washington, DC 20500, jane@acme.com, (202) 456-1111"); break;
            case "extract-html": new ContactExtractor { DefaultRegion = Region.US }.ExtractHtml("<html><body><p>Jane Smith, Acme Inc</p><a href=\"mailto:jane@acme.com\">mail</a> <p>(202) 456-1111</p></body></html>"); break;
            default: return 1;
        }

        double ms = Stopwatch.GetElapsedTime(startup).TotalMilliseconds;
        Process p = Process.GetCurrentProcess();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {kind} | {ms:F1} | {p.PeakWorkingSet64 / 1048576.0:F0} | {(p.WorkingSet64 - before) / 1048576.0:F0} |"));
        return 0;
    }
}
