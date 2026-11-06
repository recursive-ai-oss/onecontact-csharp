#!/usr/bin/env python3
"""Adjudication helper: shows items with the rules' pre-label and builds the gold file from the reviewer's corrections.

usage:
  labeling.py view  address|name <draft.jsonl> <rules-predictions.jsonl> [start] [count]
  labeling.py apply address|name <draft.jsonl> <rules-predictions.jsonl> <corrections.jsonl> <gold.jsonl> [labeler]

Corrections (one JSON object per line, only for items the rules got wrong or that must go):
  {"id": ..., "drop": true}
  {"keep": ["p000", "c012", ...]}       keeps only the items whose id ends with one of these suffixes
  address: {"id": ..., "no": "12", "rd": "N Main St", "pt": "p=N;n=Main;t=St", "un": ..., "box": ..., "rcp": ..., "ct": ..., "st": ..., "pc": ..., "cy": ...}
           replaces the pre-label with exactly these fields (pt = fine street parts p, n, t, d: pre-directional, name, type, post-directional);
           with "+": true the given keys edit the pre-label instead, and "-": ["un"] removes fields.
  name:    {"id": ..., "class": "person", "labels": "T G F"}
Every gold item records who labelled it and whether the rules' pre-label was accepted unchanged ("rules_agreed"). Items with no correction
were read and accepted. Nothing is printed by `apply` except counts.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score  # noqa: E402

ADDR_ORDER = ["recipient", "house_number", "road", "unit", "po_box", "city", "state", "postcode", "country"]
SHORT = {"recipient": "rcp", "house_number": "no", "road": "rd", "unit": "un", "po_box": "box", "city": "ct", "state": "st", "postcode": "pc", "country": "cy"}


def read(path):
    return [json.loads(l) for l in open(path, encoding="utf-8") if l.strip()]


def name_labels(item, pred):
    return " ".join(score.assign(item["text"], pred.get("parts", {}))) if pred else " ".join("O" for _ in item["text"].split())


def view(kind, draft, pred, start, count):
    items, preds = read(draft), {p["id"]: p for p in read(pred)}
    for it in items[start:start + count]:
        p = preds.get(it["id"], {})
        if kind == "address":
            f = p.get("fields", {})
            pr = p.get("parts", {})
            shown = " | ".join(f"{SHORT[k]}={f[k]}" for k in ADDR_ORDER if k in f)
            extra = (" [" + ",".join(f"{k[:3]}={v}" for k, v in pr.items()) + "]") if pr else ""
            print(f"{it['id'][-3:]} {it['text'].replace(chr(10), ' / ')} => {shown}{extra}")
        else:
            lab = name_labels(it, p).split()
            words = it["text"].split()
            print(f"{it['id'][-5:]} {p.get('class', '?')[:3]} " + " ".join(f"{w}/{l}" for w, l in zip(words, lab)))


def apply(kind, draft, pred, corr, out, labeler):
    items, preds = read(draft), {p["id"]: p for p in read(pred)}
    rows = read(corr) if os.path.exists(corr) else []
    fixes = {c["id"]: c for c in rows if "id" in c}
    # {"keep": [id suffix, ...]} keeps only the listed items (the others are dropped); handy for name drafts that are mostly page furniture.
    keep = [k for c in rows for k in c.get("keep", [])]
    agreed = corrected = dropped = 0
    with open(out, "w", encoding="utf-8") as f:
        for it in items:
            c = fixes.get(it["id"], {})
            if c.get("drop") or (keep and not any(it["id"].endswith("-" + k) or it["id"].endswith(k) for k in keep)):
                dropped += 1
                continue
            p = preds.get(it["id"], {})
            row = {"id": it["id"], "country": it["country"], "text": it["text"], "warc": it["warc"], "labeler": labeler}
            if kind == "address":
                if c and not c.get("+"):
                    fields, parts = {}, {}
                else:
                    fields, parts = dict(p.get("fields", {})), dict(p.get("parts", {}))
                long = {"no": "house_number", "rd": "road", "un": "unit", "box": "po_box", "rcp": "recipient", "ct": "city", "st": "state", "pc": "postcode", "cy": "country"}
                for short, name in long.items():
                    if short in c:
                        fields[name] = c[short]
                for short in c.get("-", []):
                    fields.pop(long[short], None)
                    if short == "rd":
                        parts = {}
                if "pt" in c:
                    pmap = {"p": "pre_directional", "n": "street_name", "t": "street_type", "d": "post_directional"}
                    parts = {}
                    for kv in c["pt"].split(";"):
                        k, v = kv.split("=", 1)
                        parts[pmap[k]] = v
                row.update({"fields": {k: v for k, v in fields.items() if v}, "parts": parts})
                changed = bool(c)
            else:
                labels = c.get("labels") or name_labels(it, p)
                cls = c.get("class") or p.get("class", "unknown")
                row.update({"class": cls, "labels": labels, "lang": it.get("lang", "")})
                changed = bool(c)
            row["rules_agreed"] = not changed
            agreed += not changed
            corrected += changed
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    print(f"gold items {agreed + corrected}: rules agreed {agreed}, corrected {corrected}; dropped {dropped}")


if __name__ == "__main__":
    a = sys.argv[1:]
    if a[0] == "view":
        view(a[1], a[2], a[3], int(a[4]) if len(a) > 4 else 0, int(a[5]) if len(a) > 5 else 40)
    else:
        apply(a[1], a[2], a[3], a[4], a[5], a[6] if len(a) > 6 else "agent adjudication")
