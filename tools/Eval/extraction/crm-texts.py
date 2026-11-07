#!/usr/bin/env python3
"""Writes synthetic CRM and user-comment style texts that mention people, for tuning the free-text name rules.

usage: crm-texts.py <names data root> <output dir> [count per category] [seed]

The names come from the development splits of the name training sets (given and family names built from open national lists); the
sentences come from the templates below, which are ours. Each output file holds one category and name group as JSON lines with "id",
"text" and "spans" ([start, end, "person", 0]), in the shape of the contact-block development set, so the extraction tool's `dev` mode
scores them. The negative file has no spans: a person found in it is a false positive.
Categories: running (a name inside a sentence), greeting (a first name addressed), lower, caps, lastfirst, list, header, honorific,
negative. A name group is "listed" (names the rules' own lists are built from) or "other" (names from other languages, which the lists
may not know). Placeholders: {0} {1} {2} are names (a span each); @E@ is an email local part made from the first name.
"""
import glob
import json
import os
import random
import re
import sys


def load(root):
    people = {"listed": [], "other": []}
    companies = []
    for group, d in (("listed", "names-us-ca-v1"), ("other", "names-tier2-v1")):
        for path in glob.glob(os.path.join(root, d, "dev", "*.jsonl")):
            for line in open(path, encoding="utf-8"):
                j = json.loads(line)
                if j["class"] == "person" and j.get("pattern") == "gf" and not j.get("noise"):
                    parts = {p[0]: j["text"][p[1]:p[2]] for p in j["parts"]}
                    if "Given" in parts and "Family" in parts and " " not in parts["Given"] and " " not in parts["Family"]:
                        people[group].append((parts["Given"], parts["Family"]))
                elif j["class"] == "company" and j.get("pattern") == "register" and group == "listed":
                    companies.append(j["text"])
    return people, companies


def render(template, names, local=None):
    """Substitutes the {n} placeholders and returns the text with a span per name; @E@ becomes the email local part."""
    template = template.replace("@E@", local or "info")
    text, spans, i = "", [], 0
    for m in re.finditer(r"\{(\d)\}", template):
        text += template[i:m.start()]
        s = len(text)
        text += names[int(m.group(1))]
        spans.append([s, len(text), "person", 0])
        i = m.end()
    return text + template[i:], spans


RUNNING = [
    "{0} called this morning about the renewal.", "I talked to {0} yesterday and she said the quote is ready.", "Per {0}, the install is scheduled for Tuesday.",
    "The invoice was approved by {0} on Friday.", "Left a voicemail for {0}, will try again tomorrow.", "Customer wants {0} to handle the account from now on.",
    "Thanks to {0} for the quick turnaround!", "Spoke with {0} regarding the contract terms.", "{0} from the front office will send the paperwork.",
    "Called twice, no answer. Asked for {0} but she was out.", "Referred by {0}, who bought the same unit last year.", "We met {0} at the trade show and she was great.",
    "Note: {0} is the point of contact for billing questions.", "Please forward this to {0} when you get a chance.", "Our technician {0} arrived on time and fixed the leak.",
    "Great service, {0} was very helpful and professional.", "Follow up with {0} next week about the estimate.", "Emailed {0} the updated proposal.",
    "Visit scheduled with {0} and the site supervisor.", "Reached {0} on her cell, she confirmed the delivery date.",
]
GREETING = [
    "Hi {0},\nThanks for getting back to me.", "Hello {0}, your order has shipped.", "Dear {0},\nPlease find the invoice attached.", "Hey {0}! Quick question about the schedule.",
    "Thanks {0}!", "Good morning {0}, just checking in.", "Thank you, {0}.", "Hi {0}, can you call me back at 212-555-0100?", "Regards,\n{0}", "Best,\n{0}\nSales Manager", "Sincerely,\n{0}",
]
LOWER = [
    "spoke with {0} about the quote", "{0} 212-555-0100", "call {0} at (212) 555-0142 after 3", "ask for {0} when you arrive", "contact: {0} - 212-555-0100",
    "met with {0} today, follow up friday", "{0} left a message", "sent estimate to {0} @E@@example.com",
]
CAPS = [
    "CONTACT: {0} - 212-555-0100", "{0}\n212-555-0100", "ATTN: {0}", "SPOKE WITH {0} RE: QUOTE", "{0} CALLED, WANTS CALLBACK", "CUSTOMER {0} (212) 555-0142", "ASK FOR {0}",
    "PER {0}, JOB IS ON HOLD",
]
LASTFIRST = [
    "{0} - 212-555-0100", "{0}, Sales Manager", "Contact: {0}", "Attendees: {0}; and others", "Patient: {0} (new)", "{0}\n{1}", "Ref: {0}; {1}",
    "Referred by {0}",
]
LISTS = [
    "Attendees: {0}, {1} and {2}.", "Present: {0}, {1}, {2}", "{0}\n{1}\n{2}", "Team: {0}; {1}; {2}", "Meeting with {0} and {1} on Monday.", "Cc: {0}, {1}",
    "Thanks to {0}, {1}, and {2} for coming.",
]
HEADER = [
    "From: {0} <@E@@example.com>", "To: {0} <@E@@example.org>", "Reply-To: \"{0}\" <@E@@acme.example>", "Sent by {0} (@E@@example.com)", "From: {0}\nSent: Monday\nTo: Support",
]
HONORIFIC = [
    "Dr. {0} will see you at 3.", "Please ask for Mrs. {0}.", "Ms. {0} approved the request.", "Mr. {0} called about the roof.", "Prof. {0} reviewed the draft.",
    "Dr {0} is out until Monday.", "Spoke to Mr {0} about parking.",
]
NEGATIVES = [
    "Visit Home Depot for all your supplies.", "We stayed at the Grand Hotel in Santa Monica and loved it.", "Mark Twain Elementary School closes at 3.",
    "Rose Hill Cemetery offers guided tours.", "Black Friday sale starts Thursday at Best Buy.", "Will Call window opens at noon.", "Grace Period ends on the 15th.",
    "Ordered from {C} last week.", "Thanks for choosing {C}!", "Invoice from {C} is overdue.", "Our office is on Washington Avenue near Park Place.",
    "The Summer Collection by Great Value ships free.", "Hope Springs Resort has a pool.", "Call the Customer Service Department for help.",
    "Project Management Office will review it.", "Santa Clara County Health Department notice.", "Austin Texas weather is hot in July.",
    "Order Status: Shipped via United Parcel Service.", "Frequently Asked Questions About Returns", "Meeting at Lincoln Center, Room 4B.",
    "Chase Bank branch closes early on Friday.", "Brooklyn Bridge Park hosts the event.", "Jordan Valley Medical Group - Family Practice",
    "Dallas Cowboys tickets are sold out.", "Mercy General Hospital, 3rd floor.", "Order Number 4521 shipped to Palm Springs.",
    "Open Monday through Friday. Closed Sunday.", "Keep Calm and Carry On.", "Wells Fargo Home Mortgage", "Pastor Appreciation Day at Grace Church.",
    "Great, Thanks for the update.", "Sure, Monday works.", "Hello, World", "Yes, Sir.", "Paris, France", "Portland, Oregon", "Lincoln, Nebraska", "Orlando, Florida",
]


def main():
    root, out = sys.argv[1], sys.argv[2]
    count = int(sys.argv[3]) if len(sys.argv) > 3 else 300
    seed = int(sys.argv[4]) if len(sys.argv) > 4 else 20261106
    rng = random.Random(seed)
    people, companies = load(root)
    os.makedirs(out, exist_ok=True)

    def write(cat, group, rows):
        with open(os.path.join(out, f"crm-{cat}-{group}.jsonl"), "w", encoding="utf-8", newline="\n") as f:
            for i, (text, spans) in enumerate(rows):
                f.write(json.dumps({"id": f"{cat}-{group}-{i}", "text": text, "spans": spans}, ensure_ascii=False) + "\n")

    for group in ("listed", "other"):
        pick = lambda: rng.choice(people[group])
        full = lambda p: f"{p[0]} {p[1]}"
        rows = {c: [] for c in ("running", "greeting", "lower", "caps", "lastfirst", "list", "header", "honorific")}
        for _ in range(count):
            p = pick()
            rows["running"].append(render(rng.choice(RUNNING), [full(p)]))
            t = rng.choice(GREETING)
            rows["greeting"].append(render(t, [p[0]]))
            rows["lower"].append(render(rng.choice(LOWER), [full(p).lower()], p[0].lower() + "." + p[1].lower()))
            rows["caps"].append(render(rng.choice(CAPS), [full(p).upper()]))
            q = pick()
            rows["lastfirst"].append(render(rng.choice(LASTFIRST), [f"{p[1]}, {p[0]}", f"{q[1]}, {q[0]}"]))
            ps = [pick() for _ in range(3)]
            rows["list"].append(render(rng.choice(LISTS), [full(x) for x in ps]))
            rows["header"].append(render(rng.choice(HEADER), [full(p)], p[0].lower() + "." + p[1].lower() if rng.random() < 0.7 else (p[0][0] + p[1]).lower()))
            # the title belongs to the name: the gold span covers "Dr. Patel"
            t = rng.choice(HONORIFIC)
            m = re.match(r"(.*?)((?:Dr\.?|Mrs\.?|Ms\.?|Mr\.?|Prof\.?) )\{0\}", t)
            text, spans = render(t, [p[1]])
            if m:
                spans[0][0] -= len(m.group(2))
            rows["honorific"].append((text, spans))
        for cat, r in rows.items():
            write(cat, group, r)
    neg = []
    for _ in range(count * 2):
        t = rng.choice(NEGATIVES)
        neg.append((t.replace("{C}", rng.choice(companies)), []))
    write("negative", "none", neg)


if __name__ == "__main__":
    main()
