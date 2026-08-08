#!/usr/bin/env bash
# Verify the self-contained macOS publish layout and the running local-only health endpoint.
# Pass --layout-only for a cross-architecture publish the runner cannot execute.
set -euo pipefail

publish_dir="${1:?Usage: verify-web-host-mac.sh <publish-dir> [--layout-only]}"
layout_only="${2:-}"
publish_dir="$(cd -- "$publish_dir" && pwd)"
# Single-file publish: the managed assemblies and Photino native library live inside the
# executable, so only run-time data is expected beside it.
for required in AbioticEditor.Web Mappings.usmap registry wiki THIRD-PARTY-NOTICES.txt launch-mac.sh wwwroot wwwroot/AbioticEditor.Web.staticwebassets.endpoints.json Templates/blank-world-template.sav Templates/blank-player-template.sav; do
  test -e "$publish_dir/$required" || { echo "Published macOS host is missing '$required'." >&2; exit 1; }
done

chmod +x "$publish_dir/launch-mac.sh"
bash -n "$publish_dir/launch-mac.sh"
grep -q 'ABIOTIC_EDITOR_URL="\$url"' "$publish_dir/launch-mac.sh"
grep -q 'ABIOTIC_EDITOR_NO_DESKTOP=1' "$publish_dir/launch-mac.sh"
grep -q 'com.apple.quarantine' "$publish_dir/launch-mac.sh"

if [[ "$layout_only" == "--layout-only" ]]; then
  echo "macOS publish layout checks passed (layout only): $publish_dir"
  exit 0
fi

# Reject any endpoint that could expose selected local save paths to the network.
unsafe_log="$(mktemp)"
if (cd "$publish_dir" && ABIOTIC_EDITOR_URL=http://0.0.0.0:37246 ./launch-mac.sh --headless) >"$unsafe_log" 2>&1; then
  cat "$unsafe_log" >&2
  echo "Unsafe endpoint was accepted." >&2
  rm -f -- "$unsafe_log"
  exit 1
fi
grep -q 'loopback URL' "$unsafe_log"
rm -f -- "$unsafe_log"

port=37262
url="http://127.0.0.1:$port"
log="$(mktemp)"
launcher_pid=""
cleanup() {
  if [[ -n "$launcher_pid" ]] && kill -0 "$launcher_pid" 2>/dev/null; then kill "$launcher_pid" 2>/dev/null || true; wait "$launcher_pid" 2>/dev/null || true; fi
  rm -f -- "$log"
}
trap cleanup EXIT

(cd "$publish_dir" && ABIOTIC_EDITOR_URL="$url" ./launch-mac.sh --headless) >"$log" 2>&1 &
launcher_pid=$!
headless_healthy=false
for _ in $(seq 1 100); do
  if curl --fail --silent --max-time 1 "$url/healthz" | grep -q '"status":"ok"'; then
    headless_healthy=true
    break
  fi
  if ! kill -0 "$launcher_pid" 2>/dev/null; then cat "$log" >&2; exit 1; fi
  sleep 0.1
done
if ! $headless_healthy; then
  cat "$log" >&2
  echo "Published macOS host did not pass /healthz within 10 seconds." >&2
  exit 1
fi

check_asset() {
  local asset_path="$1"
  local asset_file
  asset_file="$(mktemp)"
  if ! curl --fail --silent --show-error --max-time 3 "$url$asset_path" --output "$asset_file" || [[ ! -s "$asset_file" ]]; then
    rm -f -- "$asset_file"
    echo "Published macOS host did not serve a non-empty $asset_path response." >&2
    exit 1
  fi
  rm -f -- "$asset_file"
}

# The look-and-feel files live in the shared screen library, so they are served from
# _content/<library>/ rather than the host's own root; only the scoped-CSS bundle (named
# after this app) and the Blazor framework files sit at the root. Checking the old root
# paths passed for as long as those files lived here and then failed the moment they moved,
# even though the app itself was serving them correctly the whole time.
for asset in / /_content/AbioticEditor.Web.Shared/parity.css /AbioticEditor.Web.styles.css /_content/AbioticEditor.Web.Shared/fonts/Digital7.ttf /_content/AbioticEditor.Web.Shared/fonts/MaterialSymbolsOutlined.ttf /_content/AbioticEditor.Web.Shared/fonts/OpenSans-Regular.ttf /_content/AbioticEditor.Web.Shared/fonts/OpenSans-Semibold.ttf /_content/AbioticEditor.Web.Shared/images/abiotic-factor.png /_framework/blazor.web.js; do
  check_asset "$asset"
done

echo "macOS publish, health, and UI asset smoke tests passed: $publish_dir"
