#!/usr/bin/env python3
"""Assemble localization keys from the working-tree XAML changes.

The extraction agents replaced literal English UI text with {loc:Localize Key} in the XAML.
The exact English now lives only in git history, so we recover (key -> english) from the diff
of every changed App XAML file against HEAD: for each changed line we pair the '-' (old) and
'+' (new) versions and read back the original value where a {loc:Localize Key} now sits.

Outputs:
  tools/loc_keys.json   - { "Key": "English", ... } sorted by key
  prints a coverage report (keys found, plus any {loc:Localize} in the tree we could NOT resolve)
"""
import re, subprocess, json, os, html, sys, glob

ROOT = subprocess.check_output(['git', 'rev-parse', '--show-toplevel'], text=True).strip()
os.chdir(ROOT)

changed = subprocess.check_output(
    ['git', 'diff', '--name-only', 'HEAD', '--', 'src/AbioticEditor.App'], text=True).split()
xaml = [f for f in changed if f.endswith('.xaml')]

attr_re = re.compile(r'([\w.:]+)="([^"]*)"')
loc_attr_val = re.compile(r'^\{loc:Localize (\w+)\}$')
elem_loc = re.compile(r'>\s*\{loc:Localize (\w+)\}\s*<')

result = {}
conflicts = {}

def record(key, english):
    english = html.unescape(english)
    if key in result and result[key] != english:
        conflicts.setdefault(key, set()).add(result[key])
        conflicts[key].add(english)
    result[key] = english

def pair_lines(minus, plus):
    for m, p in zip(minus, plus):
        # Attribute form: attr="{loc:Localize Key}" <- attr="English"
        m_attrs = dict(attr_re.findall(m))
        for a, v in attr_re.findall(p):
            mo = loc_attr_val.match(v)
            if mo and a in m_attrs:
                record(mo.group(1), m_attrs[a])
        # Element-content form: >{loc:Localize Key}< <- >English<
        for mo in elem_loc.finditer(p):
            key = mo.group(1)
            # Reconstruct via shared prefix/suffix around the single replaced span.
            span = mo.group(0)
            idx = p.find(span)
            prefix, suffix = p[:idx], p[idx + len(span):]
            if m.startswith(prefix) and (suffix == '' or m.endswith(suffix)):
                eng = m[len(prefix): len(m) - len(suffix)]
                eng = eng.lstrip('>').rstrip('<').strip()
                if eng:
                    record(key, eng)

for f in xaml:
    diff = subprocess.check_output(['git', 'diff', '-U0', 'HEAD', '--', f], text=True).splitlines()
    minus, plus = [], []
    for line in diff:
        if line.startswith('@@'):
            pair_lines(minus, plus); minus, plus = [], []
        elif line.startswith('-') and not line.startswith('---'):
            minus.append(line[1:])
        elif line.startswith('+') and not line.startswith('+++'):
            plus.append(line[1:])
    pair_lines(minus, plus)

# Coverage: every {loc:Localize Key} referenced anywhere in App XAML must resolve to a value
# (either freshly extracted here or already present in the resx).
referenced = set()
for f in glob.glob('src/AbioticEditor.App/**/*.xaml', recursive=True):
    norm = f.replace('\\', '/')
    if '/bin/' in norm or '/obj/' in norm or not os.path.isfile(f):
        continue
    with open(f, encoding='utf-8') as fh:
        for mo in re.finditer(r'\{loc:Localize (\w+)\}', fh.read()):
            referenced.add(mo.group(1))

existing = set()
resx = 'src/AbioticEditor.App/Localization/AppResources.resx'
with open(resx, encoding='utf-8') as fh:
    for mo in re.finditer(r'<data name="([^"]+)"', fh.read()):
        existing.add(mo.group(1))

unresolved = sorted(referenced - set(result) - existing)

with open('tools/loc_keys.json', 'w', encoding='utf-8') as fh:
    json.dump(dict(sorted(result.items())), fh, ensure_ascii=False, indent=2)

print(f"files changed:        {len(xaml)}")
print(f"keys extracted:       {len(result)}")
print(f"keys referenced:      {len(referenced)}")
print(f"already in resx:      {len(existing & referenced)}")
print(f"UNRESOLVED ({len(unresolved)}): {unresolved}")
if conflicts:
    print(f"CONFLICTS ({len(conflicts)}):")
    for k, vs in conflicts.items():
        print(f"  {k}: {sorted(vs)}")
