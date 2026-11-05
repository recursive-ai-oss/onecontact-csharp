#!/usr/bin/env bash
# Prints the blocks of a reviewer view (the output of `eval-extract review`) whose item ids are not yet in a list of reviewed ids.
#   usage: unreviewed.sh <review.txt> <reviewed-ids.txt>
awk -v idsf="$2" 'BEGIN { while ((getline l < idsf) > 0) seen[l] = 1 } /^## / { show = !($2 in seen) } show' "$1"
