using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Neural.Components;
using RecursiveAI.OneNlp.Neural.Encoders;

namespace RecursiveAI.OneContact.Models.Accurate;

/// <summary>
/// The shape of the accurate-tier pipeline: the frozen multilingual student encoder, shared by name, under two window recognizers, a classifier and the contact-span
/// recognizer. Only the heads are saved; the encoder comes from the OneNLP encoder package. The recipe trains this shape and the package loads the
/// saved result, so the shape lives in one place.
/// </summary>
internal static class AccuratePipeline
{
    /// <summary>Name of the address-field head (labels in the Tag column).</summary>
    internal const string AddressHead = "address-fields";

    /// <summary>Name of the name-part head (labels in the Pos column).</summary>
    internal const string NamePartHead = "name-parts";

    /// <summary>Name of the name-class head (labels in the Morph column, one class per word).</summary>
    internal const string NameClassHead = "name-class";

    /// <summary>Name of the head that finds the six contact fields (person, company, address, phone, email, website) as entities.</summary>
    internal const string SpanHead = "contact-spans";

    /// <summary>Added to a field label for the first field of each contact block (<c>person:start</c>).</summary>
    internal const string StartSuffix = ":start";

    /// <summary>The name under which the saved heads refer to the encoder. A new encoder gets a new name, so old heads are never run over it.</summary>
    internal const string EncoderName = "contact-student-v1";

    /// <summary>Makes the untrained address-field head: BILUO tags over a window of three words each side and a hidden layer of 512, because the frozen encoder alone separates address fields poorly with a linear layer.</summary>
    internal static EntityRecognizer NewAddressHead(IEncoder encoder) =>
        new(encoder, NerDecoding.Tagging, context: 3, hidden: 512, name: AddressHead) { OwnsEncoder = false };

    /// <summary>Makes the untrained name-part head: BILUO tags over a window of two words each side and a hidden layer of 256.</summary>
    internal static EntityRecognizer NewNamePartHead(IEncoder encoder) =>
        new(encoder, NerDecoding.Tagging, context: 2, hidden: 256, name: NamePartHead) { OwnsEncoder = false };

    /// <summary>Makes the untrained name-class head: one class (person, company, multiple, unknown) for the whole string, a hidden layer of 128.</summary>
    internal static TextClassifier NewNameClassHead(IEncoder encoder) =>
        new(encoder, multiLabel: false, hiddenSize: 128, name: NameClassHead) { OwnsEncoder = false };

    /// <summary>Makes the untrained contact-span head: BILUO tags decoded without overlap, two words of context each side and a hidden layer of 256.</summary>
    internal static EntityRecognizer NewSpanHead(IEncoder encoder) =>
        new(encoder, NerDecoding.Tagging, context: 2, hidden: 256, name: SpanHead) { OwnsEncoder = false };
}
