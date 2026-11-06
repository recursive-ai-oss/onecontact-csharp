using System.Buffers;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Superset;
using RecursiveAI.OneContact.Names;

// Loads the Tier 1 + Tier 2 model from its embedded weights, with no call from this program but the parsers', and uses it. Exits non-zero on the first failure.
int failures = 0;

void Check(bool condition, string what)
{
    if (!condition)
    {
        failures++;
        Console.Error.WriteLine("FAILED: " + what);
    }
}

Check(ContactModels.PackageId == "RecursiveAI.OneContact.Models", "the package registered itself");

// The rules cannot tell where this street ends; the model labels the words.
OperationStatus s = RuleAddressParser.TryParse("100 broadway new york ny 10001", default, out ParsedAddress? a, out ParseDetail d);
Check(s == OperationStatus.Done && a is not null, "a run-on address is read through the model");
Check(a?.PostalCode?.Standard == "10001", "postal code");
Check(d.Fields.Any(f => f.Source == DecisionSource.Model), "fields say a model decided them");

// A Tier 2 address the rules refuse is read through the model, with the country from the hint.
OperationStatus t = RuleAddressParser.TryParse("58 Hordley Avenue
Shrewsbury
Shropshire
SY4 1AA", Region.Parse("GB"), out ParsedAddress? gb, out ParseDetail gd);
Check(t == OperationStatus.Done && gb is not null && gb.Country == Region.Parse("GB"), "a Tier 2 address is read through the model");
Check(gd.Fields.Any(f => f.Source == DecisionSource.Model), "Tier 2 fields say a model decided them");

// A rule hit is kept as it is.
OperationStatus r = RuleAddressParser.TryParse("100 Broadway, New York, NY 10001", default, out ParsedAddress? ruled, out ParseDetail rd);
Check(r == OperationStatus.Done && ruled is not null && rd.Fields.All(f => f.Source == DecisionSource.Rule || f.Name is "recipient" or "organization"), "rule fields stay rule fields");

NameModelResult n = ContactModels.Name!.Analyze("Northwind Traders LLC");
Check(n.Class == NameClass.Company, "name class");

// Plain-text extraction uses the span head the package registers; a rule's field is still a rule's.
Check(ContactModels.Spans is not null, "the span model is registered by the package");
ExtractResult x = new ContactExtractor { Sources = ExtractionSources.Text, DefaultRegion = Region.US }.ExtractText("Best regards\nJohn Carter\nAcme Plumbing LLC\nTel: (217) 555-0143\njcarter@acmeplumbing.example");
Check(x.Of(FieldKind.Email).All(f => f.Decision == DecisionSource.Rule), "the e-mail address is a rule's");
Check(x.Of(FieldKind.Phone).Count == 1, "the phone number is found");
Check(x.Of(FieldKind.Email).Count == 1, "the e-mail address is found");

// The head is also reachable directly.
Check(SupersetModel.Instance.Find("John Carter\njcarter@acmeplumbing.example").Spans.Count > 0, "the span head runs");

Console.WriteLine(failures == 0 ? "ok" : "failed");
return failures == 0 ? 0 : 1;
