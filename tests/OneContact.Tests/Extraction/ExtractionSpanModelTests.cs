using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Models;

namespace RecursiveAI.OneContact.Tests;

/// <summary>The contact-span model in the text half of extraction, proved with a model that returns fixed spans.</summary>
public sealed class ExtractionSpanModelTests
{
    private sealed class FixedSpanModel(string[] starts, params (string Needle, string Label, float Confidence)[] spans) : IContactSpanModel
    {
        public ContactSpanResult Find(string text)
        {
            var found = new List<ModelSpan>();
            foreach ((string needle, string label, float confidence) in spans)
            {
                int at = text.IndexOf(needle, StringComparison.Ordinal);
                if (at >= 0)
                {
                    found.Add(new ModelSpan(at, needle.Length, label, confidence));
                }
            }

            found.Sort((a, b) => a.Start.CompareTo(b.Start));
            return new ContactSpanResult(found, [.. starts.Select(s => text.IndexOf(s, StringComparison.Ordinal)).Where(i => i >= 0)]);
        }
    }

    private const string Note = "Spoke with Jane Smith about the renewal.\nShe can be reached on jane.smith@acme.example.";

    [Fact]
    public void WithoutASpanModelExtractionIsRulesOnly()
    {
        ExtractResult r = new ContactExtractor().ExtractText(Note);
        Assert.DoesNotContain(r.Fields, f => f.Decision == DecisionSource.Model);
        Assert.Equal(["jane.smith@acme.example"], r.Of(FieldKind.Email).Select(f => f.Text));
    }

    [Fact]
    public void AFieldTheModelFindsWhereRulesFoundNothingSaysModelWithItsConfidenceAndOffsets()
    {
        var model = new FixedSpanModel([], ("Jane Smith", "person", 0.9f));
        using IDisposable scope = ContactModels.Scope(null, null, model);
        ExtractResult r = new ContactExtractor().ExtractText(Note);
        ExtractedField name = Assert.Single(r.Of(FieldKind.Name));
        Assert.Equal("Jane Smith", name.Text);
        Assert.Equal(DecisionSource.Model, name.Decision);
        Assert.Equal(Note.IndexOf("Jane", StringComparison.Ordinal), name.Span.Start);
        Assert.Equal(10, name.Span.Length);
        Assert.Equal(0.9, name.Confidence, 3);
        Assert.Equal(DecisionSource.Rule, Assert.Single(r.Of(FieldKind.Email)).Decision);
    }

    [Fact]
    public void AModelSpanOverlappingARuleHitIsDroppedAndTheRuleKeepsTheField()
    {
        var model = new FixedSpanModel([], ("smith@acme.example", "email", 0.99f), ("Jane Smith", "company", 0.2f));
        using IDisposable scope = ContactModels.Scope(null, null, model);
        ExtractResult r = new ContactExtractor().ExtractText(Note);
        ExtractedField email = Assert.Single(r.Of(FieldKind.Email));
        Assert.Equal("jane.smith@acme.example", email.Text);
        Assert.Equal(DecisionSource.Rule, email.Decision);
        Assert.Empty(r.Of(FieldKind.Organization));
    }

    [Fact]
    public void AValueTheCoreParsersRefuseIsNotAFieldWhateverTheModelSays()
    {
        var model = new FixedSpanModel([], ("renewal", "email", 0.99f), ("about", "website", 0.99f));
        using IDisposable scope = ContactModels.Scope(null, null, model);
        ExtractResult r = new ContactExtractor().ExtractText(Note);
        Assert.Single(r.Of(FieldKind.Email));
        Assert.Empty(r.Of(FieldKind.Website));
    }

    [Fact]
    public void ABlockStartTheModelSeesSplitsTwoGroupsThatTheLayoutAloneWouldJoin()
    {
        const string signatures = "Jane Smith\njane@acme.example\n555-010-0199\nTom Jones\n555-010-0188";
        var extractor = new ContactExtractor { DefaultRegion = Region.US };
        var model = new FixedSpanModel(["Tom Jones"], ("Jane Smith", "person", 0.9f), ("Tom Jones", "person", 0.9f));
        using (ContactModels.Scope(null, null, model))
        {
            ExtractResult r = extractor.ExtractText(signatures);
            Assert.Equal(2, r.Records.Count);
            Assert.Equal(["Jane Smith", "Tom Jones"], r.Records.SelectMany(rec => rec.Fields).Where(f => f.Kind == FieldKind.Name).Select(f => f.Text).Order());
        }

        // The same text without the block start is one group: the layout has no reason to cut it.
        using (ContactModels.Scope(null, null, new FixedSpanModel([], ("Jane Smith", "person", 0.9f), ("Tom Jones", "person", 0.9f))))
        {
            Assert.Single(extractor.ExtractText(signatures).Records);
        }
    }
}
