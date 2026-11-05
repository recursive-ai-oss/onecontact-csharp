using System.Collections.ObjectModel;
using RecursiveAI.OneContact.Companies;

namespace RecursiveAI.OneContact.Names;

/// <summary>
/// The result of reading a name string whose kind is not known in advance: what it is, and the people or the company in it.
/// </summary>
public sealed class NameParseResult
{
    private static readonly ReadOnlyCollection<PersonName> NoPersons = new([]);

    internal NameParseResult(NameClass nameClass, IReadOnlyList<PersonName> persons, CompanyName company, ParseDetail detail)
    {
        Class = nameClass;
        Persons = persons.Count == 0 ? NoPersons : persons;
        Company = company;
        Detail = detail;
    }

    /// <summary>Gets what the rules decided the text is. <see cref="NameClass.Unknown"/> means they could not decide; <see cref="Detail"/> says why.</summary>
    public NameClass Class { get; }

    /// <summary>
    /// Gets the people found, in the order they were written. One for <see cref="NameClass.Person"/>, two or more for
    /// <see cref="NameClass.Multiple"/>, and the single word for an <see cref="NameClass.Unknown"/> text that is one bare word. Each
    /// person's offsets index the original input; a family name shared by several people points at where it was written.
    /// </summary>
    public IReadOnlyList<PersonName> Persons { get; }

    /// <summary>Gets the company for <see cref="NameClass.Company"/>; the default value for every other class.</summary>
    public CompanyName Company { get; }

    /// <summary>
    /// Gets the issues and the parsed fields. Field names are those of <see cref="PersonName"/> (<c>given</c>, <c>family</c>, ...) or
    /// <see cref="CompanyName"/> (<c>base</c>, <c>legal-form</c>, ...); for several people each name is prefixed with <c>person1.</c>,
    /// <c>person2.</c> and so on. Every field was decided by a rule.
    /// </summary>
    public ParseDetail Detail { get; }
}

/// <summary>
/// Reading name strings without knowing their kind: classification into person, company, several people or unknown, and splitting
/// several people apart. Rules only (legal forms of the ISO 20275 list, company words, connectors, titles); what they cannot settle is
/// <see cref="NameClass.Unknown"/> and says why. Nothing here looks at the current culture.
/// </summary>
public static class NameParsing
{
    /// <summary>Tells what a name string is.</summary>
    /// <param name="text">The text.</param>
    /// <param name="hints">The country and language the text is written in; neither is needed.</param>
    /// <returns>The class.</returns>
    public static NameClass Classify(ReadOnlySpan<char> text, NameHints hints = default)
    {
        Analysis analysis = NameAnalyzer.Analyze(text, hints);
        return analysis.Class is NameClass.Unknown or NameClass.Person ? ModelNameReader.Classify(text, analysis) : analysis.Class;
    }

    /// <summary>Reads a name string of unknown kind.</summary>
    /// <param name="text">The text.</param>
    /// <param name="hints">The country and language the text is written in; neither is needed.</param>
    /// <returns>The class and the people or company in it. Never throws for bad input; an empty or unreadable text is <see cref="NameClass.Unknown"/> with an error issue.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static NameParseResult Split(string text, NameHints hints = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Split(text.AsSpan(), hints);
    }

    /// <summary>Reads a name string of unknown kind.</summary>
    /// <param name="text">The text.</param>
    /// <param name="hints">The country and language the text is written in; neither is needed.</param>
    /// <returns>The class and the people or company in it. Never throws for bad input; an empty or unreadable text is <see cref="NameClass.Unknown"/> with an error issue.</returns>
    public static NameParseResult Split(ReadOnlySpan<char> text, NameHints hints = default)
    {
        Analysis analysis = NameAnalyzer.Analyze(text, hints);

        // Rules first: only a string the rules left unknown, or took as a person by default, goes to the name model, when a model package is referenced.
        if (analysis.Class is NameClass.Unknown or NameClass.Person && ModelNameReader.TryRead(text, hints, analysis, out NameParseResult? fromModel))
        {
            return fromModel;
        }

        if (analysis.Class == NameClass.Company)
        {
            CompanyName.TryParse(text, hints, out CompanyName company, out ParseDetail companyDetail);
            return new NameParseResult(NameClass.Company, [], company, companyDetail);
        }

        List<PersonName> persons = [];
        List<ParseIssue> issues = [.. analysis.Issues];
        List<ParsedField> fields = [];
        for (int i = 0; i < analysis.Persons.Count; i++)
        {
            PersonName person = PersonNameBuilder.Build(text, analysis.Persons[i], analysis.RestoreCase, hints, out ParseDetail detail);
            persons.Add(person);
            if (analysis.Persons.Count == 1)
            {
                fields.AddRange(detail.Fields);
                issues.Clear();
                issues.AddRange(detail.Issues);
                continue;
            }

            string prefix = string.Concat("person", (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), ".");
            foreach (ParsedField f in detail.Fields)
            {
                fields.Add(f with { Name = prefix + f.Name });
            }

            foreach (ParseIssue issue in detail.Issues)
            {
                issues.Add(issue);
            }
        }

        return new NameParseResult(analysis.Class, persons, default, new ParseDetail(issues, fields));
    }

    /// <summary>Reads many name strings, one result for each, in order.</summary>
    /// <param name="texts">The texts.</param>
    /// <param name="hints">The hints applied to every text.</param>
    /// <returns>The results.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="texts"/> is <see langword="null"/>.</exception>
    public static IEnumerable<NameParseResult> SplitMany(IEnumerable<string> texts, NameHints hints = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        foreach (string text in texts)
        {
            yield return Split(text ?? string.Empty, hints);
        }
    }
}
