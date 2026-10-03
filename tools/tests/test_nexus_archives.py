import importlib.util
import io
import json
import os
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("archives", Path(__file__).parents[1] / "verify-nexus-archives.py")
archives = importlib.util.module_from_spec(spec)
spec.loader.exec_module(archives)


def version(identifier, position, category="main", file_id="windows"):
    return {"id": identifier, "file": {"id": file_id}, "position": str(position),
            "category": category, "game_scoped_id": identifier}


class ArchiveTests(unittest.TestCase):
    def test_leftover_versions_are_reported_even_when_immediate_predecessor_is_archived(self):
        rows = [version("old", 1), version("previous", 2, "archived"), version("new", 3)]
        self.assertEqual(archives.check_versions(rows, "windows", "new"), [rows[0]])

    def test_archived_and_removed_versions_pass(self):
        rows = [version("old", 1, "removed"), version("previous", 2, "archived"), version("new", 3)]
        self.assertEqual(archives.check_versions(rows, "windows", "new"), [])

    def test_delayed_run_does_not_flag_newer_release(self):
        rows = [version("newer", 11), version("new", 10), version("old", 9, "archived")]
        self.assertEqual(archives.check_versions(rows, "windows", "new"), [])

    def test_missing_or_inactive_upload_and_wrong_platform_fail(self):
        for rows in ([], [version("other", 1)], [version("new", 1, "archived")],
                     [version("new", 1, file_id="linux")]):
            with self.assertRaises(ValueError):
                archives.check_versions(rows, "windows", "new")

    def test_decimal_positions_are_supported(self):
        rows = [version("old", "1.5"), version("new", "2.5")]
        self.assertEqual(archives.check_versions(rows, "windows", "new"), [rows[0]])

    def test_cleanup_is_followed_by_readback(self):
        old = version("old", 1)
        replies = [
            {"data": {"id": "mod", "game_id": "6412", "game_scoped_id": "244"}},
            {"data": {"mod_files": [{"id": "windows"}]}},
            {"data": {"versions": [old, version("new", 2)]}},
            {"data": {"versions": [version("old", 1, "archived"), version("new", 2)]}},
        ]
        with patch.dict(os.environ, {"NEXUSMODS_API_KEY": "test"}), \
                patch.object(archives.urllib.request, "urlopen", side_effect=[
                    io.BytesIO(json.dumps(r).encode()) for r in replies]), \
                patch.object(archives, "archive_version") as archive, \
                patch.object(archives.time, "sleep"):
            archives.verify("windows", "new")
        archive.assert_called_once_with(old)

    def test_missing_session_does_not_attempt_archive(self):
        with patch.dict(os.environ, {}, clear=True), \
                patch.object(archives.urllib.request, "build_opener") as opener:
            with self.assertRaisesRegex(ValueError, "NEXUSMODS_SESSION_COOKIE"):
                archives.archive_version(version("123", 1))
            opener.assert_not_called()

    def test_successful_archive_request_cannot_hide_failed_readback(self):
        replies = [
            {"data": {"id": "mod", "game_id": "6412", "game_scoped_id": "244"}},
            {"data": {"mod_files": [{"id": "windows"}]}},
            {"data": {"versions": [version("1599", 1), version("new", 2)]}},
        ]
        with patch.dict(os.environ, {"NEXUSMODS_API_KEY": "test"}), \
                patch.object(archives.urllib.request, "urlopen", side_effect=[
                    io.BytesIO(json.dumps(r).encode()) for r in replies]), \
                patch.object(archives, "archive_version"):
            with self.assertRaisesRegex(ValueError, "1599"):
                archives.verify("windows", "new", attempts=1)

    def test_archive_uses_game_scoped_id_and_supplied_endpoint(self):
        endpoint = "https://www.nexusmods.com/api/flamework/mods/archive-file"
        with patch.dict(os.environ, {"NEXUSMODS_SESSION_COOKIE": "session=test"}), \
                patch.object(archives.urllib.request, "build_opener") as opener:
            opener.return_value.open.return_value.__enter__.return_value.geturl.return_value = endpoint
            row = version("v3-id", 1)
            row["game_scoped_id"] = "1599"
            archives.archive_version(row)
        request = opener.return_value.open.call_args.args[0]
        self.assertEqual(request.full_url, endpoint)
        self.assertEqual(json.loads(request.data), {"fileId": 1599, "gameId": 6412, "modId": 244})


if __name__ == "__main__":
    unittest.main()
