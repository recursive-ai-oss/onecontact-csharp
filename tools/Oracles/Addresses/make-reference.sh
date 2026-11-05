#!/usr/bin/env bash
# Regenerates the pinned reference outputs of the structural parity test: runs the reference implementation (built by
# build-oracle.sh) over the generated cases and writes, under testdata/addresses-structural/:
#
#   oracle-formats.tsv    the format table the reference reports, one region per line
#   cases.tsv.gz          the generated cases (see CaseGen)
#   oracle-results.tsv.gz the reference's answer to each case: problems and formatted lines
#   ORACLE                what produced them (jar hash, JDK, data hash)
#
# Run it on the machine that holds the oracle install, from the repository root, with the pinned data fetched
# (tools/fetch/libaddressinput.sh).
set -euo pipefail

root=$(cd "$(dirname "$0")/../../.." && pwd)
upstream=${ONECONTACT_UPSTREAM:-$root/.upstream}/libaddressinput
data=$upstream/testdata/countryinfo.txt
oracle=${ORACLE_HOME_DIR:-$HOME/onecontact/oracles/libaddressinput/81eb9628}
jdk=${JAVA_HOME:-$HOME/onecontact/oracles/jdk/current}
out=$root/testdata/addresses-structural
work=${TMPDIR:-/tmp}/addresses-structural-ref.$$
mkdir -p "$out" "$work"
trap 'rm -rf "$work"' EXIT

bash "$root/tools/Oracles/Addresses/run-oracle.sh" "$data" --format-map > "$out/oracle-formats.tsv"
dotnet run --project "$root/tools/Oracles/Addresses/CaseGen" -c Release -- "$data" "$out/oracle-formats.tsv" "$work/cases.tsv"
bash "$root/tools/Oracles/Addresses/run-oracle.sh" "$data" < "$work/cases.tsv" > "$work/results.tsv"
test "$(wc -l < "$work/cases.tsv")" = "$(wc -l < "$work/results.tsv")"
gzip -9nc "$work/cases.tsv" > "$out/cases.tsv.gz"
gzip -9nc "$work/results.tsv" > "$out/oracle-results.tsv.gz"
{
  echo "oracle: libaddressinput common module built from google/libaddressinput 81eb9628382b07d371d8ea0b11badf7de3857fd5"
  echo "common.jar sha256: $(cat "$oracle/common.jar.sha256")"
  echo "json jar: org.json:json:20240303 sha256 3cf6cd6892e32e2b4c1c39e0f52f5248a2f5b37646fdfbb79a66b46b618414ed"
  echo "java: $("$jdk/bin/java" -version 2>&1 | head -1)"
  echo "countryinfo.txt sha256: $(sha256sum "$data" | cut -d' ' -f1)"
  echo "cases: $(wc -l < "$work/cases.tsv")"
} > "$out/ORACLE"
echo "wrote $out"
