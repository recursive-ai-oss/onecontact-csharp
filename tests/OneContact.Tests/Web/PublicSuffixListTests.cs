using System.Text.RegularExpressions;
using RecursiveAI.OneContact.Web;

namespace RecursiveAI.OneContact.Tests;

/// <summary>The Public Suffix List's own test file (testdata/psl/test_psl.txt, pinned in upstream/psl/PIN) and the algorithm's corners.</summary>
public partial class PublicSuffixListTests
{
    [GeneratedRegex(@"^checkPublicSuffix\((null|'[^']*'), (null|'[^']*')\);$")]
    private static partial Regex CheckLine();

    private static string? Arg(string s) => s == "null" ? null : s[1..^1];

    [Fact]
    public void PublicSuffixListTestFile()
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "testdata", "psl", "test_psl.txt"));
        var failures = new List<string>();
        int total = 0;
        foreach (string line in lines)
        {
            Match m = CheckLine().Match(line.Trim());
            if (!m.Success)
            {
                Assert.True(line.Trim().Length == 0 || line.StartsWith("//", StringComparison.Ordinal), "unparsed line: " + line);
                continue;
            }

            total++;
            string? input = Arg(m.Groups[1].Value);
            string? want = Arg(m.Groups[2].Value);
            string? got = PublicSuffixList.GetRegistrableDomain(input);
            if (got != want)
            {
                failures.Add($"{input ?? "null"}: want {want ?? "null"} got {got ?? "null"}");
            }
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "psl-test.txt"), $"test_psl.txt: total={total} pass={total - failures.Count} fail={failures.Count}\n" + string.Join('\n', failures));
        Assert.Equal(78, total); // the checks that are not commented out in the pinned file
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Theory]
    [InlineData("www.example.co.uk", "co.uk", "example.co.uk")]
    [InlineData("a.b.github.io", "github.io", "b.github.io")]
    [InlineData("city.kobe.jp", "kobe.jp", "city.kobe.jp")]
    [InlineData("foo.bar.kobe.jp", "bar.kobe.jp", "foo.bar.kobe.jp")]
    [InlineData("EXAMPLE.COM", "com", "example.com")]
    public void SuffixAndRegistrableDomain(string host, string suffix, string registrable)
    {
        Assert.Equal(suffix, PublicSuffixList.GetPublicSuffix(host));
        Assert.Equal(registrable, PublicSuffixList.GetRegistrableDomain(host));
    }

    [Fact]
    public void PrivateSectionCanBeIgnored()
    {
        Assert.Equal("github.io", PublicSuffixList.GetPublicSuffix("user.github.io", includePrivateSection: true));
        Assert.Equal("io", PublicSuffixList.GetPublicSuffix("user.github.io", includePrivateSection: false));
        Assert.Equal("github.io", PublicSuffixList.GetRegistrableDomain("user.github.io", includePrivateSection: false));
    }

    [Fact]
    public void UnicodeAndPunycodeAgree()
    {
        Assert.Equal("食狮.com.cn", PublicSuffixList.GetRegistrableDomain("www.食狮.com.cn"));
        Assert.Equal("xn--85x722f.com.cn", PublicSuffixList.GetRegistrableDomain("www.xn--85x722f.com.cn"));
        Assert.Equal("example.公司.cn", PublicSuffixList.GetRegistrableDomain("a.example.公司.cn"));
    }
}
