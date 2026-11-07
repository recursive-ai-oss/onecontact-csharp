using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Models.Superset;
using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Data;
using RecursiveAI.OneNlp.Neural;
using RecursiveAI.OneNlp.Neural.Training;

namespace OneContact.Recipe.Models;

/// <summary>
/// Retrains the two name heads of a trained pipeline on new name sets. The encoder is frozen and the address and contact-span heads take no part,
/// so their results do not change by a bit; the name heads start from their trained weights and train with OneNLP's Train on the rendered names.
/// </summary>
internal static class NameRecipe
{
    private const int BatchWords = 1500;
    private const float LearningRate = 0.001f;
    private const int WarmupSteps = 100;
    private const float Dropout = 0.1f;
    private const uint SelectionSalt = 20261105;

    internal static int Train(Dictionary<string, string> o, Func<string, string[]> files, Func<string, int, int> num)
    {
        string outDir = o["out"];
        ulong seed = ulong.Parse(o.GetValueOrDefault("seed", "1"), CultureInfo.InvariantCulture);
        double budget = o.TryGetValue("budget-seconds", out string? b) ? double.Parse(b, CultureInfo.InvariantCulture) : double.PositiveInfinity;

        using Pipeline baseline = PipelineFile.Load(o["base"]);
        var pipeline = Pipeline.Blank(Language.English);
        foreach (string name in new[] { SupersetPipeline.NamePartHead, SupersetPipeline.NameClassHead })
        {
            pipeline.Add(baseline.Components.First(c => c.Name == name));
        }

        List<Example> train = [];
        List<Example> dev = [];
        // Up to three name groups, each with its own count, so the smaller sets (the Tier 2 companies) are not drowned by the larger ones.
        foreach (string group in new[] { "names", "names2", "names3" })
        {
            if (o.ContainsKey(group))
            {
                train.AddRange(Data.ReadNames(files(group), pipeline.Strings, num(group + "-count", 250000), SelectionSalt));
                dev.AddRange(Data.ReadNames(files(group + "-dev"), pipeline.Strings, num(group + "-dev-count", 4000), SelectionSalt));
            }
        }

        var rng = new Random(unchecked((int)SelectionSalt));
        for (int i = train.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (train[i], train[j]) = (train[j], train[i]);
        }

        Console.WriteLine("train: " + Data.Describe(train));
        Console.WriteLine("dev: " + Data.Describe(dev));

        using var cts = new CancellationTokenSource();
        if (!double.IsPositiveInfinity(budget))
        {
            cts.CancelAfter(TimeSpan.FromSeconds(budget));
        }

        var clock = Stopwatch.StartNew();
        var progress = new Progress<TrainProgress>(p =>
        {
            if (p.DevScore is { } score)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"step={p.Step} epoch={p.Epoch} loss={p.Loss:F4} dev={score:F4} best={p.IsBest} elapsed_s={clock.Elapsed.TotalSeconds:F1}"));
            }
        });

        TrainResult result = pipeline.Train(
            train,
            new TrainOptions
            {
                Epochs = num("epochs", 4),
                BatchWords = BatchWords,
                LearningRate = LearningRate,
                WarmupSteps = WarmupSteps,
                Dropout = Dropout,
                Seed = seed,
                FreezeEncoder = true,
                DevExamples = dev,
                Score = DevScores.Mean,
                Patience = num("patience", 3),
                CheckpointDirectory = outDir,
                CheckpointEvery = num("checkpoint-every", 500),
                Progress = progress,
            },
            cts.Token);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"completed={result.Completed} steps={result.Steps} epochs={result.Epochs} evaluations={result.Evaluations} best_dev={result.BestScore:F4} best_step={result.BestStep} stopped_early={result.StoppedEarly}"));
        Console.WriteLine($"weights_sha256={result.WeightsSha256}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"chunk_wall_s={clock.Elapsed.TotalSeconds:F1}"));
        if (result.Completed)
        {
            string model = Path.Combine(outDir, "models.onenlp");
            PipelineFile.Save(baseline, model);
            Console.WriteLine($"model={model} bytes={new FileInfo(model).Length}");
        }

        return 0;
    }
}
