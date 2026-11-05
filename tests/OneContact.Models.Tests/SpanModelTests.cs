using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Tier1;

namespace RecursiveAI.OneContact.Models.Tests;

/// <summary>
/// The contact-span head. Tests that need trained weights read them from the file named by ONECONTACT_TIER1_MODEL (a pipeline file with the
/// span head) and are skipped without it.
/// </summary>
public class SpanModelTests
{
    private static Tier1Model? LoadModel()
    {
        string? path = Environment.GetEnvironmentVariable("ONECONTACT_TIER1_MODEL");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return Tier1Model.Load(stream);
    }

    // Registration is process-wide and cannot be undone, so the test registers this once and switches it.
    private sealed class Switchable(IContactSpanModel inner) : IContactSpanModel
    {
        public bool On { get; set; }

        public ContactSpanResult Find(string text) => On ? inner.Find(text) : new ContactSpanResult([], []);
    }

    private const string Signature = "Best regards\nJohn Carter\nAcme Plumbing LLC\n417 Maple Avenue, Springfield, IL 62701\nTel: (217) 555-0143\njcarter@acmeplumbing.example\nwww.acmeplumbing.example";

    [Fact]
    public void TheHeadFindsTheSixKindsOfFieldInASignatureWithOffsetsIntoTheText()
    {
        Tier1Model? model = LoadModel();
        if (model is null)
        {
            return;
        }

        ContactSpanResult r = model.Find(Signature);
        Assert.All(r.Spans, s => Assert.Equal(Signature.Substring(s.Start, s.Length).Trim(), Signature.Substring(s.Start, s.Length)));
        string Of(string label) => string.Join("|", r.Spans.Where(s => s.Label == label).Select(s => Signature.Substring(s.Start, s.Length)));
        Assert.Equal("John Carter", Of("person"));
        Assert.Equal("Acme Plumbing LLC", Of("company"));
        Assert.Contains("417 Maple Avenue", Of("address"), StringComparison.Ordinal);
        Assert.Contains("555-0143", Of("phone"), StringComparison.Ordinal);
        Assert.Equal("jcarter@acmeplumbing.example", Of("email"));
        Assert.Equal("www.acmeplumbing.example", Of("website"));
        Assert.NotEmpty(r.BlockStarts);
        Assert.All(r.Spans, s => Assert.InRange(s.Confidence, 0f, 1f));
    }

    [Fact]
    public void TheHeadIsDeterministicAndReadsTheSameTextTheSameWay()
    {
        Tier1Model? model = LoadModel();
        if (model is null)
        {
            return;
        }

        Assert.Equal(model.Find(Signature).Spans, model.Find(Signature).Spans);
    }

    [Fact]
    public void ProseWithNoContactDetailsGivesNoFields()
    {
        Tier1Model? model = LoadModel();
        if (model is null)
        {
            return;
        }

        ContactSpanResult r = model.Find("The trail will be held on the first Tuesday of each month.\nThe quarterly report has been updated.");
        Assert.Empty(r.Spans);
    }

    [Fact]
    public void ExtractionWithTheModelKeepsEveryRuleFieldAndAddsModelFieldsWithTheirSource()
    {
        Tier1Model? model = LoadModel();
        if (model is null)
        {
            return;
        }

        var switchable = new Switchable(model);
        // Registration is process-wide and the first registration of a rank wins. Tier1Tests registers rank 99 without a span model, and test classes
        // run in any order, so this one takes a rank above it; it carries the same address and name models, so both orders give the same behaviour.
        ContactModels.Register("RecursiveAI.OneContact.Models.Tier1.Test", 100, model, model, switchable);
        ExtractResult rulesOnly = new ContactExtractor { Sources = ExtractionSources.Text, DefaultRegion = Region.US }.ExtractText(Signature);
        switchable.On = true;
        ExtractResult withModel = new ContactExtractor { Sources = ExtractionSources.Text, DefaultRegion = Region.US }.ExtractText(Signature);
        foreach (ExtractedField f in rulesOnly.Fields)
        {
            Assert.Contains(withModel.Fields, g => g.Kind == f.Kind && g.Span == f.Span && g.Decision == DecisionSource.Rule);
        }

        ExtractedField person = Assert.Single(withModel.Of(FieldKind.Name));
        Assert.Equal("John Carter", person.Text);
        Assert.Equal(DecisionSource.Model, person.Decision);
        Assert.InRange(person.Confidence, 0.5, 1.0);
    }
}
