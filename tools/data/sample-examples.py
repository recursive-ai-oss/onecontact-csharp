#!/usr/bin/env python3
"""Prints a few rendered examples of one output file (text and layout), for a quick look at a generator run.

Usage: sample-examples.py <file.jsonl> [count]
"""
import json
import random
import sys

lines = open(sys.argv[1], encoding="utf-8").read().splitlines()
random.Random(1).shuffle(lines)
for line in lines[: int(sys.argv[2]) if len(sys.argv) > 2 else 6]:
    ex = json.loads(line)
    print(json.dumps(ex["text"], ensure_ascii=False), ex["layout"], [(ex["text"][s:e], l) for s, e, l in ex["entities"]][:12])
