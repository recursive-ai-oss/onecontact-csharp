using RecursiveAI.OneContact.Names;
using RecursiveAI.OneModel.Tensors;
using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Models.Multilingual.FrozenEncoder;
using RecursiveAI.OneNlp.Neural;
using RecursiveAI.OneNlp.Neural.Components;
using RecursiveAI.OneNlp.Neural.Encoders;

namespace RecursiveAI.OneContact.Models.Accurate;

/// <summary>
/// The accurate-tier models: an address-field tagger, a name classifier and part tagger, and a contact-span recognizer over OneNLP's frozen distilled
/// multilingual encoder, from head weights packed inside this assembly.
/// </summary>
/// <remarks>
/// Referencing the package is all the extractor needs: the package adds a module initializer to the project that references it, which calls
/// <see cref="Register"/>. Nothing is downloaded and nothing is discovered by reflection. The encoder (a six-layer transformer, 136 MB) and the heads are
/// read on first use. The models only label words; the parsers decide what to do with the labels, and a rule hit is never overruled by them.
/// When a fast-tier package is referenced too it keeps normalizing single addresses and names, and this package reads running text; with this
/// package alone it serves all three.
/// </remarks>
public sealed class AccurateModel : IAddressFieldModel, INameModel, IContactSpanModel
{
    /// <summary>The NuGet package id.</summary>
    public const string PackageId = "RecursiveAI.OneContact.Models.Accurate";

    private const string Weights = "accurate.onenlp";
    private static readonly Lazy<AccurateModel> Shared = new(() => new AccurateModel(Load()), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lock EncoderGate = new();
    private static TransformerEncoder? s_encoder;

    private readonly Pipeline _pipeline;
    private readonly IPipelineComponent _address;
    private readonly IPipelineComponent _nameParts;
    private readonly IPipelineComponent _nameClass;
    private readonly IPipelineComponent _spans;

    internal AccurateModel(Pipeline pipeline)
    {
        _pipeline = pipeline;
        _address = Find(pipeline, AccuratePipeline.AddressHead);
        _nameParts = Find(pipeline, AccuratePipeline.NamePartHead);
        _nameClass = Find(pipeline, AccuratePipeline.NameClassHead);
        _spans = Find(pipeline, AccuratePipeline.SpanHead);
    }

    /// <summary>Makes the extractor use this package's span model, and the address and name parsers use its models when no fast-tier package is referenced. Safe to call more than once and from any thread.</summary>
    /// <remarks>Needed only when the project that references the package cannot compile the module initializer the package adds (a non-C# project, for example).</remarks>
    public static void Register() =>
        ContactModels.Register(PackageId, 3, new LazyModel(), new LazyModel(), new LazyModel(), normalizationRank: 0);

    /// <summary>Gets the models loaded from the weights in this assembly (loaded once, shared by all callers).</summary>
    /// <exception cref="InvalidOperationException">This assembly was built without its weights (a development build).</exception>
    /// <exception cref="InvalidDataException">The weights are damaged.</exception>
    public static AccurateModel Instance => Shared.Value;

    /// <summary>Loads a new copy of the models from a saved pipeline, for use without the parsers (a retrained model, a test).</summary>
    /// <param name="stream">A pipeline file written by the accurate recipe.</param>
    /// <returns>The models. The encoder is the one this process shares.</returns>
    /// <exception cref="InvalidDataException">The file is damaged or lacks one of the heads.</exception>
    public static AccurateModel Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        EnsureEncoder();
        return new(PipelineFile.Load(stream));
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModelSpan> Tag(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Doc doc = NewDoc(text);
        if (doc.Length == 0)
        {
            return [];
        }

        _address.Process(doc);
        return AccurateLabels.Spans(doc);
    }

    /// <inheritdoc/>
    public NameModelResult Analyze(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Doc doc = NewDoc(text);
        if (doc.Length == 0)
        {
            return new NameModelResult(NameClass.Unknown, 0f, []);
        }

        // One pass of the shared encoder serves both heads; calling each head's Process would encode the words twice.
        PredictShared(doc, (TrainableHead)_nameClass, (TrainableHead)_nameParts);
        (NameClass cls, float confidence) = AccurateLabels.Class(doc);
        return new NameModelResult(cls, confidence, AccurateLabels.Spans(doc));
    }

    /// <summary>Finds the contact fields of a text and where each contact block begins.</summary>
    /// <param name="text">Plain text; line breaks tell a signature's lines apart, so keep them.</param>
    /// <returns>Fields labelled <c>person</c>, <c>company</c>, <c>address</c>, <c>phone</c>, <c>email</c> or <c>website</c> with the mean probability of their words, and block starts, all as offsets into <paramref name="text"/>.</returns>
    public ContactSpanResult Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Doc doc = NewDoc(text);
        if (doc.Length == 0)
        {
            return new ContactSpanResult([], []);
        }

        _spans.Process(doc);
        List<ModelSpan> spans = [];
        List<int> starts = [];
        foreach (RecursiveAI.OneNlp.Span e in doc.Ents)
        {
            string label = e.Label;
            if (label.EndsWith(AccuratePipeline.StartSuffix, StringComparison.Ordinal))
            {
                label = label[..^AccuratePipeline.StartSuffix.Length];
                starts.Add(e.StartChar);
            }

            spans.Add(new ModelSpan(e.StartChar, e.EndChar - e.StartChar, label, e.Confidence));
        }

        return new ContactSpanResult(spans, starts);
    }

    // Encodes the document once and lets each head write its labels from the same encoder output (no gradients, activations released when done,
    // the head's weights read one caller at a time).
    private static void PredictShared(Doc doc, params TrainableHead[] heads)
    {
        IEncoder encoder = heads[0].Encoder;
        encoder.Weights?.Eval();
        using NoGradScope noGrad = GradMode.NoGrad();
        using var scope = new ActivationScope();
        EncodedBatch encoded = encoder.Encode([doc]);
        foreach (TrainableHead head in heads)
        {
            lock (head)
            {
                head.Weights.Eval();
                head.Predict(encoded, [doc]);
            }
        }
    }

    private Doc NewDoc(string text)
    {
        List<(int Start, int Length)> words = AccurateTokenizer.Tokenize(text);
        var boundaries = new TokenBoundary[words.Count];
        for (int i = 0; i < boundaries.Length; i++)
        {
            boundaries[i] = new TokenBoundary(words[i].Start, words[i].Length);
        }

        return new Doc(_pipeline.Strings, text, boundaries);
    }

    private static IPipelineComponent Find(Pipeline pipeline, string name)
    {
        foreach (IPipelineComponent component in pipeline.Components)
        {
            if (component.Name == name)
            {
                return component;
            }
        }

        throw new InvalidDataException($"The saved pipeline has no head named '{name}'.");
    }

    // The heads name their encoder; the real one is the frozen student from the OneNLP encoder package, created once per process.
    internal static void EnsureEncoder()
    {
        lock (EncoderGate)
        {
            if (s_encoder is not null)
            {
                return;
            }

            TransformerEncoder encoder = FrozenStudentEncoder.Create();
            SharedEncoder.Register(AccuratePipeline.EncoderName, encoder);
            s_encoder = encoder;
        }
    }

    private static Pipeline Load()
    {
        using Stream stream = typeof(AccurateModel).Assembly.GetManifestResourceStream("weights/" + Weights)
            ?? throw new InvalidOperationException($"This build of {PackageId} has no weights ('{Weights}'): it was built without the weights directory.");
        EnsureEncoder();
        return PipelineFile.Load(stream);
    }

    // The registry holds these so that registering costs nothing: the encoder and the weights are read on the first label request.
    private sealed class LazyModel : IAddressFieldModel, INameModel, IContactSpanModel
    {
        public IReadOnlyList<ModelSpan> Tag(string text) => Instance.Tag(text);

        public NameModelResult Analyze(string text) => Instance.Analyze(text);

        public ContactSpanResult Find(string text) => Instance.Find(text);
    }
}
