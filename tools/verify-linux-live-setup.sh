#!/usr/bin/env bash
# Verifies live editing's Linux/Proton path end to end against a FAKE Steam library + a real
# Wine prefix, without needing a real game install or a real Proton session. Mirrors exactly what
# ProtonLiveAgentEnvironment (src/AbioticEditor.Core/Infrastructure/LiveEditing) and LiveAgentSetup
# (src/AbioticEditor.Web.Shared/Services) do when the desktop host launches the bundled native
# helper (live-agent/AbioticEditorLiveAgentHelper) through Wine on Linux:
#   - a Steam library shaped like <library>/steamapps/common/AbioticFactor/AbioticFactor/...
#   - a Proton-style prefix at <library>/steamapps/compatdata/427410/pfx
#   - the helper launched with WINEPREFIX=<prefix> and LOCALAPPDATA=C:\users\steamuser\AppData\Local
#   - the helper expected to write its token.txt/port.txt under
#     <prefix>/drive_c/users/steamuser/AppData/Local/AbioticEditorLiveAgent
#
# Usage: tools/verify-linux-live-setup.sh [work-dir]
#   work-dir defaults to a fresh mktemp -d and is left in place on exit so its logs/prefix can be
#   inspected afterwards; pass one explicitly to reuse it across runs (much faster: wineboot only
#   needs to run once per prefix).
#
# Needs `wine` on PATH (no sudo, no system packages installed by this script). Every check is
# best-effort and reports what it found rather than hard-failing the whole run, since this is a
# diagnostic tool first and a pass/fail gate second - read the summary at the end.
#
# Known issue this script exists to catch (see docs/PROGRESS.md's Linux live-editing round and the
# "NOTE (Linux/Proton, unverified against a real install)" comments in LiveAgentSetup.cs): on a
# plain distro `wine` package (Ubuntu 22.04's wine 6.0.3, as installed by the error message
# LiveAgentSetup itself prints - "Install your distro's 'wine' package"), the helper's own
# GetEnvironmentVariableA("LOCALAPPDATA") call (live-agent/AbioticEditorLiveAgentHelper/src/TokenStore.h)
# does NOT reliably return the literal value BuildLinuxWineStartInfo passes in
# (C:\users\steamuser\AppData\Local): reproduced 3/3 runs, the helper instead wrote its token/port
# files under the legacy "Local Settings\Application Data" folder name for whichever profile Wine
# had already registered as the prefix's current user - even with FORCE_STEAMUSER=1 forcing
# USER/WINEUSER on the wine process. Either way the result is the same:
# DesktopLiveEditingCapability's fixed lookup path silently finds nothing. Whether this also
# reproduces on the much newer Wine build inside a real Steam Play/Proton prefix is unverified -
# rerun this script's checks against a real compatdata/427410/pfx once one exists, or add
# `ABIOTIC_LIVE_WINE=/path/to/proton's/wine` support to this script if that becomes useful. Set
# FORCE_STEAMUSER=1 to also pass USER=steamuser/WINEUSER=steamuser, kept here as a secondary check
# even though it did not change the outcome in testing.
set -uo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
work_dir="${1:-$(mktemp -d)}"
mkdir -p "$work_dir"
work_dir="$(cd -- "$work_dir" && pwd)"

helper_exe="$repo_root/live-agent/AbioticEditorLiveAgentHelper/build/Release/AbioticEditorLiveAgentHelper.exe"
app_id=427410
library_root="$work_dir/fakesteam"
game_root="$library_root/steamapps/common/AbioticFactor/AbioticFactor"
prefix_root="$library_root/steamapps/compatdata/$app_id/pfx"

fail=0
note() { echo "  $*"; }
check() { echo "== $* =="; }
pass() { echo "  PASS: $*"; }
bug()  { echo "  BUG:  $*"; fail=1; }
skip() { echo "  SKIP: $*"; }

echo "Repo root:    $repo_root"
echo "Work dir:     $work_dir"
echo "Helper exe:   $helper_exe"
echo "Steam library: $library_root"
echo

check "Prerequisites"
if ! command -v wine >/dev/null 2>&1; then
  skip "no 'wine' on PATH - install your distro's wine package (or set ABIOTIC_LIVE_WINE) and rerun."
  exit 0
fi
pass "wine found: $(command -v wine) ($(wine --version 2>&1))"
if [[ ! -f "$helper_exe" ]]; then
  skip "helper binary missing at $helper_exe - build it first (see live-agent/README.md), or copy one in."
  exit 0
fi
pass "helper binary present"

check "Fake Steam library layout"
mkdir -p "$game_root/Content/Paks"
mkdir -p "$game_root/Binaries/Win64/ue4ss/Mods/shared/UEHelpers"
[[ -e "$game_root/Content/Paks/pakchunk0-WindowsNoEditor.pak" ]] || : > "$game_root/Content/Paks/pakchunk0-WindowsNoEditor.pak"
[[ -e "$game_root/Binaries/Win64/AbioticFactor-Win64-Shipping.exe" ]] || : > "$game_root/Binaries/Win64/AbioticFactor-Win64-Shipping.exe"
[[ -e "$game_root/Binaries/Win64/ue4ss/UE4SS.dll" ]] || : > "$game_root/Binaries/Win64/ue4ss/UE4SS.dll"
[[ -e "$game_root/Binaries/Win64/ue4ss/Mods/shared/UEHelpers/UEHelpers.lua" ]] || echo "-- fake --" > "$game_root/Binaries/Win64/ue4ss/Mods/shared/UEHelpers/UEHelpers.lua"
mkdir -p "$library_root/steamapps/compatdata"
pass "laid out at $game_root (matches AfInstallLocator.ResolvePaksDirectory / GameInstallLocator.Resolve's Steam shape)"

check "Wine prefix init (steamuser profile, matching Proton's fixed account name)"
export WINEPREFIX="$prefix_root"
export WINEDEBUG=-all
export WINEARCH=win64
if [[ -d "$prefix_root/drive_c/users/steamuser" ]]; then
  pass "prefix already initialised at $prefix_root"
else
  mkdir -p "$prefix_root"
  USER=steamuser wineboot -u >/dev/null 2>&1
  if [[ -d "$prefix_root/drive_c/users/steamuser" ]]; then
    pass "wineboot -u created $prefix_root/drive_c/users/steamuser"
  else
    bug "wineboot -u did not create a 'steamuser' profile - it may have used the host's own \$USER instead (see FORCE_STEAMUSER below)."
  fi
fi

check "Launching the real helper through Wine exactly as BuildLinuxWineStartInfo does"
# See src/AbioticEditor.Web.Shared/Services/LiveAgentSetup.cs BuildLinuxWineStartInfo: only
# WINEPREFIX and LOCALAPPDATA are set on the child process today. FORCE_STEAMUSER=1 additionally
# forces USER/WINEUSER, which is what a real Proton launch effectively guarantees but a raw `wine`
# binary does not - flip it to see whether that closes the gap this script is checking for.
env_args=(WINEPREFIX="$prefix_root" LOCALAPPDATA='C:\users\steamuser\AppData\Local')
if [[ "${FORCE_STEAMUSER:-0}" == "1" ]]; then
  env_args+=(USER=steamuser WINEUSER=steamuser)
  note "FORCE_STEAMUSER=1: also passing USER=steamuser WINEUSER=steamuser"
fi
log_file="$work_dir/helper-launch.log"
env "${env_args[@]}" wine "$helper_exe" >"$log_file" 2>&1 &
helper_pid=$!
sleep 5

check "Process visibility"
if kill -0 "$helper_pid" 2>/dev/null; then
  pass "the wine wrapper process (pid $helper_pid) is still alive"
else
  bug "the wine wrapper process exited early - see $log_file"
fi
ps_rows="$(ps -eo pid,ppid,comm,args | grep -iE 'wine|AbioticEditorLiveAgentHelper' | grep -v grep || true)"
echo "$ps_rows" | sed 's/^/  /'
comm_name="$(ps -eo comm | grep -i '^AbioticEditorLi' || true)"
if [[ -n "$comm_name" ]]; then
  note "kernel comm name for the helper is truncated to 15 bytes: '$comm_name' (not the full 'AbioticEditorLiveAgentHelper')"
  bug "Process.GetProcessesByName(\"AbioticEditorLiveAgentHelper\") (LiveAgentSetup.IsHelperRunning) will NOT match this - it does an exact-name lookup, and the truncated comm name never equals the full one. A second EnsureReadyAsync call will think no helper is running and relaunch a duplicate."
fi

check "TCP listener"
ss_rows="$(ss -ltnp 2>/dev/null | grep -E ':(42117|4211[0-9])\b' || true)"
if [[ -n "$ss_rows" ]]; then
  pass "something is listening on the helper's port range:"
  echo "$ss_rows" | sed 's/^/  /'
else
  bug "nothing found listening on port 42117 (LiveAgentSetup.DefaultPort) - see $log_file"
fi

check "Where did the helper actually write its token/port files?"
expected_dir="$prefix_root/drive_c/users/steamuser/AppData/Local/AbioticEditorLiveAgent"
if [[ -f "$expected_dir/token.txt" ]]; then
  pass "token.txt found where DesktopLiveEditingCapability expects it: $expected_dir"
else
  bug "token.txt NOT found at the expected path: $expected_dir"
  actual="$(find "$prefix_root/drive_c/users" -iname 'token.txt' 2>/dev/null | head -1)"
  if [[ -n "$actual" ]]; then
    note "it actually landed here instead: $actual"
    note "DesktopLiveEditingCapability.TryReadLocalToken()/TryReadLocalPort() will silently return null / the hardcoded default port from this mismatch, not an error - a player would just see live editing fail to auto-connect with no obvious cause."
  else
    note "not found anywhere under $prefix_root/drive_c/users - the helper may not have started at all; check $log_file"
  fi
fi

kill "$helper_pid" 2>/dev/null || true
wait "$helper_pid" 2>/dev/null || true

echo
if [[ "$fail" == "0" ]]; then
  echo "All checks passed."
else
  echo "One or more BUGs were found above - this is expected on a machine using plain Wine (see FORCE_STEAMUSER=1 to test the workaround) until LiveAgentSetup.BuildLinuxWineStartInfo / ProtonLiveAgentEnvironment are hardened against it."
fi
echo "Work dir left at $work_dir for inspection (helper log, wine prefix, fake library)."
