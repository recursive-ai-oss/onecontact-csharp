#!/usr/bin/env python3
"""Step 3 (Tier 2): extracts candidate address, person and company strings from the stored pages and samples them for diversity.

usage: extract-candidates-t2.py <out-dir> <seed> <country> <n-address> <n-person> <n-company>

Reads <out-dir>/pages.jsonl (written by fetch-pages.py from a Tier 2 selection) and writes draft-address-<country>.jsonl and
draft-name-<country>.jsonl next to it. Strings are kept as they appear on the page. At most two items per host for addresses and
persons and one for companies, so no site dominates; the draw takes the rarest layout/language strata first so that units, post-office
boxes, rural places, upper case, minority languages and French overseas departments are represented. Environment EXCLUDE names
JSON-lines files whose texts must not be drawn again. The drafts are private like the pages.
"""
import json
import os
import random
import re
import sys
from collections import defaultdict

import tier2

out, seed, country, n_addr, n_person, n_company = sys.argv[1], int(sys.argv[2]), sys.argv[3], int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6])
rng = random.Random(f"{seed}-{country}")
pages = []
for l in open(os.path.join(out, "pages.jsonl"), encoding="utf-8"):
    p = json.loads(l)
    if p.get("country") == country:
        pages.append(p)
EXCLUDED = set()
for f in filter(None, os.environ.get("EXCLUDE", "").split(",")):
    for l in open(f, encoding="utf-8"):
        EXCLUDED.add(re.sub(r"\W+", "", json.loads(l)["text"].lower()))

PC = tier2.POSTCODE_RE[country]
TAIL_CUT = re.compile(r"\s*(?:[|•·]|\s[–—-]\s|\b(?:Tel|Tél|Telefon|Telefoon|T\.|Fon|Phone|Fax|E-?mail|Mail|Web|www|http|Mobil|GSM|Öffnungszeiten|Horaires|Ouvert|Mo\.|Lu\.)\b[\s.:]|\+\d|\(\d{2,}|\b0\d{2,}[ /.\-]\d)")
FIELD_CUT = re.compile(r"\s*(?:,\s*)?(?:Tel|Tél|Telefon|Fax|E-?mail|Mail|Web|Internet|Homepage)\b.*$", re.I)


def clip_line(line):
    """Trims after the place name that follows the last postal code (and a country line word), and a leading label."""
    m = None
    for m in PC.finditer(line):
        pass
    if not m:
        return None
    head, tail = line[:m.start()], line[m.start():]
    t = TAIL_CUT.search(tail, m.end() - m.start())
    if t:
        tail = tail[:t.start()]
    tail = FIELD_CUT.sub("", tail)
    s = tier2.LABEL.sub("", head + tail)
    return s.strip(" ,;|-–•")


def street_ok(text):
    return bool(tier2.BOX.search(text)) or bool(re.search(r"\d", text))


def addresses(p):
    L = p["lines"]
    res = []
    for k, ln in enumerate(L):
        if len(ln) > 300 or not PC.search(ln):
            continue
        cur = clip_line(ln)
        if not cur:
            continue
        m = PC.search(cur)
        head = cur[:m.start()] if m else cur
        lines = [cur]
        if street_ok(head):
            # one-line address: keep from a street-looking start when a long lead-in precedes it
            if len(head) > 110:
                mm = re.search(r"(?:^|[\s,;:|])(\d{1,4}\s?[A-Za-z]?\s+\S+|[A-ZÀ-Ý][\wà-ÿ.\-]+\s*(?:str\.|straße|strasse|weg|gasse|platz|laan|straat|plein|rue|avenue|chemin)\s*\d)", head)
                if mm:
                    lines = [cur[mm.start(1):]]
        else:
            j, added = k - 1, 0
            while j >= 0 and added < 3 and len(L[j]) <= 90:
                prev = tier2.LABEL.sub("", L[j]).strip(" ,;|")
                if not prev or tier2.COUNTRY_LINE.match(prev):
                    break
                lines.insert(0, prev)
                added += 1
                if street_ok(prev):
                    break
                j -= 1
            if not street_ok(" ".join(lines[:-1])):
                continue
        nxt = L[k + 1] if k + 1 < len(L) else ""
        if tier2.COUNTRY_LINE.match(nxt):
            lines.append(nxt)
        text = "\n".join(lines)
        if 8 <= len(text) <= 220:
            res.append(text)
    return res


def strata(text, p):
    s = []
    if tier2.BOX.search(text):
        s.append("box")
    if tier2.UNIT.search(text):
        s.append("unit")
    s.append("multiline" if "\n" in text else "oneline")
    if text.upper() == text and re.search(r"[A-Z]{3}", text):
        s.append("upper")
    if re.search(r"\d+\s?[-–/]\s?\d+", text):
        s.append("range")
    if tier2.RURAL.search(text):
        s.append("rural")
    if p.get("territory"):
        s.append("dom-" + p["territory"])
    lang = tier2.page_lang(p["languages"], country)
    if lang:
        s.append("lang-" + lang)
    return s or ["plain"]


by_addr = []
for p in pages:
    for t in addresses(p):
        by_addr.append({"text": t, "host": p["host"], "warc": p["warc"], "territory": p.get("territory"), "lang": tier2.page_lang(p["languages"], country),
                        "url_kind": "root" if re.match(r"https?://[^/]+/?$", p["url"]) else "inner", "strata": strata(t, p)})


def stratified(items, n, per_host):
    seen, uniq = set(), []
    rng.shuffle(items)
    hc = defaultdict(int)
    for it in items:
        k = re.sub(r"\W+", "", it["text"].lower())
        if k in seen or k in EXCLUDED or hc[it["host"]] >= per_host:
            continue
        seen.add(k)
        hc[it["host"]] += 1
        uniq.append(it)
    counts = defaultdict(int)
    for it in uniq:
        for s in it["strata"]:
            counts[s] += 1
    buckets = defaultdict(list)
    for it in uniq:
        buckets[min(it["strata"], key=lambda s: counts[s])].append(it)
    order = sorted(buckets, key=lambda b: len(buckets[b]))
    chosen = []
    while len(chosen) < n and any(buckets[b] for b in order):
        for b in order:
            if buckets[b] and len(chosen) < n:
                chosen.append(buckets[b].pop())
    return chosen, len(uniq), dict(counts)


ch, avail, counts = stratified(by_addr, n_addr, 2)
with open(os.path.join(out, f"draft-address-{country}.jsonl"), "w", encoding="utf-8") as f:
    for i, it in enumerate(ch):
        f.write(json.dumps({"id": f"addr-{country}-{i:03d}", "country": country, **it}, ensure_ascii=False) + "\n")
print(f"address {country}: pages {len(pages)} candidates {len(by_addr)} unique {avail} drafted {len(ch)} strata {counts}")

# ---------- names ----------
UP = "A-ZÀ-ÖØ-Þ"
LOW = "a-zà-öø-ÿ"
PART = r"(?:van|von|de|del|der|den|ten|ter|te|du|des|d'|di|da|dos|das|zu|zur|zum|vom|am|im|op|het|'t|le|la|l'|el|al|mac|mc|bin|y|e|dit|de la|van der|van den|van de|von der|von dem|von und zu)"
CAPW = rf"(?:[{UP}][{LOW}{UP}'’\-]*[{LOW}][{LOW}{UP}'’\-]*\.?|[{UP}]\.|[{UP}][{LOW}]+(?:-[{UP}][{LOW}]+)+)"
TITLE = (r"(?:Herr|Frau|Fräulein|Hr\.|Fr\.|Dr\.?(?:\s?(?:med|iur|rer\.? nat|phil|h\.c|jur|med\.? dent|-Ing)\.?)?|Prof\.?|Professor|Mag\.?|Dipl\.?-?[A-Za-z]+\.?|DI|Ing\.?|Ir\.?|Drs\.?|Mr\.?|Mrs\.?|Mevr(?:ouw)?\.?|Mw\.?|Dhr\.?|De heer|Mevrouw|"
         r"M\.|Mme|Mlle|Madame|Monsieur|Me|Maître|Dre|Pr\.?|Docteur|Dr med\.?|Notaris|Notaire|Rechtsanwalt|Rechtsanwältin|RA|Pfarrer|Pater|Père|Abbé|Sr\.?|Fr\.)")
CRED = r"(?:MSc|BSc|MBA|MA|BA|LL\.?M\.?|LL\.?B\.?|PhD|Ph\.D\.|M\.D\.|MD|DDS|CPA|CFA|MAS|EMBA|Jr\.?|Sr\.?|II|III|BEng|MEng|MPH|BSc\.?|FRCS|dipl\. [a-z]+\.?|lic\. iur\.|lic\. oec\.)"
PERSON = re.compile(rf"^(?:{TITLE}\s+)*{CAPW}(?:\s+(?:{PART}\s+)*{CAPW}){{1,4}}(?:,?\s+{CRED})*(?:,\s*{CRED})*$")
MULTI = re.compile(rf"^(?:{TITLE}\s+)*{CAPW}(?:\s+{CAPW})?\s+(?:und|&|et|en|e|y)\s+(?:{TITLE}\s+)?{CAPW}(?:\s+(?:{PART}\s+)?{CAPW}){{0,2}}$")
STOP = set("""home startseite kontakt impressum datenschutz agb sitemap login anmelden suche menü menu über uns team news blog leistungen angebot produkte service
aktuelles presse karriere jobs shop warenkorb kasse konto anfahrt öffnungszeiten telefon fax mail email adresse anschrift straße strasse platz weg gasse
accueil contact mentions légales politique confidentialité plan site recherche connexion panier compte nous services produits actualités presse carrière emploi
horaires téléphone adresse rue avenue boulevard place chemin allée route
thuis home contact over ons diensten producten nieuws vacatures winkel winkelwagen account inloggen zoeken openingstijden telefoon adres straat laan plein
herr frau madame monsieur mevrouw meneer geschäftsführer inhaber verantwortlich vertreten leitung praxis kanzlei gemeinde stadt kanton land verein stiftung
weiter lesen mehr anzeigen jetzt anrufen anfrage termin buchen newsletter folgen facebook instagram linkedin youtube twitter google maps karte route
voir plus lire suite appeler demander rendez-vous suivez-nous infolettre nous suivre en savoir lees meer bekijk bel nu afspraak volg ons nieuwsbrief
schweiz suisse svizzera deutschland österreich luxembourg luxemburg belgique belgië belgien nederland france monaco liechtenstein""".split())
ROLE = re.compile(r"\b(?:geschäftsführ\w*|inhaber\w*|leitung|leiter\w*|direktor\w*|vorstand|vorsitz\w*|rechtsanw\w*|anwalt|anwältin|notar\w*|arzt|ärztin|zahnarzt|zahnärztin|"
                  r"praxis\w*|therapeut\w*|berater\w*|ansprechpartner\w*|verantwortlich\w*|vertreten|vertretungsberechtigt\w*|prokurist\w*|mitarbeiter\w*|sekretariat|"
                  r"gérant\w*|directeur|directrice|dirigeant\w*|président\w*|responsable|avocat\w*|notaire|médecin|dentiste|conseill\w*|propriétaire|fondateur|fondatrice|représentant\w*|"
                  r"eigenaar|directeur|bestuurder|zaakvoerder|advocaat|notaris|arts|tandarts|huisarts|contactpersoon|verantwoordelijke|oprichter|voorzitter|secretaris|"
                  r"gemachtigde|burgemeister\w*|gemeinderat|pfarrer\w*|lehrer\w*|trainer\w*|fotograf\w*|architekt\w*|ingenieur\w*|steuerberater\w*)\b", re.I)
LABELLED = re.compile(r"^(?:Geschäftsführer(?:in)?|Geschäftsführung|Inhaber(?:in)?|Praxisinhaber(?:in)?|Vertretungsberechtigte[rn]?(?: Geschäftsführer)?|Vertreten durch|Verantwortlich(?:e[rn]?)?[^:]{0,50}|"
                      r"Ansprechpartner(?:in)?|Leitung|Direktor(?:in)?|Vorstand|Kontaktperson|Gérant(?:e)?|Directeur(?: de la publication)?|Directrice|Responsable[^:]{0,45}|Représentant légal|Président(?:e)?|"
                      r"Dirigeant(?:e)?|Eigenaar|Bestuurder|Contactpersoon|Zaakvoerder|Verantwoordelijke[^:]{0,30}|Gemachtigde|Notaire|Notaris|Avocat|Rechtsanwalt|Rechtsanwältin|Arzt|Ärztin|Contact)\s*[:：]\s*(.+)$", re.I)
COPY = re.compile(r"(?:©|&copy;|\(c\)|Copyright)\s*(?:\d{4}\s*(?:[-–]\s*\d{4})?)?\s*[,:\-–|]?\s*(?:by\s+)?(.+)", re.I)
TRAIL = re.compile(r"\s*(?:[|•–—]|\.\s|[,;]\s*(?:Tous droits|Alle Rechte|Alle rechten|All rights|Webdesign|Website|Realisation|Réalisation|powered|made|Impressum|Datenschutz)).*$", re.I)


def person_ok(line):
    if len(line) > 60 or re.search(r"\d|@|http|www\.|[/:|]", line):
        return False
    words = re.findall(rf"[{UP}{LOW}'’\-\.]+", line)
    if len(words) < 2 or len(words) > 8:
        return False
    return not any(w.lower().strip(".,") in STOP for w in words)


def clean_company(s):
    s = TRAIL.sub("", s.strip())
    s = re.sub(r"\s*[,.]?\s*(?:Tous droits réservés|Alle Rechte vorbehalten|Alle rechten voorbehouden|All rights reserved).*$", "", s, flags=re.I)
    return s.strip(" ,.;:-–|")


def company_ok(c):
    w = c.split()
    return 3 <= len(c) <= 70 and len(w) <= 8 and not re.search(r"\d{4,}|rights|reserved|vorbehalten|droits|www\.|http|@|cookie|datenschutz|impressum", c, re.I) \
        and not c.lower().strip() in STOP and len(re.findall(rf"[{UP}]", c)) >= 1


person_items, company_items = [], []
for p in pages:
    L = p["lines"]
    lang = tier2.page_lang(p["languages"], country)
    base = {"host": p["host"], "warc": p["warc"], "lang": lang, "territory": p.get("territory")}
    for k, ln in enumerate(L):
        if len(ln) > 160:
            continue
        m = LABELLED.match(ln)
        cand = None
        if m:
            cand = re.split(r"\s*(?:[;|]|\s[-–]\s|\s\(|,\s*(?=[a-zà-ÿ0-9]))", m.group(2).strip())[0].strip(" ,.")
            # an address or number after the name belongs to the next field
            cand = re.split(r",\s*(?=\d|Tel|Tél|Rechtsanw|Notar|Rue|Str)", cand)[0].strip(" ,.")
            src = "labelled"
        elif PERSON.match(ln) or MULTI.match(ln):
            ctx = " ".join(L[max(0, k - 2):k] + L[k + 1:k + 3])
            titled = bool(re.match(rf"^{TITLE}\s", ln))
            if ROLE.search(ctx) or titled:
                cand, src = ln, "titled" if titled else "context"
        if cand and 4 <= len(cand) <= 60 and (PERSON.match(cand) or MULTI.match(cand)) and person_ok(cand):
            kind = "multi" if MULTI.match(cand) and not PERSON.match(cand) else ("titled" if re.match(rf"^{TITLE}\s", cand) else "plain")
            st = [kind, src] + (["lang-" + lang] if lang else []) + (["particle"] if re.search(rf"\s{PART}\s", cand) else []) + (["cred"] if re.search(rf",?\s{CRED}$", cand) else [])
            person_items.append({"text": cand, "strata": st, "src": src, **base})
        mc = COPY.search(ln)
        if mc and len(ln) < 160:
            c = clean_company(mc.group(1))
            if company_ok(c):
                company_items.append({"text": c, "strata": ["legal" if tier2.LEGAL.search(c) else "bare", "copyright"] + (["lang-" + lang] if lang else []), "src": "copyright", **base})
        elif tier2.LEGAL.search(ln) and 3 <= len(ln) <= 70 and not re.search(r"\d{5}|@|www\.|http", ln):
            c = clean_company(ln)
            if company_ok(c) and len(c.split()) >= 2:
                company_items.append({"text": c, "strata": ["legal", "line"] + (["lang-" + lang] if lang else []), "src": "line", **base})
    for o in p["ld"]:
        c = o["name"].strip()
        if "person" in o["type"].lower():
            if PERSON.match(c) and person_ok(c):
                person_items.append({"text": c, "strata": ["plain", "ldjson"] + (["lang-" + lang] if lang else []), "src": "ldjson", **base})
        elif company_ok(c):
            company_items.append({"text": c, "strata": ["legal" if tier2.LEGAL.search(c) else "bare", "ldjson"] + (["lang-" + lang] if lang else []), "src": "ldjson", **base})
    sn = p["meta"].get("og:site_name")
    if sn and company_ok(sn.strip()):
        company_items.append({"text": sn.strip(), "strata": ["legal" if tier2.LEGAL.search(sn) else "bare", "site_name"] + (["lang-" + lang] if lang else []), "src": "site_name", **base})

multi = [i for i in person_items if "multi" in i["strata"]]
rng.shuffle(multi)
keep = {id(x) for x in multi[:25]}
person_items = [i for i in person_items if "multi" not in i["strata"] or id(i) in keep]
rows = []
stats = {}
for kind, items, n in (("p", person_items, n_person), ("c", company_items, n_company)):
    ch, avail, counts = stratified(items, n, 2 if kind == "p" else 1)
    stats[kind] = (len(items), avail, len(ch))
    for i, it in enumerate(ch):
        rows.append({"id": f"name-{country}-{kind}{i:03d}", "country": country, "kind_hint": "person" if kind == "p" else "company", **it})
with open(os.path.join(out, f"draft-name-{country}.jsonl"), "w", encoding="utf-8") as f:
    for r in rows:
        f.write(json.dumps(r, ensure_ascii=False) + "\n")
print(f"names {country}: persons candidates/unique/drafted {stats['p']} companies {stats['c']}")
