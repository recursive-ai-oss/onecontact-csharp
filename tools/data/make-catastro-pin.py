#!/usr/bin/env python3
"""Lists the Catastro INSPIRE address downloads (one zip per municipality) and writes a pin file for a fixed share of them.

The Catastro publishes an Atom feed per territorial office, and in it one zip of addresses for every municipality. A municipality
is taken when the SHA-256 of its five-digit code, read as a number, is below RATE per mille of the range, so the choice is the
same on every run and spreads over all provinces the feed serves. Hashes in the pin are left at zero; fetch-register.sh with RECORD=1
fills them in (fetch-tier2b.sh --record does both).

usage: make-catastro-pin.py <pin file to write> [rate per mille, default 120]
"""
import hashlib, re, sys, urllib.parse, urllib.request

MASTER = "https://www.catastro.hacienda.gob.es/INSPIRE/Addresses/ES.SDGC.AD.atom.xml"
ZERO = "0" * 64


def get(url):
    with urllib.request.urlopen(url, timeout=120) as r:
        return r.read().decode("latin-1")


def main():
    out = sys.argv[1]
    rate = int(sys.argv[2]) if len(sys.argv) > 2 else 120
    master = get(MASTER)
    offices = sorted(set(re.findall(r'href="(https?://[^"]*ES\.SDGC\.[Aa][Dd]\.atom_(\d\d)\.xml)"', master)), key=lambda t: t[1])
    lines = ["register catastro-2026-08-21"]
    for url, office in offices:
        url = url.replace("http://", "https://")
        feed = get(url)
        zips = sorted(set(re.findall(r'href="(https?://[^"]*/A\.ES\.SDGC\.AD\.(\d{5})\.zip)"', feed)), key=lambda t: t[1])
        for zurl, code in zips:
            h = int.from_bytes(hashlib.sha256(code.encode()).digest()[:8], "big") % 1000
            if h < rate:
                lines.append(f"file A.ES.SDGC.AD.{code}.zip 0 {ZERO} {urllib.parse.quote(zurl.replace('http://', 'https://'), safe=':/%')}")
    open(out, "w").write("\n".join(lines) + "\n")
    print(f"{len(lines) - 1} municipalities selected from {len(offices)} offices")


main()
