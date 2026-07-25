#!/usr/bin/env bash
# Install or remove a per-user desktop entry for the self-contained desktop editor.
set -euo pipefail

app_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
desktop_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
desktop_file="$desktop_dir/abiotic-editor-web.desktop"

if [[ "${1:-}" == "--uninstall" ]]; then
  [[ $# -eq 1 ]] || { echo "Usage: $(basename "$0") [--uninstall]" >&2; exit 64; }
  rm -f -- "$desktop_file"
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$desktop_dir" >/dev/null 2>&1 || true
  echo "Removed $desktop_file"
  exit 0
fi

[[ $# -eq 0 ]] || { echo "Usage: $(basename "$0") [--uninstall]" >&2; exit 64; }
[[ -x "$app_dir/AbioticEditor.Web" ]] || { echo "Missing executable: $app_dir/AbioticEditor.Web" >&2; exit 1; }
[[ -x "$app_dir/launch-linux.sh" ]] || { echo "Missing launcher: $app_dir/launch-linux.sh" >&2; exit 1; }

mkdir -p -- "$desktop_dir"
cat > "$desktop_file" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Abiotic Editor
Comment=Abiotic Factor save editor
Exec="$app_dir/launch-linux.sh"
Terminal=false
Categories=Game;Utility;
Keywords=Abiotic;Factor;save;editor;
StartupNotify=true
EOF

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$desktop_dir" >/dev/null 2>&1 || true
echo "Installed $desktop_file"
