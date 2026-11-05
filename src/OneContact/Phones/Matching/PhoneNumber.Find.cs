using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact;

public readonly partial struct PhoneNumber
{
    /// <summary>Finds the phone numbers in a text.</summary>
    /// <param name="text">The text to search.</param>
    /// <param name="defaultRegion">The region that numbers without a country code belong to; <see cref="Region.Unknown"/> finds only numbers written with a country code.</param>
    /// <param name="leniency">How sure a candidate must be before it is reported. There is no default: the levels differ a lot in what they accept.</param>
    /// <returns>The matches in text order, produced lazily. Each carries its offsets in <paramref name="text"/>, the text it covers and the parsed number.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <remarks>Use <see cref="PhoneNumberMatcher"/> for a bound on the work done or to search a span.</remarks>
    public static IEnumerable<PhoneNumberMatch> FindAll(string text, Region defaultRegion, PhoneMatchLeniency leniency) =>
        PhoneNumberMatcher.Default.FindAll(text, defaultRegion, leniency);
}
