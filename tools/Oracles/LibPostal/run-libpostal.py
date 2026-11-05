#!/usr/bin/env python3
"""Runs libpostal's address parser (a released binary distribution, only executed) over an address evaluation set.

usage: run-libpostal.py <gold.jsonl> <predictions.jsonl> <data-dir>

<data-dir> is a libpostal data directory: the base model for "libpostal 1.1", or the base directory with the parser files replaced
for the Senzing model (see setup.sh). The Python binding `postal` is called through its documented `parse_address`.

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
