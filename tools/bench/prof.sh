#!/usr/bin/env bash
# usage: prof.sh <mode>...  -- builds the bench program, then runs the named profile modes (profile-phone, profile-find, profile-extract, accuracy).
# Meant to run inside a queued job: timings taken any other way are not trusted.
cd "$(dirname "$0")/../.."
bash tools/bench/build.sh
. tools/bench/env.sh
export DOTNET_PROCESSOR_COUNT=4
snap=$ONECONTACT_ARTIFACTS/$(basename "$PWD")/bin/OneContact.Bench/release
{ echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2); cores: $(nproc); load before: $(cat /proc/loadavg)"; }
for mode in "$@"; do
  echo "== $mode"
  nice -n 5 dotnet "$snap/RecursiveAI.OneContact.Bench.dll" $mode 2>&1
  echo "load after $mode: $(cat /proc/loadavg)"
done
