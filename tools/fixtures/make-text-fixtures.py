#!/usr/bin/env python3
"""Writes the synthetic text-extraction fixtures under testdata/extraction/text: HTML pages and plain-text notes. Every person, company, address and number is made up
(the example.* names are reserved for documentation; 555-01xx numbers are reserved for fiction). Run once; the files are committed."""
import os

root = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'testdata', 'extraction', 'text'))
os.makedirs(root + '/pages', exist_ok=True)
os.makedirs(root + '/notes', exist_ok=True)


def cf(addr, key):
    return '%02x' % key + ''.join('%02x' % (b ^ key) for b in addr.encode('utf-8'))


def page(name, body, head='', lang='en-US', raw=None, enc='utf-8'):
    meta = {'utf-8': '<meta charset="utf-8">', 'cp1252': '<meta charset="windows-1252">'}.get(enc, '')
    if raw is None:
        html = '<!DOCTYPE html>\n<html%s>\n<head>%s<title>Fixture</title>%s</head>\n<body>\n%s\n</body>\n</html>\n' % (
            (' lang="%s"' % lang) if lang else '', meta, head, body)
    else:
        html = raw
    data = html.encode(enc)
    if enc == 'utf-16-le':
        data = b'\xff\xfe' + data
    open(root + '/pages/' + name + '.html', 'wb').write(data)


def note(name, text, newline='\n'):
    open(root + '/notes/' + name + '.txt', 'wb').write(text.replace('\n', newline).encode('utf-8'))


VIS = '<p>Contact: <a href="mailto:visible@example.com">visible@example.com</a></p>'
VISP = '<p>Visible phone +1 202 555 0156</p>'

# ---- hidden-text traps: every trap value contains "trap" (emails) or 0199 (phones) and must not appear -------------------------
page('hidden-display-none', '<div style="display:none">Write trap@example.com or +1 202 555 0199</div>\n' + VIS)
page('hidden-visibility', '<div style="visibility:hidden">Write trap@example.com or +1 202 555 0199</div>\n' + VIS)
page('hidden-attribute', '<p hidden>Write trap@example.com or +1 202 555 0199</p>\n' + VIS)
page('hidden-aria-hidden', '<p aria-hidden="true">Write trap@example.com or +1 202 555 0199</p>\n' + VIS)
page('hidden-stylesheet-class', '<div class="gone">Write trap@example.com or +1 202 555 0199</div>\n' + VIS, head='<style>.gone { display: none }</style>')
page('hidden-stylesheet-descendant', '<div class="box"><span>Write trap@example.com or +1 202 555 0199</span></div>\n' + VIS, head='<style>.box span { visibility: hidden }</style>')
page('hidden-offscreen', '<div style="position:absolute;left:-9999px">Write trap@example.com or +1 202 555 0199</div>\n' + VIS)
page('hidden-zero-height', '<div style="height:0;overflow:hidden">Write trap@example.com or +1 202 555 0199</div>\n' + VIS)
page('hidden-opacity-zero', '<div style="opacity:0">Write trap@example.com or +1 202 555 0199</div>\n' + VIS)
page('hidden-font-size-zero', '<span style="font-size:0">Write trap@example.com or +1 202 555 0199</span>\n' + VIS)
page('hidden-text-indent', '<p style="text-indent:-9999px">Write trap@example.com or +1 202 555 0199</p>\n' + VIS)
page('hidden-clip-rect', '<span style="position:absolute;clip:rect(0,0,0,0)">Write trap@example.com or +1 202 555 0199</span>\n' + VIS)
page('hidden-screen-reader-only', '<span style="position:absolute;width:1px;height:1px;overflow:hidden">trap@example.com</span>\n' + VIS)
page('hidden-clip-path', '<div style="clip-path:inset(100%)">Write trap@example.com or +1 202 555 0199</div>\n' + VIS)
page('hidden-script-template-noscript',
     '<script>var a = "trap@example.com";</script><template><p>trap@example.com +1 202 555 0199</p></template><noscript>trap@example.com</noscript>\n' + VIS)
page('hidden-comment-and-title', '<!-- trap@example.com -->\n' + VIS, head='<title>trap@example.com</title>')
page('hidden-ancestor-hides-visible-child', '<div style="display:none"><p style="display:block">trap@example.com</p></div>\n' + VIS)
page('hidden-child-shows-in-hidden-parent', '<div style="visibility:hidden">trap@example.com <span style="visibility:visible">shown@example.com</span></div>')
page('hidden-aria-hidden-ancestor', '<div aria-hidden="true"><p>trap@example.com</p></div>\n' + VIS)

# ---- obfuscation ---------------------------------------------------------------------------------------------------------------
page('obf-at-brackets', '<p>Email: john [at] example [dot] com</p>')
page('obf-at-parentheses', '<p>Email: john (at) example (dot) com</p>')
page('obf-at-braces', '<p>Email: john {at} example {dot} com</p>')
page('obf-at-angle-spaced', '<p>Email: john [ at ] example [ dot ] com</p>')
page('obf-words', '<p>Email john at example dot com for details.</p>')
page('obf-words-uppercase', '<p>JOHN AT EXAMPLE DOT COM</p>')
page('obf-dashes', '<p>john-at-example-dot-com</p>')
page('obf-dot-words-in-local-part', '<p>john dot smith at example dot co dot uk</p>')
page('obf-bracket-dot-only', '<p>john@example[.]com</p>')
page('obf-fullwidth', '<p>john\uff20example\uff0ecom</p>')
page('obf-percent-escapes', '<p>john%40example.com</p>')
page('obf-spaced-at-sign', '<p>john @ example.com</p>')
page('obf-prose-is-not-an-address', '<p>Meet me at example.com or look at the poster at noon.</p>')
page('obf-entities', '<p>jo&#104;n&#64;example&#46;com and jane&commat;example.org</p>')
page('obf-split-by-span', '<p>john<span>@</span>example.com</p>')
page('obf-split-with-hidden-junk', '<p>john<span style="display:none">junk</span>@example.com</p>')
page('obf-zero-width-characters', '<p>jo\u200bhn@exam\u2060ple.com</p>')
page('obf-cloudflare-span', '<p>Mail <span class="__cf_email__" data-cfemail="%s">[email&#160;protected]</span></p>' % cf('cf.person@example.com', 0x4b))
page('obf-cloudflare-link', '<p><a href="/cdn-cgi/l/email-protection#%s">Write to us</a></p>' % cf('cf.link@example.com', 0x7a))
page('obf-cloudflare-hidden', '<p style="display:none"><span data-cfemail="%s">[email&#160;protected]</span></p>\n%s' % (cf('trap@example.com', 0x21), VIS))
page('obf-cloudflare-malformed', '<p><span data-cfemail="zz">[email&#160;protected]</span> <span data-cfemail="4b">[email&#160;protected]</span></p>')
page('obf-reversed-css', '<p><span style="direction:rtl;unicode-bidi:bidi-override">moc.elpmaxe@nhoj</span></p>')
page('obf-reversed-bdo', '<p>Write to <bdo dir="rtl">moc.elpmaxe@enaj</bdo></p>')
page('obf-reversed-not-reversed-without-override', '<p><span style="direction:rtl">moc.elpmaxe@nhoj</span></p>')
page('obf-mailto-entities-and-text', '<p><a href="&#109;&#97;&#105;&#108;&#116;&#111;&#58;mia&#64;example.com">mia [at] example [dot] com</a></p>')

# ---- layouts -------------------------------------------------------------------------------------------------------------------
page('layout-footer-contact',
     '<main><h1>Acme</h1><p>We make anvils.</p></main>\n<footer><h2>Contact</h2><p>Acme Anvils<br>100 Main Street<br>Springfield, IL 62701</p>'
     '<p>Phone: (202) 555-0148</p><p>Email: <a href="mailto:hello@acme.example">hello@acme.example</a></p><p><a href="https://www.acme.example/">www.acme.example</a></p></footer>')
page('layout-contact-page-sections',
     '<main><h1>Contact us</h1><section><h2>Sales</h2><p>sales@acme.example</p><p>Tel: +1 202 555 0144</p></section>'
     '<section><h2>Support</h2><p>support@acme.example</p><p>Tel: +1 202 555 0145</p></section></main>')
page('layout-staff-table',
     '<table><tr><th>Name</th><th>Email</th><th>Phone</th></tr>'
     '<tr><td>Ada Example</td><td>ada@staff.example</td><td>+1 202 555 0101</td></tr>'
     '<tr><td>Bo Sample</td><td>bo@staff.example</td><td>+1 202 555 0102</td></tr>'
     '<tr><td>Cy Placeholder</td><td>+1 202 555 0103</td><td>cy@staff.example</td></tr></table>')
page('layout-staff-cards',
     '<article><h3>Ada Example</h3><p>ada@staff.example</p><p>Tel +1 202 555 0101</p></article>'
     '<article><h3>Bo Sample</h3><p>bo@staff.example</p><p>Tel +1 202 555 0102</p></article>')
page('layout-staff-headings-in-main',
     '<main><h2>Ada Example</h2><p>ada@staff.example</p><p>+1 202 555 0101</p><h2>Bo Sample</h2><p>bo@staff.example</p><p>+1 202 555 0102</p></main>')
page('layout-staff-list-items',
     '<ul><li>Ada Example, ada@staff.example, +1 202 555 0101</li><li>Bo Sample, bo@staff.example, +1 202 555 0102</li></ul>')
page('layout-footer-list-one-field-per-item',
     '<footer><ul><li>Phone: +1 202 555 0150</li><li>Email: team@acme.example</li><li>Web: www.acme.example</li></ul></footer>')
page('layout-landmarks-header-footer',
     '<header><p>Call +1 202 555 0160 or write head@acme.example</p></header><main><p>Welcome.</p></main>'
     '<footer><p>Call +1 202 555 0161 or write foot@acme.example</p></footer>')
page('layout-far-apart-fields',
     '<p>Write to far1@acme.example</p><p>One.</p><p>Two.</p><p>Three.</p><p>Four.</p><p>Five.</p><p>Six.</p><p>Call +1 202 555 0162</p>')
page('layout-two-emails-one-block',
     '<p>info@acme.example<br>Tel +1 202 555 0163<br>jobs@acme.example</p>')
page('layout-div-columns-flex',
     '<div style="display:flex"><div>Tel +1 202 555 0164</div><div>team2@acme.example</div></div>')
page('layout-preformatted-signature', '<pre>Jo Example\nTel: +1 202 555 0165\njo@acme.example</pre>')
page('layout-heading-between', '<footer><p>a@acme.example</p><h4>Elsewhere</h4><p>+1 202 555 0166</p></footer>')

# ---- addresses -----------------------------------------------------------------------------------------------------------------
page('address-us-br-lines', '<p>Acme Anvils<br>100 Main Street<br>Springfield, IL 62701</p>')
page('address-us-div-lines', '<div class="adr"><div>200 Oak Ave</div><div>Portland, OR 97201-1234</div></div>')
page('address-us-single-line', '<p>Visit us at 300 Elm St, Austin, TX 78701 any weekday.</p>')
page('address-us-suite-below', '<p>400 Pine Road<br>Suite 200<br>Denver, CO 80202</p>')
page('address-us-suite-above', '<p>Suite 500<br>500 Cedar Blvd<br>Boston, MA 02108</p>')
page('address-us-suite-same-line', '<p>600 Birch Way, Apt 4B, Seattle, WA 98101</p>')
page('address-us-po-box', '<p>PO Box 1234<br>Madison, WI 53703</p>')
page('address-ca', '<p>10 King Street West<br>Toronto, ON M5V 2T6</p>')
page('address-ca-single-line', '<p>20 Rue Principale, Montreal, QC H2X 1Y4</p>', lang='fr-CA')
page('address-no-region', '<p>100 Main Street<br>Springfield, IL 62701</p>', lang='')
page('address-lang-without-region', '<p>100 Main Street<br>Springfield, IL 62701</p>', lang='en')
page('address-not-an-address', '<p>In 2024 we served 12345 customers in IL and 62701 more online.</p><p>Order OR 97201 is ready.</p>')
page('address-no-delivery-line', '<p>Springfield, IL 62701</p>')
page('address-in-address-element', '<address>Acme Anvils<br>100 Main Street<br>Springfield, IL 62701</address>')
page('address-entities', '<p>Smith &amp; Sons<br>12 Oak&nbsp;Street<br>Springfield, IL 62701</p>')
page('address-two-addresses', '<p>100 Main Street<br>Springfield, IL 62701</p><p>200 Oak Ave<br>Portland, OR 97201</p>')

# ---- phones, urls, emails, labelled names ----------------------------------------------------------------------------------------
page('phone-formats', '<p>Tel: (202) 555-0120</p><p>Mobile 202.555.0121</p><p>Fax: 202-555-0122</p><p>+44 20 7946 0958</p><p>T: +1 (202) 555-0124</p>')
page('phone-extension', '<p>Phone: 202-555-0125 ext. 301</p><p>Tel +1 202 555 0126 x12</p>')
page('phone-national-no-region', '<p>Call 202-555-0127</p><p>202-555-0128</p><p>+1 202 555 0129</p>', lang='')
page('phone-not-phones', '<p>Updated 2024-01-15, build 192.168.100.100, price $1,234,567, order 20240115123456789, ISBN 978-3-16-148410-0.</p>')
page('phone-invalid-labelled', '<p>Tel: 123-456-7890</p>')
page('phone-international-only', '<p>Zentrale +49 30 901820</p><p>Kontakt +33 1 70 18 99 00</p>', lang='')
page('url-forms', '<p>See https://www.example.com/team?x=1, www.acme.example. Also acme-example.com/jobs and shop.acme-example.com!</p>')
page('url-not-urls', '<p>Open README.md or run setup.sh then see e.g. the U.S.A. page, version 1.2.3 and v2.5.</p>')
page('url-social-and-parentheses', '<p>Follow (https://www.linkedin.com/company/acme-example) and https://github.com/acme-example/repo_(old).</p>')
page('email-in-punctuation', '<p>Email: &lt;jo@acme.example&gt;. Or "jim@acme.example", (kay@acme.example); lee@acme.example.</p>')
page('email-not-url-userinfo', '<p>Clone https://token@git.example.com/repo and mail ops@acme.example</p>')
page('name-labelled', '<p>Name: Jane Doe</p><p>Email: jane@acme.example</p><p>Phone: +1 202 555 0130</p>')
page('name-labelled-company', '<p>Company: Acme Anvils Ltd</p><p>Email: sales@acme.example</p><p>Tel: +1 202 555 0131</p>')
page('name-labelled-both', '<p>Contact person: Maria van der Berg</p><p>Company: Acme Anvils Ltd</p><p>Email: maria@acme.example</p>')
page('name-label-rejected-values', '<p>Name: see below</p><p>Name: J. Doe 3rd</p><p>Contact: Sales Team</p><p>Email: x@acme.example</p>')

# ---- markup and text together ----------------------------------------------------------------------------------------------------------
ld_org = '<script type="application/ld+json">{"@type":"Organization","name":"Acme Anvils","telephone":"+1 202 555 0140","email":"hello@acme.example"}</script>'
page('mixed-jsonld-and-footer-agree', '<footer><p>Acme Anvils</p><p>Call +1 202 555 0140</p><p>hello@acme.example</p></footer>', head=ld_org)
page('mixed-jsonld-and-footer-disagree', '<footer><p>Call +1 202 555 0149</p><p>hello@acme.example</p></footer>', head=ld_org)
page('mixed-jsonld-and-footer-single-email', '<footer><p>Write hello@acme.example</p></footer>', head=ld_org)
page('mixed-microdata-and-visible-text',
     '<div itemscope itemtype="https://schema.org/Person"><span itemprop="name">Pat Micro</span> <span itemprop="email">pat@micro.example</span> <span itemprop="telephone">+1 202 555 0141</span></div>')
page('mixed-hcard-and-visible-text',
     '<div class="h-card"><span class="p-name">Hazel Card</span> <a class="u-email" href="mailto:hazel@card.example">hazel@card.example</a> <span class="p-tel">+1 202 555 0142</span></div>')
page('mixed-mailto-link-and-its-text', '<p><a href="mailto:link@acme.example">link@acme.example</a> and <a href="mailto:other@acme.example">write to us</a></p>')
page('mixed-tel-link-and-its-text', '<p><a href="tel:+12025550143">+1 202 555 0143</a></p>')
page('mixed-address-element-and-link',
     '<address>Acme Anvils<br>100 Main Street<br>Springfield, IL 62701<br><a href="mailto:addr@acme.example">addr@acme.example</a> <a href="tel:+12025550146">+1 202 555 0146</a></address>')
page('mixed-link-without-text-is-not-in-text', '<p><a href="mailto:icon@acme.example"><img src="x.png" alt=""></a></p>')

# ---- encodings and offsets ---------------------------------------------------------------------------------------------------------------
page('offsets-utf8-multibyte', '<p>\u4f60\u597d \u2014 caf\u00e9 \U0001f600 Mail: zo\u00eb@caf\u00e9.example and +1 202 555 0147</p>')
page('offsets-entities-before-value', '<p>Fish &amp; Chips &copy; 2024 &mdash; chip@acme.example</p>')
page('offsets-comment-and-tags-before', '<!-- a comment --><div><b>Bold</b> <i>it</i> <a href="#x">link</a> mail <u>u</u>@acme.example</div>')
page('offsets-crlf-page', '<p>Line one\r\nLine two\r\n</p>\r\n<p>Mail: crlf@acme.example</p>\r\n')
page('offsets-cp1252.cp1252', '<p>Caf\u00e9 \u2013 Mail: caf\u00e9@acme.example</p>', enc='cp1252')
page('offsets-utf16le.utf16le', '<p>Mail: wide@acme.example Tel +1 202 555 0148</p>', enc='utf-16-le')
page('offsets-utf8-bom', '\ufeff<!DOCTYPE html><html><body><p>Mail: bom@acme.example</p></body></html>', raw='\ufeff<!DOCTYPE html><html><body><p>Mail: bom@acme.example</p></body></html>')

# ---- plain text notes ---------------------------------------------------------------------------------------------------------------
note('note-basic', 'Spoke with Jane Doe today.\nName: Jane Doe\nEmail: jane@acme.example\nPhone: +1 202 555 0170\n')
note('note-two-paragraphs', 'Name: Ada Example\nEmail: ada@acme.example\nPhone: +1 202 555 0171\n\nName: Bo Sample\nEmail: bo@acme.example\nPhone: +1 202 555 0172\n')
note('note-rule-separated', 'ada@acme.example\n+1 202 555 0171\n-----\nbo@acme.example\n+1 202 555 0172\n')
note('note-signature', 'Thanks,\n\nJo Example\nAcme Anvils\n100 Main Street\nSpringfield, IL 62701\nT: +1 202 555 0173\njo@acme.example\nwww.acme.example\n')
note('note-crm-company', 'Company: Acme Anvils Ltd\nEmail: sales@acme.example\nTel: +1 202 555 0174\n')
note('note-crlf', 'Name: Cy Placeholder\r\nEmail: cy@acme.example\r\nPhone: +1 202 555 0175\r\n')
note('note-obfuscated', 'reach me: jo [at] acme [dot] com or jo(at)acme(dot)com\n')
note('note-ugc-urls', 'great post! see https://www.example.org/post/1, http://blog.example.net and www.example.com.\n')
note('note-unicode', '\u4f60\u597d\U0001f600 \u00e9t\u00e9 Mail: \u00e9mile@acme.example Tel +33 1 70 18 99 01\n')
note('note-ca-address', 'Maple Leaf Corp\n10 King Street West\nToronto, ON M5V 2T6\nTel (416) 555-0100\n')
note('note-empty', '')
note('note-whitespace-only', '  \n\t\n   \n')
note('note-no-contacts', 'Nothing to see here, just a few words about the weather.\nIt was sunny on 2024-01-15 at 10:30.\n')
note('note-labelled-no-region-phone', 'Name: Jan Kowalski\nTel: 202-555-0176\nEmail: jan@acme.example\n')
note('note-many-emails-one-paragraph', 'a@acme.example b@acme.example c@acme.example\n')
note('note-obfuscated-reversed-string', 'moc.elpmaxe@nhoj is backwards plain text, not decoded\n')

print(len(os.listdir(root + '/pages')), 'pages,', len(os.listdir(root + '/notes')), 'notes')
