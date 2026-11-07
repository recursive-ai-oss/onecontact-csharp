namespace OneContact.Eval.Extract;

/// <summary>Registers the accurate-tier model named by ONECONTACT_ACCURATE_MODEL, as the module initializer of the package would.</summary>
internal static class ModelLoader
{
    internal static void RegisterFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("ONECONTACT_ACCURATE_MODEL") is { Length: > 0 } path)
        {
            using FileStream stream = File.OpenRead(path);
            var model = RecursiveAI.OneContact.Models.Accurate.AccurateModel.Load(stream);
            RecursiveAI.OneContact.Models.ContactModels.Register(RecursiveAI.OneContact.Models.Accurate.AccurateModel.PackageId, 3, model, model, model, normalizationRank: 0);
        }
    }
}
