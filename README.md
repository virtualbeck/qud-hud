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

## Automated Workshop upload (optional)
`python build.py --workshop` builds the mod and publishes `dist/QudHUD` via SteamCMD instead of the
in-game uploader, using `workshop_description.txt` as the description and `mod/preview.png` as the
preview image. This uses SteamCMD's generic `workshop_build_item` command, which isn't documented by
Freehold Games specifically — treat it as unofficial and confirm it works before relying on it.

Requires:
- [SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD) installed and on `PATH`.
- `STEAM_USER` set to your Steam account name. SteamCMD prompts for the password and, on first login
  from a machine, a Steam Guard code (interactive; it caches the session afterward).

`--workshop` refuses to run until `mod/workshop.json` exists, and only ever updates the `WorkshopId`
it names — it never creates a new item. Do the very first publish through the in-game uploader (see
"Steam Workshop" above), which writes `mod/workshop.json` for you; copy it into `mod/` here and commit
it. (If you don't want to touch the game for that step, you can instead write the file by hand with a
placeholder ID, e.g.:

    {
      "WorkshopId": 123456789,
      "Title": "Qud HUD",
      "Description": "...paste workshop_description.txt...",
      "Tags": "UI",
      "Visibility": "2",
      "ImagePath": "preview.png"
    }

then run `python build.py --workshop` once by hand with that placeholder — SteamCMD will create a new
item and print its real ID; immediately fix `WorkshopId` to that number and commit before running
`--workshop` again, or a second run will create yet another duplicate.)

Every later `--workshop` run reads `WorkshopId` from `mod/workshop.json` and updates that one item.
