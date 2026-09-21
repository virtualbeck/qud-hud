#!/usr/bin/env python3
"""Builds the Qud HUD mod folder and a release zip.

  python build.py              build dist/QudHUD and dist/QudHUD-vX.Y.Z.zip
  python build.py --install    also copy the mod into your Caves of Qud Mods folder
  python build.py --install PATH   copy into PATH instead

The version comes from the VERSION file and is stamped into the manifest,
the C# code and the display page.
  python build.py --workshop    build, then publish dist/QudHUD to the Steam Workshop via SteamCMD

--workshop needs `steamcmd` on PATH and the STEAM_USER env var set to a Steam
account. SteamCMD will prompt for the password and, on first login from this
machine, a Steam Guard code. See the "Automated Workshop upload" section in
README.md.
"""
import json, os, re, shutil, subprocess, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DIST = ROOT / "dist"
MOD_ID = "QudHUD"
STEAM_APP_ID = "333640"  # Caves of Qud
WORKSHOP_JSON = ROOT / "mod/workshop.json"


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


def changelog_head():
    """The bullet list under the top-most ## heading in CHANGELOG.md."""
    lines = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8").splitlines()
    started, out = False, []
    for line in lines:
        if line.startswith("## "):
            if started:
                break
            started = True
            continue
        if started:
            out.append(line)
    return "\n".join(out).strip()


def vdf_escape(s):
    return s.replace("\\", "\\\\").replace('"', '\\"')


def build_vdf(folder, version):
    """Write dist/workshop_item.vdf, a SteamCMD `workshop_build_item` script.

    Reuses the WorkshopId from mod/workshop.json (written by the in-game
    uploader, see README) if one exists, so this updates the same item
    instead of creating a duplicate.
    """
    published_id = "0"
    if WORKSHOP_JSON.exists():
        published_id = str(json.loads(WORKSHOP_JSON.read_text(encoding="utf-8")).get("WorkshopId", 0))

    description = (ROOT / "workshop_description.txt").read_text(encoding="utf-8").strip()
    fields = {
        "appid": STEAM_APP_ID,
        "publishedfileid": published_id,
        "contentfolder": str(folder),
        "previewfile": str(ROOT / "mod/preview.png"),
        "visibility": "0",  # Valve's ERemoteStoragePublishedFileVisibility: 0 = public
        "title": "Qud HUD",
        "description": description,
        "changenote": changelog_head() or f"v{version}",
    }
    body = "\n".join(f'\t"{k}"\t\t"{vdf_escape(v)}"' for k, v in fields.items())
    vdf_path = DIST / "workshop_item.vdf"
    vdf_path.write_text(f'"workshopitem"\n{{\n{body}\n}}\n', encoding="utf-8")
    return vdf_path


def publish_workshop(folder, version):
    user = os.environ.get("STEAM_USER")
    if not user:
        sys.exit("Set STEAM_USER to your Steam account name to publish to the Workshop.")
    if shutil.which("steamcmd") is None:
        sys.exit("steamcmd not found on PATH. Install it, then re-run with --workshop.")

    vdf_path = build_vdf(folder, version)
    print(f"  wrote {vdf_path}")
    print("Launching steamcmd (enter your password and Steam Guard code if asked)...")
    result = subprocess.run(["steamcmd", "+login", user, "+workshop_build_item", str(vdf_path), "+quit"])
    if result.returncode != 0:
        sys.exit(f"steamcmd exited with status {result.returncode}")
    if not WORKSHOP_JSON.exists():
        print()
        print("First publish: steamcmd printed a new Workshop item ID above (published file id).")
        print(f"Create {WORKSHOP_JSON} with that ID, e.g.:")
        print(json.dumps({
            "WorkshopId": 0,
            "Title": "Qud HUD",
            "Description": (ROOT / "workshop_description.txt").read_text(encoding="utf-8").strip(),
            "Tags": "UI",
            "Visibility": "2",
            "ImagePath": "preview.png",
        }, indent=2))
        print("Then commit mod/workshop.json so future builds update the same item.")


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
    if "--workshop" in sys.argv:
        publish_workshop(folder, (ROOT / "VERSION").read_text().strip())
