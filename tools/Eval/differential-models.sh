#!/usr/bin/env bash
# usage: tools/Eval/differential-models.sh <label>
# Builds this tree and, under $HOME/onecontact/differential-models/<label>/, writes what each shipped model reads: the model tools' address output over the first lines of every
# register dev render (hinted and with no hint) and the extractor's output on the private extraction sets. Each of the three models (Tier 1, the Tier 1 + Tier 2 model, the
# accurate tier) runs on its own weights. Run it in two trees (for example before and after a library bump) and compare the outputs with cmp: only counts and hashes of the
# private outputs should leave the machine.
# Needs TIER1_MODEL, MODELS_MODEL and ACCURATE_MODEL (the saved pipelines), ONECONTACT_UGC_IN (CRM/UGC items, jsonl) and the extraction sets under $ONECONTACT_EVAL.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
label=${1:?label}
: "${TIER1_MODEL:?}" "${MODELS_MODEL:?}" "${ACCURATE_MODEL:?}"
out=$HOME/onecontact/differential-models/$label
mkdir -p "$out"
export DOTNET_PROCESSOR_COUNT=4
for p in Models ModelsAccurate Extract ExtractAccurate; do
  nice dotnet build "$repo/tools/Eval/$p" -c Release -m:4 -v quiet -clp:NoSummary 2>&1 | grep -E 'error|warn' | sort -u | head || true
done
dll() { find "$repo/artifacts/bin/$1/release" -name "RecursiveAI.OneContact.Eval*.dll" | head -1; }
models=$(dll OneContact.Eval.Models)
accurate=$(dll OneContact.Eval.ModelsAccurate)
extract=$(dll OneContact.Eval.Extract)
extract_acc=$(dll OneContact.Eval.ExtractAccurate)
rendered=${ONECONTACT_RENDERED:-$HOME/onecontact/data/rendered}
python3 - "$out/in.jsonl" "$rendered" "${ONECONTACT_US_CA:-$HOME/onecontact/u9/out}" <<'PY'
import glob, json, sys, os
out, *dirs = sys.argv[1:]
with open(out, "w") as w:
    for d in dirs:
        for f in sorted(glob.glob(os.path.join(d, "**", "*.dev.jsonl"), recursive=True)):
            cc = os.path.basename(f)[:2].upper()
            for i, line in enumerate(open(f)):
                if i >= 300:
                    break
                w.write(json.dumps({"id": f"{cc}-{os.path.basename(os.path.dirname(f))}-{i}", "text": json.loads(line)["text"], "country": cc}) + "\n")
PY
wc -l "$out/in.jsonl"
env -u ONECONTACT_MODELS_MODEL ONECONTACT_TIER1_MODEL=$TIER1_MODEL dotnet "$models" address "$out/in.jsonl" "$out/addr-tier1.jsonl" > /dev/null
env -u ONECONTACT_MODELS_MODEL ONECONTACT_TIER1_MODEL=$TIER1_MODEL dotnet "$models" address "$out/in.jsonl" "$out/addr-tier1-hint.jsonl" hint > /dev/null
ONECONTACT_MODELS_MODEL=$MODELS_MODEL dotnet "$models" address "$out/in.jsonl" "$out/addr-models.jsonl" > /dev/null
ONECONTACT_MODELS_MODEL=$MODELS_MODEL dotnet "$models" address "$out/in.jsonl" "$out/addr-models-hint.jsonl" hint > /dev/null
ONECONTACT_ACCURATE_MODEL=$ACCURATE_MODEL dotnet "$accurate" address "$out/in.jsonl" "$out/addr-accurate.jsonl" > /dev/null
ONECONTACT_ACCURATE_MODEL=$ACCURATE_MODEL dotnet "$accurate" address "$out/in.jsonl" "$out/addr-accurate-hint.jsonl" hint > /dev/null
frozen=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}/extraction
ugc=${ONECONTACT_UGC_IN:?set ONECONTACT_UGC_IN to the CRM/UGC evaluation inputs (jsonl)}
python3 -c "
import json, sys
for l in open(sys.argv[1]): print(json.loads(l)['id'])" "$frozen/frozen/gold-web.jsonl" > "$out/web.ids"
for m in tier1 models; do
  if [ $m = tier1 ]; then e=(env -u ONECONTACT_MODELS_MODEL ONECONTACT_TIER1_MODEL=$TIER1_MODEL); else e=(env ONECONTACT_MODELS_MODEL=$MODELS_MODEL); fi
  "${e[@]}" dotnet "$extract" ours-web "$frozen/web" "$out/web-$m.jsonl" --ids "$out/web.ids" > /dev/null
  "${e[@]}" dotnet "$extract" ours-text "$ugc" "$out/text-$m.jsonl" > /dev/null
done
ONECONTACT_ACCURATE_MODEL=$ACCURATE_MODEL dotnet "$extract_acc" ours-web "$frozen/web" "$out/web-accurate.jsonl" --ids "$out/web.ids" > /dev/null
ONECONTACT_ACCURATE_MODEL=$ACCURATE_MODEL dotnet "$extract_acc" ours-text "$ugc" "$out/text-accurate.jsonl" > /dev/null
sha256sum "$out"/*.jsonl | sed "s#$out/##" > "$out/SHA256SUMS"
wc -l "$out"/*.jsonl | tail -20
