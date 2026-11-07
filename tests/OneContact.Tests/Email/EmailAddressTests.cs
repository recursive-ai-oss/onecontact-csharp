using System.Buffers;
using System.Globalization;
using System.Text.Json;
using RecursiveAI.OneContact.Email;
using RecursiveAI.OneContact.Keys;

namespace RecursiveAI.OneContact.Tests;

public sealed class EmailAddressTests
{
    [Fact]
    public void Happy_path_example()
    {
        EmailAddress e = EmailAddress.Parse("\"J. Smith\" <J.Smith+crm@GoogleMail.com>");
        Assert.Equal("J.Smith+crm@googlemail.com", e.Canonical);
        Assert.Equal("jsmith@gmail.com", e.Key);
        Assert.True(e.Flags.HasFlag(EmailFacts.FreeProvider));
        Assert.Equal("J. Smith", e.DisplayName);
        Assert.Equal("J.Smith+crm", e.LocalPart);
        Assert.Equal("googlemail.com", e.Domain);
        Assert.Equal(e.Key, e.GetKey(KeyVersion.V1));
        Assert.Equal(e.Key, EmailKey.Compute(e, KeyVersion.V1));
    }

    [Fact]
    public void Offsets_index_the_original_input()
    {
        const string input = "  \"J. Smith\" <j.smith@example.com>  ";
        Assert.Equal(OperationStatus.Done, EmailParser.TryParse(input, out EmailAddress e, out ParseDetail detail));
        Assert.Equal(new SourceSpan(2, 32), e.Source);
        Assert.Equal(input.IndexOf("\"J. Smith\"", StringComparison.Ordinal), detail.Fields.Single(f => f.Name == "display-name").Span.Start);
        ParsedField local = detail.Fields.Single(f => f.Name == "local-part");
        ParsedField domain = detail.Fields.Single(f => f.Name == "domain");
        Assert.Equal("j.smith", input.Substring(local.Span.Start, local.Span.Length));
        Assert.Equal("example.com", input.Substring(domain.Span.Start, domain.Span.Length));
        Assert.Equal("\"J. Smith\"", input.Substring(detail.Fields[0].Span.Start, detail.Fields[0].Span.Length));
        Assert.All(detail.Fields, f => Assert.Equal((1f, DecisionSource.Rule), (f.Confidence, f.Source)));
    }

    [Fact]
    public void Offsets_skip_comments_and_keep_the_domain_as_written()
    {
        const string input = "(x) a(c) @ (d) Example.COM (e)";
        Assert.Equal(OperationStatus.Done, EmailParser.TryParse(input, out EmailAddress e, out ParseDetail detail));
        Assert.Equal("a@example.com", e.Canonical);
        ParsedField local = detail.Fields.Single(f => f.Name == "local-part");
        ParsedField domain = detail.Fields.Single(f => f.Name == "domain");
        Assert.Equal("a", input.Substring(local.Span.Start, local.Span.Length));
        Assert.Equal("Example.COM", input.Substring(domain.Span.Start, domain.Span.Length));
    }

    [Fact]
    public void Failure_points_at_the_offending_character()
    {
        Assert.Equal(OperationStatus.InvalidData, EmailParser.TryParse("a b@x.example", out EmailAddress none, out ParseDetail detail));
        Assert.Equal(default, none);
        Assert.Equal(ReasonCode.EmailInvalidLocalPart, detail.PrimaryReason);
        Assert.Equal(new SourceSpan(2, 1), detail.Issues[0].Span);

        ContactParseException ex = Assert.Throws<ContactParseException>(() => EmailAddress.Parse("nobody"));
        Assert.Equal(ReasonCode.EmailMissingAt, ex.Reason);
        Assert.Equal(ContactKind.Email, ex.Kind);
        Assert.False(EmailAddress.TryParse((string?)null, out _));
    }

    [Fact]
    public void Issues_report_obsolete_syntax_lengths_and_typos_as_non_errors()
    {
        EmailParser.TryParse("a@b.example (x) ", out _, out ParseDetail none);
        Assert.Empty(none.Issues);

        EmailParser.TryParse("jdoe@test  . example", out _, out ParseDetail obs);
        Assert.Contains(obs.Issues, i => i.Reason == ReasonCode.EmailObsoleteSyntax && i.Severity == IssueSeverity.Info);

        EmailParser.TryParse(new string('a', 65) + "@example.com", out _, out ParseDetail longLocal);
        Assert.Contains(longLocal.Issues, i => i.Reason == ReasonCode.EmailLocalPartTooLong && i.Severity == IssueSeverity.Warning);
        Assert.Equal("email.local-part-too-long", ReasonCode.EmailLocalPartTooLong.Id);

        EmailParser.TryParse("a@" + new string('b', 64) + ".example", out _, out ParseDetail longLabel);
        Assert.Contains(longLabel.Issues, i => i.Reason == ReasonCode.EmailLabelTooLong);

        EmailParser.TryParse("a@gmial.com", out _, out ParseDetail typo);
        Assert.Contains(typo.Issues, i => i.Reason == ReasonCode.EmailPossibleTypo && i.Severity == IssueSeverity.Info);

        EmailParser.TryParse("a@[abc]", out _, out ParseDetail literal);
        Assert.Contains(literal.Issues, i => i.Reason == ReasonCode.EmailNotSmtpLiteral && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void Typo_suggestions_are_never_applied()
    {
        EmailAddress e = EmailAddress.Parse("Jane.Doe@gmial.com");
        Assert.Equal("Jane.Doe@gmial.com", e.Canonical);
        Assert.Equal("Jane.Doe@gmail.com", e.TypoSuggestion);
        Assert.Contains("gmial.com", e.Key, StringComparison.Ordinal);
        Assert.Null(EmailAddress.Parse("jane@gmail.com").TypoSuggestion);
        Assert.Null(default(EmailAddress).TypoSuggestion);
    }

    [Fact]
    public void Unicode_domain_shows_the_u_labels()
    {
        EmailAddress e = EmailAddress.Parse("info@B\u00fccher.example");
        Assert.Equal("xn--bcher-kva.example", e.Domain);
        Assert.Equal("b\u00fccher.example", e.UnicodeDomain);
        Assert.Equal("example.com", EmailAddress.Parse("a@example.com").UnicodeDomain);
        Assert.Equal("[192.0.2.1]", EmailAddress.Parse("a@[192.0.2.1]").UnicodeDomain);
    }

    [Fact]
    public void Default_value_is_empty()
    {
        EmailAddress d = default;
        Assert.Equal(string.Empty, d.Canonical);
        Assert.Equal(string.Empty, d.Key);
        Assert.Equal(string.Empty, d.LocalPart);
        Assert.Equal(string.Empty, d.Domain);
        Assert.Null(d.DisplayName);
    }

    [Fact]
    public void Equality_compares_canonical_forms_and_keys_only_on_request()
    {
        EmailAddress a = EmailAddress.Parse("John.Smith@Example.com");
        EmailAddress b = EmailAddress.Parse("Name <John.Smith@EXAMPLE.COM>");
        EmailAddress c = EmailAddress.Parse("john.smith@example.com");
        Assert.True(a == b);
        Assert.True(a != c);
        Assert.True(Contacts.SameKey(a, c));
        Assert.False(a.Equals(c));
    }

    [Fact]
    public void Parsing_ignores_the_current_culture()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            EmailAddress e = EmailAddress.Parse("I.I@EXAMPLE.COM");
            Assert.Equal("I.I@example.com", e.Canonical);
            Assert.Equal("i.i@example.com", e.Key);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Contacts_and_json_and_span_parsing_agree()
    {
        ContactValue v = Contacts.Normalize("Jane <jane@EXAMPLE.com>");
        Assert.Equal(ContactKind.Email, v.Kind);
        Assert.True(v.TryGetEmail(out EmailAddress fromContacts));
        Assert.Equal("jane@example.com", fromContacts.Canonical);

        // The display name is kept by the value, so it is kept by the JSON form too.
        string json = JsonSerializer.Serialize(fromContacts, Serialization.ContactJsonContext.Default.EmailAddress);
        EmailAddress back = JsonSerializer.Deserialize(json, Serialization.ContactJsonContext.Default.EmailAddress);
        Assert.Equal(fromContacts, back);
        Assert.Equal("Jane", back.DisplayName);

        Assert.Equal(fromContacts, EmailAddress.Parse("jane@example.com".AsSpan()));
        Assert.Equal("jane@example.com", fromContacts.ToString("C", null));
    }

    [Fact]
    public void ParseMany_gives_one_result_per_input_in_order()
    {
        EmailParseResult[] results = [.. EmailParser.ParseMany(["a@example.com", "nope", null, "B <b@example.org>"])];
        Assert.Equal([0, 1, 2, 3], results.Select(r => r.Index));
        Assert.Equal(
            [OperationStatus.Done, OperationStatus.InvalidData, OperationStatus.InvalidData, OperationStatus.Done],
            results.Select(r => r.Status));
        Assert.Equal("b@example.org", results[3].Value.Canonical);
        Assert.Equal(ReasonCode.Empty, results[2].Detail.PrimaryReason);
    }

    [Fact]
    public void Deep_comment_nesting_and_huge_inputs_fail_cleanly()
    {
        string deep = new string('(', 5000) + "a@example.com" + new string(')', 5000);
        Assert.Equal(OperationStatus.InvalidData, EmailParser.TryParse(deep, out _, out ParseDetail detail));
        Assert.Equal(ReasonCode.EmailInvalidComment, detail.PrimaryReason);

        string many = string.Concat(Enumerable.Repeat("a.", 100000)) + "b@example.com";
        Assert.Equal(OperationStatus.Done, EmailParser.TryParse(many, out EmailAddress e, out _));
        Assert.True(e.Flags.HasFlag(EmailFacts.ExceedsLengthLimit));
    }
}
