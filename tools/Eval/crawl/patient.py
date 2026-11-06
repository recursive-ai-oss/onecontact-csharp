#!/usr/bin/env python3
"""Runs one of the crawl scripts with a more patient `cc.get`: a 403 from the Common Crawl data server means "too many requests from this
address" and clears after a while, so it is retried with a long, growing pause instead of being taken for a missing file.

usage: patient.py <script.py> [args...]
"""
import os
import runpy
import sys
import time
import urllib.error
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cc  # noqa: E402

PAUSE = float(os.environ.get("CC_PAUSE", "1.0"))


def get(url, byte_range=None, tries=12, pause=PAUSE):
    delay = 5.0
    for attempt in range(tries):
        req = urllib.request.Request(url, headers={"User-Agent": cc.USER_AGENT})
        if byte_range:
            req.add_header("Range", f"bytes={byte_range[0]}-{byte_range[0] + byte_range[1] - 1}")
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                data = r.read()
            time.sleep(pause)
            return data
        except urllib.error.HTTPError as e:
            if e.code in (404, 416):
                return None
            time.sleep(delay)  # 403, 429, 5xx: wait and try again
            delay = min(delay * 2, 600)
        except Exception:
            time.sleep(delay)
            delay = min(delay * 2, 600)
    return None


cc.get = get
sys.argv = sys.argv[1:]
runpy.run_path(sys.argv[0], run_name="__main__")
