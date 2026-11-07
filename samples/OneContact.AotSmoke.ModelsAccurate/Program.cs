using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Accurate;
using RecursiveAI.OneContact.Names;

// Loads the accurate-tier model from its embedded head weights and the OneNLP encoder package, with no call from this program but the parsers', and uses it.
// Exits non-zero on the first failure.
int failures = 0;

void Check(bool condition, string what)
{
    if (!condition)
    {
        failures++;
        Console.Error.WriteLine("FAILED: " + what);
    }
}

Check(ContactModels.PackageId == "RecursiveAI.OneContact.Models.Accurate", "the package registered itself");
Check(ContactModels.Spans is not null, "the span model is registered by the package");

NameModelResult n = ContactModels.Name!.Analyze("Northwind Traders LLC");
Check(n.Class == NameClass.Company, "name class");

ExtractResult x = new ContactExtractor { Sources = ExtractionSources.Text, DefaultRegion = Region.US }.ExtractText("Best regards\nJohn Carter\nAcme Plumbing LLC\nTel: (217) 555-0143\njcarter@acmeplumbing.example");
Check(x.Of(FieldKind.Email).All(f => f.Decision == DecisionSource.Rule), "the e-mail address is a rule's");
Check(x.Of(FieldKind.Phone).Count == 1, "the phone number is found");
Check(x.Of(FieldKind.Email).Count == 1, "the e-mail address is found");

Check(AccurateModel.Instance.Find("John Carter\njcarter@acmeplumbing.example").Spans.Count > 0, "the span head runs");

Console.WriteLine(failures == 0 ? "ok" : "failed");
return failures == 0 ? 0 : 1;
