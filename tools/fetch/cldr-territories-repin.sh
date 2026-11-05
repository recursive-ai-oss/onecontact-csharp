#!/usr/bin/env bash
# Rewrites upstream/cldr-territories/PIN: the territory display names of every locale of the CLDR JSON release at a commit, plus the
# parent-locale table. Locales are listed through the hosting API's directory listing (names only); every file is downloaded by exact
# path and its SHA-256 recorded. Review the result like any new pin before committing it (tools/fetch/README.md).
#
#   usage: cldr-territories-repin.sh <commit>
set -euo pipefail
commit=${1:?commit}
here=$(cd "$(dirname "$0")" && pwd)
pin=$here/../../upstream/cldr-territories/PIN
work=$(mktemp -d "${TMPDIR:-.}/cldr-repin.XXXXXX")
trap 'rm -rf "$work"' EXIT
base=https://raw.githubusercontent.com/unicode-org/cldr-json/$commit/cldr-json
# The locale folders are found through the trees listing of the main folder (some locale folders hold no territory names).
main=$(curl --fail --silent --show-error --proto =https "https://api.github.com/repos/unicode-org/cldr-json/contents/cldr-json/cldr-localenames-full?ref=$commit" |
  python3 -c 'import json,sys; print([e["sha"] for e in json.load(sys.stdin) if e["name"]=="main"][0])')
curl --fail --silent --show-error --proto =https "https://api.github.com/repos/unicode-org/cldr-json/git/trees/$main?recursive=1" |
  python3 -c 'import json,sys; d=json.load(sys.stdin); assert not d.get("truncated"); [print("cldr-localenames-full/main/"+t["path"]) for t in d["tree"] if t["type"]=="blob" and t["path"].endswith("/territories.json")]' > "$work/paths"
echo cldr-core/supplemental/parentLocales.json >> "$work/paths"
LC_ALL=C sort -o "$work/paths" "$work/paths"
xargs -P 8 -I{} sh -c 'mkdir -p "$1/f/$(dirname "$2")" && curl --fail --silent --show-error --proto =https --output "$1/f/$2" "$3/$2"' _ "$work" {} "$base" < "$work/paths"
{
  echo "# Unicode CLDR (JSON distribution): territory display names of every locale and the parent-locale table, at a commit, by exact path."
  echo "source   cldr-territories"
  echo "licence  Unicode-3.0"
  echo "repo     https://github.com/unicode-org/cldr-json"
  echo "ref      $commit"
  echo "urlbase  $base/"
  while read -r p; do printf 'file     %s %s\n' "$p" "$(sha256sum "$work/f/$p" | cut -d' ' -f1)"; done < "$work/paths"
} > "$pin"
echo "$pin: $(grep -c '^file' "$pin") files"
