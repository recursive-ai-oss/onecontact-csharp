#!/usr/bin/env bash
# usage: table.sh <before tag> <after tag>; prints the before -> after table of the documentation (markdown): per country, with a hint and without one,
# every-field %, field %, refusals and wrong-country count.
d=${ONECONTACT_TIER2_OUT:-$HOME/onecontact/u38}
rows() { # tag -> "cc hint every fields refused wrong"
  awk '/hint=/ { cc=$1; h=($2=="hint=no") ? "no" : "yes";
    for (i=1;i<=NF;i++) { if ($i ~ /^every-field=/) { ep=$(i+1); gsub(/[()%]/,"",ep) }
      if ($i ~ /^fields=/) { fp=$(i+1); gsub(/[()%]/,"",fp) }
      if ($i ~ /^refused=/) { split($i,b,"="); r=b[2] }
      if ($i ~ /^wrong-country=/) { split($i,c,"="); w=c[2] } }
    print cc, h, ep, fp, r, w }' "$d/$1/summary.txt" | sort -u
}
echo "| Country | Hint | Every field % | Field % | Refused | Wrong country |"
echo "|---|---|---|---|---|---|"
join -j1 <(rows "$1" | awk '{print $1"-"$2, $3, $4, $5, $6}') <(rows "$2" | awk '{print $1"-"$2, $3, $4, $5, $6}') |
awk '{ split($1, k, "-"); printf "| %s | %s | %s -> %s | %s -> %s | %d -> %d | %d -> %d |\n", k[1], k[2], $2, $6, $3, $7, $4, $8, $5, $9 }' | sort -t'|' -k2,2 -k3,3r
