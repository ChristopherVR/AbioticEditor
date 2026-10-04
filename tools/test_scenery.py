import json
import tempfile
import unittest
import zipfile
from pathlib import Path

import scenery


class SceneryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.paks = self.root / "paks"
        self.paks.mkdir()
        (self.paks / "game.pak").write_bytes(b"pak footer")
        (self.paks / "game.utoc").write_bytes(b"index one")
        self.mappings = self.root / "Mappings.usmap"
        self.mappings.write_bytes(b"mappings")
        self.cache = self.root / scenery.local_stamp(self.paks)
        (self.cache / "levels").mkdir(parents=True)
        self.key = "levels/" + "b" * 64 + ".bin"
        (self.cache / self.key).write_bytes(b"prepared level")
        self.source = self.root / "source"
        self.site = self.root / "site" / "scenery"

    def tearDown(self):
        self.temp.cleanup()

    def export(self):
        scenery.export(self.cache, self.paks, self.mappings, self.source)
        return self.source / scenery.signature(self.paks, self.mappings)

    def test_export_and_assembly_keep_data_only_in_pages(self):
        (self.cache / "personal.txt").write_text("not game scenery")
        build = self.export()
        scenery.assemble(self.source, self.site)
        output = self.site / "v1" / build.name
        # Level indexes are published as .ali: download managers capture any .bin address.
        self.assertEqual((output / scenery.published_name(self.key)).read_bytes(), b"prepared level")
        self.assertEqual(len(list(output.rglob("*.ali"))), 1)
        self.assertFalse(list(output.rglob("*.bin")))
        self.assertFalse(list(output.rglob("*.zip")))
        # No manifest on Pages: desktop v2.26.0 downloaded scenery only when it found one.
        self.assertFalse((output / "manifest.json").exists())
        self.assertFalse(list(output.rglob("personal.txt")))

    def test_published_names_avoid_extensions_download_managers_capture(self):
        self.assertEqual(scenery.published_name("levels/" + "b" * 64 + ".bin"), "levels/" + "b" * 64 + ".ali")
        self.assertEqual(scenery.published_name("meshes/" + "b" * 64 + ".abm"), "meshes/" + "b" * 64 + ".abm")
        for key in ["textures/x.zip", "levels/x.gz", "meshes/x.exe"]:
            with self.assertRaises(ValueError):
                scenery.published_name(key)

    def test_extend_adds_only_what_an_existing_build_lacks(self):
        build = self.export()
        with self.assertRaises(ValueError):
            self.export()
        (self.cache / "meshes-posed-v2").mkdir()
        posed = "meshes-posed-v2/" + "c" * 64 + ".abm"
        (self.cache / posed).write_bytes(b"a posed character")
        scenery.export(self.cache, self.paks, self.mappings, self.source, extend=True)
        manifest = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(sorted(manifest["files"]), sorted([self.key, posed]))
        self.assertEqual(json.loads((build / "chunks.json").read_text(encoding="utf-8")), ["part-000.zip", "part-001.zip"])
        with zipfile.ZipFile(build / "part-001.zip") as added:
            self.assertEqual(added.namelist(), [posed])
        scenery.assemble(self.source, self.site)
        self.assertEqual((self.site / "v1" / build.name / posed).read_bytes(), b"a posed character")

    def test_index_names_the_build_the_browser_editor_draws(self):
        build = self.export()
        scenery.assemble(self.source, self.site)
        index = json.loads((self.site / "v1" / "index.json").read_text(encoding="utf-8"))
        self.assertEqual(index, {"format": 1, "latest": build.name, "builds": [build.name]})

    def test_several_builds_need_the_newest_named(self):
        build = self.export()
        (self.paks / "game.utoc").write_bytes(b"index two")
        self.cache.rename(self.root / scenery.local_stamp(self.paks))
        self.cache = self.root / scenery.local_stamp(self.paks)
        newer = self.export()
        with self.assertRaises(ValueError):
            scenery.assemble(self.source, self.site)
        (self.source / "latest.txt").write_text(newer.name + "\n", encoding="utf-8")
        scenery.assemble(self.source, self.site)
        index = json.loads((self.site / "v1" / "index.json").read_text(encoding="utf-8"))
        self.assertEqual(index["latest"], newer.name)
        self.assertEqual(sorted(index["builds"]), sorted([build.name, newer.name]))

    def test_stale_cache_cannot_be_mislabeled_as_a_new_game_build(self):
        (self.paks / "game.pak").write_bytes(b"a new game version")
        with self.assertRaisesRegex(ValueError, "does not match"):
            self.export()

    def test_changed_index_and_mappings_invalidate_portable_signature(self):
        first = scenery.signature(self.paks, self.mappings)
        (self.paks / "game.utoc").write_bytes(b"index two")
        second = scenery.signature(self.paks, self.mappings)
        self.assertNotEqual(first, second)
        self.mappings.write_bytes(b"new mappings")
        self.assertNotEqual(second, scenery.signature(self.paks, self.mappings))

    def test_export_includes_only_required_scenery_when_inventory_is_present(self):
        (self.cache / "meshes").mkdir()
        unrelated = self.cache / "meshes" / ("c" * 64 + ".abm")
        unrelated.write_bytes(b"unrelated object model")
        (self.cache / "hosted-files.json").write_text(json.dumps([self.key]))
        build = self.export()
        manifest = json.loads((build / "manifest.json").read_text())
        self.assertEqual(set(manifest["files"]), {self.key})

    def test_corrupted_pack_is_rejected_before_publishing(self):
        build = self.export()
        with zipfile.ZipFile(build / "part-000.zip", "w") as archive:
            archive.writestr(self.key, b"tampered level")
        with self.assertRaisesRegex(ValueError, "hash"):
            scenery.assemble(self.source, self.site)

    def test_missing_assets_are_rejected(self):
        build = self.export()
        (build / "chunks.json").write_text("[]")
        with self.assertRaisesRegex(ValueError, "missing assets"):
            scenery.assemble(self.source, self.site)

    def test_archive_paths_cannot_escape_pages_directory(self):
        build = self.export()
        key = "../../outside.bin"
        with zipfile.ZipFile(build / "part-000.zip", "w") as archive:
            archive.writestr(key, b"bad")
        manifest_path = build / "manifest.json"
        manifest = json.loads(manifest_path.read_text())
        manifest["files"] = {key: {"size": 3, "sha256": scenery.digest(b"bad")}}
        manifest_path.write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, "Unexpected"):
            scenery.assemble(self.source, self.site)


if __name__ == "__main__":
    unittest.main()
