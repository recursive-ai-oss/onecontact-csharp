#!/usr/bin/env python3
"""A second pre-label for the Tier 2 group B address drafts (GB, JE, GG, IM, IE, ES, PT, IT, MT, SM, VA, AD).

usage: prelabel.py <draft-address-CC.jsonl> <pred-out.jsonl>

The rule parser leaves many of these layouts unread (British four-line blocks with a locality line, Irish Eircode lines, a postcode
without a locality), which would leave the reviewer typing every label. This script splits an item into line and comma segments and
applies plain layout conventions: the postcode segment, the country segment, the street segment (a house number plus a road, or a road
word), a unit segment, leading recipient segments, and the locality as the segment before the postcode (British Isles) or after it
(Spain, Portugal, Italy and the small states). It only drafts: every item is read and corrected by the reviewer, and the rules'
agreement is recomputed against the rule parser's own output afterwards (agree.py). Output is in the scorer's prediction format
with coarse fields only.
"""
import json
import re
import sys

POST = {
    "GB": r"[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}", "JE": r"JE\d[A-Z\d]?\s?\d[A-Z]{2}", "GG": r"GY\d[A-Z\d]?\s?\d[A-Z]{2}", "IM": r"IM\d[A-Z\d]?\s?\d[A-Z]{2}",
    "MT": r"[A-Z]{3}\s?\d{2,4}", "IE": r"(?:D6W|[ACDEFHKNPRTVWXY]\d{2})\s?[0-9ACDEFHKNPRTVWXY]{4}",
    "ES": r"(?:0[1-9]|[1-4]\d|5[0-2])\d{3}", "PT": r"[1-9]\d{3}\s?-\s?\d{3}", "IT": r"(?:0\d|[1-9]\d)\d{3}", "SM": r"4789\d", "VA": r"00120",
    "AD": r"AD\s?[1-7]\d{2}",
}
POST_FIRST = {"ES", "PT", "IT", "SM", "VA", "AD"}
COUNTRY = re.compile(r"^(?:United Kingdom|UK|U\.K\.|England|Scotland|Wales|Northern Ireland|Great Britain|Jersey|Channel Islands|Guernsey|Isle of Man|Ireland|Éire|Eire|"
                     r"Republic of Ireland|IE|España|Spain|Portugal|Italia|Italy|Malta|San Marino|Repubblica di San Marino|Andorra|Andorre|Citt[àa] del Vaticano|Vatican City|Vaticano)\.?$", re.I)
COUNTY = re.compile(r"^(?:Co\.?\s+[A-Z][\w ]+|Co\.[A-Z][\w ]+|County [A-Z][\w ]+|Kent|Surrey|Essex|Sussex|East Sussex|West Sussex|Hampshire|Berkshire|Devon|Cornwall|Dorset|Norfolk|Suffolk|Lancashire|Yorkshire|"
                    r"North Yorkshire|West Yorkshire|South Yorkshire|Cheshire|Derbyshire|Nottinghamshire|Lincolnshire|Leicestershire|Staffordshire|Warwickshire|Worcestershire|Oxfordshire|"
                    r"Buckinghamshire|Hertfordshire|Bedfordshire|Cambridgeshire|Northamptonshire|Gloucestershire|Somerset|Wiltshire|Cumbria|Durham|County Durham|Northumberland|Shropshire|Herefordshire|"
                    r"Greater London|Greater Manchester|Merseyside|Tyne and Wear|West Midlands|Middlesex|Fife|Lanarkshire|Ayrshire|Aberdeenshire|Perthshire|Midlothian|East Lothian|West Lothian|"
                    r"Gwynedd|Powys|Dyfed|Clwyd|Glamorgan|South Glamorgan|West Glamorgan|Mid Glamorgan|Gwent|Antrim|Down|Armagh|Tyrone|Fermanagh|Londonderry|Derry|Dublin|Cork|Galway|Kerry|Mayo|"
                    r"Limerick|Wicklow|Meath|Louth|Kildare|Wexford|Clare|Donegal|Sligo|Tipperary|Waterford|Kilkenny|Carlow|Cavan|Laois|Offaly|Westmeath|Longford|Leitrim|Monaghan|Roscommon)$", re.I)
STREETWORD = re.compile(r"\b(?:Street|St|Road|Rd|Avenue|Ave|Lane|Ln|Drive|Dr|Close|Court|Way|Place|Square|Terrace|Hill|Park|Row|Quay|Mall|Gardens|Crescent|Grove|Mews|Parade|Walk|Green|Gate|Bridge|"
                        r"Wharf|Rise|View|Broadway|Circus|Yard|Cottages|Estate|Centre|Center|Triq|Pjazza|Misraħ|Sqaq|Telgħa|Wied|"
                        r"Calle|C/|Avda|Avenida|Plaza|Paseo|Camino|Carretera|Ctra|Travesía|Ronda|Carrer|Rúa|Polígono|Urbanización|Via|Viale|Piazza|Corso|Largo|Vicolo|Strada|Località|Frazione|Contrada|"
                        r"Rua|Av|Travessa|Praça|Estrada|Beco|Alameda|Lugar|Quinta|Largo|Zona Industrial|Bóthar)\b\.?", re.I)
PRE_ROAD = re.compile(r"^(?:Calle|C/|C\.|Cl\.|Avda\.?|Avenida|Av\.|Plaza|Pza\.?|Paseo|Pº|Camino|Carretera|Ctra\.?|Travesía|Ronda|Carrer|Rúa|Polígono|Pol\.|Urbanización|Urb\.|Via|Viale|V\.le|Piazza|P\.zza|Corso|C\.so|"
                      r"Largo|Vicolo|Strada|Lungomare|Località|Loc\.|Frazione|Fraz\.|Contrada|Salita|Borgo|Rua|R\.|Travessa|Tv\.|Praça|Estrada|Beco|Alameda|Lugar|Quinta|Zona Industrial|Parque)(?![\w])", re.I)
UNIT = re.compile(r"^(?:unit|suite|ste|flat|apartment|apt|floor|\d+(?:st|nd|rd|th)\s+floor|ground floor|first floor|second floor|room|rm|block|office|level|piso|planta|escalera|puerta|bajo|local|oficina|"
                  r"piano|interno|int\.?|scala|andar|r/c|loja|sala|fração|1º|2º|3º|4º|5º)\b", re.I)
BOX = re.compile(r"\b(?:P\.?\s?O\.?\s*Box|Post Office Box|Apartado(?: de Correos)?|Apdo\.?|Casella Postale|C\.P\.|Caixa Postal|Private Bag)\b", re.I)
NOISE = re.compile(r"@|https?://|www\.|\b\d{1,2}[:.]\d{2}\b|\b\d{1,2}\s?(?:am|pm)\b|\b(?:mon|tue|wed|thu|fri|sat|sun)\b|registration|company no|sat nav|copyright|\b(?:tel|phone|fax|mob|call|email|e-mail|open|hours)\b|^\+?[\d\s()/-]{7,}$", re.I)


def split_segs(text):
    segs = []
    for line in text.split("\n"):
        line = re.sub(r"^(?:direcci[oó]n(?: postal)?|domicilio(?: social| fiscal)?|sede(?: legale| operativa| social)?|indirizzo|morada|endere[cç]o|address)\s*:\s*", "", line.strip(), flags=re.I)
        line = re.sub(r",(?=\d)", ", ", line)
        for s in re.split(r"\s+[|/]\s+|,\s+|;\s+|(?<!\d)\s+[–-]\s+(?=[A-Z])", line):
            s = s.strip(" ,;|.")
            if not s or NOISE.search(s):
                continue
            # a unit designator sharing its segment with the rest: "Unit 9 Mill Farm" -> "Unit 9", "Mill Farm"
            m = re.match(r"^((?:unit|suite|ste|flat|apartment|apt|room|office|level|block)\b\.?\s*\d[\w-]*)\s+(\D.*)$", s, re.I)
            if m:
                segs.extend([m.group(1), m.group(2)])
            else:
                segs.append(s)
    return segs


def draft(text, cc):
    f = {}
    rx = re.compile(r"(?<![\w])" + POST[cc] + r"(?![\w])")
    segs = split_segs(text)
    pc_i = None
    for i in range(len(segs) - 1, -1, -1):
        if NOISE.search(segs[i]):
            continue
        m = rx.search(segs[i])
        if m and not (cc in ("ES", "IT") and not re.match(r"^\s*[-–]?\s*[A-ZÀ-Ý]", segs[i][m.end():] or "A") and False):
            pc_i = i
            break
    rest_after = []
    if pc_i is not None:
        s = segs[pc_i]
        m = rx.search(s)
        f["postcode"] = re.sub(r"\s+", " ", m.group(0)).strip()
        before, after = s[:m.start()].strip(" ,;-–"), s[m.end():].strip(" ,;-–")
        if cc in POST_FIRST:
            if before and not segs[:pc_i]:
                segs[pc_i] = before
            loc = after
            if loc:
                mpar = re.search(r"\(\s*([^()]+?)\s*\)\s*$", loc)
                if mpar and COUNTRY.match(mpar.group(1)):
                    f["country"] = mpar.group(1)
                    loc = loc[:mpar.start()].strip()
                elif mpar and not re.fullmatch(r"[A-Z]{2}", mpar.group(1)):
                    f["state"] = mpar.group(1)
                    loc = loc[:mpar.start()].strip()
                mprov = re.search(r"\(([A-Z]{2})\)\s*$", loc)
                if mprov:
                    f["state"] = mprov.group(1)
                    loc = loc[:mprov.start()].strip()
                if cc == "PT":
                    loc = re.sub(r"^[-–]\s*", "", loc)
                f["city"] = loc
            pre = segs[:pc_i] + ([before] if before else [])
            post = segs[pc_i + 1:]
        else:
            pre = segs[:pc_i]
            post = segs[pc_i + 1:]
            if before:
                if COUNTY.match(before):
                    f["state"] = before
                else:
                    pre.append(before)
            if after:
                post.insert(0, after)
        segs_rest = pre
        tail = post
    else:
        segs_rest, tail = segs, []
    for t in tail:
        if COUNTRY.match(t):
            f["country"] = t
        elif cc in POST_FIRST and "city" not in f and not NOISE.search(t):
            f["city"] = t
    for i in range(len(segs_rest) - 1, -1, -1):
        if COUNTRY.match(segs_rest[i]):
            f["country"] = segs_rest.pop(i)
    if cc not in POST_FIRST:
        # the last remaining segment before the postcode is the post town unless a county is named
        if segs_rest:
            last = segs_rest[-1]
            if COUNTY.match(last) and "state" not in f and (re.match(r"(?i)^(?:Co\.?|County)\b", last) or (len(segs_rest) >= 2 and not STREETWORD.search(segs_rest[-2]) and not re.match(r"^\d", segs_rest[-2]) and not UNIT.match(segs_rest[-2]))):
                f["state"] = segs_rest.pop()
        if segs_rest and not STREETWORD.search(segs_rest[-1]) and not re.match(r"^\d", segs_rest[-1]) or (len(segs_rest) > 1 and not re.match(r"^\d", segs_rest[-1]) and not BOX.search(segs_rest[-1])):
            if segs_rest and "city" not in f and not UNIT.match(segs_rest[-1]) and not BOX.search(segs_rest[-1]):
                f["city"] = segs_rest.pop()
    # street segment: first with a house number and road, a road word or a box
    street = None
    for i, s in enumerate(segs_rest):
        if BOX.search(s):
            f["po_box"] = s
            street = i
            break
    if street is None:
        for i, s in enumerate(segs_rest):
            if UNIT.match(s):
                continue
            if re.match(r"^\d+[A-Za-z]?(?:\s?[-/]\s?\d+[A-Za-z]?)?\s+\S", s) or STREETWORD.search(s) or PRE_ROAD.match(s):
                street = i
                break
    if street is None:
        for i, s_ in enumerate(segs_rest[:-1]):
            if re.match(r"^(?:n.{0,2}\s*)?\d+[A-Za-z]?(?:\s.*)?$", segs_rest[i + 1]) and not re.match(r"^\d", s_) and not UNIT.match(s_):
                street = i
                break
    if street is not None and "po_box" not in f:
        s = segs_rest[street]
        m = re.match(r"^(\d+[A-Za-z]?(?:\s?[-/]\s?\d+[A-Za-z]?)?)\s+(.+)$", s)
        if m and cc not in ("ES", "IT", "PT") or (m and not PRE_ROAD.match(s)):
            f["house_number"], f["road"] = m.group(1), m.group(2)
        else:
            f["road"] = s
            # number: the next segment when it is a bare number, or inline
            m2 = re.match(r"^(.+?)\s+(?:n[ºo°.]*\s*)?(\d+[A-Za-z]?(?:\s?[-/]\s?\d+[A-Za-z]?)?)$", s)
            if m2:
                f["road"], f["house_number"] = m2.group(1).rstrip(" ,"), m2.group(2)
            elif street + 1 < len(segs_rest) and re.match(r"^(?:n.{0,2}\s*)?\d+[A-Za-z]?(?:\s*[-–]\s*\S.*|\s+\S.*)?$", segs_rest[street + 1], re.I):
                mm = re.match(r"^(?:n[^\d\s]{0,2}\s*)?(\d+[A-Za-z]?)(?:\s*[-–]?\s*(.+))?$", segs_rest[street + 1], re.I)
                f["house_number"] = mm.group(1)
                if mm.group(2):
                    f["unit"] = mm.group(2)
                segs_rest.pop(street + 1)
        pre, post = segs_rest[:street], segs_rest[street + 1:]
        if pre and "house_number" not in f and re.match(r"^(?:no\.?\s*)?\d+[A-Za-z]?(?:\s?[-/]\s?\d+[A-Za-z]?)?$", pre[-1], re.I):
            f["house_number"] = re.sub(r"^no\.?\s*", "", pre.pop(), flags=re.I)
        units = [p for p in pre + post if UNIT.match(p)]
        others = [p for p in pre if not UNIT.match(p)]
        if units:
            f["unit"] = " ".join(units)
        if others:
            f["recipient"] = " ".join(others)
    elif street is not None:
        pre = [p for p in segs_rest[:street]]
        if pre:
            f["recipient"] = " ".join(pre)
    return {k: v for k, v in f.items() if v}


if __name__ == "__main__":
    src, out = sys.argv[1], sys.argv[2]
    with open(out, "w", encoding="utf-8") as fo:
        for l in open(src, encoding="utf-8"):
            if not l.strip():
                continue
            it = json.loads(l)
            fo.write(json.dumps({"id": it["id"], "fields": draft(it["text"], it["country"]), "parts": {}}, ensure_ascii=False) + "\n")
