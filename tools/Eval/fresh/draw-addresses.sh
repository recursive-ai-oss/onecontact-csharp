#!/usr/bin/env bash
# Draws the candidate pages and address drafts of fresh address evaluation sets, leaving out every host already used by any evaluation set,
# and writes the rules' hinted pre-label next to each draft.
#
# usage: draw-addresses.sh <set root> <rules dll> <seed> <CC>=<blocks> [<CC>=<blocks> ...]
#   <set root> is a private directory (pages and drafts hold personal data). Countries ES CZ SK HR IM are drawn with the Tier 2 south selector
#   (one folder per country); DE BE CH with the Tier 2 selector into <set root>/t2 (pages carry their country).
#   Environment: PRIVATE_ROOT (default $ONECONTACT_EVAL, then ~/onecontact/eval/private) is scanned for existing page stores;
#   EXTRA_EXCLUDE names a file of further hosts to skip, one per line (for example the hosts of a training crawl);
#   CRAWL (default CC-MAIN-2026-43); FETCH_THREADS (default 2, the polite limit); ADDRESSES (drafts per country, default 150).
# Re-running a country resumes its page fetch (fetch-pages.py skips stored pages). Drafts are written to <set root>/drafts.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
crawl=${CRAWL:-CC-MAIN-2026-43}
root=${1:?set root}; dll=${2:?rules dll}; seed=${3:?seed}; shift 3
private=${PRIVATE_ROOT:-${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}}
threads=${FETCH_THREADS:-2}
na=${ADDRESSES:-150}
mkdir -p "$root/drafts"
cd "$root"
if [ ! -f exclude-hosts.txt ]; then
  python3 "$here/exclude.py" build "$private" exclude
  mv exclude-hosts.txt exclude-hosts.base.txt
  cat exclude-hosts.base.txt ${EXTRA_EXCLUDE:+"$EXTRA_EXCLUDE"} | sort -u > exclude-hosts.txt
  rm -f exclude-hosts.base.txt
fi
for spec in "$@"; do
  c=${spec%%=*}; n=${spec#*=}
  case $c in
    ES|CZ|SK|HR|IM)
      mkdir -p "$c"
      [ -s "$c/candidates.jsonl" ] || python3 "$here/../tier2-south/select-pages.py" "$crawl" "$c" "$seed" "$c" "$n" "$root/exclude-hosts.txt"
      python3 "$here/../crawl/fetch-pages.py" "$c" 1000000 "$threads"
      python3 "$here/../tier2-south/extract-candidates.py" "$c" "$c/pages.jsonl" drafts "$seed" "$na" 0 0
      ;;
    DE|BE|CH)
      mkdir -p t2
      lc=$(echo "$c" | tr 'A-Z' 'a-z')
      [ -f "t2/.selected-$c" ] || { EXCLUDE_HOSTS="$root/exclude-hosts.txt" python3 "$here/../crawl/select-pages-t2.py" "$crawl" t2 "$seed" "$lc=$n"; touch "t2/.selected-$c"; }
      python3 "$here/../crawl/fetch-pages.py" t2 1000000 "$threads"
      python3 "$here/../crawl/extract-candidates-t2.py" t2 "$seed" "$c" "$na" 0 0
      mv "t2/draft-address-$c.jsonl" drafts/
      rm -f "t2/draft-name-$c.jsonl"
      ;;
    *) echo "unknown country $c" >&2; exit 2 ;;
  esac
  dotnet "$dll" address "drafts/draft-address-$c.jsonl" "drafts/pred-address-$c.jsonl" hint
done
