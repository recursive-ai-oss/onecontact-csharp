using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>Forms the rules read with certainty (and the one they refuse): each case lists the fields the rules must return, as name=text.</summary>
public class AddressRuleGapTests
{
    [Theory]
    [InlineData("12-1216 Johnson St, Coquitlam, BC V3B 4T2", "street.unit-number=12|street.house-number=1216|street.name=Johnson|street.type=St|locality=Coquitlam|admin-area=BC|postal-code=V3B 4T2")]
    [InlineData("172 HEBRIDES LN NEW LONDON PE", "street.house-number=172|street.name=HEBRIDES|street.type=LN|locality=NEW LONDON|admin-area=PE")]
    [InlineData("RR 4 Box 932, Peel NB", "street.route-designator=RR|street.route-number=4|street.box-designator=Box|street.box-number=932|locality=Peel|admin-area=NB")]
    [InlineData("4291, AV CHRISTOPHE-COLOMB, MONTRÉAL, QC", "street.house-number=4291|street.type=AV|street.name=CHRISTOPHE-COLOMB|locality=MONTRÉAL|admin-area=QC")]
    [InlineData("2990 Huckleberry DRIVE, STE. 473 SQUAMISH BC", "street.house-number=2990|street.name=Huckleberry|street.type=DRIVE|street.unit-designator=STE|street.unit-number=473|locality=SQUAMISH|admin-area=BC")]
    [InlineData("21631 Cemetery Road, Unit H Altura, MN 55910", "street.house-number=21631|street.name=Cemetery|street.type=Road|street.unit-designator=Unit|street.unit-number=H|locality=Altura|admin-area=MN|postal-code=55910")]
    [InlineData("Unit 5005 Box 7398 FPO, AP 96677", "street.military-designator=Unit|street.military-number=5005|street.box-designator=Box|street.box-number=7398|locality=FPO|admin-area=AP|postal-code=96677")]
    [InlineData("Post Office Box 247 Winnipeg MB", "street.box-designator=Post Office Box|street.box-number=247|locality=Winnipeg|admin-area=MB")]
    [InlineData("72 Dewey St Richford, Vermont 05476", "street.house-number=72|street.name=Dewey|street.type=St|locality=Richford|admin-area=Vermont|postal-code=05476")]
    [InlineData("100 State St, Apt 230, Portland, ME 04102", "street.house-number=100|street.name=State|street.type=St|street.unit-designator=Apt|street.unit-number=230|locality=Portland|admin-area=ME|postal-code=04102")]
    [InlineData("Raymond Miller 11505 6th Pl NE, Blaine MN 55434", "leading-line=Raymond Miller|street.house-number=11505|street.name=6th|street.type=Pl|street.post-directional=NE|locality=Blaine|admin-area=MN|postal-code=55434")]
    [InlineData("PO BOX 61582, RIVERVIEW NB, E1B 4E2", "street.box-designator=PO BOX|street.box-number=61582|locality=RIVERVIEW|admin-area=NB|postal-code=E1B 4E2")]
    [InlineData("R.R. 5\nWINNIPEG, MB", "street.route-designator=R.R.|street.route-number=5|locality=WINNIPEG|admin-area=MB")]
    [InlineData("4301 S Poppies Avenue, Sioux Falls, SD 57110-3880, U.S.A.", "street.house-number=4301|street.pre-directional=S|street.name=Poppies|street.type=Avenue|locality=Sioux Falls|admin-area=SD|postal-code=57110-3880|country=U.S.A.")]
    [InlineData("29 Deer Park Way, Spruce Grove, Alberta", "street.house-number=29|street.name=Deer Park|street.type=Way|locality=Spruce Grove|admin-area=Alberta")]
    public void RulesReadTheFormWithTheseFields(string input, string expected)
    {
        OperationStatus status = RuleAddressParser.TryParse(input, default, out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"'{input}' was refused: {d.PrimaryReason.Id}");
        string got = string.Join('|', a!.Fields.Select(f => f.Name + "=" + f.Text));
        Assert.Equal(expected, got);
    }

    [Theory]
    [InlineData("1300 Riverside Dr Mount Vernon WA 98273")]
    [InlineData("500 Main St Fort Worth TX 76102")]
    public void AWordThatCouldStartTheCityLeavesTheEndOfTheStreetToTheModel(string input)
    {
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse(input, default, out _, out ParseDetail d));
        Assert.Equal(ReasonCode.AddressModelRequired, d.PrimaryReason);
    }

    [Fact]
    public void PubTwoEightWordsInRoadNamesStillHoldWhenTheyAreNames()
    {
        // COUNTY ROAD and STATE HIGHWAY are names (Publication 28 section 237); STATE ST is a street.
        ParsedAddress road = Read("1000 County Road 12, Fairview TX 75069");
        Assert.Contains(road.Fields, f => f.Name == "street.name" && f.Text == "County Road 12");
        ParsedAddress street = Read("1 State St, Albany NY 12207");
        Assert.Contains(street.Fields, f => f.Name == "street.type" && f.Text == "St");
    }

    private static ParsedAddress Read(string text)
    {
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse(text, default, out ParsedAddress? a, out _));
        return a!;
    }
}
