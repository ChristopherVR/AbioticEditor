import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("verdicts", Path(__file__).parents[1] / "virustotal-results.py")
verdicts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verdicts)


class VerdictTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.previous = Path.cwd()
        os.chdir(self.temp.name)
        Path("uploads.txt").write_text("dist/package.zip=https://www.virustotal.com/gui/file-analysis/test-id/detection\n")

    def tearDown(self):
        os.chdir(self.previous)
        self.temp.cleanup()

    def run_result(self, stats):
        response = io.BytesIO(json.dumps({"data": {"attributes": {"status": "completed", "stats": stats}}}).encode())
        with patch.dict(os.environ, {"VT_API_KEY": "test-key"}), \
                patch.object(verdicts.urllib.request, "urlopen", return_value=response), \
                patch.object(verdicts.time, "sleep"):
            verdicts.collect("uploads.txt", "results.md")

    def test_completed_clean_report_retains_timeout_and_unsupported_counts(self):
        self.run_result({"malicious": 0, "suspicious": 0, "undetected": 61, "timeout": 5, "type-unsupported": 7})
        self.assertEqual(Path("vt_detected.txt").read_text(), "false")
        self.assertIn("5 timeout", Path("results.md").read_text())
        self.assertIn("7 type-unsupported", Path("results.md").read_text())

    def test_malicious_and_suspicious_results_both_mark_detection(self):
        for category in ("malicious", "suspicious"):
            self.run_result({category: 1, "undetected": 60})
            self.assertEqual(Path("vt_detected.txt").read_text(), "true")
            self.assertIn(f"1 {category}", Path("results.md").read_text())

    def test_deadline_never_claims_clean_result(self):
        verdicts.collect("uploads.txt", "results.md", timeout=0)
        self.assertIn("no clean verdict confirmed", Path("results.md").read_text())

    def test_service_failure_never_claims_clean_result(self):
        with patch.dict(os.environ, {"VT_API_KEY": "test-key"}), \
                patch.object(verdicts.urllib.request, "urlopen", side_effect=verdicts.urllib.error.URLError("unavailable")), \
                patch.object(verdicts.time, "monotonic", side_effect=[0, 0, 0, 2]), \
                patch.object(verdicts.time, "sleep"):
            verdicts.collect("uploads.txt", "results.md", timeout=1)
        self.assertIn("no clean verdict confirmed", Path("results.md").read_text())

    def test_queued_analysis_is_polled_until_completed(self):
        replies = [io.BytesIO(json.dumps({"data": {"attributes": attributes}}).encode())
                   for attributes in ({"status": "queued"},
                                      {"status": "completed", "stats": {"malicious": 1}})]
        with patch.dict(os.environ, {"VT_API_KEY": "test-key"}), \
                patch.object(verdicts.urllib.request, "urlopen", side_effect=replies) as request, \
                patch.object(verdicts.time, "sleep"):
            verdicts.collect("uploads.txt", "results.md")
        self.assertEqual(request.call_count, 2)
        self.assertEqual(Path("vt_detected.txt").read_text(), "true")


if __name__ == "__main__":
    unittest.main()
