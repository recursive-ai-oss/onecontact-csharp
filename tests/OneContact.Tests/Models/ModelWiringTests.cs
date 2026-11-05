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

    private const string RunOn = "100 broadway new york ny 10001";

    [Fact]
    public void ARefusedAddressIsReadFromTheModelsLabels()
    {
        var model = new FixedAddressModel(("100", "street.house-number"), ("broadway", "street.name"), ("new york", "locality"), ("ny", "admin-area"), ("10001", "postal-code"));
        using IDisposable scope = ContactModels.Scope(model, null);
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
        using IDisposable scope = ContactModels.Scope(model, null);
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
        using IDisposable scope = ContactModels.Scope(model, null);
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
        using IDisposable scope = ContactModels.Scope(model, null);
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("100 broadway new york", default, out _, out ParseDetail d));
        Assert.NotEqual(default, d.PrimaryReason);
    }

    [Fact]
    public void AnUnknownNameTakesTheModelsClassAndParts()
    {
        const string text = "jsmith2019";
        Assert.Equal(NameClass.Unknown, NameParsing.Split(text).Class);
        using IDisposable scope = ContactModels.Scope(null, new FixedNameModel(NameClass.Person, 0.9f, ("jsmith2019", "Given")));
        NameParseResult r = NameParsing.Split(text);
        Assert.Equal(NameClass.Person, r.Class);
        Assert.Single(r.Persons);
        Assert.Equal("Jsmith2019", r.Persons[0].Given.Text); // one-case input gets its casing restored
        Assert.All(r.Detail.Fields, f => Assert.Equal(DecisionSource.Model, f.Source));
        Assert.Equal(NameClass.Person, NameParsing.Classify(text));
    }

    [Fact]
    public void ANameTheRulesClassifiedIsNeverChangedAndALowConfidenceModelIsIgnored()
    {
        using IDisposable scope = ContactModels.Scope(null, new FixedNameModel(NameClass.Company, 0.99f, ("Smith", "Base")));
        Assert.Equal(NameClass.Person, NameParsing.Split("John Smith").Class);
        using IDisposable inner = ContactModels.Scope(null, new FixedNameModel(NameClass.Person, 0.4f, ("jsmith2019", "Given")));
        Assert.Equal(NameClass.Unknown, NameParsing.Split("jsmith2019").Class);
    }
}
