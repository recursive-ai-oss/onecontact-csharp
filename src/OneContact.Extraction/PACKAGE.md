# RecursiveAI.OneContact.Extraction

libphonenumber- and libpostal-class contact data for .NET.

Finds people, organizations and their phones, emails, websites and addresses in web pages and plain text, and returns them as records. Every field carries its source, a confidence, its offsets (bytes into the original HTML, or characters for text) and a value normalized by `RecursiveAI.OneContact`.

```csharp
IReadOnlyList<ContactRecord> records = ContactExtractor.Extract(html);

ExtractResult note = new ContactExtractor().ExtractText("Name: Jo Smoke\nTel: +1 202 555 0145\njo@example.com");
```

- Structured markup first (JSON-LD, microdata, RDFa Lite, h-card, `mailto:` and `tel:` links, `<address>`), then visible text through OneHtml's cascade. Hidden text is skipped.
- Obfuscated emails (`john [at] example [dot] com`, entity-encoded links, Cloudflare `data-cfemail`) are decoded and flagged.
- Pages of one site merge; records de-duplicate on keys; disagreeing values are kept with their sources.
- Version 1.0 runs on markup and rules. Model packages that read more people, organizations and addresses from running text follow in 1.1.
- Runs offline, on what you pass in, and stores nothing. Responsibility for lawful processing of the personal data it finds (for example under the GDPR) sits with the caller.

Documentation: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/docs/extraction.md. Changelog: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/CHANGELOG.md. Source: <https://github.com/recursive-ai-oss/onecontact-csharp>

Licensed `MIT OR Apache-2.0`. Copyright (c) Recursive AI, LLC. Third-party notices: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/THIRD-PARTY-NOTICES.md
