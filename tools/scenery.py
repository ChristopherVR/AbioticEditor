"""Package the desktop 3D cache for Pages; never place this data in app projects."""
import argparse
import hashlib
import json
import re
import shutil
import zipfile
from pathlib import Path

FOLDERS = {"levels", "worlds", "meshinfo", "materials-v5", "classes-v4", "meshes",
           "meshes-posed-v2", "meshes-terrain-v3", "textures", "terrain-materials-v1", "texture-alpha-v1"}
FILE = re.compile(r"[a-z0-9-]+/[a-f0-9]{64}\.(?:json|bin|abm|png)\Z")
CHUNK_BYTES = 80 * 1024 * 1024


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


def export(cache, paks, mappings, destination):
    if cache.name != local_stamp(paks):
        raise ValueError("Cache does not match this installed game. Prepare the current game's 3D view first.")
    build = signature(paks, mappings)
    output = destination / build
    if output.exists():
        raise ValueError(f"Export already exists: {output}")
    output.mkdir(parents=True)
    manifest = {"format": 1, "signature": build, "files": {}}
    chunks = []
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
            if path.parent.name not in FOLDERS or not FILE.fullmatch(key) or not path.is_file():
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
    print(f"Exported {len(manifest['files'])} assets for {build} in {len(chunks)} chunks")


def assemble(source, destination):
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
                    target = output / key
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(data)
                    seen.add(key)
        if seen != set(expected):
            raise ValueError("Scenery export is missing assets")
        shutil.copyfile(build / "manifest.json", output / "manifest.json")
        print(f"Assembled {len(seen)} verified scenery assets for {build.name}")
    total = sum(p.stat().st_size for p in destination.parent.rglob("*") if p.is_file())
    if total > 900 * 1024 * 1024:
        raise ValueError("Combined Pages site exceeds the 900 MiB publishing budget")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    pack = sub.add_parser("export")
    pack.add_argument("--cache", type=Path, required=True)
    pack.add_argument("--paks", type=Path, required=True)
    pack.add_argument("--mappings", type=Path, required=True)
    pack.add_argument("--destination", type=Path, default=Path("assets/scenery"))
    stage = sub.add_parser("assemble")
    stage.add_argument("--source", type=Path, default=Path("assets/scenery"))
    stage.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "export":
        export(args.cache, args.paks, args.mappings, args.destination)
    else:
        assemble(args.source, args.destination)
