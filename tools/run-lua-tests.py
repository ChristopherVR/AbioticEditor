"""Run the live-agent Lua test harness without a standalone Lua executable.

The harness (live-agent/AbioticEditorLiveAgentLua/tests/run.lua) normally runs under a real
Lua 5.4 interpreter; the .NET wrapper test skips when none is on PATH. This script runs the
same unmodified harness through the `lupa` Python binding (Lua 5.4 compiled in) instead:

    pip install lupa
    python tools/run-lua-tests.py

If `lupa` is not installed but an unpacked wheel exists at artifacts/lua-runtime (a local,
untracked folder), that copy is used. Exit code 0 means every check passed.
"""
from __future__ import annotations

import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUNNER = "live-agent/AbioticEditorLiveAgentLua/tests/run.lua"


def load_lupa():
    try:
        import lupa.lua54 as lua54  # type: ignore

        return lua54
    except ImportError:
        pass
    fallback = os.path.join(REPO_ROOT, "artifacts", "lua-runtime")
    if os.path.isdir(fallback):
        sys.path.insert(0, fallback)
        import lupa.lua54 as lua54  # type: ignore

        return lua54
    sys.stderr.write("lupa is not installed. Run: pip install lupa\n")
    sys.exit(2)


def main() -> int:
    lua54 = load_lupa()
    os.chdir(REPO_ROOT)
    runtime = lua54.LuaRuntime(unpack_returned_tuples=True)
    runtime.execute("arg = {[0] = '%s'}" % RUNNER)
    # run.lua ends with os.exit(code); capture it instead of killing this process.
    runtime.execute("os.exit = function(code) _G.__abiotic_exit = code end")
    runtime.execute("dofile('%s')" % RUNNER)
    code = runtime.eval("_G.__abiotic_exit")
    return 0 if code in (0, True, None) else 1


if __name__ == "__main__":
    sys.exit(main())
