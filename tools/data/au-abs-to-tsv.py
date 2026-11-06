#!/usr/bin/env python3
"""Converts the ABS correspondence of Suburbs and Localities (SAL) to Postal Areas (POA), 2021, ASGS Edition 3, into tab-separated text.

The file is an Excel workbook, which the address tool does not read itself. This script copies the cell values and changes nothing
else, except that a postal area code is written with four digits (the workbook stores 0800 as the number 800).

Usage: au-abs-to-tsv.py <CG_SAL_2021_POA_2021.xlsx> <output.tsv>   (needs openpyxl 3.1)
"""
import sys

import openpyxl


def cell(value):
    return "" if value is None else str(value).replace("\t", " ").replace("\r", " ").replace("\n", " ").strip()


def main():
    book = openpyxl.load_workbook(sys.argv[1], read_only=True)
    sheet = book.worksheets[0]
    with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as f:
        for i, row in enumerate(sheet.iter_rows(values_only=True)):
            values = [cell(v) for v in row[:7]]
            if i > 0 and values[2].isdigit():
                values[2] = values[2].zfill(4)
            f.write("\t".join(values) + "\n")


main()
