#!/usr/bin/env python3
"""Scores tool outputs against the synthetic gold set.

usage: score.py <gold.tsv> <tool>=<output.jsonl> ... [--show NAME] [--markdown]

Token accuracy: of the white-space-separated words of person and several-people items, the share whose label (T, G, M, F, S, N, or O
for anything else) the tool got right; the tool's parts are assigned to the words by matching text, in order. Classification
accuracy: the share of all items whose class (person, company, multiple) the tool got right. Legal-form detection: the share of all
items where the tool's has-a-legal-form answer is right. ELF-code accuracy: of the items with a legal form whose code is unique in
its country, the share where the tool gave that code.
"""
import json
import re
import sys
import unicodedata
from collections import defaultdict


def norm(w):
    w = unicodedata.normalize("NFKD", w.lower())
    w = "".join(c for c in w if not unicodedata.combining(c))
    return re.sub(r"[\W_]+", "", w)


def assign(text, parts):
    """Gives each word of text the label of the first unused tool part word that matches it."""
    pool = []
    for label, values in parts.items():
        for v in values:
            for w in v.split():
                if norm(w):
                    pool.append([label, norm(w), False])
    labels = []
    for tok in text.split():
        n = norm(tok)
        lab = "O"
        for entry in pool:
            if not entry[2] and entry[1] == n and n:
                entry[2] = True
                lab = entry[0]
                break
        labels.append(lab)
    return labels


def name_has_company_parts(o):
    return "B" in o["parts"] or "L" in o["parts"]


def main():
    args = sys.argv[1:]
    show = None
    if "--show" in args:
        i = args.index("--show")
        show = args[i + 1]
        del args[i:i + 2]
    markdown = "--markdown" in args
    args = [a for a in args if a != "--markdown"]
    gold = {}
    with open(args[0], encoding="utf-8") as f:
        next(f)
        for line in f:
            c = line.rstrip("\n").split("\t")
            gold[c[0]] = dict(lang=c[1], hint=c[2], kind=c[3], text=c[4], labels=c[5].split(), has_form=c[6] == "1", elf=c[7])
    tools = {}
    for a in args[1:]:
        name, path = a.split("=", 1)
        tools[name] = {}
        with open(path, encoding="utf-8") as f:
            for line in f:
                d = json.loads(line)
                tools[name][d["id"]] = d

    langs = sorted({g["lang"] for g in gold.values()}, key=lambda x: (x != "en", x))
    # stats[tool][lang] = dict(tok_ok, tok_n, cls_ok, cls_n, form_ok, form_n, elf_ok, elf_n)
    stats = {t: defaultdict(lambda: defaultdict(int)) for t in tools}
    for t, outs in tools.items():
        for i, g in gold.items():
            o = outs.get(i)
            if o is None:
                continue
            for key in (g["lang"], "ALL"):
                s = stats[t][key]
                if t != "cleanco" and g["kind"] in ("person", "multiple") and "parts" in o and o.get("class") != "company" or (t != "cleanco" and g["kind"] in ("person", "multiple") and "parts" in o and "class" not in o):
                    pred = assign(g["text"], o["parts"])
                    # Fold the gold labels onto the same alphabet: company labels do not occur here.
                    for p, q in zip(pred, g["labels"]):
                        s["tok_n"] += 1
                        s["tok_ok"] += p == q
                elif t != "cleanco" and g["kind"] in ("person", "multiple"):
                    for _ in g["labels"]:
                        s["tok_n"] += 1
                if g["kind"] == "company" and "parts" in o and o.get("class", "company") == "company" and name_has_company_parts(o):
                    pred = assign(g["text"], o["parts"])
                    for p, q in zip(pred, g["labels"]):
                        s["ctok_n"] += 1
                        s["ctok_ok"] += p == q
                elif g["kind"] == "company" and "class" in o:
                    for _ in g["labels"]:
                        s["ctok_n"] += 1
                if "class" in o:
                    s["cls_n"] += 1
                    s["cls_ok"] += o["class"] == g["kind"]
                if "has_form" in o:
                    s["form_n"] += 1
                    s["form_ok"] += bool(o["has_form"]) == g["has_form"]
                    if g["has_form"] and g["elf"] not in ("", "?") and "elf" in o:
                        s["elf_n"] += 1
                        s["elf_ok"] += o.get("elf") == g["elf"]

    def pct(a, b):
        return "n/a" if b == 0 else f"{100.0 * a / b:5.1f}"

    names = list(tools)
    if markdown:
        print("| language | " + " | ".join(f"{n} person tok" for n in names) + " | " + " | ".join(f"{n} cls" for n in names) + " | " + " | ".join(f"{n} form" for n in names) + " | " + " | ".join(f"{n} company tok" for n in names) + " |")
        print("|---|" + "---|" * (4 * len(names)))
    else:
        print(f"{'lang':5} " + " ".join(f"{n[:10]:>10}" for n in names) + " | class | " + " ".join(f"{n[:10]:>10}" for n in names) + " | form | " + " ".join(f"{n[:10]:>10}" for n in names))
    for lang in langs + ["ALL"]:
        tok = [pct(stats[t][lang]["tok_ok"], stats[t][lang]["tok_n"]) for t in names]
        cls = [pct(stats[t][lang]["cls_ok"], stats[t][lang]["cls_n"]) for t in names]
        frm = [pct(stats[t][lang]["form_ok"], stats[t][lang]["form_n"]) for t in names]
        ctk = [pct(stats[t][lang]["ctok_ok"], stats[t][lang]["ctok_n"]) for t in names]
        if markdown:
            print(f"| {lang} | " + " | ".join(tok) + " | " + " | ".join(cls) + " | " + " | ".join(frm) + " | " + " | ".join(ctk) + " |")
        else:
            print(f"{lang:5} " + " ".join(f"{x:>10}" for x in tok) + " | class | " + " ".join(f"{x:>10}" for x in cls) + " | form | " + " ".join(f"{x:>10}" for x in frm) + " | company tok | " + " ".join(f"{x:>10}" for x in ctk))
    for t in names:
        s = stats[t]["ALL"]
        if s["elf_n"]:
            print(f"ELF code accuracy {t}: {s['elf_ok']}/{s['elf_n']} = {pct(s['elf_ok'], s['elf_n'])}")
    if show:
        for i, g in gold.items():
            o = tools[show].get(i)
            if o is None:
                continue
            bad = False
            if "class" in o and o["class"] != g["kind"]:
                bad = True
            if g["kind"] in ("person", "multiple") and "parts" in o and o.get("class") != "company":
                bad |= assign(g["text"], o["parts"]) != g["labels"]
            if "has_form" in o and bool(o["has_form"]) != g["has_form"]:
                bad = True
            if g["has_form"] and g["elf"] not in ("", "?") and o.get("elf") != g["elf"]:
                bad = True
            if bad:
                print(i, g["lang"], g["kind"], repr(g["text"]), "gold", " ".join(g["labels"]), "| got", o.get("class"), json.dumps(o.get("parts"), ensure_ascii=False), o.get("has_form"), o.get("elf"), "gold-elf", g["elf"])


if __name__ == "__main__":
    main()
