using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Tests.Models;

/// <summary>The seam between the parsers and a model package, proved with models that return fixed labels.</summary>
public class ModelWiringTests
{
    private sealed class FixedAddressModel(params (string Needle, string Label)[] spans) : IAddressFieldModel
    {
        public int Calls { get; private set; }

        public IReadOnlyList<ModelSpan> Tag(string text)
        {
            Calls++;
            List<ModelSpan> result = [];
            foreach ((string needle, string label) in spans)
            {
                int at = text.IndexOf(needle, StringComparison.Ordinal);
                if (at >= 0)
                {
                    result.Add(new ModelSpan(at, needle.Length, label, 0.9f));
                }
            }

            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }
    }

    private sealed class FixedNameModel(NameClass cls, float confidence, params (string Needle, string Label)[] parts) : INameModel
    {
        public NameModelResult Analyze(string text) =>
            new(cls, confidence, [.. parts.Select(p => new ModelSpan(text.IndexOf(p.Needle, StringComparison.Ordinal), p.Needle.Length, p.Label, 0.8f))]);
    }

    private const string BritishRunOn = "58 Hordley Avenue Shrewsbury\nSY4 1AA";

    [Fact]
    public void ATier2AddressTheRulesRefuseIsReadFromTheModelsLabelsWithTheHintedCountry()
    {
        var model = new FixedAddressModel(("58", "street.house-number"), ("Hordley Avenue", "street.name"), ("Shrewsbury", "locality"), ("SY4 1AA", "postal-code"));
        using IDisposable scope = ContactModels.Use(model, null);
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse(BritishRunOn, Region.Parse("GB"), out ParsedAddress? a, out ParseDetail d));
        Assert.Equal(Region.Parse("GB"), a!.Country);
        Assert.Equal("SHREWSBURY", a.Locality!.Value.Standard);
        Assert.Equal("SY4 1AA", a.PostalCode!.Value.Standard);
        Assert.All(d.Fields, f => Assert.Equal(DecisionSource.Model, f.Source));
        Assert.Equal("58 HORDLEY AVENUE\nSHREWSBURY\nSY4 1AA", a.ToString(RecursiveAI.OneContact.AddressStyle.Postal));
        Assert.Equal(BritishRunOn.IndexOf("Hordley", StringComparison.Ordinal), a.Fields.First(f => f.Name == "street.name").Span.Start);
    }

    [Fact]
    public void ARuleParseOfATier2AddressIsNeverOverruledByTheModel()
    {
        var model = new FixedAddressModel(("Shrewsbury", "street.name"));
        using IDisposable scope = ContactModels.Use(model, null);
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse("58 Hordley Avenue\nShrewsbury\nSY4 1AA", Region.Parse("GB"), out ParsedAddress? a, out ParseDetail d));
        Assert.Equal("SHREWSBURY", a!.Locality!.Value.Standard);
        Assert.All(d.Fields, f => Assert.Equal(DecisionSource.Rule, f.Source));
    }

    private const string RunOn = "100 broadway new york ny 10001";

    [Fact]
    public void ARefusedAddressIsReadFromTheModelsLabels()
    {
        var model = new FixedAddressModel(("100", "street.house-number"), ("broadway", "street.name"), ("new york", "locality"), ("ny", "admin-area"), ("10001", "postal-code"));
        using IDisposable scope = ContactModels.Use(model, null);
        OperationStatus status = RuleAddressParser.TryParse(RunOn, default, out ParsedAddress? a, out ParseDetail d);
        Assert.Equal(OperationStatus.Done, status);
        Assert.NotNull(a);
        Assert.Equal("NEW YORK", a.Locality!.Value.Standard);
        Assert.Equal("NY", a.AdminArea!.Value.Standard);
        Assert.Equal("10001", a.PostalCode!.Value.Standard);
        Assert.Equal(Region.US, a.Country);
        Assert.All(d.Fields, f => Assert.Equal(DecisionSource.Model, f.Source));
        Assert.Equal("broadway", RunOn.Substring(a.Fields.First(f => f.Name == "street.name").Span.Start, 8));
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public void ARuleParseIsNeverOverruledByTheModel()
    {
        // The model disagrees with the rules about every field; the rules read the commas and keep their result.
        var model = new FixedAddressModel(("broadway", "locality"), ("new york", "street.name"));
        using IDisposable scope = ContactModels.Use(model, null);
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse("100 broadway, new york, ny 10001", default, out ParsedAddress? a, out ParseDetail d));
        Assert.Equal("NEW YORK", a!.Locality!.Value.Standard);
        Assert.All(d.Fields, f => Assert.Equal(DecisionSource.Rule, f.Source));
    }

    [Fact]
    public void LeadingLinesGetTheirLabelsFromTheModelAndNothingElseChanges()
    {
        const string text = "Pat Morgan\nAcme Corp\n100 Broadway\nNew York, NY 10001";
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse(text, default, out ParsedAddress? rules, out _));
        var model = new FixedAddressModel(("Pat Morgan", "recipient"), ("Acme Corp", "organization"));
        using IDisposable scope = ContactModels.Use(model, null);
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse(text, default, out ParsedAddress? a, out ParseDetail d));
        Assert.Equal(rules!.Locality, a!.Locality);
        Assert.Equal(rules.DeliveryLines.Count, a.DeliveryLines.Count);
        ParsedAddressField org = a.Fields.Single(f => f.Name == "organization");
        Assert.Equal("Acme Corp", org.Text);
        Assert.Equal(DecisionSource.Model, org.Source);
        Assert.Contains(d.Fields, f => f.Name == "recipient" && f.Source == DecisionSource.Model);
    }

    [Fact]
    public void WithoutACountryTheModelsReadingIsRefusedToo()
    {
        var model = new FixedAddressModel(("100", "street.house-number"), ("broadway", "street.name"), ("new york", "locality"));
        using IDisposable scope = ContactModels.Use(model, null);
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("100 broadway new york", default, out _, out ParseDetail d));
        Assert.NotEqual(default, d.PrimaryReason);
    }

    [Fact]
    public void AnUnknownNameTakesTheModelsClassAndParts()
    {
        const string text = "jsmith2019";
        Assert.Equal(NameClass.Unknown, NameParsing.Split(text).Class);
        using IDisposable scope = ContactModels.Use(null, new FixedNameModel(NameClass.Person, 0.9f, ("jsmith2019", "Given")));
        NameParseResult r = NameParsing.Split(text);
        Assert.Equal(NameClass.Person, r.Class);
        Assert.Single(r.Persons);
        Assert.Equal("Jsmith2019", r.Persons[0].Given.Text); // one-case input gets its casing restored
        Assert.All(r.Detail.Fields, f => Assert.Equal(DecisionSource.Model, f.Source));
        Assert.Equal(NameClass.Person, NameParsing.Classify(text));
    }

    [Fact]
    public void TheModelMayOverruleADefaultPersonReadingOnlyWhereItsNameDataSupportsIt()
    {
        // Two unfamiliar words: the rules take them as one person for want of any evidence of a company (person.no-evidence).
        const string text = "Zorblat Quux";
        Assert.Equal(NameClass.Person, NameParsing.Split(text).Class);
        using IDisposable scope = ContactModels.Use(null, new FixedNameModel(NameClass.Company, 0.9f, ("Zorblat Quux", "Base")));
        Assert.Equal(NameClass.Company, NameParsing.Split(text, NameHints.ForCountry(Region.US)).Class);
        Assert.Equal(NameClass.Company, NameParsing.Split(text, NameHints.ForCountry(Region.Parse("GB"))).Class);
        Assert.Equal(NameClass.Company, NameParsing.Classify(text, NameHints.ForCountry(Region.Parse("GB"))));

        // A Tier 2 country without an override reads only what the rules leave open.
        Assert.Equal(NameClass.Person, NameParsing.Split(text, NameHints.ForCountry(Region.Parse("PL"))).Class);
        Assert.Equal(NameClass.Person, NameParsing.Classify(text, NameHints.ForCountry(Region.Parse("PL"))));
        Assert.Equal(NameClass.Person, NameParsing.Split(text, NameHints.ForLanguage("pl")).Class);

        // Where it is allowed in a Tier 2 country the model must be surer (0.8) than for the United States (0.6).
        using IDisposable unsure = ContactModels.Use(null, new FixedNameModel(NameClass.Company, 0.7f, ("Zorblat Quux", "Base")));
        Assert.Equal(NameClass.Company, NameParsing.Split(text, NameHints.ForCountry(Region.US)).Class);
        Assert.Equal(NameClass.Person, NameParsing.Split(text, NameHints.ForCountry(Region.Parse("GB"))).Class);
    }

    [Fact]
    public void ANameTheRulesClassifiedIsNeverChangedAndALowConfidenceModelIsIgnored()
    {
        using IDisposable scope = ContactModels.Use(null, new FixedNameModel(NameClass.Company, 0.99f, ("Smith", "Base")));
        Assert.Equal(NameClass.Person, NameParsing.Split("John Smith").Class);
        using IDisposable inner = ContactModels.Use(null, new FixedNameModel(NameClass.Person, 0.4f, ("jsmith2019", "Given")));
        Assert.Equal(NameClass.Unknown, NameParsing.Split("jsmith2019").Class);
    }

    [Fact]
    public void APersonTakenOnlyByDefaultYieldsToAConfidentModelButAProvenOneDoesNot()
    {
        // Two plain words prove nothing: the rules take them as a person for lack of a sign of a company (issue person.no-evidence).
        const string plain = "Bestline Imports";
        Assert.Equal(NameClass.Person, NameParsing.Split(plain).Class);
        using (ContactModels.Use(null, new FixedNameModel(NameClass.Company, 0.9f, ("Bestline", "Base"))))
        {
            Assert.Equal(NameClass.Company, NameParsing.Split(plain).Class);
            Assert.Equal(NameClass.Company, NameParsing.Classify(plain));
            // A title is a sign of a person; the model does not overrule it.
            Assert.Equal(NameClass.Person, NameParsing.Split("Dr. Bestline Imports").Class);
        }

        using (ContactModels.Use(null, new FixedNameModel(NameClass.Company, 0.4f, ("Bestline", "Base"))))
        {
            Assert.Equal(NameClass.Person, NameParsing.Split(plain).Class);
        }
    }

    [Fact]
    public void ForATier2TextThePackageWithTier2ModelsLeavesTheRulesDefaultPersonAlone()
    {
        // The Tier 1 + Tier 2 package learned Tier 2 person names from small lists, so there it reads only what the rules leave open; United States and Canada texts keep the override.
        const string plain = "Bestline Imports";
        using (ContactModels.Use(null, new FixedNameModel(NameClass.Company, 0.9f, ("Bestline", "Base"))))
        {
            Assert.Equal(NameClass.Person, NameParsing.Split(plain, NameHints.ForCountry(Region.Parse("DE"))).Class);
            Assert.Equal(NameClass.Person, NameParsing.Classify(plain, NameHints.ForCountry(Region.Parse("PL"))));
            Assert.Equal(NameClass.Person, NameParsing.Split(plain, NameHints.ForLanguage("de")).Class);
            Assert.Equal(NameClass.Company, NameParsing.Split(plain, NameHints.ForCountry(Region.US)).Class);
            Assert.Equal(NameClass.Company, NameParsing.Split(plain, NameHints.ForCountry(Region.CA)).Class);
        }
    }
}
