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
        self.assertEqual((output / self.key).read_bytes(), b"prepared level")
        self.assertEqual(len(list(output.rglob("*.bin"))), 1)
        self.assertFalse(list(output.rglob("*.zip")))
        self.assertFalse(list(output.rglob("personal.txt")))

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
