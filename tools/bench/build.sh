#!/usr/bin/env bash
# usage: tools/bench/build.sh   -- builds the bench project (Release) on a build machine; prints errors only.
cd "$(dirname "$0")/../.."
. tools/bench/env.sh
export DOTNET_PROCESSOR_COUNT=4 MSBUILDDISABLENODEREUSE=1
nice dotnet build -m:4 -v quiet -clp:NoSummary bench/OneContact.Bench -c Release 2>&1 | grep -E 'error|warning' | sort -u | cut -c1-260 | head -30
