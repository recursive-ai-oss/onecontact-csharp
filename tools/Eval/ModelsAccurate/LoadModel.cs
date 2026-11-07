using System.Runtime.CompilerServices;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Accurate;

namespace OneContact.Eval.ModelsAccurate;

/// <summary>Registers the saved pipeline named by ONECONTACT_ACCURATE_MODEL for addresses and names (as if it were the only model package).</summary>
internal static class LoadModel
{
    [ModuleInitializer]
    internal static void Register()
    {
        string path = Environment.GetEnvironmentVariable("ONECONTACT_ACCURATE_MODEL")
            ?? throw new InvalidOperationException("Set ONECONTACT_ACCURATE_MODEL to the saved pipeline file to score.");
        using FileStream stream = File.OpenRead(path);
        AccurateModel model = AccurateModel.Load(stream);
        ContactModels.Register(AccurateModel.PackageId, 3, model, model, model, normalizationRank: 0);
    }
}
