#!/usr/bin/env python3
"""Builds the Qud HUD mod folder and a release zip.

  python build.py              build dist/QudHUD and dist/QudHUD-vX.Y.Z.zip
  python build.py --install    also copy the mod into your Caves of Qud Mods folder
  python build.py --install PATH   copy into PATH instead

The version comes from the VERSION file and is stamped into the manifest,
the C# code and the display page.
"""
import os, re, shutil, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DIST = ROOT / "dist"
MOD_ID = "QudHUD"


def mods_dir():
    home = Path.home()
    if sys.platform.startswith("win"):
        return home / "AppData/LocalLow/Freehold Games/CavesOfQud/Mods"
    if sys.platform == "darwin":
        return home / "Library/Application Support/com.FreeholdGames.CavesOfQud/Mods"
    return home / ".config/unity3d/Freehold Games/CavesOfQud/Mods"


def build():
    version = (ROOT / "VERSION").read_text().strip()
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        sys.exit(f"VERSION must look like 1.2.3, got {version!r}")

    html = (ROOT / "src/hud.html").read_text(encoding="utf-8").replace("__VERSION__", version)
    cs = (ROOT / "src/QudHUD.template.cs").read_text(encoding="utf-8")
    if "__HTML__" not in cs:
        sys.exit("src/QudHUD.template.cs is missing the __HTML__ placeholder")
    # The page is embedded in a C# verbatim string, where a quote is written as two quotes.
    cs = cs.replace("__HTML__", html.replace('"', '""')).replace("__VERSION__", version)

    out = DIST / MOD_ID
    if out.exists():
        shutil.rmtree(out)
    shutil.copytree(ROOT / "mod", out)
    manifest = out / "manifest.json"
    manifest.write_text(manifest.read_text(encoding="utf-8").replace("__VERSION__", version), encoding="utf-8")
    (out / "QudHUD.cs").write_text(cs, encoding="utf-8")

    zpath = DIST / f"{MOD_ID}-v{version}.zip"
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(out.rglob("*")):
            z.write(f, f.relative_to(DIST))
    print(f"Built {MOD_ID} v{version}")
    print(f"  folder: {out}")
    print(f"  zip:    {zpath}")
    return out


def install(src, target):
    dest = Path(target) / MOD_ID
    # Keep the Workshop link the game's uploader wrote, if any.
    keep = dest / "workshop.json"
    saved = keep.read_bytes() if keep.exists() else None
    if dest.exists():
        shutil.rmtree(dest)
    shutil.copytree(src, dest)
    if saved is not None and not (dest / "workshop.json").exists():
        (dest / "workshop.json").write_bytes(saved)
    print(f"  installed to {dest}")


if __name__ == "__main__":
    folder = build()
    if "--install" in sys.argv:
        i = sys.argv.index("--install")
        target = sys.argv[i + 1] if i + 1 < len(sys.argv) else mods_dir()
        install(folder, target)
