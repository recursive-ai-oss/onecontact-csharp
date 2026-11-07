using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Models.Tier1;
using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Data;
using RecursiveAI.OneNlp.Neural;
using RecursiveAI.OneNlp.Neural.Components;
using RecursiveAI.OneNlp.Neural.Encoders;

namespace OneContact.Recipe.Tier1;

/// <summary>
/// Adds the contact-span head to a trained Tier 1 pipeline. The encoder and the three heads are loaded and left as they are (the encoder is
/// frozen, and the old heads take no part in this training), so the address and name results do not change by a bit. The new head trains on
/// the rendered contact blocks with OneNLP's Train; when it is done the pipeline with four heads is saved.
/// </summary>
internal static class SpanRecipe
{
    private const int BatchWords = 1500;
    private const float LearningRate = 0.003f;
    private const int WarmupSteps = 200;
    private const float Dropout = 0.1f;
    private const uint SelectionSalt = 20261106;

    internal static int Train(Dictionary<string, string> o, Func<string, string[]> files, Func<string, int, int> num)
    {
        string outDir = o["out"];
        ulong seed = ulong.Parse(o.GetValueOrDefault("seed", "1"), CultureInfo.InvariantCulture);
        double budget = o.TryGetValue("budget-seconds", out string? b) ? double.Parse(b, CultureInfo.InvariantCulture) : double.PositiveInfinity;

        using Pipeline baseline = PipelineFile.Load(o["base"]);
        IEncoder encoder = ((TrainableHead)baseline.Components[0]).Encoder;
        if (baseline.Components.Any(c => c.Name == Tier1Pipeline.SpanHead))
        {
            throw new ArgumentException("The base pipeline already has a contact-span head.");
        }

        EntityRecognizer head = Tier1Pipeline.NewSpanHead(encoder);
        var pipeline = Pipeline.Blank(Language.English);
        pipeline.Add(head);
        List<Example> train = Data.ReadBlocks(files("blocks"), pipeline.Strings, num("blocks-count", 400000), SelectionSalt);
        List<Example> dev = Data.ReadBlocks(files("blocks-dev"), pipeline.Strings, num("blocks-dev-count", 6000), SelectionSalt);
        AddCrawl(o, files, num, pipeline.Strings, train, dev);
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
                Score = SpanScores.Mean,
                Patience = num("patience", 4),
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
            baseline.Add(head);
            string model = Path.Combine(outDir, "tier1.onenlp");
            PipelineFile.Save(baseline, model);
            Console.WriteLine($"model={model} bytes={new FileInfo(model).Length}");
        }

        return 0;
    }

    // Pieces of crawled pages (tools/data/crawl-spans, same JSON Lines form as the rendered blocks) are mixed in on request:
    // --crawl FILES --crawl-repeat N adds each training piece N times (the set is small next to the rendered blocks), --crawl-negative FILES
    // --crawl-negative-repeat M does the same for the pieces without a labelled person or organization, and --crawl-dev FILES
    // adds the held-out pieces to the development examples. Without --crawl the recipe is exactly what it was.
    private static void AddCrawl(Dictionary<string, string> o, Func<string, string[]> files, Func<string, int, int> num, StringStore strings, List<Example> train, List<Example> dev)
    {
        if (!o.ContainsKey("crawl"))
        {
            return;
        }

        int repeat = num("crawl-repeat", 1);
        for (int i = 0; i < repeat; i++)
        {
            train.AddRange(Data.ReadBlocks(files("crawl"), strings, int.MaxValue, SelectionSalt));
        }

        // Pieces without any labelled person or organization are far more numerous; they get their own repeat count.
        if (o.ContainsKey("crawl-negative"))
        {
            int negativeRepeat = num("crawl-negative-repeat", 1);
            for (int i = 0; i < negativeRepeat; i++)
            {
                train.AddRange(Data.ReadBlocks(files("crawl-negative"), strings, int.MaxValue, SelectionSalt));
            }
        }

        if (o.ContainsKey("crawl-dev"))
        {
            dev.AddRange(Data.ReadBlocks(files("crawl-dev"), strings, int.MaxValue, SelectionSalt));
        }

        // Shuffled with a fixed seed so the repeated pieces are spread over the epoch.
        var rng = new Random(unchecked((int)SelectionSalt));
        for (int i = train.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (train[i], train[j]) = (train[j], train[i]);
        }
    }

    internal static int Dev(Dictionary<string, string> o, Func<string, string[]> files, Func<string, int, int> num)
    {
        using Pipeline pipeline = PipelineFile.Load(o["model"]);
        List<Example> dev = Data.ReadBlocks(files("blocks-dev"), pipeline.Strings, num("blocks-dev-count", 6000), SelectionSalt);
        IPipelineComponent head = pipeline.Components.First(c => c.Name == Tier1Pipeline.SpanHead);
        foreach (Example e in dev)
        {
            head.Process(e.Predicted);
        }

        SpanScores.Report(dev, Console.Out);
        if (o.ContainsKey("crawl-dev"))
        {
            // The held-out pieces of crawled pages, reported on their own.
            List<Example> crawl = Data.ReadBlocks(files("crawl-dev"), pipeline.Strings, int.MaxValue, SelectionSalt);
            foreach (Example e in crawl)
            {
                head.Process(e.Predicted);
            }

            Console.WriteLine("crawl dev pieces:");
            SpanScores.Report(crawl, Console.Out);
        }

        return 0;
    }
}

/// <summary>Span F1 per field and for block starts on dev blocks; training selects on the mean of the overall field F1 and the block-start F1.</summary>
internal static class SpanScores
{
    internal static double Mean(IReadOnlyList<Example> examples)
    {
        Counts c = Count(examples);
        return (c.Fields.F1 + c.Starts.F1) / 2;
    }

    internal static void Report(IReadOnlyList<Example> examples, TextWriter output)
    {
        Counts c = Count(examples);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"field_f1={c.Fields.F1:F4} block_start_f1={c.Starts.F1:F4} examples={examples.Count}"));
        foreach ((string label, Tally t) in c.PerLabel.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {label}: p={t.Precision:F4} r={t.Recall:F4} f1={t.F1:F4} gold={t.Gold}"));
        }
    }

    private sealed class Tally
    {
        public long Tp, Pred, Gold;

        public double Precision => Pred == 0 ? 0 : (double)Tp / Pred;

        public double Recall => Gold == 0 ? 0 : (double)Tp / Gold;

        public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);
    }

    private sealed record Counts(Tally Fields, Tally Starts, Dictionary<string, Tally> PerLabel);

    private static Counts Count(IReadOnlyList<Example> examples)
    {
        var fields = new Tally();
        var starts = new Tally();
        var per = new Dictionary<string, Tally>(StringComparer.Ordinal);
        foreach (Example e in examples)
        {
            (HashSet<(int S, int E, string L)> gf, HashSet<int> gs) = Read(e.Reference);
            (HashSet<(int S, int E, string L)> pf, HashSet<int> ps) = Read(e.Predicted);
            Add(fields, gf, pf);
            Add(starts, gs, ps);
            foreach (string label in gf.Concat(pf).Select(x => x.L).Distinct())
            {
                if (!per.TryGetValue(label, out Tally? t))
                {
                    per[label] = t = new Tally();
                }

                Add(t, gf.Where(x => x.L == label).ToHashSet(), pf.Where(x => x.L == label).ToHashSet());
            }
        }

        return new Counts(fields, starts, per);
    }

    private static void Add<T>(Tally t, HashSet<T> gold, HashSet<T> pred)
    {
        t.Gold += gold.Count;
        t.Pred += pred.Count;
        t.Tp += gold.Intersect(pred).Count();
    }

    private static (HashSet<(int S, int E, string L)> Fields, HashSet<int> Starts) Read(Doc doc)
    {
        var fields = new HashSet<(int, int, string)>();
        var starts = new HashSet<int>();
        foreach (RecursiveAI.OneNlp.Span s in doc.Ents)
        {
            string label = s.Label;
            if (label.EndsWith(Tier1Pipeline.StartSuffix, StringComparison.Ordinal))
            {
                label = label[..^Tier1Pipeline.StartSuffix.Length];
                starts.Add(s.StartChar);
            }

            fields.Add((s.StartChar, s.EndChar, label));
        }

        return (fields, starts);
    }
}
