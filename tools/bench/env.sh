#!/usr/bin/env bash
# Environment for building and running the speed rows. Source it from the repository root.
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export ONECONTACT_LOCAL_FEEDS="$PWD/.feeds"
export ONECONTACT_ARTIFACTS="${ONECONTACT_ARTIFACTS:-${ONECONTACT_WORK:-$HOME/onecontact/work}/bench/artifacts}"
export ONECONTACT_PSL="$(ls "$PWD"/.upstream/psl/*/public_suffix_list.dat "$PWD"/.upstream/psl/public_suffix_list.dat 2>/dev/null | head -1)"
export ONECONTACT_TIER1_MODEL="${ONECONTACT_TIER1_MODEL:-${ONECONTACT_WORK:-$HOME/onecontact/work}/models/tier1.onenlp}"
