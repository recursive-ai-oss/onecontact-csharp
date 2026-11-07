using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Data;

namespace OneContact.Recipe.Models;

/// <summary>
/// The address-challenger mode of the recipe. It trains the same pipeline shape on the converted OpenStreetMap address sets (<c>--osm</c>, <c>--osm-dev</c>),
/// optionally mixed with the register-rendered sets of the default recipe (<c>--addresses</c>, <c>--addresses-dev</c>). Without <c>--osm</c> or
/// <c>--osm-dev</c> the default recipe's data loading runs untouched. The challenger sets are ODbL data: a model trained with this mode is a challenger and
/// is never packed (see docs/models-challenger.md).
/// <para>
/// The converted sets label a whole street as <c>street.road</c> and keep official districts apart as <c>dependent-locality.district</c> and
/// <c>admin-area.district</c>. The address head's labels are those of the registers, so the loader writes the street as one <c>street.name</c> and merges
/// each district label into its parent; nothing else is changed (docs/training-data-address-challenger.md, Label mapping).
/// </para>
/// </summary>
internal static class Challenger
{
    private const uint SelectionSalt = 20261105;

    /// <summary>Loads the default recipe's examples, or the challenger's when an OpenStreetMap folder is named.</summary>
    internal static List<Example> LoadAny(Dictionary<string, string> o, StringStore strings, bool dev) =>
        o.ContainsKey(dev ? "osm-dev" : "osm") ? Load(o, strings, dev) : Program.Load(o, strings, dev);

    /// <summary>The label the address head uses for a label of the converted OpenStreetMap sets.</summary>
    internal static string Relabel(string label) => label switch
    {
        "street.road" => "street.name",
        "dependent-locality.district" => "dependent-locality",
        "admin-area.district" => "admin-area",
        _ => label,
    };

    private static List<Example> Load(Dictionary<string, string> o, StringStore strings, bool dev)
    {
        string suffix = dev ? "-dev" : string.Empty;
        List<Example> all = [];

        // The OpenStreetMap files share --osm-count (--osm-dev-count) evenly; a file with fewer lines gives all of them, so every country is seen.
        string[] osmFiles = Program.Files(o, "osm" + suffix);
        int osmCount = Program.Num(o, dev ? "osm-dev-count" : "osm-count", dev ? 20000 : 1000000);
        foreach (string file in osmFiles)
        {
            all.AddRange(Data.ReadAddresses(file, strings, osmCount / Math.Max(1, osmFiles.Length), SelectionSalt, Relabel));
        }

        // The register-rendered sets and the few name examples, loaded exactly as the default recipe loads them.
        if (o.ContainsKey("addresses" + suffix) || o.ContainsKey("names" + suffix) || o.ContainsKey("names2" + suffix))
        {
            all.AddRange(Program.Load(o, strings, dev));
        }

        var rng = new Random(unchecked((int)SelectionSalt));
        for (int i = all.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (all[i], all[j]) = (all[j], all[i]);
        }

        return all;
    }
}
