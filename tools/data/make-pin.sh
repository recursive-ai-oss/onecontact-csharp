#!/usr/bin/env bash
# Writes a pin file (same format as the other tools/data/pins/*.pin) for files already downloaded into the data cache:
#   make-pin.sh <register-name> <urls.txt> > pins/<name>.pin
# urls.txt has one "<file name> <url>" per line; each file is looked up in <data root>/train/<register-name>/.
# Pinning the first download makes every later fetch verify the same bytes (the registers are published at "latest" URLs).
set -euo pipefail
register=$1
urls=$2
dir=${ONECONTACT_DATA:-$HOME/onecontact/data}/train/$register
echo "register $register"
while read -r file url; do
  [ -n "$file" ] || continue
  printf 'file %s %s %s %s\n' "$file" "$(stat -c %s "$dir/$file")" "$(sha256sum "$dir/$file" | cut -d' ' -f1)" "$url"
done < "$urls"
