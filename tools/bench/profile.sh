#!/usr/bin/env bash
# usage: profile.sh <snapshot> <label>  -- runs the phase profiles (phone, extraction, names) from a snapshot, with a load record.
cd "$(dirname "$0")/../.."
. tools/bench/env.sh
export DOTNET_PROCESSOR_COUNT=4
snap=${ONECONTACT_WORK:-$HOME/onecontact/work}/bench/snap/$1
OUT=${ONECONTACT_WORK:-$HOME/onecontact/work}/bench/results/$2
mkdir -p "$OUT"
export ONECONTACT_NAMES_DEV=$(ls "$HOME"/onecontact/data/train/names-us-ca-v1/dev/* 2>/dev/null | head -1)
{ echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2)"; echo "cores: $(nproc)"; echo "before: $(cat /proc/loadavg)"; } > "$OUT/load.txt"
for mode in profile-phone profile-extract profile-names accuracy; do
  echo "== $mode"
  nice -n 5 dotnet "$snap/RecursiveAI.OneContact.Bench.dll" $mode 2>&1
  echo "after $mode: $(cat /proc/loadavg)" >> "$OUT/load.txt"
done | tee "$OUT/profile.txt"
cat "$OUT/load.txt"
