#!/usr/bin/env bash
# Verify the self-contained Linux publish layout and the running local-only health endpoint.
set -euo pipefail

publish_dir="${1:?Usage: verify-web-host-linux.sh <publish-dir> [--layout-only]}"
# --layout-only checks the layout without launching the host. Used for the second (Nexus Mods)
# publish in the same job: the smoke test binds a fixed port, so running it twice in one job
# collides with the still-shutting-down first instance. The two builds differ only by the
# updater strip, so the full run against the standard build already covers the behaviour.
layout_only="${2:-}"
publish_dir="$(cd -- "$publish_dir" && pwd)"
# Single-file publish: the managed assemblies and Photino native library live inside the
# executable, so only run-time data is expected beside it.
for required in AbioticEditor.Web Mappings.usmap registry wiki THIRD-PARTY-NOTICES.txt launch-linux.sh install-linux-desktop.sh wwwroot wwwroot/AbioticEditor.Web.staticwebassets.endpoints.json Templates/blank-world-template.sav Templates/blank-player-template.sav; do
  test -e "$publish_dir/$required" || { echo "Published Linux host is missing '$required'." >&2; exit 1; }
done

chmod +x "$publish_dir/launch-linux.sh"
bash -n "$publish_dir/launch-linux.sh"
bash -n "$publish_dir/install-linux-desktop.sh"
grep -q 'ABIOTIC_EDITOR_URL="\$url"' "$publish_dir/launch-linux.sh"
grep -q 'ABIOTIC_EDITOR_NO_DESKTOP=1' "$publish_dir/launch-linux.sh"
grep -q 'libwebkit2gtk-4.1-0' "$publish_dir/launch-linux.sh"

if [[ "$layout_only" == "--layout-only" ]]; then
  echo "Linux publish layout checks passed (layout only): $publish_dir"
  exit 0
fi
grep -q 'webkit2gtk-4.1 gtk3 libnotify kdialog' "$publish_dir/launch-linux.sh"

# Reject any endpoint that could expose selected local save paths to the network.
unsafe_log="$(mktemp)"
if (cd "$publish_dir" && ABIOTIC_EDITOR_URL=http://0.0.0.0:37246 ./launch-linux.sh --headless) >"$unsafe_log" 2>&1; then
  cat "$unsafe_log" >&2
  echo "Unsafe endpoint was accepted." >&2
  rm -f -- "$unsafe_log"
  exit 1
fi
grep -q 'loopback URL' "$unsafe_log"
rm -f -- "$unsafe_log"

# The menu entry is per-user and must launch this exact local-only launcher.
desktop_data="$(mktemp -d)"
desktop_file="$desktop_data/applications/abiotic-editor-web.desktop"
cleanup_desktop() { rm -rf -- "$desktop_data"; }
trap cleanup_desktop EXIT
chmod +x "$publish_dir/install-linux-desktop.sh"
(cd "$publish_dir" && XDG_DATA_HOME="$desktop_data" ./install-linux-desktop.sh)
test -f "$desktop_file" || { echo "Desktop installer did not create an entry." >&2; exit 1; }
grep -Fq "Exec=\"$publish_dir/launch-linux.sh\"" "$desktop_file"
(cd "$publish_dir" && XDG_DATA_HOME="$desktop_data" ./install-linux-desktop.sh --uninstall)
test ! -e "$desktop_file" || { echo "Desktop uninstaller did not remove its entry." >&2; exit 1; }
port=37262
url="http://127.0.0.1:$port"
log="$(mktemp)"
launcher_pid=""
cleanup() {
  if [[ -n "$launcher_pid" ]] && kill -0 "$launcher_pid" 2>/dev/null; then kill "$launcher_pid" 2>/dev/null || true; wait "$launcher_pid" 2>/dev/null || true; fi
  rm -f -- "$log"
  if [[ -n "${window_marker:-}" ]]; then rm -f -- "$window_marker"; fi
  cleanup_desktop
}
trap cleanup EXIT

(cd "$publish_dir" && ABIOTIC_EDITOR_URL="$url" ./launch-linux.sh --headless) >"$log" 2>&1 &
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
  echo "Published Linux host did not pass /healthz within 10 seconds." >&2
  exit 1
fi

check_asset() {
  local asset_path="$1"
  local asset_file
  asset_file="$(mktemp)"
  if ! curl --fail --silent --show-error --max-time 3 "$url$asset_path" --output "$asset_file" || [[ ! -s "$asset_file" ]]; then
    rm -f -- "$asset_file"
    echo "Published Linux host did not serve a non-empty $asset_path response." >&2
    exit 1
  fi
  rm -f -- "$asset_file"
}

for asset in / /parity.css /AbioticEditor.Web.styles.css /fonts/Digital7.ttf /fonts/MaterialSymbolsOutlined.ttf /fonts/OpenSans-Regular.ttf /fonts/OpenSans-Semibold.ttf /images/abiotic-factor.png /_framework/blazor.web.js; do
  check_asset "$asset"
done
kill "$launcher_pid"
wait "$launcher_pid" 2>/dev/null || true
launcher_pid=""

# Exercise the real WebKitGTK window under a virtual display in CI. This catches
# missing Photino native assets and missing Linux desktop libraries before release.
if command -v xvfb-run >/dev/null 2>&1; then
  port=37263
  url="http://127.0.0.1:$port"
  : >"$log"
  window_marker="$(mktemp)"
  rm -f -- "$window_marker"
  if command -v xdotool >/dev/null 2>&1; then
    (cd "$publish_dir" && ABIOTIC_EDITOR_URL="$url" WINDOW_MARKER="$window_marker" xvfb-run -a bash -c '
      ./launch-linux.sh & app_pid=$!
      for _ in $(seq 1 100); do
        if xdotool search --onlyvisible --name "Abiotic Editor" >/dev/null 2>&1; then
          : >"$WINDOW_MARKER"
          break
        fi
        kill -0 "$app_pid" 2>/dev/null || exit 1
        sleep 0.1
      done
      wait "$app_pid"
    ') >"$log" 2>&1 &
  else
    (cd "$publish_dir" && ABIOTIC_EDITOR_URL="$url" xvfb-run -a ./launch-linux.sh) >"$log" 2>&1 &
  fi
  launcher_pid=$!
  desktop_healthy=false
  for _ in $(seq 1 100); do
    if curl --fail --silent --max-time 1 "$url/healthz" | grep -q '"status":"ok"'; then
      desktop_healthy=true
      break
    fi
    if ! kill -0 "$launcher_pid" 2>/dev/null; then cat "$log" >&2; exit 1; fi
    sleep 0.1
  done
  if command -v xdotool >/dev/null 2>&1; then
    for _ in $(seq 1 100); do
      [[ -e "$window_marker" ]] && break
      kill -0 "$launcher_pid" 2>/dev/null || break
      sleep 0.1
    done
  else
    sleep 1
  fi
  if ! $desktop_healthy || ! kill -0 "$launcher_pid" 2>/dev/null; then
    cat "$log" >&2
    echo "Published Linux desktop window did not remain healthy." >&2
    exit 1
  fi
  grep -q 'Opening Abiotic Editor desktop window' "$log"
  if command -v xdotool >/dev/null 2>&1 && [[ ! -e "$window_marker" ]]; then
    cat "$log" >&2
    echo "Published Linux desktop process did not map an Abiotic Editor window." >&2
    exit 1
  fi
  rm -f -- "$window_marker"
fi

echo "Linux desktop publish, native window, health, and UI asset smoke tests passed: $publish_dir"
