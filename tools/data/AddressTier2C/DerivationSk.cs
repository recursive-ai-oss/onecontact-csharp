using OneContact.Data.AddressTier2A;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace OneContact.Data.AddressTier2C;

internal static partial class Derivation
{
    /// <summary>Keeps, per object id, the version in force with the highest version id.</summary>
    private static Dictionary<int, T> Current<T>(string zipPath, string record, string[] wanted, Func<string?[], (int Key, long Version, T Value)?> pick)
    {
        Dictionary<int, (long Version, T Value)> best = [];
        using Stream s = XmlRecords.FirstEntry(zipPath, out ZipArchive archive);
        using (archive)
        {
            foreach (string?[] v in XmlRecords.Read(s, record, wanted))
            {
                if (pick(v) is { } hit)
                {
                    if (!best.TryGetValue(hit.Key, out (long Version, T Value) old) || old.Version < hit.Version)
                    {
                        best[hit.Key] = (hit.Version, hit.Value);
                    }
                }
            }
        }

        return best.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
    }

    private static int Int(string? s) => int.TryParse(s, out int v) ? v : -1;

    private static long Long(string? s) => long.TryParse(s, out long v) ? v : -1;

    [GeneratedRegex(@"^\d{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex FiveDigits();

    // Register adries (Ministry of the Interior, CC0 terms of use on data.gov.sk), initial batch of 2024-05-21: every file keeps versions of
    // each object with a validity range, so only the version in force (validTo 3000-12-31) is used. An entrance (vchod) carries the orientation
    // number (BuildingNumber), the post code and, in a town with streets, the street; its building (budova) carries the registration number
    // (súpisné číslo), the municipality and the part of the municipality. A building in a village without streets is addressed by its
    // registration number alone, after the name of the part (or of the municipality).
    private static IEnumerable<string[]> Slovakia(string dir, Rejects rejects)
    {
        Dictionary<int, (string Name, string County)> municipalities = Current<(string Name, string County)>(Path.Combine(dir, "obec.zip"), "municipalityChange", ["objectId", "versionId", "validTo", "ItemName", "countyIdentifier"],
            v => XmlRecords.InForce(v[2]) && v[3] is not null ? (Int(v[0]), Long(v[1]), (v[3]!, v[4] ?? "0")) : null);
        Dictionary<int, string> parts = Current<string>(Path.Combine(dir, "cast.zip"), "districtChange", ["objectId", "versionId", "validTo", "ItemName"],
            v => XmlRecords.InForce(v[2]) && v[3] is not null ? (Int(v[0]), Long(v[1]), v[3]!) : null);
        Dictionary<int, string> streets = Current<string>(Path.Combine(dir, "ulica.zip"), "streetNameChange", ["objectId", "versionId", "validTo", "StreetName"],
            v => XmlRecords.InForce(v[2]) && v[3] is not null ? (Int(v[0]), Long(v[1]), v[3]!) : null);
        Dictionary<int, (string Number, int Municipality, int Part)> buildings = Current<(string Number, int Municipality, int Part)>(Path.Combine(dir, "budova.zip"), "propertyRegistrationNumberChange",
            ["objectId", "versionId", "validTo", "PropertyRegistrationNumber", "municipalityIdentifier", "districtIdentifier"],
            v => XmlRecords.InForce(v[2]) && v[3] is not null ? (Int(v[0]), Long(v[1]), (v[3]!, Int(v[4]), Int(v[5]))) : null);
        Console.Error.WriteLine($"sk: {municipalities.Count} municipalities, {parts.Count} parts, {streets.Count} streets, {buildings.Count} buildings in force");

        using Stream s = XmlRecords.FirstEntry(Path.Combine(dir, "vchod.zip"), out ZipArchive archive);
        using (archive)
        {
            foreach (string?[] v in XmlRecords.Read(s, "buildingNumberChange", "objectId", "validTo", "BuildingNumber", "PostalCode", "streetNameIdentifier", "propertyRegistrationNumberIdentifier"))
            {
                if (!XmlRecords.InForce(v[1]))
                {
                    continue;
                }

                string postal = (v[3] ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
                if (!FiveDigits().IsMatch(postal))
                {
                    rejects.Add("sk: no usable post code");
                    continue;
                }

                if (!buildings.TryGetValue(Int(v[5]), out (string Number, int Municipality, int Part) building) || !municipalities.TryGetValue(building.Municipality, out (string Name, string County) municipality))
                {
                    rejects.Add("sk: no building or municipality");
                    continue;
                }

                string registration = building.Number.Trim() is "0" or "" ? string.Empty : building.Number.Trim();
                string orientation = (v[2] ?? string.Empty).Trim() is "0" ? string.Empty : (v[2] ?? string.Empty).Trim();
                string part = parts.GetValueOrDefault(building.Part) ?? string.Empty;
                bool hasStreet = streets.TryGetValue(Int(v[4]), out string? street) && street.Length > 0;
                string house = hasStreet ? (orientation.Length > 0 ? orientation : registration) : registration;
                if (house.Length == 0 || house.Length > 10)
                {
                    rejects.Add("sk: no usable house number");
                    continue;
                }

                string id = "ra:" + v[0];
                string region = "k" + municipality.County;
                if (hasStreet)
                {
                    yield return Row(id, region, street!, "STREET", house, string.Empty, string.Empty, orientation.Length > 0 ? registration : string.Empty, municipality.Name, part == municipality.Name ? string.Empty : part, string.Empty, postal);
                }
                else
                {
                    string place = part.Length > 0 ? part : municipality.Name;
                    yield return Row(id, region, place, "NOSTREET", house, string.Empty, string.Empty, string.Empty, municipality.Name, string.Empty, string.Empty, postal);
                }
            }
        }
    }
}
