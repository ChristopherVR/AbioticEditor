#!/usr/bin/env bash
# Launch the self-contained editor in its native desktop window.
set -euo pipefail

app_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
url="${ABIOTIC_EDITOR_URL:-http://127.0.0.1:37246}"
headless=false

if [[ "${1:-}" == "--headless" ]]; then
  headless=true
  export ABIOTIC_EDITOR_NO_DESKTOP=1
  shift
fi

if [[ $# -ne 0 ]]; then
  echo "Usage: $(basename "$0") [--headless]" >&2
  exit 64
fi

has_library() {
  local pkg_config_name="$1"
  local soname="$2"
  if command -v pkg-config >/dev/null 2>&1 && pkg-config --exists "$pkg_config_name" 2>/dev/null; then
    return 0
  fi
  command -v ldconfig >/dev/null 2>&1 && ldconfig -p 2>/dev/null | grep -F "$soname" >/dev/null
}

if ! $headless; then
  missing=()
  has_library webkit2gtk-4.1 libwebkit2gtk-4.1.so || missing+=("WebKitGTK 4.1")
  has_library gtk+-3.0 libgtk-3.so || missing+=("GTK 3")
  has_library libnotify libnotify.so || missing+=("libnotify")
  if ! command -v zenity >/dev/null 2>&1 && ! command -v kdialog >/dev/null 2>&1; then
    missing+=("zenity or kdialog")
  fi

  if (( ${#missing[@]} > 0 )); then
    printf 'Abiotic Editor cannot open because these desktop dependencies are missing:\n' >&2
    printf '  - %s\n' "${missing[@]}" >&2
    cat >&2 <<'GUIDANCE'

Install them with your distribution's package manager, then launch the editor again:
  Ubuntu 24.04+: sudo apt install libwebkit2gtk-4.1-0 libgtk-3-0t64 libnotify4 zenity
  Fedora:        sudo dnf install webkit2gtk4.1 gtk3 libnotify zenity
  Arch/SteamOS:  sudo pacman -S webkit2gtk-4.1 gtk3 libnotify kdialog

Abiotic Editor does not install or change system packages automatically.
GUIDANCE
    exit 69
  fi
fi

export ABIOTIC_EDITOR_URL="$url"
exec "$app_dir/AbioticEditor.Web"
