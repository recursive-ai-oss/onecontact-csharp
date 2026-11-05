#!/usr/bin/env python3
"""Runs python-nameparser, probablepeople and cleanco as black boxes over the gold set and writes one JSON line per item.

usage: run-python-oracles.py <gold.tsv> <output directory>

The three packages are installed from PyPI into a virtual environment (see setup-oracles.sh) and only called through their public
functions as documented in their READMEs. Output files: nameparser.jsonl, probablepeople.jsonl, cleanco.jsonl, plus versions.json.
"""
import json
import os
import re
import sys
from importlib import metadata


def main():
    gold_path, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    rows = []
    with open(gold_path, encoding="utf-8") as f:
        next(f)
        for line in f:
            c = line.rstrip("\n").split("\t")
            rows.append((c[0], c[4]))

    from nameparser import HumanName
    import probablepeople
    from cleanco import basename

    with open(os.path.join(out_dir, "nameparser.jsonl"), "w", encoding="utf-8") as out:
        for i, text in rows:
            n = HumanName(text)
            parts = {"T": [n.title], "G": [n.first], "M": [n.middle], "F": [n.last], "S": [n.suffix], "N": [n.nickname]}
            out.write(json.dumps({"id": i, "parts": {k: [x for x in v if x] for k, v in parts.items()}}, ensure_ascii=False) + "\n")

    # probablepeople labels (documented in its README) mapped onto the gold letters.
    label_map = {
        "PrefixMarital": "T", "PrefixOther": "T", "GivenName": "G", "FirstInitial": "G", "MiddleName": "M", "MiddleInitial": "M",
        "Surname": "F", "LastInitial": "F", "SecondSurname": "F", "SuffixGenerational": "S", "SuffixOther": "S", "Nickname": "N",
        "CorporationName": "B", "CorporationLegalType": "L", "ShortForm": "B",
    }
    kinds = {"Person": "person", "Corporation": "company", "Household": "multiple"}
    with open(os.path.join(out_dir, "probablepeople.jsonl"), "w", encoding="utf-8") as out:
        for i, text in rows:
            parts = {}
            kind = "unknown"
            try:
                tagged, kind_name = probablepeople.tag(text)
                kind = kinds.get(kind_name, "unknown")
                for label, value in tagged.items():
                    letter = label_map.get(label)
                    if letter:
                        parts.setdefault(letter, []).append(value)
            except Exception:  # RepeatedLabelError and parse failures: fall back to the token-level parse
                try:
                    for token, label in probablepeople.parse(text):
                        letter = label_map.get(label)
                        if letter:
                            parts.setdefault(letter, []).append(token)
                except Exception:
                    pass
            out.write(json.dumps({"id": i, "class": kind, "parts": parts}, ensure_ascii=False) + "\n")

    def squash(s):
        return re.sub(r"\s+", " ", s).strip()

    with open(os.path.join(out_dir, "cleanco.jsonl"), "w", encoding="utf-8") as out:
        for i, text in rows:
            base = squash(basename(text))
            has_form = base != squash(text).strip(" ,.")
            removed = []
            if has_form:
                # the words cleanco took off are the legal form
                base_words = base.split()
                removed = [w for w in text.split() if w.strip(",.") not in [b.strip(",.") for b in base_words]]
            parts = {"B": [base] if base else [], "L": [" ".join(removed)] if removed else []}
            out.write(json.dumps({"id": i, "has_form": has_form, "parts": parts}, ensure_ascii=False) + "\n")

    versions = {}
    for pkg in ("nameparser", "probablepeople", "cleanco"):
        versions[pkg] = metadata.version(pkg)
    with open(os.path.join(out_dir, "versions.json"), "w", encoding="utf-8") as f:
        json.dump(versions, f, indent=2)


if __name__ == "__main__":
    main()
