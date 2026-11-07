#!/usr/bin/env python3
"""Step 1 for the Tier 2 countries: picks candidate contact/about/legal-notice pages of one country's TLD from a Common Crawl index.

usage: select-pages.py <crawl-id> <out-dir> <seed> <country> <blocks> [<exclude-hosts-file> [<max candidates>]]

Like select-pages.py (random blocks of the sorted URL index, HTML captures with status 200, at most two pages per host per block), but
for one country at a time, with the local-language path words of contact, about-us, team, location and legal-notice pages
(Impressum, aviso de privacidad, and the like). Hosts listed in the exclude file (one per line) are skipped, so a host used by another
evaluation set never appears here. The index file is read from <out-dir>/cluster.idx, or downloaded there when absent.
Writes <out-dir>/candidates.jsonl. Nothing about people is read here.
"""
import gzip
import json
import os
import random
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "crawl"))
import cc  # noqa: E402

crawl, out, seed, country, n_blocks = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4], int(sys.argv[5])
exclude = set()
if len(sys.argv) > 6 and os.path.exists(sys.argv[6]):
    exclude = {l.strip().lower() for l in open(sys.argv[6], encoding="utf-8") if l.strip()}
os.makedirs(out, exist_ok=True)

TLD = {"PL": "pl", "CZ": "cz", "SK": "sk", "SI": "si", "HR": "hr", "HU": "hu", "RO": "ro", "MX": "mx", "AU": "au", "GB": "uk", "JE": "je", "GG": "gg", "IM": "im", "IE": "ie", "ES": "es", "PT": "pt", "IT": "it", "MT": "mt", "SM": "sm", "VA": "va", "AD": "ad"}
WORDS = {
    "GB": "contact|contact-us|contactus|about|about-us|aboutus|team|our-team|staff|people|locations?|find-us|visit|directions|legal|legal-notice|privacy|privacy-policy|terms|get-in-touch|offices|branches|solicitors|clinics?", "JE": "contact|contact-us|contactus|about|about-us|aboutus|team|our-team|staff|people|locations?|find-us|visit|directions|legal|legal-notice|privacy|privacy-policy|terms|get-in-touch|offices|branches|solicitors|clinics?", "GG": "contact|contact-us|contactus|about|about-us|aboutus|team|our-team|staff|people|locations?|find-us|visit|directions|legal|legal-notice|privacy|privacy-policy|terms|get-in-touch|offices|branches|solicitors|clinics?", "IM": "contact|contact-us|contactus|about|about-us|aboutus|team|our-team|staff|people|locations?|find-us|visit|directions|legal|legal-notice|privacy|privacy-policy|terms|get-in-touch|offices|branches|solicitors|clinics?", "IE": "contact|contact-us|contactus|about|about-us|aboutus|team|our-team|staff|people|locations?|find-us|visit|directions|legal|legal-notice|privacy|privacy-policy|terms|get-in-touch|offices|branches|solicitors|clinics?",
    "ES": "contacto|contactenos|contactanos|contactar|aviso-legal|quienes-somos|quien-somos|sobre-nosotros|nosotros|equipo|donde-estamos|ubicacion|politica-de-privacidad|empresa|oficinas",
    "PT": "contactos|contacto|contacte|fale-conosco|sobre-nos|quem-somos|empresa|aviso-legal|informacoes-legais|onde-estamos|equipa|politica-de-privacidade|localizacao",
    "IT": "contatti|contattaci|contatto|chi-siamo|note-legali|privacy|dove-siamo|sedi|sede|azienda|il-nostro-team|informativa-privacy|cookie-policy|team",
    "MT": "contact|contact-us|contactus|about|about-us|aboutus|team|our-team|staff|people|locations?|find-us|visit|directions|legal|legal-notice|privacy|privacy-policy|terms|get-in-touch|offices|branches|solicitors|clinics?", "SM": "contatti|contattaci|chi-siamo|note-legali|privacy|dove-siamo|sedi|contact|about|azienda", "VA": "contatti|contact|contacts|about|chi-siamo|note-legali|privacy|dove-siamo|sedi|uffici",
    "AD": "contacte|contacto|contactenos|contact|contactar|qui-som|quienes-somos|about|aviso-legal|avis-legal|nosaltres|on-som|ubicacion|politica-de-privacitat|politica-de-privacidad",
    "PL": "kontakt|kontakty|dane-kontaktowe|o-nas|o-firmie|firma|zespol|nasz-zespol|lokalizacja|lokalizacje|dojazd|gdzie-jestesmy|stopka|polityka-prywatnosci|regulamin|oddzialy|placowki",
    "CZ": "kontakt|kontakty|o-nas|o-firme|tym|nas-tym|kde-nas-najdete|pobocky|provozovny|impressum|obchodni-podminky|ochrana-osobnich-udaju",
    "SK": "kontakt|kontakty|o-nas|o-firme|tim|nas-tim|pobocky|prevadzky|impressum|obchodne-podmienky|ochrana-osobnych-udajov",
    "SI": "kontakt|kontakti|o-nas|o-podjetju|podjetje|ekipa|nasa-ekipa|lokacije|poslovalnice|impresum|splosni-pogoji|politika-zasebnosti",
    "HR": "kontakt|kontakti|o-nama|tvrtka|o-tvrtki|tim|nas-tim|lokacije|poslovnice|impressum|uvjeti-koristenja|politika-privatnosti",
    "HU": "kapcsolat|kapcsolatok|elerhetosegek|rolunk|cegunkrol|csapat|munkatarsak|impresszum|aszf|adatvedelem|uzleteink|elerhetoseg",
    "RO": "contact|contacte|despre-noi|despre|echipa|echipa-noastra|locatii|sedii|termeni-si-conditii|politica-de-confidentialitate|date-de-contact",
    "MX": "contacto|contactanos|contactenos|nosotros|quienes-somos|acerca-de|equipo|nuestro-equipo|ubicacion|sucursales|aviso-de-privacidad|aviso-legal|donde-estamos|directorio",
    "AU": "contact|contact-us|contactus|about|about-us|aboutus|team|our-team|staff|people|locations?|find-us|visit|privacy|privacy-policy|terms|stores|branches|clinics?",
}
PATH = re.compile(r"/(" + WORDS[country] + r")(/|\.|$|-)", re.I)

base = f"{cc.DATA}cc-index/collections/{crawl}/indexes/"
idx_path = os.path.join(out, "cluster.idx")
if not os.path.exists(idx_path):
    data = cc.get(base + "cluster.idx")
    assert data, "cluster.idx unavailable"
    open(idx_path, "wb").write(data)

blocks = []
for line in open(idx_path, encoding="utf-8"):
    key, rest = line.split("\t", 1)
    fn, off, ln, _ = rest.split("\t")
    if key.split(",")[0] == TLD[country]:
        blocks.append((key, fn, int(off), int(ln)))

rng = random.Random(seed)
picked = rng.sample(blocks, min(n_blocks, len(blocks)))
CAP = int(sys.argv[7]) if len(sys.argv) > 7 else 10**9  # stop after this many candidates (small states)
n_kept = 0
with open(os.path.join(out, "candidates.jsonl"), "w", encoding="utf-8") as fo:
    for key, fn, off, ln in picked:
        if n_kept >= CAP:
            break
        raw = cc.get(base + fn, (off, ln))
        if raw is None:
            continue
        try:
            txt = gzip.decompress(raw).decode("utf-8", "replace")
        except Exception:
            continue
        seen_hosts = {}
        for line in txt.splitlines():
            parts = line.split(" ", 2)
            if len(parts) < 3:
                continue
            try:
                j = json.loads(parts[2])
            except Exception:
                continue
            if j.get("status") != "200" or "html" not in j.get("mime-detected", j.get("mime", "")):
                continue
            url = j["url"]
            m = re.match(r"https?://([^/]+)(/[^?#]*)?", url)
            if not m:
                continue
            host, path = m.group(1).lower(), m.group(2) or "/"
            if host in exclude or host.removeprefix("www.") in exclude:
                continue
            if not (PATH.search(path) or path in ("/", "/index.html", "/index.php", "/index.htm")):
                continue
            if seen_hosts.get(host, 0) >= 2:
                continue
            seen_hosts[host] = seen_hosts.get(host, 0) + 1
            fo.write(json.dumps({"url": url, "host": host, "tld": host.rsplit(".", 1)[-1], "filename": j["filename"], "offset": int(j["offset"]),
                                 "length": int(j["length"]), "languages": j.get("languages", ""), "crawl": crawl}) + "\n")
            n_kept += 1
print(country, "blocks", len(picked), "candidates", n_kept)
