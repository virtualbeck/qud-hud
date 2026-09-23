# Changelog

## 1.0.7
- Changed: the HUD no longer shows you anything your character cannot see. Hostiles read as Perfect, Fine, Injured, Wounded or Badly Wounded, using the game's own wound level and colour, and an exact hit point bar is only drawn when your character can actually read those numbers. The game decides that rather than this mod, so VISAGE once it has booted, the optical scanner implants and anything a mod adds all count, and a scanner that is switched off does not. Without one the numbers are not sent to the page at all.
- Changed: when you can read a creature's exact hit points, the figures are shown beside the bar rather than the bar alone.
- Added: Hostiles in sight lists what is wrong with each creature, bleeding and the like, in the same style and colours as your own Effects panel. The game's own rule for what shows when you look at a creature decides the list, so nothing appears that you could not see by looking.
- Added: a Sort toggle on Hostiles in sight, switching between nearest first and most dangerous first. Your choice is remembered. Sorting by danger weighs the game's difficulty rating first, then how big the creature is, then how close it is, since a high level character reads almost everything as Trivial and the rating alone stops telling you much.
- Changed: hostiles at the same distance are ordered by how dangerous they are, and those tied on both by which has the most hit points left.
- Added: unspent point reminders in Pressing matters can be dismissed with the × beside them. A dismissal lasts until the number changes, so spending them clears it and earning more brings the reminder back. The totals stay in Survival either way.

## 1.0.6
- Added: panels can be hidden, not just moved. The new × on a panel hides it, and Options lists every panel so you can bring it back. R still restores the default layout and unhides everything.
- Added: the readings fade out when the link goes stale, so a closed game no longer looks like a live one at a glance.
- Added: if a panel stops drawing, the footer now names it instead of failing silently. Each distinct fault is reported once rather than on every update.
- Fixed: the idle line under Nothing pressing no longer changes every few minutes; it is held until something actually happens, as intended.

## 1.0.5
- Fixed: the "Linked" light stayed green after the game exited, because `hud_data.js` stays on disk and still loads cleanly. The light now reads the age of the data itself: green when it is current, amber ("No updates for 12m") once nothing has been written for two minutes, red when the file cannot be read at all.
- Changed: the display page now rebuilds itself every 5 minutes instead of 10, and carries the last reading across the rebuild, so it no longer flashes the waiting screen. Memory held between rebuilds is about 2MB.

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
