using RecursiveAI.OneContact.Phones.Geo;
using BenchmarkDotNet.Attributes;
using RecursiveAI.OneContact.Phones;
using Lpn = PhoneNumbers;

namespace RecursiveAI.OneContact.Bench;

/// <summary>Typing ten numbers digit by digit into an as-you-type formatter, both libraries. The figure is per number typed.</summary>
[MemoryDiagnoser]
public class AsYouTypeCompareRow
{
    private static readonly (string Region, string Digits)[] s_typed =
    [
        ("US", "2024561111"), ("US", "16502530000"), ("GB", "02079460958"), ("DE", "030123456"), ("FR", "142685300"),
        ("AU", "0293744000"), ("BR", "11912345678"), ("IN", "09876543210"), ("JP", "0312345678"), ("US", "+442079460958"),
    ];

    private readonly Lpn.PhoneNumberUtil _util = Lpn.PhoneNumberUtil.GetInstance();

    /// <summary>This library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 10)]
    public int Ours()
    {
        int n = 0;
        foreach ((string region, string digits) in s_typed)
        {
            var f = new AsYouTypeFormatter(Region.Parse(region));
            foreach (char c in digits)
            {
                n += f.InputDigit(c).Length;
            }
        }

        return n;
    }

    /// <summary>libphonenumber-csharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 10)]
    public int Theirs()
    {
        int n = 0;
        foreach ((string region, string digits) in s_typed)
        {
            Lpn.AsYouTypeFormatter f = _util.GetAsYouTypeFormatter(region);
            foreach (char c in digits)
            {
                n += f.InputDigit(c).Length;
            }
        }

        return n;
    }
}

/// <summary>Short numbers: validity and expected cost of ten already parsed numbers, and the emergency check, both libraries. Figures are per number.</summary>
[MemoryDiagnoser]
public class ShortNumberCompareRow
{
    private static readonly (string Region, string Text)[] s_numbers =
    [
        ("US", "911"), ("US", "611"), ("GB", "999"), ("GB", "112"), ("DE", "110"), ("FR", "15"), ("AU", "000"), ("BR", "190"), ("ES", "112"), ("IN", "100"),
    ];

    private readonly Lpn.PhoneNumberUtil _util = Lpn.PhoneNumberUtil.GetInstance();
    private PhoneNumber[] _ours = [];
    private Lpn.PhoneNumber[] _theirs = [];

    /// <summary>Parses the numbers once.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _ours = s_numbers.Select(p => PhoneNumber.Parse(p.Text, Region.Parse(p.Region))).ToArray();
        _theirs = s_numbers.Select(p => _util.Parse(p.Text, p.Region)).ToArray();
    }

    /// <summary>Valid and cost, this library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 10)]
    public int OursValidAndCost()
    {
        int n = 0;
        ShortNumberInfo info = ShortNumberInfo.Default;
        for (int i = 0; i < _ours.Length; i++)
        {
            Region r = Region.Parse(s_numbers[i].Region);
            n += (info.IsValidForRegion(_ours[i], r) ? 1 : 0) + (int)info.GetExpectedCostForRegion(_ours[i], r);
        }

        return n;
    }

    /// <summary>Valid and cost, libphonenumber-csharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 10)]
    public int TheirsValidAndCost()
    {
        int n = 0;
        Lpn.ShortNumberInfo info = Lpn.ShortNumberInfo.GetInstance();
        for (int i = 0; i < _theirs.Length; i++)
        {
            n += (info.IsValidShortNumberForRegion(_theirs[i], s_numbers[i].Region) ? 1 : 0) + (int)info.GetExpectedCostForRegion(_theirs[i], s_numbers[i].Region);
        }

        return n;
    }

    /// <summary>Emergency check on the typed text, this library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 10)]
    public int OursEmergency()
    {
        int n = 0;
        ShortNumberInfo info = ShortNumberInfo.Default;
        foreach ((string region, string text) in s_numbers)
        {
            n += info.IsEmergencyNumber(text, Region.Parse(region)) ? 1 : 0;
        }

        return n;
    }

    /// <summary>Emergency check on the typed text, libphonenumber-csharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 10)]
    public int TheirsEmergency()
    {
        int n = 0;
        Lpn.ShortNumberInfo info = Lpn.ShortNumberInfo.GetInstance();
        foreach ((string region, string text) in s_numbers)
        {
            n += info.IsEmergencyNumber(text, region) ? 1 : 0;
        }

        return n;
    }
}

/// <summary>Geocoder, carrier and time-zone lookups over the 40 typed numbers, English names, both libraries. Figures are per number.</summary>
[MemoryDiagnoser]
public class GeoCompareRow
{
    private static readonly Lpn.Locale s_english = new("en", "");
    private PhoneNumber[] _ours = [];
    private Lpn.PhoneNumber[] _theirs = [];
    private Lpn.PhoneNumberOfflineGeocoder _geocoder = null!;
    private Lpn.PhoneNumberToCarrierMapper _carrier = null!;
    private Lpn.PhoneNumberToTimeZonesMapper _zones = null!;

    /// <summary>Parses the numbers and creates the lookups, which load their tables on first use.</summary>
    [GlobalSetup]
    public void Setup()
    {
        Lpn.PhoneNumberUtil util = Lpn.PhoneNumberUtil.GetInstance();
        _ours = Inputs.Phones.Select(p => PhoneNumber.Parse(p.Text, Region.Parse(p.Region))).ToArray();
        _theirs = Inputs.Phones.Select(p => util.Parse(p.Text, p.Region)).ToArray();
        _geocoder = Lpn.PhoneNumberOfflineGeocoder.GetInstance();
        _carrier = Lpn.PhoneNumberToCarrierMapper.GetInstance();
        _zones = Lpn.PhoneNumberToTimeZonesMapper.GetInstance();
        foreach (PhoneNumber p in _ours)
        {
            PhoneGeocoder.Default.GetDescription(p, "en");
            PhoneCarrierMapper.Default.GetName(p, "en");
            PhoneTimeZoneMapper.Default.GetTimeZones(p);
        }

        foreach (Lpn.PhoneNumber p in _theirs)
        {
            _geocoder.GetDescriptionForNumber(p, s_english);
            _carrier.GetNameForNumber(p, s_english);
            _zones.GetTimeZonesForNumber(p);
        }
    }

    /// <summary>Geocoder, this library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int OursGeocoder()
    {
        int n = 0;
        foreach (PhoneNumber p in _ours)
        {
            n += PhoneGeocoder.Default.GetDescription(p, "en").Length;
        }

        return n;
    }

    /// <summary>Geocoder, libphonenumber-csharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int TheirsGeocoder()
    {
        int n = 0;
        foreach (Lpn.PhoneNumber p in _theirs)
        {
            n += _geocoder.GetDescriptionForNumber(p, s_english).Length;
        }

        return n;
    }

    /// <summary>Carrier, this library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int OursCarrier()
    {
        int n = 0;
        foreach (PhoneNumber p in _ours)
        {
            n += PhoneCarrierMapper.Default.GetName(p, "en").Length;
        }

        return n;
    }

    /// <summary>Carrier, libphonenumber-csharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int TheirsCarrier()
    {
        int n = 0;
        foreach (Lpn.PhoneNumber p in _theirs)
        {
            n += _carrier.GetNameForNumber(p, s_english).Length;
        }

        return n;
    }

    /// <summary>Time zones, this library.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int OursTimeZones()
    {
        int n = 0;
        foreach (PhoneNumber p in _ours)
        {
            n += PhoneTimeZoneMapper.Default.GetTimeZones(p).Count;
        }

        return n;
    }

    /// <summary>Time zones, libphonenumber-csharp.</summary>
    /// <returns>A checksum.</returns>
    [Benchmark(OperationsPerInvoke = 40)]
    public int TheirsTimeZones()
    {
        int n = 0;
        foreach (Lpn.PhoneNumber p in _theirs)
        {
            n += _zones.GetTimeZonesForNumber(p).Count;
        }

        return n;
    }
}

/// <summary>Finding numbers in about 1 MB of prose at each leniency, both libraries.</summary>
[MemoryDiagnoser]
public class PhoneFindLeniencyCompareRow
{
    private readonly Lpn.PhoneNumberUtil _util = Lpn.PhoneNumberUtil.GetInstance();
    private string _text = "";

    /// <summary>Gets the leniency under test.</summary>
    [Params(PhoneMatchLeniency.Possible, PhoneMatchLeniency.Valid, PhoneMatchLeniency.StrictGrouping, PhoneMatchLeniency.ExactGrouping)]
    public PhoneMatchLeniency Leniency { get; set; }

    /// <summary>Builds the text.</summary>
    [GlobalSetup]
    public void Setup() => _text = PhoneMatcherRow.MakeText();

    /// <summary>This library.</summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public int Ours()
    {
        int n = 0;
        foreach (PhoneNumberMatch unused in PhoneNumber.FindAll(_text, Region.US, Leniency))
        {
            n++;
        }

        return n;
    }

    /// <summary>libphonenumber-csharp.</summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public int Theirs()
    {
        Lpn.PhoneNumberUtil.Leniency l = Leniency switch
        {
            PhoneMatchLeniency.Possible => Lpn.PhoneNumberUtil.Leniency.POSSIBLE,
            PhoneMatchLeniency.Valid => Lpn.PhoneNumberUtil.Leniency.VALID,
            PhoneMatchLeniency.StrictGrouping => Lpn.PhoneNumberUtil.Leniency.STRICT_GROUPING,
            _ => Lpn.PhoneNumberUtil.Leniency.EXACT_GROUPING,
        };
        int n = 0;
        foreach (Lpn.PhoneNumberMatch unused in _util.FindNumbers(_text, "US", l, long.MaxValue))
        {
            n++;
        }

        return n;
    }
}
