using System.Globalization;
using System.Net.Mail;
using System.Text;
using MimeKit;
using RecursiveAI.OneContact;

// usage: EmailCompare <corpus directory> [isemail tests.xml]
// Feeds every corpus input to this library, System.Net.Mail.MailAddress.TryCreate and MimeKit's MailboxAddress.TryParse (public APIs only)
// and prints how often each accepts what the corpus says is valid or invalid. Information only: nothing here gates.
string dir = args[0];
const string Separator = " \u00a6 ";
List<(string Id, string Input, bool Valid)> cases = [];
foreach (string file in Directory.GetFiles(dir, "*.cases").Order(StringComparer.Ordinal))
{
    if (Path.GetFileName(file) == "isemail-overrides.cases")
    {
        continue;
    }

    foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
    {
        if (line.Length == 0 || line[0] == '#')
        {
            continue;
        }

        string[] c = line.Split(Separator);
        cases.Add((c[0], Unescape(c[2]), c[3] == "valid"));
    }
}

if (args.Length > 1)
{
    foreach (var t in System.Xml.Linq.XDocument.Load(args[1]).Descendants("test"))
    {
        string address = t.Element("address")?.Value ?? string.Empty;
        StringBuilder sb = new();
        foreach (char ch in address)
        {
            sb.Append(ch is >= '\u2400' and <= '\u241F' ? (char)(ch - 0x2400) : ch == '\u2421' ? '\u007f' : ch);
        }

        cases.Add(("isemail-" + (string)t.Attribute("id")!, sb.ToString(), t.Element("category")!.Value != "ISEMAIL_ERR"));
    }
}

int[,] table = new int[2, 8]; // [valid?, accept bits: ours=1, MailAddress=2, MimeKit=4]
int[] accepts = new int[3];
int[] agree = new int[3];
foreach ((string id, string input, bool valid) in cases)
{
    bool ours = EmailAddress.TryParse(input, out _);
    bool net = MailAddress.TryCreate(input, out _);
    bool mime = MailboxAddress.TryParse(input, out _);
    int bits = (ours ? 1 : 0) | (net ? 2 : 0) | (mime ? 4 : 0);
    table[valid ? 1 : 0, bits]++;
    accepts[0] += ours ? 1 : 0;
    accepts[1] += net ? 1 : 0;
    accepts[2] += mime ? 1 : 0;
    agree[0] += ours == valid ? 1 : 0;
    agree[1] += net == valid ? 1 : 0;
    agree[2] += mime == valid ? 1 : 0;
}

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"cases: {cases.Count} ({cases.Count(c => c.Valid)} expected valid, {cases.Count(c => !c.Valid)} expected invalid)"));
string[] names = ["OneContact", "MailAddress", "MimeKit"];
for (int i = 0; i < 3; i++)
{
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{names[i],-12} accepts {accepts[i],4}; agrees with the corpus expectation on {agree[i],4}"));
}

Console.WriteLine("expectation | OneContact MailAddress MimeKit : count");
for (int v = 1; v >= 0; v--)
{
    for (int bits = 0; bits < 8; bits++)
    {
        if (table[v, bits] > 0)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{(v == 1 ? "valid  " : "invalid")}     | {((bits & 1) != 0 ? "accept" : "reject"),-10} {((bits & 2) != 0 ? "accept" : "reject"),-11} {((bits & 4) != 0 ? "accept" : "reject"),-7} : {table[v, bits]}"));
        }
    }
}

static string Unescape(string s)
{
    StringBuilder sb = new();
    for (int i = 0; i < s.Length; i++)
    {
        if (s[i] != '\\' || i + 1 >= s.Length)
        {
            sb.Append(s[i]);
            continue;
        }

        char n = s[++i];
        switch (n)
        {
            case 's': sb.Append(' '); break;
            case 't': sb.Append('\t'); break;
            case 'r': sb.Append('\r'); break;
            case 'n': sb.Append('\n'); break;
            case '\\': sb.Append('\\'); break;
            case 'u':
                sb.Append((char)int.Parse(s.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                i += 4;
                break;
            default: sb.Append('\\').Append(n); break;
        }
    }

    return sb.ToString();
}
