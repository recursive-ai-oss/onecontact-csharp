"""Tests of score-extraction.py on small synthetic sets (no real data)."""
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "..", "score-extraction.py")
sys.path.insert(0, os.path.join(HERE, ".."))
spec = importlib.util.spec_from_file_location("score_extraction", SCRIPT)
se = importlib.util.module_from_spec(spec)
spec.loader.exec_module(se)


def write(path, rows):
    with open(path, "w", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r) + "\n")


def f(i, type_, start, end, text):
    return {"id": i, "type": type_, "start": start, "end": end, "text": text}


class Keys(unittest.TestCase):
    def test_phone_digits_and_extension(self):
        self.assertEqual(se.key("phone", "+1 (202) 456-1111"), "2024561111")
        self.assertEqual(se.key("phone", "202.456.1111 ext. 5"), "2024561111")

    def test_email_and_website(self):
        self.assertEqual(se.key("email", "mailto:Jo@Example.COM."), "jo@example.com")
        self.assertEqual(se.key("website", "https://www.Example.com/a/?utm=1#x"), "example.com/a")

    def test_names_fold(self):
        self.assertEqual(se.key("person", "Jos\u00e9 O'Neil"), se.key("person", "JOSE ONEIL"))


class Scoring(unittest.TestCase):
    gold = [
        {"id": "a", "fields": [f(0, "person", 0, 8, "Jo Smith"), f(1, "phone", 10, 22, "202-456-1111"), f(2, "email", 30, 42, "jo@example.com")],
         "records": [[0, 1, 2]]},
        {"id": "b", "fields": [f(0, "phone", 5, 17, "202-456-2222"), f(1, "phone", 40, 52, "202-456-2222")], "records": [[0, 1]]},
    ]

    def run_scorer(self, preds, extra=()):
        with tempfile.TemporaryDirectory(dir=os.environ.get("TMPDIR")) as d:
            write(os.path.join(d, "g.jsonl"), self.gold)
            cmd = [sys.executable, SCRIPT, "--gold", os.path.join(d, "g.jsonl"), "--json", os.path.join(d, "o.json"), "--resamples", "50"]
            for name, rows in preds.items():
                write(os.path.join(d, name + ".jsonl"), rows)
                cmd += ["--pred", f"{name}={os.path.join(d, name + '.jsonl')}"]
            subprocess.run(cmd + list(extra), check=True, capture_output=True)
            with open(os.path.join(d, "o.json"), encoding="utf-8") as fh:
                return {(e["metric"], e["system"]): e for e in json.load(fh)}

    def test_perfect_system_scores_100_everywhere(self):
        rows = self.run_scorer({"ours": [dict(g, records=g["records"]) for g in self.gold]})
        for m in ("phone span F1", "phone value F1", "all span F1", "record F1", "link F1"):
            self.assertEqual(rows[(m, "ours")]["score"], 100.0, m)

    def test_span_is_strict_and_value_is_not(self):
        # a: one character short at the end (wrong span, right value); b: right span for one of two mentions, right value.
        pred = [{"id": "a", "fields": [f(0, "phone", 10, 21, "202-456-1111")]},
                {"id": "b", "fields": [f(0, "phone", 5, 17, "(202) 456-2222")]}]
        rows = self.run_scorer({"x": pred}, ["--produces", "x=phone"])
        # span: 1 right of 2 predicted, 1 right of 3 gold phones -> P .5, R .333, F1 .4
        self.assertAlmostEqual(rows[("phone span F1", "x")]["score"], 40.0, places=1)
        self.assertEqual(rows[("phone value F1", "x")]["score"], 100.0)
        self.assertNotIn(("email span F1", "x"), rows)

    def test_records_need_the_exact_set(self):
        pred = [{"id": "a", "fields": [f(0, "person", 0, 8, "Jo Smith"), f(1, "phone", 10, 22, "202-456-1111"), f(2, "email", 30, 42, "jo@example.com")],
                 "records": [[0, 1], [2]]},
                {"id": "b", "fields": [f(0, "phone", 5, 17, "202-456-2222")], "records": [[0]]}]
        rows = self.run_scorer({"x": pred})
        # gold records: {Jo, phone, email} and {phone b}; predicted: 3 records, 1 right -> P 1/3, R 1/2, F1 .4
        self.assertAlmostEqual(rows[("record F1", "x")]["score"], 40.0, places=1)
        # link pairs: gold has 3 in a; predicted has 1 (Jo-phone) -> P 1, R 1/3, F1 .5
        self.assertAlmostEqual(rows[("link F1", "x")]["score"], 50.0, places=1)

    def test_text_mode_leaves_out_markup_fields(self):
        gold = [{"id": "a", "fields": [dict(f(0, "email", 0, 5, "a@b.co"), src="Text"), dict(f(1, "email", 9, 14, "a@b.co"), src="Link")], "records": []}]
        pred = [{"id": "a", "fields": [{"type": "email", "start": 0, "end": 5, "text": "a@b.co"}]}]
        with tempfile.TemporaryDirectory(dir=os.environ.get("TMPDIR")) as d:
            write(os.path.join(d, "g.jsonl"), gold)
            write(os.path.join(d, "p.jsonl"), pred)
            out = os.path.join(d, "o.json")
            subprocess.run([sys.executable, SCRIPT, "--gold", os.path.join(d, "g.jsonl"), "--pred", "x=" + os.path.join(d, "p.jsonl"), "--json", out, "--resamples", "20", "--mentions", "text"], check=True, capture_output=True)
            with open(out, encoding="utf-8") as fh:
                rows = {(e["metric"], e["system"]): e for e in json.load(fh)}
        self.assertEqual(rows[("email span F1", "x")]["score"], 100.0)
        self.assertEqual(rows[("email span F1", "x")]["gold"], 1)


if __name__ == "__main__":
    unittest.main()
