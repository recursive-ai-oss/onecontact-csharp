#!/usr/bin/env python3
"""Reads the Lithuanian address register's open-data geodatabase (Registru centras, via geoportal.lt) and writes plain text tables.

  convert-lt-gdb.py <RC_atviri_adresai.gdb> <out dir>

Needs the pyogrio package (a packaged GDAL reader; it is used only as a file-format converter, the geometry is not read).
Writes lt-addresses.tsv.gz (one row per address object, in the order of the geodatabase) and lt-flats.tsv.gz (one row per flat or
room of an address). Gzip timestamps are zero so the same input always gives the same bytes. Prints "derived <name> <size> <sha256>".
"""
import gzip
import hashlib
import os
import sys

import pyogrio.raw as raw

ADDRESS_COLUMNS = ["AOB_KODAS", "GAT_PAV", "NR", "KORPUSO_NR", "PASTO_KODAS", "SAV_PAV", "SEN_PAV", "GYV_PAV", "adresas"]
FLAT_COLUMNS = ["AOB_KODAS", "PATALPOS_NR"]


def text(v) -> str:
    if v is None:
        return ""
    s = str(v).strip()
    return "" if s.lower() == "nan" else s.replace("\t", " ").replace("\n", " ").replace("\r", " ")


def dump(path: str, layer: str, columns: list[str], out: str) -> None:
    meta, _, _, data = raw.read(path, layer=layer, read_geometry=False)
    index = {n: i for i, n in enumerate(meta["fields"])}
    n = len(data[0])
    with open(out, "wb") as f, gzip.GzipFile(filename="", mode="wb", fileobj=f, mtime=0, compresslevel=6) as gz:
        gz.write(("\t".join(columns) + "\n").encode("utf-8"))
        for k in range(n):
            gz.write(("\t".join(text(data[index[c]][k]) for c in columns) + "\n").encode("utf-8"))
    h = hashlib.sha256(open(out, "rb").read()).hexdigest()
    print(f"derived {os.path.basename(out)} {os.path.getsize(out)} {h} rows={n}")


def main() -> None:
    gdb, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    dump(gdb, "adresai", ADDRESS_COLUMNS, os.path.join(out, "lt-addresses.tsv.gz"))
    dump(gdb, "adr_pat", FLAT_COLUMNS, os.path.join(out, "lt-flats.tsv.gz"))


main()
