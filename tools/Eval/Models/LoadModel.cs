using System.Runtime.CompilerServices;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Superset;
using RecursiveAI.OneContact.Models.Tier1;

namespace OneContact.Eval.Models;

/// <summary>Registers the saved pipeline named by ONECONTACT_MODELS_MODEL (the Tier 1 + Tier 2 model, rank 2) or else ONECONTACT_TIER1_MODEL (Tier 1, rank 1).</summary>
internal static class LoadModel
{
    [ModuleInitializer]
    internal static void Register()
    {
        string? superset = Environment.GetEnvironmentVariable("ONECONTACT_MODELS_MODEL");
        if (!string.IsNullOrEmpty(superset))
        {
            using FileStream stream = File.OpenRead(superset);
            SupersetModel model = SupersetModel.Load(stream);
            ContactModels.Register(SupersetModel.PackageId, 2, model, model, model);
            return;
        }

        string path = Environment.GetEnvironmentVariable("ONECONTACT_TIER1_MODEL")
            ?? throw new InvalidOperationException("Set ONECONTACT_MODELS_MODEL or ONECONTACT_TIER1_MODEL to the saved pipeline file to score.");
        using FileStream tier1 = File.OpenRead(path);
        Tier1Model t1 = Tier1Model.Load(tier1);
        ContactModels.Register(Tier1Model.PackageId, 1, t1, t1);
    }
}
