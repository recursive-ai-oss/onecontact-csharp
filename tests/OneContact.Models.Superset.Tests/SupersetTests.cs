using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Superset;
using RecursiveAI.OneContact.Models.Tier1;

namespace RecursiveAI.OneContact.Models.Superset.Tests;

/// <summary>
/// The Tier 1 + Tier 2 package. Tests that need trained weights read them from the files named by ONECONTACT_MODELS_MODEL (the superset pipeline) and
/// ONECONTACT_TIER1_MODEL (the Tier 1 pipeline), and are skipped without them, so a development build still passes.
/// </summary>
public class SupersetTests
{
    private const string BritishRunOn = "58 Hordley Avenue\nShrewsbury\nShropshire\nSY4 1AA";

    private static string? Path(string variable)
    {
        string? p = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
    }

    [Fact]
    public void ABuildWithoutWeightsNamesThePackage()
    {
        if (typeof(SupersetModel).Assembly.GetManifestResourceStream("weights/models.onenlp") is not null)
        {
            return;
        }

        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => SupersetModel.Instance);
        Assert.Contains("RecursiveAI.OneContact.Models", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRefusalNamesBothPackagesAndTheSupersetIsAboveTier1()
    {
        string description = ReasonCode.AddressModelRequired.Description;
        Assert.Contains("RecursiveAI.OneContact.Models.Tier1", description, StringComparison.Ordinal);
        Assert.Contains("RecursiveAI.OneContact.Models (", description, StringComparison.Ordinal);
        Assert.Equal("RecursiveAI.OneContact.Models", SupersetModel.PackageId);
    }

    [Fact]
    public void NothingRegisteredTier1AloneThenTheSupersetThenBothInEitherOrder()
    {
        string? models = Path("ONECONTACT_MODELS_MODEL");
        string? tier1 = Path("ONECONTACT_TIER1_MODEL");
        Assert.SkipWhen(models is null || tier1 is null, "ONECONTACT_MODELS_MODEL and ONECONTACT_TIER1_MODEL are not set");

        // No package: the Tier 2 address stays refused, and the refusal names the packages.
        Assert.Null(ContactModels.PackageId);
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse(BritishRunOn, Region.Parse("GB"), out _, out ParseDetail none));
        Assert.Equal(ReasonCode.AddressModelRequired, none.PrimaryReason);

        // Tier 1 alone: its weights know the United States and Canada only, so the Tier 2 address stays refused.
        using (FileStream s = File.OpenRead(tier1!))
        {
            Tier1Model t1 = Tier1Model.Load(s);
            ContactModels.Register(Tier1Model.PackageId, 1, t1, t1, t1);
        }

        Assert.Equal(Tier1Model.PackageId, ContactModels.PackageId);
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse(BritishRunOn, Region.Parse("GB"), out _, out ParseDetail withTier1));
        Assert.Equal(ReasonCode.AddressModelRequired, withTier1.PrimaryReason);

        // The superset registered as well: it wins, and the Tier 2 address is read from its labels.
        using (FileStream s = File.OpenRead(models!))
        {
            SupersetModel m = SupersetModel.Load(s);
            ContactModels.Register(SupersetModel.PackageId, 2, m, m, m);
        }

        Assert.Equal(SupersetModel.PackageId, ContactModels.PackageId);
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse(BritishRunOn, Region.Parse("GB"), out ParsedAddress? read, out ParseDetail withBoth));
        Assert.Equal(Region.Parse("GB"), read!.Country);
        Assert.Equal("SY4 1AA", read.PostalCode!.Value.Standard);
        Assert.Contains(withBoth.Fields, f => f.Source == DecisionSource.Model);

        // Registering Tier 1 again afterwards changes nothing: the most specific package wins whatever the order.
        using (FileStream s = File.OpenRead(tier1!))
        {
            Tier1Model t1 = Tier1Model.Load(s);
            ContactModels.Register(Tier1Model.PackageId, 1, t1, t1, t1);
        }

        Assert.Equal(SupersetModel.PackageId, ContactModels.PackageId);
    }
}
