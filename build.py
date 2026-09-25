#!/usr/bin/env python3
"""Builds the Qud HUD mod folder and a release zip.

  python build.py                    build dist/QudHUD and dist/QudHUD-vX.Y.Z.zip
  python build.py --install          also copy the mod into your Caves of Qud Mods folder
  python build.py --install PATH     copy into PATH instead
  python build.py --uninstall        remove the mod from your Caves of Qud Mods folder
  python build.py --uninstall PATH   remove it from PATH instead
  python build.py --check            compile the mod against your installed game, before installing it
  python build.py --check PATH       the same, given the game's Managed folder
  python build.py --workshop         build, then publish dist/QudHUD to the Steam Workshop via SteamCMD
  python build.py --release          build, tag vX.Y.Z, and publish a GitHub release with the zip
  python build.py --release --yes    the same without the confirmation prompt

The version comes from the VERSION file and is stamped into the manifest,
the C# code and the display page.

--workshop needs `steamcmd` on PATH and the STEAM_USER env var set to a Steam
account. SteamCMD will prompt for the password and, on first login from this
machine, a Steam Guard code. It updates the item named by mod/workshop.json and
never creates a new one; it leaves the Workshop description alone.

--release needs `gh` on PATH and logged in, and Node with the test dependencies
installed (`npm install` in tests/), because it runs the whole test suite before
tagging anything. It refuses to run on a dirty tree, on a failing test, on a
version with no CHANGELOG section, or when the tag already exists. Release
notes cover every CHANGELOG section since the previous tag, so versions that
were never released still reach people who download the zip.

--check finds a C# compiler (on Windows the .NET Framework one, which is always
there) and the game's Managed folder (through Steam, or QUD_MANAGED, or PATH), then
compiles the built mod against the game's own DLLs and reports errors by line in
src/QudHUD.template.cs. Combine it with --install to install only what compiles. It
then compiles tests/probe/GameApi.cs, which names every member the mod reaches only
by reflection, and reports which of them this build of the game has.

Releasing, in order:
  1. bump VERSION, add its CHANGELOG section
  2. --check --install, test in game, repeat; --uninstall when done
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
(normally `Documents/QudHUD/hud.html`, or `~/QudHUD/hud.html` on Linux) on your second monitor.

## Changes{" since " + prev if prev else ""}

{chr(10).join(changes) if changes else "See CHANGELOG.md."}

Found a bug? Please [open an issue]({repo}/issues).
"""


def find_compiler():
    """A C# compiler as (command, kind), or (None, None).

    The .NET Framework one is on every Windows machine and handles the C# 5 the mod is written in,
    so on Windows this normally needs nothing installed.
    """
    exe = shutil.which("csc")
    if exe:
        return [exe], "csc"
    dotnet = shutil.which("dotnet")
    if dotnet:
        sdks = subprocess.run([dotnet, "--list-sdks"], capture_output=True, text=True).stdout
        for line in reversed(sdks.splitlines()):
            m = re.match(r"(\S+) \[(.+)\]", line.strip())
            csc = m and Path(m.group(2)) / m.group(1) / "Roslyn" / "bincore" / "csc.dll"
            if csc and csc.exists():
                return [dotnet, "exec", str(csc)], "dotnet"
    windir = os.environ.get("WINDIR")
    if windir:
        for fw in ("Framework64", "Framework"):
            exe = Path(windir) / "Microsoft.NET" / fw / "v4.0.30319" / "csc.exe"
            if exe.exists():
                return [str(exe)], "framework"
    exe = shutil.which("mcs")
    if exe:
        return [exe], "mcs"
    return None, None


def find_managed():
    """The game's Managed folder, where Assembly-CSharp.dll lives, or None."""
    if os.environ.get("QUD_MANAGED"):
        return Path(os.environ["QUD_MANAGED"])
    home = Path.home()
    roots = [Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Steam",
             home / ".steam/steam", home / ".local/share/Steam",
             home / "Library/Application Support/Steam"]
    libraries = list(roots)
    for root in roots:
        vdf = root / "steamapps" / "libraryfolders.vdf"
        if vdf.exists():
            for p in re.findall(r'"path"\s+"([^"]+)"', vdf.read_text(encoding="utf-8", errors="ignore")):
                libraries.append(Path(p.replace("\\\\", "\\")))
    for lib in libraries:
        game = lib / "steamapps" / "common" / "Caves of Qud"
        # the data folder is named after the executable, so find it rather than assume its name
        for managed in list(game.glob("*_Data/Managed")) + list(game.glob("*.app/Contents/Resources/Data/Managed")):
            if (managed / "Assembly-CSharp.dll").exists():
                return managed
    return None


def compile_cs(compiler, sources, references, out, extra=(), target="library"):
    """Compile against exactly these references. Returns (ok, output)."""
    if isinstance(sources, (str, Path)):
        sources = [sources]
    rsp = out.with_suffix(".rsp")
    args = ["-nostdlib", f"-t:{target}", f'-out:"{out}"', *extra]
    args += [f'-r:"{r}"' for r in references] + [f'"{s}"' for s in sources]
    rsp.write_text("\n".join(args), encoding="utf-8")
    # -noconfig is ignored inside a response file, so it goes on the command line. The references go
    # in the file because the game ships enough DLLs to overflow a Windows command line.
    r = subprocess.run(compiler + ["-noconfig", f"@{rsp}"], capture_output=True, text=True)
    return r.returncode == 0 and out.exists(), (r.stdout + r.stderr)


def template_line(built_line, page_start, page_newlines):
    """Map a line of the built mod back to the template, which holds the page as one short line."""
    if built_line <= page_start:
        return built_line
    if built_line <= page_start + page_newlines:
        return None  # inside the embedded page
    return built_line - page_newlines


def probe_report(output, source_lines):
    """Which labelled lines of the API probe failed. Returns (held, total, missing, unchecked, broken).

    A missing type does not make the lines that use it fail, the compiler only reports the type, so
    those lines count as unchecked rather than as held.
    """
    labels = {}
    for n, line in enumerate(source_lines, 1):
        m = re.search(r"// probe: (.+?)\s*$", line)
        if m:
            labels[n] = m.group(1)
    failed = {int(n) for n in re.findall(r"GameApi\.cs\((\d+),\d+\): error", output)}
    missing_types = {labels[n][5:] for n in failed if n in labels and labels[n].startswith("type ")}
    missing, unchecked = [], []
    for n in sorted(labels):
        label = labels[n]
        if label.startswith("type "):
            if n in failed:
                missing.append(label)
            continue
        if label.split(".")[0] in missing_types:
            unchecked.append(label)
        elif n in failed:
            missing.append(label)
    members = [l for l in labels.values() if not l.startswith("type ")]
    held = len(members) - len([m for m in missing if not m.startswith("type ")]) - len(unchecked)
    broken = [m for m in missing + unchecked if "known good" in m]
    return held, len(members), missing, unchecked, broken


def probe(compiler, references):
    """Compile the API probe against the game, reporting which of the mod's guesses hold."""
    src = ROOT / "tests" / "probe" / "GameApi.cs"
    if not src.exists():
        return
    out = DIST / "check" / "GameApi.dll"
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        out.unlink()
    ok, output = compile_cs(compiler, src, references, out)
    lines = src.read_text(encoding="utf-8").splitlines()
    held, total, missing, unchecked, broken = probe_report(output, lines)
    # Only failures on labelled lines say anything about a guess. A failure anywhere else means the
    # probe did not compile for some other reason, and "everything holds" would then be a lie.
    labelled = {n for n, l in enumerate(lines, 1) if "// probe:" in l}
    failed = {int(n) for n in re.findall(r"GameApi\.cs\((\d+),\d+\): error", output)}
    if not ok and (not failed or failed - labelled):
        print("  game API: the probe did not compile for a reason unrelated to the game, so nothing is known:")
        print("    " + "\n    ".join(output.strip().splitlines()[:5]))
        return
    print(f"  game API: {held} of {total} of the mod's guesses hold against this build")
    if missing:
        print("    not found: " + ", ".join(missing))
    if unchecked:
        print("    not checked, since their type was not found: " + ", ".join(unchecked))
    if broken:
        print("    some of those have worked in game before, so the probe itself may be at fault")


def check(managed):
    """Compile the built mod against the game's own DLLs, so a mistake fails here, not at load."""
    compiler, kind = find_compiler()
    if compiler is None:
        sys.exit("No C# compiler found. On Windows the .NET Framework one is normally present; "
                 "elsewhere install the .NET SDK or Mono.")
    managed = Path(managed) if managed else find_managed()
    if managed is None or not (managed / "Assembly-CSharp.dll").exists():
        sys.exit("Couldn't find the game. Pass its Managed folder, the one holding Assembly-CSharp.dll:\n"
                 '  python build.py --check "C:\\...\\Caves of Qud\\CoQ_Data\\Managed"')

    source = DIST / MOD_ID / "QudHUD.cs"
    out = DIST / "check" / "QudHUD.dll"
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        out.unlink()
    print(f"Compiling against {managed} with {kind}...")
    ok, output = compile_cs(compiler, source, sorted(managed.glob("*.dll")), out)

    template = (ROOT / "src/QudHUD.template.cs").read_text(encoding="utf-8").splitlines()
    page_start = next(i for i, l in enumerate(template, 1) if "const string HtmlPage" in l)
    page_newlines = (ROOT / "src/hud.html").read_text(encoding="utf-8").count("\n")
    errors = 0
    for line in output.splitlines():
        m = re.match(r".*?\((\d+),(\d+)\): (error|warning) (\w+): (.*)", line)
        if not m or m.group(3) != "error":
            continue
        errors += 1
        n = template_line(int(m.group(1)), page_start, page_newlines)
        where = f"src/QudHUD.template.cs({n},{m.group(2)})" if n else "src/hud.html (embedded page)"
        print(f"  {where}: {m.group(4)}: {m.group(5)}")
    if not ok:
        if not errors:
            print(output.strip())
        sys.exit("The mod does not compile against this copy of the game.")
    print("  compiles cleanly")
    # informational: a wrong guess leaves a panel saying "unavailable" rather than breaking the mod
    probe(compiler, sorted(managed.glob("*.dll")))


def run_tests():
    """The whole suite, so a release cannot go out with a failing test."""
    tests = ROOT / "tests"
    # node itself rather than npm: on Windows npm is a .cmd shim that needs a shell to launch
    if shutil.which("node") is None:
        sys.exit("node not found on PATH. --release runs the test suite first; install Node, then re-run.")
    if not (tests / "node_modules" / "jsdom").exists():
        sys.exit("Test dependencies are missing. Run `npm install` in tests/, then re-run with --release.")
    print("Running the test suite...")
    if subprocess.run(["node", str(tests / "run.js")], cwd=tests).returncode != 0:
        sys.exit("Tests failed, so nothing was tagged or published. Fix them, then re-run with --release.")


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

    run_tests()

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


def flag_value(flag):
    """The path following a flag, if there is one rather than another flag."""
    i = sys.argv.index(flag)
    nxt = sys.argv[i + 1] if i + 1 < len(sys.argv) else None
    return None if nxt is None or nxt.startswith("--") else nxt


if __name__ == "__main__":
    if "--uninstall" in sys.argv:
        uninstall(flag_value("--uninstall") or mods_dir())
        sys.exit(0)

    folder = build()
    # before --install, so a mod that will not compile never reaches the Mods folder
    if "--check" in sys.argv:
        check(flag_value("--check"))
    if "--install" in sys.argv:
        install(folder, flag_value("--install") or mods_dir())
    # --release before --workshop when both are given: it is the step with preconditions, so a
    # dirty tree or a missing changelog section stops things before anything reaches Steam.
    if "--release" in sys.argv:
        release((ROOT / "VERSION").read_text().strip(), "--yes" in sys.argv)
    if "--workshop" in sys.argv:
        publish_workshop(folder, (ROOT / "VERSION").read_text().strip())
