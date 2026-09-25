# Changelog

## 1.0.10
- Fixed: travelling on the world map was noticeably slower with the mod enabled, up to a second between a keypress and the move. One step there passes hundreds of game turns, and the mod rebuilt and rewrote its data on every one of them. It now updates once when you get control back, and a few times a second while time passes without input, as when resting. A step that still costs noticeable time is noted in Player.log.
- Fixed: on Linux with a Flatpak browser, such as the Firefox that Bazzite ships, the page sat on "Waiting for Caves of Qud" forever with the game running. The sandbox hands the browser the page alone, not the data file beside it. The page now recognises this and says how to grant access, and the READMEs give the Linux location, `~/QudHUD`, rather than `Documents/QudHUD`.
- Fixed: dismissed unspent point reminders came back when you levelled up, and all of them came back after playing another character. A dismissal now belongs to one character and one kind of point, and lasts until that character spends them.
- Added: a Minimap of the current zone. It shows what your character knows and nothing more: unexplored ground is blank, what is in view is drawn live, and places you have seen but cannot see now are drawn faded, as they were when you last saw them, with no creatures in them.
- Added: a Nearby objects panel listing what is in view around you, nearest first: items, stairs, containers, plants, liquid pools and creatures that are neither hostile nor with you. Options can narrow it to takeable items, or hide plants or pools, as the game's own list can.
- Changed: the Minimap, Nearby objects and Messages panels share a third column by default, as the game docks them together. A layout you have already arranged is kept.
- Added: a Messages panel showing the last dozen lines of the game's message log, newest last and in the game's colours, so a combat result or a warning does not scroll away while you are looking at the other screen.
- Added: a Companions panel, beside Hostiles in sight. Followers in view show their health and what is wrong with them, by the same rules as hostiles: the game's health word, exact figures only when your character can scan them, and effects only when looking at them would show them. A follower out of sight is listed as such, with nothing about where it is or how it is doing.
- Fixed: sorting Hostiles in sight by danger could never show a dangerous creature further away than the fifteenth nearest, because the list was cut in nearest order before it reached the page. Each sort now gets its own fifteen.
- Added: Options can hide abilities that are simply ready, leaving cooldowns, toggles and disabled abilities. Off by default.
- Changed: Trivial hostiles no longer set off the "adjacent to you" and "in sight" alarms in Pressing matters. A character who has outgrown them was getting a pulsing red alarm for every snapjaw, which teaches you to ignore the loudest thing on the page. They still appear in Hostiles in sight, and anything rated above Trivial still raises the alarm, with the distance given for the nearest real threat.

## 1.0.9
- Added: AV and DV in the Combat panel now carry the game's marks, a solid diamond and a hollow circle, beside their values.
- Fixed: with nerve poppy and a powered scanner, the hit points line showed loose numbers (17/17 2 10). The game returns a full scan readout there, hit points followed by armour and dodge, whose glyphs do not exist outside the game's own font. Being able to scan yourself now puts the line back to plain figures and a bar, and a scan readout can no longer arrive where a word is expected.
- Fixed: the HUD showed your exact hit points to a character who cannot read them. With the nerve poppy defect the game shows a wound level on your own sheet, and the HUD now does the same, including in the low health warnings.

## 1.0.8
- Changed: the page now waits for a quiet moment to rebuild itself. Once a rebuild is due it holds until the link goes stale, so it no longer lands in the middle of a fight, and rebuilds anyway after fifteen minutes if things never go quiet.
- Fixed: the dot in the footer and the markers on effects had no space after them. A space written after a CSS escape is swallowed by the escape itself, so it never rendered.
- Changed: white text is now a soft off-white rather than pure white, which was harsh against the dark background at the sizes it is used for. This covers text the game itself marks as bright white, such as zone and creature names, so it is very slightly softer here than in game.
- Fixed: hit point figures and bars were coloured from a flat percentage, so a creature at 13 of 25 read green while the game showed it amber. The colour now comes from the game, whose bands follow the wound levels rather than halves and quarters. Applies to your own hit points too.

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
