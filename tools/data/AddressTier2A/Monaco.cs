namespace OneContact.Data.AddressTier2A;

/// <summary>
/// Monaco has no open address register, so its addresses are made here: the principality's well-known public streets (street names are
/// facts of the map, not a database), a number drawn from a plausible range per street, the single postcode 98000 and the locality
/// "Monaco". The quarter (Monte-Carlo, La Condamine, Fontvieille, Larvotto, Monaco-Ville) is the dependent locality some writers add.
/// The draw is a fixed hash of the record number, so the set is the same on every run.
/// </summary>
internal static class Monaco
{
    private static readonly (string Street, int MaxNumber, string Quarter)[] Streets =
    [
        ("Avenue de la Costa", 40, "Monte-Carlo"), ("Boulevard Princesse Charlotte", 40, "Monte-Carlo"), ("Avenue Princesse Grace", 50, "Larvotto"),
        ("Boulevard des Moulins", 40, "Monte-Carlo"), ("Avenue d'Ostende", 20, "Monte-Carlo"), ("Quai Antoine 1er", 20, "La Condamine"),
        ("Rue Grimaldi", 30, "La Condamine"), ("Avenue de Monte-Carlo", 30, "Monte-Carlo"), ("Place du Casino", 10, "Monte-Carlo"),
        ("Boulevard du Jardin Exotique", 30, "Monaco-Ville"), ("Rue Princesse Caroline", 30, "La Condamine"), ("Avenue de Fontvieille", 30, "Fontvieille"),
        ("Quai des États-Unis", 10, "Monaco-Ville"), ("Rue de la Turbie", 30, "La Condamine"), ("Boulevard Rainier III", 40, "La Condamine"),
        ("Avenue Saint-Michel", 15, "Monte-Carlo"), ("Boulevard d'Italie", 20, "Monte-Carlo"), ("Rue Basse", 20, "Monaco-Ville"),
        ("Rue Comte Félix Gastaldi", 20, "Monaco-Ville"), ("Rue Plati", 15, "La Condamine"), ("Rue Louis Notari", 15, "La Condamine"),
        ("Boulevard de Belgique", 20, "Monte-Carlo"), ("Boulevard Albert 1er", 20, "La Condamine"), ("Avenue Crovetto Frères", 15, "Fontvieille"),
        ("Avenue des Papalins", 20, "Fontvieille"), ("Avenue de Grande-Bretagne", 20, "Monte-Carlo"), ("Avenue du Port", 20, "La Condamine"),
        ("Rue Bellando de Castro", 15, "La Condamine"), ("Avenue de l'Annonciade", 20, "Monte-Carlo"), ("Boulevard de France", 30, "Monte-Carlo"),
        ("Rue des Orchidées", 15, "Monte-Carlo"), ("Avenue des Spélugues", 15, "Monte-Carlo"), ("Chemin des Pêcheurs", 20, "Fontvieille"),
        ("Rue du Portier", 10, "Monte-Carlo"), ("Rue de l'Église", 10, "La Condamine"), ("Avenue Prince Pierre", 15, "La Condamine"),
        ("Rue Terrazzani", 15, "La Condamine"), ("Quai Jean-Charles Rey", 20, "Fontvieille"), ("Avenue de Roqueville", 20, "Monte-Carlo"),
        ("Rue Émile de Loth", 15, "La Condamine"),
    ];

    /// <summary>How many records the synthesis yields.</summary>
    internal const int Count = 6000;

    internal static IEnumerable<Addr> Generate()
    {
        for (int i = 0; i < Count; i++)
        {
            ulong h = OneContact.Data.AddressUsCa.Prng.Hash("monaco:" + i, 0x4D43);
            (string street, int max, string quarter) = Streets[(int)(h % (ulong)Streets.Length)];
            int number = 1 + (int)((h >> 16) % (ulong)max);
            yield return new Addr
            {
                Id = "synthetic-mc:" + i,
                Country = "MC",
                Region = "MC",
                Language = "fr",
                Street = street,
                HouseNumber = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Locality = "Monaco",
                LocalityPostal = "MONACO",
                Place = (h >> 40) % 4 == 0 ? quarter : string.Empty,
                PostalCode = "98000",
            };
        }
    }
}
