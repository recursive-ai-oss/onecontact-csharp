#!/usr/bin/env bash
# Coverage-guided fuzzing of one target:   fuzz/run.sh <target> <seconds>
#
# Builds the fuzz host, instruments the library assemblies with SharpFuzz, builds the seed corpus for the target from the repository's
# own fixtures, runs AFL++ for the given time and writes the result to $WORK/results/<target>.txt (executions, speed, crashes,
# hangs, coverage). The exit status is 1 when AFL++ saved a crash or a hang, 0 when it saved none. Install the engine first with
# fuzz/setup.sh. `fuzz/run.sh list` prints the targets.
#
# Environment: ONECONTACT_FUZZ (default ~/onecontact/fuzz) holds the engine and the work folder.
# The model targets add seeds from weights files when ONECONTACT_TIER1_WEIGHTS / ONECONTACT_MODELS_WEIGHTS name their folders.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FUZZ_HOME="${ONECONTACT_FUZZ:-$HOME/onecontact/fuzz}"
WORK="$FUZZ_HOME/work"
TARGETS="phone phone-metadata short-number-metadata phone-match phone-typing email web idna address address-hint address-validate person company normalize extract-html extract-text model-tier1 model-superset json tables canary"

if [ "${1:-}" = "list" ]; then echo "$TARGETS"; exit 0; fi
if [ $# -ne 2 ]; then echo "usage: fuzz/run.sh <target> <seconds>   (targets: $TARGETS)" >&2; exit 64; fi
TARGET="$1"; SECONDS_TO_RUN="$2"
case " $TARGETS " in *" $TARGET "*) ;; *) echo "unknown target '$TARGET'; targets: $TARGETS" >&2; exit 64 ;; esac
[ -x "$FUZZ_HOME/afl/afl-fuzz" ] && [ -x "$FUZZ_HOME/tools/sharpfuzz" ] || { echo "run fuzz/setup.sh first" >&2; exit 1; }

mkdir -p "$WORK/results"

# ---- build once per run and instrument a private copy -------------------------------------------------------------------------
PUBLISH="$WORK/publish"
HOST="$WORK/host-$TARGET"
dotnet publish "$REPO/fuzz/OneContact.Fuzz" -c Release -o "$PUBLISH" -v quiet -clp:NoSummary
rm -rf "$HOST"; cp -r "$PUBLISH" "$HOST"
# Instrument our assemblies and the Recursive AI libraries they read through (OneHtml, OneNlp, OneModel); never the fuzz host or SharpFuzz.
for dll in "$HOST"/RecursiveAI.*.dll; do
  case "$(basename "$dll")" in RecursiveAI.OneContact.Fuzz.dll) continue ;; esac
  "$FUZZ_HOME/tools/sharpfuzz" "$dll" >>"$WORK/instrument-$TARGET.log" 2>&1 || echo "note: $(basename "$dll") was not instrumented" >&2
done

# ---- seeds ----------------------------------------------------------------------------------------------------------------------
SEEDS="$WORK/seeds/$TARGET"
rm -rf "$SEEDS"; mkdir -p "$SEEDS"
cp -r "$REPO/testdata/fuzz/$TARGET/." "$SEEDS/" 2>/dev/null || true

# Writes an unsigned 64-bit integer as eight little-endian bytes.
le64() { local i; for i in 0 1 2 3 4 5 6 7; do printf "\\x$(printf %02x $(( ($1 >> (8 * i)) & 255 )))"; done; }

# One file per line of column $2 of the tab- or bar-separated fixture $1 (comments skipped); a backslash-n in the text is a line break.
lines_to_seeds() { # file column separator limit
  awk -F"$3" -v col="$2" -v dir="$SEEDS" -v limit="$4" -v name="$(basename "$1")" \
    '/^#/ || NF < col || $col == "" { next } { n++; if (n > limit) exit; t = $col; gsub(/\\n/, "\n", t); f = sprintf("%s/%s-%04d", dir, name, n); printf "%s", t > f; close(f) }' "$1"
}
files_to_seeds() { # directory glob
  local f
  for f in $1/$2; do [ -f "$f" ] && [ "$(wc -c <"$f")" -lt 65536 ] && cp "$f" "$SEEDS/$(basename "$f")"; done
  return 0
}
T="$REPO/testdata"
case "$TARGET" in
  address|address-hint|address-validate)
    lines_to_seeds "$T/addresses/parse-gold.tsv" 3 $'\t' 150
    for f in "$T"/addresses/authority-examples*.tsv; do lines_to_seeds "$f" 4 $'\t' 60; done ;;
  person) lines_to_seeds "$T/names/person-cases.psv" 3 '|' 300; lines_to_seeds "$T/names/multi-cases.psv" 3 '|' 100 ;;
  company) lines_to_seeds "$T/names/company-cases.psv" 3 '|' 300 ;;
  normalize) lines_to_seeds "$T/names/gold.tsv" 5 $'\t' 100 ;;
  email) lines_to_seeds "$REPO/tests/OneContact.Tests/Email/corpus/rfc5322.cases" 3 '¦' 150 ;;
  extract-html) files_to_seeds "$T/extraction/pages" '*.html' ;;
  extract-text) files_to_seeds "$T/extraction/text/golden" '*.txt' ;;
  model-tier1|model-superset)
    # A real pipeline file is large; prefixes of it reach the header and the first tables. The weights are not in the repository.
    dir="${ONECONTACT_TIER1_WEIGHTS:-}"; [ "$TARGET" = "model-superset" ] && dir="${ONECONTACT_MODELS_WEIGHTS:-}"
    if [ -n "$dir" ] && [ -d "$dir" ]; then
      for w in "$dir"/*.onenlp; do
        head -c 2048 "$w" >"$SEEDS/prefix-2k"; head -c 65536 "$w" >"$SEEDS/prefix-64k"
        # The loader checks the declared lengths against the file first, so a plain prefix stops there. These keep the header and the
        # whole config but declare a short weights section, which lets mutation reach the config reader and the tensor checks.
        config_length=$(od -An -t u8 -j 16 -N 8 "$w" | tr -d ' ')
        for weights_length in 0 4096; do
          { head -c 24 "$w"; le64 "$weights_length"; head -c $((32 + config_length + weights_length)) "$w" | tail -c +33; } >"$SEEDS/short-weights-$weights_length"
        done
      done
    fi ;;
esac
if [ -z "$(ls -A "$SEEDS")" ]; then printf '0' >"$SEEDS/zero"; fi   # AFL++ needs at least one input

# ---- run ------------------------------------------------------------------------------------------------------------------------
OUT="$WORK/out/$TARGET"
rm -rf "$OUT"; mkdir -p "$OUT"
export AFL_SKIP_CPUFREQ=1 AFL_I_DONT_CARE_ABOUT_MISSING_CRASHES=1 AFL_NO_UI=1 AFL_SKIP_BIN_CHECK=1 AFL_NO_AFFINITY=1 AFL_AUTORESUME=0
export DOTNET_TieredPGO=0 DOTNET_gcServer=0 DOTNET_CLI_TELEMETRY_OPTOUT=1
set +e
nice "$FUZZ_HOME/afl/afl-fuzz" -i "$SEEDS" -o "$OUT" -V "$SECONDS_TO_RUN" -t 5000 -m none -- \
  dotnet "$HOST/RecursiveAI.OneContact.Fuzz.dll" afl "$TARGET" >"$WORK/afl-$TARGET.log" 2>&1
afl_status=$?
set -e

# ---- result ---------------------------------------------------------------------------------------------------------------------
STATS="$OUT/default/fuzzer_stats"
if [ ! -f "$STATS" ]; then echo "AFL++ produced no statistics (exit $afl_status); see $WORK/afl-$TARGET.log" >&2; tail -n 20 "$WORK/afl-$TARGET.log" >&2; exit 2; fi
afl_stat() { awk -F' *: *' -v k="$1" '$1 == k { print $2 }' "$STATS"; }
CRASHES="$(afl_stat saved_crashes)"; HANGS="$(afl_stat saved_hangs)"
{
  echo "target:           $TARGET"
  echo "engine:           AFL++ $(cat "$FUZZ_HOME/afl/VERSION") with SharpFuzz 2.3.0 (library and OneHtml/OneNlp/OneModel assemblies instrumented)"
  echo "seconds:          $(afl_stat run_time)"
  echo "executions:       $(afl_stat execs_done)"
  echo "executions/s:     $(afl_stat execs_per_sec)"
  echo "corpus:           $(afl_stat corpus_count) inputs, $(afl_stat edges_found) edges found, map coverage $(afl_stat bitmap_cvg)"
  echo "crashes:          $CRASHES"
  echo "hangs:            $HANGS"
  echo "crash inputs:     $OUT/default/crashes (copy a minimized one to testdata/fuzz-repros/$TARGET/ and add a test)"
} | tee "$WORK/results/$TARGET.txt"
if [ "${CRASHES:-0}" != "0" ] || [ "${HANGS:-0}" != "0" ]; then exit 1; fi
