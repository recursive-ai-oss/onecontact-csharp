#!/usr/bin/env python3
"""Generates testdata/addresses/parse-gold.tsv: synthetic United States and Canadian free-text addresses with gold fields.

Every address is assembled from the word lists below with a fixed seed, so the file is reproducible:
    python3 tools/gold/make-address-gold.py testdata/addresses/parse-gold.tsv
No real person's data is used; streets, cities and numbers are drawn from small invented or generic lists.

Columns: id, country, text (a backslash-n is a line break), gold fields as key=value pairs separated by ';'.
Gold keys: house_number, pre_directional, street_name, street_type, post_directional, unit, box, locality, state, postal.
Gold values are in the postal style (upper case, the authority's abbreviations), the form the parser's standard text takes.
"""
import random
import sys

SEED = 20261105
rng = random.Random(SEED)

STREET_NAMES = ["Maple", "Oak", "Cedar", "Elm", "Washington", "Lincoln", "Sunset", "Highland", "Pine Ridge", "Mill", "Church", "Spring", "Willow",
                "Hickory", "Chestnut", "Lake View", "Franklin", "Jefferson", "Magnolia", "Meadow Brook", "Orchard", "Walnut", "Birch", "Dogwood", "Cypress",
                "5th", "42nd", "3rd", "21st", "Main", "Broad", "Center", "Academy", "Bridge", "Hillcrest", "Forest Glen", "Sycamore", "Aspen", "Heritage", "Liberty"]
TYPES = [("Street", "ST"), ("St", "ST"), ("Avenue", "AVE"), ("Ave", "AVE"), ("Road", "RD"), ("Rd", "RD"), ("Drive", "DR"), ("Dr", "DR"), ("Lane", "LN"),
         ("Boulevard", "BLVD"), ("Blvd", "BLVD"), ("Court", "CT"), ("Ct", "CT"), ("Place", "PL"), ("Way", "WAY"), ("Circle", "CIR"), ("Terrace", "TER"),
         ("Parkway", "PKWY"), ("Pkwy", "PKWY")]
DIRS = [("N", "N"), ("North", "N"), ("S", "S"), ("South", "S"), ("E", "E"), ("East", "E"), ("W", "W"), ("West", "W"), ("NE", "NE"), ("SW", "SW"), ("Northwest", "NW")]
US_CITIES = ["Springfield", "Riverton", "Fairview", "Franklin", "Greenville", "Madison", "Clinton", "Salem", "Georgetown", "Arlington", "Ashland", "Oxford",
             "Burlington", "Manchester", "Kingston", "Dover", "Auburn", "Milton", "Newport", "Lexington"]
US_STATES = [("AL", "Alabama"), ("AZ", "Arizona"), ("CA", "California"), ("CO", "Colorado"), ("FL", "Florida"), ("GA", "Georgia"), ("IL", "Illinois"),
             ("MA", "Massachusetts"), ("MI", "Michigan"), ("MN", "Minnesota"), ("NC", "North Carolina"), ("NJ", "New Jersey"), ("NY", "New York"), ("OH", "Ohio"),
             ("OR", "Oregon"), ("PA", "Pennsylvania"), ("TX", "Texas"), ("VA", "Virginia"), ("WA", "Washington"), ("WI", "Wisconsin")]
UNITS = [("Apt", "APT"), ("Apartment", "APT"), ("Suite", "STE"), ("Ste", "STE"), ("Unit", "UNIT"), ("#", "#"), ("Room", "RM"), ("Floor", "FL"), ("Bldg", "BLDG")]
CA_CITIES = ["Toronto", "Calgary", "Vancouver", "Ottawa", "Winnipeg", "Halifax", "Regina", "Edmonton", "Hamilton", "Victoria", "London", "Kitchener",
             "Saskatoon", "Moncton", "Oshawa", "Barrie", "Kelowna", "Guelph"]
CA_PROVS = [("ON", "Ontario", "MKLNP"), ("QC", "Quebec", "GHJ"), ("BC", "British Columbia", "V"), ("AB", "Alberta", "T"), ("MB", "Manitoba", "R"),
            ("SK", "Saskatchewan", "S"), ("NS", "Nova Scotia", "B"), ("NB", "New Brunswick", "E"), ("NL", "Newfoundland and Labrador", "A"), ("PE", "Prince Edward Island", "C")]
FR_CITIES = ["Montréal", "Québec", "Laval", "Gatineau", "Sherbrooke", "Longueuil"]
FR_STREETS = ["Principale", "Saint-Denis", "des Pins", "du Parc", "de la Gare", "Champlain", "Sherbrooke", "Laurier", "Notre-Dame", "des Érables"]
FR_TYPES = [("Rue", "RUE"), ("Boulevard", "BOUL"), ("Boul", "BOUL"), ("Avenue", "AV"), ("Chemin", "CH"), ("Av", "AV"), ("Impasse", "IMP")]
CA_LETTERS = "ABCEGHJKLMNPRSTVXY"
CA_LETTERS2 = "ABCEGHJKLMNPRSTVWXYZ"
RECIPIENTS = ["Pat Morgan", "Alex Rivera", "Jordan Lee", "Sam Taylor", "Casey Brooks", "ABC Supply Co", "Northwind Traders", "Attn: Receiving", "Dana Whitfield", "Riley Chen"]

rows = []


def case(s, mode):
    return s.lower() if mode == 0 else (s.upper() if mode == 1 else s)


def pick(lst):
    return lst[rng.randrange(len(lst))]


def street_us(short_post=False):
    num = str(rng.randrange(1, 9999))
    fields = {"house_number": num}
    parts = [num]
    if rng.random() < 0.15:
        frac = "1/2"
        parts.append(frac)
    pre = None
    if rng.random() < 0.25:
        pre = pick(DIRS)
        parts.append(pre[0])
        fields["pre_directional"] = pre[1]
    name = pick(STREET_NAMES)
    parts.append(name)
    fields["street_name"] = name.upper()
    t = pick(TYPES)
    parts.append(t[0])
    fields["street_type"] = t[1]
    if pre is None and rng.random() < 0.15:
        post = pick([d for d in DIRS if len(d[0]) <= 2] if short_post else DIRS)
        parts.append(post[0])
        fields["post_directional"] = post[1]
    unit_text = None
    if rng.random() < 0.3:
        u = pick(UNITS)
        v = str(rng.randrange(1, 500)) + (pick("ABCD") if rng.random() < 0.3 else "")
        unit_text = (u[0] + v) if u[0] == "#" and rng.random() < 0.5 else (u[0] + " " + v)
        fields["unit"] = (u[1] + " " + v).upper()
    return " ".join(parts), unit_text, fields


def last_us():
    city = pick(US_CITIES)
    st = pick(US_STATES)
    zip5 = "%05d" % rng.randrange(1000, 99999)
    zipfull = zip5 + ("-%04d" % rng.randrange(1, 9999) if rng.random() < 0.3 else "")
    state_text = st[1] if rng.random() < 0.2 else st[0]
    return city, st[0], zipfull, state_text


def emit(cid, country, lines_or_text, fields):
    text = lines_or_text
    gold = ";".join(f"{k}={v}" for k, v in fields.items())
    rows.append((cid, country, text.replace("\n", "\\n"), gold))


n = 0
# --- US street addresses in three layouts
for i in range(170):
    street, unit, f = street_us(short_post=(i % 3 == 2))
    city, st, zipf, state_text = last_us()
    fields = dict(f)
    fields.update({"locality": city.upper(), "state": st, "postal": zipf})
    layout = i % 3
    mode = rng.randrange(3)
    lead = []
    if layout == 0 and rng.random() < 0.3:
        lead = [pick(RECIPIENTS)]
    if layout == 0:
        lines = lead + [street + (" " + unit if unit and rng.random() < 0.7 else "")] + [f"{city}, {state_text} {zipf}"]
        if unit and (street + " " + unit) not in lines[len(lead)]:
            lines = lead + [unit, street, f"{city}, {state_text} {zipf}"]
        text = "\n".join(case(l, mode) if mode != 2 else l for l in lines)
    elif layout == 1:
        parts = [street] + ([unit] if unit and rng.random() < 0.5 else []) + [city, f"{state_text} {zipf}"]
        if unit and unit not in parts:
            parts[0] = street + " " + unit
        text = ", ".join(case(p, mode) if mode != 2 else p for p in parts)
    else:
        text = case(" ".join([street] + ([unit] if unit else []) + [city, state_text, zipf]), mode) if mode != 2 else " ".join([street] + ([unit] if unit else []) + [city, state_text, zipf])
    n += 1
    emit(f"us-street-{n:03d}", "US", text, fields)

# --- US PO boxes, rural routes, highway contract, general delivery, military, Puerto Rico
for i in range(30):
    city, st, zipf, state_text = last_us()
    kind = i % 5
    fields = {"locality": city.upper(), "state": st, "postal": zipf}
    if kind == 0:
        b = str(rng.randrange(1, 99999))
        line = pick(["PO Box ", "P.O. Box ", "Post Office Box ", "POB "]) + b if False else pick(["PO Box ", "P.O. Box ", "Post Office Box "]) + b
        fields["box"] = "PO BOX " + b
    elif kind == 1:
        r, b = str(rng.randrange(1, 12)), str(rng.randrange(1, 400))
        line = pick(["RR ", "Rural Route ", "RR #"]) + r + " Box " + b
        fields["box"] = f"RR {r} BOX {b}"
    elif kind == 2:
        r, b = str(rng.randrange(1, 90)), str(rng.randrange(1, 400))
        line = pick(["HC ", "Highway Contract Route ", "Star Route "]) + r + " Box " + b
        fields["box"] = f"HC {r} BOX {b}"
    elif kind == 3:
        line = "General Delivery"
        fields["box"] = "GENERAL DELIVERY"
    else:
        line = f"PSC {rng.randrange(1, 9999)} Box {rng.randrange(1, 9999)}"
        city, st = pick(["APO", "FPO"]), pick(["AE", "AP", "AA"])
        zipf = "%05d" % rng.randrange(96000, 99999) if st != "AE" else "%05d" % rng.randrange(9000, 9999)
        fields = {"box": line.upper(), "locality": city, "state": st, "postal": zipf}
    mode = rng.randrange(3)
    layout = i % 2
    last = f"{city}, {st} {zipf}" if layout == 0 else f"{city} {st} {zipf}"
    text = (line + "\n" + last) if layout == 0 else (line + ", " + last)
    text = case(text, mode) if mode != 2 else text
    n += 1
    emit(f"us-box-{n:03d}", "US", text, fields)

pr_streets = [("1234", "CALLE", "AURORA"), ("150", "CALLE", "A"), ("1510", "CALLE", "3"), ("77", "AVE", "WILSON CHURCHILL")]
for i in range(12):
    num, t, name = pr_streets[i % len(pr_streets)]
    city = pick(["SAN JUAN", "PONCE", "CAROLINA", "BAYAMON", "MAYAGUEZ"])
    zipf = "00%03d" % rng.randrange(600, 999)
    fields = {"house_number": num, "street_name": f"{t} {name}" if t == "CALLE" else name, "locality": city, "state": "PR", "postal": zipf}
    if t == "AVE":
        fields["street_name"] = "WILSON CHURCHILL"
        fields["pre_directional"] = ""
        del fields["pre_directional"]
        fields["street_name"] = "AVE WILSON CHURCHILL"
    text = f"{num} {t.title()} {name.title()}, {city.title()}, PR {zipf}"
    n += 1
    emit(f"us-pr-{n:03d}", "US", text, fields)

# --- Canada
for i in range(100):
    ps = pick(CA_PROVS)
    first = pick(ps[2])
    city = pick(CA_CITIES)
    fsa = first + str(rng.randrange(0, 10)) + pick(CA_LETTERS2.replace("D", "").replace("F", "").replace("I", "").replace("O", "").replace("Q", "").replace("U", "").replace("W", "").replace("Z", ""))
    ldu = str(rng.randrange(0, 10)) + pick("ABCEGHJKLMNPRSTVXYZ") + str(rng.randrange(0, 10))
    postal = fsa + " " + ldu
    kind = i % 10
    fields = {"locality": city.upper(), "state": ps[0], "postal": postal}
    prov_text = ps[1] if rng.random() < 0.15 else ps[0]
    mode = rng.randrange(3)
    num = str(rng.randrange(1, 9999))
    if kind in (0, 1, 2, 3):
        name = pick(STREET_NAMES)
        t = pick([("Street", "ST"), ("St", "ST"), ("Avenue", "AVE"), ("Ave", "AVE"), ("Road", "RD"), ("Rd", "RD"), ("Drive", "DR"), ("Crescent", "CRES"), ("Boulevard", "BLVD"), ("Court", "CRT"), ("Way", "WAY")])
        street = f"{num} {name} {t[0]}"
        fields.update({"house_number": num, "street_name": name.upper(), "street_type": t[1]})
        if kind == 1:
            unit = str(rng.randrange(1, 300))
            street = f"{unit}-{street}"
            fields["unit"] = f"UNIT {unit}"
            # a unit before the civic number is recorded as the unit prefix
        if kind == 2:
            d = pick([("NW", "NW"), ("SE", "SE"), ("E", "E"), ("W", "W"), ("N", "N")])
            street += " " + d[0]
            fields["post_directional"] = d[1]
        if kind == 3:
            u = pick([("Apt", "APT"), ("Apartment", "APT"), ("Suite", "SUITE"), ("Unit", "UNIT")])
            v = str(rng.randrange(1, 300))
            street += f" {u[0]} {v}"
            fields["unit"] = f"{u[1]} {v}"
    elif kind in (4, 5):
        name = pick(FR_STREETS)
        t = pick(FR_TYPES)
        street = f"{num} {t[0]} {name}"
        city = pick(FR_CITIES)
        ps = ("QC", "Québec", "GHJ")
        prov_text = "QC" if rng.random() < 0.8 else "Québec"
        fsa = pick("GHJ") + str(rng.randrange(0, 10)) + pick("ABCEGHJKLMNPRSTV")
        postal = fsa + " " + str(rng.randrange(0, 10)) + pick("ABCEGHJKLMNPRSTV") + str(rng.randrange(0, 10))
        fields = {"house_number": num, "street_name": name.upper(), "street_type": t[1], "locality": city.upper(), "state": "QC", "postal": postal}
    elif kind == 6:
        b = str(rng.randrange(1, 9999))
        street = f"PO Box {b} Stn {pick('ABCDE')}"
        fields = {"box": street.upper().replace("STN ", "STN "), "locality": city.upper(), "state": ps[0], "postal": postal}
    elif kind == 7:
        r = str(rng.randrange(1, 9))
        street = f"RR {r} Stn Main"
        fields = {"box": f"RR {r} STN MAIN", "locality": city.upper(), "state": ps[0], "postal": postal}
    elif kind == 8:
        street = "GD Stn A"
        fields = {"box": "GD STN A", "locality": city.upper(), "state": ps[0], "postal": postal}
    else:
        name = pick(STREET_NAMES)
        street = f"{num}A {name} St"
        fields.update({"house_number": num + "A", "street_name": name.upper(), "street_type": "ST"})
    layout = i % 3
    if layout == 0:
        text = street + "\n" + f"{city} {prov_text}  {postal}"
    elif layout == 1:
        text = f"{street}, {city}, {prov_text} {postal}"
    else:
        text = f"{street} {city} {prov_text} {postal}" if kind not in (4, 5) else f"{street}, {city} {prov_text} {postal}"
    text = case(text, mode) if mode != 2 else text
    n += 1
    emit(f"ca-{n:03d}", "CA", text, fields)

with open(sys.argv[1], "w", encoding="utf-8") as f:
    f.write("# Synthetic United States and Canadian free-text addresses with gold fields, generated by tools/gold/make-address-gold.py (seed %d).\n" % SEED)
    f.write("# id <TAB> country <TAB> text (backslash-n is a line break) <TAB> gold fields. No real person's data.\n")
    for r in rows:
        f.write("\t".join(r) + "\n")
print(len(rows))
