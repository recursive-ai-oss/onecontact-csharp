#!/usr/bin/env bash
export PATH=~/dotnet10:$PATH
# usage: run.sh <cases file>; builds the probe and prints how the rules read each case. Output also goes to $HOME/onecontact/probe/<name>.out
export PATH=~/dotnet10:$PATH DOTNET_ROOT=~/dotnet10 DOTNET_PROCESSOR_COUNT=4 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
test -e ~/WORKER-BENCH.lock && { echo bench lock; exit 9; }
nice dotnet build tools/Eval/Probe -c Release -m:4 -v quiet -clp:NoSummary 2>&1 | grep -E 'error|warn' | sort -u | head
mkdir -p ~/onecontact/probe
exe=artifacts/bin/OneContact.Eval.Probe/release/RecursiveAI.OneContact.Eval.Probe
for f in "$@"; do nice $exe < "$f" | tee ~/onecontact/probe/$(basename "$f" .txt).out; done
