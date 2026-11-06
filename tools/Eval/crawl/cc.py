"""Small shared helpers for the evaluation-set selection scripts: polite HTTP with back-off, WARC record reading, HTML to lines.

Only public Common Crawl endpoints are used (the index files and WARC byte ranges); pages are read, never altered.
Everything these scripts write goes to the private evaluation directory given on the command line.
"""
import gzip
import html.parser
import io
import re
import time
import urllib.request
import urllib.error

DATA = "https://data.commoncrawl.org/"
USER_AGENT = "onecontact-eval-selection/1.0 (research; evaluation sets only)"


_PROBE = DATA + "cc-index/collections/CC-MAIN-2026-43/indexes/cluster.idx"


def throttled():
    """True when a tiny known-good range request is also refused, i.e. the 403 means rate limiting and not a missing object."""
    req = urllib.request.Request(_PROBE, headers={"User-Agent": USER_AGENT, "Range": "bytes=0-63"})
    try:
        with urllib.request.urlopen(req, timeout=60):
            return False
    except urllib.error.HTTPError as e:
        return e.code == 403
    except Exception:
        return False


def get(url, byte_range=None, tries=8, pause=0.35):
    """GET with exponential back-off on 5xx/SlowDown; returns bytes or None for a permanent miss."""
    delay = 2.0
    for attempt in range(tries):
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        if byte_range:
            req.add_header("Range", f"bytes={byte_range[0]}-{byte_range[0] + byte_range[1] - 1}")
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                data = r.read()
            time.sleep(pause)
            return data
        except urllib.error.HTTPError as e:
            if e.code == 403 and throttled():
                # CloudFront answers 403 to a client that asks too often; wait and ask again rather than count it as a missing object.
                time.sleep(120)
                continue
            if e.code in (404, 403, 416):
                return None
            time.sleep(delay)
            delay = min(delay * 2, 60)
        except Exception:
            time.sleep(delay)
            delay = min(delay * 2, 60)
    return None


def warc_body(raw):
    """Takes one gzip member holding a WARC response record; returns (content-type header, body bytes) or None."""
    try:
        data = gzip.decompress(raw)
    except Exception:
        return None
    # WARC headers, blank line, HTTP headers, blank line, body.
    i = data.find(b"\r\n\r\n")
    if i < 0:
        return None
    rest = data[i + 4:]
    j = rest.find(b"\r\n\r\n")
    if j < 0:
        return None
    head = rest[:j].decode("latin-1", "replace")
    body = rest[j + 4:]
    m = re.search(r"(?im)^content-type:\s*([^\r\n]+)", head)
    ctype = m.group(1) if m else ""
    if re.search(r"(?im)^transfer-encoding:\s*chunked", head):
        body = unchunk(body)
    return ctype, body


def unchunk(b):
    out = io.BytesIO()
    pos = 0
    while True:
        k = b.find(b"\r\n", pos)
        if k < 0:
            break
        try:
            size = int(b[pos:k].split(b";")[0], 16)
        except ValueError:
            return b
        if size == 0:
            break
        out.write(b[k + 2:k + 2 + size])
        pos = k + 2 + size + 2
    return out.getvalue()


def decode(ctype, body):
    enc = None
    m = re.search(r"charset=([\w-]+)", ctype or "", re.I)
    if m:
        enc = m.group(1)
    else:
        m = re.search(rb"<meta[^>]+charset=[\"']?([\w-]+)", body[:4096], re.I)
        if m:
            enc = m.group(1).decode("ascii", "ignore")
    for e in (enc, "utf-8", "cp1252"):
        if not e:
            continue
        try:
            return body.decode(e)
        except Exception:
            continue
    return body.decode("utf-8", "replace")


BLOCK = {"p", "div", "li", "ul", "ol", "br", "tr", "td", "th", "table", "section", "article", "header", "footer", "nav", "address", "h1", "h2", "h3", "h4",
         "h5", "h6", "dt", "dd", "dl", "blockquote", "form", "main", "aside", "figure", "figcaption", "hr", "option", "label", "span.break"}
VOID = {"input", "img", "br", "hr", "meta", "link", "area", "base", "col", "embed", "source", "track", "wbr"}
SKIP = {"script", "style", "noscript", "template", "svg", "head", "title", "iframe", "select"}


class Lines(html.parser.HTMLParser):
    """Visible text as lines: block elements and <br> start a new line; whitespace is collapsed inside a line."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.lines = []
        self.cur = []
        self.skip = 0
        self.hidden = []
        self.meta = {}
        self.ldjson = []
        self._in_ld = False

    def flush(self):
        t = re.sub(r"[ \t\r\f\v\u00a0\u2009\u200b]+", " ", "".join(self.cur)).strip()
        if t:
            self.lines.append(t)
        self.cur = []

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag == "meta":
            k = (a.get("property") or a.get("name") or "").lower()
            if k in ("og:site_name", "og:title", "og:street-address", "og:locality", "og:region", "og:postal-code"):
                self.meta[k] = a.get("content", "")
        if tag == "script" and (a.get("type") or "").lower() == "application/ld+json":
            self._in_ld = True
            self.ldjson.append("")
        if tag in SKIP:
            self.skip += 1
        elif tag in BLOCK:
            self.flush()
        st = (a.get("style") or "").replace(" ", "").lower()
        if tag not in VOID and ("display:none" in st or "visibility:hidden" in st or "hidden" in a):
            self.hidden.append(tag)
            self.skip += 1

    def handle_endtag(self, tag):
        if tag == "script":
            self._in_ld = False
        if tag in SKIP:
            self.skip = max(0, self.skip - 1)
        elif tag in BLOCK:
            self.flush()
        if self.hidden and self.hidden[-1] == tag:
            self.hidden.pop()
            self.skip = max(0, self.skip - 1)

    def handle_data(self, data):
        if self._in_ld:
            self.ldjson[-1] += data
        elif not self.skip:
            self.cur.append(data)

    def close(self):
        super().close()
        self.flush()


def page_lines(text):
    p = Lines()
    try:
        p.feed(text)
        p.close()
    except Exception:
        pass
    return p
