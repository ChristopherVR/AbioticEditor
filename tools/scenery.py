"""Package a maintainer's 3D cache for the browser editor on Pages; never place this data in app projects.

The desktop app never downloads any of it: it reads the player's installed game.
"""
import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path

FOLDERS = {"levels", "worlds", "meshinfo", "materials-v5", "classes-v4", "meshes",
           "meshes-posed-v2", "meshes-terrain-v3", "textures", "terrain-materials-v1", "texture-alpha-v1"}
FILE = re.compile(r"[a-z0-9-]+/[a-f0-9]{64}\.(?:json|bin|abm|png)\Z")
CHUNK_BYTES = 80 * 1024 * 1024
# GitHub Pages refuses a site over 1 GiB. The objects' models took the site past the old 900 MiB budget
# (883.6 MiB before them); this keeps a margin under the hard limit.
PAGES_BUDGET_MIB = 980
# Small JSON answers the browser needs before drawing anything: a level asks for about a thousand of
# them, and fetched one by one they took 20 seconds. All of them are also published together as one
# descriptions.json per build (2.6 MB, about 330 KB compressed); the separate files stay too.
DESCRIPTION_FOLDERS = {"meshinfo", "materials-v5", "texture-alpha-v1", "terrain-materials-v1"}
# How each placed object looks (a few hundred answers, 2-3 MB): published together as classes.json, which
# the browser loads once when a scene holds objects, so the view needs no request per class.
CLASS_FOLDERS = {"classes-v4"}
# Extensions browser download managers (IDM, FDM and the like) capture by default. A published
# scenery file must not end in one, or the browser editor's request for it is taken away.
CAPTURED_EXTENSIONS = {".7z", ".aac", ".apk", ".arj", ".avi", ".bin", ".bz2", ".cab", ".dmg", ".exe", ".gz",
                       ".gzip", ".img", ".iso", ".lzh", ".m4a", ".mkv", ".mov", ".mp3", ".mp4", ".mpg", ".msi",
                       ".ogg", ".pdf", ".rar", ".tar", ".tgz", ".wav", ".wma", ".wmv", ".xz", ".z", ".zip"}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def signature(paks, mappings):
    files = sorted(paks.glob("*"), key=lambda p: p.name.lower())
    identity = "scenery-v1\n"
    archives = [p for p in files if p.suffix.lower() in {".pak", ".utoc", ".ucas"}]
    if not archives:
        raise ValueError("No installed game pak archives found")
    for path in archives:
        size = path.stat().st_size
        with path.open("rb") as stream:
            stream.seek(0 if path.suffix.lower() == ".utoc" else size if path.suffix.lower() == ".ucas" else max(0, size - 65536))
            tail = digest(stream.read())
        identity += f"{path.name.lower()}:{size}:{tail}\n"
    identity += f"mappings:{digest(mappings.read_bytes())}\n"
    return digest(identity.encode())


def local_stamp(paks):
    # Match the existing desktop cache identity, including .NET UTC ticks.
    value = "v1|"
    for path in sorted(paks.iterdir(), key=lambda p: p.name.lower()):
        if path.is_file():
            stat = path.stat()
            ticks = stat.st_mtime_ns // 100 + 621355968000000000
            value += f"{path.name}:{stat.st_size}:{ticks}|"
    return digest(value.encode())[:16]


def export(cache, paks, mappings, destination, extend=False):
    """Packs the prepared cache. With extend, adds only what an existing export of this build lacks."""
    if cache.name != local_stamp(paks):
        raise ValueError("Cache does not match this installed game. Prepare the current game's 3D view first.")
    build = signature(paks, mappings)
    output = destination / build
    if output.exists() and not extend:
        raise ValueError(f"Export already exists: {output}")
    if extend:
        if not output.exists():
            raise ValueError(f"No export of this build to extend: {output}")
        manifest = json.loads((output / "manifest.json").read_text(encoding="utf-8"))
        chunks = json.loads((output / "chunks.json").read_text(encoding="utf-8"))
        if manifest["format"] != 1 or manifest["signature"] != build:
            raise ValueError("Incompatible scenery manifest")
    else:
        output.mkdir(parents=True)
        manifest = {"format": 1, "signature": build, "files": {}}
        chunks = []
    known = len(manifest["files"])
    archive = None
    size = 0
    inventory = cache / "hosted-files.json"
    if inventory.exists():
        keys = json.loads(inventory.read_text(encoding="utf-8"))
        if any(not FILE.fullmatch(key) for key in keys):
            raise ValueError("Invalid prepared scenery inventory")
        paths = [cache / key for key in sorted(set(keys))]
    else:
        paths = sorted(cache.glob("*/*"))
    try:
        for path in paths:
            key = path.relative_to(cache).as_posix()
            if path.parent.name not in FOLDERS or not FILE.fullmatch(key) or not path.is_file() or key in manifest["files"]:
                continue
            data = path.read_bytes()
            if not data:  # failed local extractions are not hosted assets
                continue
            if len(data) > CHUNK_BYTES:
                raise ValueError(f"Asset too large for a Pages source chunk: {key}")
            if archive is None or size + len(data) > CHUNK_BYTES:
                if archive:
                    archive.close()
                name = f"part-{len(chunks):03}.zip"
                chunks.append(name)
                archive = zipfile.ZipFile(output / name, "w", zipfile.ZIP_DEFLATED, compresslevel=6)
                size = 0
            archive.writestr(key, data)
            manifest["files"][key] = {"size": len(data), "sha256": digest(data)}
            size += len(data)
    finally:
        if archive:
            archive.close()
    if not any(key.startswith("levels/") for key in manifest["files"]):
        raise ValueError("No prepared levels in this cache")
    (output / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")) + "\n", encoding="utf-8")
    (output / "chunks.json").write_text(json.dumps(chunks) + "\n", encoding="utf-8")
    print(f"Exported {len(manifest['files']) - known} new assets for {build} ({len(manifest['files'])} in {len(chunks)} chunks)")


def assemble(source, destination):
    builds = []
    for build in sorted(source.iterdir()):
        if not build.is_dir() or not re.fullmatch(r"[a-f0-9]{64}", build.name):
            continue
        manifest = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
        if manifest["format"] != 1 or manifest["signature"] != build.name:
            raise ValueError("Incompatible scenery manifest")
        output = destination / "v1" / build.name
        output.mkdir(parents=True, exist_ok=True)
        expected = manifest["files"]
        seen = set()
        descriptions = {}
        classes = {}
        for chunk in json.loads((build / "chunks.json").read_text(encoding="utf-8")):
            if not re.fullmatch(r"part-[0-9]{3}\.zip", chunk):
                raise ValueError("Invalid chunk name")
            with zipfile.ZipFile(build / chunk) as archive:
                for item in archive.infolist():
                    key = item.filename
                    if not FILE.fullmatch(key) or key not in expected or key in seen:
                        raise ValueError(f"Unexpected or duplicate scenery file: {key}")
                    entry = expected[key]
                    if item.file_size != entry["size"] or item.file_size > CHUNK_BYTES:
                        raise ValueError(f"Incorrect scenery size: {key}")
                    data = archive.read(item)
                    if digest(data) != entry["sha256"]:
                        raise ValueError(f"Incorrect scenery hash: {key}")
                    target = output / published_name(key)
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(data)
                    seen.add(key)
                    if key.split("/")[0] in DESCRIPTION_FOLDERS:
                        descriptions[key] = json.loads(data)
                    elif key.split("/")[0] in CLASS_FOLDERS:
                        classes[key] = json.loads(data)
        if seen != set(expected):
            raise ValueError("Scenery export is missing assets")
        (output / "descriptions.json").write_text(
            json.dumps(dict(sorted(descriptions.items())), separators=(",", ":")) + "\n", encoding="utf-8")
        (output / "classes.json").write_text(
            json.dumps(dict(sorted(classes.items())), separators=(",", ":")) + "\n", encoding="utf-8")
        # The manifest stays in the source packs: the browser needs none, and desktop v2.26.0 downloaded
        # scenery only when it found one, which the desktop must never do.
        builds.append(build.name)
        print(f"Assembled {len(seen)} verified scenery assets for {build.name}")
    write_index(source, destination, builds)
    total = sum(p.stat().st_size for p in destination.parent.rglob("*") if p.is_file())
    if total > PAGES_BUDGET_MIB * 1024 * 1024:
        raise ValueError(f"Combined Pages site exceeds the {PAGES_BUDGET_MIB} MiB publishing budget")


def published_name(key):
    """Where a cache file is published (HostedSceneryReader.PublishedPath).

    Browser download managers capture requests by the address's file extension, even a page's
    own background requests, and .bin is on their lists: level indexes (cached as .bin) are
    published as .ali, after their ALI1 header. Every other name is kept.
    """
    name = key[:-4] + ".ali" if key.endswith(".bin") else key
    if Path(name).suffix.lower() in CAPTURED_EXTENSIONS:
        raise ValueError(f"Download managers would capture this published file: {name}")
    return name


def write_index(source, destination, builds):
    """Names the build the browser editor draws (it cannot fingerprint an installed game)."""
    if not builds:
        raise ValueError("No scenery builds to publish")
    marker = source / "latest.txt"
    latest = marker.read_text(encoding="utf-8").strip() if marker.exists() else (builds[0] if len(builds) == 1 else None)
    if latest not in builds:
        raise ValueError("Several scenery builds: name the newest in assets/scenery/latest.txt")
    index = {"format": 1, "latest": latest, "builds": builds}
    (destination / "v1" / "index.json").write_text(json.dumps(index, separators=(",", ":")) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    pack = sub.add_parser("export")
    pack.add_argument("--cache", type=Path, required=True)
    pack.add_argument("--paks", type=Path, required=True)
    pack.add_argument("--mappings", type=Path, required=True)
    pack.add_argument("--destination", type=Path, default=Path("assets/scenery"))
    pack.add_argument("--extend", action="store_true", help="add what an existing export of this build lacks")
    stage = sub.add_parser("assemble")
    stage.add_argument("--source", type=Path, default=Path("assets/scenery"))
    stage.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "export":
        export(args.cache, args.paks, args.mappings, args.destination, args.extend)
    else:
        assemble(args.source, args.destination)
