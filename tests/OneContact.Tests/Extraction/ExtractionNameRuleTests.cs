using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Tests;

/// <summary>The rule finder for people and organizations in visible text: what counts as evidence, and what is refused.</summary>
public sealed class ExtractionNameRuleTests
{
    private static readonly ContactExtractor Rules = new() { Sources = ExtractionSources.Text };

    private static string[] People(string text) => Names(text, ContactKind.Person);

    private static string[] Companies(string text) => Names(text, ContactKind.Company);

    private static string[] Names(string text, ContactKind kind) =>
        [.. Rules.ExtractText(text).Fields.Where(f => f.Kind is FieldKind.Name or FieldKind.Organization && f.Normalized.Kind == kind).Select(f => f.Text)];

    [Fact]
    public void A_name_beside_an_email_address_is_a_person()
    {
        Assert.Equal(["Maria Lopez"], People("Maria Lopez\nmaria.lopez@harbor.example"));
    }

    [Fact]
    public void A_name_beside_a_phone_number_is_a_person()
    {
        Assert.Equal(["Daniel Brooks"], People("Daniel Brooks\n+1 202 555 0143"));
    }

    [Fact]
    public void A_name_over_a_job_line_is_a_person_and_the_job_is_not_output()
    {
        ExtractResult r = Rules.ExtractText("Tom Heller\nSales Manager\ntom@harbor.example");
        Assert.Equal(["Tom Heller"], r.Of(FieldKind.Name).Select(f => f.Text).ToArray());
    }

    [Fact]
    public void A_cue_phrase_points_at_a_person()
    {
        Assert.Contains("Helen Marsh", People("Please contact Helen Marsh about the order."));
    }

    [Fact]
    public void A_closing_line_above_a_name_makes_it_a_person()
    {
        Assert.Contains("Peter Wolfe", People("Thanks for your help.\n\nBest regards,\nPeter Wolfe"));
    }

    [Fact]
    public void A_single_name_after_a_greeting_is_a_person()
    {
        Assert.Contains("Lucile", People("Hi Lucile,\nthe files are attached."));
    }

    [Fact]
    public void Two_capitalized_words_in_prose_are_not_a_person()
    {
        Assert.Empty(People("The Quarterly Report was filed on time and nobody objected."));
    }

    [Fact]
    public void Place_and_region_words_are_not_people()
    {
        Assert.Empty(People("New York\nnyc@harbor.example"));
        Assert.Empty(People("Customer Service\nhelp@harbor.example"));
    }

    [Fact]
    public void A_legal_form_makes_an_organization()
    {
        Assert.Equal(["Harbor Freight LLC"], Companies("Harbor Freight LLC\n+1 202 555 0143"));
    }

    [Fact]
    public void A_dba_part_stays_inside_the_organization()
    {
        Assert.Contains(Companies("Harbor Freight LLC dba Quick Move\n+1 202 555 0143"), c => c.StartsWith("Harbor Freight LLC", StringComparison.Ordinal));
    }

    [Fact]
    public void A_copyright_line_names_an_organization()
    {
        Assert.Contains("Harbor Freight LLC", Companies("© 2026 Harbor Freight LLC. All rights reserved."));
    }

    [Fact]
    public void An_employer_after_at_is_an_organization()
    {
        Assert.Contains("Harbor Freight LLC", Companies("Anna Keller\nDirector at Harbor Freight LLC\nanna@harbor.example"));
    }

    [Fact]
    public void A_sentence_with_a_legal_form_in_the_middle_is_cut_at_the_form()
    {
        Assert.DoesNotContain(Companies("We shipped with Harbor Freight LLC and were happy."), c => c.Contains("happy", StringComparison.Ordinal));
    }

    [Fact]
    public void An_email_that_ends_in_a_file_extension_is_not_an_email()
    {
        Assert.Empty(Rules.ExtractText("see logo@2x.png for the image").Of(FieldKind.Email));
        Assert.Single(Rules.ExtractText("write to info@harbor.example").Of(FieldKind.Email));
    }

    [Fact]
    public void Side_by_side_elements_do_not_merge_into_one_organization()
    {
        ExtractResult r = Rules.ExtractHtml("<html><body><div><span>Harbor Freight</span> <span>Quick Movers LLC</span></div><p>office@harbor.example</p></body></html>");
        Assert.DoesNotContain(r.Of(FieldKind.Organization), f => f.Text.Contains("Freight Quick", StringComparison.Ordinal));
    }

    [Fact]
    public void A_text_name_equal_to_a_markup_organization_is_an_organization()
    {
        const string html = "<html><body><script type=\"application/ld+json\">{\"@type\":\"Organization\",\"name\":\"Harbor Freight\"}</script>"
            + "<p>Harbor Freight</p><p>harbor@harbor.example</p></body></html>";
        ExtractResult r = new ContactExtractor().ExtractHtml(html);
        Assert.DoesNotContain(r.Of(FieldKind.Name), f => f.Text == "Harbor Freight" && f.Normalized.Kind == ContactKind.Person);
    }

    [Fact]
    public void Every_name_span_points_back_into_the_input()
    {
        const string text = "Maria Lopez\nmaria.lopez@harbor.example\nHarbor Freight LLC";
        foreach (ExtractedField f in Rules.ExtractText(text).Of(FieldKind.Name))
        {
            Assert.Equal(f.Text, text.Substring(f.Span.Start, f.Span.Length));
        }
    }
}
