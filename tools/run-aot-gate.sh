#!/usr/bin/env bash
# Publishes the five NativeAOT smoke apps with warnings as errors, counts trim and AOT warnings, and runs each app:
#   tools/run-aot-gate.sh <work dir>
# The model apps need ONECONTACT_TIER1_WEIGHTS / ONECONTACT_MODELS_WEIGHTS / ONECONTACT_ACCURATE_WEIGHTS (folders holding the weights the package manifests list).
# CPP_LINKER_ARGS may carry extra publish properties for toolchains without clang (for example
# "-p:CppCompilerAndLinker=gcc -p:SysRoot=/").
set -uo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
work=${1:?usage: run-aot-gate.sh <work dir>}
mkdir -p "$work"
status=0
for app in OneContact.AotSmoke OneContact.AotSmoke.Models OneContact.AotSmoke.ModelsSuperset OneContact.AotSmoke.ModelsAccurate OneContact.AotSmoke.ModelsMixed; do
  out=$work/$app
  # shellcheck disable=SC2086
  dotnet publish "$root/samples/$app" -c Release -r linux-x64 -o "$out" -warnaserror ${CPP_LINKER_ARGS:-} > "$work/$app.publish.log" 2>&1
  code=$?
  warnings=$(grep -ciE "warning (IL|CS|NU|NETSDK)[0-9]+" "$work/$app.publish.log")
  echo "== $app: publish exit $code, warning lines $warnings, binary $(du -b "$out/RecursiveAI.$app" 2>/dev/null | cut -f1) bytes"
  [ $code -eq 0 ] || { tail -15 "$work/$app.publish.log"; status=1; continue; }
  "$out/RecursiveAI.$app" > "$work/$app.run.log" 2>&1
  code=$?
  echo "   run exit $code; last line: $(tail -1 "$work/$app.run.log")"
  [ $code -eq 0 ] || status=1
done
exit $status
