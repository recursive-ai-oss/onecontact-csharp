using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Models.Superset;
using RecursiveAI.OneModel.Tensors;
using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Data;
using RecursiveAI.OneNlp.Neural;
using RecursiveAI.OneNlp.Neural.Training;

namespace OneContact.Recipe.Models;

// The Tier 1 + Tier 2 recipe: one shared fast-tier encoder with an address-field head, a name-part head and a name-class head, trained together with
// OneNLP's Train on the rendered United States, Canada and Tier 2 sets.
//
//   train --addresses US.jsonl,CA.jsonl,... --addresses-dev US.dev.jsonl,... --names DIR_OR_FILES --names-dev FILES --out DIR
//         [--address-count N] [--names-count N] [--seed N] [--epochs N] [--threads N] [--budget-seconds S] [--checkpoint-every N]
//         trains (or resumes from DIR/last.ckpt) until done or the time budget is used; writes DIR/models.onenlp when done.
//         Run it again with the same arguments to continue: the result is the same as one uninterrupted run.
//   spans --base models.onenlp --blocks DIR_OR_FILES --blocks-dev FILES --out DIR [--blocks-count N] [--seed N] [--epochs N] [--budget-seconds S]
//         adds the contact-span head to a trained pipeline with the encoder frozen (resumable like train); writes DIR/models.onenlp when done.
//   spans-dev --model models.onenlp --blocks-dev FILES      scores the contact-span head on dev blocks.
//   dev   --model models.onenlp --addresses FILES --names FILES
//         scores a saved model on dev files and prints the numbers.
internal static class Program
{
    // The recipe. Everything that shapes the model is here or in SupersetPipeline, so a model can be rebuilt from these two files and the data.
    private const int BatchWords = 1500;
    private const float LearningRate = 0.002f;
    private const int WarmupSteps = 300;
    private const float Dropout = 0.1f;
    private const int Patience = 6;
    private const uint SelectionSalt = 20261105;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("usage: train|dev ... (see the comment at the top of Program.cs)");
                return 2;
            }

            Dictionary<string, string> o = Options(args.AsSpan(1));
            if (o.TryGetValue("threads", out string? threads))
            {
                TensorCompute.Threads = int.Parse(threads, CultureInfo.InvariantCulture);
            }

            return args[0] switch
            {
                "train" => Train(o),
                "dev" => Dev(o),
                "names-dev" => NamesDev(o),
                "spans" => SpanRecipe.Train(o, k => Files(o, k), (k, d) => Num(o, k, d)),
                "spans-dev" => SpanRecipe.Dev(o, k => Files(o, k), (k, d) => Num(o, k, d)),
                _ => 2,
            };
        }
        catch (Exception e) when (e is ArgumentException or IOException or DataFormatException or InvalidDataException or FormatException or KeyNotFoundException)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 1;
        }
    }

    private static List<Example> Load(Dictionary<string, string> o, StringStore strings, bool dev)
    {
        string suffix = dev ? "-dev" : string.Empty;
        int addrCount = Num(o, dev ? "address-dev-count" : "address-count", dev ? 8000 : 300000);
        int nameCount = Num(o, dev ? "names-dev-count" : "names-count", dev ? 8000 : 200000);
        List<Example> all = [];
        string[] addressFiles = Files(o, "addresses" + suffix);
        // The count is shared out evenly over the files, so every region is seen.
        foreach (string file in addressFiles)
        {
            all.AddRange(Data.ReadAddresses(file, strings, addrCount / Math.Max(1, addressFiles.Length), SelectionSalt));
        }

        if (o.ContainsKey("names" + suffix))
        {
            all.AddRange(Data.ReadNames(Files(o, "names" + suffix), strings, nameCount, SelectionSalt));
        }

        // A fixed shuffle, so batches mix addresses and names whatever order the files came in.
        var rng = new Random(unchecked((int)SelectionSalt));
        for (int i = all.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (all[i], all[j]) = (all[j], all[i]);
        }

        return all;
    }

    private static int Train(Dictionary<string, string> o)
    {
        string outDir = Required(o, "out");
        int epochs = Num(o, "epochs", 8);
        ulong seed = ulong.Parse(o.GetValueOrDefault("seed", "1"), CultureInfo.InvariantCulture);
        int every = Num(o, "checkpoint-every", 500);
        double budget = o.TryGetValue("budget-seconds", out string? b) ? double.Parse(b, CultureInfo.InvariantCulture) : double.PositiveInfinity;

        using Pipeline pipeline = SupersetPipeline.New(seed);
        List<Example> train = Load(o, pipeline.Strings, dev: false);
        List<Example> dev = Load(o, pipeline.Strings, dev: true);
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
                Epochs = epochs,
                BatchWords = BatchWords,
                LearningRate = LearningRate,
                WarmupSteps = WarmupSteps,
                Dropout = Dropout,
                Seed = seed,
                DevExamples = dev,
                Score = DevScores.Mean,
                Patience = Num(o, "patience", Patience),
                CheckpointDirectory = outDir,
                CheckpointEvery = every,
                Progress = progress,
            },
            cts.Token);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"completed={result.Completed} steps={result.Steps} epochs={result.Epochs} evaluations={result.Evaluations} best_dev={result.BestScore:F4} best_step={result.BestStep} stopped_early={result.StoppedEarly}"));
        Console.WriteLine($"weights_sha256={result.WeightsSha256}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"chunk_wall_s={clock.Elapsed.TotalSeconds:F1} threads={TensorCompute.Threads}"));
        if (result.Completed)
        {
            string model = Path.Combine(outDir, "models.onenlp");
            PipelineFile.Save(pipeline, model);
            Console.WriteLine($"model={model} bytes={new FileInfo(model).Length}");
        }

        return 0;
    }

    private static int Dev(Dictionary<string, string> o)
    {
        using Pipeline pipeline = PipelineFile.Load(Required(o, "model"));
        List<Example> dev = Load(o, pipeline.Strings, dev: true);
        foreach (Example e in dev)
        {
            pipeline.Process(e.Predicted);
        }

        DevScores.Report(dev, Console.Out);
        return 0;
    }

    // Chooses how sure the model must be before it replaces the rules' default reading of a name (a person taken for lack of evidence otherwise).
    // Reads dev names only. For each threshold, prints the class accuracy of the rules alone and of rules plus model on the dev names.
    private static int NamesDev(Dictionary<string, string> o)
    {
        var model = SupersetModel.Load(File.OpenRead(Required(o, "model")));
        string[] thresholds = ["0.5", "0.6", "0.7", "0.8", "0.9", "0.95", "0.99"];
        int count = Num(o, "names-dev-count", 20000);
        var items = new List<(string Text, string Class)>();
        foreach (string file in Files(o, "names-dev"))
        {
            foreach (string line in File.ReadLines(file))
            {
                using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(line);
                items.Add((json.RootElement.GetProperty("text").GetString()!, json.RootElement.GetProperty("class").GetString()!));
            }
        }

        int step = Math.Max(1, items.Count / count);
        var rows = new List<(string Class, bool Default, RecursiveAI.OneContact.Models.NameModelResult M, RecursiveAI.OneContact.Names.NameClass Rules)>();
        for (int i = 0; i < items.Count; i += step)
        {
            RecursiveAI.OneContact.Names.NameParseResult r = RecursiveAI.OneContact.Names.NameParsing.Split(items[i].Text);
            bool isDefault = r.Class == RecursiveAI.OneContact.Names.NameClass.Person && r.Detail.Issues.Any(x => x.Reason.Equals(RecursiveAI.OneContact.ReasonCode.PersonNoEvidence));
            rows.Add((items[i].Class, isDefault, model.Analyze(items[i].Text), r.Class));
        }

        static string Label(RecursiveAI.OneContact.Names.NameClass c) => c switch { RecursiveAI.OneContact.Names.NameClass.Person => "person", RecursiveAI.OneContact.Names.NameClass.Company => "company", RecursiveAI.OneContact.Names.NameClass.Multiple => "multiple", _ => "unknown" };
        int rulesRight = rows.Count(r => Label(r.Rules) == r.Class);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"items={rows.Count} defaults={rows.Count(r => r.Default)} rules_class_accuracy={(double)rulesRight / rows.Count:F4}"));
        foreach (string t in thresholds)
        {
            float threshold = float.Parse(t, CultureInfo.InvariantCulture);
            int right = rows.Count(r => (r.Default && r.M.Class != RecursiveAI.OneContact.Names.NameClass.Person && r.M.ClassConfidence >= threshold ? Label(r.M.Class) : Label(r.Rules)) == r.Class);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"threshold={t} class_accuracy={(double)right / rows.Count:F4}"));
        }

        return 0;
    }

    private static string[] Files(Dictionary<string, string> o, string key)
    {
        List<string> files = [];
        foreach (string item in Required(o, key).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Directory.Exists(item))
            {
                files.AddRange(Directory.GetFiles(item, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal));
            }
            else
            {
                files.Add(item);
            }
        }

        return [.. files];
    }

    private static int Num(Dictionary<string, string> o, string key, int fallback) =>
        o.TryGetValue(key, out string? v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    private static string Required(Dictionary<string, string> o, string key) =>
        o.TryGetValue(key, out string? v) ? v : throw new ArgumentException($"missing --{key}");

    private static Dictionary<string, string> Options(ReadOnlySpan<string> args)
    {
        Dictionary<string, string> o = new(StringComparer.Ordinal);
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            o[args[i].TrimStart('-')] = args[i + 1];
        }

        return o;
    }
}
