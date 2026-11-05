namespace RecursiveAI.OneContact.Ingest.LibPhoneNumberGeo;

/// <summary>
/// Compiles <c>timezones/map_data.txt</c> into the one table <c>map</c>: keys are calling code plus leading digits, values the zone
/// identifiers joined with <c>&amp;</c> exactly as the file has them (the order is the file's).
/// </summary>
internal sealed class TimeZoneSource : IIngestSource
{
    public string Name => GeoSources.TimeZonesName;

    public string ToolVersion => "1";

    public string NoticeText => GeoSources.ApacheNotice("time-zone map", "timezones");

    public void Convert(IngestInput input, TableSetWriter tables)
    {
        const string path = "timezones/map_data.txt";
        tables.Add("map", PrefixMapWriter.Build(GeoSources.ReadDataFile(path, input.ReadAllBytes(path))));
    }
}
