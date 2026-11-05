extern alias geo;

using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Phones;
using geo::RecursiveAI.OneContact.Phones.Geo;
using geo::RecursiveAI.OneContact.Phones.Geo.Data;

namespace RecursiveAI.OneContact.Tests;

// Known answers were checked against the released libphonenumber geocoder, carrier and prefix mapper jars; the generated-case parity
// harness in tools/Oracles/PhonesGeo is what covers every region and language.
public class PhoneGeoTests
{
    private static PhoneNumber Parse(string text) => PhoneNumber.Parse(text);

    [Fact]
    public void Geocoder_describes_places_in_the_requested_language()
    {
        PhoneGeocoder geo = PhoneGeocoder.Default;
        Assert.Equal("Mountain View, CA", geo.GetDescription(Parse("+1 650 253 0000"), "en"));
        Assert.Equal("Berlin", geo.GetDescription(Parse("+49 30 123456"), "de"));
        Assert.Equal("London", geo.GetDescription(Parse("+44 20 7946 0958"), "en"));
    }

    [Fact]
    public void Geocoder_with_a_user_region_gives_only_the_country_name_to_foreign_users()
    {
        PhoneGeocoder geo = PhoneGeocoder.Default;
        PhoneNumber de = Parse("+49 30 123456");
        Assert.Equal("Berlin", geo.GetDescription(de, "en", Region.Parse("DE")));
        Assert.Equal("Germany", geo.GetDescription(de, "en", Region.US));
    }

    [Fact]
    public void Geocoder_answers_empty_for_an_invalid_number()
    {
        Assert.Equal(string.Empty, PhoneGeocoder.Default.GetDescription(Parse("+1 123 456 7890"), "en"));
    }

    [Fact]
    public void Time_zones_come_from_the_longest_prefix_or_are_unknown()
    {
        PhoneTimeZoneMapper tz = PhoneTimeZoneMapper.Default;
        Assert.Contains("America/Los_Angeles", tz.GetTimeZones(Parse("+1 650 253 0000")));
        Assert.Contains("Europe/Berlin", tz.GetTimeZones(Parse("+49 30 123456")));
        Assert.Equal([PhoneTimeZoneMapper.UnknownTimeZone], tz.GetTimeZones(Parse("+1 123 456 7890")));
    }

    [Fact]
    public void Carrier_names_only_mobile_numbers_and_safe_names_skip_portable_regions()
    {
        PhoneCarrierMapper carriers = PhoneCarrierMapper.Default;
        PhoneNumber fixedLine = Parse("+49 30 123456");
        Assert.Equal(string.Empty, carriers.GetName(fixedLine, "en"));
        PhoneNumber us = Parse("+1 650 253 0000");
        Assert.Equal(string.Empty, carriers.GetSafeDisplayName(us, "en"));
    }

    [Fact]
    public void Argentine_mobile_numbers_are_looked_up_without_the_mobile_token()
    {
        // The data lists Buenos Aires under 11 whether or not the number carries the 9 after the country code.
        PhoneGeocoder geo = PhoneGeocoder.Default;
        string withToken = geo.GetDescription(Parse("+54 9 11 2345 6789"), "en");
        string without = geo.GetDescription(Parse("+54 11 2345 6789"), "en");
        Assert.NotEqual(string.Empty, withToken);
        Assert.Equal(without, withToken);
    }

    [Fact]
    public void Unknown_languages_fall_back_to_English_and_traditional_chinese_follows_the_region()
    {
        PhoneGeocoder geo = PhoneGeocoder.Default;
        PhoneNumber n = Parse("+49 30 123456");
        Assert.Equal(geo.GetDescription(n, "en"), geo.GetDescription(n, "xx"));
        PhoneNumber tw = Parse("+886 2 2345 6789");
        Assert.NotEqual(string.Empty, geo.GetDescription(tw, "zh-TW"));
    }

    [Fact]
    public void Results_do_not_depend_on_the_current_culture()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            string expected = PhoneGeocoder.Default.GetDescription(Parse("+90 212 123 4567"), "tr");
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal(expected, PhoneGeocoder.Default.GetDescription(Parse("+90 212 123 4567"), "tr"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Tables_load_lazily_and_only_what_a_lookup_needs()
    {
        GeoTables tables = new("libphonenumber-geocoding");
        Assert.False(tables.IsLoaded);
        Assert.True(tables.TryGetMap("g/en/49", out PrefixMap map));
        Assert.True(map.Count > 0);
        Assert.Equal(1, tables.OpenedMaps);
        Assert.False(tables.TryGetMap("g/en/0", out _));
    }

    [Fact]
    public void A_damaged_prefix_table_is_rejected_cleanly()
    {
        GeoTables tables = new("libphonenumber-geocoding");
        Assert.True(tables.TryGetTable("g/en/49", out ReadOnlyMemory<byte> bytes));
        byte[] good = bytes.ToArray();

        Assert.Throws<InvalidDataException>(() => new PrefixMap(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<InvalidDataException>(() => new PrefixMap(good.AsMemory(0, 7)));
        for (int cut = 1; cut < good.Length; cut += Math.Max(1, good.Length / 40))
        {
            byte[] truncated = good.AsMemory(0, cut).ToArray();
            try
            {
                new PrefixMap(truncated).Validate();
            }
            catch (InvalidDataException)
            {
            }
        }

        // A header that claims more entries than the table can hold.
        byte[] huge = (byte[])good.Clone();
        huge[0] = huge[1] = huge[2] = huge[3] = 0xFF;
        Assert.Throws<InvalidDataException>(() => new PrefixMap(huge));
    }

    [Fact]
    public void A_damaged_table_file_is_rejected_cleanly()
    {
        Assert.Throws<InvalidDataException>(() => new GeoTables(() => [1, 2, 3]).TryGetTable("g/en/49", out _));
        Assert.Throws<InvalidDataException>(() => new GeoTables(() => []).TryGetTable("g/en/49", out _));
        Assert.Throws<InvalidDataException>(() => new GeoTables(() => new byte[64]).TryGetTable("g/en/49", out _));
    }

    // The loader smoke: reads every embedded table of every data set and validates each prefix map, within a minute.
    [Fact]
    public void Every_embedded_table_validates_within_a_minute()
    {
        Stopwatch watch = Stopwatch.StartNew();
        int maps = 0;
        foreach (string source in new[] { "libphonenumber-geocoding", "libphonenumber-carrier", "libphonenumber-timezones" })
        {
            GeoTables tables = new(source);
            foreach (string name in tables.TableNames.ToArray())
            {
                if (name.Length > 2 && name[1] == '/' && (name[0] is 'g' or 'c') && tables.TryGetMap(name, out PrefixMap map))
                {
                    map.Validate();
                    maps++;
                }
            }
        }

        Assert.True(maps > 400, $"only {maps} maps");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"took {watch.Elapsed}");
    }
}
