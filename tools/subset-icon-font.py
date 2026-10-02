"""Cut the Material Symbols icon font down to the icons the editor uses.

The full font (assets/fonts/MaterialSymbolsOutlined.full.ttf, about 10 MB, every icon in four
variation axes) is the source. Icons are drawn by ligature: the text "close" in a .material-symbol
span becomes the close icon. This script:

1. scans the app and plugin sources for every word that is also an icon name (a superset of what is
   used, so an icon picked in C# or JavaScript is kept too),
2. pins the variation axes to the look the app uses (outlined, normal grade, weight 400, 24 px design),
3. keeps only those icons plus the letters their names are spelled with,

and writes src/AbioticEditor.Web.Shared/wwwroot/fonts/MaterialSymbolsOutlined.ttf plus
assets/fonts/material-symbols-kept.txt. IconFontSubsetTests fails when code uses an icon the cut-down
font is missing; re-run this script then (pip install fonttools).
"""
import os
import re
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FULL = os.path.join(ROOT, "assets", "fonts", "MaterialSymbolsOutlined.full.ttf")
ALL_LIST = os.path.join(ROOT, "assets", "fonts", "material-symbols-all.txt")
KEPT_LIST = os.path.join(ROOT, "assets", "fonts", "material-symbols-kept.txt")
OUT = os.path.join(ROOT, "src", "AbioticEditor.Web.Shared", "wwwroot", "fonts", "MaterialSymbolsOutlined.ttf")

SCAN_DIRS = ["src", "plugins"]
SCAN_EXT = {".razor", ".cs", ".js", ".mjs", ".css", ".html", ".cshtml", ".ts", ".tsx", ".jsx"}
SKIP_DIRS = {"bin", "obj", "node_modules", "lib", "thumbs", "dist"}
TOKEN = re.compile(r"[a-z0-9_]{2,}")


def source_tokens():
    tokens = set()
    for top in SCAN_DIRS:
        for dirpath, dirnames, filenames in os.walk(os.path.join(ROOT, top)):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for name in filenames:
                if os.path.splitext(name)[1].lower() not in SCAN_EXT:
                    continue
                with open(os.path.join(dirpath, name), encoding="utf-8", errors="ignore") as handle:
                    tokens.update(TOKEN.findall(handle.read()))
    return tokens


def main():
    font = TTFont(FULL)
    icons = sorted({g for g in font.getGlyphOrder() if re.fullmatch(r"[a-z0-9_]+", g)} - {"underscore"})
    with open(ALL_LIST, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(icons) + "\n")

    used = sorted(set(icons) & source_tokens())
    letters = "abcdefghijklmnopqrstuvwxyz0123456789_"
    font = instancer.instantiateVariableFont(font, {"FILL": 0, "GRAD": 0, "opsz": 24, "wght": 400})

    options = subset.Options()
    options.layout_features = ["liga", "rlig", "calt", "ccmp"]
    options.layout_closure = False
    options.name_IDs = ["*"]
    options.notdef_outline = True
    options.glyph_names = True
    subsetter = subset.Subsetter(options)
    subsetter.populate(glyphs=used, text=letters)
    subsetter.subset(font)
    font.save(OUT)

    with open(KEPT_LIST, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(used) + "\n")
    print(f"{len(used)} icons kept, {os.path.getsize(OUT) / 1024:.0f} KB (from {os.path.getsize(FULL) / 1048576:.1f} MB)")


if __name__ == "__main__":
    sys.exit(main())
