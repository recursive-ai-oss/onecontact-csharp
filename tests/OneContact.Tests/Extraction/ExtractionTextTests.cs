using System.Text;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Tests;

/// <summary>The visible-text rules: fixtures with golden results, offsets back into the input, hidden-text traps and obfuscation.</summary>
public sealed class ExtractionTextTests
{
    private static readonly ContactExtractor TextOnly = new() { Sources = ExtractionSources.Text };

    private static string TextRoot => Path.Combine(ExtractionFixtures.Root, "text");

    private static ExtractResult Page(string name, ContactExtractor? extractor = null)
    {
        string file = ExtractionFixtures.TextPageFiles().Single(f => ExtractionFixtures.NameOf(f) == name);
        return (extractor ?? new ContactExtractor()).ExtractHtml(File.ReadAllBytes(file));
    }

    private static ExtractResult Note(string name)
    {
        string file = ExtractionFixtures.NoteFiles().Single(f => ExtractionFixtures.NameOf(f) == name);
        return new ContactExtractor().ExtractText(new UTF8Encoding(false).GetString(File.ReadAllBytes(file)));
    }

    private static string[] Texts(ExtractResult r, FieldKind kind) => [.. r.Fields.Where(f => f.Kind == kind).Select(f => f.Text)];

    [Fact]
    public void There_are_at_least_sixty_text_fixtures()
    {
        Assert.True(ExtractionFixtures.TextPageFiles().Count() + ExtractionFixtures.NoteFiles().Count() >= 60);
    }

    [Fact]
    public void Every_fixture_gives_its_golden_result()
    {
        string? update = Environment.GetEnvironmentVariable("ONECONTACT_UPDATE_GOLDEN");
        var failures = new List<string>();
        foreach ((string name, ExtractResult result) in AllResults())
        {
            string actual = ExtractionFixtures.Dump(result);
            if (!string.IsNullOrEmpty(update))
            {
                Directory.CreateDirectory(Path.Combine(update, "text"));
                File.WriteAllText(Path.Combine(update, "text", name + ".txt"), actual, new UTF8Encoding(false));
                continue;
            }

            string golden = Path.Combine(TextRoot, "golden", name + ".txt");
            if (!File.Exists(golden))
            {
                failures.Add(name + ": no golden file");
            }
            else if (File.ReadAllText(golden) != actual)
            {
                failures.Add(name + ": differs from its golden file");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Every_golden_file_has_a_fixture()
    {
        string[] names = [.. ExtractionFixtures.TextPageFiles().Concat(ExtractionFixtures.NoteFiles()).Select(ExtractionFixtures.NameOf)];
        foreach (string golden in Directory.EnumerateFiles(Path.Combine(TextRoot, "golden")))
        {
            Assert.Contains(Path.GetFileNameWithoutExtension(golden), names);
        }
    }

    private static IEnumerable<(string Name, ExtractResult Result)> AllResults()
    {
        foreach (string file in ExtractionFixtures.TextPageFiles())
        {
            yield return (ExtractionFixtures.NameOf(file), new ContactExtractor().ExtractHtml(File.ReadAllBytes(file)));
        }

        foreach (string file in ExtractionFixtures.NoteFiles())
        {
            yield return (ExtractionFixtures.NameOf(file), new ContactExtractor().ExtractText(new UTF8Encoding(false).GetString(File.ReadAllBytes(file))));
        }
    }

    // The raw value of every field is the very bytes (or characters) it was read from; for a decoded field that is the obfuscated form.
    [Fact]
    public void Offsets_name_the_raw_text_in_the_original_input()
    {
        int checkedFields = 0;
        foreach (string file in ExtractionFixtures.TextPageFiles())
        {
            byte[] bytes = File.ReadAllBytes(file);
            Encoding encoding = ExtractionFixtures.EncodingOf(file);
            ExtractResult result = new ContactExtractor().ExtractHtml(bytes);
            foreach (ExtractedField f in ExtractionFixtureTests.AllFields(result).Where(f => f.HasSpan))
            {
                string at = encoding.GetString(bytes, f.Span.Start, f.Span.Length).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                Assert.True(at == f.Raw, $"{ExtractionFixtures.NameOf(file)}: bytes at {f.Span.Start}+{f.Span.Length} are \"{at}\", raw value is \"{f.Raw}\"");
                checkedFields++;
            }
        }

        foreach (string file in ExtractionFixtures.NoteFiles())
        {
            string text = new UTF8Encoding(false).GetString(File.ReadAllBytes(file));
            ExtractResult result = new ContactExtractor().ExtractText(text);
            Assert.Equal(OffsetUnit.Chars, result.OffsetUnit);
            foreach (ExtractedField f in ExtractionFixtureTests.AllFields(result).Where(f => f.HasSpan))
            {
                Assert.True(text.Substring(f.Span.Start, f.Span.Length) == f.Raw, $"{ExtractionFixtures.NameOf(file)}: raw value differs from the text at its span");
                checkedFields++;
            }
        }

        Assert.True(checkedFields > 150, $"only {checkedFields} fields were checked");
    }

    // A page given as a string has chars as offsets into the string as written, line ends included.
    [Fact]
    public void A_page_as_a_string_has_char_offsets_into_that_string()
    {
        foreach (string file in ExtractionFixtures.TextPageFiles().Where(f => ExtractionFixtures.EncodingOf(f).CodePage == 65001))
        {
            string html = new UTF8Encoding(false).GetString(File.ReadAllBytes(file)).TrimStart('\uFEFF');
            ExtractResult result = new ContactExtractor().ExtractHtml(html);
            foreach (ExtractedField f in ExtractionFixtureTests.AllFields(result).Where(f => f.HasSpan))
            {
                string at = html.Substring(f.Span.Start, f.Span.Length).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                Assert.True(at == f.Raw, $"{ExtractionFixtures.NameOf(file)}: chars at {f.Span.Start}+{f.Span.Length} are \"{at}\", raw value is \"{f.Raw}\"");
            }
        }
    }

    [Fact]
    public void Bytes_and_string_inputs_find_the_same_values()
    {
        foreach (string file in ExtractionFixtures.TextPageFiles().Where(f => ExtractionFixtures.EncodingOf(f).CodePage == 65001))
        {
            byte[] bytes = File.ReadAllBytes(file);
            string html = new UTF8Encoding(false).GetString(bytes).TrimStart('\uFEFF');
            string[] fromBytes = [.. ExtractionFixtureTests.AllFields(new ContactExtractor().ExtractHtml(bytes)).Select(f => f.Kind + "|" + f.Text)];
            string[] fromString = [.. ExtractionFixtureTests.AllFields(new ContactExtractor().ExtractHtml(html)).Select(f => f.Kind + "|" + f.Text)];
            Assert.True(fromBytes.SequenceEqual(fromString), ExtractionFixtures.NameOf(file));
        }
    }

    // Every trap value contains "trap" or the number 0199: none of them may come out.
    [Fact]
    public void Hidden_text_is_never_read()
    {
        int pages = 0;
        foreach (string file in ExtractionFixtures.TextPageFiles().Where(f => ExtractionFixtures.NameOf(f).StartsWith("hidden-", StringComparison.Ordinal)
            || ExtractionFixtures.NameOf(f) == "obf-cloudflare-hidden"))
        {
            ExtractResult result = new ContactExtractor().ExtractHtml(File.ReadAllBytes(file));
            foreach (ExtractedField f in ExtractionFixtureTests.AllFields(result))
            {
                Assert.DoesNotContain("trap", f.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("0199", f.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("trap", f.Raw, StringComparison.OrdinalIgnoreCase);
            }

            pages++;
        }

        Assert.True(pages >= 18, $"only {pages} trap pages");
    }

    [Fact]
    public void A_visible_child_of_a_hidden_parent_is_read()
    {
        ExtractResult r = Page("hidden-child-shows-in-hidden-parent");
        Assert.Equal(["shown@example.com"], Texts(r, FieldKind.Email));
    }

    [Fact]
    public void The_control_value_on_every_trap_page_is_still_found()
    {
        foreach (string file in ExtractionFixtures.TextPageFiles().Where(f => ExtractionFixtures.NameOf(f).StartsWith("hidden-", StringComparison.Ordinal)
            && ExtractionFixtures.NameOf(f) != "hidden-child-shows-in-hidden-parent"))
        {
            ExtractResult result = new ContactExtractor().ExtractHtml(File.ReadAllBytes(file));
            Assert.Contains(result.Fields, f => f.Kind == FieldKind.Email && f.Text == "visible@example.com");
        }
    }

    [Theory]
    [InlineData("obf-at-brackets", "john@example.com")]
    [InlineData("obf-at-parentheses", "john@example.com")]
    [InlineData("obf-at-braces", "john@example.com")]
    [InlineData("obf-at-angle-spaced", "john@example.com")]
    [InlineData("obf-words", "john@example.com")]
    [InlineData("obf-words-uppercase", "JOHN@EXAMPLE.COM")]
    [InlineData("obf-dashes", "john@example.com")]
    [InlineData("obf-dot-words-in-local-part", "john.smith@example.co.uk")]
    [InlineData("obf-bracket-dot-only", "john@example.com")]
    [InlineData("obf-fullwidth", "john@example.com")]
    [InlineData("obf-percent-escapes", "john@example.com")]
    [InlineData("obf-spaced-at-sign", "john@example.com")]
    [InlineData("obf-split-by-span", "john@example.com")]
    [InlineData("obf-split-with-hidden-junk", "john@example.com")]
    [InlineData("obf-zero-width-characters", "john@example.com")]
    [InlineData("obf-cloudflare-span", "cf.person@example.com")]
    [InlineData("obf-cloudflare-link", "cf.link@example.com")]
    [InlineData("obf-reversed-css", "john@example.com")]
    [InlineData("obf-reversed-bdo", "jane@example.com")]
    public void Obfuscated_addresses_are_decoded_and_flagged(string page, string expected)
    {
        ExtractedField f = Assert.Single(Page(page).Fields, x => x.Kind == FieldKind.Email);
        Assert.Equal(expected, f.Text);
        Assert.True(f.Flags.HasFlag(ExtractedFieldFacts.Deobfuscated), page);
        Assert.Equal(FieldSource.Text, f.Source);
        Assert.True(f.IsNormalized);
    }

    [Fact]
    public void Characters_written_as_references_are_decoded_and_flagged()
    {
        ExtractResult r = Page("obf-entities");
        Assert.Equal(["john@example.com", "jane@example.org"], Texts(r, FieldKind.Email));
        Assert.All(r.Of(FieldKind.Email), f => Assert.True(f.Flags.HasFlag(ExtractedFieldFacts.Deobfuscated)));
        // Each address has its own span, not the whole text node.
        Assert.Equal("jo&#104;n&#64;example&#46;com", r.Of(FieldKind.Email)[0].Raw);
        Assert.Equal("jane&commat;example.org", r.Of(FieldKind.Email)[1].Raw);
    }

    [Fact]
    public void A_url_user_name_is_not_a_mailbox()
    {
        Assert.Equal(["ops@acme.example"], Texts(Page("email-not-url-userinfo"), FieldKind.Email));
    }

    [Fact]
    public void Prose_with_the_word_at_is_not_an_address()
    {
        Assert.Empty(Page("obf-prose-is-not-an-address").Of(FieldKind.Email));
    }

    [Fact]
    public void Rtl_text_without_an_override_is_not_reversed()
    {
        Assert.Empty(Page("obf-reversed-not-reversed-without-override").Of(FieldKind.Email));
    }

    [Fact]
    public void Malformed_cloudflare_values_give_nothing()
    {
        Assert.Empty(Page("obf-cloudflare-malformed").Fields);
    }

    [Fact]
    public void A_plain_address_is_not_flagged()
    {
        ExtractedField f = Assert.Single(Page("mixed-mailto-link-and-its-text").Of(FieldKind.Email), x => x.Text == "link@acme.example");
        Assert.False(f.Flags.HasFlag(ExtractedFieldFacts.Deobfuscated));
    }

    [Fact]
    public void The_raw_form_of_a_decoded_field_is_what_the_page_wrote()
    {
        ExtractedField f = Assert.Single(Page("obf-at-brackets").Of(FieldKind.Email));
        Assert.Equal("john [at] example [dot] com", f.Raw);
        ExtractedField cf = Assert.Single(Page("obf-cloudflare-span").Of(FieldKind.Email));
        Assert.Matches("^[0-9a-f]+$", cf.Raw);
        ExtractedField reversed = Assert.Single(Page("obf-reversed-css").Of(FieldKind.Email));
        Assert.Equal("moc.elpmaxe@nhoj", reversed.Raw);
    }

    [Fact]
    public void A_duplicate_of_a_markup_field_is_one_field_with_the_text_sighting_kept()
    {
        ExtractResult r = Page("mixed-jsonld-and-footer-agree");
        ContactRecord org = Assert.Single(r.Records, x => x.Kind == ContactRecordKind.Organization);
        ExtractedField email = Assert.Single(org.Emails);
        Assert.Equal(FieldSource.JsonLd, email.Source);
        Assert.Contains(email.Duplicates, d => d.Source == FieldSource.Text);
        ExtractedField phone = Assert.Single(org.Phones);
        Assert.Contains(phone.Duplicates, d => d.Source == FieldSource.Text);
        Assert.Empty(org.Disagreements);
    }

    [Fact]
    public void A_different_value_in_the_text_is_kept_beside_the_markup_one()
    {
        ExtractResult r = Page("mixed-jsonld-and-footer-disagree");
        ContactRecord org = Assert.Single(r.Records, x => x.Kind == ContactRecordKind.Organization);
        Assert.Equal(2, org.Phones.Count);
        Assert.Contains(org.Phones, f => f.Source == FieldSource.Text && f.Flags.HasFlag(ExtractedFieldFacts.Disputed));
        FieldDisagreement d = Assert.Single(org.Disagreements);
        Assert.Equal(FieldKind.Phone, d.Kind);
    }

    [Fact]
    public void A_lone_text_field_that_repeats_one_record_joins_it()
    {
        ExtractResult r = Page("mixed-jsonld-and-footer-single-email");
        ContactRecord org = Assert.Single(r.Records);
        Assert.Contains(org.Emails.Single().Duplicates, d => d.Source == FieldSource.Text);
        Assert.Empty(r.Ungrouped);
    }

    [Fact]
    public void Text_inside_a_microdata_item_joins_its_record()
    {
        ExtractResult r = Page("mixed-microdata-and-visible-text");
        ContactRecord person = Assert.Single(r.Records);
        Assert.Equal(ContactRecordKind.Person, person.Kind);
        Assert.All(person.Fields, f => Assert.Equal(FieldSource.Microdata, f.Source));
        Assert.Contains(person.Emails.Single().Duplicates, d => d.Source == FieldSource.Text);
    }

    [Fact]
    public void Fields_of_one_block_form_a_record_of_unknown_kind()
    {
        ExtractResult r = Page("layout-footer-contact");
        ContactRecord record = Assert.Single(r.Records);
        Assert.Equal(ContactRecordKind.Unknown, record.Kind);
        Assert.Single(record.Emails);
        Assert.Single(record.Phones);
        Assert.Single(record.Addresses);
        Assert.Single(record.Websites);
    }

    [Fact]
    public void Table_rows_and_cards_are_separate_records()
    {
        Assert.Equal(3, Page("layout-staff-table").Records.Count);
        Assert.Equal(2, Page("layout-staff-cards").Records.Count);
        Assert.Equal(2, Page("layout-staff-headings-in-main").Records.Count);
        Assert.Equal(2, Page("layout-landmarks-header-footer").Records.Count);
    }

    [Fact]
    public void A_name_label_makes_a_person_record_and_a_company_label_an_organization()
    {
        ContactRecord person = Assert.Single(Page("name-labelled").Records);
        Assert.Equal(ContactRecordKind.Person, person.Kind);
        Assert.Equal("Jane Doe", person.Name!.Text);
        ContactRecord org = Assert.Single(Page("name-labelled-company").Records);
        Assert.Equal(ContactRecordKind.Organization, org.Kind);
        Assert.Equal("Acme Anvils Ltd", org.Name!.Text);
        ContactRecord both = Assert.Single(Page("name-labelled-both").Records);
        Assert.Equal(ContactRecordKind.Person, both.Kind);
        Assert.Equal("Acme Anvils Ltd", both.Organizations.Single().Text);
    }

    [Fact]
    public void Names_without_a_certain_label_are_not_read()
    {
        ExtractResult r = Page("name-label-rejected-values");
        Assert.Empty(r.Of(FieldKind.Name));
    }

    [Fact]
    public void Dates_addresses_and_prices_are_not_phone_numbers()
    {
        Assert.Empty(Page("phone-not-phones").Of(FieldKind.Phone));
        Assert.Empty(Page("address-not-an-address").Of(FieldKind.Address));
    }

    [Fact]
    public void An_unlabelled_national_number_needs_a_region_and_is_dropped_without_one()
    {
        ExtractResult r = Page("phone-national-no-region");
        string[] phones = Texts(r, FieldKind.Phone);
        Assert.Contains("+1 202 555 0129", phones);
        Assert.DoesNotContain("202-555-0128", phones);
        // A labelled one stays as text, unparsed, with the reason.
        ExtractedField labelled = Assert.Single(r.Fields, f => f.Text == "202-555-0127");
        Assert.True(labelled.Flags.HasFlag(ExtractedFieldFacts.Unnormalized));
        Assert.Equal("phone.region-required", labelled.Issue.Id);
    }

    [Fact]
    public void An_address_is_read_in_the_page_region_and_left_unparsed_without_one()
    {
        ExtractedField withRegion = Assert.Single(Page("address-us-br-lines").Of(FieldKind.Address));
        Assert.True(withRegion.IsNormalized);
        Assert.True(withRegion.Flags.HasFlag(ExtractedFieldFacts.RegionInferred));
        ExtractedField without = Assert.Single(Page("address-no-region").Of(FieldKind.Address));
        Assert.False(without.IsNormalized);
        Assert.Equal("address.country-required", without.Issue.Id);
        Assert.Equal("100 Main Street\nSpringfield, IL 62701", without.Text);
    }

    [Fact]
    public void A_caller_region_reads_the_address_when_the_page_names_none()
    {
        var extractor = new ContactExtractor { DefaultRegion = Region.US };
        ExtractedField f = Assert.Single(Page("address-no-region", extractor).Of(FieldKind.Address));
        Assert.True(f.IsNormalized);
    }

    [Fact]
    public void A_plain_string_is_read_as_text_only()
    {
        ExtractResult r = new ContactExtractor { DefaultRegion = Region.US }.ExtractText("Name: Jo Example\nTel: (202) 555-0100\njo@example.com\n<script type=\"application/ld+json\">{\"@type\":\"Person\",\"name\":\"Nobody\"}</script>");
        ContactRecord record = Assert.Single(r.Records);
        Assert.Equal("Jo Example", record.Name!.Text);
        Assert.Equal("+12025550100", record.Phones.Single().Normalized.Canonical);
        Assert.Equal(OffsetUnit.Chars, r.OffsetUnit);
        Assert.Empty(new ContactExtractor { Sources = ExtractionSources.Markup }.ExtractText("jo@example.com").Fields);
    }

    [Fact]
    public void Line_endings_do_not_change_what_a_note_gives()
    {
        string[] lf = [.. Note("note-basic").Fields.Select(f => f.Kind + "|" + f.Text)];
        string text = "Spoke with Jane Doe today.\r\nName: Jane Doe\r\nEmail: jane@acme.example\r\nPhone: +1 202 555 0170\r\n";
        string[] crlf = [.. new ContactExtractor().ExtractText(text).Fields.Select(f => f.Kind + "|" + f.Text)];
        Assert.Equal(lf, crlf);
    }

    [Fact]
    public void Text_findings_can_be_turned_off()
    {
        ExtractResult markup = Page("layout-footer-contact", new ContactExtractor { Sources = ExtractionSources.Markup });
        Assert.DoesNotContain(markup.Fields, f => f.Source == FieldSource.Text);
        ExtractResult text = Page("layout-footer-contact", TextOnly);
        Assert.NotEmpty(text.Fields);
        Assert.All(text.Fields, f => Assert.Equal(FieldSource.Text, f.Source));
    }

    [Fact]
    public void The_happy_path_includes_the_text()
    {
        IReadOnlyList<ContactRecord> records = ContactExtractor.Extract("<footer><p>Call +1 202 555 0100</p><p>team@example.com</p></footer>");
        ContactRecord r = Assert.Single(records);
        Assert.Single(r.Emails);
        Assert.Single(r.Phones);
    }
}
