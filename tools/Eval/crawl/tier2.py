"""Per-country tables shared by the Tier 2 evaluation-set selection scripts: host TLDs, postcode patterns, street and unit words.

Used by select-pages-tier2.py and extract-candidates-tier2.py. Nothing here reads a page; it is configuration only.
Postcode shapes follow the national post's published format (Royal Mail, An Post Eircode, Correos, CTT, Poste Italiane, MaltaPost,
the Crown dependencies' posts) as recorded in docs/address-rules-tier2-b.md.
"""
import re

# country -> host TLDs (the last label of the host name)
TLDS = {"GB": ("uk",), "JE": ("je",), "GG": ("gg",), "IM": ("im",), "IE": ("ie",), "ES": ("es",), "PT": ("pt",), "IT": ("it",),
        "MT": ("mt",), "SM": ("sm",), "VA": ("va",), "AD": ("ad",)}
TLD_TO_COUNTRY = {t: c for c, ts in TLDS.items() for t in ts}

_UKPC = r"[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}"
# anchor = the pattern that makes a line the last line of an address; "first" countries write the postcode before the locality
POSTCODE = {
    "GB": re.compile(r"\b(?!(?:JE|GY|IM)\d)" + _UKPC + r"\b"),
    "JE": re.compile(r"\bJE\d[A-Z\d]?\s?\d[A-Z]{2}\b"),
    "GG": re.compile(r"\bGY\d[A-Z\d]?\s?\d[A-Z]{2}\b"),
    "IM": re.compile(r"\bIM\d[A-Z\d]?\s?\d[A-Z]{2}\b"),
    "IE": re.compile(r"\b(?:D6W|[ACDEFHKNPRTVWXY]\d{2})\s?[0-9ACDEFHKNPRTVWXY]{4}\b|\b(?:Co\.?|County)\s+[A-Z][a-zé]+(?:\s[A-Z][a-z]+)?\b|\bDublin\s?\d{1,2}W?\b"),
    "ES": re.compile(r"\b(?:0[1-9]|[1-4]\d|5[0-2])\d{3}(?=[\s,\-–]+[A-ZÁÉÍÓÚÑÇ])"),
    "PT": re.compile(r"\b[1-9]\d{3}[-–]\d{3}\b"),
    "IT": re.compile(r"\b(?:0\d|[1-9]\d)\d{3}(?=[\s,\-–]+[A-ZÀ-Ý])"),
    "MT": re.compile(r"\b[A-Z]{3}\s?\d{2,4}\b"),
    "SM": re.compile(r"\b4789\d\b"),
    "VA": re.compile(r"\b00120\b"),
    "AD": re.compile(r"\bAD\s?[1-7]\d{2}\b"),
}
POST_FIRST = {"ES", "PT", "IT", "SM", "VA", "AD"}

COUNTRY_LINE = re.compile(r"^(?:United Kingdom|UK|U\.K\.|England|Scotland|Wales|Northern Ireland|Great Britain|GB|Ireland|Éire|Eire|Republic of Ireland|España|Spain|Portugal|"
                          r"Italia|Italy|Malta|Jersey|Channel Islands|Guernsey|Isle of Man|San Marino|Repubblica di San Marino|Andorra|Andorre|Vatican City|Città del Vaticano|"
                          r"Vaticano|Citta del Vaticano|Vatican)\s*$", re.I)
COUNTRY_TAIL = re.compile(r"\s*[,\-–]?\s*(?:United Kingdom|UK|U\.K\.|England|Scotland|Wales|Ireland|Éire|Eire|España|Spain|Portugal|Italia|Italy|Malta|Jersey|Channel Islands|"
                          r"Guernsey|Isle of Man|San Marino|Andorra|Andorre|Vatican City|Città del Vaticano|Vaticano)\b", re.I)

STREETWORD = re.compile(
    r"\b(?:Calle|C/|Cl\.|Avda\.?|Avenida|Plaza|Pza\.?|Paseo|Pº|Camino|Carretera|Ctra\.?|Travesía|Ronda|Carrer|Kalea|Rúa|Pol[ií]gono|Urbanizaci[oó]n|"
    r"Via|Viale|V\.le|Piazza|P\.zza|Corso|C\.so|Largo|Vicolo|Strada|Lungomare|Località|Loc\.|Frazione|Fraz\.|Contrada|Salita|Borgo|"
    r"Rua|Av\.|Travessa|Praça|Estrada|Beco|Alameda|Lugar|Quinta|Largo|Urb\.|Zona Industrial|Parque|"
    r"Street|St\.?|Road|Rd\.?|Avenue|Ave\.?|Lane|Ln|Drive|Dr\.?|Close|Court|Way|Place|Square|Terrace|Hill|Park|Row|Quay|Mall|Gardens|Crescent|Grove|Mews|Parade|Walk|Green|Business Park|Industrial Estate|House|"
    r"Triq|Pjazza|Misraħ|Sqaq|Telgħa|Wied|Rotunda|Bypass|Highway|Main Street|Route|Rue|Chemin|Carretera|Camí|Passeig|Plaça|Avinguda|Gran Vía)\b", re.I)
BOX = re.compile(r"\b(?:P\.?\s?O\.?\s*Box|Post Office Box|Apartado(?: de Correos)?|Apdo\.?|Casella Postale|C\.?P\.?\s+\d|Caixa Postal|Apartado|Box\s+\d|BP\s?\d|PO\s?Box|Private Bag|Poste restante)\b", re.I)
UNIT = re.compile(r"\b(?:suite|ste\.?|apt\.?|apartment|flat|unit|#\s?\d|floor|fl\.?|bldg|building|room|rm\.?|block|piso|planta|escalera|puerta|bajo|bajos|entresuelo|local|oficina|"
                  r"piano|interno|int\.|scala|sc\.|andar|fra[cç][aã]o|esq\.?|dto\.?|r/c|loja|sala|\d+\s?[ºª°]|\d+(?:st|nd|rd|th)\s+floor)\b", re.I)
RURAL = re.compile(r"\b(?:Pol[ií]gono|Pol\.\s?Ind|Urbanizaci[oó]n|Urb\.|Parcela|Paraje|Finca|Località|Loc\.|Frazione|Fraz\.|Contrada|C\.da|Quinta|Aldeia|Lugar|Estrada|Townland|Industrial Estate|Business Park|Co\.|Farm|Cottage|Lodge|Hall|Manor)\b", re.I)
OTHERLANG = re.compile(r"\b(?:Carrer|Passeig|Plaça|Avinguda|Kalea|Etorbidea|Rúa|Praza|Triq|Pjazza|Misraħ|Sqaq|Bóthar|Sráid|Baile|Straße|Strasse|Platz|Via dei|Rue|Chemin|Boulevard)\b", re.I)
