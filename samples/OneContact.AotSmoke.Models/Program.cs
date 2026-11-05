using System.Buffers;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Names;

// Loads the Tier 1 model from its embedded weights, with no call from this program but the parsers', and uses it. Exits non-zero on the first failure.
int failures = 0;

void Check(bool condition, string what)
{
    if (!condition)
    {
        failures++;
        Console.Error.WriteLine("FAILED: " + what);
    }
}

Check(ContactModels.PackageId == "RecursiveAI.OneContact.Models.Tier1", "the package registered itself");

// The rules cannot tell where this street ends; the model labels the words.
OperationStatus s = RuleAddressParser.TryParse("100 broadway new york ny 10001", default, out ParsedAddress? a, out ParseDetail d);
Check(s == OperationStatus.Done && a is not null, "a run-on address is read through the model");
Check(a?.PostalCode?.Standard == "10001", "postal code");
Check(d.Fields.Any(f => f.Source == DecisionSource.Model), "fields say a model decided them");

// A rule hit is kept as it is.
OperationStatus r = RuleAddressParser.TryParse("100 Broadway, New York, NY 10001", default, out ParsedAddress? ruled, out ParseDetail rd);
Check(r == OperationStatus.Done && ruled is not null && rd.Fields.All(f => f.Source == DecisionSource.Rule || f.Name is "recipient" or "organization"), "rule fields stay rule fields");

NameModelResult n = ContactModels.Name!.Analyze("Northwind Traders LLC");
Check(n.Class == NameClass.Company, "name class");

// Text extraction finds the name and the company with the span head; the e-mail address is the rules' and stays theirs.
ExtractResult x = new ContactExtractor { Sources = ExtractionSources.Text, DefaultRegion = Region.US }.ExtractText("Best regards\nJohn Carter\nAcme Plumbing LLC\nTel: (217) 555-0143\njcarter@acmeplumbing.example");
Check(x.TextModelPackage is null, "the span model is in use");
Check(x.Fields.Any(f => f.Decision == DecisionSource.Model && f.Kind == FieldKind.Name || f.Decision == DecisionSource.Model && f.Kind == FieldKind.Organization), "a name or company comes from the model");
Check(x.Of(FieldKind.Email).All(f => f.Decision == DecisionSource.Rule), "rule fields stay rule fields in extraction");

Console.WriteLine(failures == 0 ? "ok" : "failed");
return failures == 0 ? 0 : 1;
