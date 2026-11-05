#!/usr/bin/env python3
"""Step 3: finds address and name strings in the stored page lines and draws a diverse sample of each, as drafts to be labelled.

usage: extract-candidates.py <out-dir> <seed> <addresses-per-country> <persons-per-country> <companies-per-country>

Reads <out-dir>/pages.jsonl; writes <out-dir>/draft-address-{US,CA}.jsonl and <out-dir>/draft-name-{US,CA}.jsonl. Strings are kept verbatim,
in the order and with the line breaks of the page. A page counts as US or Canadian only when it holds a US ZIP-with-state or a
Canadian postal code with a province; names from pages without such an address are not used. Prints only counts.
"""
import json
import os
import random
import re
import sys
from collections import defaultdict

out, seed = sys.argv[1], int(sys.argv[2])
n_addr, n_person, n_company = int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
rng = random.Random(seed)
# Optional: EXCLUDE=file1,file2 skips strings already drafted (their "text" values); PERSON_PER_HOST sets the cap per host for persons.
EXCLUDED = set()
for _f in filter(None, os.environ.get("EXCLUDE", "").split(",")):
    for _l in open(_f, encoding="utf-8"):
        EXCLUDED.add(re.sub(r"\W+", "", json.loads(_l)["text"].lower()))
PERSON_PER_HOST = int(os.environ.get("PERSON_PER_HOST", "2"))

US_ST = "AL AK AZ AR CA CO CT DE DC FL GA HI ID IL IN IA KS KY LA ME MD MA MI MN MS MO MT NE NV NH NJ NM NY NC ND OH OK OR PA RI SC SD TN TX UT VT VA WA WV WI WY PR GU VI AS MP".split()
US_NAMES = ["Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois",
            "Indiana", "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", "Minnesota", "Mississippi", "Missouri", "Montana",
            "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico", "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania",
            "Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont", "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming"]
CA_PR = "AB BC MB NB NL NS NT NU ON PE QC SK YT".split()
CA_NAMES = ["Alberta", "British Columbia", "Manitoba", "New Brunswick", "Newfoundland and Labrador", "Newfoundland", "Nova Scotia", "Northwest Territories", "Nunavut",
            "Ontario", "Prince Edward Island", "Quebec", "Québec", "Saskatchewan", "Yukon", "P.E.I.", "Que."]
US_RE = re.compile(r"\b(?:%s)\b\.?,?\s+\d{5}(?:-\d{4})?\b" % "|".join(US_ST + US_NAMES))
CA_RE = re.compile(r"\b(?:%s|[A-Z]\.?[A-Z]\.?)\b\.?,?\s*[ABCEGHJ-NPRSTVXY]\d[ABCEGHJ-NPRSTV-Z][ \-]?\d[ABCEGHJ-NPRSTV-Z]\d\b" % "|".join(CA_PR + CA_NAMES))
STREETNUM = re.compile(r"(?:^|[\s,;:|])(\d{1,6}[A-Za-z]?(?:-\d{1,6})?(?:\s+\d/\d)?\s+(?:[A-Za-z0-9#'\.\-]+))")
BOX = re.compile(r"\b(?:P\.?\s?O\.?\s*Box|Post Office Box|Box\s+\d|R\.?R\.?\s*#?\s*\d|C\.?P\.?\s+\d|Case postale|Rural Route|HC\s?\d|General Delivery)\b", re.I)
LABEL = re.compile(r"^(?:address|our address|mailing address|physical address|location|visit us|find us|adresse|office|head ?office|main office|where)\s*[:\-–]\s*", re.I)
COUNTRY_LINE = re.compile(r"^(?:USA|U\.S\.A\.?|US|United States(?: of America)?|Canada)\s*$", re.I)
UNIT = re.compile(r"\b(?:suite|ste\.?|apt\.?|apartment|unit|#\s?\d|floor|fl\.?|bldg|building|room|rm\.?|bureau|local|app\.?|étage)\b", re.I)
FRENCH = re.compile(r"\b(?:rue|boul(?:evard)?|avenue|chemin|montr[eé]al|qu[eé]bec|succ\.?|bureau|app\.?|case postale|des|du|de la)\b", re.I)
DIRECTION = re.compile(r"\b(?:N|S|E|W|NE|NW|SE|SW|North|South|East|West|Nord|Sud|Est|Ouest)\b\.?")

pages = [json.loads(l) for l in open(os.path.join(out, "pages.jsonl"), encoding="utf-8")]


def page_country(p):
    us = ca = 0
    for ln in p["lines"]:
        if len(ln) > 250:
            continue
        if US_RE.search(ln):
            us += 1
        if CA_RE.search(ln):
            ca += 1
    if p["tld"] == "ca" and ca:
        return "CA"
    if ca > us:
        return "CA"
    if us:
        return "US"
    return None


def clip_line(line, cc):
    """Trims text after the postal code (and optional country) and a leading label, keeping the rest as written."""
    rx = US_RE if cc == "US" else CA_RE
    m = None
    for m in rx.finditer(line):
        pass
    end = m.end() if m else len(line)
    tail = re.match(r"\s*,?\s*(?:USA|U\.S\.A\.?|US|United States|Canada)\b", line[end:], re.I)
    if tail:
        end += tail.end()
    s = line[:end]
    s = LABEL.sub("", s)
    return s.strip(" ,;|-–•")


def addresses(p, cc):
    rx = US_RE if cc == "US" else CA_RE
    L = p["lines"]
    res = []
    for k, ln in enumerate(L):
        if len(ln) > 300 or not rx.search(ln):
            continue
        cur = clip_line(ln, cc)
        # Street part inside the same line?
        first = rx.search(cur)
        head = cur[:first.start()] if first else cur
        has_street = bool(STREETNUM.search(head)) or bool(BOX.search(head))
        lines = [cur]
        if has_street:
            m = STREETNUM.search(head)
            if m and m.start() > 0 and len(head) > 100:
                lines = [cur[m.start(1):]]
        else:
            j = k - 1
            added = 0
            while j >= 0 and added < 3 and len(L[j]) <= 90:
                prev = LABEL.sub("", L[j]).strip(" ,;|")
                if not prev:
                    break
                lines.insert(0, prev)
                added += 1
                if STREETNUM.search(" " + prev) and re.match(r"^\d", prev) or BOX.search(prev):
                    break
                j -= 1
            if not (STREETNUM.search(" " + " ".join(lines[:-1])) or BOX.search(" ".join(lines[:-1]))):
                continue
        nxt = L[k + 1] if k + 1 < len(L) else ""
        if COUNTRY_LINE.match(nxt):
            lines.append(nxt)
        text = "\n".join(lines)
        if 8 <= len(text) <= 220:
            res.append(text)
    return res


def strata(text, cc):
    s = []
    if BOX.search(text):
        s.append("box")
    if UNIT.search(text):
        s.append("unit")
    if "\n" in text:
        s.append("multiline")
    else:
        s.append("oneline")
    if text.upper() == text and re.search(r"[A-Z]{3}", text):
        s.append("upper")
    if DIRECTION.search(text):
        s.append("direction")
    if re.search(r"\d+-\d+", text):
        s.append("hyphen")
    if cc == "CA" and FRENCH.search(text):
        s.append("french")
    if re.search(r"\b(?:Route|Rte|Hwy|Highway|County Road|CR|RR|Rang|Rural)\b", text, re.I):
        s.append("rural")
    return s or ["plain"]


by_country = {"US": [], "CA": []}
name_pages = {"US": [], "CA": []}
for p in pages:
    cc = page_country(p)
    if not cc:
        continue
    name_pages[cc].append(p)
    for t in addresses(p, cc):
        by_country[cc].append({"text": t, "host": p["host"], "warc": p["warc"], "url_kind": "root" if re.match(r"https?://[^/]+/?$", p["url"]) else "inner",
                               "strata": strata(t, cc)})


def stratified(items, n, key_fn, per_host=2):
    seen = set()
    uniq = []
    rng.shuffle(items)
    host_count = defaultdict(int)
    for it in items:
        k = re.sub(r"\W+", "", it["text"].lower())
        if k in seen or k in EXCLUDED or host_count[it["host"]] >= per_host:
            continue
        seen.add(k)
        host_count[it["host"]] += 1
        uniq.append(it)
    buckets = defaultdict(list)
    for it in uniq:
        # an item goes to its rarest stratum
        buckets[key_fn(it)].append(it)
    order = sorted(buckets, key=lambda b: len(buckets[b]))
    chosen = []
    while len(chosen) < n and any(buckets[b] for b in order):
        for b in order:
            if buckets[b] and len(chosen) < n:
                chosen.append(buckets[b].pop())
    return chosen


def rarest(counts):
    def fn(it):
        return min(it["strata"], key=lambda s: counts.get(s, 0))
    return fn


stats = {}
for cc in ("US", "CA"):
    counts = defaultdict(int)
    for it in by_country[cc]:
        for s in it["strata"]:
            counts[s] += 1
    ch = stratified(by_country[cc], n_addr, rarest(counts))
    with open(os.path.join(out, f"draft-address-{cc}.jsonl"), "w", encoding="utf-8") as f:
        for i, it in enumerate(ch):
            f.write(json.dumps({"id": f"addr-{cc}-{i:03d}", "country": cc, **it}, ensure_ascii=False) + "\n")
    stats[f"address {cc}"] = (len(by_country[cc]), len(ch), {k: v for k, v in counts.items()})

# ---------- names ----------
PART = r"(?:van|von|de|del|della|da|di|du|des|der|den|la|le|bin|ibn|al|el|mac|mc|st\.?|ter|ten|y|e)"
CAPW = r"(?:[A-Z][a-zA-Z'’\-]+\.?|[A-Z]\.|[A-Z][a-z]+(?:-[A-Z][a-z]+)+)"
TITLE = r"(?:Dr|Mr|Mrs|Ms|Miss|Mme|Mlle|M|Me|Prof|Professor|Rev|Reverend|Hon|Fr|Father|Sr|Sister|Pastor|Rabbi|Judge|Capt|Col|Sgt|Lt|Dre|Dr\.|Sen|Rep)\.?"
CRED = r"(?:MD|DDS|DMD|DO|PhD|Ph\.D\.|JD|Esq\.?|CPA|RN|NP|PA-C|DVM|OD|DC|PE|MBA|CFP|CPA|LLM|MSW|LCSW|FACS|CPC|CFA|Jr\.?|Sr\.?|II|III|IV)"
PERSON = re.compile(rf"^(?:{TITLE}\s+)?{CAPW}(?:\s+(?:{PART}\s+)*{CAPW}){{1,4}}(?:,?\s+{CRED})*(?:,\s*{CRED})*$")
MULTI = re.compile(rf"^(?:{TITLE}\s+)?{CAPW}(?:\s+{CAPW})?\s+(?:and|&|et)\s+(?:{TITLE}\s+)?{CAPW}(?:\s+{CAPW}){{0,2}}$")
STOP = set("""home about contact us services service our team news blog privacy policy terms login search menu skip content call now get free quote request learn view all click here location
locations hours monday tuesday wednesday thursday friday saturday sunday company inc llc ltd read more meet staff careers join shop cart checkout account sign register faq faqs gallery
resources events donate give support help sitemap copyright rights reserved follow share facebook twitter instagram linkedin youtube email phone fax street road avenue suite drive
welcome new used customer reviews testimonials quick links products specials offers financing schedule appointment book online patient patients forms insurance payment directions
north south east west first second third last next previous page submit send message name general information board directors members officers committee committees department
departments office offices city county state united states america canada contacts hours open closed today tomorrow""".split())
ROLE = re.compile(r"\b(?:president|owner|director|manager|attorney|partner|ceo|cfo|coo|cto|realtor|agent|broker|principal|associate|hygienist|nurse|teacher|coach|pastor|founder|vice|chief|counsel|"
                  r"dentist|physician|therapist|instructor|officer|administrator|coordinator|specialist|technician|supervisor|lawyer|esq|dds|md|cpa|treasurer|secretary|chair|chairman|chairwoman|"
                  r"superintendent|principal|professor|doctor|veterinarian|advisor|consultant|planner|designer|architect|engineer|stylist|barber|mechanic|sales|realty|présidente?|directeur|directrice|avocat|avocate|notaire|"
                  r"dentiste|médecin|propriétaire|conseill(?:er|ère)|courtier|gestionnaire|adjoint|adjointe)\b", re.I)


def person_ok(line):
    if len(line) > 60 or re.search(r"\d|@|http|www\.", line):
        return False
    words = re.findall(r"[A-Za-zÀ-ÿ'’\-\.]+", line)
    if len(words) < 2:
        return False
    low = [w.lower().strip(".,") for w in words]
    if any(w in STOP for w in low):
        return False
    return True


FORM = re.compile(r"(?:\b(?:Inc|LLC|L\.L\.C|Ltd|Limited|Corp|Corporation|Co|Company|LLP|LP|PLLC|PC|P\.C|PA|P\.A|Incorporated|Associates|Group|Partners|Ltée|Limitée|Enr|S\.E\.N\.C|SENC|SENCRL|Cie|Fondation|Foundation|Services|Bank|Association|Society)\b\.?)\s*$", re.I)
LEGAL = re.compile(r"\b(?:Inc|LLC|L\.L\.C|Ltd|Limited|Corp|Corporation|LLP|LP|PLLC|P\.C|Incorporated|Ltée|Limitée|S\.E\.N\.C|SENC|SENCRL|Enr|GP|Co)\b\.?", re.I)
COPY = re.compile(r"(?:©|\(c\)|&copy;|Copyright)\s*(?:\d{4}(?:\s*[-–]\s*\d{4})?)?[\s,:.\-–]*(.+)", re.I)


def clean_company(s):
    s = re.split(r"\s*(?:\.\s+All|\s+All rights|\s*\|\s|\s+Powered by|\s+Designed by|\s+Website|\s+Privacy|\s+Terms|\s+Site by|\s+Tous droits|\s+—\s)", s, flags=re.I)[0]
    s = s.strip(" .,;|-–•©")
    return s


person_items = defaultdict(list)
company_items = defaultdict(list)
for cc in ("US", "CA"):
    for p in name_pages[cc]:
        L = p["lines"]
        french = "fra" in p["languages"]
        for k, ln in enumerate(L):
            if person_ok(ln) and (PERSON.match(ln) or MULTI.match(ln)):
                nxt = L[k + 1] if k + 1 < len(L) else ""
                prv = L[k - 1] if k > 0 else ""
                role_ctx = (len(nxt) <= 70 and ROLE.search(nxt)) or (len(prv) <= 40 and ROLE.search(prv) and False)
                titled = re.match(rf"^{TITLE}\s", ln) or re.search(rf",\s*{CRED}\.?$", ln)
                if (role_ctx or titled) or (MULTI.match(ln) and len(ln.split()) <= 5):
                    kind = "multi" if MULTI.match(ln) else ("titled" if titled else "plain")
                    person_items[cc].append({"text": ln, "host": p["host"], "warc": p["warc"], "strata": [kind] + (["french"] if french else []), "src": "text"})
            m = COPY.search(ln)
            if m and len(ln) < 160:
                c = clean_company(m.group(1))
                if 3 <= len(c) <= 70 and len(c.split()) <= 8 and not re.search(r"\d{3,}|rights|reserved", c, re.I):
                    company_items[cc].append({"text": c, "host": p["host"], "warc": p["warc"], "strata": ["legal" if LEGAL.search(c) else "bare"] + (["french"] if french else []), "src": "copyright"})
        for o in p["ld"]:
            c = o["name"].strip()
            if o["type"].lower().find("person") >= 0:
                if 3 <= len(c) <= 60 and PERSON.match(c) and person_ok(c):
                    person_items[cc].append({"text": c, "host": p["host"], "warc": p["warc"], "strata": ["plain"] + (["french"] if french else []), "src": "ldjson"})
            elif 3 <= len(c) <= 70 and len(c.split()) <= 8:
                company_items[cc].append({"text": c, "host": p["host"], "warc": p["warc"], "strata": ["legal" if LEGAL.search(c) else "bare"] + (["french"] if french else []), "src": "ldjson"})
        sn = p["meta"].get("og:site_name")
        if sn and 3 <= len(sn) <= 70 and len(sn.split()) <= 8:
            company_items[cc].append({"text": sn.strip(), "host": p["host"], "warc": p["warc"], "strata": ["legal" if LEGAL.search(sn) else "bare"] + (["french"] if french else []), "src": "site_name"})

for cc in ("US", "CA"):
    for kind, items, n in (("person", person_items[cc], n_person), ("company", company_items[cc], n_company)):
        counts = defaultdict(int)
        for it in items:
            for s in it["strata"]:
                counts[s] += 1
        ch = stratified(items, n, rarest(counts), per_host=PERSON_PER_HOST if kind == "person" else 1)
        stats[f"{kind} {cc}"] = (len(items), len(ch), dict(counts))
        with open(os.path.join(out, f"draft-name-{cc}.jsonl"), "a" if kind == "company" else "w", encoding="utf-8") as f:
            for i, it in enumerate(ch):
                f.write(json.dumps({"id": f"name-{cc}-{kind[0]}{i:03d}", "country": cc, "kind_hint": kind, **it}, ensure_ascii=False) + "\n")

print("pages", len(pages), "with US page", len(name_pages["US"]), "CA page", len(name_pages["CA"]))
for k, v in stats.items():
    print(k, "candidates", v[0], "drafted", v[1], v[2])
