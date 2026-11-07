#!/usr/bin/env bash
# Runs every phone parity comparison for one seed against the pinned libphonenumber jars:
#   run-phone-gate.sh <work dir> <seed> [geo prefixes per calling code]
# Steps: core operations (Phones/run.sh), as-you-type, matcher and short numbers (Phones/run-extra.sh), geocoder, carrier and time
# zones (PhonesGeo/run.sh). Each step leaves its diff.txt in <work dir>/<step>/; the last line of the summary says how many results
# were compared and how many differ. Needs the environment of the individual scripts (ONECONTACT_ORACLES, ONECONTACT_UPSTREAM).
set -uo pipefail
here=$(cd "$(dirname "$0")" && pwd)
work=${1:?usage: run-phone-gate.sh <work dir> <seed> [prefixes]}
seed=${2:?seed}
prefixes=${3:-200}
status=0
mkdir -p "$work"
# The comparison scripts build with --no-restore, so restore the two tool projects first.
dotnet restore "$here/Phones/OneContact.Oracles.Phones.csproj" -v q > "$work/restore.log" 2>&1 || { cat "$work/restore.log"; exit 1; }
dotnet restore "$here/PhonesGeo/OneContact.Oracles.PhonesGeo.csproj" -v q >> "$work/restore.log" 2>&1 || { cat "$work/restore.log"; exit 1; }
SHOW_LINES=0 "$here/Phones/run.sh" "$work/core" "$seed" > "$work/core.log" 2>&1 || status=1
SHOW_LINES=0 "$here/Phones/run-extra.sh" "$work/extra" "$seed" > "$work/extra.log" 2>&1 || status=1
SHOW_LINES=0 "$here/PhonesGeo/run.sh" "$work/geo" "$seed" "$prefixes" > "$work/geo.log" 2>&1 || status=1
for step in core extra geo; do
  echo "== $step (seed $seed)"
  head -3 "$work/$step/diff.txt" 2>/dev/null || { echo "no report"; tail -5 "$work/$step.log"; }
done
exit $status
