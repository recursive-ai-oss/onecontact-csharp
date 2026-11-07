#!/usr/bin/env bash
# Runs the exact-agreement rows that need no oracle at run time, and re-derives the structural reference:
#   run-conformance-gate.sh <work dir>
# 1. Asks the libaddressinput reference build the generated cases again (make-reference.sh) and checks that its answers are
#    byte-identical to the pinned ones in testdata/addresses-structural, so the pinned answers are the reference's answers.
# 2. Builds the test project and runs the conformance classes (structural parity, WPT URL, Public Suffix List, email corpus and
#    isemail, authority examples of every group), printing the runner's totals for each class.
# Needs the .NET SDK, the fetched upstream files (ONECONTACT_UPSTREAM), the oracle installs (ONECONTACT_ORACLES) and a restored solution.
set -uo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
work=${1:?usage: run-conformance-gate.sh <work dir>}
mkdir -p "$work"
status=0
pinned=$root/testdata/addresses-structural

mkdir -p "$work/pinned"
cp "$pinned"/* "$work/pinned/"
if [ -n "${TESTS_ONLY:-}" ]; then
  echo "(reference run skipped)"
elif bash "$root/tools/Oracles/Addresses/make-reference.sh" > "$work/reference.log" 2>&1; then
  for f in oracle-formats.tsv; do cmp "$work/pinned/$f" "$pinned/$f" && echo "same: $f"; done
  for f in cases.tsv.gz oracle-results.tsv.gz; do
    if cmp <(zcat "$work/pinned/$f") <(zcat "$pinned/$f"); then echo "same: $f ($(zcat "$pinned/$f" | wc -l) lines)"; else echo "DIFFERENT: $f"; status=1; fi
  done
else
  echo "reference run failed"; tail -5 "$work/reference.log"; status=1
fi
cp "$work/pinned/"* "$pinned/"   # leave the checkout as it was

dotnet build "$root/tests/OneContact.Tests" -c Release -v q -clp:NoSummary -m:4 2>&1 | tail -3
exe=$(find "$root/artifacts/bin" -name 'RecursiveAI.OneContact.Tests' -type f 2>/dev/null | head -1)
echo "test host: $exe"
for c in AddressStructuralParityTests WptUrlTests PublicSuffixListTests EmailConformanceTests AuthorityExampleTests Tier2AuthorityTests Tier2GroupBTests Tier2GroupCNorthTests Tier2GroupCSouthTests; do
  echo "== $c"
  "$exe" -class "*.$c" -noLogo 2>&1 | tail -2
done
exit $status
