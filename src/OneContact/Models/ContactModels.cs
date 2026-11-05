using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Models;

/// <summary>A labelled stretch of the input found by a model.</summary>
/// <param name="Start">The offset of the first UTF-16 code unit in the original input.</param>
/// <param name="Length">The number of UTF-16 code units; at least 1.</param>
/// <param name="Label">The field label: <c>street.name</c>, <c>locality</c>, <c>recipient</c>, ... for addresses; <c>Given</c>, <c>Family</c>, ... for names.</param>
/// <param name="Confidence">The mean probability of the label over the stretch's words, from 0 to 1.</param>
public readonly record struct ModelSpan(int Start, int Length, string Label, float Confidence)
{
    /// <summary>Gets the offset just past the last code unit.</summary>
    public int End => Start + Length;
}

/// <summary>What a name model says about a string.</summary>
/// <param name="Class">The class the model chose; never <see cref="NameClass.Unknown"/> unless the model has no opinion.</param>
/// <param name="ClassConfidence">The share of the string's words that voted for the class, weighted by their probabilities, from 0 to 1.</param>
/// <param name="Parts">The labelled parts: person labels (<c>Prefix</c>, <c>Given</c>, <c>Middle</c>, <c>Nickname</c>, <c>Particle</c>, <c>Family</c>, <c>SecondFamily</c>, <c>Suffix</c>, <c>ProfessionalSuffix</c>) or company labels (<c>LeadingArticle</c>, <c>Base</c>, <c>LegalForm</c>, <c>TradingAs</c>, <c>CareOf</c>).</param>
public sealed record NameModelResult(NameClass Class, float ClassConfidence, IReadOnlyList<ModelSpan> Parts);

/// <summary>A trained model that labels the fields of a free-text address. Implementations must be safe to call from several threads.</summary>
public interface IAddressFieldModel
{
    /// <summary>Labels the fields of an address.</summary>
    /// <param name="text">The address, one or several lines.</param>
    /// <returns>The labelled stretches in input order, non-overlapping; words the model gave no field are not covered. Offsets index <paramref name="text"/>.</returns>
    IReadOnlyList<ModelSpan> Tag(string text);
}

/// <summary>A trained model that classifies a name string and labels its parts. Implementations must be safe to call from several threads.</summary>
public interface INameModel
{
    /// <summary>Classifies a name string and labels its parts.</summary>
    /// <param name="text">The name.</param>
    /// <returns>The class and the parts; offsets index <paramref name="text"/>.</returns>
    NameModelResult Analyze(string text);
}

/// <summary>What a span model found in a stretch of text.</summary>
/// <param name="Spans">The fields, in input order and without overlap. <see cref="ModelSpan.Label"/> is one of <c>person</c>, <c>company</c>, <c>address</c>, <c>phone</c>, <c>email</c> or <c>website</c>.</param>
/// <param name="BlockStarts">The offsets where the model reads a new contact block (a person's or a company's entry) begin, increasing. A block is what a reader sees as one entry: a signature, a staff-list row, a footer's address block.</param>
public sealed record ContactSpanResult(IReadOnlyList<ModelSpan> Spans, IReadOnlyList<int> BlockStarts);

/// <summary>A trained model that finds the six contact fields (person, company, address, phone, email, website) in running text and where each contact block begins. Implementations must be safe to call from several threads.</summary>
public interface IContactSpanModel
{
    /// <summary>Finds contact fields in a text.</summary>
    /// <param name="text">Plain text, one or several lines; a page's visible text is passed in pieces of a few dozen lines.</param>
    /// <returns>The fields and block starts; offsets index <paramref name="text"/>. Rules are not consulted: the caller decides which spans to keep.</returns>
    ContactSpanResult Find(string text);
}

/// <summary>
/// The place where a model package makes its models known to the parsers. Nothing is found by scanning assemblies or by reflection: the model package
/// ships one source file that the referencing project compiles, and that file calls <see cref="Register"/> from a module initializer.
/// </summary>
/// <remarks>
/// When rules cannot decide and no model is registered, parse results say so and name <see cref="Tier1PackageId"/>. Models are never downloaded.
/// </remarks>
public static class ContactModels
{
    /// <summary>The NuGet package that carries the United States and Canada models.</summary>
    public const string Tier1PackageId = "RecursiveAI.OneContact.Models.Tier1";

    private static readonly Lock Gate = new();
    private static Registration? s_current;

    // Lets a test use a model for the current async flow only, so the process-wide registration other tests see is untouched.
    private static readonly AsyncLocal<Registration?> s_scoped = new();

    /// <summary>Gets the registered address model, or <see langword="null"/> when no model package is referenced.</summary>
    public static IAddressFieldModel? Address => (s_scoped.Value ?? Volatile.Read(ref s_current))?.Address;

    /// <summary>Gets the registered name model, or <see langword="null"/> when no model package is referenced.</summary>
    public static INameModel? Name => (s_scoped.Value ?? Volatile.Read(ref s_current))?.Name;

    /// <summary>Gets the registered contact-span model, or <see langword="null"/> when no model package that carries one is referenced.</summary>
    public static IContactSpanModel? Spans => (s_scoped.Value ?? Volatile.Read(ref s_current))?.Spans;

    /// <summary>Gets the id of the package whose models are in use, or <see langword="null"/> when none is.</summary>
    public static string? PackageId => (s_scoped.Value ?? Volatile.Read(ref s_current))?.PackageId;

    /// <summary>Makes a package's models known. Calling it again for the same package does nothing; when several packages register, the one with the highest rank wins.</summary>
    /// <param name="packageId">The NuGet package id, for messages and for <see cref="PackageId"/>.</param>
    /// <param name="rank">How specific the package is: 1 for Tier 1, 2 for the Tier 1+2 superset, 3 for the accurate tier.</param>
    /// <param name="address">The address model, or <see langword="null"/>.</param>
    /// <param name="name">The name model, or <see langword="null"/>.</param>
    /// <param name="spans">The contact-span model, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="packageId"/> is empty.</exception>
    public static void Register(string packageId, int rank, IAddressFieldModel? address, INameModel? name, IContactSpanModel? spans = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        lock (Gate)
        {
            Registration? current = s_current;
            if (current is null || rank > current.Rank)
            {
                Volatile.Write(ref s_current, new Registration(packageId, rank, address, name, spans));
            }
        }
    }

    /// <summary>Uses the given models in the current async flow until the returned object is disposed; for tests.</summary>
    internal static IDisposable Scope(IAddressFieldModel? address, INameModel? name, IContactSpanModel? spans = null)
    {
        Registration? previous = s_scoped.Value;
        s_scoped.Value = new Registration("scoped", int.MaxValue, address, name, spans);
        return new Restore(previous);
    }

    private sealed class Restore(Registration? previous) : IDisposable
    {
        public void Dispose() => s_scoped.Value = previous;
    }

    private sealed record Registration(string PackageId, int Rank, IAddressFieldModel? Address, INameModel? Name, IContactSpanModel? Spans);
}
