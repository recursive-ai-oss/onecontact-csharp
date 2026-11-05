namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>
/// Structural validation of an address against the address data, following the rules the data's documentation gives: required
/// fields are filled in, only fields the country's format uses are filled in, subdivision values are known, and the postal code
/// matches the country's pattern and the prefix the chosen subdivision expects. Nothing is looked up: it only reads the tables.
/// At most one problem is reported per field, the first found.
/// </summary>
internal static class StructuralValidator
{
    internal static List<AddressIssue> Validate(AddressDataSet data, AddressParts address)
    {
        Dictionary<AddressField, AddressIssueKind> found = [];
        CountryStructure? country = AddressDirectory.Find(data, address.Country, address.Language);
        AddressNode? defaults = AddressDirectory.Default(data);

        if (address.Country.IsUnknown)
        {
            found[AddressField.Country] = AddressIssueKind.MissingRequiredField;
        }
        else if (country is null)
        {
            found[AddressField.Country] = AddressIssueKind.UnknownValue;
        }

        // A country the data does not describe is judged by the default node alone.
        AddressFormat format = country?.Format ?? AddressFormat.Parse(defaults?[NodeProperty.Fmt] ?? "%N%n%O%n%A%n%C");
        string require = country?.Require ?? defaults?[NodeProperty.Require] ?? string.Empty;

        AddressNode? node = country?.Node;
        bool chainOk = country is not null;
        AddressField[] chain = [AddressField.AdminArea, AddressField.Locality, AddressField.DependentLocality];
        foreach (AddressField field in chain)
        {
            string value = address.Get(field).Trim();
            if (value.Length == 0)
            {
                if (IsRequired(require, field))
                {
                    found.TryAdd(field, AddressIssueKind.MissingRequiredField);
                }

                chainOk = false;
                continue;
            }

            if (!format.Used[(int)field])
            {
                if (country is not null)
                {
                    found.TryAdd(field, AddressIssueKind.UnexpectedField);
                }

                chainOk = false;
                continue;
            }

            if (!chainOk || node is null || country is null)
            {
                chainOk = false;
                continue;
            }

            if (CountryStructure.HasChildren(node))
            {
                AddressNode? child = country.FindChild(node, value);
                if (child is null)
                {
                    found.TryAdd(field, AddressIssueKind.UnknownValue);
                    chainOk = false;
                }
                else
                {
                    node = child;
                }
            }
            else
            {
                // No list to check against: any value is accepted, and nothing below can be looked up.
                chainOk = false;
            }
        }

        ValidatePostalCode(address, country, format, require, found, node, chainOk);

        CheckSimple(AddressField.SortingCode, address, format, require, found, country is not null);
        CheckSimple(AddressField.Organization, address, format, require, found, country is not null);
        CheckSimple(AddressField.Recipient, address, format, require, found, country is not null);

        bool anyLine = false;
        foreach (string line in address.StreetLines)
        {
            if (line.Trim().Length > 0)
            {
                anyLine = true;
                break;
            }
        }

        if (!anyLine && IsRequired(require, AddressField.StreetAddress))
        {
            found.TryAdd(AddressField.StreetAddress, AddressIssueKind.MissingRequiredField);
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
        Dictionary<AddressField, AddressIssueKind> found,
        bool countryKnown)
    {
        string value = address.Get(field).Trim();
        if (value.Length == 0)
        {
            if (IsRequired(require, field))
            {
                found.TryAdd(field, AddressIssueKind.MissingRequiredField);
            }
        }
        else if (!format.Used[(int)field] && countryKnown)
        {
            found.TryAdd(field, AddressIssueKind.UnexpectedField);
        }
    }

    private static void ValidatePostalCode(
        AddressParts address,
        CountryStructure? country,
        AddressFormat format,
        string require,
        Dictionary<AddressField, AddressIssueKind> found,
        AddressNode? deepest,
        bool chainOk)
    {
        string zip = address.PostalCode.Trim();
        if (zip.Length == 0)
        {
            if (IsRequired(require, AddressField.PostalCode))
            {
                found.TryAdd(AddressField.PostalCode, AddressIssueKind.MissingRequiredField);
            }

            return;
        }

        if (!format.Used[(int)AddressField.PostalCode])
        {
            found.TryAdd(AddressField.PostalCode, AddressIssueKind.UnexpectedField);
            return;
        }

        if (country is null)
        {
            return;
        }

        if (country.Node[NodeProperty.Zip] is { } pattern)
        {
            PostalPattern? compiled = CountryStructure.Pattern(pattern);
            if (compiled is not null && !compiled.IsMatch(zip, ignoreCase: true))
            {
                found.TryAdd(AddressField.PostalCode, AddressIssueKind.InvalidFormat);
                return;
            }
        }

        if (deepest is not null && !ReferenceEquals(deepest, country.Node) && deepest[NodeProperty.Zip] is { } prefix)
        {
            PostalPattern? compiled = CountryStructure.Pattern(prefix);
            if (compiled is not null && !compiled.IsPrefixMatch(zip, ignoreCase: true))
            {
                found.TryAdd(AddressField.PostalCode, AddressIssueKind.MismatchingValue);
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
