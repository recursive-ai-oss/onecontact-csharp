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

    // No word in front of the name points at it, so the rules leave it to the model.
    private const string Note = "Renewal notes: Quillon Marchetti has the details.\nShe can be reached on info@acme.example.";

    [Fact]
    public void WithoutASpanModelExtractionIsRulesOnly()
    {
        ExtractResult r = new ContactExtractor().ExtractText(Note);
        Assert.DoesNotContain(r.Fields, f => f.Decision == DecisionSource.Model);
        Assert.Equal(["info@acme.example"], r.Of(FieldKind.Email).Select(f => f.Text));
    }

    [Fact]
    public void AFieldTheModelFindsWhereRulesFoundNothingSaysModelWithItsConfidenceAndOffsets()
    {
        var model = new FixedSpanModel([], ("Quillon Marchetti", "person", 0.9f));
        using IDisposable scope = ContactModels.Scope(null, null, model);
        ExtractResult r = new ContactExtractor().ExtractText(Note);
        ExtractedField name = Assert.Single(r.Of(FieldKind.Name));
        Assert.Equal("Quillon Marchetti", name.Text);
        Assert.Equal(DecisionSource.Model, name.Decision);
        Assert.Equal(Note.IndexOf("Quillon", StringComparison.Ordinal), name.Span.Start);
        Assert.Equal(17, name.Span.Length);
        Assert.Equal(0.9, name.Confidence, 3);
        Assert.Equal(DecisionSource.Rule, Assert.Single(r.Of(FieldKind.Email)).Decision);
    }

    [Fact]
    public void AModelSpanOverlappingARuleHitIsDroppedAndTheRuleKeepsTheField()
    {
        var model = new FixedSpanModel([], ("info@acme.example", "email", 0.99f), ("Quillon Marchetti", "company", 0.2f));
        using IDisposable scope = ContactModels.Scope(null, null, model);
        ExtractResult r = new ContactExtractor().ExtractText(Note);
        ExtractedField email = Assert.Single(r.Of(FieldKind.Email));
        Assert.Equal("info@acme.example", email.Text);
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
        const string signatures = "Quillon Marchetti\njane@acme.example\n555-010-0199\nTom Jones\n555-010-0188";
        var extractor = new ContactExtractor { DefaultRegion = Region.US };
        var model = new FixedSpanModel(["Tom Jones"], ("Quillon Marchetti", "person", 0.9f), ("Tom Jones", "person", 0.9f));
        using (ContactModels.Scope(null, null, model))
        {
            ExtractResult r = extractor.ExtractText(signatures);
            Assert.Equal(2, r.Records.Count);
            Assert.Equal(["Quillon Marchetti", "Tom Jones"], r.Records.SelectMany(rec => rec.Fields).Where(f => f.Kind == FieldKind.Name).Select(f => f.Text).Order());
        }

        // The same text without the block start is one group: the layout has no reason to cut it.
        using (ContactModels.Scope(null, null, new FixedSpanModel([], ("Quillon Marchetti", "person", 0.9f), ("Tom Jones", "person", 0.9f))))
        {
            Assert.Single(extractor.ExtractText(signatures).Records);
        }
    }

    private sealed class Restore(bool was) : IDisposable
    {
        public void Dispose() => RecursiveAI.OneContact.Extraction.Text.TextFieldReader.ModelOnWeb = was;
    }

    // The gate on web pages is off by default; these tests turn it on for their own duration.
    private static Restore WebModelOn()
    {
        var was = new Restore(RecursiveAI.OneContact.Extraction.Text.TextFieldReader.ModelOnWeb);
        RecursiveAI.OneContact.Extraction.Text.TextFieldReader.ModelOnWeb = true;
        return was;
    }

    [Fact]
    public void ByDefaultAWebPageIsReadByRulesAndMarkupWhateverModelIsRegistered()
    {
        const string near = "<html><body><p>Renewal notes: Quillon Marchetti has the details.</p><p>She can be reached on info@acme.example.</p></body></html>";
        var model = new FixedSpanModel(["Quillon Marchetti"], ("Quillon Marchetti", "person", 0.9f));
        using IDisposable scope = ContactModels.Scope(null, null, model);
        Assert.DoesNotContain(new ContactExtractor().ExtractHtml(near).Fields, f => f.Decision == DecisionSource.Model);
    }

    [Fact]
    public void OnAWebPageTheModelsPeopleCountOnlyInsideAContactBlock()
    {
        // A name next to an email address is read; the same name in prose far from any contact detail is not (the model's names on whole
        // pages were mostly navigation, product names and bylines).
        const string near = "<html><body><p>Renewal notes: Quillon Marchetti has the details.</p><p>She can be reached on info@acme.example.</p></body></html>";
        string far = "<html><body><p>Renewal notes: Quillon Marchetti has the details.</p>" + string.Concat(Enumerable.Repeat("<p>The quarterly report is attached.</p>", 8))
            + "<p>Mail info@acme.example for more.</p></body></html>";
        var model = new FixedSpanModel(["Quillon Marchetti"], ("Quillon Marchetti", "person", 0.9f));
        using IDisposable scope = ContactModels.Scope(null, null, model);
        using IDisposable gate = WebModelOn();
        ExtractedField inBlock = Assert.Single(new ContactExtractor().ExtractHtml(near).Of(FieldKind.Name));
        Assert.Equal(DecisionSource.Model, inBlock.Decision);
        Assert.Empty(new ContactExtractor().ExtractHtml(far).Of(FieldKind.Name));
        Assert.DoesNotContain(new ContactExtractor().ExtractHtml(near).Fields, f => f.Kind != FieldKind.Name && f.Decision == DecisionSource.Model);
    }

    [Fact]
    public void OnAWebPageAModelPersonBelowTheWebCutIsDropped()
    {
        const string near = "<html><body><p>Renewal notes: Quillon Marchetti has the details.</p><p>She can be reached on info@acme.example.</p></body></html>";
        var model = new FixedSpanModel(["Quillon Marchetti"], ("Quillon Marchetti", "person", 0.5f));
        using IDisposable scope = ContactModels.Scope(null, null, model);
        using IDisposable gate = WebModelOn();
        Assert.Empty(new ContactExtractor().ExtractHtml(near).Of(FieldKind.Name));
        Assert.Single(new ContactExtractor().ExtractText("Renewal notes: Quillon Marchetti has the details.\nShe can be reached on info@acme.example.").Of(FieldKind.Name));
    }

    [Fact]
    public void OnAWebPageTheModelsAddressesPhonesAndEmailsAreNotUsed()
    {
        const string html = "<html><body><p>Renewal notes: Quillon Marchetti has the details.</p><p>Write to info@acme.example</p><p>Oak Lane 12</p></body></html>";
        var model = new FixedSpanModel([], ("Oak Lane 12", "address", 0.99f), ("Quillon Marchetti", "person", 0.9f));
        using IDisposable scope = ContactModels.Scope(null, null, model);
        using IDisposable gate = WebModelOn();
        ExtractResult page = new ContactExtractor().ExtractHtml(html);
        Assert.Empty(page.Of(FieldKind.Address));
    }
}
