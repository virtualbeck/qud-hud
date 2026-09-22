# Changelog

## 1.0.4
- Fixed: the display page's Chrome tab grew steadily in memory over long sessions (hundreds of MB after a few hours). Polling now runs once a second instead of four times a second, and the page reloads itself every 10 minutes (layout and options are preserved) to clear out the accumulated overhead from the file:// script-polling technique.

## 1.0.3
- Added: a compass arrow (or a dot for the same tile) after the distance in Hostiles in sight, showing each hostile's direction relative to you.

## 1.0.2
- Fixed: a hostile stayed listed in Hostiles in sight / Pressing matters after it was no longer in line of sight (e.g. behind a closed door). Visibility now uses the game's per-cell FOV check instead of the creature's own IsVisible(), which reflects stealth/invisibility rather than real line of sight.

## 1.0.1
- Fixed: a killed creature (e.g. holograms that linger in the zone after death) could still show up in Hostiles in sight and Pressing matters. Hostiles are now dropped once their Hitpoints reach 0.

## 1.0.0
First public release.
- Second monitor display page with Pressing matters, effects, abilities, hostiles, gear, attributes, combat and survival panels.
- Updates right before player input, at the start of your turn and at the end of the turn.
- Movable panels with saved layout. Empty columns collapse so the others widen.
- Hostile difficulty ratings using the game's own wording and colors.
- Cooldowns grouped at the top of Abilities.
- Optional low fresh water warning with adjustable threshold.
- XP bar shows progress within the current level.
- Version shown in Options; stale pages reload themselves after an update.
