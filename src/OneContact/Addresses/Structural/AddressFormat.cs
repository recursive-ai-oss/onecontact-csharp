using System.Text;

namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>
/// A parsed address format string such as <c>%N%n%O%n%A%n%C, %S %Z</c>. <c>%N</c> recipient, <c>%O</c> organization, <c>%A</c>
/// street address lines, <c>%D</c> dependent locality, <c>%C</c> locality, <c>%S</c> administrative area, <c>%Z</c> postal code,
/// <c>%X</c> sorting code, <c>%n</c> line break; everything else is literal text. The format is split into lines, and each line
/// into runs of literal text and fields, so that rendering can drop the literal text that belongs to a field with no value.
/// </summary>
internal sealed class AddressFormat
{
    private AddressFormat(string text, Line[] lines, bool[] used)
    {
        Text = text;
        Lines = lines;
        Used = used;
    }

    /// <summary>Gets the format string as written in the data.</summary>
    internal string Text { get; }

    /// <summary>Gets the lines of the format.</summary>
    internal Line[] Lines { get; }

    /// <summary>Gets, by <see cref="AddressField"/> value, whether the format places that field.</summary>
    internal bool[] Used { get; }

    /// <summary>Gets the fields in the order the format places them, each once.</summary>
    internal IReadOnlyList<AddressField> FieldOrder
    {
        get
        {
            List<AddressField> order = [];
            foreach (Line line in Lines)
            {
                foreach (Piece piece in line.Pieces)
                {
                    if (piece.IsField && !order.Contains(piece.Field))
                    {
                        order.Add(piece.Field);
                    }
                }
            }

            return order;
        }
    }

    /// <summary>Parses a format string.</summary>
    /// <param name="text">The format.</param>
    /// <returns>The parsed format.</returns>
    /// <exception cref="FormatException">The format uses a <c>%</c> code that is not defined.</exception>
    internal static AddressFormat Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<Line> lines = [];
        List<Piece> pieces = [];
        StringBuilder literal = new();
        bool[] used = new bool[Enum.GetValues<AddressField>().Length];

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                pieces.Add(new Piece(false, default, literal.ToString()));
                literal.Clear();
            }
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '%')
            {
                literal.Append(c);
                continue;
            }

            if (i + 1 >= text.Length)
            {
                throw new FormatException("An address format ends with a lone '%'.");
            }

            char code = text[++i];
            if (code == 'n')
            {
                FlushLiteral();
                lines.Add(new Line([.. pieces]));
                pieces.Clear();
                continue;
            }

            if (!TryField(code, out AddressField field))
            {
                throw new FormatException($"An address format uses the undefined code '%{code}'.");
            }

            FlushLiteral();
            pieces.Add(new Piece(true, field, string.Empty));
            used[(int)field] = true;
        }

        FlushLiteral();
        lines.Add(new Line([.. pieces]));
        return new AddressFormat(text, [.. lines], used);
    }

    /// <summary>Maps a format code letter to its field.</summary>
    /// <param name="code">The letter after the <c>%</c>.</param>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> when the letter is a field code.</returns>
    internal static bool TryField(char code, out AddressField field)
    {
        switch (code)
        {
            case 'N':
                field = AddressField.Recipient;
                return true;
            case 'O':
                field = AddressField.Organization;
                return true;
            case 'A':
                field = AddressField.StreetAddress;
                return true;
            case 'D':
                field = AddressField.DependentLocality;
                return true;
            case 'C':
                field = AddressField.Locality;
                return true;
            case 'S':
                field = AddressField.AdminArea;
                return true;
            case 'Z':
                field = AddressField.PostalCode;
                return true;
            case 'X':
                field = AddressField.SortingCode;
                return true;
            default:
                field = default;
                return false;
        }
    }

    /// <summary>One line of a format.</summary>
    /// <param name="Pieces">The runs of literal text and fields, in order.</param>
    internal readonly record struct Line(Piece[] Pieces);

    /// <summary>A run of literal text or a field.</summary>
    /// <param name="IsField">Whether this is a field (otherwise literal text).</param>
    /// <param name="Field">The field, when <paramref name="IsField"/>.</param>
    /// <param name="Literal">The text, when not a field.</param>
    internal readonly record struct Piece(bool IsField, AddressField Field, string Literal);
}
