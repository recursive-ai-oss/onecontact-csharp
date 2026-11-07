namespace OneContact.Eval.Extract;

/// <summary>Registers the fast-tier model named by the environment: ONECONTACT_MODELS_MODEL (the Tier 1 + Tier 2 pipeline, rank 2) or else ONECONTACT_TIER1_MODEL (Tier 1, rank 1).</summary>
internal static class ModelLoader
{
    internal static void RegisterFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("ONECONTACT_MODELS_MODEL") is { Length: > 0 } supersetPath)
        {
            using FileStream supersetStream = File.OpenRead(supersetPath);
            var superset = RecursiveAI.OneContact.Models.Superset.SupersetModel.Load(supersetStream);
            RecursiveAI.OneContact.Models.ContactModels.Register(RecursiveAI.OneContact.Models.Superset.SupersetModel.PackageId, 2, superset, superset, superset);
        }
        else if (Environment.GetEnvironmentVariable("ONECONTACT_TIER1_MODEL") is { Length: > 0 } modelPath)
        {
            using FileStream modelStream = File.OpenRead(modelPath);
            var tier1 = RecursiveAI.OneContact.Models.Tier1.Tier1Model.Load(modelStream);
            RecursiveAI.OneContact.Models.ContactModels.Register(RecursiveAI.OneContact.Models.Tier1.Tier1Model.PackageId, 1, tier1, tier1, tier1);
        }
    }
}
