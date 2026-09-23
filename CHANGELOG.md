# Changelog

## 1.0.7
- Changed: Hostiles in sight now shows a creature's health the way the game does, as Perfect, Fine, Injured, Wounded or Badly Wounded. The exact hit point bar is only drawn when you are carrying something that reads exact stats off that creature, such as an optical bioscanner for living things or a technoscanner for robots. Without one, the numbers are no longer sent to the page at all.

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
