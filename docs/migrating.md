# Migrating to OneContact

Three reads-like tables for people who know the libraries OneContact replaces. Each row names a call or concept from the other library's published documentation and the OneContact call that does the same job. There is no compatibility layer and no copied API shape: OneContact uses .NET conventions (value types, `Parse`/`TryParse`, enums, `OperationStatus`), so the names differ and a few ideas are folded together or left out on purpose. Where a feature has no equivalent, the row says so.

The other libraries are described here only from their public documentation (their READMEs, Javadoc, wikis and user guides). If a behaviour matters to you, check it against your data: the phone and structural-address layers are tested for exact agreement with their reference (see the Results section of the [README](../README.md)), the parsing layers are measured, not identical.

Habits that change for everyone:

- **No default region, no default culture.** A national phone number needs an explicit `Region`; text that is not an international number fails with `phone.region-required` rather than guessing. Nothing reads `CultureInfo`.
- **Value types.** `PhoneNumber`, `EmailAddress`, `WebAddress`, `PostalAddress`, `PersonName` and `CompanyName` are immutable structs. `==` compares the canonical form; compare keys explicitly with `Contacts.SameKey` or `.Key`.
- **Errors.** `Parse` throws `ContactParseException` (a `FormatException`) carrying a `ReasonCode`; `TryParse` returns `bool`; the low-level `TryParse(span, out value, out ParseDetail)` returns `OperationStatus` and a detail with issues, field offsets, confidences and whether a rule or a model decided each field.
- **Offsets.** Every value and part carries a `SourceSpan` of UTF-16 offsets into the original input.
- **Never invented.** OneContact does not fill in a city from a postcode, guess gender or origin, or apply a typo fix. Typos come back as suggestions (`EmailAddress.TypoSuggestion`), facts as flags.

## libphonenumber (Java) and libphonenumber-csharp → OneContact

Both libraries expose one `PhoneNumberUtil` singleton with the same method names (the C# port in Pascal case) and a mutable `PhoneNumber` message. OneContact has an immutable `PhoneNumber` struct with the common questions as members, and a `PhoneNumberParser` engine class for everything that takes a metadata choice. Namespaces: `RecursiveAI.OneContact.Phones` for the engine, enums and matcher; `RecursiveAI.OneContact` for `PhoneNumber` and `Region`. Regions are a `Region` struct (`Region.US`, `Region.Parse("DE")`), not strings.

| libphonenumber | OneContact |
|---|---|
| `PhoneNumberUtil.getInstance()` | `PhoneNumberParser.Default` (most calls are also members of `PhoneNumber`) |
| `parse(text, region)` | `PhoneNumber.Parse(text, Region)`, `PhoneNumber.TryParse(text, Region, out value)` |
| `parse` of an international number (`+...`), region `ZZ` or null | `PhoneNumber.Parse(text)` (no region) |
| `parseAndKeepRawInput` | `PhoneNumberParser.TryParse(span, region, keepRawInput: true, out number, out detail)`; `number.RawInput`, `number.CountryCodeSource` |
| `NumberParseException` and its `ErrorType` | `ContactParseException.Reason`: `PhoneRegionRequired`, `PhoneNotANumber`, `PhoneInvalidCountryCode`, `PhoneTooShortAfterIdd`, `PhoneTooShort`, `PhoneTooLong` (ids such as `phone.too-short`) |
| `isValidNumber` | `number.IsValid` |
| `isValidNumberForRegion` | `number.IsValidForRegion(region)` |
| `isPossibleNumber` / `isPossibleNumberWithReason` | `number.GetPossibility()` returns `PhonePossibility` (`IsPossible`, `IsPossibleLocalOnly`, `TooShort`, `TooLong`, `InvalidLength`, `InvalidCountryCode`); `GetPossibility(type)` checks one number type |
| `getNumberType` | `number.Type` (`PhoneNumberType`) |
| `getRegionCodeForNumber` | `number.Region` |
| `getRegionCodesForCountryCode`, `getCountryCodeForRegion` | `PhoneMetadata.Default.GetRegions(callingCode)`, `GetCallingCode(region)` |
| `getSupportedRegions`, `getSupportedCallingCodes` | `PhoneMetadata.Default.Regions`, `.CallingCodes` |
| `getSupportedTypesForRegion` | `PhoneNumberParser.Default.GetSupportedTypes(region)` |
| `getNationalSignificantNumber` | `number.NationalSignificantNumber` |
| `getCountryCode`, `getNationalNumber`, `hasExtension`, `getExtension` | `number.CountryCode`, `.NationalNumber`, `.Extension` (null when absent) |
| `isItalianLeadingZero`, `getNumberOfLeadingZeros` | `number.ItalianLeadingZero`, `.LeadingZeroCount` |
| `format(number, E164 / INTERNATIONAL / NATIONAL / RFC3966)` | `number.Format(PhoneFormat.E164 / International / National / Rfc3966)`; `number.E164` |
| `formatOutOfCountryCallingNumber` | `number.FormatOutOfCountry(callingFrom)` |
| `formatNumberForMobileDialing` | `number.FormatForMobileDialing(callingFrom, withFormatting)` |
| `formatInOriginalFormat` | `number.FormatInOriginalFormat(callingFrom)` (returns the raw input where formatting would change a digit) |
| `formatNationalNumberWithCarrierCode`, `...WithPreferredCarrierCode` | `PhoneNumberParser.Default.FormatNationalWithCarrierCode`, `FormatNationalWithPreferredCarrierCode` |
| `formatOutOfCountryKeepingAlphaChars`, `formatByPattern` | no equivalent (vanity letters are converted when parsing; custom patterns are not supported) |
| `getExampleNumber`, `getExampleNumberForType` | `PhoneNumber.GetExample(region, type)` (null when the region has none) |
| `getExampleNumberForNonGeoEntity` | `PhoneNumberParser.Default.GetNonGeographicExample(callingCode)` |
| `isAlphaNumber` | `PhoneNumberParser.IsVanityNumber(text)`; `PhoneFacts.Vanity` |
| `getLengthOfGeographicalAreaCode`, `getLengthOfNationalDestinationCode` | `GetAreaCodeLength(number)`, `GetNationalDestinationCodeLength(number)` |
| `isNumberGeographical`, `canBeInternationallyDialled` | `IsGeographical(number)`, `CanBeInternationallyDialled(number)` |
| `getNddPrefixForRegion` | `GetNationalPrefix(region)` |
| `isNANPACountry`, `isMobileNumberPortableRegion` | `IsNorthAmericanPlan(region)`, `IsMobileNumberPortableRegion(region)` |
| `isNumberMatch` and `MatchType` | no equivalent: compare `number == other` (canonical form) or `number.Key` (E.164 plus extension) |
| `findNumbers(text, region)` and `PhoneNumberMatcher` | `PhoneNumber.FindAll(text, region, leniency)`; the engine form `PhoneNumberMatcher.Default.FindAll(...)` / `TryFindNext(...)` on spans; matches carry `Start`, `End`, `RawString`, `Number` |
| `Leniency.POSSIBLE / VALID / STRICT_GROUPING / EXACT_GROUPING` | `PhoneMatchLeniency.Possible / Valid / StrictGrouping / ExactGrouping` |
| `AsYouTypeFormatter` (`getAsYouTypeFormatter(region)`, `inputDigit`, `inputDigitAndRememberPosition`, `getRememberedPosition`, `clear`) | `new AsYouTypeFormatter(region)`, `InputDigit(char)`, `InputDigitAndRememberPosition(char)`, `RememberedPosition`, `Clear()` |
| `ShortNumberInfo` `isPossibleShortNumber`, `isValidShortNumber` | `ShortNumberInfo.Default.IsPossible(number)`, `.IsValid(number)` and the `...ForRegion(number, region)` forms |
| `getExpectedCost`, `ShortNumberInfo.ShortNumberCost` | `GetExpectedCost(number)`, `GetExpectedCostForRegion(number, region)`; `ShortNumberCost` |
| `isEmergencyNumber`, `connectsToEmergencyNumber` | `IsEmergencyNumber(text, region)`, `ConnectsToEmergencyNumber(text, region)` |
| `isCarrierSpecific`, `isSmsServiceForRegion`, `getExampleShortNumber` | `IsCarrierSpecific`, `IsSmsServiceForRegion`, `GetExample(region)` |
| `PhoneNumberOfflineGeocoder.getDescriptionForNumber(number, locale)` | `PhoneGeocoder.Default.GetDescription(number, "en")` (a BCP 47 tag, package `RecursiveAI.OneContact.Phones.Geo`) |
| `PhoneNumberToCarrierMapper.getNameForNumber`, `getSafeDisplayName` | `PhoneCarrierMapper.Default.GetName(number, tag)`, `.GetSafeDisplayName(number, tag)`; the carrier is the one a range was first assigned to, so read the caveat in [phones-geo.md](phones-geo.md) |
| `PhoneNumberToTimeZonesMapper.getTimeZonesForNumber` | `PhoneTimeZoneMapper.Default.GetTimeZones(number)` |
| Loading other metadata (`MetadataLoader`, a replaced metadata file) | `new PhoneNumberParser(PhoneMetadata.LoadXml(stream))`, which reads the same XML format with size limits; see [phones.md](phones.md) |
| A `PhoneNumber` as a dictionary key or duplicate check | `number.Key` (E.164 plus extension) or `Contacts.SameKey` |

Concepts that differ:

- **Metadata** is compiled into the package from libphonenumber's published metadata files at a pinned release; [phones.md](phones.md) says which. A newer release arrives as a patch release, or load it yourself with `PhoneMetadata.LoadXml`.
- **Offsets.** `number.Source` is the span of the number inside the text you parsed; matcher results carry `Start` and `End`.
- **Batch.** `PhoneNumberParser.ParseMany(inputs, region)` parses a sequence without exceptions.

## libpostal → OneContact

libpostal gives two calls: `parse_address` (free text to labelled components) and `expand_address` (free text to normalized variants), plus near-duplicate hashing. OneContact folds them into `PostalAddress` (parse, standardize, render, validate, key) and `AddressExpander`. The big differences: OneContact reads rules first and uses a small model only for what the rules leave open (the `RecursiveAI.OneContact.Models*` packages; without one the rules still run and a refusal names the package to add), it has no multi-gigabyte data directory, and it renders addresses back in each national postal authority's own style.

| libpostal | OneContact |
|---|---|
| `parse_address(text)` with optional `language` and `country` options | `PostalAddress.Parse(text)`; with a country, `PostalAddress.TryParse(text, Region, out value, out detail)`. Without a country OneContact infers it from the evidence in the text (`Flags` has `CountryInferred`) or reports `CountryUndetermined`; it never guesses |
| Components come back as `(label, value)` pairs | Properties on `PostalAddress`; offsets and confidences through `ParseDetail.Fields` and `PostalAddress.RuleDetail` |
| `house` (a venue or building name) | `Organization` / `Recipient` lines |
| `house_number` | `StreetDetail` part `StreetPartRole.HouseNumber` |
| `road` | `StreetDetail` parts `PreDirectional`, `StreetName`, `StreetType`, `PostDirectional`; the whole line is in `StreetLines` |
| `unit` | parts `UnitDesignator` and `UnitNumber` |
| `po_box` | street line of kind `PostOfficeBox` with parts `BoxDesignator`, `BoxNumber` (`AddressFacts.PostOfficeBox`) |
| `level`, `staircase`, `entrance` | not separate fields; the text stays in the street line as written |
| `suburb`, `city_district` | `DependentLocality` |
| `city` | `Locality` |
| `state_district`, `state`, `country_region` | `AdminArea` (the authority's state, province or territory code is the standard form) |
| `postcode` | `PostalCode` |
| `country` | `Country` (a `Region`) |
| `category`, `near`, `island`, `world_region` | not fields |
| `expand_address(text, languages=[...])` | `AddressExpander.Expand(text, ExpansionLanguages.English \| ExpansionLanguages.German, maxVariants)`; the languages are flags (`English`, `French`, `German`, `Dutch`, `Italian`, `Luxembourgish`, `Spanish`, `Portuguese`, `Catalan`, `Basque`, `Galician`, `Maltese`, `Irish`; English and French by default) |
| Expansion options (which transformations to apply) | no per-transformation switches: the variants come back with the fully expanded one first, capped by `maxVariants` (default 16) |
| Near-duplicate hashing and duplicate classification | `PostalAddress.Key` / `GetKey(KeyVersion)`: one deterministic string built from the parsed parts and the expansions; equal keys mean the same address under the documented rules (see [address-keys.md](address-keys.md)). There is no probabilistic classifier |
| `libpostal_setup()` and the data directory (about 2 GB) | nothing to set up; rules and dictionaries are compiled in, and a model package is 10 to 23 MB |
| Language classification (`libpostal_place_languages`) | not a call: the reader chooses languages from the country and the evidence |
| Normalizing for display or labels | `address.ToString(AddressStyle.Postal / Display, AddressLayout.Label / SingleLine, includeCountry)`; `GetLabelLines()` and `ToSingleLine()` for the country's own field layout |
| Checking an address is plausible | `address.Validate()` returns `AddressIssue`s from structural rules only (required fields, postcode pattern, postcode and region agreement). There are no reference-data lookups |

Not equivalent: libpostal's training data and models are not used or read. The OneContact models are trained on open registers and our own renderers, with their provenance in the model packages.

## python-nameparser and probablepeople → OneContact

python-nameparser (`HumanName`) splits a person name by rules, with customizable lists. probablepeople tags a person or corporation string with a trained CRF. OneContact does both jobs in `PersonName`, `CompanyName` and `NameParsing`: rules first, a small model for the unclear remainder, with the decision source reported.

| python-nameparser | OneContact |
|---|---|
| `HumanName(full_name)` | `PersonName.Parse(text)`; add `NameHints.ForCountry(Region)` or `NameHints.ForLanguage(tag)` for order and particles |
| `name.title` | `Prefix` |
| `name.first` | `Given` |
| `name.middle` | `Middle` |
| `name.last` | `Family`, with `Particle` kept separate (`van der`) and `SecondFamily` for Iberian names |
| `name.suffix` | `Suffix` (generational) and `ProfessionalSuffix` (credentials) |
| `name.nickname` | `Nickname` |
| `name.full_name`, `str(name)`, `string_format` | `Canonical`; compose your own text from the parts |
| `name.capitalize()` | done on parse: all-caps or all-lower input is restored (`NameFacts.CasingRestored`), traceable by offsets |
| `name.original`, `name.unparsable` | `Source` span; `TryParse` returns `false` or an `OperationStatus` with a reason |
| `name.as_dict()`, `initials()` | read the parts; no initials helper |
| "Last, First" input | read as given order `FamilyCommaGiven` (`PersonName.Order`) |
| Customizing `CONSTANTS` (titles, prefixes, suffixes, conjunctions) | no runtime mutation of the lists. Titles, suffixes, particles and legal forms are compiled data; use `NameHints` for country and language |
| `"John and Jane Smith"` (kept as one string) | `NameParsing.Split(text)` returns separate `PersonName`s that share the family name |
| Equality of names | `==` compares canonical forms; `person.Key` is the aggressive key (folded diacritics, nickname equivalence, titles and suffixes dropped, see [name-keys.md](name-keys.md)) |

| probablepeople | OneContact |
|---|---|
| `tag(text)` returning `Person`, `Corporation` or `Household` | `NameParsing.Classify(text, hints)` / `NameParsing.Split(text, hints).Class`: `NameClass.Person`, `Company`, `Multiple` (a household of several people), `Unknown`. Unknown is an honest answer |
| `parse(text)` returning `(token, label)` pairs | the parts of `PersonName` and `CompanyName`, each with a span |
| `GivenName`, `MiddleName`, `Surname` | `Given`, `Middle`, `Family` |
| `FirstInitial`, `MiddleInitial`, `LastInitial` | no separate label: an initial is a `Given`, `Middle` or `Family` part whose text is one letter |
| `PrefixMarital`, `PrefixOther` | `Prefix` |
| `SuffixGenerational`, `SuffixOther` | `Suffix`, `ProfessionalSuffix` |
| `Nickname` | `Nickname` |
| `And` | a split into several people (`NameClass.Multiple`) |
| `CorporationName`, `CorporationNameOrganization` | `CompanyName.Base` |
| `CorporationLegalType` | `CompanyName.LegalForm`: an ISO 20275 entity legal form with its ELF code (`ElfCode`) and canonical abbreviation; `LegalFormBeforeName`, `LegalFormAmbiguous` and `LegalFormInactive` flags |
| Other corporation labels (branch, committee, ...) | not parts. DBA and c/o are split out as `TradingAs` and `CareOf`; a leading "The" is `LeadingArticle` |
| Model confidence | `ParseDetail` per field: `Confidence` and `Source` (`Rule` or `Model`) |
| Training your own model | the recipes in `recipes/` use the `Train` API of OneNLP; see [models.md](models.md) |

Not equivalent: neither library's data or models are used. OneContact never infers gender, origin or a salutation from a name.
