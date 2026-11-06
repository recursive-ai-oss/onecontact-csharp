#!/usr/bin/env bash
# usage: view.sh <set root> address|name <CC> <start> <count>   (re-drafts the second pre-label for addresses, then prints a batch)
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
cd "$1/drafts"
if [ "$2" = address ]; then
  python3 "$here/prelabel.py" draft-address-$3.jsonl pre2-address-$3.jsonl
  python3 "$here/../labeling.py" view address draft-address-$3.jsonl pre2-address-$3.jsonl $4 $5 | cut -c1-${WIDTH:-250}
else
  python3 "$here/../labeling.py" view name draft-name-$3.jsonl pred-name-$3.jsonl $4 $5 | cut -c1-${WIDTH:-250}
fi
