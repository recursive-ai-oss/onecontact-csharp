using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Accurate;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Models.Accurate.Tests;

/// <summary>
/// The accurate-tier package. Tests that need trained weights read them from the file named by ONECONTACT_ACCURATE_MODEL (a pipeline written by the
/// accurate recipe) and are skipped without it, so a development build still passes.
/// </summary>
public class AccurateTests
{
    private const string Signature = "Jane Doe\nAcme Holdings Ltd\n1600 Pennsylvania Avenue NW, Washington, DC 20500\nTel: +1 202 456 1111\njane.doe@example.com\nwww.example.com";

    private static string? Path()
    {
        string? p = Environment.GetEnvironmentVariable("ONECONTACT_ACCURATE_MODEL");
        return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
    }

    [Fact]
    public void ABuildWithoutWeightsNamesThePackage()
    {
        if (typeof(AccurateModel).Assembly.GetManifestResourceStream("weights/accurate.onenlp") is not null)
        {
            return;
        }

        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => AccurateModel.Instance);
        Assert.Contains("RecursiveAI.OneContact.Models.Accurate", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRefusalNamesTheAccuratePackage()
    {
        Assert.Contains(AccurateModel.PackageId, ReasonCode.AddressModelRequired.Description, StringComparison.Ordinal);
        Assert.Equal("RecursiveAI.OneContact.Models.Accurate", AccurateModel.PackageId);
    }

    [Fact]
    public void NothingRegisteredTheFastTierAloneTheAccurateTierAloneThenBoth()
    {
        // Nothing referenced: no model of any kind.
        Assert.Null(ContactModels.PackageId);
        Assert.Null(ContactModels.Spans);

        // The accurate tier alone serves addresses, names and spans (it registers for normalization at the lowest rank).
        var accurate = new Stub();
        ContactModels.Register(AccurateModel.PackageId, 3, accurate, accurate, accurate, normalizationRank: 0);
        Assert.Equal(AccurateModel.PackageId, ContactModels.PackageId);
        Assert.Same(accurate, ContactModels.Address);
        Assert.Same(accurate, ContactModels.Name);
        Assert.Same(accurate, ContactModels.Spans);

        // A fast-tier package registered afterwards takes over addresses and names; the accurate tier stays the span model for running text.
        var fast = new Stub();
        ContactModels.Register("RecursiveAI.OneContact.Models", 2, fast, fast, fast);
        Assert.Equal("RecursiveAI.OneContact.Models", ContactModels.PackageId);
        Assert.Same(fast, ContactModels.Address);
        Assert.Same(fast, ContactModels.Name);
        Assert.Same(accurate, ContactModels.Spans);

        // Registering the accurate tier again changes nothing: the order of registration does not matter.
        ContactModels.Register(AccurateModel.PackageId, 3, accurate, accurate, accurate, normalizationRank: 0);
        Assert.Same(fast, ContactModels.Address);
        Assert.Same(fast, ContactModels.Name);
        Assert.Same(accurate, ContactModels.Spans);
    }

    [Fact]
    public void TheTrainedHeadsLabelAContactBlockAnAddressAndAName()
    {
        string? path = Path();
        Assert.SkipWhen(path is null, "ONECONTACT_ACCURATE_MODEL is not set");

        using FileStream s = File.OpenRead(path!);
        AccurateModel model = AccurateModel.Load(s);

        ContactSpanResult found = model.Find(Signature);
        string[] labels = [.. found.Spans.Select(x => x.Label)];
        Assert.Contains("person", labels);
        Assert.Contains("email", labels);
        Assert.Contains("phone", labels);
        Assert.Contains("address", labels);
        Assert.All(found.Spans, x =>
        {
            Assert.InRange(x.Start, 0, Signature.Length - 1);
            Assert.True(x.End <= Signature.Length);
            Assert.InRange(x.Confidence, 0f, 1f);
        });
        ModelSpan email = found.Spans.First(x => x.Label == "email");
        Assert.Equal("jane.doe@example.com", Signature.Substring(email.Start, email.Length));
        Assert.NotEmpty(found.BlockStarts);

        IReadOnlyList<ModelSpan> fields = model.Tag("1600 Pennsylvania Avenue NW\nWashington, DC 20500");
        Assert.Contains(fields, f => f.Label == "postal-code" && f.Start > 0);

        Assert.Equal(NameClass.Company, model.Analyze("Acme Holdings Ltd").Class);
        NameModelResult person = model.Analyze("Jane Doe");
        Assert.Equal(NameClass.Person, person.Class);
        Assert.NotEmpty(person.Parts);

        // The same text gives the same answer twice.
        Assert.Equal(model.Find(Signature).Spans, found.Spans);
    }

    private sealed class Stub : IAddressFieldModel, INameModel, IContactSpanModel
    {
        public IReadOnlyList<ModelSpan> Tag(string text) => [];

        public NameModelResult Analyze(string text) => new(NameClass.Unknown, 0f, []);

        public ContactSpanResult Find(string text) => new([], []);
    }
}
