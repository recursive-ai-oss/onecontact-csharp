#!/usr/bin/env python3
"""Runs probablepeople and python-nameparser (installed by setup-oracles.sh, only executed) over a name evaluation set.

usage: run-eval-names.py <gold.jsonl> <output directory>

Writes probablepeople.jsonl and nameparser.jsonl in the scorer's format ({"id", "class", "parts": {letter: [text]}}), plus versions.json.
Label mappings (the packages' documented labels -> our letters; T title, G given, M middle, F family, S suffix, N nickname, B company
base, L legal form):
  probablepeople: PrefixMarital, PrefixOther -> T; GivenName, FirstInitial -> G; MiddleName, MiddleInitial -> M; Surname, LastInitial,
  SecondSurname -> F; SuffixGenerational, SuffixOther -> S; Nickname -> N; CorporationName, ShortForm -> B; CorporationLegalType -> L.
  Its type Person -> person, Corporation -> company, Household -> multiple; a failed parse -> unknown, with the token-level parse used for parts.
  python-nameparser: title -> T, first -> G, middle -> M, last -> F, suffix -> S, nickname -> N. It has no classification and no
  company parts, so no class is written and it is scored on token accuracy only.
"""
import json
import os
import sys
from importlib import metadata

gold_path, out_dir = sys.argv[1], sys.argv[2]
os.makedirs(out_dir, exist_ok=True)
rows = []
with open(gold_path, encoding="utf-8") as f:
    for line in f:
        if line.strip():
            j = json.loads(line)
            rows.append((j["id"], j["text"]))

from nameparser import HumanName  # noqa: E402
import probablepeople  # noqa: E402

with open(os.path.join(out_dir, "nameparser.jsonl"), "w", encoding="utf-8") as out:
    for i, text in rows:
        n = HumanName(text)
        parts = {"T": [n.title], "G": [n.first], "M": [n.middle], "F": [n.last], "S": [n.suffix], "N": [n.nickname]}
        out.write(json.dumps({"id": i, "parts": {k: [x for x in v if x] for k, v in parts.items()}}, ensure_ascii=False) + "\n")

label_map = {"PrefixMarital": "T", "PrefixOther": "T", "GivenName": "G", "FirstInitial": "G", "MiddleName": "M", "MiddleInitial": "M", "Surname": "F",
             "LastInitial": "F", "SecondSurname": "F", "SuffixGenerational": "S", "SuffixOther": "S", "Nickname": "N", "CorporationName": "B",
             "CorporationLegalType": "L", "ShortForm": "B"}
kinds = {"Person": "person", "Corporation": "company", "Household": "multiple"}
with open(os.path.join(out_dir, "probablepeople.jsonl"), "w", encoding="utf-8") as out:
    for i, text in rows:
        parts, kind = {}, "unknown"
        try:
            tagged, kind_name = probablepeople.tag(text)
            kind = kinds.get(kind_name, "unknown")
            for label, value in tagged.items():
                letter = label_map.get(label)
                if letter:
                    parts.setdefault(letter, []).append(value)
        except Exception:  # repeated labels and other parse failures: fall back to the token-level parse
            try:
                for token, label in probablepeople.parse(text):
                    letter = label_map.get(label)
                    if letter:
                        parts.setdefault(letter, []).append(token)
            except Exception:
                pass
        out.write(json.dumps({"id": i, "class": kind, "parts": parts}, ensure_ascii=False) + "\n")

with open(os.path.join(out_dir, "versions.json"), "w", encoding="utf-8") as f:
    json.dump({p: metadata.version(p) for p in ("nameparser", "probablepeople")}, f)
print("items", len(rows))
