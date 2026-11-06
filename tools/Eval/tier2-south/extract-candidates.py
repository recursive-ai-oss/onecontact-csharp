#!/usr/bin/env python3
"""Step 3 for the Tier 2 countries: finds address and name strings in the stored page lines and draws a diverse sample of each, as drafts.

usage: extract-candidates.py <country> <pages.jsonl> <out-dir> <seed> <addresses> <persons> <companies>

Reads the stored visible lines of the pages of one country (see fetch-pages.py) and writes <out-dir>/draft-address-<CC>.jsonl and
<out-dir>/draft-name-<CC>.jsonl. Strings are kept verbatim, with the line breaks of the page; a leading label such as "Adres:" is
cut. An address needs a postcode of the country's own shape plus a street or box part; a name needs a title, a nearby role word, a
structured-data record or a legal form. The draw is stratified on the rarest feature of each item (box, unit, rural, one line or
several, capitals, title, legal form, ...), with at most two addresses and two persons per host. Prints only counts.
"""
import json
import os
import random
import re
import sys
from collections import defaultdict

cc_code, pages_path, out, seed = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
n_addr, n_person, n_company = int(sys.argv[5]), int(sys.argv[6]), int(sys.argv[7])
rng = random.Random(seed)
EXCLUDED = set()
for _f in filter(None, os.environ.get("EXCLUDE", "").split(",")):
    for _l in open(_f, encoding="utf-8"):
        EXCLUDED.add(re.sub(r"\W+", "", json.loads(_l)["text"].lower()))

L = r"[^\W\d_]"
UP = "A-ZÀ-ÖØ-ÞĀĂĄĆČĎĐĒĘĚĞİŁŃŇŐŒŘŚŞŠŤŮŰŹŻŽȘȚ"
LANG = {"PL": "pl", "CZ": "cs", "SK": "sk", "SI": "sl", "HR": "hr", "HU": "hu", "RO": "ro", "MX": "es", "AU": "en", "GB": "en", "JE": "en", "GG": "en", "IM": "en", "IE": "en", "ES": "es", "PT": "pt", "IT": "it", "MT": "en", "SM": "it", "VA": "it", "AD": "ca"}

# Postcode shapes. Each is searched in a line; the text after it up to the next comma is the locality for the countries that write
# the postcode first.
PC = {
    "GB": re.compile(r"\b(?!(?:JE|GY|IM)\d)[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}\b"),
    "JE": re.compile(r"\bJE\d[A-Z\d]?\s?\d[A-Z]{2}\b"),
    "GG": re.compile(r"\bGY\d[A-Z\d]?\s?\d[A-Z]{2}\b"),
    "IM": re.compile(r"\bIM\d[A-Z\d]?\s?\d[A-Z]{2}\b"),
    "IE": re.compile(r"\b(?:D6W|[ACDEFHKNPRTVWXY]\d{2})\s?[0-9ACDEFHKNPRTVWXY]{4}\b|\bDublin\s?\d{1,2}W?\b|\b(?:Co\.?|County)\s+[A-Z][a-z]+"),
    "ES": re.compile(rf"\b(?:0[1-9]|[1-4]\d|5[0-2])\d{{3}}\s+[{UP}]{L}+"),
    "PT": re.compile(r"\b[1-9]\d{3}\s?-\s?\d{3}\b"),
    "IT": re.compile(rf"\b(?:0\d|[1-9]\d)\d{{3}}\s+[{UP}]{L}+"),
    "MT": re.compile(r"\b[A-Z]{3}\s?\d{2,4}\b"),
    "SM": re.compile(r"\b4789\d\b"),
    "VA": re.compile(r"\b00120\b"),
    "AD": re.compile(r"\bAD\s?[1-7]\d{2}\b"),
    "PL": re.compile(rf"\b\d{{2}}\s?-\s?\d{{3}}\s+[{UP}]{L}+"),
    "CZ": re.compile(rf"\b(?:CZ[- ])?\d{{3}}\s?\d{{2}}\s+[{UP}]{L}+"),
    "SK": re.compile(rf"\b(?:SK[- ])?\d{{3}}\s?\d{{2}}\s+[{UP}]{L}+"),
    "SI": re.compile(rf"\b(?:SI[- ])?[1-9]\d{{3}}\s+[{UP}]{L}+"),
    "HR": re.compile(rf"\b(?:HR[- ])?[1-5]\d{{4}}\s+[{UP}]{L}+"),
    "HU": re.compile(rf"\b(?:H[- ])?[1-9]\d{{3}}\s+[{UP}]{L}+"),
    "RO": re.compile(rf"(?:\b(?:cod\s+po[sşș]tal|cp|c\.p\.|RO)[:\s.-]*)?\b\d{{6}}\b", re.I),
    "MX": re.compile(r"\b(?:C\.?\s?P\.?\s*:?\s*)\d{5}\b|\b\d{5}\s*,?\s+[A-ZÁÉÍÓÚÑ][\w\.]+(?:\s+[A-Za-zÁÉÍÓÚÑáéíóúñ\.]+){0,3},?\s+(?:Ags|Aguascalientes|B\.?C\.?S?|Baja|Camp|Chis|Chih|Coah|Col|CDMX|D\.?F|Dgo|Gto|Gro|Hgo|Jal|M[eé]x|Mich|Mor|Nay|N\.?L|Nuevo|Oax|Pue|Qro|Quer[eé]taro|Q\.?\s?Roo|S\.?L\.?P|Sin|Son|Tab|Tamps|Tlax|Ver|Yuc|Zac)\b"),
    "AU": re.compile(r"\b(?:NSW|VIC|QLD|SA|WA|TAS|NT|ACT)\.?\s+\d{4}\b|\b(?:New South Wales|Victoria|Queensland|South Australia|Western Australia|Tasmania|Northern Territory)\s+\d{4}\b"),
}
COUNTRY_LINE = {
    "GB": r"United Kingdom|UK|U\.K\.|England|Scotland|Wales|Northern Ireland|Great Britain", "JE": r"Jersey|Channel Islands|C\.I\.", "GG": r"Guernsey|Channel Islands|C\.I\.",
    "IM": r"Isle of Man|British Isles", "IE": r"Ireland|[ÉE]ire|Republic of Ireland", "ES": r"Espa[ñn]a|Spain", "PT": r"Portugal", "IT": r"Italia|Italy",
    "MT": r"Malta", "SM": r"San Marino|Repubblica di San Marino", "VA": r"Citt[àa] del Vaticano|Vatican City|Vaticano", "AD": r"Andorra|Andorre",
    "PL": r"Polska|Poland", "CZ": r"[ČC]esk[áa] republika|Czech Republic|Czechia|[ČC]esko", "SK": r"Slovensko|Slovensk[áa] republika|Slovakia",
    "SI": r"Slovenija|Slovenia", "HR": r"Hrvatska|Croatia", "HU": r"Magyarorsz[áa]g|Hungary", "RO": r"Rom[âa]nia|Romania",
    "MX": r"M[ée]xico|Mexico|M[ée]x\.?", "AU": r"Australia",
}
COUNTRY_RE = re.compile(rf"^(?:{COUNTRY_LINE[cc_code]})\.?\s*$", re.I)
LABEL = re.compile(r"^(?:adres(?:a)?(?: siedziby| korespondencyjny| do korespondencji)?|siedziba|si[ée]dziba|sídlo|s[ií]dlo|adresa(?: sídla| provozovny)?|provozovna|prev[aá]dzka|"
                   r"naslov(?: sedeža)?|sjedište|sjediste|adresa tvrtke|cím|székhely|telephely|levelezési cím|sediul(?: social)?|sediu|adres[aă]|punct de lucru|"
                   r"direcci[oó]n|domicilio(?: fiscal)?|ubicaci[oó]n|oficinas?|matriz|sucursal|address|postal address|street address|mailing address|physical address|"
                   r"our address|visit us|find us|location|office|head ?office|registered office|principal place of business|kde n[aá]s najdete|gdzie jeste[sś]my|jak dojecha[cć])\s*[:\-–]\s*", re.I)
BOX = re.compile(r"\b(?:apartado(?:\s+de\s+correos)?\s*\d|apdo\.?\s*\d|casella\s+postale|caixa\s+postal|private\s+bag|poste\s+restante|skr\.?\s*poczt(?:owa)?\.?|skrytka\s+pocztowa|p\.?\s?o\.?\s*box|po\s+box|gpo\s+box|locked\s+bag|private\s+bag|rmb|rmd|reply\s+paid|poštovní\s+přihrádka|"
                 r"p\.?\s?p\.?\s*\d|pošt\.?\s*schr[aá]nka|po[sš]tov[aá]\s+schr[aá]nka|poštni\s+predal|pp\s+\d|poštanski\s+pretinac|pf\.?\s*\d|postafi[oó]k|c\.?p\.?\s*\d+\s*,?\s*of|"
                 r"oficiul\s+po[sş]tal|apartado\s+postal|ap(?:do)?\.?\s*postal|a\.?\s?p\.?\s*\d)\b", re.I)
UNIT = re.compile(r"\b(?:flat|apartment|apt\.?|planta|escalera|esc\.?|puerta|bajo|piano|scala|interno|andar|esq\.?|dto\.?|loja|sala|lok\.?|m\.|suite|ste\.?|unit|level|lvl|shop|floor|fl\.?|bldg|building|tower|p[řr]\.?|pat[rř]o|poschodie|posch\.?|nadst\.?|[eé]p\.?|em\.?|ajt[oó]|fszt\.?|"
                  r"bl\.?|sc\.?|ap\.?|et\.?|etaj|int\.?|interior|piso|depto\.?|dpto\.?|local|oficina|ofic\.?|despacho|torre|col\.?|colonia|nadstropje|pritli[cč]je|kat|kat\.?|zgrada)\s*[\d#A-Za-z]|#\s?\d", re.I)
RURAL = re.compile(r"\b(?:pol[ií]gono|urbanizaci[oó]n|urb\.|localit[àa]|loc\.|frazione|fraz\.|contrada|quinta|aldeia|lugar|estrada|townland|industrial estate|business park|farm|cottage|wie[sś]|osiedle|os\.|č\.\s?p\.|[čc]\.p\.|obec|dedina|selo|naselje|puszta|major|tanya|sat\b|com\.|comuna|jud\.|carretera|carr\.|km\.?\s?\d|rancho|ejido|"
                   r"highway|hwy|rd\b|road|lot\s+\d|rmb|rmd|mail\s+service|pmb|station)\b", re.I)
DIGITS_OUTSIDE = re.compile(r"\d+\s*[A-Za-z]?(?:\s*[/\-]\s*\d+[A-Za-z]?)?")


def strip_pc(line, cc):
    return PC[cc].sub(" ", line)


def clip(line, cc):
    s = LABEL.sub("", line.strip())
    return s.strip(" ,;|•–-·")


def addresses(p, cc):
    lines = p["lines"]
    res = []
    for k, ln in enumerate(lines):
        if len(ln) > 300 or not PC[cc].search(ln):
            continue
        cur = clip(ln, cc)
        rest = strip_pc(cur, cc)
        block = [cur]
        has_street = bool(DIGITS_OUTSIDE.search(rest)) or bool(BOX.search(cur))
        if not has_street:
            j, added = k - 1, 0
            while j >= 0 and added < 3 and len(lines[j]) <= 90:
                prev = clip(lines[j], cc)
                if not prev:
                    break
                block.insert(0, prev)
                added += 1
                if BOX.search(prev) or (re.search(r"\d", prev) and not re.search(r"@|\+\d|tel|fax|www", prev, re.I)):
                    has_street = True
                    break
                j -= 1
            if not has_street:
                continue
            if any(re.search(r"@|tel\.?|fax|www\.|http", b, re.I) for b in block[:-1]):
                continue
        nxt = lines[k + 1] if k + 1 < len(lines) else ""
        if COUNTRY_RE.match(nxt):
            block.append(nxt)
        text = "\n".join(block)
        # Running prose (a privacy notice that quotes an address) is not an address block: every line stays short.
        if any(len(b.split()) > 10 for b in block):
            continue
        # Page furniture that carries digits shaped like a postcode: copyright lines, bank and tax identifiers, prices.
        if re.search(r"©|\(c\)|copyright|pridr[žz]an|jogok|all rights|\bIBAN\b|\bTRR\b|\bIČO?\b|\bNIP\b|\bREGON\b|\bKč\b|\bPLN\b|\bHUF\b|\bEUR\b|\bKM\b|\bkn\b", text, re.I):
            continue
        # A bare six-digit number is not enough for Romania: phone numbers and catalogue codes look the same.
        if cc == "RO" and not re.search(r"\b(?:str|strada|bd|bdul|b-dul|bulevardul|calea|aleea|[şsș]os|[şsș]oseaua|pia[țt]a|splaiul|nr|no|cod|street|road|avenue)\b", text, re.I):
            continue
        if 10 <= len(text) <= 230 and not re.search(r"@|https?://|www\.", text):
            res.append(text)
    return res


def a_strata(t):
    s = []
    if BOX.search(t):
        s.append("box")
    if UNIT.search(t):
        s.append("unit")
    s.append("multiline" if "\n" in t else "oneline")
    if re.search(r"[A-Z]{3}", t) and t.upper() == t:
        s.append("upper")
    if RURAL.search(t):
        s.append("rural")
    if re.search(r"\d+\s*[/\-]\s*\d+", t):
        s.append("compound")
    if COUNTRY_RE.search(t.split("\n")[-1]):
        s.append("country-line")
    return s or ["plain"]


def stratified(items, n, key_fn, per_host):
    seen, uniq = set(), []
    rng.shuffle(items)
    hc = defaultdict(int)
    for it in items:
        k = re.sub(r"\W+", "", it["text"].lower())
        if k in seen or k in EXCLUDED or hc[it["host"]] >= per_host:
            continue
        seen.add(k)
        hc[it["host"]] += 1
        uniq.append(it)
    buckets = defaultdict(list)
    for it in uniq:
        buckets[key_fn(it)].append(it)
    order = sorted(buckets, key=lambda b: len(buckets[b]))
    chosen = []
    while len(chosen) < n and any(buckets[b] for b in order):
        for b in order:
            if buckets[b] and len(chosen) < n:
                chosen.append(buckets[b].pop())
    return chosen


def rarest(counts):
    return lambda it: min(it["strata"], key=lambda s: counts.get(s, 0))


pages = [json.loads(l) for l in open(pages_path, encoding="utf-8")]
stats = {}
addr_items = []
for p in pages:
    for t in addresses(p, cc_code):
        addr_items.append({"text": t, "host": p["host"], "warc": p["warc"], "url_kind": "root" if re.match(r"https?://[^/]+/?$", p["url"]) else "inner", "strata": a_strata(t)})
counts = defaultdict(int)
for it in addr_items:
    for s in it["strata"]:
        counts[s] += 1
chosen = stratified(addr_items, n_addr, rarest(counts), 2)
with open(os.path.join(out, f"draft-address-{cc_code}.jsonl"), "w", encoding="utf-8") as f:
    for i, it in enumerate(chosen):
        f.write(json.dumps({"id": f"addr-{cc_code}-{i:03d}", "country": cc_code, **it}, ensure_ascii=False) + "\n")
stats["address"] = (len(addr_items), len(chosen), dict(counts))

# ---------- names ----------
TITLES = {
    "GB": r"Dr\.?|Mr\.?|Mrs\.?|Ms\.?|Miss|Mx|Prof\.?|Professor|Rev\.?|Reverend|Sir|Dame|Lord|Lady|Cllr|Councillor|Hon\.?|Fr\.?|Canon|Judge|Capt\.?|Col\.?|Maj\.?",
    "JE": r"Dr\.?|Mr\.?|Mrs\.?|Ms\.?|Miss|Prof\.?|Rev\.?|Sir|Dame|Deputy|Connétable|Constable|Senator|Hon\.?|Canon",
    "GG": r"Dr\.?|Mr\.?|Mrs\.?|Ms\.?|Miss|Prof\.?|Rev\.?|Sir|Dame|Deputy|Hon\.?|Canon",
    "IM": r"Dr\.?|Mr\.?|Mrs\.?|Ms\.?|Miss|Prof\.?|Rev\.?|Sir|Dame|Hon\.?|Canon|MHK|MLC",
    "IE": r"Dr\.?|Mr\.?|Mrs\.?|Ms\.?|Miss|Prof\.?|Rev\.?|Fr\.?|Sr\.?|Cllr|Councillor|Judge|Senator|Deputy|Canon|Captain|Hon\.?",
    "ES": r"Don|Do[ñn]a|D\.|D[ñn]a\.?|Sr\.?|Sra\.?|Srta\.?|Dr\.?|Dra\.?|Lic\.?|Ing\.?|Arq\.?|Prof\.?|Profa\.?|Abg\.?|Excmo\.?|Ilmo\.?|Fr\.?|P\.",
    "PT": r"Dr\.?|Dra\.?|Eng\.?|Eng\.?[ªa]\.?|Arq\.?|Arq\.?[ªa]\.?|Prof\.?|Prof\.?[ªa]\.?|Sr\.?|Sra\.?|Sr\.?[ªa]\.?|Mestre|Dom|D\.|Padre|Cons\.?",
    "IT": r"Dott\.?|Dott\.?\s?ssa\.?|Dr\.?|Dr\.?\s?ssa\.?|Avv\.?|Ing\.?|Arch\.?|Geom\.?|Rag\.?|Prof\.?|Prof\.?\s?ssa\.?|Sig\.?|Sig\.?\s?ra\.?|Sig\.?\s?na\.?|Notaio|Don|Mons\.?|Cav\.?|On\.?|P\.",
    "MT": r"Dr\.?|Mr\.?|Mrs\.?|Ms\.?|Miss|Prof\.?|Perit|Av\.?|Avv\.?|Rev\.?|Mons\.?|Fr\.?|Hon\.?|Sir|Ing\.?|Kav\.?|Dun|Dr",
    "SM": r"Dott\.?|Dott\.?\s?ssa\.?|Dr\.?|Avv\.?|Ing\.?|Arch\.?|Geom\.?|Rag\.?|Prof\.?|Sig\.?|Sig\.?\s?ra\.?|Notaio",
    "VA": r"Dott\.?|Dr\.?|Avv\.?|Ing\.?|Prof\.?|Mons\.?|Don|Card\.?|S\.E\.|Em\.?mo|P\.|Fr\.?|Sig\.?|Suor|Sr\.?",
    "AD": r"Sr\.?|Sra\.?|Srta\.?|Dr\.?|Dra\.?|Don|Do[ñn]a|Ing\.?|Arq\.?|Lic\.?|Prof\.?|Hble\.?|Il·lm\.?|Excm\.?|Mn\.?|Mossèn",
    "PL": r"dr(?:\s+hab\.?)?|mgr(?:\s+in[zż]\.?)?|in[zż]\.?|prof\.?(?:\s+(?:dr\s+hab\.?|zw\.?|nadzw\.?|UW|UJ))?|lek\.?(?:\s+med\.?)?|adw\.?|r\.?\s?pr\.?|rad\.?\s+pr\.?|ks\.?|lic\.?|dr\s+n\.\s?med\.?|arch\.?|techn\.?",
    "CZ": r"Ing\.?(?:\s+arch\.?)?|Mgr\.?|Bc\.?|JUDr\.?|MUDr\.?|MVDr\.?|RNDr\.?|PhDr\.?|PaedDr\.?|MgA\.?|doc\.?|prof\.?|Dr\.?|DiS\.?|Ing\.\s?arch\.?|arch\.?",
    "SK": r"Ing\.?(?:\s+arch\.?)?|Mgr\.?|Bc\.?|JUDr\.?|MUDr\.?|MVDr\.?|RNDr\.?|PhDr\.?|PaedDr\.?|MgA\.?|doc\.?|prof\.?|Dr\.?|PharmDr\.?",
    "SI": r"dr\.?|mag\.?|prof\.?|doc\.?|univ\.?\s?dipl\.?\s?(?:in[zž]\.?|jur\.?|ekon\.?)|dipl\.?\s?in[zž]\.?|dipl\.?\s?ing\.?|ing\.?|g\.|ga\.|asist\.?|spec\.?",
    "HR": r"dr\.?(?:\s?sc\.?)?|mag\.?(?:\s?\w+\.?)?|prof\.?(?:\s?dr\.?\s?sc\.?)?|dipl\.?\s?(?:ing|iur|oec|\w+)\.?|ing\.?|bacc\.?|univ\.?\s?(?:spec|mag)\.?|doc\.?|struc\.?\s?spec\.?|pred\.?",
    "HU": r"dr\.?|Dr\.|id\.|ifj\.|[öÖ]zv\.|prof\.?|PhD|ügyv\.?|ifj|ifj\.\s?dr\.",
    "RO": r"dr\.?|ing\.?|av\.?|prof\.?(?:\s+univ\.?)?|conf\.?|lect\.?|arh\.?|asist\.?|Dna\.?|Dl\.?|Dr\.|dr\.|c\.p\.",
    "MX": r"Lic\.?|Ing\.?|Dr\.?|Dra\.?|Arq\.?|Mtro\.?|Mtra\.?|Sr\.?|Sra\.?|Srita\.?|C\.P\.?|L\.C\.P\.?|C\.P\.C\.?|Q\.F\.B\.?|M\.V\.Z\.?|Prof\.?|Profa\.?|Cap\.?|Gral\.?",
    "AU": r"Dr\.?|Mr\.?|Mrs\.?|Ms\.?|Miss|Prof\.?|A/Prof\.?|Assoc\.?\s+Prof\.?|Hon\.?|Rev\.?|Fr\.?|Sr\.?|Pastor|Judge|Justice|Capt\.?|Cr\.?|Councillor|Sir|Dame",
}
CREDS = r"(?:MBA|PhD|Ph\.D\.|CSc\.?|DrSc\.?|DiS\.?|MBA|MSc|BSc|LL\.M\.?|MD|CPA|FCA|OAM|AM|AO|QC|SC|JP|FRACP|FRACS|FRANZCOG|MBBS|BDS|B\.?Sc|M\.?Sc|CFP|Jr\.?|Sr\.?|PMP|CCSP|MPH|FRACGP|Esq\.?|CA|ACCA|ing\.?|inż\.|M\.?D\.?|DDS|Dipl\.?\s?Ing\.?|DrPH|CPA)"
ROLE = re.compile(
    r"\b(?:administrador[a]?|gerente|abogad[oa]|notari[oa]|arquitect[oa]|ingenier[oa]|gestor[a]?|titular|propietari[oa]|resp(?:onsable)?|amministratore|titolare|avvocato|avvocata|dottore|dottoressa|commercialista|architetto|ingegnere|notaio|geometra|consulente|responsabile|direttore|direttrice|diretor[a]?|advogad[oa]|s[óo]cio|s[óo]cia|engenheir[oa]|solicitador[a]?|contabilista|t[ée]cnic[oa]|councillor|mayor|headteacher|solicitor|therapist|surgeon|vet|prezes|dyrektor|kierownik|w[łl]a[sś]ciciel|adwokat|radca|lekarz|specjalista|doradca|zast[eę]pca|ksi[eę]gow|pe[łl]nomocnik|za[łl]o[zż]yciel|mened[zż]er|handlowiec|"
    r"jednatel|[řr]editel|majitel|vedouc[ií]|lék[aá][řr]|advok[aá]t|obchodn[ií]|[uú][čc]etn[ií]|jednate[ľl]|riadite[ľl]|vedúci|majite[ľl]|lek[aá]r|"
    r"direktor|direktorica|vodja|lastnik|odvetnik|zdravnik|svetovalec|zaposlen|ravnatelj|vlasnik|voditelj|odvjetnik|lije[cč]nik|savjetnik|"
    r"igazgat[oó]|[uü]gyvezet[oő]|tulajdonos|vezet[oő]|[uü]gyv[eé]d|orvos|tan[aá]csad[oó]|[uü]gyf[eé]lszolg|munkat[aá]rs|"
    r"administrator|director|manager|proprietar|avocat|medic|consultant|[sş]ef|responsabil|contabil|fondator|"
    r"gerente|director[a]?|abogad[oa]|propietari[oa]|socio|m[eé]dic[oa]|asesor|contador|encargad[oa]|gerencia|fundador|"
    r"principal|partner|solicitor|barrister|owner|dentist|physician|practice manager|ceo|cfo|coo|managing|founder|agent|broker|lawyer|doctor|accountant|surgeon|nurse|"
    r"president|vice|chief|officer|coordinator|specialist|consultant|supervisor|secretary|treasurer|chair|sales|team leader)\b", re.I)
PARTS = r"(?:van|von|de|del|della|da|di|du|des|der|den|la|le|los|las|y|e|bin|al|el|ter|ten|zu|af|z|ze|ap|mac|mc|st\.?)"
CAP = rf"(?:[{UP}]{L}+(?:[-'’][{UP}]?{L}+)*\.?|[{UP}]\.)"
STOP = set("""strona główna kontakt o nas firma oferta cennik usługi aktualności polityka prywatności regulamin home about contact us news blog login search menu services
úvod kontakty o nás služby ceník novinky domů naše tým kariéra obchodní podmínky ochrana osobních údajov podmienky tím nové sluzby
domov kontaktirajte nas storitve novice zasebnosti pogoji kariera naslovnica kontakti usluge vijesti uvjeti privatnosti naša
kezdőlap kapcsolat rólunk szolgáltatások hírek adatvédelem akciók termékek csapat vezetés irodák
acasă despre noi servicii noutăți confidențialitate termeni condiții echipa echipă produse
inicio nosotros contacto servicios noticias privacidad aviso legal productos nuestro equipo ubicación sucursales
read more our team meet staff careers join shop cart privacy policy terms conditions click here sign register faq gallery resources events locations hours
inicio servicios noticias privacidad cookies aviso legal quienes somos chi siamo azienda servizi notizie sedi pagina página produtos serviços empresa contactos equipa contatti home ireland england scotland wales jersey guernsey malta portugal españa italia andorra marino vaticano monday tuesday wednesday thursday friday saturday sunday australia mexico polska česká slovensko slovenija hrvatska magyarország românia méxico""".split())


def person_ok(line, cc):
    if len(line) > 70 or re.search(r"\d|@|https?:|www\.|[/|]", line):
        return False
    words = line.replace(",", " ").split()
    if not 2 <= len(words) <= 7:
        return False
    low = [w.lower().strip(".,") for w in words]
    if any(w in STOP for w in low):
        return False
    title_re = re.compile(rf"^(?:{TITLES[cc]})$", re.I)
    caps = 0
    for w in words:
        if title_re.match(w) or re.match(rf"^{CREDS}$", w.strip(",")) or re.match(rf"^{PARTS}$", w, re.I):
            continue
        if re.match(rf"^{CAP}$", w.strip(",")):
            caps += 1
        else:
            return False
    return caps >= 2


def has_title(line, cc):
    return bool(re.match(rf"^(?:{TITLES[cc]})\s", line.strip(), re.I)) or bool(re.search(rf",\s*{CREDS}\.?\s*$", line)) or bool(re.search(rf"\s(?:{CREDS})\.?\s*$", line)) and False


FORMS = {
    "GB": r"Ltd\.?|Limited|plc|LLP|L\.?L\.?P\.?|LP|CIC|Limited Partnership|Community Interest Company|Co\.?\s+Ltd\.?|Trust|Foundation|Charity",
    "JE": r"Ltd\.?|Limited|plc|LLP|LP|Limited Partnership|Trust|Foundation|Association",
    "GG": r"Ltd\.?|Limited|plc|LLP|LP|Limited Partnership|Trust|Foundation|Association",
    "IM": r"Ltd\.?|Limited|plc|LLP|LP|Limited Partnership|Trust|Foundation|Association",
    "IE": r"Ltd\.?|Limited|DAC|CLG|UC|plc|Unlimited Company|Teoranta|Teo\.?|Designated Activity Company|Company Limited by Guarantee|LLP|Trust|Foundation",
    "ES": r"S\.?\s?L\.?\s?U?\.?|S\.?\s?A\.?\s?U?\.?|S\.?\s?L\.?\s?L\.?|S\.?\s?Coop\.?|S\.?\s?C\.?\s?P\.?|C\.?\s?B\.?|S\.?\s?A\.?\s?L\.?|S\.?\s?Com\.?|Sociedad\s+Limitada|Sociedad\s+An[oó]nima|Fundaci[oó]n|Asociaci[oó]n",
    "PT": r"Lda\.?|Limitada|Unipessoal|S\.?\s?A\.?|S\.?\s?G\.?\s?P\.?\s?S\.?|E\.?\s?I\.?\s?R\.?\s?L\.?|Cooperativa|CRL|Associa[cç][aã]o|Funda[cç][aã]o|Sociedade\s+An[oó]nima",
    "IT": r"S\.?\s?r\.?\s?l\.?\s?s?\.?|S\.?\s?p\.?\s?A\.?|S\.?\s?n\.?\s?c\.?|S\.?\s?a\.?\s?s\.?|S\.?\s?s\.?|S\.?\s?c\.?\s?a\.?\s?r\.?\s?l\.?|Soc\.?\s?Coop\.?|Societ[àa]|Fondazione|Associazione|Cooperativa|Onlus|ETS|APS|ASD|S\.?\s?r\.?\s?l\.?\s?unipersonale",
    "MT": r"Ltd\.?|Limited|plc|LLP|Co\.?\s+Ltd|Foundation|Association|Trust",
    "SM": r"S\.?\s?r\.?\s?l\.?|S\.?\s?p\.?\s?A\.?|S\.?\s?A\.?|S\.?\s?n\.?\s?c\.?|S\.?\s?a\.?\s?s\.?|Societ[àa]|Fondazione|Associazione",
    "VA": r"Fondazione|Associazione|Governatorato|Congregazione|Pontificio|Pontificia|Societ[àa]",
    "AD": r"S\.?\s?A\.?\s?U?\.?|S\.?\s?L\.?\s?U?\.?|S\.?\s?L\.?\s?P\.?|S\.?\s?C\.?|Societat|Fundaci[oó]|Associaci[oó]",
    "PL": r"sp\.?\s?z\s?o\.?\s?o\.?(?:\s+sp\.?\s?(?:k|j|p)\.?)?|S\.?A\.?|sp\.?\s?j\.?|sp\.?\s?k\.?|sp\.?\s?k\.?\s?a\.?|sp[óo][łl]ka\s+(?:z\s+ograniczon[aą]|akcyjna|jawna|komandytowa)[\w\s]*|P\.?P\.?H\.?U\.?|P\.?H\.?U\.?|Fundacja|Stowarzyszenie",
    "CZ": r"s\.?\s?r\.?\s?o\.?|a\.?\s?s\.?|spol\.?\s+s\s+r\.?\s?o\.?|v\.?\s?o\.?\s?s\.?|k\.?\s?s\.?|z\.?\s?s\.?|o\.?\s?p\.?\s?s\.?|z\.?\s?ú\.?|s\.?\s?p\.?",
    "SK": r"s\.?\s?r\.?\s?o\.?|a\.?\s?s\.?|spol\.?\s+s\s+r\.?\s?o\.?|v\.?\s?o\.?\s?s\.?|k\.?\s?s\.?|n\.?\s?o\.?|o\.?\s?z\.?",
    "SI": r"d\.?\s?o\.?\s?o\.?|d\.?\s?d\.?|d\.?\s?n\.?\s?o\.?|k\.?\s?d\.?|s\.?\s?p\.?|z\.?\s?o\.?\s?o\.?",
    "HR": r"d\.?\s?o\.?\s?o\.?|j\.?\s?d\.?\s?o\.?\s?o\.?|d\.?\s?d\.?|obrt|k\.?\s?d\.?|j\.?\s?t\.?\s?d\.?|d\.?\s?n\.?\s?o\.?",
    "HU": r"Kft\.?|Zrt\.?|Bt\.?|Nyrt\.?|Kkt\.?|Kht\.?|Egyesület|Alapítvány|e\.?\s?v\.?|e\.?\s?c\.?|Rt\.?",
    "RO": r"S\.?\s?R\.?\s?L\.?(?:-D)?|S\.?\s?A\.?|S\.?\s?C\.?\s?A\.?|S\.?\s?N\.?\s?C\.?|P\.?\s?F\.?\s?A\.?|I\.?\s?I\.?|S\.?\s?C\.?|Asocia[tț]ia|Funda[tț]ia",
    "MX": r"S\.?\s?A\.?\s+de\s+C\.?\s?V\.?|S\.?\s+de\s+R\.?\s?L\.?(?:\s+de\s+C\.?\s?V\.?)?|S\.?\s?A\.?\s?P\.?\s?I\.?(?:\s+de\s+C\.?\s?V\.?)?|S\.?\s?A\.?|A\.?\s?C\.?|S\.?\s?C\.?|S\.?\s?A\.?\s?B\.?(?:\s+de\s+C\.?\s?V\.?)?|S\.?\s?A\.?\s?S\.?|S\.?\s?R\.?\s?L\.?",
    "AU": r"Pty\.?\s+Ltd\.?|Pty\.?\s+Limited|Pty\.?|Ltd\.?|Limited|Inc\.?|Incorporated|Trust|Co-operative|Cooperative|Foundation|Association|Partnership|Group|Holdings|Services|Pty",
}
LEGAL = re.compile(rf"(?:^|[\s,])(?:{FORMS[cc_code]})(?=$|[\s,.;)])", re.I)
COPY = re.compile(r"(?:©|\(c\)|&copy;|Copyright|Prawa autorskie|Autorsk[eé] pr[aá]va|Všechna práva|Szerzői jog|Drepturi de autor|Derechos reservados|Todos los derechos|Todos os direitos|Tutti i diritti|Diritti riservati)\s*(?:\d{4}(?:\s*[-–]\s*\d{4})?)?[\s,:.\-–]*(.+)", re.I)
REGISTERED = re.compile(rf"(?:Titular|Raz[oó]n social|Denominaci[oó]n social|Denomina[cç][aã]o social|Ragione sociale|Denominazione sociale|Company name|Registered name|Registered company|Registered in|Operator|Wydawca|Prowadz[aą]cy|Sprzedawca|Firma|Obchodn[ií] firma|Provozovatel|Prevádzkovate[ľl]|Podjetje|Naziv|Tvrtka|Szolgáltató|Üzemeltető|Cégnév|Denumire|Societatea|Raz[oó]n social|Nombre|Operated by|Trading as|ABN|Registered)\s*[:\-–]\s*(.+)", re.I)


def clean_company(s):
    s = re.sub(r"\b(?:all rights reserved|wszelkie prawa zastrze[zż]one|všechna práva vyhrazena|všetky práva vyhradené|sva prava pridržana|minden jog fenntartva|toate drepturile rezervate|todos los derechos reservados)\b.*", "", s, flags=re.I)
    s = re.split(r"\s+[|·•–—]\s+|\s{2,}|\.\s+[A-ZŁČŠŽ]", s)[0]
    s = re.sub(r"https?://\S+|www\.\S+|\S+@\S+", "", s)
    return s.strip(" ,;.|-–•").strip()


person_items, company_items = [], []
for p in pages:
    L_ = p["lines"]
    french = False
    for k, ln in enumerate(L_):
        if k > 600:
            break
        if person_ok(ln, cc_code):
            near = " ".join(L_[max(0, k - 2):k] + L_[k + 1:k + 3])
            titled = has_title(ln, cc_code)
            role_ctx = bool(ROLE.search(near)) and len(near) < 400
            if titled or role_ctx:
                person_items.append({"text": ln, "host": p["host"], "warc": p["warc"], "strata": ["titled" if titled else "plain"], "src": "text"})
        m = COPY.search(ln)
        if m and len(ln) < 200:
            c = clean_company(m.group(1))
            if 3 <= len(c) <= 80 and len(c.split()) <= 9 and not re.search(r"\d{4,}", c):
                company_items.append({"text": c, "host": p["host"], "warc": p["warc"], "strata": ["legal" if LEGAL.search(c) else "bare"], "src": "copyright"})
        m = REGISTERED.search(ln)
        if m and len(ln) < 200:
            c = clean_company(m.group(1))
            if 3 <= len(c) <= 80 and len(c.split()) <= 9 and not re.search(r"\d{5,}", c):
                company_items.append({"text": c, "host": p["host"], "warc": p["warc"], "strata": ["legal" if LEGAL.search(c) else "bare"], "src": "registered"})
        if LEGAL.search(ln) and 6 <= len(ln) <= 70 and len(ln.split()) <= 8 and not re.search(r"\d{4,}|@|:", ln) and re.match(rf"^[{UP}0-9]", ln):
            company_items.append({"text": ln.strip(" ,;."), "host": p["host"], "warc": p["warc"], "strata": ["legal"], "src": "legal-line"})
    for o in p["ld"]:
        c = o["name"].strip()
        if "person" in o["type"].lower():
            if 3 <= len(c) <= 60 and person_ok(c, cc_code):
                person_items.append({"text": c, "host": p["host"], "warc": p["warc"], "strata": ["plain"], "src": "ldjson"})
        elif 3 <= len(c) <= 80 and len(c.split()) <= 9:
            company_items.append({"text": c, "host": p["host"], "warc": p["warc"], "strata": ["legal" if LEGAL.search(c) else "bare"], "src": "ldjson"})
    sn = p["meta"].get("og:site_name")
    if sn and 3 <= len(sn) <= 80 and len(sn.split()) <= 9:
        company_items.append({"text": sn.strip(), "host": p["host"], "warc": p["warc"], "strata": ["legal" if LEGAL.search(sn) else "bare"], "src": "site_name"})

for kind, items, n in (("person", person_items, n_person), ("company", company_items, n_company)):
    counts = defaultdict(int)
    for it in items:
        for s in it["strata"]:
            counts[s] += 1
    ch = stratified(items, n, rarest(counts), 2 if kind == "person" else 1)
    stats[kind] = (len(items), len(ch), dict(counts))
    with open(os.path.join(out, f"draft-name-{cc_code}.jsonl"), "a" if kind == "company" else "w", encoding="utf-8") as f:
        for i, it in enumerate(ch):
            f.write(json.dumps({"id": f"name-{cc_code}-{kind[0]}{i:03d}", "country": cc_code, "lang": LANG[cc_code], "kind_hint": kind, **it}, ensure_ascii=False) + "\n")

print(cc_code, "pages", len(pages))
for k, v in stats.items():
    print(" ", k, "candidates", v[0], "drafted", v[1], v[2])
