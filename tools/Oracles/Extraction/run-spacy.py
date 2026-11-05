#!/usr/bin/env python3
"""Runs spaCy's released English transformer pipeline over {id, text} lines and writes its PERSON and ORG spans.

usage: run-spacy.py <in.jsonl> <out.jsonl>

The pipeline (en_core_web_trf, version recorded by setup-spacy.sh) is only run. Mapping to field types: PERSON -> person, ORG -> org;
every other label is dropped. Offsets are converted from spaCy's code points to UTF-16 code units, which is what the scorer counts.
"""
import json
import sys

import spacy

LABELS = {"PERSON": "person", "ORG": "org"}
nlp = spacy.load("en_core_web_trf", disable=["lemmatizer"])


def utf16_offsets(text):
    """Index i holds the UTF-16 offset of code point i (and one past the end)."""
    out, pos = [], 0
    for ch in text:
        out.append(pos)
        pos += 2 if ord(ch) > 0xFFFF else 1
    out.append(pos)
    return out


items = [json.loads(l) for l in open(sys.argv[1], encoding="utf-8") if l.strip()]
with open(sys.argv[2], "w", encoding="utf-8") as fo:
    for item, doc in zip(items, nlp.pipe((i["text"][:100000] for i in items), batch_size=8)):
        m = utf16_offsets(item["text"])
        fields = [{"type": LABELS[e.label_], "start": m[e.start_char], "end": m[e.end_char], "text": e.text}
                  for e in doc.ents if e.label_ in LABELS]
        fo.write(json.dumps({"id": item["id"], "fields": fields}, ensure_ascii=False) + "\n")
print("items", len(items))
