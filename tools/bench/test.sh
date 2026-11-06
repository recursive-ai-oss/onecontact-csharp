#!/usr/bin/env bash
# usage: tools/bench/test.sh [xunit filter args...]   -- builds the test project (Release) on a build machine and runs it; prints errors and failures only.
cd "$(dirname "$0")/../.."
. tools/bench/env.sh
export DOTNET_PROCESSOR_COUNT=4 MSBUILDDISABLENODEREUSE=1 ONECONTACT_UPSTREAM="$PWD/.upstream"
nice dotnet build -m:4 -v quiet -clp:NoSummary tests/OneContact.Tests -c Release 2>&1 | grep -E 'error|warning' | sort -u | cut -c1-300 | head -20
# The tests locate the repository root from their binaries, which are built outside it: link the root's contents next to them.
A="$ONECONTACT_ARTIFACTS/$(basename "$PWD")"
for f in OneContact.slnx testdata docs tools upstream samples src tests; do ln -sfn "$PWD/$f" "$A/$f"; done
E="$A/bin/OneContact.Tests/release/RecursiveAI.OneContact.Tests"
cd "$(dirname "$E")" && nice "$E" "$@" 2>&1 | grep -v '^ *at ' | cut -c1-600 | head -${TEST_LINES:-60}
