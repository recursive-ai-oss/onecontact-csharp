using RecursiveAI.OneContact.Names;
using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Neural;

namespace RecursiveAI.OneContact.Models.Tier1;

/// <summary>
/// The United States and Canada models: an address-field tagger and a name classifier and part tagger on one shared fast-tier encoder, from
/// weights packed inside this assembly.
/// </summary>
/// <remarks>
/// Referencing the package is all the parsers need: the package adds a module initializer to the project that references it, which calls
/// <see cref="Register"/>. Nothing is downloaded and nothing is discovered by reflection. The weights are read on first use. The models only
/// label words; the parsers decide what to do with the labels, and a rule hit is never overruled by them.
/// </remarks>
public sealed class Tier1Model : IAddressFieldModel, INameModel
{
    /// <summary>The NuGet package id.</summary>
    public const string PackageId = ContactModels.Tier1PackageId;

    private const string Weights = "tier1.onenlp";
    private static readonly Lazy<Tier1Model> Shared = new(() => new Tier1Model(Load()), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Pipeline _pipeline;
    private readonly IPipelineComponent _address;
    private readonly IPipelineComponent _nameParts;
    private readonly IPipelineComponent _nameClass;

    internal Tier1Model(Pipeline pipeline)
    {
        _pipeline = pipeline;
        _address = Find(pipeline, Tier1Pipeline.AddressHead);
        _nameParts = Find(pipeline, Tier1Pipeline.NamePartHead);
        _nameClass = Find(pipeline, Tier1Pipeline.NameClassHead);
    }

    /// <summary>Makes the parsers use this package's models. Safe to call more than once and from any thread. Needed only when the project that
    /// references the package cannot compile the module initializer the package adds (a non-C# project, for example).</summary>
    public static void Register() =>
        ContactModels.Register(PackageId, 1, new LazyModel(), new LazyModel());

    /// <summary>Gets the models loaded from the weights in this assembly (loaded once, shared by all callers).</summary>
    /// <exception cref="InvalidOperationException">This assembly was built without its weights (a development build).</exception>
    /// <exception cref="InvalidDataException">The weights are damaged.</exception>
    public static Tier1Model Instance => Shared.Value;

    /// <summary>Loads a new copy of the models from a saved pipeline, for use without the parsers (a retrained model, a test).</summary>
    /// <param name="stream">A pipeline file written by the Tier 1 recipe.</param>
    /// <returns>The models; the caller owns nothing else.</returns>
    /// <exception cref="InvalidDataException">The file is damaged or lacks one of the three heads.</exception>
    public static Tier1Model Load(Stream stream) => new(PipelineFile.Load(stream));

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
        return Tier1Labels.Spans(doc, TokenColumn.Tag);
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

        _nameClass.Process(doc);
        (NameClass cls, float confidence) = Tier1Labels.Vote(doc);
        _nameParts.Process(doc);
        return new NameModelResult(cls, confidence, Tier1Labels.Spans(doc, TokenColumn.Pos));
    }

    private Doc NewDoc(string text)
    {
        List<(int Start, int Length)> words = Tier1Tokenizer.Tokenize(text);
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

    private static Pipeline Load()
    {
        using Stream stream = typeof(Tier1Model).Assembly.GetManifestResourceStream("weights/" + Weights)
            ?? throw new InvalidOperationException($"This build of {PackageId} has no weights ('{Weights}'): it was built without the weights directory.");
        return PipelineFile.Load(stream);
    }

    // The registry holds these two so that registering costs nothing: the weights are read on the first label request.
    private sealed class LazyModel : IAddressFieldModel, INameModel
    {
        public IReadOnlyList<ModelSpan> Tag(string text) => Instance.Tag(text);

        public NameModelResult Analyze(string text) => Instance.Analyze(text);
    }
}
