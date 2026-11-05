using System.Text;

namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>
/// Renders an address in a country's format. The rules, which are the behaviour of the reference implementation settled by
/// experiment (docs/address-structural.md):
/// <list type="bullet">
/// <item>values are trimmed and a value that is empty after trimming is absent;</item>
/// <item>a field with no value leaves out the literal text written just before it in the format;</item>
/// <item>that literal text is also left out when it comes after nothing else on the line, unless the field is the first one on the line;</item>
/// <item>a line made of literal text only is always kept; a line with fields and no values is left out;</item>
/// <item>street lines replace <c>%A</c> one per line, blank ones dropped; when there are several, text that follows them in the format starts a new line.</item>
/// </list>
/// </summary>
internal static class AddressLayoutRenderer
{
    /// <summary>Renders the lines of the label.</summary>
    /// <param name="format">The format.</param>
    /// <param name="address">The address.</param>
    /// <returns>The lines, none empty.</returns>
    internal static List<string> RenderLines(AddressFormat format, AddressParts address)
    {
        List<string> result = [];
        StringBuilder line = new();
        foreach (AddressFormat.Line formatLine in format.Lines)
        {
            bool hasField = false;
            foreach (AddressFormat.Piece piece in formatLine.Pieces)
            {
                if (piece.IsField)
                {
                    hasField = true;
                    break;
                }
            }

            if (!hasField)
            {
                string text = string.Concat(formatLine.Pieces.Select(p => p.Literal));
                if (text.Length > 0)
                {
                    result.Add(text);
                }

                continue;
            }

            line.Clear();
            StringBuilder prefix = new();
            int fieldIndex = 0;
            foreach (AddressFormat.Piece piece in formatLine.Pieces)
            {
                if (!piece.IsField)
                {
                    prefix.Append(piece.Literal);
                    continue;
                }

                string value = Value(address, piece.Field);
                if (value.Length > 0)
                {
                    if (line.Length > 0 || fieldIndex == 0)
                    {
                        line.Append(prefix);
                    }

                    line.Append(value);
                }

                prefix.Clear();
                fieldIndex++;
            }

            foreach (string part in line.ToString().Split('\n'))
            {
                if (part.Length > 0)
                {
                    result.Add(part);
                }
            }
        }

        return result;
    }

    private static string Value(AddressParts address, AddressField field)
    {
        if (field != AddressField.StreetAddress)
        {
            return address.Get(field).Trim();
        }

        StringBuilder joined = new();
        int count = 0;
        foreach (string street in address.StreetLines)
        {
            string trimmed = street.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (joined.Length > 0)
            {
                joined.Append('\n');
            }

            joined.Append(trimmed);
            count++;
        }

        // With several street lines whatever follows them in the format starts a new line.
        if (count > 1)
        {
            joined.Append('\n');
        }

        return joined.ToString();
    }
}
