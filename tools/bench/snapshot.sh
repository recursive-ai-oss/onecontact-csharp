#!/usr/bin/env bash
# usage: snapshot.sh <name>  -- copies the built bench program aside so a later build does not disturb a run by later builds.
cd "$(dirname "$0")/../.."
. tools/bench/env.sh
rm -rf "${ONECONTACT_WORK:-$HOME/onecontact/work}/bench/snap/$1"
mkdir -p "${ONECONTACT_WORK:-$HOME/onecontact/work}/bench/snap"
cp -r "$ONECONTACT_ARTIFACTS/$(basename "$PWD")/bin/OneContact.Bench/release" "${ONECONTACT_WORK:-$HOME/onecontact/work}/bench/snap/$1"
