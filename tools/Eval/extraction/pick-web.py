#!/usr/bin/env python3
"""Step 3 (web set): picks about 150 pages from the fetched ones, stratified by what markup and text they carry.

usage: pick-web.py <set-dir> <seed> [total] [kept-ids-file]

Reads <set-dir>/pages.jsonl and the stored page bytes; writes <set-dir>/web-picked.jsonl (the chosen pages' metadata plus `country`,
`group` and the feature flags) in a fixed order. Only pages that look North American are eligible: an English page (French allowed on .ca hosts) whose contact data, if any, has
a United States or Canadian shape (area-code phone, state and ZIP, Canadian postal code, a country name), or a .ca host. Pages
listed in the optional kept-ids file are preferred, so a partly reviewed draw keeps its reviewed pages. One page per host. The groups are drawn rarest first, about two thirds from the
United States and one third from Canada:
  obfuscated     an email written as `name [at] host`, `name (at) host` or protected by Cloudflare's `data-cfemail`
  structured     microdata or h-card markup
  jsonld         JSON-LD that names an organization, person, telephone or email
  none           no phone number, email address or street-address-like text in the page at all
  links          `mailto:` or `tel:` links
  text           contact data only in the visible text
"""
import json
import os
import random
import re
import sys

d, seed = sys.argv[1], int(sys.argv[2])
total = int(sys.argv[3]) if len(sys.argv) > 3 else 150
pages = [json.loads(l) for l in open(os.path.join(d, "pages.jsonl"), encoding="utf-8")]

PHONE = re.compile(rb"\(?\b\d{3}\)?[-. \xc2\xa0]\d{3}[-. ]\d{4}\b")
EMAIL = re.compile(rb"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+\.[A-Za-z]{2,}")
STREET = re.compile(rb"\b\d{1,5}\s+[A-Z][A-Za-z.]+(?:\s+[A-Za-z.]+){0,3}\s+(?:St|Street|Ave|Avenue|Rd|Road|Blvd|Boulevard|Dr|Drive|Ln|Lane|Way|Hwy|Court|Ct)\b")
OBF = re.compile(rb"data-cfemail|\[\s*at\s*\]|\(\s*at\s*\)|&#64;|&#x40;|\[\s*dot\s*\]|\(\s*dot\s*\)", re.I)


def features(b):
    return {
        "ld": bool(re.search(rb"application/ld\+json", b, re.I)) and bool(re.search(rb'"@type"\s*:\s*\[?\s*"(?:[A-Za-z]*(?:Organization|Business|Person|Store|Restaurant|Clinic|Dentist|Attorney|Hotel|Service)[A-Za-z]*)"|"telephone"|"email"', b)),
        "micro": bool(re.search(rb"\bitemscope\b", b)) and bool(re.search(rb"itemprop=[\"'](?:telephone|email|address|streetAddress)", b)) or bool(re.search(rb"class=[\"'][^\"']*\b(?:h-card|vcard)\b", b)),
        "links": bool(re.search(rb"href=[\"'](?:mailto|tel):", b, re.I)),
        "obf": bool(OBF.search(b)),
        "text": bool(PHONE.search(b) or EMAIL.search(b) or STREET.search(b)),
    }


NA = re.compile(rb'\(?\b[2-9]\d{2}\)?[-. \xc2\xa0]\d{3}[-. ]\d{4}\b|,\s*[A-Z]{2},?\s+\d{5}\b|\b[A-Z]\d[A-Z] ?\d[A-Z]\d\b|\bCanada\b|\bU\.?S\.?A\.?\b|United States|"addressCountry"\s*:\s*"?(?:US|CA|USA|Canada|United States)"|"telephone"\s*:\s*"\+?1[- (]')
kept = set(open(sys.argv[4]).read().split()) if len(sys.argv) > 4 else set()
rng = random.Random(seed)
rng.shuffle(pages)
for p in pages:
    b = open(os.path.join(d, "pages", p["id"] + ".html"), "rb").read()
    p.update(features(b))
    p["country"] = "CA" if p["tld"] == "ca" else "US"
    lang = (p.get("languages") or "eng").split(",")[0]
    p["na"] = (lang == "eng" or (p["tld"] == "ca" and lang == "fra")) and (bool(NA.search(b)) or p["tld"] == "ca" or not (p["text"] or p["links"]))
    p["na"] = p["na"] and (p["tld"] == "ca" or not p["text"] and not p["links"] or bool(NA.search(b)))

plan = [("obfuscated", lambda p: p["obf"], 0.08), ("structured", lambda p: p["micro"], 0.10), ("jsonld", lambda p: p["ld"], 0.20),
        ("none", lambda p: not p["text"] and not p["links"] and not p["ld"] and not p["micro"], 0.17),
        ("links", lambda p: p["links"], 0.17), ("text", lambda p: p["text"], 1.0)]
chosen, hosts = [], set()
for name, pred, share in plan:
    quota = round(total * share) if share < 1 else total - len(chosen)
    pool = [p for p in pages if p["na"] and pred(p) and p["host"] not in hosts and not any(p is c for c in chosen)]
    pool.sort(key=lambda p: p["id"] not in kept)  # stable: reviewed pages first
    us = [p for p in pool if p["country"] == "US"]
    ca = [p for p in pool if p["country"] == "CA"]
    taken = 0
    i = j = 0
    while taken < quota and (i < len(us) or j < len(ca)):
        # two United States pages for each Canadian one, while both last
        order = [("us", 2), ("ca", 1)]
        for which, n in order:
            for _ in range(n):
                src, k = (us, i) if which == "us" else (ca, j)
                while k < len(src) and src[k]["host"] in hosts:
                    k += 1
                if k < len(src) and taken < quota:
                    p = src[k]
                    p["group"] = name
                    chosen.append(p)
                    hosts.add(p["host"])
                    taken += 1
                    k += 1
                if which == "us":
                    i = k
                else:
                    j = k
    print(name, taken, "of", quota)
chosen.sort(key=lambda p: p["id"])
with open(os.path.join(d, "web-picked.jsonl"), "w", encoding="utf-8") as fo:
    for p in chosen:
        fo.write(json.dumps(p) + "\n")
print("picked", len(chosen), "US", sum(p["country"] == "US" for p in chosen), "CA", sum(p["country"] == "CA" for p in chosen))
