#!/usr/bin/env python3
"""Downloads the Slovenian address register (Register prostorskih enot, GURS) from its public INSPIRE addresses WFS into the data cache.

  fetch-si-rpe.py <out dir>

The service answers GetFeature in pages; this keeps every page as the server sent it (without the XML declaration, so the pages
form one XML fragment) in a gzip file with a zero timestamp, so the same download always gives the same bytes. Two files are
written: ad-address.xml.gz (the ad:Address features, with their component names) and ad-postal.xml.gz (the ad:PostalDescriptor
features, which carry the post codes). Prints "file <name> <size> <sha256>" lines for the pin.

The server sometimes cuts a page short while still announcing a full one, so every page is checked (it must end with the closing
tag and hold as many members as it announces) and fetched again when it is not. Run with an existing output file, the script only
repairs the pages that fail the check.
"""
import gzip
import hashlib
import os
import re
import sys
import time
import urllib.request

BASE = "https://ipi.eprostor.gov.si/wfs-si-gurs-ins/ad/wfs"
UA = "Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0"
PAGE = 10000


def complete(page: bytes) -> bool:
    m = re.search(rb'numberReturned="(\d+)"', page)
    return m is not None and page.rstrip().endswith(b"</wfs:FeatureCollection>") and page.count(b"<wfs:member>") == int(m.group(1))


def get(type_name: str, start: int, count: int) -> bytes:
    url = (f"{BASE}?service=WFS&version=2.0.0&request=GetFeature&typeNames={type_name}"
           f"&count={count}&startIndex={start}&sortBy=gml:id")
    for attempt in range(6):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=300) as r:
                page = r.read()
            if not complete(page):
                raise ValueError(f"incomplete page at {start}")
            return page
        except Exception as e:  # network errors are retried; the last one is raised
            if attempt == 5:
                raise
            print(f"retry {attempt + 1}: {e}", file=sys.stderr)
            time.sleep(5 * (attempt + 1))
    raise AssertionError


def repair(type_name: str, path: str) -> None:
    with gzip.open(path, "rb") as f:
        pages = [p for p in f.read().split(b"\n") if p]
    fixed = 0
    for i, page in enumerate(pages):
        if not complete(page):
            pages[i] = re.sub(rb"^<\?xml[^>]*\?>", b"", get(type_name, i * PAGE, PAGE), count=1)
            fixed += 1
            print(f"{type_name}: page {i} fetched again", file=sys.stderr)
    with open(path, "wb") as raw, gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=6) as gz:
        for page in pages:
            gz.write(page)
            gz.write(b"\n")
    print(f"{type_name}: {len(pages)} pages, {fixed} repaired", file=sys.stderr)


def fetch(type_name: str, path: str) -> None:
    start = 0
    total = 0
    with open(path, "wb") as raw, gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=6) as gz:
        while True:
            page = get(type_name, start, PAGE)
            m = re.search(rb'numberReturned="(\d+)"', page)
            n = int(m.group(1)) if m else 0
            if n == 0:
                break
            gz.write(re.sub(rb"^<\?xml[^>]*\?>", b"", page, count=1))
            gz.write(b"\n")
            start += n
            total += n
            print(f"{type_name}: {total}", file=sys.stderr)
    print(f"{type_name}: {total} features", file=sys.stderr)


def main() -> None:
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for type_name, name in (("ad:PostalDescriptor", "ad-postal.xml.gz"), ("ad:Address", "ad-address.xml.gz")):
        path = os.path.join(out, name)
        if os.path.exists(path):
            repair(type_name, path)
        else:
            fetch(type_name, path)
        h = hashlib.sha256(open(path, "rb").read()).hexdigest()
        print(f"file {name} {os.path.getsize(path)} {h} {BASE}?service=WFS&version=2.0.0&request=GetFeature&typeNames={type_name}")


main()
