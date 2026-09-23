# Qud HUD

A second monitor heads-up display for Caves of Qud.

## Install
Subscribe on the Steam Workshop, or copy the `QudHUD` folder into your Caves of Qud `Mods` folder:
- Windows: `%USERPROFILE%\AppData\LocalLow\Freehold Games\CavesOfQud\Mods`
- macOS: `~/Library/Application Support/com.FreeholdGames.CavesOfQud/Mods`
- Linux: `~/.config/unity3d/Freehold Games/CavesOfQud/Mods`

Enable it in the Mods menu, then start or load a game. The message log shows where the display page was written, normally `Documents/QudHUD/hud.html`. Open it in Chrome, Edge or Firefox on your second monitor (F11 for fullscreen).

## Use
The page updates right before each of your turns, after everything else has acted.
- Drag a panel by its ≡ grip to rearrange, or focus the grip and use the arrow keys.
- `×` hides a panel you don't want. **Options** lists them all and brings them back.
- `+` and `-` resize, `0` resets size. `R` restores the default layout and unhides everything.
- **Options** in the footer: low fresh water warning and threshold, which panels are shown, and the installed version.
- **Hostiles in sight** has a Sort toggle in its top right, switching between nearest first and most dangerous first. Your choice is remembered.
- Unspent point reminders in **Pressing matters** have an × to dismiss them. They come back if the number changes, and the totals are always in **Survival**.

The dot at the bottom left tells you whether what you're looking at is current:
- **Green**: live.
- **Amber**: nothing has been written for a couple of minutes. The game is closed, or you have been idle. The readings fade at the same time, so stale numbers don't read as live ones.
- **Red**: the data file can't be read at all.

## Notes
- The page reads a local file; no server or internet connection is needed.
- Your own hit points show as a word rather than figures if your character cannot read them, for instance with the nerve poppy defect.
- Hostiles show a health word (Perfect, Fine, Injured, Wounded, Badly Wounded), the same as looking at them in game. You only get an exact hit point bar for creatures your character can actually scan, for instance while wearing a booted VISAGE or with one of the optical scanner implants. The game decides that, not the mod, so anything else granting it works too.
- The mod adds a small tracker part to your character. Keep the mod enabled for saves made with it.
- If a section stays empty after a game update, check `Player.log` for lines starting with `[QudHUD]`. If a panel stops drawing entirely, the footer names it and the browser console has the error.

## Bugs and suggestions
Please open an issue at https://github.com/virtualbeck/qud-hud/issues. Include the version shown in **Options**, your game version, and any `[QudHUD]` lines from `Player.log`.
