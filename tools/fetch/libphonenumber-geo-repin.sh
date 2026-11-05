#!/usr/bin/env bash
# Rewrites the pins of libphonenumber's geocoding, carrier and time-zone data for a new release: lists the data files of the three
# folders at a commit through the git trees listing of the hosting API (names only), downloads each file by exact path and records its
# SHA-256 in upstream/libphonenumber-{geocoding,carrier,timezones}/PIN.
#
#   usage: libphonenumber-geo-repin.sh <commit> <geocoding tree sha> <carrier tree sha> <timezones tree sha>
#
# The result must be read and reviewed like any new pin before it is committed (tools/fetch/README.md).
set -euo pipefail
commit=${1:?commit} gtree=${2:?geocoding tree} ctree=${3:?carrier tree} ttree=${4:?timezones tree}
here=$(cd "$(dirname "$0")" && pwd)
work=$(mktemp -d "${TMPDIR:-.}/geo-repin.XXXXXX")
trap 'rm -rf "$work"' EXIT
base=https://raw.githubusercontent.com/google/libphonenumber/$commit/resources

repin() { # folder tree description
  folder=$1 tree=$2 what=$3
  pin=$here/../../upstream/libphonenumber-$folder/PIN
  curl --fail --silent --show-error --proto =https "https://api.github.com/repos/google/libphonenumber/git/trees/$tree?recursive=1" |
    python3 -c 'import json,sys; d=json.load(sys.stdin); assert not d.get("truncated"); [print(t["path"]) for t in d["tree"] if t["type"]=="blob" and t["path"].endswith(".txt")]' |
    sed "s|^|$folder/|" | LC_ALL=C sort > "$work/$folder.paths"
  xargs -P 8 -I{} sh -c 'mkdir -p "$1/f/$(dirname "$2")" && curl --fail --silent --show-error --proto =https --output "$1/f/$2" "$3/$2"' _ "$work" {} "$base" < "$work/$folder.paths"
  {
    echo "# Google libphonenumber's $what files (not its code), at a commit, by exact path."
    echo "source   libphonenumber-$folder"
    echo "licence  Apache-2.0"
    echo "repo     https://github.com/google/libphonenumber"
    echo "ref      $commit"
    echo "urlbase  $base/"
    while read -r p; do printf 'file     %s %s\n' "$p" "$(sha256sum "$work/f/$p" | cut -d' ' -f1)"; done < "$work/$folder.paths"
  } > "$pin"
  echo "$pin: $(grep -c '^file' "$pin") files"
}

repin geocoding "$gtree" "geocoding (area description) data"
repin carrier "$ctree" "carrier name data"
repin timezones "$ttree" "time-zone map"
