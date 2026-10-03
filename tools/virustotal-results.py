"""Wait for existing VirusTotal uploads and record completed verdicts, never upload files."""
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.request


def collect(results_path, verdict_path, timeout=3000):
    pending = {}
    for line in Path(results_path).read_text().splitlines():
        name, url = line.split("=", 1)
        analysis_id = url.split("/file-analysis/", 1)[1].split("/", 1)[0]
        pending[name] = analysis_id
    verdicts = {}
    detected = False
    deadline = time.monotonic() + timeout
    while pending and time.monotonic() < deadline:
        for name, analysis_id in list(pending.items()):
            if time.monotonic() >= deadline:
                break
            request = urllib.request.Request(
                "https://www.virustotal.com/api/v3/analyses/" + analysis_id,
                headers={"x-apikey": os.environ["VT_API_KEY"]},
            )
            try:
                with urllib.request.urlopen(request, timeout=30) as response:
                    attributes = json.load(response)["data"]["attributes"]
                if attributes["status"] == "completed":
                    stats = attributes["stats"]
                    hits = stats.get("malicious", 0) + stats.get("suspicious", 0)
                    detected |= hits > 0
                    verdicts[name] = ", ".join(f"{count} {category}" for category, count in sorted(stats.items()))
                    del pending[name]
            except (urllib.error.URLError, TimeoutError, KeyError, ValueError, TypeError):
                # An unavailable result is explicitly reported; it never becomes a clean verdict.
                pass
            time.sleep(20)  # At most three requests per minute on the free API.
    for name in pending:
        verdicts[name] = "Analysis unavailable or still pending; no clean verdict confirmed."
    Path(verdict_path).write_text(
        "\nCompleted analysis results (timeouts and unsupported engines remain visible):\n\n"
        + "\n".join(f"- `{Path(name).name}`: {result}" for name, result in verdicts.items()) + "\n"
    )
    Path("vt_detected.txt").write_text("true" if detected else "false")


if __name__ == "__main__":
    collect("vt_results.txt", "vt_verdicts.md")
