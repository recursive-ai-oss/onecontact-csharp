using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Tests;

public sealed class ExtractionBehaviourTests
{
    private static ExtractResult Page(string name, ContactExtractor? extractor = null)
    {
        string file = ExtractionFixtures.PageFiles().Single(f => ExtractionFixtures.NameOf(f) == name);
        return (extractor ?? new ContactExtractor()).ExtractHtml(File.ReadAllBytes(file));
    }

    [Fact]
    public void The_happy_path_returns_records()
    {
        IReadOnlyList<ContactRecord> records = ContactExtractor.Extract("<script type=\"application/ld+json\">{\"@type\":\"Person\",\"name\":\"Jo Example\"}</script>");
        ContactRecord r = Assert.Single(records);
        Assert.Equal(ContactRecordKind.Person, r.Kind);
        Assert.Equal("Jo Example", r.Name!.Text);
        Assert.Equal(ContactKind.Person, r.Name.Normalized.Kind);
    }

    [Fact]
    public void Malformed_json_ld_is_rejected_and_the_other_readers_still_run()
    {
        ExtractResult r = Page("jsonld-malformed-others-run");
        Assert.Equal(ExtractIssueKind.MalformedJsonLd, Assert.Single(r.Issues).Kind);
        Assert.Equal(["Micro Person", "Card Person"], r.Records.Select(x => x.Name!.Text).ToArray());
        Assert.Equal(2, r.Records.Count);

        ExtractResult trailing = Page("jsonld-malformed-trailing-comma");
        Assert.Empty(trailing.Records);
        Assert.Single(trailing.Issues);
        Assert.Equal("still@example.com", Assert.Single(trailing.Fields).Normalized.Canonical);

        ExtractResult second = Page("jsonld-malformed-then-good");
        Assert.Equal("Good Second Block", Assert.Single(second.Records).Name!.Text);
        Assert.Single(second.Issues);
    }

    [Fact]
    public void Hidden_structured_markup_is_read_and_flagged()
    {
        foreach (string name in new[] { "jsonld-hidden-block", "microdata-hidden", "hcard-hidden", "links-hidden" })
        {
            ExtractResult r = Page(name);
            Assert.NotEmpty(r.Fields);
            Assert.All(r.Fields, f => Assert.True(f.Flags.HasFlag(ExtractedFieldFacts.Hidden), name));
        }

        Assert.DoesNotContain(Page("microdata-person").Fields, f => f.Flags.HasFlag(ExtractedFieldFacts.Hidden));
    }

    [Fact]
    public void An_entity_encoded_mailto_is_decoded_and_its_raw_text_is_kept()
    {
        ExtractedField f = Assert.Single(Page("links-mailto-entity-encoded").Fields);
        Assert.Equal(FieldSource.Link, f.Source);
        Assert.StartsWith("&#109;ailto:", f.Raw, StringComparison.Ordinal);
        Assert.Equal("secret@example.com", f.Text);
        Assert.Equal("secret@example.com", f.Normalized.Canonical);
        Assert.Equal("write to us", f.Label);
    }

    [Fact]
    public void A_mailto_with_several_recipients_and_a_query_gives_each_recipient()
    {
        string[] emails = [.. Page("links-mailto-query-multiple").Fields.Select(f => f.Text)];
        Assert.Equal(["one@example.com", "two@example.com"], emails);
    }

    [Fact]
    public void A_national_phone_number_needs_a_region_and_never_gets_a_guessed_one()
    {
        ExtractedField none = Assert.Single(Page("links-tel-national-no-region").Fields);
        Assert.True(none.Flags.HasFlag(ExtractedFieldFacts.Unnormalized));
        Assert.Equal("phone.region-required", none.Issue.Id);
        Assert.False(none.IsNormalized);

        ExtractedField page = Assert.Single(Page("links-tel-national-page-region").Fields);
        Assert.True(page.Flags.HasFlag(ExtractedFieldFacts.RegionInferred));
        Assert.Equal("+12025550123", page.Normalized.Canonical);

        ExtractedField caller = Assert.Single(Page("links-tel-national-no-region", new ContactExtractor { DefaultRegion = Region.US }).Fields);
        Assert.True(caller.Flags.HasFlag(ExtractedFieldFacts.RegionInferred));

        ExtractedField international = Page("links-tel-label").Fields[0];
        Assert.False(international.Flags.HasFlag(ExtractedFieldFacts.RegionInferred));
    }

    [Fact]
    public void A_phone_number_is_read_in_the_country_of_the_address_in_the_same_record()
    {
        // Canadian address, national number: the number is Canadian, not the page's US.
        ExtractResult r = Page("jsonld-location");
        ExtractedField phone = Assert.Single(r.Records.Single().Phones);
        Assert.Equal("+14165550111", phone.Normalized.Canonical);
        Assert.True(phone.Flags.HasFlag(ExtractedFieldFacts.RegionInferred));
    }

    [Fact]
    public void Conflicting_values_are_all_kept_and_reported()
    {
        ContactRecord names = Page("conflict-two-names").Records.Single();
        Assert.Equal(["First Spelling", "Second Spelling"], names.Names.Select(f => f.Text).ToArray());
        FieldDisagreement d = Assert.Single(names.Disagreements);
        Assert.Equal(FieldKind.Name, d.Kind);
        Assert.All(d.Fields, f => Assert.True(f.Flags.HasFlag(ExtractedFieldFacts.Disputed)));
        Assert.Single(names.Emails);

        ContactRecord phones = Page("conflict-phone-jsonld-vs-link").Records.Single();
        Assert.Equal(2, phones.Phones.Count);
        Assert.Equal(FieldKind.Phone, Assert.Single(phones.Disagreements).Kind);

        ContactRecord stale = Page("conflict-jsonld-stale-phone").Records.Single();
        Assert.Equal(2, stale.Phones.Count);
        Assert.Equal(new[] { FieldSource.JsonLd, FieldSource.HCard }, stale.Phones.Select(f => f.Source).Order().ToArray());
        Assert.Single(stale.Disagreements);
    }

    [Fact]
    public void The_same_entity_read_twice_is_one_record_with_the_other_sighting_kept()
    {
        ContactRecord agree = Page("conflict-agree-jsonld-and-microdata").Records.Single();
        Assert.Empty(agree.Disagreements);
        ExtractedField phone = Assert.Single(agree.Phones);
        Assert.Single(phone.Duplicates);
        Assert.NotEqual(phone.Source, phone.Duplicates[0].Source);
        Assert.Single(agree.Names);

        ContactRecord dee = Page("dedup-same-person-two-readers").Records.Single();
        Assert.Single(dee.Emails);
        Assert.Single(dee.Phones);
    }

    [Fact]
    public void Two_people_sharing_a_phone_number_stay_two_records()
    {
        ExtractResult r = Page("dedup-different-people-same-phone");
        Assert.Equal(2, r.Records.Count);
        Assert.All(r.Records, rec => Assert.Single(rec.Phones));
    }

    [Fact]
    public void Employees_and_employers_are_linked_both_ways()
    {
        ExtractResult r = Page("jsonld-nested-employee");
        ContactRecord org = r.Records.Single(x => x.Kind == ContactRecordKind.Organization);
        ContactRecord[] people = [.. r.Records.Where(x => x.Kind == ContactRecordKind.Person)];
        Assert.Equal(2, people.Length);
        Assert.Equal(2, org.Links.Count(l => l.Kind == RecordLinkKind.HasMember));
        Assert.All(people, p => Assert.Same(org, p.Organization));

        ContactRecord worker = Page("jsonld-worksfor-nested").Records.Single(x => x.Kind == ContactRecordKind.Person);
        Assert.Equal("Employer Ltd", worker.Organization!.Name!.Text);
        Assert.Equal("Employer Ltd", worker.Organizations.Single().Text);

        ContactRecord textual = Assert.Single(Page("jsonld-worksfor-text").Records);
        Assert.Null(textual.Organization);
        Assert.Equal("Text Employer GmbH", textual.Organizations.Single().Text);
    }

    [Fact]
    public void An_id_reference_resolves_and_a_reference_cycle_terminates()
    {
        ContactRecord org = Page("jsonld-id-reference").Records.Single();
        Assert.Equal("2 Reference Way", Assert.Single(org.Addresses).Normalized.TryGetAddress(out PostalAddress a) ? a.StreetLines[0] : null);

        ExtractResult cycle = Page("jsonld-reference-cycle");
        Assert.Equal(2, cycle.Records.Count);
        Assert.All(cycle.Records, r => Assert.Equal(2, r.Links.Count));
    }

    [Fact]
    public void Links_inside_a_structured_item_join_its_record()
    {
        ExtractResult r = Page("microdata-link-inside-item");
        ContactRecord org = r.Records.Single();
        Assert.Contains(org.Fields, f => f.Source == FieldSource.Link && f.Kind == FieldKind.Email);
        Assert.Contains(org.Fields, f => f.Source == FieldSource.Link && f.Kind == FieldKind.Phone);
        Assert.Empty(r.Ungrouped);
    }

    [Fact]
    public void A_link_that_a_structured_reader_took_is_not_read_twice()
    {
        ExtractResult r = Page("microdata-person");
        Assert.Single(r.Records.Single().Emails);
        Assert.DoesNotContain(r.Fields, f => f.Source == FieldSource.Link);
    }

    [Fact]
    public void An_address_element_that_is_a_property_is_read_once_and_a_loose_one_is_kept_with_its_block()
    {
        ExtractResult consumed = Page("address-element-consumed");
        Assert.DoesNotContain(consumed.Fields, f => f.Source == FieldSource.AddressElement);
        Assert.Single(consumed.Records.Single().Addresses);

        ExtractResult two = Page("address-element-two");
        Assert.Equal(2, two.Ungrouped.Count);
        Assert.NotEqual(two.Ungrouped[0].BlockId, two.Ungrouped[1].BlockId);

        ExtractResult inPerson = Page("address-element-in-person-item");
        Assert.Contains(inPerson.Records.Single().Addresses, f => f.Source == FieldSource.AddressElement);

        ExtractResult links = Page("links-in-address");
        Assert.Equal(3, links.Ungrouped.Count);
        Assert.All(links.Ungrouped, f => Assert.True(f.BlockId >= 0));
    }

    [Fact]
    public void A_free_text_address_is_read_only_in_a_known_country()
    {
        ExtractedField none = Assert.Single(Page("address-element-no-region").Fields);
        Assert.True(none.Flags.HasFlag(ExtractedFieldFacts.Unnormalized));
        Assert.Equal("address.country-required", none.Issue.Id);

        ExtractedField us = Assert.Single(Page("address-element-us").Fields);
        Assert.True(us.IsNormalized);
        Assert.True(us.Flags.HasFlag(ExtractedFieldFacts.RegionInferred));
        Assert.Equal(FieldSource.AddressElement, us.Source);
    }

    [Fact]
    public void An_address_without_a_country_keeps_its_parts_and_no_country()
    {
        PostalAddress a;
        ExtractedField f = Assert.Single(Page("microdata-no-type-address").Records.Single().Addresses);
        Assert.True(f.Normalized.TryGetAddress(out a));
        Assert.True(a.Country.IsUnknown);
        Assert.Equal("Austin", a.Locality);
        Assert.Equal("address.country-required", f.Issue.Id);
    }

    [Fact]
    public void The_sources_flag_limits_the_readers()
    {
        ExtractResult all = Page("conflict-agree-jsonld-and-microdata");
        Assert.Contains(all.Fields.SelectMany(f => f.Duplicates.Prepend(f)), f => f.Source == FieldSource.Microdata);

        ExtractResult onlyJson = Page("conflict-agree-jsonld-and-microdata", new ContactExtractor { Sources = ExtractionSources.JsonLd });
        Assert.All(onlyJson.Fields, f => Assert.Equal(FieldSource.JsonLd, f.Source));
        Assert.Equal(ExtractionSources.JsonLd, onlyJson.Sources);

        Assert.Empty(Page("microdata-person", new ContactExtractor { Sources = ExtractionSources.None }).Fields);
    }

    [Fact]
    public void A_card_is_an_organization_when_it_names_itself_as_its_org_or_has_a_legal_form()
    {
        Assert.Equal(ContactRecordKind.Organization, Page("hcard-fn-org-same").Records.Single().Kind);
        Assert.Equal(ContactRecordKind.Organization, Page("hcard-legal-form-org").Records.Single().Kind);
        Assert.Equal(ContactRecordKind.Person, Page("hcard-person").Records.Single().Kind);
        ContactRecord nested = Page("hcard-nested-org").Records.Single(r => r.Kind == ContactRecordKind.Person);
        Assert.Equal("Nested Employer Corp", nested.Organization!.Name!.Text);
    }

    [Fact]
    public void A_given_and_a_family_name_make_one_composed_name_covering_both()
    {
        ExtractedField f = Page("jsonld-person-given-family").Records.Single().Name!;
        Assert.Equal("Alex Sample", f.Text);
        Assert.True(f.Flags.HasFlag(ExtractedFieldFacts.Composed));
        Assert.Contains("Alex", f.Raw, StringComparison.Ordinal);
        Assert.Contains("Sample", f.Raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_escapes_are_decoded_in_the_text_and_kept_in_the_raw_value()
    {
        ExtractedField f = Page("jsonld-escapes").Records.Single().Name!;
        Assert.Equal("Caf\u00e9 \"Example\" & Co", f.Text);
        Assert.Contains("\\u00e9", f.Raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Microdata_text_with_character_references_is_decoded()
    {
        ContactRecord r = Page("microdata-entities").Records.Single();
        Assert.Equal("Tom & Jerry's Pty Ltd", r.Name!.Text);
        Assert.Contains("&amp;", r.Name.Raw, StringComparison.Ordinal);
        Assert.Equal("tom@example.com", r.Emails.Single().Text);
    }

    [Fact]
    public void A_foreign_rdfa_vocabulary_is_ignored()
    {
        Assert.Empty(Page("rdfa-other-vocab-ignored").Records);
    }

    [Fact]
    public void A_page_without_markup_and_an_empty_page_give_nothing()
    {
        Assert.Empty(Page("no-markup").Fields);
        Assert.Empty(Page("empty-page").Fields);
    }

    [Fact]
    public void Facts_of_the_normalized_value_are_listed()
    {
        ContactRecord r = ContactExtractor.Extract("<script type=\"application/ld+json\">{\"@type\":\"Person\",\"name\":\"Gmail User\",\"email\":\"someone@gmail.com\"}</script>").Single();
        Assert.Contains("FreeProvider", r.Emails.Single().Facts);
    }

    [Fact]
    public void Confidence_is_full_for_named_markup_and_lower_for_suggestions()
    {
        Assert.Equal(1.0, Page("jsonld-org-basic").Records.Single().Name!.Confidence);
        Assert.Equal(0.9, Assert.Single(Page("links-mailto-plain").Fields).Confidence);
        Assert.Equal(0.7, Assert.Single(Page("address-element-us").Fields).Confidence);
        Assert.Equal(0.45, Assert.Single(Page("links-invalid-email").Fields).Confidence, 3);
    }

    [Fact]
    public void Flat_fields_are_in_document_order_and_ids_match_their_position()
    {
        foreach (string file in ExtractionFixtures.PageFiles())
        {
            ExtractResult r = new ContactExtractor().ExtractHtml(File.ReadAllBytes(file));
            for (int i = 0; i < r.Fields.Count; i++)
            {
                Assert.Equal(i, r.Fields[i].Id);
                if (i > 0 && r.Fields[i].HasSpan && r.Fields[i - 1].HasSpan)
                {
                    Assert.True(r.Fields[i - 1].Span.Start <= r.Fields[i].Span.Start, ExtractionFixtures.NameOf(file));
                }
            }

            foreach (ContactRecord rec in r.Records)
            {
                Assert.All(rec.Fields, f => Assert.Equal(rec.Id, f.RecordId));
            }
        }
    }

    [Fact]
    public void A_document_parsed_by_the_caller_can_be_extracted()
    {
        OneHtml.Document doc = OneHtml.Document.Parse("<div class=\"h-card\"><span class=\"p-name\">Doc Person</span></div>");
        ExtractResult r = new ContactExtractor().Extract(doc);
        Assert.Equal("Doc Person", r.Records.Single().Name!.Text);
    }

    [Fact]
    public void Hostile_nesting_and_huge_repetition_finish()
    {
        string deep = string.Concat(Enumerable.Repeat("<div itemscope itemtype=\"https://schema.org/Organization\"><span itemprop=\"employee\" itemscope itemtype=\"https://schema.org/Person\">", 2000)) + "<span itemprop=\"name\">x</span>";
        ExtractResult r = new ContactExtractor().ExtractHtml(deep);
        Assert.NotNull(r);

        string json = string.Concat(Enumerable.Repeat("[", 5000)) + string.Concat(Enumerable.Repeat("]", 5000));
        ExtractResult bad = new ContactExtractor().ExtractHtml("<script type=\"application/ld+json\">" + json + "</script>");
        Assert.Equal(ExtractIssueKind.MalformedJsonLd, Assert.Single(bad.Issues).Kind);

        // A billion-laughs shape: every node references the next one many times over.
        var sb = new System.Text.StringBuilder("{\"@graph\":[");
        for (int i = 0; i < 30; i++)
        {
            sb.Append("{\"@type\":\"Organization\",\"@id\":\"#n").Append(i).Append("\",\"name\":\"N").Append(i).Append("\",\"contactPoint\":[");
            sb.Append(string.Join(',', Enumerable.Repeat("{\"@id\":\"#n" + (i + 1) + "\"}", 40))).Append("]},");
        }

        sb.Append("{\"@type\":\"Organization\",\"@id\":\"#n30\",\"name\":\"End\"}]}");
        ExtractResult bomb = new ContactExtractor().ExtractHtml("<script type=\"application/ld+json\">" + sb + "</script>");
        Assert.NotNull(bomb);
    }
}

public sealed class JsonSyntaxTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"a\":[1,2.5e3,-0,true,false,null,\"x\\u00e9\\n\"]}")]
    [InlineData("  {\"a\" : {\"b\" : []}}  ")]
    [InlineData("\"just a string\"")]
    public void Valid_json_is_read(string text)
    {
        Assert.NotNull(JsonSyntax.TryParse(text, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1,]")]
    [InlineData("{\"a\":01}")]
    [InlineData("{\"a\":.5}")]
    [InlineData("{\"a\":1.}")]
    [InlineData("{a:1}")]
    [InlineData("{'a':1}")]
    [InlineData("{\"a\":\"line\nbreak\"}")]
    [InlineData("{\"a\":\"\\x\"}")]
    [InlineData("{\"a\":\"\\u12G4\"}")]
    [InlineData("{\"a\":1} extra")]
    [InlineData("{\"a\":1}//c")]
    [InlineData("/*c*/{}")]
    [InlineData("nul")]
    [InlineData("{\"a\":\"unterminated}")]
    public void Anything_outside_the_grammar_is_refused(string text)
    {
        Assert.Null(JsonSyntax.TryParse(text, out _));
    }

    [Fact]
    public void Nesting_beyond_the_limit_is_refused_and_the_limit_itself_is_allowed()
    {
        string ok = string.Concat(Enumerable.Repeat("[", JsonSyntax.MaxDepth)) + string.Concat(Enumerable.Repeat("]", JsonSyntax.MaxDepth));
        string tooDeep = "[" + ok + "]";
        Assert.NotNull(JsonSyntax.TryParse(ok, out _));
        Assert.Null(JsonSyntax.TryParse(tooDeep, out _));
    }

    [Fact]
    public void Strings_know_where_they_are()
    {
        const string text = "{\"name\": \"caf\\u00e9\"}";
        var o = (JsonObject)JsonSyntax.TryParse(text, out _)!;
        var s = (JsonString)o.Members[0].Value;
        Assert.Equal("caf\u00e9", s.Value);
        Assert.Equal("\"caf\\u00e9\"", text[s.Start..s.End]);
    }
}
