#!/usr/bin/env python3
"""Fetches the three BAG extract members the address renderer reads (addresses, public spaces, places) from the PDOK extract zip.

The extract is 3.6 GB because it also carries buildings and dwellings; the members needed are about 390 MB. They are read out of the
zip over HTTP range requests (the zip's own central directory says where each member starts), written to the cache, and checked
against pins/bag.pin. A pin line is `file <local name> <size> <sha256> <zip url>#<member>`. RECORD=1 prints lines for unpinned files.
"""
import hashlib, io, os, sys, urllib.request, zipfile


class RangeFile(io.RawIOBase):
    def __init__(self, url):
        self.url, self.pos = url, 0
        self.size = int(urllib.request.urlopen(urllib.request.Request(url, method="HEAD")).headers["Content-Length"])

    def seekable(self): return True
    def readable(self): return True
    def tell(self): return self.pos

    def seek(self, offset, whence=0):
        self.pos = offset if whence == 0 else (self.pos + offset if whence == 1 else self.size + offset)
        return self.pos

    def read(self, n=-1):
        n = self.size - self.pos if n < 0 else min(n, self.size - self.pos)
        if n <= 0: return b""
        req = urllib.request.Request(self.url, headers={"Range": f"bytes={self.pos}-{self.pos + n - 1}"})
        data = urllib.request.urlopen(req).read()
        self.pos += len(data)
        return data

    def readinto(self, b):
        data = self.read(len(b)); b[:len(data)] = data; return len(data)


def main():
    pin = sys.argv[1]
    lines = [l.split() for l in open(pin) if l.strip()]
    name = next(l[1] for l in lines if l[0] == "register")
    dest = os.path.join(os.environ.get("ONECONTACT_DATA", os.path.expanduser("~/onecontact/data")), "train", name)
    os.makedirs(dest, exist_ok=True)
    zips = {}
    status = 0
    for kind, local, size, sha, url in (l for l in lines if l[0] == "file"):
        target = os.path.join(dest, local)
        if os.path.exists(target) and hashlib.sha256(open(target, "rb").read()).hexdigest() == sha:
            print("ok      ", local); continue
        outer, member = url.split("#")
        if outer not in zips:
            zips[outer] = zipfile.ZipFile(io.BufferedReader(RangeFile(outer), 1 << 20))
        h, n = hashlib.sha256(), 0
        with zips[outer].open(member) as src, open(target + ".part", "wb") as out:
            while chunk := src.read(1 << 20):
                out.write(chunk); h.update(chunk); n += len(chunk)
        if h.hexdigest() != sha:
            if os.environ.get("RECORD") == "1":
                os.replace(target + ".part", target); print("file", local, n, h.hexdigest(), url); continue
            os.remove(target + ".part"); print(f"MISMATCH {local}: got {h.hexdigest()} ({n} bytes), the pin says {sha}", file=sys.stderr); status = 2; continue
        os.replace(target + ".part", target); print("fetched ", local)
    sys.exit(status)


main()
