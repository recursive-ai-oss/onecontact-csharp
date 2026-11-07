#!/usr/bin/env bash
# Environment for building and running the speed rows on a build machine. Source it from the repository root.
export PATH=~/dotnet10:$PATH DOTNET_ROOT=~/dotnet10 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export ONECONTACT_LOCAL_FEEDS="$PWD/.feeds"
export ONECONTACT_ARTIFACTS="${ONECONTACT_ARTIFACTS:-$HOME/onecontact/u37/artifacts}"
export ONECONTACT_PSL="$(ls "$PWD"/.upstream/psl/*/public_suffix_list.dat "$PWD"/.upstream/psl/public_suffix_list.dat 2>/dev/null | head -1)"
export ONECONTACT_TIER1_MODEL="${ONECONTACT_TIER1_MODEL:-$HOME/onecontact/u23/runs/b-s1/tier1.onenlp}"
