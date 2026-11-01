"""Writes the predefined CMaps the library declares non-embedded CID fonts with as compact tables.

    python build/cmap-tables.py <dir holding the Adobe CMap files>

The CMap files are Adobe's public cmap-resources (github.com/adobe-type-tools/cmap-resources,
BSD-3-Clause, notice in src/Text/Resources/CMap.NOTICE.txt): Adobe-Japan1-7/CMap/UniJIS-UCS2-H
and 90ms-RKSJ-H, Adobe-GB1-6/CMap/UniGB-UCS2-H and GBK-EUC-H, Adobe-CNS1-7/CMap/UniCNS-UCS2-H
and Adobe-Korea1-2/CMap/UniKS-UCS2-H. Each table is src/Text/Resources/CMap.<name>.bin, a
sequence of little-endian UInt16 triples: first the number of codespace ranges, then the
codespace ranges as (first code, last code, bytes per code), then the cidrange and cidchar
entries as (first code, last code, first CID), sorted by first code; a cidchar is a range of
one code.
"""
import os
import re
import struct
import sys

NAMES = ["UniJIS-UCS2-H", "90ms-RKSJ-H", "UniGB-UCS2-H", "GBK-EUC-H", "UniCNS-UCS2-H", "UniKS-UCS2-H"]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Text", "Resources")


def codespace(text):
    for block in re.findall(r"begincodespacerange(.*?)endcodespacerange", text, re.S):
        for first, last in re.findall(r"<([0-9a-fA-F]+)>\s*<([0-9a-fA-F]+)>", block):
            yield int(first, 16), int(last, 16), len(first) // 2


def entries(text):
    for block in re.findall(r"begincidrange(.*?)endcidrange", text, re.S):
        for first, last, cid in re.findall(r"<([0-9a-fA-F]+)>\s*<([0-9a-fA-F]+)>\s*(\d+)", block):
            yield int(first, 16), int(last, 16), int(cid)
    for block in re.findall(r"begincidchar(.*?)endcidchar", text, re.S):
        for code, cid in re.findall(r"<([0-9a-fA-F]+)>\s*(\d+)", block):
            yield int(code, 16), int(code, 16), int(cid)


def main(source):
    for name in NAMES:
        with open(os.path.join(source, name), encoding="latin-1") as f:
            text = f.read()
        if "usecmap" in text:
            sys.exit(name + " uses another CMap: not supported")
        ranges = sorted(codespace(text))
        table = sorted(entries(text))
        with open(os.path.join(OUT, "CMap." + name + ".bin"), "wb") as f:
            f.write(struct.pack("<3H", len(ranges), 0, 0))
            for first, last, width in ranges:
                f.write(struct.pack("<3H", first, last, width))
            for first, last, cid in table:
                f.write(struct.pack("<3H", first, last, cid))
        print(name, len(ranges), "codespace ranges,", len(table), "cid ranges")


if __name__ == "__main__":
    main(sys.argv[1])
