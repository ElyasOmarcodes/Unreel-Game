#!/usr/bin/env python3
"""
Builds the game's UI font "Urban Agents UI" (UAFont.ttf): the Latin letters of Russo One (OFL)
plus the Pashto / Dari / Arabic letter forms of Vazirmatn UA (OFL, made by make_rtl_font.py).
Unity's simple text renderer uses one font per text, so both scripts must live in one file.

Run:  python3 UnityGame/Tools/make_ui_font.py   (needs fonttools)
"""
import os
from fontTools.ttLib import TTFont
from fontTools.ttLib.scaleUpem import scale_upem
from fontTools import subset
from fontTools.merge import Merger

HERE = os.path.dirname(os.path.abspath(__file__))
LATIN = os.path.join(HERE, "fonts", "RussoOne-Regular.ttf")
RTL = os.path.join(HERE, "fonts", "Vazirmatn-UA.ttf")
OUT = os.path.join(HERE, "..", "Assets", "Resources", "Fonts", "UAFont.ttf")
TMP = "/tmp/ua-font"


def main():
    os.makedirs(TMP, exist_ok=True)
    latin = TTFont(LATIN)
    upem = latin["head"].unitsPerEm

    # the Arabic-script part: only the private-use letter forms, lam-alef ligatures and Arabic digits
    rtl = TTFont(RTL)
    keep = [c for c in rtl.getBestCmap() if 0xE000 <= c <= 0xE1FF or 0xFEF5 <= c <= 0xFEFC or 0x0660 <= c <= 0x0669 or 0x06F0 <= c <= 0x06F9]
    options = subset.Options()
    options.layout_features = []
    options.name_IDs = ["*"]
    options.notdef_outline = True
    options.drop_tables += ["GSUB", "GPOS", "GDEF", "DSIG", "FFTM"]
    sub = subset.Subsetter(options)
    sub.populate(unicodes=keep)
    sub.subset(rtl)
    scale_upem(rtl, upem)
    rtl_path = os.path.join(TMP, "rtl.ttf")
    rtl.save(rtl_path)

    # the Latin part without its layout tables (the merger needs matching tables)
    for tag in ("GSUB", "GPOS", "GDEF", "DSIG", "FFTM"):
        if tag in latin:
            del latin[tag]
    latin_path = os.path.join(TMP, "latin.ttf")
    latin.save(latin_path)

    merged = Merger().merge([latin_path, rtl_path])
    for record in merged["name"].names:
        if record.nameID in (1, 4, 16):
            record.string = "Urban Agents UI"
        elif record.nameID == 6:
            record.string = "UrbanAgentsUI-Regular"
    merged.save(OUT)
    cmap = merged.getBestCmap()
    print(f"{OUT}: {len(cmap)} characters, A={'A' in map(chr, cmap)}, pashto forms={sum(1 for c in cmap if 0xE000 <= c <= 0xE1FF)}")


if __name__ == "__main__":
    main()
