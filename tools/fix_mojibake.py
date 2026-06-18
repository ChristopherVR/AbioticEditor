#!/usr/bin/env python3
"""Repair UTF-8-as-cp1252 mojibake in the resx <value> text.

tools/loc_extract.py decoded `git diff` output with the Windows default codec (cp1252) instead
of UTF-8, so characters like the ellipsis and middot were mangled into sequences such as `â€¦`
and `Â·` when they were merged into AppResources.resx. Re-encoding a mangled value back to
cp1252 recovers the original UTF-8 bytes, which then decode as UTF-8 to the correct character.

This is safe to run on every resx: a pure-ASCII value round-trips to itself, and a value with
genuine (correctly-stored) accented characters fails the cp1252 re-encode and is left untouched.
"""
import re, sys, os

FILES = [
    "src/AbioticEditor.App/Localization/AppResources.resx",
    "src/AbioticEditor.App/Localization/AppResources.de.resx",
    "src/AbioticEditor.App/Localization/AppResources.es.resx",
    "src/AbioticEditor.App/Localization/AppResources.fr.resx",
]

def repair(s: str) -> str:
    try:
        fixed = s.encode("cp1252").decode("utf-8")
    except (UnicodeEncodeError, UnicodeDecodeError):
        return s  # genuine UTF-8 (accented) or non-cp1252 content - not mojibake
    return fixed

def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    for rel in FILES:
        path = os.path.join(root, rel)
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
        new = re.sub(r"<value>(.*?)</value>",
                     lambda m: "<value>" + repair(m.group(1)) + "</value>",
                     text, flags=re.DOTALL)
        if new != text:
            with open(path, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(new)
        print(f"{os.path.basename(path)}: {'repaired' if new != text else 'unchanged'}")

if __name__ == "__main__":
    main()
