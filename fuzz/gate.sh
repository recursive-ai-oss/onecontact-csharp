#!/usr/bin/env bash
# Release fuzz run of several targets one after another, with a private engine folder per call so parallel calls do not share
# work directories:   fuzz/gate.sh <engine folder> <seconds> <target>...
# The engine folder holds links to the installed engine (afl, tools, src) taken from ONECONTACT_FUZZ (default ~/onecontact/fuzz).
set -uo pipefail
here=$(cd "$(dirname "$0")" && pwd)
private=${1:?usage: fuzz/gate.sh <engine folder> <seconds> <target>...}
seconds=${2:?seconds}
shift 2
engine=${ONECONTACT_FUZZ:-$HOME/onecontact/fuzz}
mkdir -p "$private"
for part in afl tools src; do ln -sfn "$engine/$part" "$private/$part"; done
export ONECONTACT_FUZZ=$private
status=0
for target in "$@"; do
  "$here/run.sh" "$target" "$seconds" || status=1
  echo "== $target"; cat "$private/work/results/$target.txt"
done
exit $status
