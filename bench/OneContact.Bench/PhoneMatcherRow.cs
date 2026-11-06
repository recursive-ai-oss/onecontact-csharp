using System.Text;
using BenchmarkDotNet.Attributes;
using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// Matcher throughput on about 1 MB of prose in which roughly one word in forty is a phone number of the default region, written
/// in several ways, among dates, ids and digit runs. The text is seeded, so every run searches the same bytes.
/// </summary>
[MemoryDiagnoser]
public class PhoneMatcherRow
{
    private string _text = "";

    /// <summary>Builds the text.</summary>
    [GlobalSetup]
    public void Setup() => _text = MakeText();

    /// <summary>Builds the seeded text.</summary>
    /// <returns>About one million characters.</returns>
    internal static string MakeText()
    {
        string[] words = ["the", "customer", "called", "about", "order", "and", "asked", "for", "a", "refund", "on", "12/03/2021", "at", "10:30:15", "ref", "A-4471", "please", "reply", "to", "support", "2012-12-22"];
        string[] numbers = ["(202) 456-1111", "202-456-1111", "+1 650 253 0000", "1-800-356-9377", "650.253.0000", "+44 20 7946 0958", "2024561111", "(212) 736-5000 ext. 12"];
        Random rng = new(7);
        StringBuilder sb = new(1_100_000);
        while (sb.Length < 1_000_000)
        {
            if (rng.Next(40) == 0)
            {
                sb.Append(numbers[rng.Next(numbers.Length)]);
            }
            else
            {
                sb.Append(words[rng.Next(words.Length)]);
            }

            sb.Append(rng.Next(12) == 0 ? ". " : " ");
        }

        return sb.ToString();
    }

    /// <summary>Gets the leniency under test.</summary>
    [Params(PhoneMatchLeniency.Possible, PhoneMatchLeniency.Valid, PhoneMatchLeniency.ExactGrouping)]
    public PhoneMatchLeniency Leniency { get; set; }

    /// <summary>Finds every number in the text.</summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public int FindAll()
    {
        int count = 0;
        foreach (PhoneNumberMatch unused in PhoneNumber.FindAll(_text, Region.US, Leniency))
        {
            count++;
        }

        return count;
    }
}
