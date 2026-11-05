using System.Runtime.CompilerServices;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Tier1;

namespace OneContact.Eval.Models;

internal static class LoadModel
{
    [ModuleInitializer]
    internal static void Register()
    {
        string path = Environment.GetEnvironmentVariable("ONECONTACT_TIER1_MODEL")
            ?? throw new InvalidOperationException("Set ONECONTACT_TIER1_MODEL to the saved pipeline file to score.");
        using FileStream stream = File.OpenRead(path);
        Tier1Model model = Tier1Model.Load(stream);
        ContactModels.Register(Tier1Model.PackageId, 1, model, model);
    }
}
