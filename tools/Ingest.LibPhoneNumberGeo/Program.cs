using RecursiveAI.OneContact.Ingest;
using RecursiveAI.OneContact.Ingest.LibPhoneNumberGeo;

// One tool for the four pins of the Phones.Geo package: the pin's source entry names the converter.
string? pinPath = null;
for (int i = 0; i + 1 < args.Length; i++)
{
    if (args[i] == "--pin")
    {
        pinPath = args[i + 1];
    }
}

if (pinPath is null || !File.Exists(pinPath))
{
    Console.Error.WriteLine("usage: --pin <pin file> [--files <dir>] --out <dir>");
    return 64;
}

string source = PinFile.Load(pinPath).Source;
IIngestSource? converter = source switch
{
    GeoSources.GeocodingName => new PrefixMapSource(GeoSources.GeocodingName, "geocoding", "g", "area descriptions"),
    GeoSources.CarrierName => new PrefixMapSource(GeoSources.CarrierName, "carrier", "c", "carrier names"),
    GeoSources.TimeZonesName => new TimeZoneSource(),
    GeoSources.TerritoriesName => new TerritorySource(),
    _ => null,
};

if (converter is null)
{
    Console.Error.WriteLine($"The pin is for source '{source}', which this tool does not know.");
    return 3;
}

return IngestRunner.Main(args, converter);
