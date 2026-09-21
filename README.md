# Qud HUD

Second monitor heads-up display for Caves of Qud. Player instructions are in [mod/README.md](mod/README.md).

## Layout
- `src/QudHUD.template.cs` mod code. `__HTML__` and `__VERSION__` are filled in by the build.
- `src/hud.html` display page. Open it directly in a browser to work on the design.
- `mod/` files shipped as-is (manifest, preview, player README, and `workshop.json` once you have one).
- `VERSION` the single source of the version number.

## Build
Requires Python 3.8+.

    python build.py              # dist/QudHUD and dist/QudHUD-vX.Y.Z.zip
    python build.py --install    # also copies into your Caves of Qud Mods folder

## Release checklist
1. Bump `VERSION` and add a section to `CHANGELOG.md`.
2. `python build.py --install`, launch the game, and check the display page and Options version.
3. Upload to Steam from the game's Workshop uploader (see below).
4. Commit, tag `vX.Y.Z`, push, and attach `dist/QudHUD-vX.Y.Z.zip` to a GitHub release.

## Steam Workshop
Requires the Steam copy of the game.
1. Main menu, Modding Utilities (lower left), Steam workshop uploader.
2. Select Qud HUD and click **Create Workshop Id for Mod...** (first time only). This writes `workshop.json` into the installed mod folder.
3. Copy that `workshop.json` into `mod/` here and commit it, so future builds keep the Workshop link.
4. Fill in title, tags and description (paste `workshop_description.txt`), then **Upload Content...**.
5. Updates: build, install, open the uploader, select the mod, **Upload Content...**.

Keep `"Visibility": "2"` in `workshop.json`, or updates may reset the mod to private.
If you subscribe to your own Workshop item, move your dev copy out of `Mods` to avoid loading it twice.
