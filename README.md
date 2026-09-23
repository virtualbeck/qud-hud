# Qud HUD

A second monitor heads-up display for [Caves of Qud](https://www.cavesofqud.com/).

Your character's state is written out every turn and drawn as a page you keep open on another
screen — hit points, what's wrong with you, what's hunting you, what's on cooldown, and what needs
attention right now, without pausing to dig through sub-screens.

![Qud HUD](mod/preview.png)

## Install

**From Steam:** subscribe to the [Workshop item](https://steamcommunity.com/sharedfiles/filedetails/?id=3805872225).

**Without Steam:** download `QudHUD-vX.Y.Z.zip` from [Releases](https://github.com/virtualbeck/qud-hud/releases)
and extract the `QudHUD` folder into your Caves of Qud `Mods` folder:

| | |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\Freehold Games\CavesOfQud\Mods` |
| macOS | `~/Library/Application Support/com.FreeholdGames.CavesOfQud/Mods` |
| Linux | `~/.config/unity3d/Freehold Games/CavesOfQud/Mods` |

Either way: enable it in the Mods menu, start or load a game, and the message log will tell you
where the display page was written — normally `Documents/QudHUD/hud.html`. Open that in a browser
on your second monitor.

Nothing is sent anywhere. The page reads a local file the mod writes; no server, no network.

## Use

Full instructions are in [mod/README.md](mod/README.md), which ships with the mod. In short:

- Drag a panel by its `≡` grip to rearrange it, or focus the grip and use the arrow keys.
- `×` hides a panel; **Options** in the footer brings it back.
- `+` / `-` resize, `0` resets, `R` restores the default layout.
- The dot in the footer is green while the data is current, amber once nothing has been written for
  a couple of minutes, and red if the file can't be read. The readings fade when they go stale, so a
  closed game doesn't look like a live one.

## Bugs and suggestions

Please open an [issue](https://github.com/virtualbeck/qud-hud/issues). Useful things to include:
the mod version (shown in **Options** on the page), your game version, and any lines starting with
`[QudHUD]` in `Player.log`.

If a panel stops drawing after a game update, the footer will name it and the browser console will
have the error — that text is the most useful thing you can paste into an issue.

## Building from source

Requires Python 3.8+. No other dependencies.

```
python build.py              # dist/QudHUD and dist/QudHUD-vX.Y.Z.zip
python build.py --install    # also copy it into your Mods folder
python build.py --uninstall  # remove that copy again
```

`src/hud.html` is the display page and can be opened directly in a browser to work on the design —
it shows a waiting screen until a `hud_data.js` sits next to it.

One gotcha worth knowing: a locally installed copy and a Workshop-subscribed copy share the same mod
ID, and having both present can make the game load a mix of the two. Unsubscribe or `--uninstall`
while working on it.

## Layout

| | |
|---|---|
| `src/QudHUD.template.cs` | the mod. `__HTML__` and `__VERSION__` are filled in by the build |
| `src/hud.html` | the display page |
| `mod/` | files shipped as-is: manifest, preview, player README, Workshop link |
| `VERSION` | the single source of the version number |
| `build.py` | builds, installs, and can publish to the Workshop via SteamCMD (`--workshop`) |

## License

MIT — see [LICENSE](LICENSE).
