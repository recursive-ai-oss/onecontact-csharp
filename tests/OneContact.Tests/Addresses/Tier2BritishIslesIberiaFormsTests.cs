using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// The forms people write British, Irish, Iberian, Italian, Maltese and small-state addresses in on the web (all inputs written for this repository), checked against
/// the posts' guides: Royal Mail PAF Programmers' Guide (county optional, sub-building before the building name), Jersey Post and Guernsey Post, the UPU Ireland sheet,
/// CTT Manual de Endereçamento (the number marker is unnecessary), Correos, Poste Italiane and the UPU Malta, San Marino and Andorra sheets.
/// </summary>
public class Tier2BritishIslesIberiaFormsTests
{
    private static ParsedAddress Read(string text, string? hint)
    {
        OperationStatus status = RuleAddressParser.TryParse(text, hint is null ? Region.Unknown : Region.Parse(hint), out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"{text}: {d.PrimaryReason}");
        foreach (ParsedAddressField f in a!.Fields)
        {
            Assert.Equal(f.Text, text.Substring(f.Span.Start, f.Span.Length));
        }

        return a;
    }

    private static string Fields(ParsedAddress a) => string.Join(" ; ", a.Fields.Select(f => f.Name + "=" + f.Text));

    // Britain: the county on its own line, with "Co.", or with "County"; a glued postcode; the post town on the postcode's line.
    [Theory]
    [InlineData("Rose Cottage\n12 High Street\nGuildford\nSurrey GU1 3AA", "leading-line=Rose Cottage ; street.house-number=12 ; street.name=High ; street.type=Street ; locality=Guildford ; admin-area=Surrey ; postal-code=GU1 3AA")]
    [InlineData("Rose Cottage, 12 High Street, Guildford, Surrey GU1 3AA", "leading-line=Rose Cottage ; street.house-number=12 ; street.name=High ; street.type=Street ; locality=Guildford ; admin-area=Surrey ; postal-code=GU1 3AA")]
    [InlineData("Flat 3, 25 Park Lane, London W1K 1PN", "street.unit-designator=Flat ; street.unit-number=3 ; street.house-number=25 ; street.name=Park ; street.type=Lane ; locality=London ; postal-code=W1K 1PN")]
    [InlineData("Unit 4, Brunel Business Park, Station Road, Bristol BS1 4DJ", "street.unit-designator=Unit ; street.unit-number=4 ; leading-line=Brunel Business Park ; street.name=Station ; street.type=Road ; locality=Bristol ; postal-code=BS1 4DJ")]
    [InlineData("3 Old Road, Durham, Co. Durham DH1 3LY", "street.house-number=3 ; street.name=Old ; street.type=Road ; locality=Durham ; admin-area=Co. Durham ; postal-code=DH1 3LY")]
    [InlineData("3 Old Road, Newcastle upon Tyne, Tyne and Wear NE1 4ST", "street.house-number=3 ; street.name=Old ; street.type=Road ; locality=Newcastle upon Tyne ; admin-area=Tyne and Wear ; postal-code=NE1 4ST")]
    [InlineData("12 Castle Street, Belfast, Northern Ireland, BT1 1GH", "street.house-number=12 ; street.name=Castle ; street.type=Street ; locality=Belfast ; admin-area=Northern Ireland ; postal-code=BT1 1GH")]
    [InlineData("12 Castle Street, Belfast BT1 1GH, United Kingdom", "street.house-number=12 ; street.name=Castle ; street.type=Street ; locality=Belfast ; postal-code=BT1 1GH ; country=United Kingdom")]
    [InlineData("14 Mill Lane Henley-on-Thames RG9 2AA", "street.house-number=14 ; street.name=Mill ; street.type=Lane ; locality=Henley-on-Thames ; postal-code=RG9 2AA")]
    [InlineData("PO Box 123, Leeds LS1 1AA", "street.box-designator=PO Box ; street.box-number=123 ; locality=Leeds ; postal-code=LS1 1AA")]
    public void BritishForms(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "GB")));

    [Fact]
    public void GluedBritishPostcodeIsSplitInTheStandardForm()
    {
        ParsedAddress a = Read("12 High Street, Canterbury, CT161QD", "GB");
        Assert.Equal("CT16 1QD", a.PostalCode!.Value.Standard);
    }

    // Jersey, Guernsey and the Isle of Man: the island's name is the country and the parish or town above it is the locality (Jersey Post, Guernsey Post, UPU sheets).
    [Theory]
    [InlineData("JE", "12 Beresford Street, St Helier, Jersey, JE2 4WN", "street.house-number=12 ; street.name=Beresford ; street.type=Street ; locality=St Helier ; country=Jersey ; postal-code=JE2 4WN")]
    [InlineData("JE", "Maison Bel Air, Rue de la Vallee, St Mary, Jersey JE3 3DL", "leading-line=Maison Bel Air ; street.type=Rue ; street.name=de la Vallee ; locality=St Mary ; country=Jersey ; postal-code=JE3 3DL")]
    [InlineData("GG", "Le Mont, Route de Carteret, Castel, Guernsey, GY5 7AA", "leading-line=Le Mont ; street.type=Route ; street.name=de Carteret ; locality=Castel ; country=Guernsey ; postal-code=GY5 7AA")]
    [InlineData("IM", "12 Strand Street, Douglas, Isle of Man, IM1 2PX", "street.house-number=12 ; street.name=Strand ; street.type=Street ; locality=Douglas ; country=Isle of Man ; postal-code=IM1 2PX")]
    [InlineData("IM", "12 Athol Street, Douglas IM1 1AB", "street.house-number=12 ; street.name=Athol ; street.type=Street ; locality=Douglas ; postal-code=IM1 1AB")]
    public void CrownDependencyForms(string hint, string input, string fields) => Assert.Equal(fields, Fields(Read(input, hint)));

    // Ireland: the county with "Co." or "County" or bare, Dublin postal districts, an Eircode alone, and the last line without a postcode (hint only).
    [Theory]
    [InlineData("12 Main Street, Dundalk, Co. Louth, A91 X2Y3", "street.house-number=12 ; street.name=Main ; street.type=Street ; locality=Dundalk ; admin-area=Co. Louth ; postal-code=A91 X2Y3")]
    [InlineData("12 Main Street, Dundalk, Co.Louth, A91 X2Y3", "street.house-number=12 ; street.name=Main ; street.type=Street ; locality=Dundalk ; admin-area=Co.Louth ; postal-code=A91 X2Y3")]
    [InlineData("12 Main Street, Dundalk, County Louth", "street.house-number=12 ; street.name=Main ; street.type=Street ; locality=Dundalk ; admin-area=County Louth")]
    [InlineData("12 Main Street, Dundalk, Louth", "street.house-number=12 ; street.name=Main ; street.type=Street ; locality=Dundalk ; admin-area=Louth")]
    [InlineData("12 Main Street, Dublin 8, D08 X2Y3", "street.house-number=12 ; street.name=Main ; street.type=Street ; locality=Dublin 8 ; postal-code=D08 X2Y3")]
    [InlineData("12 Main Street, D08X2Y3", "street.house-number=12 ; street.name=Main ; street.type=Street ; postal-code=D08X2Y3")]
    [InlineData("14 Oakwood Estate, Ballincollig, Cork, P31 XY12", "street.house-number=14 ; street.name=Oakwood ; street.type=Estate ; locality=Ballincollig ; admin-area=Cork ; postal-code=P31 XY12")]
    [InlineData("38 Castle Street\nTuam", "street.house-number=38 ; street.name=Castle ; street.type=Street ; locality=Tuam")]
    [InlineData("Main Street, Ardara, Donegal F94 AB12, Ireland", "street.name=Main ; street.type=Street ; locality=Ardara ; admin-area=Donegal ; postal-code=F94 AB12 ; country=Ireland")]
    public void IrishForms(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "IE")));

    // Malta: the number first with a comma, a building name above the street, and units above (UPU Malta sheet; the postcode is three letters and four digits).
    [Theory]
    [InlineData("29, Triq il-Kbira, Valletta VLT 1234", "street.house-number=29 ; street.type=Triq ; street.name=il-Kbira ; locality=Valletta ; postal-code=VLT 1234")]
    [InlineData("Villa Rosa, 29, Triq il-Kbira, Sliema SLM 1234", "leading-line=Villa Rosa ; street.house-number=29 ; street.type=Triq ; street.name=il-Kbira ; locality=Sliema ; postal-code=SLM 1234")]
    [InlineData("Flat 3, Block B, Triq il-Kbira, Sliema SLM 1234", "street.unit-designator=Flat ; street.unit-number=3 ; street.unit-designator=Block ; street.unit-number=B ; street.type=Triq ; street.name=il-Kbira ; locality=Sliema ; postal-code=SLM 1234")]
    [InlineData("Triq San Pawl, 29, Mosta MST 1234, Malta", "street.type=Triq ; street.name=San Pawl ; street.house-number=29 ; locality=Mosta ; postal-code=MST 1234 ; country=Malta")]
    public void MalteseForms(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "MT")));

    // Spain: the number marker, glued type and name, the province in parentheses, the floor and door, polígono and nave, Basque and Galician street words (Correos, UPU Spain sheet).
    [Theory]
    [InlineData("CL. MAYOR,47 1ºB, 28013 Madrid", "street.type=CL ; street.name=MAYOR ; street.house-number=47 ; street.unit-number=1ºB ; postal-code=28013 ; locality=Madrid")]
    [InlineData("Calle Mayor, nº 5, 28013 Madrid", "street.type=Calle ; street.name=Mayor ; street.house-number=5 ; postal-code=28013 ; locality=Madrid")]
    [InlineData("Calle Mayor, nº5, 28013 Madrid", "street.type=Calle ; street.name=Mayor ; street.house-number=5 ; postal-code=28013 ; locality=Madrid")]
    [InlineData("CL.MAYOR 47, 28013 Madrid", "street.type=CL ; street.name=MAYOR ; street.house-number=47 ; postal-code=28013 ; locality=Madrid")]
    [InlineData("Calle Mayor 5\n28013 Madrid (Madrid)\nEspaña", "street.type=Calle ; street.name=Mayor ; street.house-number=5 ; postal-code=28013 ; locality=Madrid ; admin-area=Madrid ; country=España")]
    [InlineData("Avenida de América 12, 3º C, 28028 Madrid", "street.type=Avenida ; street.name=de América ; street.house-number=12 ; street.unit-number=3º C ; postal-code=28028 ; locality=Madrid")]
    [InlineData("Avda. Diagonal, 100, 4º 2ª, 08019 Barcelona", "street.type=Avda ; street.name=Diagonal ; street.house-number=100 ; street.unit-number=4º 2ª ; postal-code=08019 ; locality=Barcelona")]
    [InlineData("Carrer de Balmes, 120, 3r 2a, 08008 Barcelona", "street.type=Carrer ; street.name=de Balmes ; street.house-number=120 ; street.unit-number=3r 2a ; postal-code=08008 ; locality=Barcelona")]
    [InlineData("Ctra. de Valencia km 5, 28031 Madrid", "street.type=Ctra ; street.name=de Valencia ; street.house-number=km 5 ; postal-code=28031 ; locality=Madrid")]
    [InlineData("Plaza Mayor s/n 28012 Madrid", "street.type=Plaza ; street.name=Mayor ; street.house-number=s/n ; postal-code=28012 ; locality=Madrid")]
    [InlineData("Polígono Las Salinas, Parcela 3, 28850 Torrejón de Ardoz (Madrid)", "street.type=Polígono ; street.name=Las Salinas ; street.unit-designator=Parcela ; street.unit-number=3 ; postal-code=28850 ; locality=Torrejón de Ardoz ; admin-area=Madrid")]
    [InlineData("Kalea Nagusia 5, 48001 Bilbao, Bizkaia", "street.type=Kalea ; street.name=Nagusia ; street.house-number=5 ; postal-code=48001 ; locality=Bilbao ; admin-area=Bizkaia")]
    [InlineData("Nagusia Kalea 5, 48001 Bilbao", "street.name=Nagusia ; street.type=Kalea ; street.house-number=5 ; postal-code=48001 ; locality=Bilbao")]
    [InlineData("Rúa do Franco 5, 15705 Santiago de Compostela, A Coruña", "street.type=Rúa ; street.name=do Franco ; street.house-number=5 ; postal-code=15705 ; locality=Santiago de Compostela ; admin-area=A Coruña")]
    public void SpanishForms(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "ES")));

    // Portugal: n.º markers are dropped, lote and loja are units, a zona industrial is a type (CTT Manual de Endereçamento p. 7).
    [Theory]
    [InlineData("Rua das Flores, nº 25, 1200-195 Lisboa", "street.type=Rua ; street.name=das Flores ; street.house-number=25 ; postal-code=1200-195 ; locality=Lisboa")]
    [InlineData("Rua das Flores n.º 25, 1200-195 Lisboa", "street.type=Rua ; street.name=das Flores ; street.house-number=25 ; postal-code=1200-195 ; locality=Lisboa")]
    [InlineData("Rua das Flores, 25, 3º Dto., 1200-195 Lisboa", "street.type=Rua ; street.name=das Flores ; street.house-number=25 ; street.unit-number=3º Dto. ; postal-code=1200-195 ; locality=Lisboa")]
    [InlineData("Rua das Flores, Lote 5, 1200-195 Lisboa", "street.type=Rua ; street.name=das Flores ; street.unit-designator=Lote ; street.unit-number=5 ; postal-code=1200-195 ; locality=Lisboa")]
    [InlineData("Praceta dos Cravos, Lote 3, Loja A, 2735-123 Cacém", "street.type=Praceta ; street.name=dos Cravos ; street.unit-designator=Lote ; street.unit-number=3 ; street.unit-designator=Loja ; street.unit-number=A ; postal-code=2735-123 ; locality=Cacém")]
    [InlineData("Zona Industrial de Cabanas, Lote 12, 2840-123 Seixal", "street.type=Zona Industrial ; street.name=de Cabanas ; street.unit-designator=Lote ; street.unit-number=12 ; postal-code=2840-123 ; locality=Seixal")]
    public void PortugueseForms(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "PT")));

    // Italy: the province after the locality in any of its writings, additions to the civic number, units, and no postcode when the province code is there (Poste Italiane).
    [Theory]
    [InlineData("Via Roma 12 bis, 00184 Roma RM", "street.type=Via ; street.name=Roma ; street.house-number=12 bis ; postal-code=00184 ; locality=Roma ; admin-area=RM")]
    [InlineData("Via Roma, 12/a, 00184 Roma (RM)", "street.type=Via ; street.name=Roma ; street.house-number=12/a ; postal-code=00184 ; locality=Roma ; admin-area=RM")]
    [InlineData("Via Toledo 100, 80134 Napoli, NA", "street.type=Via ; street.name=Toledo ; street.house-number=100 ; postal-code=80134 ; locality=Napoli ; admin-area=NA")]
    [InlineData("Via Toledo 100 - 80134 Napoli NA", "street.type=Via ; street.name=Toledo ; street.house-number=100 ; postal-code=80134 ; locality=Napoli ; admin-area=NA")]
    [InlineData("Via Toledo 100, Napoli NA", "street.type=Via ; street.name=Toledo ; street.house-number=100 ; locality=Napoli ; admin-area=NA")]
    [InlineData("Via Roma 12, 00184 Roma (Italy)", "street.type=Via ; street.name=Roma ; street.house-number=12 ; postal-code=00184 ; locality=Roma ; country=Italy")]
    [InlineData("Piazza del Duomo, snc, 20122 Milano MI", "street.type=Piazza ; street.name=del Duomo ; street.other=snc ; postal-code=20122 ; locality=Milano ; admin-area=MI")]
    public void ItalianForms(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "IT")));

    // San Marino and Andorra: the country written as a code or a long name after the locality, and the postcode with a dash.
    [Theory]
    [InlineData("SM", "Via del Voltone 12, 47890 San Marino (RSM)", "street.type=Via ; street.name=del Voltone ; street.house-number=12 ; postal-code=47890 ; locality=San Marino ; country=RSM")]
    [InlineData("SM", "Via del Voltone 12, 47893 Borgo Maggiore, Repubblica di San Marino", "street.type=Via ; street.name=del Voltone ; street.house-number=12 ; postal-code=47893 ; locality=Borgo Maggiore ; country=Repubblica di San Marino")]
    [InlineData("AD", "Av. Meritxell 12, AD-500 Andorra la Vella", "street.type=Av ; street.name=Meritxell ; street.house-number=12 ; postal-code=AD-500 ; locality=Andorra la Vella")]
    [InlineData("AD", "Av. Meritxell 12, AD500 Andorra la Vella (AD)", "street.type=Av ; street.name=Meritxell ; street.house-number=12 ; postal-code=AD500 ; locality=Andorra la Vella ; country=AD")]
    [InlineData("AD", "Carrer Major 5 baixos, AD500 Andorra la Vella", "street.type=Carrer ; street.name=Major ; street.house-number=5 ; street.unit-number=baixos ; postal-code=AD500 ; locality=Andorra la Vella")]
    public void SmallStateForms(string hint, string input, string fields) => Assert.Equal(fields, Fields(Read(input, hint)));

    // Without a hint a San Marino postcode and the island country names decide by themselves; a shared Italian form is refused rather than guessed.
    [Fact]
    public void CountryFromTheContentAlone()
    {
        Assert.Equal(Region.Parse("SM"), Read("Via del Voltone 12, 47890 San Marino (RSM)", null).Country);
        Assert.Equal(Region.Parse("IT"), Read("Via Toledo 100, 80134 Napoli, NA", null).Country);
        Assert.NotEqual(OperationStatus.Done, RuleAddressParser.TryParse("Via Roma 12, 00184 Roma", Region.Unknown, out _, out _));
    }
}
