"""Tests of score.py on small synthetic sets (no real data)."""
import json
import os
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCORE = os.path.join(HERE, "..", "score.py")
sys.path.insert(0, os.path.join(HERE, ".."))
import score  # noqa: E402


def write(path, rows):
    with open(path, "w", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r) + "\n")


class Normalization(unittest.TestCase):
    def test_folds_case_accents_punctuation(self):
        self.assertEqual(score.norm("Montr\u00e9al, QC"), score.norm("MONTREAL qc"))
        self.assertEqual(score.norm("H3B 1A1"), score.norm("h3b-1a1"))


class AddressItems(unittest.TestCase):
    gold = {"fields": {"house_number": "12", "road": "Main St", "city": "Springfield", "state": "IL", "postcode": "62701"}, "parts": {"street_name": "Main", "street_type": "St"}}

    def test_perfect(self):
        self.assertEqual(score.address_item(self.gold, self.gold, False), (5, 5, 5, True))

    def test_one_wrong_one_extra(self):
        pred = {"fields": {"house_number": "12", "road": "main street", "city": "springfield", "state": "il", "postcode": "62701", "unit": "4"}}
        r, p, g, exact = score.address_item(self.gold, pred, False)
        self.assertEqual((r, p, g, exact), (4, 6, 5, False))

    def test_fine_uses_parts(self):
        pred = {"fields": {}, "parts": {"street_name": "MAIN", "street_type": "ST"}}
        r, p, g, _ = score.address_item(self.gold, pred, True)
        self.assertEqual((r, p), (2, 2))
        self.assertEqual(g, 6)

    def test_missing_prediction(self):
        self.assertEqual(score.address_item(self.gold, None, False)[:2], (0, 0))


class Assignment(unittest.TestCase):
    def test_labels_follow_text(self):
        self.assertEqual(score.assign("Dr. Ana Maria Lopez", {"T": ["Dr."], "G": ["Ana"], "M": ["Maria"], "F": ["Lopez"]}), ["T", "G", "M", "F"])

    def test_repeated_word_uses_each_once(self):
        self.assertEqual(score.assign("Lee Lee", {"G": ["Lee"], "F": ["Lee"]}), ["G", "F"])


class Bootstrap(unittest.TestCase):
    def test_known_interval_and_determinism(self):
        items = [(1, 1)] * 80 + [(0, 1)] * 20
        a = score.bootstrap({"s": items}, score.ratio_stat, 100, 1000, 7)[0]["s"]
        b = score.bootstrap({"s": items}, score.ratio_stat, 100, 1000, 7)[0]["s"]
        self.assertEqual(a, b)
        self.assertAlmostEqual(a[0], 0.8)
        self.assertTrue(0.70 < a[1] < 0.80 < a[2] < 0.90)

    def test_f1(self):
        self.assertAlmostEqual(score.f1_stat([(2, 4, 4)]), 0.5)
        self.assertEqual(score.f1_stat([(0, 3, 3)]), 0.0)


class Command(unittest.TestCase):
    def test_address_and_name_runs_print_only_aggregates(self):
        with tempfile.TemporaryDirectory() as d:
            g = [{"id": str(i), "country": "US", "fields": {"house_number": str(i), "city": "Town"}} for i in range(1, 21)]
            p_good = [{"id": str(i), "fields": {"house_number": str(i), "city": "town"}} for i in range(1, 21)]
            p_bad = [{"id": str(i), "fields": {"house_number": str(i), "city": "other"}} for i in range(1, 21)]
            write(os.path.join(d, "g.jsonl"), g)
            write(os.path.join(d, "a.jsonl"), p_good)
            write(os.path.join(d, "b.jsonl"), p_bad)
            out = subprocess.run([sys.executable, SCORE, "address", "--gold", os.path.join(d, "g.jsonl"), "--pred", "good=" + os.path.join(d, "a.jsonl"),
                                  "--pred", "bad=" + os.path.join(d, "b.jsonl"), "--resamples", "200"], capture_output=True, text=True, check=True).stdout
            self.assertIn("100.00", out)
            self.assertIn("vs_good", out)
            self.assertNotIn("Town", out)
            gn = [{"id": "1", "country": "US", "text": "Ann Lee", "class": "person", "labels": "G F"},
                  {"id": "2", "country": "US", "text": "Acme Inc", "class": "company", "labels": "B L"}]
            pn = [{"id": "1", "class": "person", "parts": {"G": ["Ann"], "F": ["Lee"]}}, {"id": "2", "class": "person", "parts": {"G": ["Acme"]}}]
            write(os.path.join(d, "gn.jsonl"), gn)
            write(os.path.join(d, "pn.jsonl"), pn)
            out = subprocess.run([sys.executable, SCORE, "name", "--gold", os.path.join(d, "gn.jsonl"), "--pred", "x=" + os.path.join(d, "pn.jsonl"), "--resamples", "50"],
                                 capture_output=True, text=True, check=True).stdout
            self.assertIn("classification", out)
            self.assertNotIn("Acme", out)


class PostcodePrefix(unittest.TestCase):
    def test_country_prefix_is_not_part_of_the_postcode(self):
        gold = {"fields": {"postcode": "3505", "city": "Dudelange"}}
        for written in ("L-3505", "l 3505", "3505"):
            right, predicted, _, exact = score.address_item(gold, {"fields": {"postcode": written, "city": "Dudelange"}}, False)
            self.assertEqual((right, predicted, exact), (2, 2, True))
        # Canadian postal codes start with letters and are not touched.
        gold = {"fields": {"postcode": "K1A 0B1"}}
        self.assertTrue(score.address_item(gold, {"fields": {"postcode": "K1A0B1"}}, False)[3])


if __name__ == "__main__":
    unittest.main()
