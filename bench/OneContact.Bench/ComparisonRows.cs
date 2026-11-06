using System.Net.Mail;
using AngleSharp.Dom;
using BenchmarkDotNet.Attributes;
using MimeKit;
using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;
using NameParser;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Tier1;
using RecursiveAI.OneContact.Names;
using RecursiveAI.OneContact.Phones;
using Lpn = PhoneNumbers;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// Phone operations over the same 40 typed numbers in both libraries: parse, parse and validate, format (international and national) and number
/// type of an already parsed number. The figures are per number.
/// </summary>
[MemoryDiagnoser]
public class PhoneCompareRow
{
    private static readonly (Region Region, string Text)[] s_inputs = Inputs.Phones.Select(p => (Region.Parse(p.Region), p.Text)).ToArray();
    private readonly Lpn.PhoneNumberUtil _util = Lpn.PhoneNumberUtil.GetInstance();
    private PhoneNumber[] _ours = [];
    private Lpn.PhoneNumber[] _theirs = [];

    /// <summary>Parses the numbers once for the format and type rows.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _ours = s_inputs.Select(p => PhoneNumber.Parse(p.Text, p.Region)).ToArray();
        _theirs = Inputs.Phones.Select(p => _util.Parse(p.Text, p.Region)).ToArray();
    }

    /// <summary>Parse, this library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int OursParse()
    {
        int n = 0;
        foreach ((Region region, string text) in s_inputs)
        {
            n += PhoneNumber.TryParse(text, region, out PhoneNumber p) ? p.CountryCode : 0;
        }

        return n;
    }

    /// <summary>Parse, libphonenumber-csharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int TheirsParse()
    {
        int n = 0;
        foreach ((string region, string text) in Inputs.Phones)
        {
            n += _util.Parse(text, region).CountryCode;
        }

        return n;
    }

    /// <summary>Parse and validate, this library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int OursParseValidate()
    {
        int n = 0;
        foreach ((Region region, string text) in s_inputs)
        {
            n += PhoneNumber.TryParse(text, region, out PhoneNumber p) && p.IsValid ? 1 : 0;
        }

        return n;
    }

    /// <summary>Parse and validate, libphonenumber-csharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int TheirsParseValidate()
    {
        int n = 0;
        foreach ((string region, string text) in Inputs.Phones)
        {
            n += _util.IsValidNumber(_util.Parse(text, region)) ? 1 : 0;
        }

        return n;
    }

    /// <summary>Format international and national, this library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int OursFormat()
    {
        int n = 0;
        foreach (PhoneNumber p in _ours)
        {
            n += p.Format(PhoneFormat.International).Length + p.Format(PhoneFormat.National).Length;
        }

        return n;
    }

    /// <summary>Format international and national, libphonenumber-csharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int TheirsFormat()
    {
        int n = 0;
        foreach (Lpn.PhoneNumber p in _theirs)
        {
            n += _util.Format(p, Lpn.PhoneNumberFormat.INTERNATIONAL).Length + _util.Format(p, Lpn.PhoneNumberFormat.NATIONAL).Length;
        }

        return n;
    }

    /// <summary>Number type, this library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int OursType()
    {
        int n = 0;
        foreach (PhoneNumber p in _ours)
        {
            n += (int)p.Type;
        }

        return n;
    }

    /// <summary>Number type, libphonenumber-csharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int TheirsType()
    {
        int n = 0;
        foreach (Lpn.PhoneNumber p in _theirs)
        {
            n += (int)_util.GetNumberType(p);
        }

        return n;
    }
}

/// <summary>Finding numbers in about 1 MB of prose (the text of the matcher row), at the valid leniency, both libraries.</summary>
[MemoryDiagnoser]
public class PhoneFindCompareRow
{
    private readonly Lpn.PhoneNumberUtil _util = Lpn.PhoneNumberUtil.GetInstance();
    private string _text = "";

    /// <summary>Builds the text.</summary>
    [GlobalSetup]
    public void Setup() => _text = PhoneMatcherRow.MakeText();

    /// <summary>This library.</summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public int OursFindAll()
    {
        int n = 0;
        foreach (PhoneNumberMatch unused in PhoneNumber.FindAll(_text, Region.US, PhoneMatchLeniency.Valid))
        {
            n++;
        }

        return n;
    }

    /// <summary>libphonenumber-csharp.</summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public int TheirsFindNumbers()
    {
        int n = 0;
        foreach (Lpn.PhoneNumberMatch unused in _util.FindNumbers(_text, "US"))
        {
            n++;
        }

        return n;
    }
}

/// <summary>Email parsing over 33 addresses, valid and not: this library, System.Net.Mail.MailAddress and MimeKit.</summary>
[MemoryDiagnoser]
public class EmailCompareRow
{
    /// <summary>This library.</summary>
    /// <returns>The number accepted.</returns>
    [Benchmark(OperationsPerInvoke = 33)]
    public int Ours()
    {
        int n = 0;
        foreach ((string text, _) in Inputs.Emails)
        {
            n += EmailAddress.TryParse(text, out _) ? 1 : 0;
        }

        return n;
    }

    /// <summary>System.Net.Mail.MailAddress.</summary>
    /// <returns>The number accepted.</returns>
    [Benchmark(OperationsPerInvoke = 33)]
    public int MailAddressTryCreate()
    {
        int n = 0;
        foreach ((string text, _) in Inputs.Emails)
        {
            n += MailAddress.TryCreate(text, out _) ? 1 : 0;
        }

        return n;
    }

    /// <summary>MimeKit MailboxAddress.</summary>
    /// <returns>The number accepted.</returns>
    [Benchmark(OperationsPerInvoke = 33)]
    public int MimeKitTryParse()
    {
        int n = 0;
        foreach ((string text, _) in Inputs.Emails)
        {
            n += MailboxAddress.TryParse(text, out _) ? 1 : 0;
        }

        return n;
    }
}

/// <summary>URL parsing over 30 absolute URLs: this library's <c>WebAddress</c> (parse, canonical form, registrable domain) against AngleSharp's <c>Url</c>.</summary>
[MemoryDiagnoser]
public class UrlCompareRow
{
    /// <summary>This library.</summary>
    /// <returns>The number accepted.</returns>
    [Benchmark(OperationsPerInvoke = 30)]
    public int Ours()
    {
        int n = 0;
        foreach (string text in Inputs.Urls)
        {
            n += WebAddress.TryParse(text, out _) ? 1 : 0;
        }

        return n;
    }

    /// <summary>AngleSharp.</summary>
    /// <returns>The number accepted.</returns>
    [Benchmark(OperationsPerInvoke = 30)]
    public int AngleSharpUrl()
    {
        int n = 0;
        foreach (string text in Inputs.Urls)
        {
            n += new Url(text).IsInvalid ? 0 : 1;
        }

        return n;
    }
}

/// <summary>Registrable-domain lookup over 14 hosts: this library through a parsed address, against Nager.PublicSuffix with the same list.</summary>
[MemoryDiagnoser]
public class PublicSuffixCompareRow
{
    private DomainParser? _nager;

    /// <summary>Loads the list from the path in ONECONTACT_PSL.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var provider = new LocalFileRuleProvider(Environment.GetEnvironmentVariable("ONECONTACT_PSL") ?? throw new InvalidOperationException("set ONECONTACT_PSL to public_suffix_list.dat"));
        provider.BuildAsync().GetAwaiter().GetResult();
        _nager = new DomainParser(provider);
    }

    /// <summary>This library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 14)]
    public int Ours()
    {
        int n = 0;
        foreach (string host in Inputs.Hosts)
        {
            n += WebAddress.TryParse("https://" + host, out WebAddress w) ? w.RegistrableDomain?.Length ?? 0 : 0;
        }

        return n;
    }

    /// <summary>Nager.PublicSuffix.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 14)]
    public int Nager()
    {
        int n = 0;
        foreach (string host in Inputs.Hosts)
        {
            n += _nager!.Parse(host)?.RegistrableDomain?.Length ?? 0;
        }

        return n;
    }
}

/// <summary>Person-name parsing over 38 CRM-style names, rules only, against NameParserSharp.</summary>
[MemoryDiagnoser]
public class NameCompareRow
{
    /// <summary>This library, <c>PersonName.TryParse</c>.</summary>
    /// <returns>The number parsed.</returns>
    [Benchmark(OperationsPerInvoke = 38)]
    public int OursPersonName()
    {
        int n = 0;
        foreach (string text in Inputs.Names)
        {
            n += PersonName.TryParse(text, out _) ? 1 : 0;
        }

        return n;
    }

    /// <summary>This library, the classifying splitter.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 38)]
    public int OursSplit()
    {
        int n = 0;
        foreach (string text in Inputs.Names)
        {
            n += (int)NameParsing.Split(text).Class;
        }

        return n;
    }

    /// <summary>NameParserSharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 38)]
    public int NameParserSharpParse()
    {
        int n = 0;
        foreach (string text in Inputs.Names)
        {
            n += new HumanName(text).First.Length;
        }

        return n;
    }
}

/// <summary>The same names with the Tier 1 model registered (set ONECONTACT_TIER1_MODEL to its weights). Run in its own process.</summary>
[MemoryDiagnoser]
public class NameModelRow
{
    private IDisposable? _scope;

    /// <summary>Loads and registers the model.</summary>
    [GlobalSetup]
    public void Setup()
    {
        using FileStream stream = File.OpenRead(Environment.GetEnvironmentVariable("ONECONTACT_TIER1_MODEL") ?? throw new InvalidOperationException("set ONECONTACT_TIER1_MODEL"));
        Tier1Model model = Tier1Model.Load(stream);
        _scope = ContactModels.Scope(model, model, model);
        NameParsing.Split("Jane Smith");
    }

    /// <summary>Removes the registration.</summary>
    [GlobalCleanup]
    public void Cleanup() => _scope?.Dispose();

    /// <summary>This library, <c>PersonName.TryParse</c> with the model.</summary>
    /// <returns>The number parsed.</returns>
    [Benchmark(OperationsPerInvoke = 38)]
    public int OursPersonName()
    {
        int n = 0;
        foreach (string text in Inputs.Names)
        {
            n += PersonName.TryParse(text, out _) ? 1 : 0;
        }

        return n;
    }

    /// <summary>This library, the classifying splitter with the model.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 38)]
    public int OursSplit()
    {
        int n = 0;
        foreach (string text in Inputs.Names)
        {
            n += (int)NameParsing.Split(text).Class;
        }

        return n;
    }
}

/// <summary>Pages of staff blocks (24 per page): HTML extraction here and, on the visible text, Microsoft.Recognizers.Text sequence finding.</summary>
[MemoryDiagnoser]
public class ExtractionCompareRow
{
    private Inputs.Page[] _pages = [];
    private ContactExtractor _both = new();

    /// <summary>Builds eight pages.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _pages = Enumerable.Range(1, 8).Select(i => Inputs.MakePage(i, 24)).ToArray();
        _both = new ContactExtractor { Sources = ExtractionSources.Markup | ExtractionSources.Text, DefaultRegion = Region.US };
    }

    /// <summary>HTML in, records out, rules and markup.</summary>
    /// <returns>The number of fields.</returns>
    [Benchmark(OperationsPerInvoke = 8)]
    public int OursHtml()
    {
        int n = 0;
        foreach (Inputs.Page page in _pages)
        {
            n += _both.ExtractHtml(page.Html).Fields.Count;
        }

        return n;
    }

    /// <summary>Plain text in, rules only.</summary>
    /// <returns>The number of fields.</returns>
    [Benchmark(OperationsPerInvoke = 8)]
    public int OursText()
    {
        int n = 0;
        foreach (Inputs.Page page in _pages)
        {
            n += _both.ExtractText(page.Text).Fields.Count;
        }

        return n;
    }

    /// <summary>Microsoft.Recognizers.Text.Sequence: email, phone number and URL on the page's text.</summary>
    /// <returns>The number of matches.</returns>
    [Benchmark(OperationsPerInvoke = 8)]
    public int RecognizersSequence()
    {
        int n = 0;
        foreach (Inputs.Page page in _pages)
        {
            n += Accuracy.Recognizers(page.Text).Count;
        }

        return n;
    }
}
