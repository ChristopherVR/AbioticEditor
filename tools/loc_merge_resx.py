#!/usr/bin/env python3
"""Merge tools/loc_keys.json into the neutral AppResources.resx.

Adds a <data> entry for every key not already present, grouped by key prefix (the part before
the first underscore) with a comment header per group, so the file stays readable for
translators. Existing entries are left untouched.
"""
import json, re, os, subprocess
from xml.sax.saxutils import escape

ROOT = subprocess.check_output(['git', 'rev-parse', '--show-toplevel'], text=True).strip()
os.chdir(ROOT)

RESX = 'src/AbioticEditor.App/Localization/AppResources.resx'

with open('tools/loc_keys.json', encoding='utf-8') as fh:
    keys = json.load(fh)

with open(RESX, encoding='utf-8') as fh:
    text = fh.read()

existing = set(re.findall(r'<data name="([^"]+)"', text))
new_keys = {k: v for k, v in keys.items() if k not in existing}

# Group by prefix (before first underscore), groups and keys sorted.
groups = {}
for k in sorted(new_keys):
    prefix = k.split('_', 1)[0]
    groups.setdefault(prefix, []).append(k)

lines = []
for prefix in sorted(groups):
    lines.append(f'\n  <!-- {prefix} -->')
    for k in groups[prefix]:
        val = escape(new_keys[k])
        lines.append(f'  <data name="{k}" xml:space="preserve"><value>{val}</value></data>')

block = '\n'.join(lines) + '\n'
text = text.replace('</root>', block + '</root>')

with open(RESX, 'w', encoding='utf-8', newline='\n') as fh:
    fh.write(text)

print(f"existing keys: {len(existing)}")
print(f"new keys added: {len(new_keys)}")
print(f"total keys now: {len(existing) + len(new_keys)}")
