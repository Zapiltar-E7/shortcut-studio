# Shortcut Studio

Makes any non-Steam game look and feel like an official one in your Steam library: name, program and launch settings, all five pieces of library art, an "About this game" section, playtime, launch options and achievements.

It works with anything you can add to Steam as a shortcut:
- **Games from other launchers:** Epic, GOG, EA app, Ubisoft Connect, Battle.net, itch.io, the Xbox app, or standalone installs.
- **Emulated games:** RetroArch, RPCS3, shadPS4, PCSX2, Dolphin, DuckStation, PPSSPP and other emulators.
- **Recompilations and source ports:** native PC builds of console games, fan ports and mods.

Run **Shortcut Studio.exe**. It needs no install and no .NET.

## What it does

| Tab | What you set |
| --- | --- |
| **Details** | Name, target program, start folder, launch options, collections, overlay, Steam Input desktop config, hidden. Also removes the game from Steam. |
| **Artwork** | Library capsule (600×900), wide capsule (920×430), hero (3840×1240), logo with its position over the hero, and the icon. Choose a file, drop an image in, or pick one from SteamGridDB. |
| **Store page** | Short description, "About this game" (plain text or Steam store HTML), developer, publisher, release date, genres. |
| **Achievements** | Name, description, unlocked and locked icons, hidden-until-unlocked, global unlock %, and unlocked state. Add, remove, reorder, unlock all or lock all. |

**Import from Steam…** searches the Steam store by name or appid. It copies that game's library art, store text and details, and its full achievement list onto the shortcut, including hidden achievements, icons and global percentages. This suits games that are also sold on Steam, such as one you own on another launcher. No API key is needed.

**SteamGridDB…** browses community art for each slot. Add a free API key in **Settings** first.

## Game Support 
Games are not inherently supported if the emulator and or recompilation doesn't already have achievements the plugin can find.

If there is a game you want achievements for; open an issue and list the:
 - Game
 - Emulator/Recomp/Native PC Game
 - If it has an achievement list already or if one needs to be made.

Getting as many games supported as possible comes only second to the fluid functionality of the app; I rely on the users for issue reports, and game suggestions.
If a game you want already has an Issue open, please don't open a new issue. Reply to that one; and if you could even suggest achievements. I have not played every game so I won't know what triggers are achievement worthy and
I don't think anyone wants to wait for me to 100% every game before it gets added to the plugin.


## Achievements

Achievements can come from several places:
- **By hand:** make your own list and unlock achievements by clicking them in Steam.
- **Steam:** copy a game's real Steam achievement list with **Import from Steam…**.
- **Emulator trophies:** PlayStation trophy lists that sync unlocks from the emulator.
- **RetroAchievements:** sets for retro games, synced from your RetroAchievements account.
- **Save files:** rules that read a game's save data, for games with no achievement system of their own.

## Achievement sets (DLC)

Steam sorts achievements into sets: the base game, then one set per DLC. In Shortcut Studio, select an achievement, tick **DLC achievement** under **Achievement set** and type the DLC's name (press Enter), or pick one the game already has with **▾**. Achievements with the same name form one set; clearing the name or unticking the box puts them back in the base game.

To set many at once, select them in the list with Ctrl- or Shift-click: the **Achievement set** fields then apply to every selected achievement. Right-clicking the selection also offers **Move to set**: **Base game**, an existing DLC or **New DLC set…**.

Once a game has a DLC set, its achievement list is grouped under **Base Game** and one header per DLC, in Steam's order. Click a header to collapse or expand that set; the app remembers it until you close it. **▲ / ▼** move an achievement within its own set. **▲ Hide banner** in the play bar hides the hero banner to show more of the list; the app remembers that choice.

PlayStation trophy lists already mark DLC trophy packs, so **Emulator trophies…** puts those trophies in a set named after the pack.

When a game has more than one set, the Steam plugin shows them the way Steam does:
- The game page's Achievements section adds "1 base game set · 2 DLC sets".
- The full list shows "across N sets" and "Sets: completed / total" in the header. Each set has its own heading with "Unlocked X / Y" and a progress bar, which turns gold when the set is complete. A set filter ("All Sets", "Base Game", "DLC …") sits next to the unlocked/locked filter.

## Playtime

Steam records playtime for non-Steam games itself, in `localconfig.vdf`, but only shows it in the game page's play bar. The plugin copies Steam's own numbers into the library, so the Recent Games hover card ("Time played"), sorting by playtime and other library views show them too. They're re-read whenever a non-Steam game closes and every few minutes. The **Play time** field on the Details tab edits the same Steam record.

## Launch menu

**Launch menu** on the Details tab gives a game several ways to start, like real Steam games with more than one launch option. For example: plain, with a mod loader, or with different arguments. Each option has a name and its own arguments. It can also have its own target and start folder; leave those blank to use the ones above.

With two or more options, pressing Play anywhere in Steam (the play bar, the library list or the context menu) opens Steam's **Select launch option** dialog. The plugin then sets the shortcut to that option through Steam's API and launches it. Ticking **Always use this option** skips the dialog from then on; **Ask every time** in the app brings it back.

The game page's Play button also gets Steam's split arrow, as on real games. It opens a menu under the button with every option: pick one to make Play start it without asking (✓ marks the current one), or **Ask every time** to bring the dialog back.

In **Big Picture** the dialog is a native Big Picture dialog: the controller moves between the options, A launches the focused one, B cancels, and **Always use this option** is a toggle. The split arrow on Play works there too, but only by touch or mouse; a controller can't reach it, so use the dialog's toggle or the app to set a default.

## Emulator trophies (RPCS3, shadPS4 and PS3 recompilations)

On the **Achievements** tab, **Emulator trophies…** finds the trophy set for the shortcut. It searches the shortcut's program, start folder and launch options, or a folder you pick.

- **RPCS3 (PS3):** reads `dev_hdd0\home\<user>\trophy\<NPWR…>\TROPCONF.SFM` and the `TROP###.PNG` icons. Unlocks come from `TROPUSR.DAT`.
- **shadPS4 (PS4):** decrypts the game's `sce_sys\trophy\trophy00.trp`, preferring the `-patch` folder's copy, with the trophy key from shadPS4's `user\keys.json`. So the list works even before the game has run. It also reads lists shadPS4 has already unpacked under `user\trophy`. Unlocks come from `user\home\<user id>\trophy\<NPWR…>.xml`. Pick the shadPS4 user (1000–1003) whose progress you want.
- **Native PC ports built on ps3recomp:** the trophy list and icons come from the disc's `PS3_GAME\TROPDIR\<id>\TROPHY.TRP`. The port's log names the game folder. Unlocks come from the port's `gamedata\trophies\<comm id>.json`, or `%PS3_TROPHY_DIR%` if that's set.

Every trophy becomes an achievement, with its name, description, icon, hidden flag and grade (Platinum, Gold, Silver or Bronze). After you save, the Steam plugin checks the emulator's unlock file every 3 seconds. A trophy earned in the game unlocks on the Steam page with the "Achievement Unlocked!" popup and the emulator's own unlock time. Synced trophies can't be unlocked by clicking, just as on a real game. **Stop syncing** turns that off.

The popup shows in Steam's library window. The emulator still shows its own trophy popup in the game.

## Save-file achievements

For games with no achievement system of their own, achievements can come from the game's save files. The game's entry has a trophy source with emulator `savefile`: its `unlockFile` is the game's save folder and its `npCommId` names a rules file, `extras\saverules\<id>.json`.

The save reader handles flag-based saves: a `game_data.sav` of named flags (looked up by the CRC32 of their name), with an optional `caption.sav` per slot.

- Every save slot under the save folder is checked. A rules file can name a mode flag (for example a hard mode) so each slot's mode is read from its save, and no slot needs to be picked. Each achievement belongs to one mode.
- Rules test the save's flags: one flag at or above a value, how many of a list of flags are set, any of several conditions, or text in a string flag.
- The Steam plugin re-reads a slot only when its files change. An achievement unlocks the first time any slot of its mode meets its rules, with Steam's popup, and stays unlocked even if that save is later deleted. Unlocks are kept in `extras\saverules\<id>.unlocks.json`.
- Because the game writes its saves at autosaves and manual saves, an achievement pops at the next save, not the moment it's earned.
- A rule marked `progress` shows Steam's progress bar ("37 / 100") on the locked achievement. Each "count" condition adds how many of its flags are set, and each flag condition adds its value, capped at what the rule needs. The furthest save slot of the rule's mode counts.
- **Show progress in Steam (0 / N)** on the Achievements tab turns that bar on or off for one achievement. It only appears when the rule counts more than one step, and **Save to Steam** writes just that switch into the rules file. A condition marked `noProgress` is a final yes/no step the bar doesn't count. Thresholds over 100,000 are raw encodings (floats), not counts.
- A rule marked `requiresAll` only unlocks once every other achievement in the set has, for a 100% capstone.
- Removed achievements should leave their trophy id unused, so the ids of the others never change.

## Dusklight (Twilight Princess PC port)

Dusklight has its own achievements and writes them to `data\achievements.json` as `{ "<key>": { "progress", "unlocked" } }`. A trophy source with emulator `dusklight` reads that file: `unlockFile` is its path and `npCommId` names `extras\saverules\<id>.json`, which maps trophy ids to the game's keys (`{ "achievements": { "1": { "key": "hero_of_twilight" } } }`). A rule with `max` shows a progress bar from the game's own count. Dusklight stores no unlock times, so the first time the plugin sees one unlocked is kept in `<id>.unlocks.json`. Clearing an achievement in the game's menu locks it again in Steam.

## RetroAchievements

For games played through emulators that support RetroAchievements: RetroArch, PCSX2, DuckStation, PPSSPP, Dolphin, Flycast, melonDS, BizHawk and others.

This plugin does not replace the need for RetroAchievement support in said emulators, you still need to be running a compatible rom in an emulator that has RetroAchievement support and is logged in properly.

1. In **Settings**, enter your RetroAchievements username and Web API key. The key is at retroachievements.org/settings under **Keys**.
2. On the **Achievements** tab, click **RetroAchievements…**. Pick the system (it's guessed from the shortcut's emulator), search for the game and import it.

Every achievement comes in with its badge and locked badge, points, global unlock % and the date you earned it. Empty Developer, Publisher, Release date and Genre fields on the Store page are filled from RetroAchievements too.

While the game runs through Steam, the plugin asks RetroAchievements for your progress about once a minute, and new unlocks get the popup. It also refreshes when you open the game's page and the data is more than 5 minutes old. Unlocks are cached in `extras\ra\<game id>.json`, so pages load without waiting on the website.

## Big Picture

The plugin also draws in Big Picture mode. On a non-Steam game's page:

- **Achievements X/Y** sits in the play section's stats, next to Last Played and Play Time.
- The **Achievements** section is at the top of the **Your stuff** tab, where Steam lists them for real games.
- **Game Info** shows the description and details in place of Steam's "detailed information is unavailable" notice.
- Unlock popups and the launch-option dialog appear over Big Picture.

**View My Achievements** and the achievement icons open the full list by touch or mouse. Steam's controller focus can't reach elements the plugin adds to the page, so a controller can't open the list. Once it's open, the D-pad and bumpers scroll it and B closes it.

## Plugin style

The page sections use Steam's own classes, so they follow whatever Steam or Millennium theme is active. The plugin's own windows have their own colours: the achievement list, the desktop launch-option dialog, the unlock popup and the achievement tiles. **Style** picks those colours:

- **Stock:** stock Steam's blue-grey.
- **Simply Dark:** the neutral greys of the Simply Dark Millennium theme. It uses the theme's own colour variables, so changes made in the theme's settings carry over.
- **Auto** (default): Simply Dark while that theme is active, otherwise Stock.

Set it in the app's **Settings** (under Steam plugin) or on the plugin's page in Millennium's settings. Both change `extrasplugin_settings.json`, and Steam picks up the change within a few seconds.

## How it reaches Steam

- **Live saving (no restart):** when Steam is running with the plugin, **Save to Steam** sends your changes to the plugin, which applies them through Steam's own client API. That covers adding and removing shortcuts, name, target, start folder, launch options, hidden, all artwork and the logo position, and they update at once. The app and plugin pass these through request files in `extras\live`; the plugin writes a heartbeat there, so the app knows it's listening.
- **Instant pages:** a few seconds after Steam starts, the plugin reads every game's page data into memory, so a game page opens with its sections already drawn, in the same frame Steam draws the page. It then checks the game's files and re-reads them only if they changed.
- **Live pages:** while a non-Steam game's page is open, the plugin checks every 1.5 seconds whether anything it shows has changed: `games.json`, `unlocks.json`, and the game's unlock source (emulator trophy file, RetroAchievements cache, or the save rules and every save slot). If so, the page and an open achievement list reload in place, keeping the list's filter and search. Saves from the app, unlocks and progress from a game's autosave all show up without leaving the page.
- **Restart saving:** a few edits have no Steam API, so saving them still closes and restarts Steam: play time, overlay, Steam Input desktop config and collections. So does installing or updating the plugin. If the plugin isn't running, the app also falls back to this.
- **Shortcuts file:** shortcuts are kept in `userdata\<account>\config\shortcuts.vdf`. Steam rewrites it on exit, so on the restart path the app closes Steam, writes the file and starts Steam again. Each of those saves keeps `shortcuts.vdf.bak` and one dated `.bak` per day.
- **Art:** saved in `userdata\<account>\config\grid` under Steam's own file names.
- **Description and achievements:** Steam has nowhere to show these for non-Steam games. The app installs a small **Millennium** plugin (`shortcut-studio`) the first time you save. It adds the "About this game" and "Achievements" sections to the game's library page, using Steam's own styles. **View My Achievements** opens the full list. Click an achievement there to unlock it, with Steam's "Achievement Unlocked" popup, or click it again to lock it.
- **Shared data:** the app and the plugin both use `%APPDATA%\ShortcutStudio\extras`. `games.json` holds the details and achievements, `unlocks.json` holds unlock times, and the icons sit next to them.

Existing shortcuts keep their appid when you rename them or change the target, so their art stays linked.

## Requirements

- Windows and the Steam desktop client.
- [Millennium](https://steambrew.app) for the library-page features (description, achievements, playtime, launch menu, live saving). The app installs its plugin for you.

## Building

Build the plugin first: the app embeds `plugin\dist`, which isn't kept in the repository.

## Contributing
I am 100% open to contributions, the faster this app can get polished and have added game support the better.
Just create a PR, list your changes and why they're a good addition if you're modifying the app or plugin behavior.

If it's just another game getting a data reader set up or an achievement list, make sure you test each achievement and that they properly trigger.

1. **Plugin** (`plugin`, Node 22): run `npm install`, then `npm run build`, then `sh stage.sh`.
2. **App** (`src\ShortcutStudio`, WPF, .NET 8): `dotnet publish -c Release -o ..\publish`, then copy `publish\ShortcutStudio.exe` to the repository root as `Shortcut Studio.exe`.

After any plugin change, rebuild the app too, so saving from the app never installs an older plugin.
