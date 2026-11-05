namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>The structural operations on an address, over a data set. The public members of <see cref="PostalAddress"/> call these with the compiled-in data.</summary>
internal static class StructuralAddress
{
    internal static List<AddressIssue> Validate(AddressDataSet data, AddressParts address) => StructuralValidator.Validate(data, address);

    /// <summary>The label lines in the country's format for the script.</summary>
    internal static List<string> LabelLines(AddressDataSet data, AddressParts address, AddressScript script)
    {
        CountryStructure? country = AddressDirectory.Find(data, address.Country, address.Language);
        AddressFormat format;
        if (country is null)
        {
            // A country the data does not describe is laid out by the default node's format.
            format = AddressFormat.Parse(AddressDirectory.Default(data)?[NodeProperty.Fmt] ?? "%N%n%O%n%A%n%C");
        }
        else if (UsesLatin(script, address.Language) && country.LatinFormat is { } latin)
        {
            format = latin;
        }
        else
        {
            format = country.Format;
        }

        return AddressLayoutRenderer.RenderLines(format, address);
    }

    /// <summary>Whether the Latin-script layout is asked for.</summary>
    internal static bool UsesLatin(AddressScript script, string? language) => script switch
    {
        AddressScript.Latin => true,
        AddressScript.Local => false,
        _ => HasLatinScriptSubtag(language),
    };

    private static bool HasLatinScriptSubtag(string? language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return false;
        }

        foreach (string part in language.Split('-', '_'))
        {
            if (string.Equals(part, "Latn", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
