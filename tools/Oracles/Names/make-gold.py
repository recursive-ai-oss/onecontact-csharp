#!/usr/bin/env python3
"""Writes testdata/names/gold.tsv: a synthetic set of person, several-people and company strings with gold labels per word.

Everything is invented: the given and family names are common words of each language combined at random by a seeded generator, so
no row is a real person. The generator is deterministic (seed 20261105). Columns:

  id, language, hint, kind, text, labels, has_form, elf

labels has one letter per white-space-separated word of text: T title, G given name, M middle name, F family name (particles and
second family names included), S suffix, N nickname, O other (a connector), B company base name, L legal form (and the rest of a
trading-as part is B too). has_form is 1 when the text has a legal form. elf is the ELF code of the form when it is unique in the
hinted country according to the ELF list, ? when it is not, and empty when there is no form.

usage: make-gold.py [path to the ELF CSV] > testdata/names/gold.tsv
"""
import csv
import os
import random
import re
import sys
import unicodedata

SEED = 20261105

# language: (hint, titles, given names, family names, particle words, and-word, company bases, legal forms)
# A hint is a country (two capitals) or a language (two lower-case letters), or both written CC/ll.
L = {
    "en": ("US", ["Mr", "Mrs", "Dr.", "Prof."], ["James", "Emily", "Robert", "Olivia", "Daniel", "Grace", "Peter", "Laura"], ["Harrison", "Fletcher", "Whitaker", "Lindqvist", "Brooks", "Thornton", "Pemberton", "Rowan"], ["van", "de"], "and", ["Northwind Trading", "Blue Harbor", "Silverline Systems", "Oakridge Consulting"], ["Inc", "LLC", "Corp."]),
    "fr": ("FR", ["M.", "Mme", "Dr", "Prof."], ["Pierre", "Camille", "Julien", "Chloé", "Mathieu", "Élodie", "Antoine", "Margaux"], ["Lefèvre", "Moreau", "Fontaine", "Bernard", "Garnier", "Rousseau", "Lambert", "Colin"], ["de la", "du", "de"], "et", ["Atelier du Port", "Boulangerie Soleil", "Groupe Horizon", "Maison Lumière"], ["SARL", "SAS", "SA"]),
    "es": ("ES", ["Sr.", "Sra.", "Dr.", "Dña."], ["Javier", "Lucía", "Alejandro", "Carmen", "Diego", "Marta", "Sergio", "Paula"], ["García", "Fernández", "Ruiz", "Navarro", "Molina", "Ortega", "Castillo", "Vidal"], ["de la", "del", "de"], "y", ["Talleres Norte", "Construcciones Levante", "Servicios Atlántico", "Estudio Mediterráneo"], ["S.L.", "S.A.", "SL"]),
    "pt": ("PT", ["Sr.", "Sra.", "Dr.", "Eng."], ["João", "Beatriz", "Tiago", "Inês", "Rui", "Mariana", "Duarte", "Filipa"], ["Silva", "Santos", "Ferreira", "Pereira", "Oliveira", "Costa", "Rodrigues", "Martins"], ["da", "dos", "de"], "e", ["Pastelaria Aurora", "Transportes Tejo", "Serra Azul", "Padaria Central"], ["Lda", "S.A.", "Lda."]),
    "de": ("DE", ["Herr", "Frau", "Dr.", "Prof. Dr."], ["Lukas", "Anna", "Jonas", "Katharina", "Felix", "Sabine", "Matthias", "Greta"], ["Müller", "Schneider", "Fischer", "Weber", "Hoffmann", "Becker", "Wagner", "Keller"], ["von", "zu", "von der"], "und", ["Nordwind Handel", "Rheinblick Bau", "Alpen Technik", "Sonnenhof Verlag"], ["GmbH", "AG", "GmbH & Co. KG", "UG"]),
    "nl": ("NL", ["Dhr.", "Mevr.", "Dr.", "Ir."], ["Jan", "Sanne", "Pieter", "Lotte", "Bram", "Fleur", "Joost", "Noor"], ["de Vries", "Jansen", "Bakker", "Visser", "Smit", "Meijer", "Mulder", "Bos"], ["van", "van der", "van den"], "en", ["Haven Logistiek", "Tulp Design", "Molen Advies", "Duin Techniek"], ["B.V.", "N.V.", "BV"]),
    "it": ("IT", ["Sig.", "Sig.ra", "Dott.", "Avv."], ["Marco", "Giulia", "Luca", "Chiara", "Matteo", "Elena", "Paolo", "Sara"], ["Rossi", "Bianchi", "Romano", "Colombo", "Ricci", "Marino", "Greco", "Conti"], ["di", "de", "del"], "e", ["Officine Adriatico", "Pasticceria Aurora", "Costruzioni Etna", "Studio Monte"], ["S.r.l.", "S.p.A.", "SRL"]),
    "sv": ("SE", ["Herr", "Fru", "Dr.", "Prof."], ["Erik", "Anna", "Johan", "Maja", "Oskar", "Elsa", "Lars", "Ingrid"], ["Andersson", "Lindberg", "Nyström", "Holmgren", "Berglund", "Sandström", "Ekström", "Forsberg"], ["af", "von"], "och", ["Nordic Bygg", "Fjord Konsult", "Sjö Teknik", "Björk Design"], ["AB", "HB", "Aktiebolag"]),
    "da": ("DK", ["Hr.", "Fru", "Dr.", "Prof."], ["Mads", "Freja", "Søren", "Ida", "Anders", "Signe", "Henrik", "Maja"], ["Jensen", "Nielsen", "Hansen", "Pedersen", "Andersen", "Christensen", "Larsen", "Sørensen"], ["von", "de"], "og", ["Havn Rådgivning", "Fjord Byg", "Nordlys Design", "Bølge Transport"], ["ApS", "A/S", "I/S"]),
    "no": ("NO", ["Hr.", "Fru", "Dr.", "Prof."], ["Ola", "Ingrid", "Kari", "Henrik", "Sigrid", "Magnus", "Astrid", "Tor"], ["Hansen", "Johansen", "Olsen", "Larsen", "Kristiansen", "Berg", "Haugen", "Eriksen"], ["von", "de"], "og", ["Fjellheim Bygg", "Nordkyst Rådgivning", "Havbris Transport", "Vik Design"], ["AS", "ASA", "ANS"]),
    "fi": ("FI", ["Dr.", "Prof.", "Herra", "Rouva"], ["Juha", "Aino", "Matti", "Helmi", "Eero", "Sanna", "Ville", "Kaisa"], ["Virtanen", "Korhonen", "Mäkinen", "Nieminen", "Heikkinen", "Laine", "Lehtonen", "Salo"], [], "ja", ["Järvi Rakennus", "Koivu Konsultointi", "Pohjola Tekniikka", "Aurinko Design"], ["Oy", "Ky", "Oyj"]),
    "is": ("IS", ["Dr.", "Prof.", "Hr.", "Frú"], ["Jón", "Guðrún", "Sigurður", "Helga", "Gunnar", "Anna", "Björn", "Katrín"], ["Jónsson", "Sigurðsson", "Guðmundsson", "Einarsson", "Magnússon", "Ólafsson", "Kristjánsson", "Haraldsson"], [], "og", ["Norðurljós Ráðgjöf", "Hafnar Smíði", "Jökull Hönnun", "Fjalla Flutningar"], ["ehf.", "hf.", "slf."]),
    "pl": ("PL", ["Pan", "Pani", "Dr", "Mgr"], ["Jan", "Anna", "Piotr", "Katarzyna", "Tomasz", "Magdalena", "Paweł", "Agnieszka"], ["Kowalski", "Nowak", "Wiśniewski", "Wójcik", "Kamiński", "Lewandowski", "Zieliński", "Szymański"], [], "i", ["Nowa Fala", "Biuro Wisła", "Polna Budowa", "Słoneczny Dom"], ["Sp. z o.o.", "S.A.", "sp.j."]),
    "cs": ("CZ", ["Pan", "Paní", "Ing.", "MUDr."], ["Jan", "Tereza", "Petr", "Eliška", "Jakub", "Lucie", "Martin", "Veronika"], ["Novák", "Svoboda", "Novotný", "Dvořák", "Černý", "Procházka", "Kučera", "Veselý"], [], "a", ["Zlatá Hora", "Dům Vltava", "Stavby Morava", "Technika Sever"], ["s.r.o.", "a.s.", "v.o.s."]),
    "sk": ("SK", ["Pan", "Pani", "Ing.", "Mgr."], ["Peter", "Zuzana", "Martin", "Lenka", "Michal", "Jana", "Tomáš", "Katarína"], ["Horváth", "Kováč", "Varga", "Tóth", "Nagy", "Baláž", "Szabó", "Molnár"], [], "a", ["Tatra Stavby", "Dunaj Servis", "Hron Technika", "Slnečný Dom"], ["s.r.o.", "a.s.", "k.s."]),
    "sl": ("SI", ["Dr.", "Prof.", "mag.", "Ing."], ["Luka", "Maja", "Matej", "Nina", "Andrej", "Eva", "Tomaž", "Ana"], ["Novak", "Horvat", "Kovačič", "Krajnc", "Zupančič", "Potočnik", "Mlakar", "Kos"], [], "in", ["Triglav Gradnja", "Soča Svetovanje", "Bled Design", "Kras Transport"], ["d.o.o.", "d.d.", "k.d."]),
    "hr": ("HR", ["Dr.", "Prof.", "mag.", "Ing."], ["Ivan", "Marija", "Luka", "Ana", "Marko", "Petra", "Josip", "Ivana"], ["Horvat", "Kovačević", "Babić", "Marić", "Jurić", "Novak", "Knežević", "Vuković"], [], "i", ["Jadran Gradnja", "Velebit Savjetovanje", "Zagorje Servis", "Kvarner Promet"], ["d.o.o.", "d.d.", "j.d.o.o."]),
    "hu": ("HU", ["Dr.", "Prof.", "ifj.", "id."], ["János", "Katalin", "István", "Zsuzsanna", "László", "Éva", "Gábor", "Ágnes"], ["Nagy", "Kovács", "Tóth", "Szabó", "Horváth", "Varga", "Kiss", "Molnár"], [], "és", ["Duna Építő", "Tisza Tanácsadó", "Balaton Szerviz", "Napfény Dizájn"], ["Kft.", "Zrt.", "Bt."]),
    "ro": ("RO", ["Dl.", "Dna", "Dr.", "Prof."], ["Andrei", "Elena", "Mihai", "Ioana", "Radu", "Maria", "Stefan", "Ana"], ["Popescu", "Ionescu", "Dumitrescu", "Stan", "Gheorghe", "Rusu", "Munteanu", "Matei"], [], "și", ["Carpați Construct", "Dunărea Servicii", "Delta Consultanță", "Soare Design"], ["S.R.L.", "S.A.", "SRL"]),
    "lv": ("LV", ["Dr.", "Prof.", "Mg.", "Kgs"], ["Jānis", "Anna", "Andris", "Liene", "Mārtiņš", "Ieva", "Edgars", "Laura"], ["Bērziņš", "Kalniņš", "Ozols", "Liepiņš", "Krūmiņš", "Eglītis", "Zariņš", "Pētersons"], [], "un", ["Daugava Būve", "Baltijas Konsultācijas", "Rīgas Serviss", "Saule Dizains"], ["SIA", "AS", "Sabiedrība ar ierobežotu atbildību"]),
    "lt": ("LT", ["Dr.", "Prof.", "Ponas", "Ponia"], ["Jonas", "Ona", "Tomas", "Rūta", "Mantas", "Gintarė", "Darius", "Lina"], ["Kazlauskas", "Petrauskas", "Jankauskas", "Butkus", "Paulauskas", "Stankevičius", "Urbonas", "Vasiliauskas"], [], "ir", ["Nemuno Statyba", "Baltijos Paslaugos", "Vilnios Dizainas", "Saulė Transportas"], ["UAB", "AB", "MB"]),
    "et": ("EE", ["Dr.", "Prof.", "Härra", "Proua"], ["Mart", "Kadri", "Tõnu", "Liis", "Andres", "Maarja", "Priit", "Triin"], ["Tamm", "Saar", "Sepp", "Mägi", "Kask", "Kukk", "Rebane", "Ilves"], [], "ja", ["Läänemere Ehitus", "Tallinna Nõustamine", "Päike Disain", "Soome Transport"], ["OÜ", "AS", "TÜ"]),
    "mt": ("MT", ["Sur", "Sinjura", "Dr.", "Perit"], ["Joseph", "Maria", "Mario", "Doris", "Karl", "Anna", "Charles", "Rita"], ["Borg", "Camilleri", "Vella", "Farrugia", "Zammit", "Galea", "Attard", "Spiteri"], [], "u", ["Gozo Konsulenza", "Valletta Bini", "Mdina Disinn", "Marsa Trasport"], ["Ltd", "plc", "Limited"]),
    "lb": ("LU", ["Här", "Madamm", "Dr.", "Prof."], ["Marc", "Anne", "Jean", "Sophie", "Paul", "Claire", "Luc", "Nadine"], ["Schmit", "Weber", "Hoffmann", "Wagner", "Thill", "Muller", "Reuter", "Klein"], ["de", "von"], "an", ["Atelier Alzette", "Moselle Conseil", "Eisch Technik", "Sauer Bau"], ["S.à r.l.", "S.A.", "SARL"]),
    "ga": ("IE/ga", ["Dr.", "Prof.", "An tUasal", "Bean"], ["Seán", "Aoife", "Pádraig", "Niamh", "Ciarán", "Siobhán", "Cormac", "Orla"], ["Murphy", "Kelly", "Byrne", "Walsh", "Doyle", "Brennan", "Gallagher", "Quinn"], [], "agus", ["Cuan Tógála", "Sliabh Comhairle", "Gaoth Dearg", "Tráigh Iompair"], ["Teoranta", "Ltd", "DAC"]),
    "ca": ("ES/ca", ["Sr.", "Sra.", "Dr.", "Prof."], ["Jordi", "Núria", "Pau", "Montserrat", "Marc", "Laia", "Oriol", "Mireia"], ["Puig", "Serra", "Vidal", "Ferrer", "Soler", "Pujol", "Roca", "Camps"], ["de", "de la"], "i", ["Obres Montseny", "Serveis Llevant", "Taller Ebre", "Disseny Maresme"], ["S.L.", "S.A.", "SL"]),
    "eu": ("ES/eu", ["Dr.", "Prof.", "Jauna", "Sr."], ["Iker", "Ainhoa", "Mikel", "Amaia", "Unai", "Nerea", "Aitor", "Leire"], ["Etxeberria", "Goikoetxea", "Urrutia", "Aguirre", "Mendizabal", "Zubiaurre", "Arregi", "Olano"], [], "eta", ["Ibaia Eraikuntza", "Mendi Aholkularitza", "Itsaso Garraioa", "Eguzki Diseinua"], ["S.L.", "S.A.", "SL"]),
    "gl": ("ES/gl", ["Sr.", "Sra.", "Dr.", "Prof."], ["Xoán", "Uxía", "Brais", "Noa", "Anxo", "Sabela", "Iago", "Carme"], ["Pereira", "Castro", "Varela", "Souto", "Rodríguez", "Lorenzo", "Freire", "Pazos"], ["de", "da"], "e", ["Obras Miño", "Servizos Ría", "Taller Costa", "Deseño Atlántico"], ["S.L.", "S.A.", "SL"]),
}

HISPANIC = {"es", "ca", "eu", "gl"}

# Second given names that are typical for the language (a middle name that is also a family name would be a guess).
SECOND_GIVEN = {
    "es": ["José", "María", "Carlos", "Luis"],
    "ca": ["Maria", "Josep", "Joan"],
    "eu": ["Maria", "Jose", "Mari"],
    "gl": ["María", "Xosé", "Manuel"],
}


def fold(s):
    s = unicodedata.normalize("NFKD", s.lower())
    s = "".join(c for c in s if not unicodedata.combining(c))
    return re.sub(r"[\W_]+", "", s)


def load_elf(path):
    index = {}
    with open(path, encoding="utf-8-sig", newline="") as f:
        for r in list(csv.reader(f))[1:]:
            code, country, status = r[0], r[2], r[12]
            if not country or not r[5]:
                continue
            for v in [r[5], r[8]] + r[9].split(";") + r[10].split(";"):
                if v.strip():
                    index.setdefault((fold(v), country), set()).add((code, status))
    return index


def elf_for(index, form, hint):
    country = hint.split("/")[0] if hint[:2].isupper() else ""
    if not country:
        return "?"
    hits = index.get((fold(form), country), set())
    active = {c for c, s in hits if s == "ACTV"} or {c for c, _ in hits}
    return next(iter(active)) if len(active) == 1 else "?"


def fl(text):
    return " ".join(["F"] * len(text.split()))


def label_words(text, label):
    return [label] * len(text.split())


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: make-gold.py <path to 2026-02-19-elf-code-list-v1.6.csv>")
    elf_path = sys.argv[1]
    index = load_elf(elf_path)
    rnd = random.Random(SEED)
    rows = []

    def add(lang, hint, kind, text, labels, form="", elf=""):
        assert len(text.split()) == len(labels.split()), (text, labels)
        rows.append((lang, hint, kind, text, labels, "1" if form else "0", elf))

    for lang, (hint, titles, givens, families, particles, conj, bases, forms) in L.items():
        g = lambda: rnd.choice(givens)
        f = lambda: rnd.choice(families)
        fam_first = lang == "hu"

        def gf(given, family):
            """Text and labels of a given name and a family name in the language's usual order."""
            if fam_first:
                return f"{family} {given}", fl(family) + " G"
            return f"{given} {family}", "G " + fl(family)

        a, b = g(), f()
        text, labels = gf(a, b)
        add(lang, hint, "person", text, labels)
        a, b = g(), f()
        add(lang, hint, "person", f"{b}, {a}", fl(b) + " G")
        t = rnd.choice(titles)
        a, b = g(), f()
        text, labels = gf(a, b)
        add(lang, hint, "person", f"{t} {text}", " ".join(label_words(t, "T")) + " " + labels)
        a, m, b = g(), rnd.choice(SECOND_GIVEN.get(lang, givens)), f()
        while m == a:
            m = g()
        if fam_first:
            add(lang, hint, "person", f"{b} {a} {m}", fl(b) + " G M")
        else:
            add(lang, hint, "person", f"{a} {m} {b}", "G M " + fl(b))
        a, b = g(), f()
        text, labels = gf(a, b)
        add(lang, hint, "person", text.upper(), labels)
        a, b = g(), f()
        text, labels = gf(a, b)
        add(lang, hint, "person", text.lower(), labels)
        a, b = g(), f()
        text, labels = gf(a, b)
        add(lang, hint, "person", f"{text} PhD", labels + " S")
        a, b = g(), f()
        text, labels = gf(a, b)
        add(lang, hint, "person", f"{text} Jr.", labels + " S")
        a, n, b = g(), g(), f()
        if fam_first:
            add(lang, hint, "person", f'{b} {a} "{n}"', fl(b) + " G N")
        else:
            add(lang, hint, "person", f'{a} "{n}" {b}', "G N " + fl(b))
        if particles:
            a = g()
            p = rnd.choice(particles)
            b = rnd.choice([x for x in families if " " not in x])
            add(lang, hint, "person", f"{a} {p} {b}", "G " + " ".join(["F"] * (len(p.split()) + 1)))
        if lang in HISPANIC:
            a, b, c = g(), f(), f()
            add(lang, hint, "person", f"{a} {b} {c}", "G F F")
        if lang in ("es", "ca"):
            a, b, c = g(), f(), f()
            add(lang, hint, "person", f"{a} {b} {conj} {c}", "G F O F")
        # several people
        a1, a2, b = g(), g(), f()
        while a2 == a1:
            a2 = g()
        if not fam_first:
            add(lang, hint, "multiple", f"{a1} {conj} {a2} {b}", "G O G " + fl(b))
        a1, b1, a2, b2 = g(), f(), g(), f()
        t1, lab1 = gf(a1, b1)
        t2, lab2 = gf(a2, b2)
        add(lang, hint, "multiple", f"{t1} {conj} {t2}", lab1 + " O " + lab2)
        t1, t2 = titles[0], titles[1]
        a, b = g(), f()
        text, labels = gf(a, b)
        add(lang, hint, "multiple", f"{t1} {conj} {t2} {text}", " ".join(label_words(t1, "T")) + " O " + " ".join(label_words(t2, "T")) + " " + labels)
        # companies
        for i in range(3):
            base = rnd.choice(bases)
            form = forms[i % len(forms)]
            if form in ("SIA", "UAB", "AB") and lang in ("lv", "lt"):
                text = f"{form} {base}"
                labels = " ".join(label_words(form, "L") + label_words(base, "B"))
            else:
                text = f"{base} {form}"
                labels = " ".join(label_words(base, "B") + label_words(form, "L"))
            add(lang, hint, "company", text, labels, form, elf_for(index, form, hint))
        base = rnd.choice(bases)
        add(lang, hint, "company", base.upper() + " " + forms[0].upper(), " ".join(label_words(base, "B") + label_words(forms[0], "L")), forms[0], elf_for(index, forms[0], hint))
        # a company with a company word and no legal form
        base = rnd.choice(bases)
        add(lang, hint, "company", base, " ".join(label_words(base, "B")))
        # trading as
        b1, b2 = rnd.choice(bases), rnd.choice(bases)
        add(lang, hint, "company", f"{b1} {forms[0]} t/a {b2}", " ".join(label_words(b1, "B") + label_words(forms[0], "L") + ["O"] + label_words(b2, "B")), forms[0], elf_for(index, forms[0], hint))

    sys.stdout.write("id\tlanguage\thint\tkind\ttext\tlabels\thas_form\telf\n")
    for i, r in enumerate(rows, 1):
        sys.stdout.write(f"{i:04d}\t" + "\t".join(r) + "\n")


if __name__ == "__main__":
    main()
