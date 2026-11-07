# RecursiveAI.OneContact.Phones.Geo

libphonenumber- and libpostal-class contact data for .NET.

Geocoder, carrier and time-zone mappers for phone numbers, an add-on to `RecursiveAI.OneContact`. The tables are embedded and read on first use; nothing is fetched.

```csharp
PhoneNumber number = PhoneNumber.Parse("+44 20 7946 0958");
string place = PhoneGeocoder.Default.GetDescription(number, "en");        // London
IReadOnlyList<string> zones = PhoneTimeZoneMapper.Default.GetTimeZones(number);
```

**Carrier caveat.** The carrier mapper names the carrier a number range was first assigned to. Where number portability exists, a number may have moved to another carrier and no offline table can know that. Use `GetSafeDisplayName` when a wrong carrier would mislead, and never treat the result as proof of who operates a line.

- Documentation: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/docs/phones-geo.md
- Changelog: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/CHANGELOG.md
- Source: <https://github.com/recursive-ai-oss/onecontact-csharp>

Licensed `MIT OR Apache-2.0`. Copyright (c) Recursive AI, LLC. Data notices: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/THIRD-PARTY-NOTICES.md
