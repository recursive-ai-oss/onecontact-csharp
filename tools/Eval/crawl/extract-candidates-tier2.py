#!/usr/bin/env python3
"""Step 3 (Tier 2 group B): finds address and name strings in the stored page lines and draws a diverse sample of each, as drafts to be labelled.

usage: extract-candidates-tier2.py <out-dir> <seed> <addresses-per-country> <persons-per-country> <companies-per-country> [CC,CC,...]

Reads <out-dir>/pages.jsonl (written by fetch-pages.py from select-pages-tier2.py's candidates); writes
<out-dir>/draft-address-<CC>.jsonl and <out-dir>/draft-name-<CC>.jsonl. Strings are kept verbatim, in the order and with the line breaks of
the page. A page belongs to the country of its host TLD, and counts only when it holds that country's postcode shape in a line with a
street or box; names are taken only from such pages. The sample is stratified by the rarest feature of: PO box, unit, rural, other
language, multi-line, single line, upper case, hyphenated number. At most PER_HOST (default 2) candidates per host. Prints only counts.
EXCLUDE=file1,file2 skips strings already drafted.
"""
import json
import os
import random
import re
import sys
from collections import defaultdict

import tier2

out, seed = sys.argv[1], int(sys.argv[2])
n_addr, n_person, n_company = int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
only = sys.argv[6].split(",") if len(sys.argv) > 6 else list(tier2.TLDS)
rng = random.Random(seed)
PER_HOST = int(os.environ.get("PER_HOST", "2"))
EXCLUDED = set()
for _f in filter(None, os.environ.get("EXCLUDE", "").split(",")):
    for _l in open(_f, encoding="utf-8"):
        EXCLUDED.add(re.sub(r"\W+", "", json.loads(_l)["text"].lower()))

LABEL = re.compile(r"^(?:address|our address|registered office|registered address|mailing address|postal address|head ?office|visit us|find us|location|office|"
                   r"dirección|domicilio|direcci[oó]n postal|sede|ubicaci[oó]n|oficinas?|"
                   r"indirizzo|sede legale|sede operativa|sede centrale|dove siamo|"
                   r"morada|endereço|sede|localização|contacto|"
                   r"adrese|addreess|tel)\s*[:\-–]\s*", re.I)
CITY_WORD = re.compile(r"^(?:[A-ZÀ-ÝÑÇ][\wÀ-ÿ'’\.\-]*|de|del|la|las|los|di|da|do|dos|das|e|y|d'|dell'|sul|sulla|in|al|en|a|of|upon|on)$")
STOP_TAIL = re.compile(r"^(?:Tel|Tlf|Tlfn|Telf|Telefono|Telefone|Fax|Email|E-mail|Mail|Phone|T:|F:|M:|P\.?IVA|CIF|NIF|CF|VAT|Reg|Registered|Company|Registro|Inscrit|Número|"
                       r"Lun|Mar|Mon|Tue|Horario|Orari|Opening|Open|www|http)", re.I)

pages = [json.loads(l) for l in open(os.path.join(out, "pages.jsonl"), encoding="utf-8")]


def page_country(p):
    c = tier2.TLD_TO_COUNTRY.get(p["tld"])
    if c is None or c not in only:
        return None
    rx = tier2.POSTCODE[c]
    hits = sum(1 for ln in p["lines"] if len(ln) <= 250 and rx.search(ln))
    return c if hits else None


def clip(line, c):
    """Trims after the postcode (and the locality that follows it, for the countries that write the postcode first)."""
    rx = tier2.POSTCODE[c]
    m = None
    for m in rx.finditer(line):
        pass
    if not m:
        return line.strip()
    end = m.end()
    if c in tier2.POST_FIRST:
        # the locality follows the postcode: take up to five capitalized words, stopping at a phone, label or separator
        rest = line[end:]
        sep = re.match(r"[\s,\-–]+", rest)
        pos = sep.end() if sep else 0
        used, last = 0, end
        for t in re.finditer(r"\S+", rest[pos:]):
            tt = t.group().strip(",;|")
            if STOP_TAIL.match(tt) or re.fullmatch(r"[\d\+\(\)\-\./]+", tt) or tt in ("-", "–", "|", "·", "•", "/"):
                break
            if not CITY_WORD.match(tt.strip("()")) and not re.fullmatch(r"\([A-Za-z]{2,}\)", tt):
                break
            used += 1
            last = end + pos + t.end()
            if t.group().endswith((",", ";", "|")) or used >= 5:
                break
        end = last
    tail = tier2.COUNTRY_TAIL.match(line[end:])
    if tail:
        end += tail.end()
    s = LABEL.sub("", line[:end])
    return s.strip(" ,;|-–•·")


def addresses(p, c):
    rx = tier2.POSTCODE[c]
    L = p["lines"]
    res = []
    for k, ln in enumerate(L):
        if len(ln) > 300 or not rx.search(ln):
            continue
        cur = clip(ln, c)
        first = rx.search(cur)
        head = cur[:first.start()] if first else cur
        has_street = bool(tier2.STREETWORD.search(head)) or bool(re.search(r"\d", head)) or bool(tier2.BOX.search(head))
        lines = [cur]
        if not has_street or len(head.strip(" ,")) < 6:
            j = k - 1
            added = 0
            while j >= 0 and added < 3 and len(L[j]) <= 90:
                prev = LABEL.sub("", L[j]).strip(" ,;|")
                if not prev or STOP_TAIL.match(prev):
                    break
                lines.insert(0, prev)
                added += 1
                if tier2.STREETWORD.search(prev) or re.search(r"\d", prev) or tier2.BOX.search(prev):
                    break
                j -= 1
            joined = " ".join(lines[:-1]) + " " + lines[-1][:max(0, (first.start() if first else 0))]
            if not (tier2.STREETWORD.search(joined) or tier2.BOX.search(joined) or re.search(r"\d", " ".join(lines[:-1]))):
                continue
        nxt = L[k + 1] if k + 1 < len(L) else ""
        if tier2.COUNTRY_LINE.match(nxt):
            lines.append(nxt)
        text = "\n".join(lines)
        if 10 <= len(text) <= 240 and len(re.findall(r"[A-Za-zÀ-ÿ]{3,}", text)) >= 2:
            res.append(text)
    return res


def strata(text, p):
    s = []
    if tier2.BOX.search(text):
        s.append("box")
    if tier2.UNIT.search(text):
        s.append("unit")
    if tier2.RURAL.search(text):
        s.append("rural")
    if tier2.OTHERLANG.search(text):
        s.append("otherlang")
    s.append("multiline" if "\n" in text else "oneline")
    if text.upper() == text and re.search(r"[A-Z]{3}", text):
        s.append("upper")
    if re.search(r"\d+\s?[-/]\s?\d+", text):
        s.append("hyphen")
    return s


def stratified(items, n, per_host):
    seen, uniq = set(), []
    rng.shuffle(items)
    host_count = defaultdict(int)
    for it in items:
        k = re.sub(r"\W+", "", it["text"].lower())
        if k in seen or k in EXCLUDED or host_count[it["host"]] >= per_host:
            continue
        seen.add(k)
        host_count[it["host"]] += 1
        uniq.append(it)
    counts = defaultdict(int)
    for it in uniq:
        for s in it["strata"]:
            counts[s] += 1
    buckets = defaultdict(list)
    for it in uniq:
        buckets[min(it["strata"], key=lambda s: counts[s])].append(it)
    order = sorted(buckets, key=lambda b: len(buckets[b]))
    chosen = []
    while len(chosen) < n and any(buckets[b] for b in order):
        for b in order:
            if buckets[b] and len(chosen) < n:
                chosen.append(buckets[b].pop())
    return chosen, len(uniq)


by_country = defaultdict(list)
name_pages = defaultdict(list)
for p in pages:
    c = page_country(p)
    if not c:
        continue
    name_pages[c].append(p)
    for t in addresses(p, c):
        by_country[c].append({"text": t, "host": p["host"], "warc": p["warc"], "languages": p["languages"], "strata": strata(t, p)})

for c in only:
    ch, uniq = stratified(by_country[c], n_addr, PER_HOST)
    with open(os.path.join(out, f"draft-address-{c}.jsonl"), "w", encoding="utf-8") as f:
        for i, it in enumerate(ch):
            f.write(json.dumps({"id": f"addr-{c}-{i:03d}", "country": c, **it}, ensure_ascii=False) + "\n")
    print(f"address {c}: pages {len(name_pages[c])}, found {len(by_country[c])}, unique {uniq}, drafted {len(ch)}")

# ---------- names ----------
UP = r"[A-ZÀ-ÖØ-Þ]"
LO = r"[a-zà-öø-ÿ]"
CAPW = rf"(?:{UP}{LO}+(?:['’\-]{UP}?{LO}+)*\.?|{UP}\.|{UP}{UP}+(?!{LO}))"
PART = r"(?:van|von|de|del|della|degli|dei|dello|da|das|dos|do|di|du|la|le|los|las|y|e|mac|mc|o'|ní|ni|ó|ap|fitz|ter|ten|der|den|bin|al|el|st\.?)"
TITLE = (r"(?:Dr|Mr|Mrs|Ms|Miss|Mx|Prof|Professor|Rev|Reverend|Sir|Dame|Lord|Lady|Cllr|Hon|Fr|Father|Canon|Judge|Capt|Col|Maj|Lt|Sgt|"
         r"Don|Doña|Dña|D|Sr|Sra|Srta|Lic|Ing|Arq|Dra|"
         r"Avv|Dott|Dott\.ssa|Dottssa|Ing|Arch|Geom|Rag|Sig|Sig\.ra|Sig\.na|Notaio|Prof\.ssa|Mons|Don|"
         r"Eng|Engª|Eng\.ª|Arq|Dr\.ª|Drª|Dra|Prof\.ª|Profª|Sr\.ª|Srª|Mgr|Rev)\.?")
CRED = (r"(?:MBE|OBE|CBE|KC|QC|BSc|MSc|BA|MA|LLB|LLM|MBA|PhD|FCA|ACA|ACCA|FCCA|CTA|MRICS|FRICS|MCIOB|CEng|FRCS|MRCGP|MRCP|FRCP|MBChB|MBBS|MB|BDS|BVSc|MRCVS|DipFA|"
        r"Jr|Sr|II|III|IV|SC|MD|CPA|Esq|"
        r"Esp|Máster|Ph\.D|DEA|CPA|PMP)\.?")
PERSON = re.compile(rf"^(?:{TITLE}\s+)?{CAPW}(?:\s+(?:{PART}\s+)*{CAPW}){{1,4}}(?:,?\s+{CRED})*(?:,\s*{CRED})*$")
MULTI = re.compile(rf"^(?:{TITLE}\s+)?{CAPW}(?:\s+{CAPW})?\s+(?:and|&|e|y|et)\s+(?:{TITLE}\s+)?{CAPW}(?:\s+{CAPW}){{0,2}}$")
STOP = set("""home about contact us services service our team news blog privacy policy terms login search menu skip content call now get free quote request learn view all click here location
locations hours monday tuesday wednesday thursday friday saturday sunday company ltd limited plc llp meet staff careers join shop cart checkout account sign register faq faqs gallery
resources events donate support help sitemap copyright rights reserved follow share facebook twitter instagram linkedin youtube email phone fax street road avenue suite drive
welcome new used customer reviews testimonials quick links products specials offers schedule appointment book online forms insurance payment directions north south east west
first second third last next previous page submit send message name general information board directors members officers committee department departments office offices city county
state ireland england scotland wales kingdom spain españa italia italy portugal malta jersey guernsey man andorra marino vaticano contacto contatti contatto inicio servicios nosotros
empresa noticias aviso legal cookies privacidad politica quienes somos equipo chi siamo azienda servizi notizie sedi sede home pagina página produtos serviços empresa contactos equipa
cerca buscar ricerca accedi entrar acceder carrito carrello comprar acquista leggi saber más mais ver todos tutti vai""".split())
ROLE = re.compile(r"\b(?:director|manager|solicitor|partner|ceo|cfo|coo|cto|owner|founder|principal|associate|consultant|dentist|doctor|surgeon|nurse|teacher|head|chair|chairman|"
                  r"secretary|treasurer|administrator|coordinator|specialist|technician|supervisor|lawyer|barrister|accountant|architect|engineer|agent|broker|vet|physiotherapist|"
                  r"president|vice|chief|councillor|mayor|manager|officer|"
                  r"director[a]?|gerente|abogad[oa]|socio|socia|propietari[oa]|fundador[a]?|presidente|secretari[oa]|administrador[a]?|arquitect[oa]|ingenier[oa]|médic[oa]|doctor[a]?|"
                  r"notari[oa]|procurador[a]?|consejer[oa]|responsable|coordinador[a]?|"
                  r"amministratore|titolare|avvocato|avvocata|socio|fondatore|fondatrice|presidente|segretari[oa]|direttore|direttrice|dottore|dottoressa|commercialista|architetto|ingegnere|"
                  r"notaio|geometra|consulente|responsabile|"
                  r"diretor[a]?|gerente|advogad[oa]|sócio|sócia|proprietári[oa]|fundador[a]?|presidente|administrador[a]?|arquitet[oa]|engenheir[oa]|médic[oa]|notári[oa]|solicitador[a]?|"
                  r"contabilista|técnic[oa])\b", re.I)
LEGAL = re.compile(r"(?:\b(?:Ltd|Limited|plc|PLC|LLP|LP|CIC|DAC|CLG|UC|Unlimited Company|Teoranta|Teo|Cyf|"
                   r"S\.?L\.?U?|S\.?A\.?U?|S\.?L\.?L|S\.?C\.?|S\.?Coop|S\.?A\.?L|S\.?Com|C\.B|CB|S\.?R\.?L|S\.?r\.?l\.?s?|S\.?p\.?A|S\.?n\.?c|S\.?a\.?s|S\.?s|S\.?c\.?a\.?r\.?l|"
                   r"Soc\.?\s*Coop|Società|Lda|Unipessoal|S\.?A|Limitada|Lda\.|S\.?G\.?P\.?S|E\.?I\.?R\.?L|"
                   r"Co|Inc|Corp|GmbH|Foundation|Fundación|Fondazione|Fundação|Associazione|Asociación|Associação|Cooperativa|Cooperative)\b\.?)\s*$", re.I)
COMPANY_LABEL = re.compile(r"(?:Titular|Raz[oó]n social|Denominaci[oó]n social|Empresa|Nombre comercial|Responsable|Ragione sociale|Denominazione sociale|Titolare|"
                           r"Denomina[cç][aã]o social|Firma|Entidade|Propriet[aá]rio|Registered (?:name|company)|Company name|Trading name|Trading as|Operated by|Owned and operated by|"
                           r"Company|A company registered)\s*[:\-–]\s*(.{3,90})$", re.I)
COPY = re.compile(r"(?:©|\(c\)|&copy;|Copyright|Copyleft)\s*(?:\d{4}(?:\s*[-–]\s*\d{4})?)?[\s,:.\-–]*(.+)", re.I)


def clean_company(s):
    s = re.split(r"\s*(?:\.\s+All|\s+All rights|\s*\|\s|\s+Powered by|\s+Designed by|\s+Website|\s+Privacy|\s+Terms|\s+Site by|\s+Todos los derechos|\s+Tutti i diritti|"
                 r"\s+Todos os direitos|\s+Reservados|\s+—\s|\s+Registered|\s+Company (?:No|Number|Reg)|\s+VAT|\s+CIF|\s+P\.?\s?IVA|\s+NIF|\s+Reg\.|\s+Tel)", s, flags=re.I)[0]
    return s.strip(" .,;|-–•©")


def person_ok(line):
    if len(line) > 60 or re.search(r"\d|@|http|www\.", line):
        return False
    words = re.findall(r"[A-Za-zÀ-ÿ'’\-\.]+", line)
    if len(words) < 2:
        return False
    return not any(w.lower().strip(".,") in STOP for w in words)


person_items, company_items = defaultdict(list), defaultdict(list)
for c in only:
    for p in name_pages[c]:
        L = p["lines"]
        for k, ln in enumerate(L):
            if person_ok(ln) and (PERSON.match(ln) or MULTI.match(ln)):
                nxt = L[k + 1] if k + 1 < len(L) else ""
                role_ctx = len(nxt) <= 70 and ROLE.search(nxt)
                titled = re.match(rf"^{TITLE}\s", ln) or re.search(rf",\s*{CRED}$", ln)
                if role_ctx or titled or (MULTI.match(ln) and len(ln.split()) <= 5):
                    kind = "multi" if MULTI.match(ln) else ("titled" if titled else "plain")
                    person_items[c].append({"text": ln, "host": p["host"], "warc": p["warc"], "strata": [kind], "src": "text", "languages": p["languages"]})
            if len(ln) < 170:
                m = COMPANY_LABEL.search(ln) or COPY.search(ln)
                if m:
                    comp = clean_company(m.group(1))
                    if 3 <= len(comp) <= 70 and len(comp.split()) <= 9 and not re.search(r"\d{4,}|rights|reserved|derechos|diritti", comp, re.I):
                        company_items[c].append({"text": comp, "host": p["host"], "warc": p["warc"], "strata": ["legal" if LEGAL.search(comp) else "bare"],
                                                 "src": "label" if COMPANY_LABEL.search(ln) else "copyright", "languages": p["languages"]})
        for o in p["ld"]:
            nm = o["name"].strip()
            if "person" in o["type"].lower():
                if 3 <= len(nm) <= 60 and PERSON.match(nm) and person_ok(nm):
                    person_items[c].append({"text": nm, "host": p["host"], "warc": p["warc"], "strata": ["plain"], "src": "ldjson", "languages": p["languages"]})
            elif 3 <= len(nm) <= 70 and len(nm.split()) <= 9:
                company_items[c].append({"text": nm, "host": p["host"], "warc": p["warc"], "strata": ["legal" if LEGAL.search(nm) else "bare"], "src": "ldjson", "languages": p["languages"]})
        sn = p["meta"].get("og:site_name", "").strip()
        if 3 <= len(sn) <= 70:
            company_items[c].append({"text": sn, "host": p["host"], "warc": p["warc"], "strata": ["legal" if LEGAL.search(sn) else "bare"], "src": "site_name", "languages": p["languages"]})

for c in only:
    pe, upe = stratified(person_items[c], n_person, PER_HOST)
    co, uco = stratified(company_items[c], n_company, PER_HOST)
    with open(os.path.join(out, f"draft-name-{c}.jsonl"), "w", encoding="utf-8") as f:
        for i, it in enumerate(pe + co):
            f.write(json.dumps({"id": f"name-{c}-{i:03d}", "country": c, **it}, ensure_ascii=False) + "\n")
    print(f"name {c}: persons found {len(person_items[c])} drafted {len(pe)}; companies found {len(company_items[c])} drafted {len(co)}")
