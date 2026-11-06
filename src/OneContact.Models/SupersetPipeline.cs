using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Neural.Components;
using RecursiveAI.OneNlp.Neural.Encoders;

namespace RecursiveAI.OneContact.Models.Superset;

/// <summary>
/// The shape of the Tier 1 + Tier 2 pipeline: one fast-tier encoder shared by three taggers. The recipe trains this shape and the package loads the saved
/// result, so the shape lives in one place.
/// </summary>
internal static class SupersetPipeline
{
    /// <summary>Name of the address-field head (labels in the Tag column).</summary>
    internal const string AddressHead = "address-fields";

    /// <summary>Name of the name-part head (labels in the Pos column).</summary>
    internal const string NamePartHead = "name-parts";

    /// <summary>Name of the name-class head (labels in the Morph column, one class per word).</summary>
    internal const string NameClassHead = "name-class";

    /// <summary>Name of the head that finds the six contact fields (person, company, address, phone, email, website) as entities.</summary>
    internal const string SpanHead = "contact-spans";

    /// <summary>
    /// Added to a field label for the first field of each contact block (<c>person:start</c>). One head reads both: where fields are and where a
    /// block begins, because a block begins at its first field.
    /// </summary>
    internal const string StartSuffix = ":start";

    /// <summary>Makes the untrained contact-span head over an existing encoder: BILUO tags decoded without overlap, two words of context each side.</summary>
    internal static EntityRecognizer NewSpanHead(IEncoder encoder) =>
        new(encoder, NerDecoding.Tagging, context: 2, hidden: 128, name: SpanHead) { OwnsEncoder = false };

    /// <summary>Makes the untrained encoder. Everything that shapes the model is written here.</summary>
    /// <param name="seed">Seed of the initial weights.</param>
    internal static FastEncoder NewEncoder(ulong seed) =>
        new(width: 128, depth: 3, normRows: 32768, prefixRows: 4096, suffixRows: 16384, shapeRows: 2048, seed: seed, prefixLength: 3, suffixLength: 3, casedRows: 12288, embedWidth: 64, hashes: 2, relu: true, neighbourRows: 4096);

    /// <summary>Makes the untrained pipeline: the encoder and the three heads.</summary>
    /// <param name="seed">Seed of the initial weights.</param>
    internal static Pipeline New(ulong seed)
    {
        FastEncoder encoder = NewEncoder(seed);
        Pipeline pipeline = Pipeline.Blank(Language.English);
        pipeline.Add(new Tagger(encoder, TokenColumn.Tag, AddressHead) { OwnsEncoder = true });
        pipeline.Add(new Tagger(encoder, TokenColumn.Pos, NamePartHead) { OwnsEncoder = false });
        pipeline.Add(new Tagger(encoder, TokenColumn.Morph, NameClassHead) { OwnsEncoder = false });
        return pipeline;
    }
}
