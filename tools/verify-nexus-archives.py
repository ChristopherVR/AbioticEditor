"""Archive leftover predecessors and verify a successful Nexus platform upload."""

import json
from decimal import Decimal
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def check_versions(versions, file_id, uploaded_id):
    if not versions or any(str(v["file"]["id"]) != file_id for v in versions):
        raise ValueError("Nexus returned an empty or mismatched file history")
    uploaded = [v for v in versions if str(v["id"]) == uploaded_id]
    if len(uploaded) != 1 or uploaded[0]["category"] != "main":
        raise ValueError("The uploaded version is missing or is not an active main file")
    # Check only predecessors in this platform's chain. A delayed workflow must
    # never demand archiving a newer release or the other platform's current file.
    position = Decimal(uploaded[0]["position"])
    return [v for v in versions if Decimal(v["position"]) < position
            and v["category"] not in {"archived", "removed"}]


def archive_version(version):
    cookie = os.environ.get("NEXUSMODS_SESSION_COOKIE", "").strip()
    if not cookie:
        raise ValueError("Older Nexus files remain active. Set the NEXUSMODS_SESSION_COOKIE "
                         "repository secret to enable browser-authenticated archive cleanup.")
    request = urllib.request.Request(
        "https://www.nexusmods.com/api/flamework/mods/archive-file",
        data=json.dumps({"fileId": int(version["game_scoped_id"]),
                         "gameId": 6412, "modId": 244}).encode(),
        headers={"Cookie": cookie, "Content-Type": "application/json",
                 "Accept": "application/json", "Origin": "https://www.nexusmods.com",
                 "Referer": "https://www.nexusmods.com/games/abioticfactor/mods/244/edit/files"},
        method="POST",
    )
    with urllib.request.build_opener(NoRedirect).open(request, timeout=30) as response:
        # A login redirect is not a successful archive. The authoritative check
        # is the API readback below; never log the session or response contents.
        if response.geturl() != request.full_url:
            raise ValueError("Nexus archive request redirected; refresh NEXUSMODS_SESSION_COOKIE")
    print(f"Requested archive for older Nexus file {version['game_scoped_id']}.")


def verify(file_id, uploaded_id, attempts=6):
    if not os.environ.get("NEXUSMODS_SESSION_COOKIE", "").strip():
        print("Nexus archive cleanup skipped: no session cookie configured; "
              "keeping the upload action's automatic archive behavior.")
        return
    if not file_id or not uploaded_id:
        raise ValueError("Both the platform file ID and uploaded version ID are required")
    request = urllib.request.Request(
        "https://api.nexusmods.com/v3/mod-files/"
        + urllib.parse.quote(file_id, safe="") + "/versions",
        headers={"apikey": os.environ["NEXUSMODS_API_KEY"],
                 "User-Agent": "AbioticEditor-release", "Accept": "application/json"},
    )
    # Validate the configured chain belongs to this mod before using site-scoped IDs.
    metadata_request = urllib.request.Request(
        "https://api.nexusmods.com/v3/games/abioticfactor/mods/244",
        headers=dict(request.header_items()),
    )
    with urllib.request.urlopen(metadata_request, timeout=30) as response:
        mod = json.load(response)["data"]
    if str(mod["game_scoped_id"]) != "244" or str(mod["game_id"]) != "6412":
        raise ValueError("Nexus returned the wrong mod")
    files_request = urllib.request.Request(
        "https://api.nexusmods.com/v3/mods/" + urllib.parse.quote(str(mod["id"]), safe="") + "/files",
        headers=dict(request.header_items()),
    )
    with urllib.request.urlopen(files_request, timeout=30) as response:
        files = json.load(response)["data"]["mod_files"]
    if not any(str(f["id"]) == file_id for f in files):
        raise ValueError("Configured Nexus file does not belong to Abiotic Factor mod 244")
    requested = set()
    for attempt in range(attempts):
        with urllib.request.urlopen(request, timeout=30) as response:
            versions = json.load(response)["data"]["versions"]
        pending = check_versions(versions, file_id, uploaded_id)
        if not pending:
            print("Nexus archive verification passed: all older platform versions are archived or removed.")
            return
        for version in pending:
            if version["id"] not in requested:
                archive_version(version)
                requested.add(version["id"])
        if attempt + 1 < attempts:
            time.sleep(10)
    # IDs are sufficient for remediation; do not print response bodies or credentials.
    ids = ", ".join(str(v["game_scoped_id"]) for v in pending)
    raise ValueError(f"Nexus left older versions unarchived (game-scoped file IDs: {ids}). "
                     "Archive these files on Nexus; the upload's automatic archive request did not clean them up.")


if __name__ == "__main__":
    try:
        verify(*sys.argv[1:])
    except (ValueError, KeyError, urllib.error.URLError) as error:
        print(f"::error::{error}", file=sys.stderr)
        sys.exit(1)
