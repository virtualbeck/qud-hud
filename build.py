#!/usr/bin/env python3
"""Builds the Qud HUD mod folder and a release zip.

  python build.py                    build dist/QudHUD and dist/QudHUD-vX.Y.Z.zip
  python build.py --install          also copy the mod into your Caves of Qud Mods folder
  python build.py --install PATH     copy into PATH instead
  python build.py --uninstall        remove the mod from your Caves of Qud Mods folder
  python build.py --uninstall PATH   remove it from PATH instead
  python build.py --workshop         build, then publish dist/QudHUD to the Steam Workshop via SteamCMD
  python build.py --release          build, tag vX.Y.Z, and publish a GitHub release with the zip
  python build.py --release --yes    the same without the confirmation prompt

The version comes from the VERSION file and is stamped into the manifest,
the C# code and the display page.

--workshop needs `steamcmd` on PATH and the STEAM_USER env var set to a Steam
account. SteamCMD will prompt for the password and, on first login from this
machine, a Steam Guard code. It updates the item named by mod/workshop.json and
never creates a new one; it leaves the Workshop description alone.

--release needs `gh` on PATH and logged in. It refuses to run on a dirty tree,
on a version with no CHANGELOG section, or when the tag already exists. Release
notes cover every CHANGELOG section since the previous tag, so versions that
were never released still reach people who download the zip.

Releasing, in order:
  1. bump VERSION, add its CHANGELOG section
  2. --install, test in game, repeat; --uninstall when done
  3. commit and push, since --release refuses to run on a dirty tree
  4. --release   tags, pushes the tag, publishes the zip on GitHub
  5. --workshop  pushes the same build to Steam
Given both flags at once, --release runs first so its checks fail before Steam
sees anything.
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


def changelog_bullets():
    """The entries under the top-most ## heading in CHANGELOG.md, one string each.

    Markdown markup is dropped, since the changenote is shown as plain text.
    """
    lines = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8").splitlines()
    started, out = False, []
    for line in lines:
        if line.startswith("## "):
            if started:
                break
            started = True
            continue
        if not started:
            continue
        line = line.strip().replace("**", "").replace("`", "")
        if line.startswith("- "):
            out.append(line[2:].strip())
        elif line and out:
            out[-1] += " " + line  # a wrapped continuation of the bullet above
    return out


def changenote(version):
    """A one-line changenote, since a KeyValues value cannot contain a newline.

    Steam renders BBCode in Workshop text and [list] is block level, so the entries still come out
    on separate lines despite the whole thing being one line in the file. If a Steam page ever
    shows the tags literally, the entries are still readable: [*] separates them.
    """
    items = [b if b[-1] in ".!?" else b + "." for b in changelog_bullets() if b]
    if not items:
        return f"v{version}"
    return f"v{version}[list]" + "".join(f"[*]{b}" for b in items) + "[/list]"


def vdf_value(s):
    # steamcmd's KeyValues parser does not honour escape sequences, so a value cannot contain a
    # quote or a newline at all: both just end it early and the rest of the file is read as keys.
    # Backslashes stay doubled, which is what every published workshop_build_item example does
    # and which Windows collapses back when it opens the path.
    return " ".join(s.replace("\\", "\\\\").replace('"', "'").split())


def build_vdf(folder, version):
    """Write dist/workshop_item.vdf, a SteamCMD `workshop_build_item` script.

    Reuses the WorkshopId from mod/workshop.json (written by the in-game
    uploader, see README) if one exists, so this updates the same item
    instead of creating a duplicate.
    """
    published_id = "0"
    if WORKSHOP_JSON.exists():
        published_id = str(json.loads(WORKSHOP_JSON.read_text(encoding="utf-8")).get("WorkshopId", 0))

    # No description field: it is the one value that needs line breaks, and this file format
    # cannot carry them. Leaving the key out means the item keeps the description already on its
    # Workshop page, rather than having it flattened into a single paragraph on every upload.
    fields = {
        "appid": STEAM_APP_ID,
        "publishedfileid": published_id,
        "contentfolder": str(folder),
        "previewfile": str(ROOT / "mod/preview.png"),
        "visibility": "0",  # Valve's ERemoteStoragePublishedFileVisibility: 0 = public
        "title": "Qud HUD",
        "changenote": changenote(version),
    }
    body = "\n".join(f'\t"{k}"\t\t"{vdf_value(v)}"' for k, v in fields.items())
    vdf_path = DIST / "workshop_item.vdf"
    vdf_path.write_text(f'"workshopitem"\n{{\n{body}\n}}\n', encoding="utf-8")
    return vdf_path


def changelog_sections():
    """Every (version, body) section of CHANGELOG.md, newest first."""
    out, version, body = [], None, []
    for line in (ROOT / "CHANGELOG.md").read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            if version:
                out.append((version, "\n".join(body).strip()))
            version, body = line[3:].strip(), []
        elif version:
            body.append(line)
    if version:
        out.append((version, "\n".join(body).strip()))
    return out


def git(*args):
    r = subprocess.run(["git", *args], capture_output=True, text=True, cwd=ROOT)
    if r.returncode != 0:
        sys.exit(f"git {' '.join(args)} failed: {(r.stderr or r.stdout).strip()}")
    return r.stdout.strip()


def version_tuple(tag):
    m = re.fullmatch(r"v(\d+)\.(\d+)\.(\d+)", tag)
    return tuple(int(p) for p in m.groups()) if m else None


def previous_tag(tag):
    """The highest existing release tag below this one, or None for a first release."""
    here = version_tuple(tag)
    below = [t for t in git("tag").split() if version_tuple(t) and version_tuple(t) < here]
    return max(below, key=version_tuple) if below else None


def release_notes(version, prev):
    """Notes covering every changelog section after prev, so skipped versions still get read."""
    stop = version_tuple(prev) if prev else None
    changes = [
        f"### {v}\n{body}"
        for v, body in changelog_sections()
        if version_tuple("v" + v) and (stop is None or version_tuple("v" + v) > stop)
    ]
    repo = "https://github.com/virtualbeck/qud-hud"
    return f"""Second monitor heads-up display for Caves of Qud.

## Install

Download `{MOD_ID}-v{version}.zip` below and extract the `{MOD_ID}` folder into your Caves of Qud `Mods` folder:

| | |
|---|---|
| Windows | `%USERPROFILE%\\AppData\\LocalLow\\Freehold Games\\CavesOfQud\\Mods` |
| macOS | `~/Library/Application Support/com.FreeholdGames.CavesOfQud/Mods` |
| Linux | `~/.config/unity3d/Freehold Games/CavesOfQud/Mods` |

Or subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3805872225).

Enable it in the Mods menu, start or load a game, and open the page the message log points at
(normally `Documents/QudHUD/hud.html`) on your second monitor.

## Changes{" since " + prev if prev else ""}

{chr(10).join(changes) if changes else "See CHANGELOG.md."}

Found a bug? Please [open an issue]({repo}/issues).
"""


def release(version, assume_yes):
    if shutil.which("gh") is None:
        sys.exit("gh not found on PATH. Install the GitHub CLI and run `gh auth login`, then re-run with --release.")

    tag = f"v{version}"
    if git("status", "--porcelain"):
        sys.exit("Working tree has uncommitted changes. Commit or stash them, then re-run with --release.")
    if tag in git("tag").split():
        sys.exit(f"Tag {tag} already exists. Bump VERSION and add a CHANGELOG section first.")
    top = changelog_sections()
    if not top or top[0][0] != version:
        sys.exit(f"CHANGELOG.md's newest section is {top[0][0] if top else 'missing'}, not {version}. Add it first.")

    zpath = DIST / f"{MOD_ID}-v{version}.zip"
    if not zpath.exists():
        sys.exit(f"{zpath} is missing. Run the build first.")

    prev = previous_tag(tag)
    notes = DIST / "release_notes.md"
    notes.write_text(release_notes(version, prev), encoding="utf-8")

    print(f"Releasing {tag}")
    print(f"  commit: {git('rev-parse', '--short', 'HEAD')} on {git('rev-parse', '--abbrev-ref', 'HEAD')}")
    print(f"  asset:  {zpath.name}")
    print(f"  notes:  changes since {prev}" if prev else "  notes:  all changes so far")
    if not assume_yes:
        if input("Tag, push and publish? [y/N] ").strip().lower() not in ("y", "yes"):
            sys.exit("Cancelled, nothing was pushed.")

    git("tag", "-a", tag, "-m", tag)
    git("push", "origin", tag)
    # The title is the bare tag, matching every earlier release on this repo.
    r = subprocess.run(["gh", "release", "create", tag, str(zpath),
                        "--title", tag, "--notes-file", str(notes)], cwd=ROOT)
    if r.returncode != 0:
        sys.exit(f"gh release create failed (status {r.returncode}). The tag {tag} is already pushed; "
                 f"fix the problem and finish with: gh release create {tag} {zpath} --title {tag} --notes-file {notes}")
    print(f"  released {tag}")


def publish_workshop(folder, version):
    user = os.environ.get("STEAM_USER")
    if not user:
        sys.exit("Set STEAM_USER to your Steam account name to publish to the Workshop.")
    if shutil.which("steamcmd") is None:
        sys.exit("steamcmd not found on PATH. Install it, then re-run with --workshop.")
    if not WORKSHOP_JSON.exists():
        sys.exit(
            f"{WORKSHOP_JSON} doesn't exist yet. Publishing without it creates a NEW Workshop item "
            "every time instead of updating one. Create the item once with the game's in-game "
            "Workshop uploader, copy the workshop.json it writes to mod/, then re-run --workshop."
        )

    vdf_path = build_vdf(folder, version)
    print(f"  wrote {vdf_path}")
    print("  the Workshop description is left untouched; edit it on the item's Steam page.")
    print("Launching steamcmd (enter your password and Steam Guard code if asked)...")
    result = subprocess.run(["steamcmd", "+login", user, "+workshop_build_item", str(vdf_path), "+quit"])
    if result.returncode != 0:
        sys.exit(f"steamcmd exited with status {result.returncode}")


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


def uninstall(target):
    dest = Path(target) / MOD_ID
    if not dest.exists():
        print(f"  nothing installed at {dest}")
        return
    shutil.rmtree(dest)
    print(f"  removed {dest}")


if __name__ == "__main__":
    if "--uninstall" in sys.argv:
        i = sys.argv.index("--uninstall")
        target = sys.argv[i + 1] if i + 1 < len(sys.argv) else mods_dir()
        uninstall(target)
        sys.exit(0)

    folder = build()
    if "--install" in sys.argv:
        i = sys.argv.index("--install")
        target = sys.argv[i + 1] if i + 1 < len(sys.argv) else mods_dir()
        install(folder, target)
    # --release before --workshop when both are given: it is the step with preconditions, so a
    # dirty tree or a missing changelog section stops things before anything reaches Steam.
    if "--release" in sys.argv:
        release((ROOT / "VERSION").read_text().strip(), "--yes" in sys.argv)
    if "--workshop" in sys.argv:
        publish_workshop(folder, (ROOT / "VERSION").read_text().strip())
