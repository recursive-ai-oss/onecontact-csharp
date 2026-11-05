using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact.Tests;

// Expected values were written by hand and checked against a released libphonenumber with the harness in tools/Oracles/Phones
// (as-you-type, matcher and short-number cases); that harness is what covers every region. These tests keep the behaviours that
// were once wrong, or that are easy to get wrong, from regressing.
public class PhoneTypingMatchingShortTests
{
    private static readonly Region GB = Region.Parse("GB");
    private static readonly Region BR = Region.Parse("BR");
    private static readonly Region CL = Region.Parse("CL");
    private static readonly Region AE = Region.Parse("AE");
    private static readonly Region MG = Region.Parse("MG");
    private static readonly Region DE = Region.Parse("DE");

    private static string[] Type(Region region, string keys)
    {
        AsYouTypeFormatter f = new(region);
        return [.. keys.Select(f.InputDigit)];
    }

    [Fact]
    public void Typing_a_national_US_number_uses_the_seven_digit_grouping_until_the_area_code_is_certain()
    {
        // The 7-digit local format fits until the eighth digit, then the full format takes over.
        Assert.Equal(
            ["2", "20", "202", "202-4", "202-45", "202-456", "202-4561", "(202) 456-11", "(202) 456-111", "(202) 456-1111"],
            Type(Region.US, "2024561111"));
    }

    [Fact]
    public void Typing_the_national_prefix_one_keeps_the_area_code_in_brackets()
    {
        string[] steps = Type(Region.US, "12024561111");
        Assert.Equal(["1", "12", "1 20", "1 (202", "1 (202) 4"], steps[..5]);
        Assert.Equal("1 (202) 456-1111", steps[^1]);
    }

    [Fact]
    public void Typing_an_international_number_groups_after_the_country_code()
    {
        Assert.Equal("+1 202-456-1111", Type(Region.US, "+12024561111")[^1]);
        string[] gb = Type(GB, "+442079460958");
        Assert.Equal(["+", "+4", "+44 ", "+44 2", "+44 20", "+44 20 7"], gb[..6]);
        Assert.Equal("+44 20 7946 0958", gb[^1]);
    }

    [Fact]
    public void Typing_the_trunk_zero_keeps_it_in_front_of_the_groups()
    {
        string[] gb = Type(GB, "02079460958");
        Assert.Equal(["0", "02", "020", "020 7"], gb[..4]);
        Assert.Equal("020 7946 0958", gb[^1]);
    }

    [Fact]
    public void A_format_that_has_no_national_prefix_rule_is_skipped_once_the_prefix_is_typed()
    {
        // United Arab Emirates: the toll-free grouping has no prefix rule, so "0800..." stays as typed while "800..." is grouped.
        Assert.Equal("0800123456", Type(AE, "0800123456")[^1]);
        Assert.Equal("800 123456", Type(AE, "800123456")[^1]);
        // ... while a format that names the prefix in its carrier-code rule is used with it (Bolivia).
        Assert.Equal("02 2123456", Type(Region.Parse("BO"), "022123456")[^1]);
    }

    [Fact]
    public void A_prefix_found_late_is_taken_out_and_formatting_restarts()
    {
        // Madagascar: seven digits that match the national prefix pattern are set aside once typing goes on past them.
        string[] steps = Type(MG, "2612168314930");
        Assert.Equal("2612168 ", steps[6]);
        Assert.Equal("2612168 31 49 30", steps[^1]);
    }

    [Fact]
    public void Typing_anything_but_digits_and_a_leading_plus_echoes_the_input()
    {
        string[] steps = Type(Region.US, "202-456-1111");
        Assert.Equal(["2", "20", "202", "202-", "202-4"], steps[..5]);
        Assert.Equal("202-456-1111", steps[^1]);
        Assert.Equal("1a", Type(Region.US, "1a2")[1]);
    }

    [Fact]
    public void Unknown_regions_format_nothing_without_a_plus()
    {
        Assert.Equal("2024561111", Type(Region.Unknown, "2024561111")[^1]);
    }

    [Fact]
    public void The_remembered_position_follows_the_digit_through_the_formatting()
    {
        AsYouTypeFormatter f = new(Region.US);
        foreach (char c in "2024")
        {
            f.InputDigit(c);
        }

        string output = f.InputDigitAndRememberPosition('5');
        Assert.Equal("202-45", output);
        Assert.Equal(6, f.RememberedPosition);
        foreach (char c in "61111")
        {
            output = f.InputDigit(c);
        }

        // The remembered digit is the fifth one; in the final text it ends at index 8.
        Assert.Equal("(202) 456-1111", output);
        Assert.Equal(8, f.RememberedPosition);
    }

    [Fact]
    public void Clear_starts_a_new_number()
    {
        AsYouTypeFormatter f = new(Region.US);
        foreach (char c in "+4420")
        {
            f.InputDigit(c);
        }

        f.Clear();
        Assert.Equal("2", f.InputDigit('2'));
        Assert.Equal("20", f.InputDigit('0'));
        Assert.Equal("202", f.InputDigit('2'));
        Assert.Equal("202-4", f.InputDigit('4'));
    }

    [Theory]
    [InlineData("tel:+12025550100;ext=204", false)]
    [InlineData("+12025550100 ext. 204", false)]
    [InlineData("202-555-0100 x204", false)]
    [InlineData("1-800-FLOWERS ext 5", true)]
    [InlineData("tel:+1-800-FLOWERS;ext=204", true)]
    public void Extension_labels_do_not_make_a_vanity_number(string text, bool vanity)
    {
        PhoneNumber n = PhoneNumber.Parse(text, Region.US);
        Assert.Equal(vanity, n.Flags.HasFlag(PhoneFacts.Vanity));
        if (!vanity)
        {
            Assert.NotNull(n.Extension);
        }
    }

    private static PhoneNumberMatch[] Find(string text, Region region, PhoneMatchLeniency leniency) =>
        [.. PhoneNumber.FindAll(text, region, leniency)];

    [Fact]
    public void Matches_carry_offsets_raw_text_and_the_parsed_number()
    {
        PhoneNumberMatch[] found = Find("Call 202-456-1111 or +44 20 7946 0958 today", Region.US, PhoneMatchLeniency.Valid);
        Assert.Equal(2, found.Length);
        Assert.Equal((5, 17, "202-456-1111"), (found[0].Start, found[0].End, found[0].RawString));
        Assert.Equal("+12024561111", found[0].Number.E164);
        Assert.Equal((21, 37, "+44 20 7946 0958"), (found[1].Start, found[1].End, found[1].RawString));
        Assert.Equal(44, found[1].Number.CountryCode);
    }

    [Fact]
    public void Dates_and_digits_glued_to_letters_are_not_numbers()
    {
        Assert.Empty(Find("on 12/03/2021 at 10:30", Region.US, PhoneMatchLeniency.Possible));
        Assert.Equal(2, Find("abc2024561111 and 2024561111def", Region.US, PhoneMatchLeniency.Possible).Length);
        Assert.Empty(Find("abc2024561111 and 2024561111def", Region.US, PhoneMatchLeniency.Valid));
        Assert.Empty(Find("$2024561111 and 2024561111%", Region.US, PhoneMatchLeniency.Valid));
    }

    [Fact]
    public void Grouping_levels_demand_the_regions_groups()
    {
        Assert.Single(Find("ring 202 45611 11 now", Region.US, PhoneMatchLeniency.Valid));
        Assert.Empty(Find("ring 202 45611 11 now", Region.US, PhoneMatchLeniency.StrictGrouping));
        Assert.Single(Find("ring 2024 561 111 now", Region.US, PhoneMatchLeniency.Valid));
        Assert.Empty(Find("ring 2024 561 111 now", Region.US, PhoneMatchLeniency.StrictGrouping));
        Assert.Single(Find("ring 202 456 1111 now", Region.US, PhoneMatchLeniency.ExactGrouping));
    }

    [Fact]
    public void An_alternate_grouping_the_data_lists_passes_the_strict_levels_only_when_it_is_whole()
    {
        // Serbia lists a 2-3-3 alternate that leaves the last digit of a nine-digit mobile number over: the groups 60, 123 and 4567
        // are exactly present, whatever separates them.
        Region rs = Region.Parse("RS");
        Assert.Single(Find("at +381 60.123.4567", rs, PhoneMatchLeniency.ExactGrouping));
        // Cambodia's alternate pattern covers the first nine of ten digits and leaves the last as it is.
        Assert.Single(Find("(18-0012-3456", Region.Parse("KH"), PhoneMatchLeniency.StrictGrouping));
    }

    [Fact]
    public void Without_a_region_only_numbers_with_a_country_code_are_found()
    {
        PhoneNumberMatch[] found = Find("202-456-1111 +1 202-456-1111", Region.Unknown, PhoneMatchLeniency.Valid);
        Assert.Equal([(13, 28)], found.Select(m => (m.Start, m.End)));
    }

    [Fact]
    public void Extensions_belong_to_the_match()
    {
        PhoneNumberMatch[] found = Find("202-456-1111 ext. 55 and 202-456-1111x77", Region.US, PhoneMatchLeniency.Valid);
        Assert.Equal(["55", "77"], found.Select(m => m.Number.Extension));
        Assert.Equal("202-456-1111 ext. 55", found[0].RawString);
    }

    [Fact]
    public void Max_tries_bounds_the_candidates_examined()
    {
        const string Text = "2012-12-22 20:12:30 and 650-253-0000";
        Assert.Single(Find(Text, Region.US, PhoneMatchLeniency.Valid));
        Assert.Empty(PhoneNumberMatcher.Default.FindAll(Text, Region.US, PhoneMatchLeniency.Valid, maxTries: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneNumberMatcher.Default.FindAll(Text, Region.US, PhoneMatchLeniency.Valid, maxTries: -1));
    }

    [Fact]
    public void The_low_level_search_continues_where_it_stopped()
    {
        const string Text = "2024561111 2024561112";
        int index = 0;
        long tries = long.MaxValue;
        Assert.True(PhoneNumberMatcher.Default.TryFindNext(Text, Region.US, PhoneMatchLeniency.Valid, ref index, ref tries, out PhoneNumberMatch first));
        Assert.Equal(10, index);
        Assert.True(PhoneNumberMatcher.Default.TryFindNext(Text, Region.US, PhoneMatchLeniency.Valid, ref index, ref tries, out PhoneNumberMatch second));
        Assert.Equal((11, "2024561112"), (second.Start, second.RawString));
        Assert.Equal("2024561111", first.RawString);
        Assert.False(PhoneNumberMatcher.Default.TryFindNext(Text, Region.US, PhoneMatchLeniency.Valid, ref index, ref tries, out _));
    }

    [Fact]
    public void Numbers_with_a_trunk_zero_and_a_slash_are_found()
    {
        Assert.Equal(2, Find("030 123456 / 0171 1234567", DE, PhoneMatchLeniency.ExactGrouping).Length);
        Assert.Single(Find("0 11 91234-5678", BR, PhoneMatchLeniency.ExactGrouping));
    }

    [Fact]
    public void Hostile_text_neither_hangs_nor_throws()
    {
        string text = new string('1', 5000) + new string('(', 3000) + new string('-', 3000) + "/" + new string('9', 400);
        foreach (PhoneMatchLeniency level in Enum.GetValues<PhoneMatchLeniency>())
        {
            _ = Find(text, Region.US, level);
        }
    }

    private static PhoneNumber Short(Region region, string digits) =>
        PhoneNumber.Parse("+" + PhoneMetadata.Default.GetCallingCode(region) + digits);

    [Theory]
    [InlineData("US", "911", true, ShortNumberCost.TollFree)]
    [InlineData("US", "9115", true, ShortNumberCost.Unknown)]
    [InlineData("US", "611", true, ShortNumberCost.TollFree)]
    [InlineData("GB", "999", true, ShortNumberCost.TollFree)]
    [InlineData("GB", "118118", true, ShortNumberCost.Unknown)]
    [InlineData("GB", "116000", true, ShortNumberCost.TollFree)]
    [InlineData("BR", "190", true, ShortNumberCost.TollFree)]
    [InlineData("BR", "1901", false, ShortNumberCost.Unknown)]
    [InlineData("FR", "15", true, ShortNumberCost.TollFree)]
    [InlineData("DE", "110", true, ShortNumberCost.TollFree)]
    public void Short_numbers_have_a_validity_and_a_cost(string region, string digits, bool valid, ShortNumberCost cost)
    {
        Region r = Region.Parse(region);
        PhoneNumber n = Short(r, digits);
        ShortNumberInfo info = ShortNumberInfo.Default;
        Assert.Equal(valid, info.IsValidForRegion(n, r));
        Assert.Equal(valid, info.IsValid(n));
        Assert.Equal(cost, info.GetExpectedCostForRegion(n, r));
    }

    [Fact]
    public void The_cost_over_all_regions_of_a_calling_code_is_the_most_expensive_answer()
    {
        // 911 is toll free in the US, but the other regions that share the calling code do not list it, so their answer is unknown.
        Assert.Equal(ShortNumberCost.Unknown, ShortNumberInfo.Default.GetExpectedCost(Short(Region.US, "911")));
        // Germany is the only region of its calling code.
        Assert.Equal(ShortNumberCost.TollFree, ShortNumberInfo.Default.GetExpectedCost(Short(DE, "110")));
    }

    [Fact]
    public void Short_numbers_of_another_calling_code_are_not_possible_here()
    {
        PhoneNumber nine = Short(Region.US, "911");
        Assert.False(ShortNumberInfo.Default.IsPossibleForRegion(nine, GB));
        Assert.False(ShortNumberInfo.Default.IsValidForRegion(nine, GB));
        Assert.True(ShortNumberInfo.Default.IsPossible(nine));
    }

    [Fact]
    public void Emergency_numbers_may_have_trailing_digits_except_where_that_dials_something_else()
    {
        ShortNumberInfo info = ShortNumberInfo.Default;
        Assert.True(info.IsEmergencyNumber("911", Region.US));
        Assert.True(info.ConnectsToEmergencyNumber("9115", Region.US));
        Assert.False(info.IsEmergencyNumber("9115", Region.US));
        Assert.True(info.ConnectsToEmergencyNumber("190", BR) && info.IsEmergencyNumber("190", BR));
        Assert.False(info.ConnectsToEmergencyNumber("1901", BR));
        Assert.True(info.ConnectsToEmergencyNumber("133", CL));
        Assert.False(info.ConnectsToEmergencyNumber("1330", CL));
        Assert.True(info.IsEmergencyNumber("999 ", GB));
        Assert.False(info.IsEmergencyNumber("+999", GB));
    }

    [Fact]
    public void Examples_come_from_the_data()
    {
        ShortNumberInfo info = ShortNumberInfo.Default;
        Assert.NotEmpty(info.GetExample(Region.US));
        Assert.NotEmpty(info.GetExample(GB, ShortNumberCost.TollFree));
        Assert.Equal("", info.GetExample(GB, ShortNumberCost.Unknown));
        Assert.Equal("", info.GetExample(Region.Unknown));
        Assert.Contains(Region.US, info.SupportedRegions);
        Assert.True(info.IsValidForRegion(Short(Region.US, info.GetExample(Region.US)), Region.US));
    }
}
