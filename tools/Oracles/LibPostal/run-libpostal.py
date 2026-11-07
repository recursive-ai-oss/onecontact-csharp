#!/usr/bin/env python3
"""Runs libpostal's address parser (a released binary distribution, only executed) over an address evaluation set.

usage: run-libpostal.py <gold.jsonl> <predictions.jsonl> <data-dir> [country|country-text]

<data-dir> is a libpostal data directory: the base model for "libpostal 1.1", or the base directory with the parser files replaced
for the Senzing model (see setup.sh). The Python binding `postal` is called through its documented `parse_address`.
With the optional fourth argument `country`, each item's `country` field (an ISO 3166 alpha-2 code, lower-cased) is passed as the documented
`country` keyword of `parse_address`, which is how a caller who knows the country uses the parser; without it the parser gets the text alone.
The binding rejects a country given with no language (TypeError, observed), so the language is passed as the empty string, meaning none.
The keyword changes nothing in the parse (identical output on every item of a 122-item Swiss set, observed), so a second way to tell the parser
the country is offered: `country-text` appends the country's English name to the text as a last component (unless the text already has it), the way a caller who knows the
country would complete an address for libpostal. The country field of that parse is then replaced by the one parsed from the unextended text
(the scorer must not be given a country the page never wrote).

Label mapping (libpostal's documented component labels -> the scorer's coarse labels):
  house -> recipient          house_number -> house_number     road -> road
  unit, level, staircase, entrance -> unit (values joined in output order)
  po_box -> po_box            city -> city                     state -> state
  postcode -> postcode        country -> country
  suburb, city_district -> city, only when the parse has no `city` label
  every other label (category, near, state_district, country_region, island, world_region) is not scored as a prediction.
Multi-line input is given to the parser as its lines joined with ", ". When a label occurs more than once its values are joined in
output order. Values are used as returned (libpostal lower-cases them); the scorer compares case-insensitively.
"""
import json
import os
import sys

gold_path, out_path, data_dir = sys.argv[1], sys.argv[2], sys.argv[3]
mode = sys.argv[4] if len(sys.argv) > 4 else ""
NAMES = {"US": "United States", "CA": "Canada", "MX": "Mexico", "AU": "Australia", "GB": "United Kingdom", "JE": "Jersey", "GG": "Guernsey",
         "IM": "Isle of Man", "IE": "Ireland", "AT": "Austria", "BE": "Belgium", "HR": "Croatia", "CZ": "Czech Republic", "DK": "Denmark",
         "EE": "Estonia", "FI": "Finland", "FR": "France", "DE": "Germany", "HU": "Hungary", "IT": "Italy", "LV": "Latvia", "LT": "Lithuania",
         "LU": "Luxembourg", "MT": "Malta", "NL": "Netherlands", "PL": "Poland", "PT": "Portugal", "RO": "Romania", "SK": "Slovakia",
         "SI": "Slovenia", "ES": "Spain", "SE": "Sweden", "CH": "Switzerland", "NO": "Norway", "IS": "Iceland", "LI": "Liechtenstein",
         "AD": "Andorra", "MC": "Monaco", "SM": "San Marino", "VA": "Vatican City"}
os.environ["LIBPOSTAL_DATA_DIR"] = data_dir
from postal.parser import parse_address  # noqa: E402  (imported after the data directory is set)

DIRECT = {"house": "recipient", "house_number": "house_number", "road": "road", "po_box": "po_box", "city": "city", "state": "state",
          "postcode": "postcode", "country": "country", "unit": "unit", "level": "unit", "staircase": "unit", "entrance": "unit"}

n = 0
with open(gold_path, encoding="utf-8") as f, open(out_path, "w", encoding="utf-8") as out:
    for line in f:
        if not line.strip():
            continue
        item = json.loads(line)
        text = ", ".join(x.strip() for x in item["text"].split("\n") if x.strip())
        cc = (item.get("country") or "").upper()
        if mode == "country" and cc:
            parsed = parse_address(text, language="", country=cc.lower())
        elif mode == "country-text" and cc in NAMES:
            # Not appended when the text already names the country in English (a repeated country confuses the parser, which is no help to anyone).
            more = "" if NAMES[cc].lower() in text.lower() else ", " + NAMES[cc]
            parsed = [(v, l) for v, l in parse_address(text + more) if l != "country"]
            parsed += [(v, l) for v, l in parse_address(text) if l == "country"]
        else:
            parsed = parse_address(text)
        fields = {}
        for value, label in parsed:
            key = DIRECT.get(label)
            if key:
                fields[key] = (fields[key] + " " + value) if key in fields else value
        if "city" not in fields:
            extra = [v for v, l in parsed if l in ("suburb", "city_district")]
            if extra:
                fields["city"] = " ".join(extra)
        out.write(json.dumps({"id": item["id"], "fields": fields}, ensure_ascii=False) + "\n")
        n += 1
print("items", n)
