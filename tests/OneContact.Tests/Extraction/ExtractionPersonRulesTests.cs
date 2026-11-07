using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Tests;

/// <summary>The rules that read people out of notes and comments typed as free text: names in sentences, in lower case or capitals, in "Last, First" order and in lists.</summary>
public sealed class ExtractionPersonRulesTests
{
    private static string[] Names(string text) => [.. new ContactExtractor().ExtractText(text).Of(FieldKind.Name).Select(f => f.Text)];

    [Theory]
    [InlineData("Maria Lopez called this morning about the renewal.", "Maria Lopez")]
    [InlineData("Yesterday Maria Lopez called us about the invoice.", "Maria Lopez")]
    [InlineData("I talked to Maria Lopez and she said the quote is ready.", "Maria Lopez")]
    [InlineData("Customer wants John R. Smith to handle the account.", "John R. Smith")]
    [InlineData("Great service, Karen Patel was very helpful.", "Karen Patel")]
    public void A_name_in_a_sentence_is_found_when_the_text_gives_a_reason(string text, string expected)
    {
        ExtractedField name = Assert.Single(new ContactExtractor().ExtractText(text).Of(FieldKind.Name));
        Assert.Equal(expected, name.Text);
        Assert.Equal(text.IndexOf(expected, StringComparison.Ordinal), name.Span.Start);
        Assert.Equal(expected.Length, name.Span.Length);
    }

    [Theory]
    [InlineData("Will Call window opens at noon.")]
    [InlineData("Visit Home Depot for all your supplies.")]
    [InlineData("Rose Hill Cemetery offers guided tours.")]
    [InlineData("Lincoln, Nebraska")]
    [InlineData("Paris, France")]
    [InlineData("Great, Thanks for the update.")]
    [InlineData("Sure, Monday works.")]
    [InlineData("Mark Twain Elementary School closes at 3.")]
    [InlineData("Ordered from Wells Fargo Home Mortgage last week.")]
    public void Capitalized_words_that_are_not_a_person_are_left_alone(string text) => Assert.Empty(Names(text));

    [Theory]
    [InlineData("By Dana Reyes | May 3, 2024")]
    [InlineData("\u201CHonest advice.\u201D\nDana Reyes\nMarch 11, 2019")]
    [InlineData("\u2014 Dana Reyes, Toronto")]
    public void A_byline_or_the_signature_under_a_quotation_is_no_contact(string text) => Assert.Empty(Names(text));

    [Fact]
    public void Names_in_lower_case_need_a_cue_or_a_contact_beside_them()
    {
        Assert.Equal(["maria lopez"], Names("spoke with maria lopez about the quote"));
        Assert.Equal(["john smith"], Names("john smith (212) 555-0100"));
        Assert.Empty(Names("john smith is a common name"));
        Assert.Empty(Names("call john tomorrow after lunch about the quote"));
    }

    [Fact]
    public void A_first_name_is_found_after_a_cue_or_before_a_verb()
    {
        Assert.Equal(["Dan"], Names("Thank you, Dan."));
        Assert.Equal(["Sofia"], Names("Sofia called and left a message."));
        Assert.Equal(["john"], Names("ask for john when you arrive"));
        Assert.Empty(Names("Boise and Savannah since 1985"));
    }

    [Fact]
    public void Names_in_capitals_are_names_too()
    {
        Assert.Equal(["MARIA LOPEZ"], Names("SPOKE WITH MARIA LOPEZ RE: QUOTE"));
        Assert.Equal(["MARIA LOPEZ"], Names("CUSTOMER MARIA LOPEZ (212) 555-0142"));
        Assert.Empty(Names("PRIVACY POLICY AND TERMS OF SERVICE"));
    }

    [Fact]
    public void Last_comma_First_is_read_when_it_starts_the_line_or_a_contact_follows()
    {
        Assert.Equal(["Lopez, Maria"], Names("Lopez, Maria - (212) 555-0100"));
        Assert.Equal(["Pieske, Dwaine"], Names("Ref: Pieske, Dwaine; others to follow"));
        Assert.Empty(Names("We moved from Austin, Texas last year."));
    }

    [Fact]
    public void People_in_a_list_follow_the_first_name_found()
    {
        Assert.Equal(["Ana Ruiz", "Pedro Gil", "Luz Marin"], Names("Attendees: Ana Ruiz, Pedro Gil and Luz Marin."));
        Assert.Equal(["Ana Ruiz", "Zbigniew Wolczak"], Names("Meeting with Ana Ruiz and Zbigniew Wolczak on Monday."));
        Assert.Equal(["Ana Ruiz", "Zbigniew Wolczak"], Names("Ana Ruiz\nZbigniew Wolczak"));
    }

    [Fact]
    public void A_header_label_a_title_or_the_email_address_is_enough_for_any_name()
    {
        Assert.Equal(["Zbigniew Wolczak"], Names("From: Zbigniew Wolczak\nSent: Monday"));
        Assert.Equal(["Dr. Patel"], Names("Dr. Patel will see you at 3."));
        Assert.Empty(Names("Our office is on Oak Dr near the park."));
        Assert.Equal(["Zbigniew Wolczak"], Names("Zbigniew Wolczak left a note. Write to zbigniew.wolczak@example.org"));
    }

    [Fact]
    public void On_a_web_page_only_the_strong_evidence_counts()
    {
        // The same sentence that names a person in a note is, on a page, as likely a product review or a byline.
        Assert.Empty(new ContactExtractor().ExtractHtml("<html><body><p>Maria Lopez called this morning about the renewal.</p></body></html>").Of(FieldKind.Name));
        Assert.Empty(new ContactExtractor().ExtractHtml("<html><body><p>Dr. Patel will see you at 3.</p></body></html>").Of(FieldKind.Name));
        Assert.Empty(new ContactExtractor().ExtractHtml("<html><body><p>From: Zbigniew Wolczak</p></body></html>").Of(FieldKind.Name));
        ExtractedField titled = Assert.Single(new ContactExtractor().ExtractHtml("<html><body><p>Dr. Patel, Chief Medical Officer</p></body></html>").Of(FieldKind.Name));
        Assert.Equal("Dr. Patel", titled.Text);
    }
}
