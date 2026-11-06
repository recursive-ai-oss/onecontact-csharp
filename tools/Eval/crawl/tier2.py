"""Per-country settings for the Tier 2 evaluation-set selection: host TLDs, postal-code shapes, page paths and legal forms.

Shared by select-pages-t2.py and extract-candidates-t2.py. The country of an item is the country of its host's TLD; the postal code
shape is only a filter for "this line is an address of that country" (the labels come from adjudication, not from these patterns).
"""
import re

# TLD -> (country, territory tag). The French overseas departments have their own TLDs; they are part of the FR set.
TLD = {"fr": ("FR", None), "gp": ("FR", "GP"), "mq": ("FR", "MQ"), "gf": ("FR", "GF"), "re": ("FR", "RE"), "yt": ("FR", "YT"),
       "mc": ("MC", None), "be": ("BE", None), "nl": ("NL", None), "lu": ("LU", None), "de": ("DE", None), "at": ("AT", None),
       "ch": ("CH", None), "li": ("LI", None)}

CITY = r"[A-ZÀ-ÖØ-Þ][A-Za-zÀ-ÖØ-öø-ÿ'’\-\.]*"
POSTCODE = {
    "FR": r"(?:F[- ]?)?(?:(?:0[1-9]|[1-8]\d|9[0-5])\d{3}|97[1-6]\d{2}|98[0-9]\d{2})",
    "MC": r"(?:MC[- ]?)?980\d{2}",
    "BE": r"(?:B[- ]?)?[1-9]\d{3}",
    "NL": r"[1-9]\d{3}\s?[A-Z]{2}",
    "LU": r"(?:L[- ]?)?[1-9]\d{3}",
    "DE": r"(?:D[- ]?)?\d{5}",
    "AT": r"(?:A[- ]?)?[1-9]\d{3}",
    "CH": r"(?:CH[- ]?)?[1-9]\d{3}",
    "LI": r"(?:FL[- ]?)?94(?:8[5-9]|9\d)",
}
# a postal code followed by a place name (all nine countries write the code before the place)
POSTCODE_RE = {c: re.compile(rf"(?<![\w/\-–.])(?:{p})(?![\d])\s+{CITY}") for c, p in POSTCODE.items()}

PATH = re.compile(
    r"/(contact|contacts|contact-us|contactus|kontakt|kontaktformular|impressum|imprint|legal-notice|mentions-legales|mentions-l%C3%A9gales|mentionslegales|"
    r"about|about-us|ueber-uns|%C3%BCber-uns|uber-uns|team|unser-team|mitarbeiter|ansprechpartner|standort|standorte|anfahrt|oeffnungszeiten|"
    r"nous-contacter|contactez-nous|nous-joindre|a-propos|qui-sommes-nous|equipe|notre-equipe|coordonnees|acces|plan-d-acces|agences?|magasins?|"
    r"over-ons|contactgegevens|colofon|locatie|locaties|vestigingen|bereikbaarheid|route|adres|datenschutz|privacy|agb|kanzlei|praxis|"
    r"cabinet|etude|notaire|notaris|avocats?|anwaelte|aerzte|chi-siamo|contatti|sede)(/|\.|$|-)", re.I)
ROOTS = ("/", "/index.html", "/index.php", "/index.htm", "/home", "/fr", "/de", "/nl", "/en", "/fr/", "/de/", "/nl/")

LABEL = re.compile(
    r"^(?:adresse|anschrift|postanschrift|besucheradresse|besuchsadresse|unsere adresse|firmenanschrift|sitz|firmensitz|standort|büro|buero|"
    r"notre adresse|adresse postale|siège social|siège|bureau|nous trouver|où nous trouver|"
    r"adres|bezoekadres|postadres|vestiging|kantoor|onze adres|maatschappelijke zetel|zetel|hoofdkantoor|"
    r"address|location|visit us)\s*[:\-–]\s*", re.I)
COUNTRY_LINE = re.compile(r"^(?:France|Monaco|Belgique|Belgi[eë]|Belgien|Belgium|Nederland|Niederlande|Netherlands|Luxembourg|Luxemburg|Deutschland|"
                          r"Germany|Allemagne|[ÖO]sterreich|Austria|Autriche|Schweiz|Suisse|Svizzera|Switzerland|Liechtenstein|"
                          r"(?:FR|MC|BE|NL|LU|DE|AT|CH|LI))\s*$", re.I)
BOX = re.compile(r"\b(?:Postfach|Postbus|PF\s?\d|B\.?P\.?\s?\d|Bo[iî]te\s+postale|Case\s+postale|CS\s?\d{4,5}|CEDEX|Cedex|TSA\s?\d|Postlagernd|Antwoordnummer|Boîte|Bus\s?\d)\b", re.I)
STREETNUM = re.compile(r"\d{1,4}\s?[A-Za-z]?(?:\s?[-–/]\s?\d{1,4}\s?[A-Za-z]?)?\b")
UNIT = re.compile(r"\b(?:b[âa]t(?:iment)?\.?|étage|etage|escalier|appt?\.?|appartement|résidence|residence|zone|bte|boîte|bus|OG|EG|UG|Stock|Haus|Gebäude|Geb\.|"
                  r"Etage|Top|Tür|Whg|Wohnung|Block|App\.?|verdieping|verd\.?|kamer|unit|suite|c/o|z\.H\.|zH|zHd|z\. ?Hd)\b", re.I)
RURAL = re.compile(r"\b(?:lieu[- ]?dit|ferme|hameau|les |le |la |Hof|Weiler|Alp|Gehöft|Ortsteil|OT |lieu dit)\b|\bBP\b", re.I)

# Legal forms that appear in the nine countries' company names (surface forms only; adjudication decides the labels).
LEGAL = re.compile(
    r"\b(?:GmbH(?:\s*&\s*Co\.?\s*KG)?|gGmbH|UG(?:\s*\(haftungsbeschränkt\))?|AG|KG|OHG|GbR|e\.?\s?K\.?|e\.?\s?V\.?|eG|SE|Ges\.?m\.?b\.?H\.?|GesmbH|KGaA|mbH|"
    r"S\.?A\.?R\.?L\.?|S[àa]rl|SARL|SAS|SASU|S\.A\.S\.?|EURL|SCI|SNC|SCOP|SCM|SELARL|SELAS|S\.A\.|SA|SCA|GIE|"
    r"B\.?V\.?|N\.?V\.?|V\.?O\.?F\.?|C\.?V\.?|Stichting|VZW|ASBL|AISBL|SRL|BVBA|SPRL|CVBA|SCRL|Comm\.?V|"
    r"S[àa]rl-S|Sàrl|S\.à r\.l\.|S\.à\s?r\.l\.|Genossenschaft|Stiftung|Verein|Ltd|Limited|Inc|LLC|S\.?p\.?A\.?|Srl)(?=$|[\s,.;)])", re.I)
LANG3 = {"deu": "de", "fra": "fr", "nld": "nl", "ita": "it", "ltz": "lb", "eng": "en", "gsw": "de"}


def host_info(host):
    tld = host.rsplit(".", 1)[-1]
    return TLD.get(tld)


def page_lang(languages, country):
    """First detected language of the page when it is one the name rules know; otherwise empty."""
    for l in (languages or "").split(","):
        if l[:3] in LANG3:
            return LANG3[l[:3]]
    return ""
