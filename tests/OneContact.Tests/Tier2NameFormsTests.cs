using System.Buffers;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Tests;

/// <summary>
/// Name forms of the Iberian, Irish, Maltese, Czech, Slovak and Croatian pages (all inputs written for this repository, synthetic names only). The rules read titles,
/// particles, family-name pairs and the order of the words; they say nothing about gender, origin or salutation.
/// </summary>
public class NameFormsTests
{
    private static string Parts(string input, NameHints hints)
    {
        OperationStatus status = PersonName.TryParse(input, hints, out PersonName p, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"{input}: {d.PrimaryReason}");
        return $"pre={p.Prefix.Text}|giv={p.Given.Text}|mid={p.Middle.Text}|part={p.Particle.Text}|fam={p.Family.Text}|fam2={p.SecondFamily.Text}|prof={p.ProfessionalSuffix.Text}|{p.Order}";
    }

    private static NameHints Country(string code) => NameHints.ForCountry(Region.Parse(code));

    [Theory]
    [InlineData("PT", "Ana Maria dos Santos Ferreira", "pre=|giv=Ana|mid=Maria|part=dos|fam=Santos|fam2=Ferreira|prof=|GivenFamily")]
    [InlineData("PT", "Ana dos Santos Ferreira", "pre=|giv=Ana|mid=|part=dos|fam=Santos|fam2=Ferreira|prof=|GivenFamily")]
    [InlineData("PT", "Joao da Silva", "pre=|giv=Joao|mid=|part=da|fam=Silva|fam2=|prof=|GivenFamily")]
    [InlineData("PT", "Maria dos Santos e Silva", "pre=|giv=Maria|mid=|part=dos|fam=Santos|fam2=Silva|prof=|GivenFamily")]
    [InlineData("ES", "Garcia y Lopez, Maria", "pre=|giv=Maria|mid=|part=|fam=Garcia|fam2=Lopez|prof=|FamilyCommaGiven")]
    [InlineData("ES", "GARCIA Y LOPEZ, MARIA", "pre=|giv=Maria|mid=|part=|fam=Garcia|fam2=Lopez|prof=|FamilyCommaGiven")]
    [InlineData("ES", "Maria de la Cruz Garcia", "pre=|giv=Maria|mid=|part=de la|fam=Cruz|fam2=Garcia|prof=|GivenFamily")]
    [InlineData("ES", "Ana Garcia de la Fuente", "pre=|giv=Ana|mid=|part=|fam=Garcia|fam2=de la Fuente|prof=|GivenFamily")]
    public void IberianFamilyNamesAndParticles(string country, string input, string expected) => Assert.Equal(expected, Parts(input, Country(country)));

    [Theory]
    [InlineData("ES", "Mons. Jose Garcia", "Mons.")]
    [InlineData("IT", "Msgr. Mario Rossi", "Msgr.")]
    [InlineData("PT", "Mestre Manuel Silva", "Mestre")]
    [InlineData("US", "Senator John Smith", "Senator")]
    [InlineData("US", "Sen. John Smith", "Sen.")]
    [InlineData("IE", "Hon. Mary Murphy", "Hon.")]
    [InlineData("IE", "Rev. Patrick Murphy", "Rev.")]
    [InlineData("IE", "Fr. Seamus Murphy", "Fr.")]
    public void TitlesAreTakenAsPrefix(string country, string input, string prefix) => Assert.StartsWith("pre=" + prefix + "|", Parts(input, Country(country)));

    [Theory]
    [InlineData("IE", "Aoife Ní Bhriain", "pre=|giv=Aoife|mid=|part=Ní|fam=Bhriain|fam2=|prof=|GivenFamily")]
    [InlineData("IE", "Sean Ó Briain", "pre=|giv=Sean|mid=|part=Ó|fam=Briain|fam2=|prof=|GivenFamily")]
    [InlineData("IE", "Padraig Mac Giolla", "pre=|giv=Padraig|mid=|part=Mac|fam=Giolla|fam2=|prof=|GivenFamily")]
    [InlineData("IE", "Padraig MacGiolla", "pre=|giv=Padraig|mid=|part=|fam=MacGiolla|fam2=|prof=|GivenFamily")]
    [InlineData("IE", "Sean O'Brien", "pre=|giv=Sean|mid=|part=|fam=O'Brien|fam2=|prof=|GivenFamily")]
    [InlineData("MT", "Josef ta' Cassar", "pre=|giv=Josef|mid=|part=ta'|fam=Cassar|fam2=|prof=|GivenFamily")]
    [InlineData("MT", "Joseph Ta' Pinu", "pre=|giv=Joseph|mid=|part=Ta'|fam=Pinu|fam2=|prof=|GivenFamily")]
    [InlineData("MT", "Maria Il-Borg", "pre=|giv=Maria|mid=|part=|fam=Il-Borg|fam2=|prof=|GivenFamily")]
    public void IrishAndMalteseParticles(string country, string input, string expected) => Assert.Equal(expected, Parts(input, Country(country)));

    // "Ta" and "Il" are ordinary words or names outside Malta.
    [Fact]
    public void MalteseParticlesNeedAMalteseHint() => Assert.Equal("pre=|giv=Josef|mid=ta'|part=|fam=Cassar|fam2=|prof=|GivenFamily", Parts("Josef ta' Cassar", Country("GB")));

    [Theory]
    [InlineData("CZ", "Novák Jan", "pre=|giv=Jan|mid=|part=|fam=Novák|fam2=|prof=|FamilyGiven")]
    [InlineData("SK", "Kováč Peter", "pre=|giv=Peter|mid=|part=|fam=Kováč|fam2=|prof=|FamilyGiven")]
    [InlineData("SK", "Mgr. Kováč Peter", "pre=Mgr.|giv=Peter|mid=|part=|fam=Kováč|fam2=|prof=|FamilyGiven")]
    [InlineData("SK", "Nováková Jana", "pre=|giv=Jana|mid=|part=|fam=Nováková|fam2=|prof=|FamilyGiven")]
    [InlineData("CZ", "NOVÁKOVÁ JANA", "pre=|giv=Jana|mid=|part=|fam=Nováková|fam2=|prof=|FamilyGiven")]
    [InlineData("CZ", "Ing. Dvořák Pavel", "pre=Ing.|giv=Pavel|mid=|part=|fam=Dvořák|fam2=|prof=|FamilyGiven")]
    [InlineData("HR", "Horvat Ivan", "pre=|giv=Ivan|mid=|part=|fam=Horvat|fam2=|prof=|FamilyGiven")]
    [InlineData("HR", "Kovačić Ana, mag. oec.", "pre=mag. oec.|giv=Ana|mid=|part=|fam=Kovačić|fam2=|prof=|FamilyGiven")]
    [InlineData("SK", "Peter Kováč", "pre=|giv=Peter|mid=|part=|fam=Kováč|fam2=|prof=|GivenFamily")]
    [InlineData("SK", "Ing. Jana Nováková", "pre=Ing.|giv=Jana|mid=|part=|fam=Nováková|fam2=|prof=|GivenFamily")]
    [InlineData("HR", "Ivan Horvat", "pre=|giv=Ivan|mid=|part=|fam=Horvat|fam2=|prof=|GivenFamily")]
    // Two listed given names: the words stay in the order written, given first.
    [InlineData("CZ", "Jan Pavel", "pre=|giv=Jan|mid=|part=|fam=Pavel|fam2=|prof=|GivenFamily")]
    public void SurnameFirstOrderOnSlavicPages(string country, string input, string expected) => Assert.Equal(expected, Parts(input, Country(country)));

    [Theory]
    [InlineData("SK", "Ing. Jana Nováková, PhD.", "pre=Ing.|giv=Jana|mid=|part=|fam=Nováková|fam2=|prof=PhD.|GivenFamily")]
    [InlineData("CZ", "Ing. Jan Novák, CSc.", "pre=Ing.|giv=Jan|mid=|part=|fam=Novák|fam2=|prof=CSc.|GivenFamily")]
    [InlineData("CZ", "Mgr. Jana Dvořáková, DiS.", "pre=Mgr.|giv=Jana|mid=|part=|fam=Dvořáková|fam2=|prof=DiS.|GivenFamily")]
    [InlineData("SK", "dipl. ing. Peter Novák", "pre=dipl. ing.|giv=Peter|mid=|part=|fam=Novák|fam2=|prof=|GivenFamily")]
    [InlineData("HR", "prof. dr. sc. Ivan Horvat", "pre=prof. dr. sc.|giv=Ivan|mid=|part=|fam=Horvat|fam2=|prof=|GivenFamily")]
    [InlineData("HR", "dipl. iur. Ana Kovačić", "pre=dipl. iur.|giv=Ana|mid=|part=|fam=Kovačić|fam2=|prof=|GivenFamily")]
    public void SlavicTitlesAndDegrees(string country, string input, string expected) => Assert.Equal(expected, Parts(input, Country(country)));
}
