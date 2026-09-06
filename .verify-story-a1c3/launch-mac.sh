#!/usr/bin/env bash
# Launch the self-contained editor in its native desktop window on macOS.
set -euo pipefail

app_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
url="${ABIOTIC_EDITOR_URL:-http://127.0.0.1:37246}"

if [[ "${1:-}" == "--headless" ]]; then
  export ABIOTIC_EDITOR_NO_DESKTOP=1
  shift
fi

if [[ $# -ne 0 ]]; then
  echo "Usage: $(basename "$0") [--headless]" >&2
  exit 64
fi

# The download is not notarized, so macOS quarantines every file in the zip and
# Gatekeeper would refuse to start the app. Clearing the quarantine flag on the
# app folder is the standard way to run an open-source tool you chose to download.
xattr -dr com.apple.quarantine "$app_dir" 2>/dev/null || true

chmod +x "$app_dir/AbioticEditor.Web"

cd "$app_dir"
ABIOTIC_EDITOR_URL="$url" exec "$app_dir/AbioticEditor.Web"
