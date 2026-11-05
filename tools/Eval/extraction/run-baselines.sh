#!/usr/bin/env bash
# Runs the reference systems over an extraction evaluation set and writes one prediction file per system.
#
#   usage: run-baselines.sh web|text <set> <work dir>
#     web   <set> is the set directory (pages/ and pages.jsonl); the reference systems read each page's visible text
#           (the same text view the extractor reads) and their character spans are mapped back to bytes of the page.
#     text  <set> is a JSONL file of {id, country, text}; spans are UTF-16 code units of the text.
#   Needs in the work dir: gold.jsonl (its ids select the items). Writes jar.jsonl, mstext.jsonl, spacy.jsonl and ours.jsonl.
#
# Environment: EVAL_EXTRACT (the built eval-extract tool), JAVA and the jars (default: ~/onecontact/oracles/...), SPACY_PYTHON.
# The reference systems are only run: libphonenumber's findNumbers at the VALID leniency, Microsoft.Recognizers.Text's phone, email
# and URL finders, and spaCy's released English transformer pipeline. Their mapping to field types is in docs/extraction-eval-sets.md.
set -euo pipefail
kind=$1; set_path=$2; work=$3
here=$(cd "$(dirname "$0")/../.." && pwd)
oracles=${ONECONTACT_ORACLES:-$HOME/onecontact/oracles}
java=${JAVA:-$oracles/jdk/current/bin/java}
jar=$oracles/libphonenumber/9.0.43/libphonenumber-9.0.43.jar
json=$oracles/deps/json-20240303.jar
spacy_py=${SPACY_PYTHON:-$oracles/spacy-venv/bin/python}
py=${PYTHON:-python3}
x=$EVAL_EXTRACT
mkdir -p "$work"
"$py" -c "
import json
for l in open('$work/gold.jsonl'): print(json.loads(l)['id'])" > "$work/ids.txt"

if [ "$kind" = web ]; then
  "$x" visible "$set_path" "$work/in.jsonl" --ids "$work/ids.txt"
  "$x" ours-web "$set_path" "$work/ours.jsonl" --ids "$work/ids.txt"
  suffix=.chars.jsonl
else
  "$py" -c "
import json
ids = set(open('$work/ids.txt').read().split())
with open('$work/in.jsonl', 'w') as fo:
    for l in open('$set_path'):
        r = json.loads(l)
        if r['id'] in ids:
            fo.write(json.dumps({'id': r['id'], 'country': r['country'], 'text': r['text']}, ensure_ascii=False) + '\n')"
  "$x" ours-text "$work/in.jsonl" "$work/ours.jsonl"
  suffix=.jsonl
fi

"$java" -cp "$jar:$json" "$here/Oracles/Extraction/Matcher.java" "$work/in.jsonl" "$work/jar$suffix"
"$x" recognizers "$work/in.jsonl" "$work/mstext$suffix"
OMP_NUM_THREADS=${SPACY_THREADS:-8} "$spacy_py" "$here/Oracles/Extraction/run-spacy.py" "$work/in.jsonl" "$work/spacy$suffix"

if [ "$kind" = web ]; then
  for s in jar mstext spacy; do "$x" map "$set_path" "$work/$s.chars.jsonl" "$work/$s.jsonl"; done
fi
