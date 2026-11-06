#!/usr/bin/env bash
# usage: snapshot.sh <name>  -- copies the built bench program aside so a queued run is not disturbed by later builds.
cd "$(dirname "$0")/../.."
. tools/bench/env.sh
rm -rf "$HOME/onecontact/u37/snap/$1"
mkdir -p "$HOME/onecontact/u37/snap"
cp -r "$ONECONTACT_ARTIFACTS/$(basename "$PWD")/bin/OneContact.Bench/release" "$HOME/onecontact/u37/snap/$1"
