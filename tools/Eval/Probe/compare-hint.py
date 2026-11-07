#!/usr/bin/env python3
"""Compares the probe's no-hint reads with the hinted reads of the same texts.

usage: run.sh cases.txt > out.txt; compare-hint.py out.txt

The cases file lists each text first with no hint ("|text") and then again with a country ("CC|text"), in the same order. The probe prints two lines per case. A case is printed
when the two reads differ in anything but the facts column, or when the no-hint read is a refusal.
"""
import re
import sys

lines = open(sys.argv[1], encoding="utf-8").read().split("\n")
pairs = [(lines[i], lines[i + 1]) for i in range(0, len(lines) - 1, 2) if lines[i].strip()]
half = len(pairs) // 2


def strip_facts(s):
    return re.sub(r"facts=[^;]*;", "", s).strip()


same = 0
for (text, no_hint), (_, hinted) in zip(pairs[:half], pairs[half:]):
    if strip_facts(no_hint) == strip_facts(hinted) and not no_hint.strip().startswith(("ZZ", "REFUSED")):
        same += 1
        continue
    print(text.split("|", 1)[1])
    print("   no hint:", strip_facts(no_hint)[:200])
    print("   hinted: ", strip_facts(hinted)[:200])
print(f"same {same} of {half}")
