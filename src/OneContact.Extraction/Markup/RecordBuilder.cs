using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>
/// Turns items (from any reader) into record drafts, following the markup's own nesting: an organization that lists employees gives
/// a record for each, linked; a person's <c>worksFor</c> gives a linked organization; a <c>ContactPoint</c> or <c>location</c> adds its
/// phone, email, url and address to the record that holds it.
/// </summary>
internal sealed class RecordBuilder
{
    private const int MaxDepth = 32;
    private const int MaxVisits = 200_000;

    // The schema.org Organization hierarchy as schema.org lists it. A type outside it is not read as an organization.
    private static readonly HashSet<string> OrganizationTypes = new(StringComparer.Ordinal)
    {
        "Organization", "Corporation", "NGO", "LocalBusiness", "Airline", "Consortium", "GovernmentOrganization", "LibrarySystem",
        "NewsMediaOrganization", "OnlineBusiness", "OnlineStore", "PerformingGroup", "DanceGroup", "MusicGroup", "TheaterGroup",
        "ResearchOrganization", "SportsOrganization", "SportsTeam", "WorkersUnion", "EducationalOrganization", "CollegeOrUniversity",
        "School", "ElementarySchool", "HighSchool", "MiddleSchool", "Preschool", "MedicalOrganization", "Dentist", "Hospital", "Pharmacy",
        "Physician", "DiagnosticLab", "VeterinaryCare", "MedicalClinic", "AnimalShelter", "ArchiveOrganization", "AutomotiveBusiness",
        "AutoBodyShop", "AutoDealer", "AutoPartsStore", "AutoRental", "AutoRepair", "AutoWash", "GasStation", "MotorcycleDealer",
        "MotorcycleRepair", "ChildCare", "DryCleaningOrLaundry", "EmergencyService", "FireStation", "PoliceStation", "EmploymentAgency",
        "EntertainmentBusiness", "AmusementPark", "ArtGallery", "Casino", "ComedyClub", "MovieTheater", "NightClub", "FinancialService",
        "AccountingService", "AutomatedTeller", "BankOrCreditUnion", "InsuranceAgency", "FoodEstablishment", "Bakery", "BarOrPub", "Brewery",
        "CafeOrCoffeeShop", "Distillery", "FastFoodRestaurant", "IceCreamShop", "Restaurant", "Winery", "GovernmentOffice", "PostOffice",
        "HealthAndBeautyBusiness", "BeautySalon", "DaySpa", "HairSalon", "HealthClub", "NailSalon", "TattooParlor",
        "HomeAndConstructionBusiness", "Electrician", "GeneralContractor", "HVACBusiness", "HousePainter", "Locksmith", "MovingCompany",
        "Plumber", "RoofingContractor", "InternetCafe", "LegalService", "Attorney", "Notary", "Library", "LodgingBusiness", "BedAndBreakfast",
        "Campground", "Hostel", "Hotel", "Motel", "Resort", "SkiResort", "VacationRental", "ProfessionalService", "RadioStation",
        "RealEstateAgent", "RecyclingCenter", "SelfStorage", "ShoppingCenter", "SportsActivityLocation", "TelevisionStation",
        "TouristInformationCenter", "TravelAgency", "Store", "BikeStore", "BookStore", "ClothingStore", "ComputerStore",
        "ConvenienceStore", "DepartmentStore", "ElectronicsStore", "Florist", "FurnitureStore", "GardenStore", "GroceryStore",
        "HardwareStore", "HobbyShop", "HomeGoodsStore", "JewelryStore", "LiquorStore", "MensClothingStore", "MobilePhoneStore",
        "MovieRentalStore", "MusicStore", "OfficeEquipmentStore", "OutletStore", "PawnShop", "PetStore", "ShoeStore", "SportingGoodsStore",
        "TireShop", "ToyStore", "WholesaleStore",
    };

    private readonly Dictionary<string, MarkupItem> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<MarkupItem, RecordDraft> _built = new(ReferenceEqualityComparer.Instance);
    private int _visits;

    public RecordBuilder(IReadOnlyList<MarkupItem> roots)
    {
        IndexIds(roots);
    }

    public List<RecordDraft> Records { get; } = [];

    /// <summary>True when the work budget ran out; what was read until then is kept.</summary>
    public bool LimitReached { get; private set; }

    public void Build(IReadOnlyList<MarkupItem> roots)
    {
        foreach (MarkupItem root in roots)
        {
            Visit(root, null, 0);
        }
    }

    private void IndexIds(IReadOnlyList<MarkupItem> roots)
    {
        var stack = new Stack<MarkupItem>(roots.Reverse());
        int seen = 0;
        while (stack.Count > 0 && seen++ < MaxVisits)
        {
            MarkupItem item = stack.Pop();
            if (item.Id is not null && (item.Properties.Count > 0 || item.Types.Count > 0))
            {
                _ids.TryAdd(item.Id, item);
            }

            foreach (MarkupProperty p in item.Properties)
            {
                if (p.Item is not null)
                {
                    stack.Push(p.Item);
                }
            }
        }
    }

    private MarkupItem? Resolve(MarkupProperty p)
    {
        if (p.Item is not null)
        {
            return p.Item;
        }

        return p.Reference is not null && _ids.TryGetValue(p.Reference, out MarkupItem? target) ? target : null;
    }

    private bool Spend()
    {
        if (++_visits > MaxVisits)
        {
            LimitReached = true;
            return false;
        }

        return true;
    }

    private static ContactRecordKind? Classify(MarkupItem item)
    {
        foreach (string t in item.Types)
        {
            if (t == "Person")
            {
                return ContactRecordKind.Person;
            }
        }

        foreach (string t in item.Types)
        {
            if (OrganizationTypes.Contains(t))
            {
                return ContactRecordKind.Organization;
            }
        }

        return null;
    }

    // An h-card says only "card". It is an organization when it names itself as its own org (class "fn org") or its name carries a
    // legal form; otherwise a person. The caller's expectation (a nested h-card under p-org) wins.
    private static ContactRecordKind ClassifyCard(MarkupItem item, ContactRecordKind? expected)
    {
        if (expected is not null)
        {
            return expected.Value;
        }

        string? name = item.Properties.FirstOrDefault(p => p.Name == "name")?.Text;
        if (string.IsNullOrWhiteSpace(name))
        {
            return ContactRecordKind.Person;
        }

        foreach (MarkupProperty p in item.Properties)
        {
            if (p.Name == "worksFor" && p.Text is not null && SameText(p.Text, name))
            {
                return ContactRecordKind.Organization;
            }
        }

        return CompanyName.TryParse(name, out CompanyName company) && company.LegalForm is not null
            ? ContactRecordKind.Organization
            : ContactRecordKind.Person;
    }

    private static bool SameText(string a, string b) =>
        string.Equals(Collapse(a), Collapse(b), StringComparison.OrdinalIgnoreCase);

    private static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private RecordDraft? Visit(MarkupItem item, ContactRecordKind? expected, int depth)
    {
        if (depth > MaxDepth || !Spend())
        {
            return null;
        }

        if (_built.TryGetValue(item, out RecordDraft? existing))
        {
            return existing;
        }

        ContactRecordKind? kind = item.Types.Contains("h-card") ? ClassifyCard(item, expected) : Classify(item) ?? expected;
        if (kind is null)
        {
            // Not a person or organization itself (an Article, a Product): look for records inside it.
            foreach (MarkupProperty p in item.Properties)
            {
                if (Resolve(p) is MarkupItem inner)
                {
                    Visit(inner, null, depth + 1);
                }
            }

            return null;
        }

        var draft = new RecordDraft(kind.Value, item.Source, item.Range, item.ElementIndex);
        _built[item] = draft;
        Records.Add(draft);
        Fill(draft, item, depth);
        return draft;
    }

    private void Fill(RecordDraft record, MarkupItem item, int depth)
    {
        bool hasName = item.Properties.Any(p => p.Name == "name" && p.Text is not null);
        MarkupProperty? given = null;
        MarkupProperty? family = null;
        var directAddress = new List<MarkupProperty>();
        bool hasAddressItem = false;

        foreach (MarkupProperty p in item.Properties)
        {
            switch (p.Name)
            {
                case "name":
                    AddText(record, FieldKind.Name, p, item);
                    break;
                case "legalName":
                    if (!hasName)
                    {
                        AddText(record, FieldKind.Name, p, item);
                    }

                    break;
                case "givenName":
                    given ??= p;
                    break;
                case "familyName":
                    family ??= p;
                    break;
                case "telephone":
                    AddText(record, FieldKind.Phone, p, item);
                    break;
                case "email":
                    AddText(record, FieldKind.Email, p, item);
                    break;
                case "url":
                case "sameAs":
                    AddText(record, FieldKind.Website, p, item);
                    break;
                case "address":
                    hasAddressItem |= AddAddress(record, p, item, depth);
                    break;
                case "streetAddress":
                case "extendedAddress":
                case "addressLocality":
                case "addressRegion":
                case "postalCode":
                case "addressCountry":
                    directAddress.Add(p);
                    break;
                case "contactPoint":
                case "contactPoints":
                case "location":
                    MergeIn(record, p, depth + 1);
                    break;
                case "worksFor":
                    WorksFor(record, p, item, depth);
                    break;
                case "employee":
                case "employees":
                case "member":
                case "members":
                    LinkedRecord(record, p, ContactRecordKind.Person, RecordLinkKind.HasMember, RecordLinkKind.WorksFor, depth);
                    break;
                case "parentOrganization":
                    LinkedRecord(record, p, ContactRecordKind.Organization, RecordLinkKind.ParentOrganization, RecordLinkKind.SubOrganization, depth);
                    break;
                case "subOrganization":
                case "department":
                    LinkedRecord(record, p, ContactRecordKind.Organization, RecordLinkKind.SubOrganization, RecordLinkKind.ParentOrganization, depth);
                    break;
                default:
                    if (Resolve(p) is MarkupItem inner)
                    {
                        Visit(inner, null, depth + 1);
                    }

                    break;
            }
        }

        if (!hasName && given?.Text is not null && family?.Text is not null)
        {
            // Given and family name are separate properties: the field's text puts them in reading order, and its range covers both.
            TextRange hull = TextRange.Hull(given.Raw, family.Raw);
            record.Fields.Add(new FieldDraft
            {
                Kind = FieldKind.Name,
                Source = item.Source,
                Text = Collapse(given.Text) + " " + Collapse(family.Text),
                Raw = hull,
                Hidden = item.Hidden || given.Hidden || family.Hidden,
                Composed = true,
            });
        }
        else if (!hasName && (given?.Text ?? family?.Text) is not null)
        {
            MarkupProperty only = given ?? family!;
            AddText(record, FieldKind.Name, only, item);
        }

        if (!hasAddressItem && directAddress.Count > 0)
        {
            AddressParts parts = PartsFrom(directAddress);
            if (!parts.IsEmpty)
            {
                TextRange hull = TextRange.None;
                bool hidden = item.Hidden;
                foreach (MarkupProperty p in directAddress)
                {
                    hull = TextRange.Hull(hull, p.Raw);
                    hidden |= p.Hidden;
                }

                record.Fields.Add(new FieldDraft
                {
                    Kind = FieldKind.Address,
                    Source = item.Source,
                    Text = parts.ToSingleLine(),
                    Raw = hull,
                    Hidden = hidden,
                    Composed = true,
                    Parts = parts,
                });
            }
        }
    }

    private static string OneLine(string text) => Collapse(text);

    private void AddText(RecordDraft record, FieldKind kind, MarkupProperty p, MarkupItem owner, string? label = null)
    {
        if (p.Text is null)
        {
            // A reference or nested item where text was expected: for a name or url, the item's own name or url.
            if (Resolve(p) is MarkupItem nested)
            {
                string wanted = kind == FieldKind.Website ? "url" : "name";
                foreach (MarkupProperty q in nested.Properties.Where(q => q.Name == wanted && q.Text is not null))
                {
                    AddText(record, kind, q, nested, label);
                }
            }

            return;
        }

        string text = kind == FieldKind.Address ? p.Text.Trim() : OneLine(p.Text);
        if (text.Length == 0)
        {
            return;
        }

        record.Fields.Add(new FieldDraft
        {
            Kind = kind,
            Source = owner.Source,
            Text = text,
            Raw = p.Raw,
            Label = label,
            Hidden = owner.Hidden || p.Hidden,
        });
    }

    // Returns true when the property gave an address (so loose address parts on the item are not read a second time).
    private bool AddAddress(RecordDraft record, MarkupProperty p, MarkupItem owner, int depth)
    {
        if (p.Text is not null)
        {
            string text = p.Text.Trim();
            if (text.Length == 0)
            {
                return false;
            }

            record.Fields.Add(new FieldDraft
            {
                Kind = FieldKind.Address,
                Source = owner.Source,
                Text = text,
                Raw = p.Raw,
                Hidden = owner.Hidden || p.Hidden,
            });
            return true;
        }

        if (depth > MaxDepth || Resolve(p) is not MarkupItem item || !Spend())
        {
            return false;
        }

        AddressParts parts = PartsFrom(item.Properties);
        if (parts.IsEmpty)
        {
            return false;
        }

        record.Fields.Add(new FieldDraft
        {
            Kind = FieldKind.Address,
            Source = item.Source,
            Text = parts.ToSingleLine(),
            Raw = item.Range,
            Hidden = owner.Hidden || item.Hidden || p.Hidden,
            Composed = true,
            Parts = parts,
        });
        return true;
    }

    private AddressParts PartsFrom(IEnumerable<MarkupProperty> props)
    {
        var parts = new AddressParts();
        foreach (MarkupProperty p in props)
        {
            string? text = p.Text is null ? NestedName(p) : p.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            switch (p.Name)
            {
                case "streetAddress":
                case "extendedAddress":
                    foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        parts.StreetLines.Add(line);
                    }

                    break;
                case "addressLocality":
                    parts.Locality ??= OneLine(text);
                    break;
                case "addressRegion":
                    parts.AdminArea ??= OneLine(text);
                    break;
                case "postalCode":
                    parts.PostalCode ??= OneLine(text);
                    break;
                case "addressCountry":
                    parts.CountryText ??= OneLine(text);
                    break;
            }
        }

        return parts;
    }

    // schema.org lets addressCountry be a Country item; its name (or code) is the text.
    private string? NestedName(MarkupProperty p)
    {
        if (Resolve(p) is MarkupItem country)
        {
            return country.Properties.FirstOrDefault(q => q.Name == "name" && q.Text is not null)?.Text;
        }

        return null;
    }

    // A ContactPoint or Place inside a record: its phone, email, url and address belong to the record. contactType labels them.
    private void MergeIn(RecordDraft record, MarkupProperty p, int depth)
    {
        if (depth > MaxDepth || Resolve(p) is not MarkupItem item || !Spend())
        {
            return;
        }

        string? label = item.Properties.FirstOrDefault(q => q.Name == "contactType" && q.Text is not null)?.Text;
        label = label is null ? null : OneLine(label);
        bool hasAddress = false;
        var direct = new List<MarkupProperty>();
        foreach (MarkupProperty q in item.Properties)
        {
            switch (q.Name)
            {
                case "telephone":
                    AddText(record, FieldKind.Phone, q, item, label);
                    break;
                case "email":
                    AddText(record, FieldKind.Email, q, item, label);
                    break;
                case "url":
                    AddText(record, FieldKind.Website, q, item, label);
                    break;
                case "address":
                    hasAddress |= AddAddress(record, q, item, depth);
                    break;
                case "contactPoint":
                case "contactPoints":
                case "location":
                    MergeIn(record, q, depth + 1);
                    break;
                case "streetAddress":
                case "addressLocality":
                case "addressRegion":
                case "postalCode":
                case "addressCountry":
                    direct.Add(q);
                    break;
            }
        }

        if (!hasAddress && direct.Count > 0)
        {
            AddressParts parts = PartsFrom(direct);
            if (!parts.IsEmpty)
            {
                record.Fields.Add(new FieldDraft
                {
                    Kind = FieldKind.Address,
                    Source = item.Source,
                    Text = parts.ToSingleLine(),
                    Raw = item.Range,
                    Hidden = item.Hidden,
                    Composed = true,
                    Parts = parts,
                });
            }
        }
    }

    private void WorksFor(RecordDraft person, MarkupProperty p, MarkupItem owner, int depth)
    {
        if (person.Kind != ContactRecordKind.Person)
        {
            if (Resolve(p) is MarkupItem other)
            {
                Visit(other, null, depth + 1);
            }

            return;
        }

        if (p.Text is not null)
        {
            string text = OneLine(p.Text);
            if (text.Length > 0)
            {
                person.Fields.Add(new FieldDraft { Kind = FieldKind.Organization, Source = owner.Source, Text = text, Raw = p.Raw, Hidden = owner.Hidden || p.Hidden });
            }

            return;
        }

        if (Resolve(p) is not MarkupItem item)
        {
            return;
        }

        RecordDraft? org = Visit(item, ContactRecordKind.Organization, depth + 1);
        if (org is null)
        {
            return;
        }

        person.Links.Add((RecordLinkKind.WorksFor, org));
        org.Links.Add((RecordLinkKind.HasMember, person));

        // The organization's name is also the person's organization field, at the name's own position.
        FieldDraft? nameDraft = org.Fields.FirstOrDefault(f => f.Kind == FieldKind.Name);
        if (nameDraft is not null)
        {
            person.Fields.Add(new FieldDraft
            {
                Kind = FieldKind.Organization,
                Source = nameDraft.Source,
                Text = nameDraft.Text,
                Raw = nameDraft.Raw,
                Hidden = nameDraft.Hidden,
            });
        }
    }

    private void LinkedRecord(RecordDraft owner, MarkupProperty p, ContactRecordKind expected, RecordLinkKind ownerToTarget, RecordLinkKind targetToOwner, int depth)
    {
        if (Resolve(p) is not MarkupItem item)
        {
            return;
        }

        RecordDraft? target = Visit(item, expected, depth + 1);
        if (target is null || ReferenceEquals(target, owner))
        {
            return;
        }

        owner.Links.Add((ownerToTarget, target));
        target.Links.Add((targetToOwner, owner));
    }
}
