using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Models.Accurate;
using RecursiveAI.OneModel.Tensors;
using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Data;
using RecursiveAI.OneNlp.Models.Multilingual.FrozenEncoder;
using RecursiveAI.OneNlp.Neural;
using RecursiveAI.OneNlp.Neural.Components;
using RecursiveAI.OneNlp.Neural.Encoders;

namespace OneContact.Recipe.Accurate;

// The accurate-tier recipe: heads over OneNLP's frozen distilled multilingual encoder, trained with OneNLP's Train. The encoder is not trained and
// is not saved; the saved pipeline holds the heads only and names the encoder.
//
//   addresses --addresses FILES --addresses-dev FILES --names DIR --names-dev DIR [--names2/--names3 ...] [--osm FILES --osm-dev FILES] --out DIR
//          [--address-count N] [--names-count N] [--seed N] [--epochs N] [--threads N] [--budget-seconds S] [--checkpoint-every N]
//          trains the address-field head (or resumes from DIR/last.ckpt); writes DIR/addr.onenlp when done.
//   names  --base addr.onenlp --names DIR [--names2/--names3 ...] --names-dev DIR ... --out DIR
//          adds the name-part and name-class heads; writes DIR/heads.onenlp when done.
//   spans  --base heads.onenlp --blocks FILES --blocks-dev FILES --out DIR [--blocks-count N] ...
//          adds the contact-span head to the trained heads (resumable like train); writes DIR/accurate.onenlp when done.
//   dev    --model accurate.onenlp|heads.onenlp --addresses-dev FILES --names-dev FILES   scores the address and name heads on dev files.
//   spans-dev --model accurate.onenlp --blocks-dev FILES    scores the span head on dev blocks.
//   probe  --addresses FILES --count N     times the encoder alone on N address examples (words per second).
// Run a command again with the same arguments to continue: the result equals one uninterrupted run.
internal static class Program
{
    private const int BatchWords = 1500;
    private const float LearningRate = 0.002f;
    private const int WarmupSteps = 300;
    private const float Dropout = 0.1f;
    private const int Patience = 6;
    private const uint SelectionSalt = 20261105;
    private const uint SpanSalt = 20261106;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("usage: train|spans|dev|spans-dev|probe ... (see the comment at the top of Program.cs)");
                return 2;
            }

            Dictionary<string, string> o = Options(args.AsSpan(1));
            if (o.TryGetValue("threads", out string? threads))
            {
                TensorCompute.Threads = int.Parse(threads, CultureInfo.InvariantCulture);
            }

            return args[0] switch
            {
                "addresses" => AddressPhase(o),
                "names" => NamePhase(o),
                "spans" => Spans(o),
                "dev" => Dev(o),
                "spans-dev" => SpansDev(o),
                "probe" => Probe(o),
                "loadcheck" => LoadCheck(o),
                _ => 2,
            };
        }
        catch (Exception e) when (e is ArgumentException or IOException or DataFormatException or InvalidDataException or FormatException or KeyNotFoundException)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 1;
        }
    }

    internal static List<Example> Load(Dictionary<string, string> o, StringStore strings, bool dev, bool addresses = true, bool names = true)
    {
        string suffix = dev ? "-dev" : string.Empty;
        int addrCount = Num(o, dev ? "address-dev-count" : "address-count", dev ? 8000 : 300000);
        int bigCount = Num(o, dev ? "address-big-dev-count" : "address-big-count", addrCount);
        List<Example> all = [];
        // Files named in --address-big (the United States, Canada and the territories) have their own count; the other files share --address-count evenly.
        HashSet<string> big = new(o.TryGetValue("address-big", out string? bigList) ? bigList.Split(',', StringSplitOptions.RemoveEmptyEntries) : [], StringComparer.Ordinal);
        string[] addressFiles = addresses && o.ContainsKey("addresses" + suffix) ? Files(o, "addresses" + suffix) : [];
        string[] others = [.. addressFiles.Where(f => !big.Contains(StripSplit(f)))];
        foreach (string file in addressFiles)
        {
            int count = big.Contains(StripSplit(file)) ? bigCount : addrCount / Math.Max(1, others.Length);
            all.AddRange(Data.ReadAddresses(file, strings, count, SelectionSalt));
        }

        // OpenStreetMap addresses (ODbL, credited in the package notices): street.road is one street.name and district labels merge into their parents.
        if (addresses && o.ContainsKey("osm" + suffix))
        {
            string[] osmFiles = Files(o, "osm" + suffix);
            int osmCount = Num(o, dev ? "osm-dev-count" : "osm-count", dev ? 8000 : 300000);
            foreach (string file in osmFiles)
            {
                all.AddRange(Data.ReadAddresses(file, strings, osmCount / Math.Max(1, osmFiles.Length), SelectionSalt, RelabelOsm));
            }
        }

        foreach (string group in new[] { "names", "names2", "names3" })
        {
            if (names && o.ContainsKey(group + suffix))
            {
                all.AddRange(Data.ReadNames(Files(o, group + suffix), strings, Num(o, dev ? group + "-dev-count" : group + "-count", dev ? 4000 : 100000), SelectionSalt));
            }
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

    private static string RelabelOsm(string label) => label switch
    {
        "street.road" => "street.name",
        "dependent-locality.district" => "dependent-locality",
        "admin-area.district" => "admin-area",
        _ => label,
    };

    private static TrainOptions Options(Dictionary<string, string> o, string outDir, List<Example> dev, Func<IReadOnlyList<Example>, double> score, float learningRate, int warmup, int defaultEpochs, Stopwatch clock)
    {
        var progress = new Progress<TrainProgress>(p =>
        {
            if (p.DevScore is { } s)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"step={p.Step} epoch={p.Epoch} loss={p.Loss:F4} dev={s:F4} best={p.IsBest} elapsed_s={clock.Elapsed.TotalSeconds:F1}"));
            }
        });
        return new TrainOptions
        {
            Epochs = Num(o, "epochs", defaultEpochs),
            BatchWords = BatchWords,
            LearningRate = learningRate,
            WarmupSteps = warmup,
            Dropout = Dropout,
            Seed = ulong.Parse(o.GetValueOrDefault("seed", "1"), CultureInfo.InvariantCulture),
            FreezeEncoder = true,
            DevExamples = dev,
            Score = score,
            Patience = Num(o, "patience", Patience),
            CheckpointDirectory = outDir,
            CheckpointEvery = Num(o, "checkpoint-every", 500),
            Progress = progress,
        };
    }

    private static CancellationTokenSource Budget(Dictionary<string, string> o)
    {
        var cts = new CancellationTokenSource();
        if (o.TryGetValue("budget-seconds", out string? b))
        {
            cts.CancelAfter(TimeSpan.FromSeconds(double.Parse(b, CultureInfo.InvariantCulture)));
        }

        return cts;
    }

    private static void Report(TrainResult result, Stopwatch clock)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"completed={result.Completed} steps={result.Steps} epochs={result.Epochs} evaluations={result.Evaluations} best_dev={result.BestScore:F4} best_step={result.BestStep} stopped_early={result.StoppedEarly}"));
        Console.WriteLine($"weights_sha256={result.WeightsSha256}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"chunk_wall_s={clock.Elapsed.TotalSeconds:F1} threads={TensorCompute.Threads}"));
    }

    // Phase 1: the address-field head, trained alone on address examples.
    private static int AddressPhase(Dictionary<string, string> o)
    {
        string outDir = Required(o, "out");
        using TransformerEncoder real = FrozenStudentEncoder.Create();
        SharedEncoder.Register(AccuratePipeline.EncoderName, real);
        var pipeline = Pipeline.Blank(Language.English);
        EntityRecognizer head = AccuratePipeline.NewAddressHead(new SharedEncoder(real, AccuratePipeline.EncoderName));
        pipeline.Add(head);
        List<Example> train = Load(o, pipeline.Strings, dev: false, names: false);
        List<Example> dev = Load(o, pipeline.Strings, dev: true, names: false);
        Console.WriteLine("train: " + Data.Describe(train));
        Console.WriteLine("dev: " + Data.Describe(dev));
        using CancellationTokenSource cts = Budget(o);
        var clock = Stopwatch.StartNew();
        TrainResult result = pipeline.Train(train, Options(o, outDir, dev, DevScores.Mean, 0.003f, 300, 6, clock), cts.Token);
        Report(result, clock);
        if (result.Completed)
        {
            string model = Path.Combine(outDir, "addr.onenlp");
            PipelineFile.Save(pipeline, model);
            Console.WriteLine($"model={model} bytes={new FileInfo(model).Length}");
        }

        pipeline.Dispose();
        return 0;
    }

    // Phase 2: the name-part and name-class heads, trained together on name and company examples, added to the address pipeline.
    private static int NamePhase(Dictionary<string, string> o)
    {
        string outDir = Required(o, "out");
        using TransformerEncoder real = FrozenStudentEncoder.Create();
        SharedEncoder.Register(AccuratePipeline.EncoderName, real);
        using Pipeline baseline = PipelineFile.Load(Required(o, "base"));
        IEncoder shared = ((TrainableHead)baseline.Components[0]).Encoder;
        EntityRecognizer parts = AccuratePipeline.NewNamePartHead(shared);
        TextClassifier cls = AccuratePipeline.NewNameClassHead(shared);
        var pipeline = Pipeline.Blank(Language.English);
        pipeline.Add(parts);
        pipeline.Add(cls);
        List<Example> train = Load(o, pipeline.Strings, dev: false, addresses: false);
        List<Example> dev = Load(o, pipeline.Strings, dev: true, addresses: false);
        Console.WriteLine("train: " + Data.Describe(train));
        Console.WriteLine("dev: " + Data.Describe(dev));
        using CancellationTokenSource cts = Budget(o);
        var clock = Stopwatch.StartNew();
        TrainResult result = pipeline.Train(train, Options(o, outDir, dev, DevScores.Mean, 0.003f, 200, 6, clock), cts.Token);
        Report(result, clock);
        if (result.Completed)
        {
            baseline.Add(parts);
            baseline.Add(cls);
            string model = Path.Combine(outDir, "heads.onenlp");
            PipelineFile.Save(baseline, model);
            Console.WriteLine($"model={model} bytes={new FileInfo(model).Length}");
        }

        return 0;
    }

    private static int Spans(Dictionary<string, string> o)
    {
        string outDir = Required(o, "out");
        using TransformerEncoder real = FrozenStudentEncoder.Create();
        SharedEncoder.Register(AccuratePipeline.EncoderName, real);
        using Pipeline baseline = PipelineFile.Load(Required(o, "base"));
        if (baseline.Components.Any(c => c.Name == AccuratePipeline.SpanHead))
        {
            throw new ArgumentException("The base pipeline already has a contact-span head.");
        }

        EntityRecognizer head = AccuratePipeline.NewSpanHead(((TrainableHead)baseline.Components[0]).Encoder);
        var pipeline = Pipeline.Blank(Language.English);
        pipeline.Add(head);
        List<Example> train = Data.ReadBlocks(Files(o, "blocks"), pipeline.Strings, Num(o, "blocks-count", 100000), SpanSalt);
        List<Example> dev = Data.ReadBlocks(Files(o, "blocks-dev"), pipeline.Strings, Num(o, "blocks-dev-count", 4000), SpanSalt);
        Console.WriteLine("train: " + Data.Describe(train));
        Console.WriteLine("dev: " + Data.Describe(dev));
        using CancellationTokenSource cts = Budget(o);
        var clock = Stopwatch.StartNew();
        TrainResult result = pipeline.Train(train, Options(o, outDir, dev, SpanScores.Mean, 0.003f, 200, 3, clock), cts.Token);
        Report(result, clock);
        if (result.Completed)
        {
            baseline.Add(head);
            string model = Path.Combine(outDir, "accurate.onenlp");
            PipelineFile.Save(baseline, model);
            Console.WriteLine($"model={model} bytes={new FileInfo(model).Length}");
        }

        return 0;
    }

    private static int Dev(Dictionary<string, string> o)
    {
        using TransformerEncoder real = FrozenStudentEncoder.Create();
        SharedEncoder.Register(AccuratePipeline.EncoderName, real);
        using Pipeline pipeline = PipelineFile.Load(Required(o, "model"));
        List<Example> dev = Load(o, pipeline.Strings, dev: true);
        foreach (Example e in dev)
        {
            // An example with categories is a name string; the others are addresses.
            foreach (IPipelineComponent c in pipeline.Components)
            {
                bool nameHead = c.Name is AccuratePipeline.NamePartHead or AccuratePipeline.NameClassHead;
                if (c.Name != AccuratePipeline.SpanHead && nameHead == (e.Cats is not null))
                {
                    c.Process(e.Predicted);
                }
            }
        }

        DevScores.Report(dev, Console.Out);
        return 0;
    }

    private static int SpansDev(Dictionary<string, string> o)
    {
        using TransformerEncoder real = FrozenStudentEncoder.Create();
        SharedEncoder.Register(AccuratePipeline.EncoderName, real);
        using Pipeline pipeline = PipelineFile.Load(Required(o, "model"));
        List<Example> dev = Data.ReadBlocks(Files(o, "blocks-dev"), pipeline.Strings, Num(o, "blocks-dev-count", 4000), SpanSalt);
        IPipelineComponent head = pipeline.Components.First(c => c.Name == AccuratePipeline.SpanHead);
        foreach (Example e in dev)
        {
            head.Process(e.Predicted);
        }

        SpanScores.Report(dev, Console.Out);
        return 0;
    }

    // Times the frozen encoder alone, one example per call and in batches of 32, so a training plan can be sized.
    private static int Probe(Dictionary<string, string> o)
    {
        using TransformerEncoder real = FrozenStudentEncoder.Create();
        foreach (int batch in new[] { 32, 128, 1 })
        {
            // Fresh documents for each batch size: the encoder keeps its output per document.
            var strings = new StringStore();
            List<Example> ex = [];
            foreach (string f in Files(o, "addresses"))
            {
                ex.AddRange(Data.ReadAddresses(f, strings, Num(o, "count", 2000), SelectionSalt));
            }

            long words = ex.Sum(e => (long)e.Predicted.Length);
            var clock = Stopwatch.StartNew();
            using NoGradScope noGrad = GradMode.NoGrad();
            for (int i = 0; i < ex.Count; i += batch)
            {
                using var scope = new ActivationScope();
                Doc[] docs = [.. ex.Skip(i).Take(batch).Select(e => e.Predicted)];
                real.Encode(docs);
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"batch={batch} seconds={clock.Elapsed.TotalSeconds:F2} words_per_s={words / clock.Elapsed.TotalSeconds:F0} threads={TensorCompute.Threads}"));
        }

        return 0;
    }

    // Loads a pipeline file written by an older OneNLP (a fast-tier model) and labels one address with every tagger in it, to show the file still loads and runs.
    private static int LoadCheck(Dictionary<string, string> o)
    {
        using Pipeline pipeline = PipelineFile.Load(Required(o, "model"));
        string text = "100 Broadway\nNew York, NY 10001";
        var words = new List<(int Start, int Length)>();
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]) && text[i] != '\n') { i++; continue; }
            int start = i;
            i++;
            while (i < text.Length && char.IsLetterOrDigit(text[i]) && char.IsLetterOrDigit(text[start])) { i++; }
            words.Add((start, i - start));
        }

        Doc doc = new(pipeline.Strings, text, [.. words.Select(w => new TokenBoundary(w.Start, w.Length))]);
        foreach (IPipelineComponent c in pipeline.Components)
        {
            Console.WriteLine($"component {c.Name}");
        }

        pipeline.Components[0].Process(doc);
        Console.WriteLine(string.Join(' ', Enumerable.Range(0, doc.Length).Select(k => doc.Strings[doc.GetColumn(TokenColumn.Tag)[k]])));
        return 0;
    }

    private static string StripSplit(string file) => Path.GetFileName(file).Split('.')[0];

    internal static string[] Files(Dictionary<string, string> o, string key)
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

    internal static int Num(Dictionary<string, string> o, string key, int fallback) =>
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
