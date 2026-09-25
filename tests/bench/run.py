"""Time the mod's work per update, see Bench.cs.

    python tests/bench/run.py [runs]

Runs under Mono when `mono` is on PATH, since that is what the game runs on, and under .NET when the
SDK is. Numbers are microseconds per call, from a stand-in game whose own calls cost nothing, so they
measure the mod's overhead rather than what a turn costs in the real game.
"""

import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(REPO))
import build as b  # noqa: E402


def sources():
    return [REPO / "tests/stubs/Game.cs", b.DIST / "QudHUD/QudHUD.cs", HERE / "Bench.cs"]


def under_mono(tmp, runs):
    mono = shutil.which("mono")
    if not mono:
        return None
    lib = Path(mono).resolve().parent.parent / "lib/mono/4.5"
    csc = shutil.which("csc") or shutil.which("mcs")
    refs = [lib / n for n in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    exe = tmp / "bench-mono.exe"
    ok, out = b.compile_cs([csc], sources(), refs, exe, ["-langversion:5", "-optimize+"], target="exe")
    if not ok:
        return "compile failed:\n" + out
    r = subprocess.run([mono, "--optimize=all", str(exe), str(runs)], capture_output=True, text=True)
    return r.stdout + r.stderr[-1500:]


def under_dotnet(tmp, runs):
    dotnet = shutil.which("dotnet")
    if not dotnet:
        return None
    root = Path(dotnet).resolve().parent
    csc = sorted(root.glob("sdk/*/Roslyn/bincore/csc.dll"))
    if not csc:
        return None
    compiler = [dotnet, "exec", str(csc[-1])]
    dirs = sorted(root.glob("packs/Microsoft.NETCore.App.Ref/*/ref/net*"))
    if not dirs:
        return None
    exe = tmp / "bench.dll"
    ok, out = b.compile_cs(compiler, sources(), sorted(dirs[-1].glob("*.dll")), exe,
                           ["-langversion:5", "-optimize+"], target="exe")
    if not ok:
        return "compile failed:\n" + out
    tfm = dirs[-1].name
    (tmp / "bench.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
        "tfm": tfm, "framework": {"name": "Microsoft.NETCore.App", "version": tfm[3:] + ".0"}}}))
    r = subprocess.run([compiler[0], "exec", str(exe), str(runs)], capture_output=True, text=True)
    return r.stdout + r.stderr[-1500:]


def main():
    runs = int(sys.argv[1]) if len(sys.argv) > 1 else 300
    b.build()
    with tempfile.TemporaryDirectory() as t:
        tmp = Path(t)
        results = [r for r in (under_mono(tmp, runs), under_dotnet(tmp, runs)) if r]
        if not results:
            sys.exit("no runtime found: put mono or the .NET SDK on PATH")
        print("\n".join(results))


if __name__ == "__main__":
    main()
