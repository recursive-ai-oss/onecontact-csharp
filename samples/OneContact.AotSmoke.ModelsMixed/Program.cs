using System.Buffers;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Accurate;
using RecursiveAI.OneContact.Models.Superset;

// References the Tier 1 + Tier 2 package and the accurate-tier package in one app and checks who serves what: addresses and names are read by the fast tier,
// contact spans in running text by the accurate tier. Exits non-zero on the first failure.
int failures = 0;

void Check(bool condition, string what)
{
    if (!condition)
    {
        failures++;
        Console.Error.WriteLine("FAILED: " + what);
    }
}

static bool Same(IReadOnlyList<ModelSpan> a, IReadOnlyList<ModelSpan> b) => a.Count == b.Count && a.Zip(b).All(p => p.First == p.Second);

// Normalization: the fast tier's package is the one in use, and its address model is the one reading.
Check(ContactModels.PackageId == SupersetModel.PackageId, "the fast tier serves normalization");
const string Address = "58 Hordley Avenue Shrewsbury\nSY4 1AA";
Check(Same(ContactModels.Address!.Tag(Address), SupersetModel.Instance.Tag(Address)), "the address model is the fast tier's");
OperationStatus s = RuleAddressParser.TryParse(Address, Region.Parse("GB"), out ParsedAddress? gb, out ParseDetail d);
Check(s == OperationStatus.Done && gb is not null && d.Fields.Any(f => f.Source == DecisionSource.Model), "a Tier 2 address is read through the model");

// Extraction: the accurate tier's span model is the one registered.
const string Text = "Best regards\nJohn Carter\nAcme Plumbing LLC\n58 Hordley Avenue, Shrewsbury SY4 1AA\nTel: (217) 555-0143\njcarter@acmeplumbing.example";
ContactSpanResult served = ContactModels.Spans!.Find(Text);
Check(Same(served.Spans, AccurateModel.Instance.Find(Text).Spans), "the span model is the accurate tier's");
Check(!Same(served.Spans, SupersetModel.Instance.Find(Text).Spans), "the accurate tier's spans differ from the fast tier's on this text");

Console.WriteLine(failures == 0 ? "ok" : "failed");
return failures == 0 ? 0 : 1;
