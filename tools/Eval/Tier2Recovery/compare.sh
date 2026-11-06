#!/usr/bin/env bash
# usage: compare.sh <before tag> <after tag>; prints, per country, hint and no hint: every-field %, field %, refusals, wrong-country count, before -> after.
d=${ONECONTACT_TIER2_OUT:-$HOME/onecontact/tier2-recovery}
parse() { # file -> cc hint every fields refused wrong
  awk '/hint=/ { cc=$1; h=$2; if (h=="hint=no") h="no"; else h="yes";
    for (i=1;i<=NF;i++) { if ($i ~ /^every-field=/) { split($i,a,"="); e=a[2]; getline_pct=$(i+1); gsub(/[()%]/,"",getline_pct); ep=getline_pct }
      if ($i ~ /^fields=/) { fp=$(i+1); gsub(/[()%]/,"",fp) }
      if ($i ~ /^refused=/) { split($i,b,"="); r=b[2] }
      if ($i ~ /^wrong-country=/) { split($i,c,"="); w=c[2] } }
    print cc, h, ep, fp, r, w }' "$1"
}
join -j1 <(parse "$d/$1/summary.txt" | awk '{print $1"-"$2, $3, $4, $5, $6}' | sort -u) <(parse "$d/$2/summary.txt" | awk '{print $1"-"$2, $3, $4, $5, $6}' | sort -u) |
awk 'BEGIN { print "country-hint  every-field% (before -> after)  field%  refused  wrong-country" }
  { de=$6-$2; df=$7-$3; flag=""; if (de < -0.5 || df < -0.5 || $9 > $5) flag="  <-- CHECK";
    printf "%-8s %6.1f -> %6.1f (%+5.1f)   %6.1f -> %6.1f   %4d -> %4d   %3d -> %3d%s\n", $1, $2, $6, de, $3, $7, $4, $8, $5, $9, flag }'
