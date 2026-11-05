namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>
/// Structural validation of an address against the address data. The rules come from the data's documentation, and where the
/// documentation is silent from experiments with the reference implementation (docs/address-structural.md):
/// <list type="bullet">
/// <item>The country, the administrative area, the locality and the dependent locality are checked in that order, and checking stops
/// at the first of them that has a problem: the levels below cannot be judged once one above is wrong.</item>
/// <item>A field is <em>unexpected</em> when it has a value and the country's format does not place it, and <em>missing</em> when
/// it is empty and the country requires it. The format and the required fields of a country the data does not describe are the
/// default node's.</item>
/// <item>A subdivision value is <em>known</em> when it equals, ignoring case, a key, a name or a Latin name in the parent's lists.
/// Only a value equal to a key (case-sensitively) or to a Latin name (ignoring case) leads on to that subdivision's own data, so
/// its own subdivisions and postal code prefix are consulted for those values only.</item>
/// <item>The postal code is checked against the country's pattern, replaced by the extended pattern of the most specific
/// subdivision reached that has one, and then against the prefix pattern of the most specific subdivision reached that has one.</item>
/// </list>
/// At most one problem is reported per field.
/// </summary>
internal static class StructuralValidator
{
    internal static List<AddressIssue> Validate(AddressDataSet data, AddressParts address)
    {
        Dictionary<AddressField, AddressIssueKind> found = [];
        CountryStructure? country = AddressDirectory.Find(data, address.Country, address.Language);
        AddressNode? defaults = AddressDirectory.Default(data);

        // A country the data does not describe is judged by the default node alone.
        AddressFormat format = country?.Format ?? AddressFormat.Parse(defaults?[NodeProperty.Fmt] ?? "%N%n%O%n%A%n%C");
        string require = country?.Require ?? defaults?[NodeProperty.Require] ?? string.Empty;

        // The subdivision levels, from the country down. Each level can only be judged against the one above it.
        AddressNode? node = country?.Node;
        string? extendedPattern = null;
        string? prefixPattern = null;
        bool stopped = false;
        if (address.Country.IsUnknown)
        {
            found[AddressField.Country] = AddressIssueKind.MissingRequiredField;
            stopped = true;
        }
        else if (country is null)
        {
            found[AddressField.Country] = AddressIssueKind.UnknownValue;
            stopped = true;
        }

        foreach (AddressField field in new[] { AddressField.AdminArea, AddressField.Locality, AddressField.DependentLocality })
        {
            if (stopped)
            {
                break;
            }

            string value = AddressText.Trim(address.Get(field));
            if (value.Length == 0)
            {
                if (IsRequired(require, field))
                {
                    found[field] = AddressIssueKind.MissingRequiredField;
                    stopped = true;
                }

                node = null;
                continue;
            }

            if (!format.Used[(int)field])
            {
                found[field] = AddressIssueKind.UnexpectedField;
                stopped = true;
                continue;
            }

            if (node is null || country is null || !CountryStructure.HasChildren(node))
            {
                // No list to check against: any value is accepted, and nothing below it can be looked up.
                node = null;
                continue;
            }

            if (!CountryStructure.IsKnown(node, value))
            {
                found[field] = AddressIssueKind.UnknownValue;
                stopped = true;
                continue;
            }

            node = country.FindChildByKey(node, value);
            extendedPattern = node?[NodeProperty.Xzip] ?? extendedPattern;
            prefixPattern = node?[NodeProperty.Zip] ?? prefixPattern;
        }

        ValidatePostalCode(address, country, format, require, found, extendedPattern, prefixPattern);

        CheckSimple(AddressField.SortingCode, address, format, require, found);
        CheckSimple(AddressField.Organization, address, format, require, found);
        CheckSimple(AddressField.Recipient, address, format, require, found);

        bool anyLine = false;
        foreach (string line in address.StreetLines)
        {
            if (AddressText.Trim(line).Length > 0)
            {
                anyLine = true;
                break;
            }
        }

        if (!anyLine && IsRequired(require, AddressField.StreetAddress))
        {
            found[AddressField.StreetAddress] = AddressIssueKind.MissingRequiredField;
        }

        List<AddressIssue> issues = [];
        foreach (AddressField field in Enum.GetValues<AddressField>())
        {
            if (found.TryGetValue(field, out AddressIssueKind kind))
            {
                issues.Add(new AddressIssue(field, kind));
            }
        }

        return issues;
    }

    private static void CheckSimple(
        AddressField field,
        AddressParts address,
        AddressFormat format,
        string require,
        Dictionary<AddressField, AddressIssueKind> found)
    {
        string value = AddressText.Trim(address.Get(field));
        if (value.Length == 0)
        {
            if (IsRequired(require, field))
            {
                found[field] = AddressIssueKind.MissingRequiredField;
            }
        }
        else if (!format.Used[(int)field])
        {
            found[field] = AddressIssueKind.UnexpectedField;
        }
    }

    private static void ValidatePostalCode(
        AddressParts address,
        CountryStructure? country,
        AddressFormat format,
        string require,
        Dictionary<AddressField, AddressIssueKind> found,
        string? extendedPattern,
        string? prefixPattern)
    {
        string zip = AddressText.Trim(address.PostalCode);
        if (zip.Length == 0)
        {
            if (IsRequired(require, AddressField.PostalCode))
            {
                found[AddressField.PostalCode] = AddressIssueKind.MissingRequiredField;
            }

            return;
        }

        if (!format.Used[(int)AddressField.PostalCode])
        {
            found[AddressField.PostalCode] = AddressIssueKind.UnexpectedField;
            return;
        }

        if (country is null)
        {
            return;
        }

        string? whole = extendedPattern ?? country.Node[NodeProperty.Zip];
        if (whole is not null)
        {
            PostalPattern? compiled = CountryStructure.Pattern(whole);
            if (compiled is not null && !compiled.IsMatch(zip, ignoreCase: true))
            {
                found[AddressField.PostalCode] = AddressIssueKind.InvalidFormat;
                return;
            }
        }

        if (prefixPattern is { } prefix)
        {
            PostalPattern? compiled = CountryStructure.Pattern(prefix);
            if (compiled is not null && !compiled.IsPrefixMatch(zip, ignoreCase: true))
            {
                found[AddressField.PostalCode] = AddressIssueKind.MismatchingValue;
            }
        }
    }

    private static bool IsRequired(string require, AddressField field)
    {
        char letter = field switch
        {
            AddressField.Recipient => 'N',
            AddressField.Organization => 'O',
            AddressField.StreetAddress => 'A',
            AddressField.DependentLocality => 'D',
            AddressField.Locality => 'C',
            AddressField.AdminArea => 'S',
            AddressField.PostalCode => 'Z',
            AddressField.SortingCode => 'X',
            _ => '\0',
        };

        return letter != '\0' && require.Contains(letter, StringComparison.Ordinal);
    }
}
