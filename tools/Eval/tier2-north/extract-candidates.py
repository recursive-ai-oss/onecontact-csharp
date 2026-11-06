#!/usr/bin/env python3
"""Step 3 for country sets drawn by host top-level domain: finds address and name strings in the stored page lines and draws a diverse
sample of each, as drafts to be labelled.

usage: extract-candidates.py <out-dir> <seed> <country> <addresses> <persons> <companies>

<country> is a lowercase top-level domain from the table below (dk se fi no is ee lv lt). Reads <out-dir>/pages.jsonl (pages of that
TLD only are used); writes <out-dir>/draft-address-<CC>.jsonl and <out-dir>/draft-name-<CC>.jsonl. Strings are kept verbatim, in the
page's order and with its line breaks. Prints only counts.

An address candidate is a line holding the country's postcode pattern followed by (or preceded by) a locality, with a street (a word
and a house number) or a post-box word on the same line or in the up-to-three lines before it. A person candidate is a line of two to
four capitalised words next to a role word, an e-mail address or a telephone number, or a name from a JSON-LD Person. A company
candidate is a line, JSON-LD organisation name, site name or copyright holder that carries the country's legal-form words.
"""
import json
import os
import random
import re
import sys
from collections import defaultdict

out, seed, tld = sys.argv[1], int(sys.argv[2]), sys.argv[3]
n_addr, n_person, n_company = int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6])
CC = tld.upper()
rng = random.Random(seed)
EXCLUDED = set()
for _f in filter(None, os.environ.get("EXCLUDE", "").split(",")):
    for _l in open(_f, encoding="utf-8"):
        EXCLUDED.add(re.sub(r"\W+", "", json.loads(_l)["text"].lower()))

UP = "A-ZÀ-ÖØ-ÞĀĂĄĆČĎĐĒĖĘĚĢĪĮĶĻŁŃŅŇŌŐŒŘŚŠŞŤŪŲŰŹŻŽ"
LO = "a-zß-öø-ÿāăąćčďđēėęěģīįķļłńņňōőœřśšşťūųűźżž"
CAP = rf"[{UP}][{LO}{UP}'’.\-]*"  # one capitalised word, hyphens and apostrophes inside
LOC = rf"[{UP}][{LO}{UP}'’.\-]+(?:[ \-]+[{UP}][{LO}{UP}'’.\-]+){{0,2}}"

# postcode followed by a locality, per country; Baltic and Estonian forms also appear after the locality.
PC = {
    "dk": rf"(?:DK[- ]?)?\b[1-9]\d{{3}}\b(?!\s*[-–/])",
    "se": r"(?:SE[- ]?)?\b\d{3}\s?\d{2}\b",
    "fi": r"(?:FI[- ]?)?\b\d{5}\b",
    "no": r"(?:NO[- ]?)?\b[0-9]{4}\b",
    "is": r"(?:IS[- ]?)?\b\d{3}\b",
    "ee": r"(?:EE[- ]?)?\b\d{5}\b",
    "lv": r"\bLV[- –]?\d{4}\b",
    "lt": r"(?:LT[- –]?)?\b\d{5}\b",
}[tld]
# For these countries the postcode comes before the locality in the national style; the others may put it after.
LOC_AFTER = rf"\s+{LOC}"
LOC_BEFORE = {"ee": True, "lv": True, "lt": True}.get(tld, False)
ADDR_RE = re.compile(rf"(?<![\w/\-.]){PC}{LOC_AFTER}")
ADDR_RE_PCLAST = re.compile(rf"{LOC}[ ,]+{PC}(?![\w\-])") if LOC_BEFORE else None
PREFIXED = re.compile(r"(?:DK|SE|FI|NO|IS|EE|LV|LT)[- –]\d") if tld in ("lv", "lt") else None

STREET = re.compile(rf"[{UP}{LO}.\-]{{3,}}\.?\s+\d{{1,4}}\s?[A-Za-z]?\b(?![\d.]*\s*(?:%|kr|€|eur|dkk|sek|nok))|\b\d{{1,4}}\s?[A-Za-z]?\s+[{UP}][{LO}]{{3,}}", re.U)
BOX = re.compile(r"\b(?:postboks|postbox|postfack|pb\.?|pl\.?|postiljonsfack|postkasse|postkast|pósthólf|postkontor|pk\.?|box|p\.?o\.?\s*box|pasta kastīte|pasta kaste|"
                 r"a\.?\s?k\.?|pašto dėžutė|p\.?\s?d\.?|postiosoite|postadress|postadresse|postiosoite|postiaadress|postiindeks)\s*\.?\s*\d", re.I)
COUNTRY = re.compile(r"^(?:Danmark|Denmark|Sverige|Sweden|Suomi|Finland|Norge|Norway|Noreg|Ísland|Island|Iceland|Eesti|Estonia|Latvija|Latvia|Lietuva|Lithuania)\s*$", re.I)
LABEL = re.compile(r"^(?:adresse|adress|osoite|aadress|adrese|adresas|besøgsadresse|besøksadresse|postadresse|postadress|postiosoite|käyntiosoite|"
                   r"besöksadress|heimilisfang|kontakt|kontakta oss|kontakt os|address|visit us|find os|"
                   r"juridiskā adrese|juridinis adresas|büroo|kontor|office|head office|hovedkontor|pea ?kontor|galvenais birojs|buveinė)\s*[:\-–]\s*", re.I)
UNIT = re.compile(r"\b(?:st\.?\s?(?:tv|th|mf)|\d\.\s?(?:sal|tv|th|mf|etg)|lgh|\d\s?tr\.?|våning|vån|krs\.?|kerros|rapp|trappa|ingång|port|hus|bygning|etasje|"
                  r"etg\.?|etage|boliga?|kab\.?|k-\d|kor\.?|korrus|ap\.?|dz\.?|dzīv|buteo|but\.?|bt\.?|aukšt|kont|dør|leil|apt)\b", re.I)
CAPS = re.compile(r"[A-ZÆØÅÄÖ]{4,}")
RURAL = re.compile(r"\b(?:gård|gaard|gård|bygd|kylä|küla|village|ciems|kaimas|k\.|vald|pagasts|novads|apskr|maakond|region|kommun|kommune|by|bæ)\b", re.I)

LEGAL = {
    "dk": r"\b(?:ApS|A/S|I/S|K/S|IVS|P/S|Aps|A\.m\.b\.a\.?|amba|f\.m\.b\.a\.?|S\.m\.b\.a|Fond|Forening)\b",
    "se": r"\b(?:AB|Aktiebolag|HB|Handelsbolag|KB|Kommanditbolag|Ekonomisk förening|Ideell förening|Stiftelse|\(publ\))",
    "fi": r"\b(?:Oy|Oyj|Ky|Tmi|Osk|Ab|Abp|Oy Ab|ry|r\.y\.|Oyj Abp|Osuuskunta|Säätiö|Toiminimi)\b",
    "no": r"\b(?:AS|ASA|ANS|DA|SA|BA|NUF|KS|Stiftelse|Forening|Samvirke)\b",
    "is": r"\b(?:ehf\.?|hf\.?|sf\.?|slf\.?|ohf\.?|ses\.?|svf\.?|ehf)\b",
    "ee": r"\b(?:OÜ|AS|MTÜ|TÜ|FIE|UÜ|SA|Sihtasutus|Osaühing|Aktsiaselts)\b",
    "lv": r"\b(?:SIA|AS|IK|Biedrība|Sabiedrība ar ierobežotu atbildību|Akciju sabiedrība|KS|PS|SIA\s+\")",
    "lt": r"\b(?:UAB|AB|MB|VšĮ|IĮ|ŽŪB|KB|TŪB|Uždaroji akcinė bendrovė|Akcinė bendrovė|Mažoji bendrija|Asociacija)\b",
}[tld]
LEGAL_RE = re.compile(LEGAL)
ROLE = re.compile(r"\b(?:ceo|cfo|coo|cto|direktør|administrerende|adm\.? dir|daglig leder|styreleder|styrets leder|formand|bestyrelse|kontaktperson|kontaktpersoon|vd|"
                  r"verkställande|ordförande|chef|ansvarig|toimitusjohtaja|johtaja|puheenjohtaja|yhteyshenkilö|päällikkö|asiantuntija|myynti|"
                  r"framkvæmdastjóri|forstjóri|stjórnarformaður|stjórn|juhataja|direktor|tegevjuht|juhatuse|kontaktisik|juhatuse liige|"
                  r"direktors|valdes|vadītājs|vadītāja|valdybos|direktorius|vadovas|vadovė|generalinis|advokat|advokaat|jurist|advokatas|advokāts|"
                  r"lege|tannlege|tandlæge|tandläkare|hammaslääkäri|læknir|arst|lääkäri|läkare|læge|konsulent|rådgiver|rådgivare|neuvoja|projektleder|"
                  r"projektipäällikkö|koordinator|koordinaator|koordinators|koordinatorius|partner|manager|founder|grundlægger|grundare|perustaja|"
                  r"stofnandi|asutaja|dibinātājs|įkūrėjas|owner|ejer|ägare|omistaja|eigandi|omanik|īpašnieks|savininkas|sekretær|sihteeri|sekretär|"
                  r"sales|kundeservice|kundtjänst|asiakaspalvelu|regnskab|redovisning|kirjanpito|rektor|lærer|lärare|opettaja|kennari|õpetaja|skolotājs|mokytojas|"
                  r"professor|lektor|dr\.|ph\.?d|tohtori|cand\.|ing\.|mag\.)\b", re.I)
EMAIL = re.compile(r"[\w.+-]+@[\w-]+\.[\w.-]+")
PHONE = re.compile(r"(?:\+|\b00)\s?\d[\d ()\-]{6,}|\btel\w*\.?\s*:?\s*\d|\b\d{2}\s\d{2}\s\d{2}\s\d{2}\b")
PERSON = re.compile(rf"^(?:(?:Dr|Prof|Mag|Ing|Cand|Adv|Mr|Mrs|Ms|Fru|Hr|Herr|Dir|Tohtori|Prof|Lic|Doc)\.?\s+)*{CAP}(?:\s+(?:af|von|van|de|der|den|di|du|la|le|ten|ter|til|av|zu|a|e|y|of|[{UP}]\.?|{CAP})){{1,4}}$")
BADWORD = re.compile(r"\b(?:kontakt|contact|about|home|menu|login|cookie|privacy|policy|terms|copyright|rights|reserved|read more|læs mere|läs mer|lue lisää|loe edasi|"
                     r"press|news|blog|shop|cart|search|follow|facebook|instagram|linkedin|twitter|youtube|support|help|hjælp|email|e-post|sähköposti|e-mail|telefon|puhelin|"
                     r"phone|tel|mobil|mobile|fax|adresse|adress|osoite|address|aadress|opening|åbningstider|öppettider|aukioloajat|kl\.|man|tir|ons|tor|fre|lør|søn|"
                     r"mon|tue|wed|thu|fri|sat|sun|stockholm|oslo|bergen|københavn|helsinki|tallinn|riga|rīga|vilnius|kaunas|reykjavík|reykjavik|"
                     r"danmark|sverige|norge|suomi|eesti|latvija|lietuva|island|ísland|denmark|sweden|norway|finland|estonia|latvia|lithuania|iceland)\b", re.I)

LANGMAP = {"dan": "da", "swe": "sv", "fin": "fi", "nor": "nb", "nob": "nb", "nno": "nn", "isl": "is", "est": "et", "lav": "lv", "lit": "lt", "eng": "en",
           "deu": "de", "rus": "ru", "pol": "pl"}
DEFAULT_LANG = {"dk": "da", "se": "sv", "fi": "fi", "no": "nb", "is": "is", "ee": "et", "lv": "lv", "lt": "lt"}[tld]


def page_lang(p):
    for t in (p.get("languages") or "").split(","):
        if t in LANGMAP:
            return LANGMAP[t]
    return DEFAULT_LANG


pages = [json.loads(l) for l in open(os.path.join(out, "pages.jsonl"), encoding="utf-8")]
pages = [p for p in pages if p["tld"] == tld]


def clip_line(line):
    """Trims the line to start at its street (when the street is on the line) and end after the locality or postcode; strips a label."""
    s = LABEL.sub("", line).strip(" ,;|-–•·")
    m = None
    for m in ADDR_RE.finditer(s):
        pass
    end = None
    if m:
        end = m.end()
        # the locality run may be followed by a country name
        tail = re.match(r"\s*[,;]?\s*(Danmark|Denmark|Sverige|Sweden|Suomi|Finland|Norge|Norway|Ísland|Iceland|Eesti|Estonia|Latvija|Latvia|Lietuva|Lithuania)\b", s[end:], re.I)
        if tail:
            end += tail.end()
    elif ADDR_RE_PCLAST is not None:
        for m2 in ADDR_RE_PCLAST.finditer(s):
            m = m2
        if m:
            end = m.end()
            tail = re.match(r"\s*[,;]?\s*(Eesti|Estonia|Latvija|Latvia|Lietuva|Lithuania)\b", s[end:], re.I)
            if tail:
                end += tail.end()
    if end is None:
        return None
    s = s[:end]
    sm = STREET.search(s) or BOX.search(s)
    if sm and sm.start() > 0 and len(s) > 90:
        s = s[sm.start():]
    return s.strip(" ,;|-–•·")


def has_street(text):
    return bool(STREET.search(text) or BOX.search(text))


def addresses(p):
    L = p["lines"]
    res = []
    for k, ln in enumerate(L):
        if len(ln) > 260:
            continue
        cur = clip_line(ln)
        if not cur:
            continue
        if not (ADDR_RE.search(cur) or (ADDR_RE_PCLAST is not None and ADDR_RE_PCLAST.search(cur))):
            continue
        lines = [cur]
        # a line with only a postcode and locality needs the street lines before it; a line with a street needs nothing more.
        head = cur
        if not has_street(head) or re.match(rf"^(?:(?:DK|SE|FI|NO|IS|EE|LV|LT)[- –]?)?\d[\d ]*\s+{LOC}$", cur):
            j, added = k - 1, 0
            while j >= 0 and added < 3 and len(L[j]) <= 90:
                prev = LABEL.sub("", L[j]).strip(" ,;|")
                if not prev or EMAIL.search(prev):
                    break
                lines.insert(0, prev)
                added += 1
                if has_street(prev):
                    break
                j -= 1
            if not has_street(" ".join(lines[:-1])):
                continue
        nxt = L[k + 1] if k + 1 < len(L) else ""
        if COUNTRY.match(nxt):
            lines.append(nxt)
        text = "\n".join(lines)
        if 8 <= len(text) <= 240:
            res.append(text)
    return res


def addr_strata(text):
    s = []
    if BOX.search(text):
        s.append("box")
    if UNIT.search(text):
        s.append("unit")
    s.append("multiline" if "\n" in text else "oneline")
    if CAPS.search(text):
        s.append("upper")
    if RURAL.search(text):
        s.append("rural")
    if re.search(r"\d\s*[-/]\s*\d", text):
        s.append("hyphen")
    if re.search(rf"\b(?:{COUNTRY.pattern[5:-6]})\b", text, re.I):
        s.append("country")
    return s or ["plain"]


def stratified(items, n, per_host):
    seen, uniq = set(), []
    rng.shuffle(items)
    for it in items:
        k = re.sub(r"\W+", "", it["text"].lower())
        if k in seen or k in EXCLUDED:
            continue
        seen.add(k)
        uniq.append(it)
    counts = defaultdict(int)
    for it in uniq:
        for s in it["strata"]:
            counts[s] += 1
    keyed = []
    for it in uniq:
        w = sum(1.0 / counts[s] for s in it["strata"])
        keyed.append((rng.random() ** (1.0 / w), it))
    keyed.sort(key=lambda x: -x[0])
    chosen, per = [], defaultdict(int)
    for _, it in keyed:
        if per[it["host"]] >= per_host:
            continue
        per[it["host"]] += 1
        chosen.append(it)
        if len(chosen) >= n:
            break
    return chosen, dict(counts)


def title_ok(c):
    return not BADWORD.search(c)


addr_items = []
for p in pages:
    lang = page_lang(p)
    for t in addresses(p):
        addr_items.append({"text": t, "host": p["host"], "warc": p["warc"], "lang": lang, "strata": addr_strata(t),
                           "url_kind": "root" if re.match(r"https?://[^/]+/?$", p["url"]) else "inner"})

COPY = re.compile(r"(?:©|\(c\)|copyright)\s*(?:\d{4}(?:\s*[-–]\s*\d{4})?)?\s*[,:.]?\s*(.+)", re.I)
TAILCUT = re.compile(r"\s*(?:[|•·–—]|\.\s|,\s*(?:alle|all|kaikki|kõik|visas|visi|vse)|\ball rights|\balle rettigheder|\balla rättigheter|\bkaikki oikeudet|"
                     r"\bcvr|\borg\.?\s?nr|\by-tunnus|\bregistrikood|\breģ|\bįm\.?\s?kodas|\bkt\.|\bkt:|\bVAT|\bsvf).*$", re.I)


def clean_company(c):
    c = TAILCUT.sub("", c).strip(" ,;:|-–•·.")
    return c


person_items, company_items = [], []
for p in pages:
    lang = page_lang(p)
    L = p["lines"]
    for k, ln in enumerate(L):
        if len(ln) <= 70:
            cand = ln.strip(" ,;|•·")
            # persons: the name on its own, with a role, e-mail or phone within two lines either side
            ctx = " ".join(L[max(0, k - 2):k] + L[k + 1:k + 3])
            for piece in {cand, re.split(r"\s*[,|–—]\s*|\s+-\s+", cand)[0]}:
                if 5 <= len(piece) <= 45 and PERSON.match(piece) and title_ok(piece) and not LEGAL_RE.search(piece) and len(piece.split()) <= 5:
                    role_ctx = bool(ROLE.search(ctx) or EMAIL.search(ctx) or PHONE.search(ctx) or ROLE.search(cand))
                    if role_ctx:
                        titled = bool(re.match(r"^(?:Dr|Prof|Mag|Ing|Cand|Adv|Mr|Mrs|Ms|Fru|Hr|Herr|Dir|Tohtori|Lic|Doc)\b", piece))
                        multi = bool(re.search(r"\s(?:og|och|ja|ir|un|ning|&|and|und)\s", piece))
                        strata = ["multi" if multi else ("titled" if titled else "plain")]
                        if re.search(r"-", piece):
                            strata.append("hyphen")
                        if len(piece.split()) >= 4:
                            strata.append("long")
                        if CAPS.search(piece):
                            strata.append("upper")
                        if re.search(r"\b(?:af|von|van|de|der|den|di|du|la|le|ten|ter|til|av|zu)\b", piece):
                            strata.append("particle")
                        person_items.append({"text": piece, "host": p["host"], "warc": p["warc"], "lang": lang, "strata": strata, "src": "text"})
                    break
        if LEGAL_RE.search(ln) and len(ln) <= 90 and not re.search(r"\d{6,}", ln):
            cand = clean_company(re.sub(rf"^.*?(?=[{UP}0-9\"“])", "", ln))
            m = COPY.search(cand)
            if m:
                cand = clean_company(m.group(1))
            if 3 <= len(cand) <= 70 and len(cand.split()) <= 8 and LEGAL_RE.search(cand) and not re.search(r"\b(?:cvr|org\.?\s?nr|vat|tel)\b", cand, re.I):
                company_items.append({"text": cand, "host": p["host"], "warc": p["warc"], "lang": lang, "strata": ["legal"], "src": "line"})
        m = COPY.search(ln)
        if m and len(ln) < 200:
            c = clean_company(m.group(1))
            if 3 <= len(c) <= 70 and len(c.split()) <= 8 and not re.search(r"\d{3,}|rights|reserved|rettigheder|rättigheter|oikeudet|õigused|tiesības|teisės", c, re.I):
                company_items.append({"text": c, "host": p["host"], "warc": p["warc"], "lang": lang,
                                      "strata": ["legal" if LEGAL_RE.search(c) else "bare"], "src": "copyright"})
    for o in p["ld"]:
        c = o["name"].strip()
        if "person" in o["type"].lower():
            if 3 <= len(c) <= 60 and PERSON.match(c) and title_ok(c):
                person_items.append({"text": c, "host": p["host"], "warc": p["warc"], "lang": lang, "strata": ["plain"], "src": "ldjson"})
        elif 3 <= len(c) <= 70 and len(c.split()) <= 8:
            company_items.append({"text": c, "host": p["host"], "warc": p["warc"], "lang": lang, "strata": ["legal" if LEGAL_RE.search(c) else "bare"], "src": "ldjson"})
    sn = p["meta"].get("og:site_name")
    if sn and 3 <= len(sn) <= 70 and len(sn.split()) <= 8:
        company_items.append({"text": sn.strip(), "host": p["host"], "warc": p["warc"], "lang": lang, "strata": ["legal" if LEGAL_RE.search(sn) else "bare"], "src": "site_name"})

stats = {}
ch, cn = stratified(addr_items, n_addr, 2)
stats["address"] = (len(addr_items), len(ch), cn)
with open(os.path.join(out, f"draft-address-{CC}.jsonl"), "w", encoding="utf-8") as f:
    for i, it in enumerate(ch):
        f.write(json.dumps({"id": f"addr-{CC}-{i:03d}", "country": CC, **it}, ensure_ascii=False) + "\n")
for kind, items, n, cap in (("person", person_items, n_person, 2), ("company", company_items, n_company, 1)):
    ch, cn = stratified(items, n, cap)
    stats[kind] = (len(items), len(ch), cn)
    with open(os.path.join(out, f"draft-name-{CC}.jsonl"), "a" if kind == "company" else "w", encoding="utf-8") as f:
        for i, it in enumerate(ch):
            f.write(json.dumps({"id": f"name-{CC}-{kind[0]}{i:03d}", "country": CC, "kind_hint": kind, **it}, ensure_ascii=False) + "\n")
print(CC, "pages", len(pages), {k: (v[0], v[1]) for k, v in stats.items()})
for k, v in stats.items():
    print("  ", k, v[2])
