#!/usr/bin/env python3
"""Converts Posta Romana's postal-code workbook (Infocod, September 2016, with SIRUTA codes) into three tab-separated files.

The workbook is an old binary Excel file, which the address tool does not read itself. This script copies cell values and changes
nothing else: no cleaning, no reordering. The sheets are, in order, Bucharest streets, streets of the localities over 50,000
inhabitants, and the other localities.

Usage: ro-infocod-to-tsv.py <workbook.xls> <output directory>   (needs xlrd 2.x)
"""
import os
import sys

import xlrd


def cell(value):
    if isinstance(value, float):
        value = str(int(value)) if value == int(value) else repr(value)
    return str(value).replace("\t", " ").replace("\r", " ").replace("\n", " ").strip()


def main():
    workbook, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    book = xlrd.open_workbook(workbook)
    # (sheet index, file name, columns to copy)
    plan = [
        (0, "bucuresti.tsv", [0, 1, 2, 3, 4, 5, 6, 7, 8]),
        (1, "orase.tsv", [0, 1, 2, 3, 4, 5, 6, 7, 8]),
        (2, "localitati.tsv", [1, 2, 3, 4]),
    ]
    for index, name, columns in plan:
        sheet = book.sheet_by_index(index)
        with open(os.path.join(out, name), "w", encoding="utf-8", newline="\n") as f:
            for r in range(sheet.nrows):
                row = sheet.row_values(r)
                f.write("\t".join(cell(row[c]) if c < len(row) else "" for c in columns) + "\n")


main()
