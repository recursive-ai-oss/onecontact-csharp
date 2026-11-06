using System.Text;

namespace RecursiveAI.OneContact.Bench;

/// <summary>The inputs every comparison row uses. They are written here or seeded, so each run and each library sees the same bytes.</summary>
internal static class Inputs
{
    /// <summary>Numbers a person would type, with the region they are typed in. All parse in both libraries; some are not valid.</summary>
    public static readonly (string Region, string Text)[] Phones =
    [
        ("US", "(202) 456-1111"), ("US", "202-456-1111"), ("US", "+1 650 253 0000"), ("US", "1-800-356-9377"), ("US", "650.253.0000"),
        ("US", "2024561111"), ("US", "(212) 736-5000 ext. 12"), ("US", "555-0100"), ("CA", "(416) 555-0199"), ("CA", "+1 604 555 0123"),
        ("CA", "613-995-0253"), ("GB", "020 7946 0958"), ("GB", "+44 20 7946 0958"), ("GB", "07911 123456"), ("GB", "0800 123 4567"),
        ("DE", "030 123456"), ("DE", "+49 30 901820"), ("DE", "0151 23456789"), ("FR", "01 42 68 53 00"), ("FR", "+33 1 42 68 53 00"),
        ("FR", "06 12 34 56 78"), ("AU", "(02) 9374 4000"), ("AU", "+61 2 9374 4000"), ("AU", "0412 345 678"), ("BR", "(11) 91234-5678"),
        ("BR", "+55 11 91234-5678"), ("IN", "098765 43210"), ("IN", "+91 98765 43210"), ("JP", "03-1234-5678"), ("JP", "+81 3 1234 5678"),
        ("MX", "+52 55 1234 5678"), ("IT", "+39 06 1234 5678"), ("ES", "+34 912 34 56 78"), ("NL", "020 123 4567"), ("SE", "08-123 456 78"),
        ("PL", "22 123 45 67"), ("CH", "044 668 18 00"), ("AT", "01 5871234"), ("BE", "02 123 45 67"), ("US", "1-800-GOOD-JOB"),
    ];

    /// <summary>Addresses as people write them into forms; <c>Valid</c> says whether RFC 5322 (with RFC 6531) accepts the text.</summary>
    public static readonly (string Text, bool Valid)[] Emails =
    [
        ("jane@example.com", true), ("jane.smith@example.co.uk", true), ("jane+crm@example.com", true), ("J.Smith+crm@GoogleMail.com", true),
        ("\"J. Smith\" <jsmith@example.com>", true), ("Jane Smith <jane@example.org>", true), ("<jane@example.org>", true),
        ("\"quoted local\"@example.com", true), ("user@sub.domain.example.com", true), ("a@b.co", true), ("x_y-z@mail.example.net", true),
        ("first.last@example.museum", true), ("user@[192.168.0.1]", true), ("info@xn--bcher-kva.example", true), ("user@例え.jp", true),
        ("ユーザー@example.jp", true), ("o'brien@example.ie", true), ("a!b#c$d%e&f'g*h+i/j=k?l^m_n`o{p|q}r~s@example.com", true),
        ("plainaddress", false), ("@example.com", false), ("jane@", false), ("jane@@example.com", false), ("jane smith@example.com", false),
        ("jane@example..com", false), (".jane@example.com", false), ("jane.@example.com", false), ("jane@-example.com", false),
        ("jane@example.com.", true), ("jane(comment)@example.com", true), ("\"unterminated@example.com", false),
        ("jane@exa mple.com", false), ("jane@example,com", false), ("Jane <jane@example.com> extra", false),
    ];

    /// <summary>Absolute URLs, written as found on web pages.</summary>
    public static readonly string[] Urls =
    [
        "https://www.example.com/", "http://example.com", "https://Example.COM:443/Path/To/Page.html?x=1&y=2#frag", "https://shop.example.co.uk/a/b/c?utm_source=x&id=7",
        "https://blog.example.com.au/2024/05/post-title/", "http://192.168.0.1:8080/admin", "https://münchen.example/straße", "https://xn--bcher-kva.example/",
        "https://user:pw@example.org/dir/", "https://example.com/a%20b/c%2Fd?q=%E2%9C%93", "http://[2001:db8::1]/index.html", "https://www.linkedin.com/in/jane-smith-123/",
        "https://github.com/recursive-ai-oss/onecontact-csharp", "https://example.com/a/./b/../c", "HTTPS://EXAMPLE.ORG/%7Euser", "ftp://files.example.net/pub/readme.txt",
        "https://example.com?query", "https://a.b.c.d.example.com/", "https://www.example.com:8443/x", "https://bücher.de/", "mailto:jane@example.com",
        "https://example.com/path with space", "https://example.com/\\back\\slash", "https://www.city.kawasaki.jp/", "https://foo.bar.pvt.k12.ma.us/",
        "https://x.github.io/", "https://example.xn--p1ai/", "https://EXAMPLE.com./", "https://example.com:99999/", "https://ex ample.com/",
    ];

    /// <summary>Hosts for the public-suffix row.</summary>
    public static readonly string[] Hosts =
    [
        "www.example.com", "example.co.uk", "shop.example.co.uk", "blog.example.com.au", "a.b.c.d.example.com", "foo.bar.pvt.k12.ma.us", "x.github.io",
        "www.city.kawasaki.jp", "example.org", "deep.sub.domain.example.net", "jane.blogspot.com", "example.com.br", "mail.example.de", "www.example.ac.uk",
    ];

    /// <summary>Names as they arrive in CRM exports: plain, titled, comma-ordered, suffixed, with particles, all caps.</summary>
    public static readonly string[] Names =
    [
        "Jane Smith", "John Q. Public", "Dr. Maria Garcia", "Smith, Jane", "Mr. Robert Brown Jr.", "MARY ANN O'BRIEN", "Ludwig van Beethoven", "Anna-Lena Weber",
        "james t. kirk", "Mrs. Elizabeth Alexandra Mary Windsor", "Juan de la Cruz", "Prof. Dr. Hans Müller", "Sarah Connor, PhD", "Michael Jordan", "Linda McDonald",
        "Kim Lee", "Jean-Luc Picard", "Maria José García López", "Bob Johnson III", "Rev. Martin Luther King, Jr.", "Emily Davis", "William Henry Gates III",
        "Carlos Santana", "Nguyen Van Anh", "Jessica Alba", "Smith, Dr. John A.", "Patricia Williams", "Christopher Robin", "Amelia Earhart", "DAVID JONES",
        "Acme Holdings Pty Ltd", "Widgets Inc.", "Smith & Sons LLC", "The Bank of Nowhere", "Mr & Mrs John Smith", "Jane and John Smith", "Sue Lee", "Tom",
    ];

    private static readonly string[] s_given = ["Jane", "John", "Maria", "Robert", "Linda", "James", "Patricia", "Michael", "Barbara", "William", "Elizabeth", "David"];
    private static readonly string[] s_family = ["Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Martinez", "Lopez", "Wilson", "Anderson"];
    private static readonly string[] s_street = ["Main St", "Oak Avenue", "Maple Drive", "Cedar Ln", "Elm Street", "Park Blvd", "Washington Street", "Lake Road"];
    private static readonly string[] s_city = ["Springfield, IL 62704", "Austin, TX 78701", "Portland, OR 97201", "Denver, CO 80202", "Madison, WI 53703", "Raleigh, NC 27601"];
    private static readonly string[] s_company = ["Acme Holdings LLC", "Northwind Traders Inc.", "Blue Harbor Dental", "Summit Legal Group LLP", "Pioneer Plumbing Co.", "Greenleaf Landscaping"];
    private static readonly string[] s_filler =
    [
        "We have served the community for more than twenty years and we are proud of the work we do.",
        "Our office is open Monday to Friday from 9 am to 5 pm, and by appointment on weekends.",
        "Please contact us with any questions about our services or to schedule a visit.",
        "Read our privacy policy and terms of use. All rights reserved. Order 4471 shipped 2024-05-17.",
        "Meet the team behind the company and learn about how we got started in 1998.",
    ];

    /// <summary>What a generated page holds, so each library can be scored on the same plant.</summary>
    /// <param name="Html">The page.</param>
    /// <param name="Text">The page's visible text, as the plain-text comparator reads it.</param>
    /// <param name="Phones">The planted phone numbers, as digits only.</param>
    /// <param name="Emails">The planted email addresses.</param>
    /// <param name="Websites">The planted website hosts.</param>
    internal sealed record Page(string Html, string Text, List<string> Phones, List<string> Emails, List<string> Websites);

    /// <summary>Builds a page of staff blocks (name, company, street, city line, phone, email, website) among navigation and prose.</summary>
    /// <param name="seed">The seed.</param>
    /// <param name="blocks">How many contact blocks.</param>
    /// <returns>The page and its plant.</returns>
    public static Page MakePage(int seed, int blocks)
    {
        Random rng = new(seed);
        var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><title>Contact us</title><meta charset=\"utf-8\"><style>.hide{display:none}</style></head><body><nav><a href=\"/\">Home</a> <a href=\"/about\">About</a> <a href=\"/contact\">Contact</a></nav><main>");
        var text = new StringBuilder();
        List<string> phones = [];
        List<string> emails = [];
        List<string> sites = [];
        for (int i = 0; i < blocks; i++)
        {
            string given = s_given[rng.Next(s_given.Length)];
            string family = s_family[rng.Next(s_family.Length)];
            string company = s_company[rng.Next(s_company.Length)];
            string host = company.Split(' ')[0].ToLowerInvariant() + rng.Next(100) + ".com";
            string email = given.ToLowerInvariant() + "." + family.ToLowerInvariant() + "@" + host;
            string area = (200 + rng.Next(700)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string phone = "(" + area + ") 555-" + rng.Next(1000, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string[] lines =
            [
                given + " " + family,
                company,
                rng.Next(10, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + s_street[rng.Next(s_street.Length)],
                s_city[rng.Next(s_city.Length)],
                "Tel: " + phone,
                "Email: " + email,
                "www." + host,
                s_filler[rng.Next(s_filler.Length)],
            ];
            html.Append("<section class=\"person\"><h3>").Append(lines[0]).Append("</h3>");
            foreach (string line in lines.Skip(1))
            {
                html.Append("<p>").Append(line).Append("</p>");
            }

            html.Append("</section>");
            foreach (string line in lines)
            {
                text.Append(line).Append('\n');
            }

            phones.Add(new string(phone.Where(char.IsAsciiDigit).ToArray()));
            emails.Add(email);
            sites.Add("www." + host);
        }

        html.Append("</main><footer><p>Copyright 2026 Example Co. All rights reserved.</p></footer></body></html>");
        return new Page(html.ToString(), text.ToString(), phones, emails, sites);
    }
}
