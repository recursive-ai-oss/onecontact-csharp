using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Tier1;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Models.Tests;

/// <summary>
/// The Tier 1 package. Tests that need trained weights read them from the file named by the environment variable ONECONTACT_TIER1_MODEL
/// (a pipeline file written by the Tier 1 recipe) and are skipped without it, so a development build still passes.
/// </summary>
public class Tier1Tests
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

    [Fact]
    public void AReferencedPackageRegistersItselfAndNamesItself()
    {
        Tier1Model.Register();
        Assert.Equal("RecursiveAI.OneContact.Models.Tier1", ContactModels.PackageId);
        Assert.NotNull(ContactModels.Address);
        Assert.NotNull(ContactModels.Name);
        Tier1Model.Register();
        Assert.Equal("RecursiveAI.OneContact.Models.Tier1", ContactModels.PackageId);
    }

    [Fact]
    public void ABuildWithoutWeightsSaysSo()
    {
        // A development build has no embedded weights; the message names the package and what was missing.
        if (typeof(Tier1Model).Assembly.GetManifestResourceStream("weights/tier1.onenlp") is not null)
        {
            return;
        }

        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => Tier1Model.Instance);
        Assert.Contains("RecursiveAI.OneContact.Models.Tier1", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAddressRefusalNamesThePackageWhenNoModelIsRegistered()
    {
        Assert.Contains("RecursiveAI.OneContact.Models.Tier1", ReasonCode.AddressModelRequired.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTokenizerKeepsLineBreaksAsWordsAndCutsAtSpanEdges()
    {
        List<(int Start, int Length)> words = Tier1Tokenizer.Tokenize("12 Main St.\nAlbany, NY");
        Assert.Equal(["12", "Main", "St", ".", "\n", "Albany", ",", "NY"], words.Select(w => "12 Main St.\nAlbany, NY".Substring(w.Start, w.Length)));
        List<(int Start, int Length)> cut = Tier1Tokenizer.Tokenize("4B", [1]);
        Assert.Equal(2, cut.Count);
    }

    [Fact]
    public void TheModelLabelsAnAddressAndOffsetsIndexTheInput()
    {
        Tier1Model? model = LoadModel();
        Assert.SkipWhen(model is null, "ONECONTACT_TIER1_MODEL is not set");
        const string text = "Pat Morgan\n100 Broadway Apt 4\nNew York, NY 10001";
        IReadOnlyList<ModelSpan> spans = model!.Tag(text);
        Assert.NotEmpty(spans);
        foreach (ModelSpan s in spans)
        {
            Assert.InRange(s.Start, 0, text.Length - 1);
            Assert.InRange(s.End, s.Start + 1, text.Length);
            Assert.InRange(s.Confidence, 0f, 1f);
        }

        Assert.Contains(spans, s => s.Label == "postal-code" && text.Substring(s.Start, s.Length) == "10001");
    }

    [Fact]
    public void TheModelClassifiesNames()
    {
        Tier1Model? model = LoadModel();
        Assert.SkipWhen(model is null, "ONECONTACT_TIER1_MODEL is not set");
        Assert.Equal(NameClass.Person, model!.Analyze("Dr. Maria Garcia").Class);
        Assert.Equal(NameClass.Company, model.Analyze("Northwind Traders LLC").Class);
    }

    [Fact]
    public void TheEmbeddedWeightsLoadWhenThePackageWasBuiltWithThem()
    {
        Assert.SkipWhen(typeof(Tier1Model).Assembly.GetManifestResourceStream("weights/tier1.onenlp") is null, "built without weights");
        Tier1Model.Register();
        Assert.Equal(NameClass.Company, ContactModels.Name!.Analyze("Northwind Traders LLC").Class);
        Assert.Contains(ContactModels.Address!.Tag("100 Broadway, New York, NY 10001"), s => s.Label == "locality");
    }

    [Fact]
    public void TheModelIsDeterministic()
    {
        Tier1Model? model = LoadModel();
        Assert.SkipWhen(model is null, "ONECONTACT_TIER1_MODEL is not set");
        const string text = "742 Evergreen Terrace, Springfield, OR 97477";
        Assert.Equal(model!.Tag(text), model.Tag(text));
    }

    [Fact]
    public void ARefusedAddressIsReadThroughTheRegisteredPackage()
    {
        Tier1Model? model = LoadModel();
        Assert.SkipWhen(model is null, "ONECONTACT_TIER1_MODEL is not set");
        // Register the loaded copy the way the package does with its embedded weights.
        ContactModels.Register("RecursiveAI.OneContact.Models.Tier1", 99, model, model);
        Assert.Equal(System.Buffers.OperationStatus.Done, RuleAddressParser.TryParse("100 broadway new york ny 10001", default, out ParsedAddress? a, out ParseDetail d));
        Assert.True(a!.AdminArea?.Standard == "NY", string.Join("; ", model.Tag("100 broadway new york ny 10001").Select(x => x.Label + "@" + x.Start + "+" + x.Length)));
        Assert.Contains(d.Fields, f => f.Source == DecisionSource.Model && f.Confidence is > 0 and <= 1);
    }
}
