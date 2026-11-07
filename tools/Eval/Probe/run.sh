#!/usr/bin/env bash
# usage: run.sh <cases file>; builds the probe and prints how the rules read each case. Output also goes to $HOME/onecontact/probe/<name>.out
export DOTNET_PROCESSOR_COUNT=4 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
nice dotnet build tools/Eval/Probe -c Release -m:4 -v quiet -clp:NoSummary 2>&1 | grep -E 'error|warn' | sort -u | head
mkdir -p ~/onecontact/probe
exe=artifacts/bin/OneContact.Eval.Probe/release/RecursiveAI.OneContact.Eval.Probe
for f in "$@"; do nice $exe < "$f" | tee ~/onecontact/probe/$(basename "$f" .txt).out; done
