#!/usr/bin/env python3
"""Finds strings that name several people at once ("Mr. and Mrs. John Smith", "John & Jane Smith") in stored page lines.

usage: extract-multi.py <country> <pages.jsonl> <out draft.jsonl> <seed> <max items> [<max per host>]

The ordinary name draft (crawl/extract-candidates.py) only looks for a person line followed by a role line, so couples written as one
line rarely reach it. This pass looks for the joining word between two person-like parts on a short line, or after a label such as
"Owners:" / "by". Strings are kept verbatim. Items get ids name-<country>-m000... and a `multi` stratum, and are labelled by the
reviewer like any other name draft (labeling.py). The joining words are those of the country's languages.
"""
import json
import os
import random
import re
import sys

cc, pages_path, out, seed, n_max = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5])
per_host = int(sys.argv[6]) if len(sys.argv) > 6 else 2
JOIN = {"US": r"and|&", "CA": r"and|&|et", "RO": r"și|si|and|&", "MX": r"y|e|&|and"}.get(cc, r"and|&|et|und|y|e")
TITLE = r"(?:Dr|Mr|Mrs|Ms|Miss|Mme|Mlle|M|Me|Prof|Rev|Hon|Pastor|Sr|Sra|Srta|Lic|Ing|Dna|Dl|Dna|Dl|Doamna|Domnul|Dna\.|Dl\.)\.?"
CAPW = r"(?:[A-ZÀ-ÖØ-Þ][a-zà-öø-ÿA-ZÀ-ÖØ-Þ'’\-]+\.?|[A-Z]\.)"
PERSON_PART = rf"(?:{TITLE}\s+)?{CAPW}(?:\s+{CAPW}){{0,2}}"
MULTI = re.compile(rf"^{PERSON_PART}\s+(?:{JOIN})\s+{PERSON_PART}$")
LABEL = re.compile(r"^(?:(?:our )?(?:owners?|founders?|proprietors?|hosts?|managed by|owned by|operated by|founded by|by|pastors?|directors?|co-?owners?|"
                   r"propriétaires?|fondateurs?|fondatori|proprietari|proprietarios?|propietarios?)\s*[:\-–]?\s+)", re.I)
STOP = set("""home about contact us services service our team news blog privacy policy terms login search menu skip content call now get free quote request learn view all click here
location locations hours company inc llc ltd read more meet staff careers join shop cart checkout account sign register faq gallery resources events donate give support help sitemap
copyright rights reserved follow share facebook twitter instagram linkedin youtube email phone fax street road avenue suite drive welcome new used customer reviews products specials
offers financing schedule appointment book online patient patients forms insurance payment directions north south east west first second third last next previous page submit send message
name general information board directors members officers committee department departments office offices city county state united states america canada terms conditions cookies
sales service parts rentals repair home garden food drink wine beer coffee tea books music art design media group partners associates law legal firm bank club school church""".split())
US_ST = "AL AK AZ AR CA CO CT DE DC FL GA HI ID IL IN IA KS KY LA ME MD MA MI MN MS MO MT NE NV NH NJ NM NY NC ND OH OK OR PA RI SC SD TN TX UT VT VA WA WV WI WY PR GU VI AS MP".split()
CA_PR = "AB BC MB NB NL NS NT NU ON PE QC SK YT".split()
US_RE = re.compile(r"\b(?:%s)\b\.?,?\s+\d{5}(?:-\d{4})?\b" % "|".join(US_ST))
CA_RE = re.compile(r"\b(?:%s)\b\.?,?\s*[ABCEGHJ-NPRSTVXY]\d[ABCEGHJ-NPRSTV-Z][ \-]?\d[ABCEGHJ-NPRSTV-Z]\d\b" % "|".join(CA_PR))


def page_is(p):
    """United States and Canada: the page holds a state-and-ZIP or province-and-postal-code address, as in extract-candidates.py."""
    if cc not in ("US", "CA"):
        return True
    us = sum(1 for ln in p["lines"] if len(ln) <= 250 and US_RE.search(ln))
    ca = sum(1 for ln in p["lines"] if len(ln) <= 250 and CA_RE.search(ln))
    here = "CA" if (p["tld"] == "ca" and ca) or ca > us else ("US" if us else None)
    return here == cc


# A short list of common given names (ours), so that "Boards & Committees" or "Smith and Jones LLP" never reach the draft: a string is kept
# only when each side opens with a title or one of these names. Other languages add their own names through GIVEN_EXTRA (comma separated).
GIVEN = set("""james john robert michael william david richard joseph thomas charles chris christopher daniel matthew anthony mark donald steven paul andrew joshua kenneth kevin
brian george timothy ronald edward jason jeffrey ryan jacob gary nicholas eric jonathan stephen larry justin scott brandon benjamin samuel gregory frank alexander raymond patrick jack dennis jerry
tyler aaron henry douglas peter adam nathan zachary walter kyle harold carl arthur gerald roger keith jeremy terry lawrence sean christian albert joe ethan austin jesse willie billy bryan bruce
jordan ralph roy noah dylan eugene wayne alan juan louis russell gabriel randy philip harry vincent bobby johnny logan bob jim tom mike dave steve tim jeff greg ron don ken ed
mary patricia jennifer linda elizabeth barbara susan jessica sarah karen lisa nancy betty margaret sandra ashley kimberly emily donna michelle carol amanda dorothy melissa deborah stephanie
rebecca sharon laura cynthia kathleen amy angela shirley anna brenda pamela emma nicole helen samantha katherine christine debra rachel carolyn janet catherine maria heather diane ruth julie
olivia joyce virginia victoria kelly lauren christina joan evelyn judith megan andrea cheryl hannah jacqueline martha gloria teresa ann sara madison frances kathryn janice jean abigail alice
julia judy sophia grace denise amber doris marilyn danielle beverly isabella theresa diana natalie brittany charlotte marie kayla alexis lori kim jane jill beth sue pat jo anne ginger
pierre jean-pierre marc luc andré michel jacques françois claude denis yves gilles sylvie nathalie isabelle monique chantal diane louise line"""
             .split())
GIVEN |= {x.strip().lower() for x in os.environ.get("GIVEN_EXTRA", "").split(",") if x.strip()}


def opens_with_person(part):
    w = part.split()
    if re.fullmatch(TITLE, w[0]):
        return True
    return w[0].strip(".,").lower() in GIVEN


rng = random.Random(seed)
items = []
for l in open(pages_path, encoding="utf-8"):
    p = json.loads(l)
    if not page_is(p):
        continue
    for ln in p["lines"]:
        s = ln.strip()
        if not (8 <= len(s) <= 70) or re.search(r"\d|@|http|www\.", s):
            continue
        s = LABEL.sub("", s).strip(" ,;:|-–•")
        if not MULTI.match(s):
            continue
        left, right = re.split(rf"\s+(?:{JOIN})\s+", s, maxsplit=1)
        if not (opens_with_person(left) and (opens_with_person(right) or len(right.split()) == 1 or right.split()[0].strip(".,").lower() in GIVEN)):
            continue
        words = [w.strip(".,").lower() for w in s.split()]
        if any(w in STOP for w in words):
            continue
        items.append({"text": s, "host": p["host"], "warc": p["warc"], "strata": ["multi"], "src": "text"})
rng.shuffle(items)
seen, per, chosen = set(), {}, []
for it in items:
    k = re.sub(r"\W+", "", it["text"].lower())
    if k in seen or per.get(it["host"], 0) >= per_host:
        continue
    seen.add(k)
    per[it["host"]] = per.get(it["host"], 0) + 1
    chosen.append(it)
    if len(chosen) >= n_max:
        break
with open(out, "w", encoding="utf-8") as f:
    for i, it in enumerate(chosen):
        f.write(json.dumps({"id": f"name-{cc}-m{i:03d}", "country": cc, "kind_hint": "multi", **it}, ensure_ascii=False) + "\n")
print(cc, "multi candidates", len(items), "drafted", len(chosen))
