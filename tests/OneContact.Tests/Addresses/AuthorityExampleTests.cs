using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Every example address of USPS Publication 28 and the Canada Post Addressing Guidelines (English and French editions), as transcribed in
/// testdata/addresses/authority-examples.tsv: the guide's variant form in, the guide's standard form out, each with its citation.
/// </summary>
public class AuthorityExampleTests
{
    public static TheoryData<string, string, string, string, string> Cases()
    {
        TheoryData<string, string, string, string, string> data = [];
        foreach (string[] c in AddressTestData.Rows("testdata/addresses/authority-examples.tsv", 5))
        {
            data.Add(c[0], c[1], c[2], c[3].Replace("\\n", "\n", StringComparison.Ordinal), c[4].Replace("\\n", "\n", StringComparison.Ordinal));
        }

        return data;
    }

    // The guides' figures show one or two spaces before the ZIP or postal code (Publication 28 section 224 prefers two, Canada Post requires two); the comparison ignores the width of runs of spaces.
    private static string Squash(string s) => string.Join('\n', s.Split('\n').Select(l => string.Join(' ', l.Split(' ', StringSplitOptions.RemoveEmptyEntries))));

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExampleIsStandardized(string id, string citation, string kind, string input, string expected)
    {
        string lastLine = kind switch
        {
            "line-us" => "ANYTOWN VA 22042",
            "line-pr" => "SAN JUAN PR 00907",
            "line-ca" => "MONTREAL QC H3Z 2Y7",
            _ => string.Empty,
        };
        string wrapped = lastLine.Length == 0 ? input : input + "\n" + lastLine;
        string want = lastLine.Length == 0 ? expected : expected + "\n" + lastLine;

        OperationStatus status = RuleAddressParser.TryParse(wrapped, Region.Unknown, out ParsedAddress? address, out ParseDetail detail);
        Assert.True(status == OperationStatus.Done, $"{id} ({citation}) was not read: {detail.PrimaryReason}");
        string got = address!.ToString(AddressStyle.Postal, AddressLayout.Label, includeCountry: kind == "full-country");
        Assert.Equal(Squash(want), Squash(got));
    }
}
