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
- Drag a panel by its ≡ grip to rearrange, or focus the grip and use the arrow keys. `R` restores the default layout.
- `+` and `-` resize, `0` resets size.
- **Options** in the footer: low fresh water warning and threshold, and the installed version.

## Notes
- The page reads a local file; no server or internet connection is needed.
- The mod adds a small tracker part to your character. Keep the mod enabled for saves made with it.
- If a section stays empty after a game update, check `Player.log` for lines starting with `[QudHUD]`.
