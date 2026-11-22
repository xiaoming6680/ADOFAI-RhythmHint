# Changelog

[中文](CHANGELOG.md) | English

What changed in each version, newest first.

## Unreleased

- The mirror site gh-proxy.com stopped working; auto-update now uses edgeone.gh-proxy.org instead (ghfast.top and ghproxy.net are unchanged)

## 1.3.5 · 2026-11-20

- Fixed: the purple tiles in RJ-X that need two keystrokes at once (multitap tiles) showed a single note, so pressing once as hinted didn't get you through. They now show a double on one hand (tiles needing three or four taps show a triple or quad); if Multitap Tile Behavior is set to Hit Once in the game's settings they still show a single, and changing the setting updates the hints right away. Custom fingerings made earlier get keys added or removed on multitap tiles to match the current setting

## 1.3.4 · 2026-11-20

- Fixed: when two hold tiles were next to each other (for example the workshop level DoG Nonstop Mix), the hints merged them into one long hold, but the game needs a new press on the second one, so following the hints got you killed. Each hold tile now has its own hint, held until the next tile. On levels with such holds, custom fingerings made earlier fall back to automatic fingering around those holds
- Back-to-back holds now alternate hands: while one hold is still down, the next one goes to the other hand, and you let go of the first after pressing, instead of releasing and re-pressing the same key on the exact beat
- Fixed: in the fingering editor, dragging a chord selected with a box moved only the key under the cursor. Box-selected notes now move together; dragging one key of a chord directly still moves only that key

## 1.3.3 · 2026-11-20

- When auto-update can't reach GitHub, it downloads through mirror sites in mainland China instead (ghfast.top, gh-proxy.com, ghproxy.net); the download is still checked against its SHA-256. 1.3.2 and earlier only use GitHub, so if GitHub can't be reached, install this version by hand once
- Fixed: on computers with fonts such as Cascadia Mono installed, the version number, hotkey keycaps and status in the settings window showed nearby heading text instead, and the window lagged and kept growing the game log (Player.log) while open
- Fixed: on high-refresh-rate monitors the hints didn't fall smoothly and looked like they weren't running at the monitor's frame rate. The game's song position jumps a little every time it syncs to the audio clock; the hints now advance evenly with real frame time and match the game's song position on average, so judgement is unaffected. The demo on the settings page is smoother too

## 1.3.2 · 2026-11-19

- Triples and quads on the taiko bar now show 3 or 4 in the middle, so you can tell them apart with only the taiko bar on; turn it off under F8 → Appearance → Show if you don't need it
- Fixed: on screens above 1080p, expanding Advanced in the settings window moved its arrow to the top-left corner of the window
- Fixed: one broken fingering file in the folder (for example a field of the wrong type or a number that is too large) disabled the whole mod, again on every launch. Now only that file is skipped and listed on the Custom fingering page
- Fixed: the mod could not read a fingering whose author or comment happened to be a date and time (for example 2026-11-18T00:00:00Z)
- Custom fingerings are backed up to Documents\RhythmHint\FingeringsBackup automatically. Uninstalling or reverting the mod with UMM deletes the whole mod folder; the fingerings are restored from the backup on the next launch
- When saving a fingering, a file with the same name that belongs to another chart (another charter's chart of the same song, or one shared by someone else) is no longer overwritten; the new one is saved as name (2).json
- Fixed: in the fingering editor, saving, undoing and then editing something else still showed the level as saved, while the game still had the saved version
- Fixed: when the game exported a level with its notes grouped differently (for example after a mod update changed the chord rules), unsaved progress in the editor was not restored and was lost on the next edit. Progress is now restored by tile, the editor tells you how many edits could not be restored, and switching to left-handed restores correctly too
- Fixed: the fingering editor's track preview froze for seconds and used several GB of memory on levels with many Move Track events
- Fixed: in the fingering editor, the first few hit sounds played ahead of the music after starting playback, seeking, changing the practice speed, looping back, or starting before the song begins
- Beat and measure lines are now inferred from the level's speed changes and tiles: beats are counted continuously across speed changes, levels whose BPM is far above the music no longer show overly dense beat lines, and measures start on accents such as chords and holds; the note values in the fingering editor follow the beat lines too
- The fingering editor's track preview now replays Repeat Events and Conditional Events, shows tiles with opacity above 100% correctly, keeps the planets on the last tile at the end of a level, and plays more smoothly on levels with many tiles
- Custom fingerings: duplicate files for the same chart no longer block automatic selection, fingerings with a different key count are now noted as unsupported in the settings, entering a level no longer stalls when many fingerings are installed, and large fingering files load faster with much less memory
- The fingering editor keeps fields added by newer versions when saving and no longer saves files the game cannot read; files over 20 MB are not opened; suggested file names avoid Windows reserved names and control characters
- Fingering editor: pressing N moves through every spot to review in turn and wraps around at the end; auto-filled notes stop being marked for review once you change or confirm them; holds stay on their own key when mirroring, moving one key or applying an auto fingering
- Fingering editor: the File panel can change the save location, choosing a wrong folder asks you to choose again, and a remembered folder that can no longer be used is asked for again; downloading the file no longer counts as saved; a changed file name is kept with your progress
- Fingering editor: it warns when lane keys clash with shortcuts, game keys such as the numpad and Shift work in it, and Ctrl+S in a text box saves the fingering instead of opening the browser's save page dialog
- Fingering editor: clicking levels quickly in History opens the last one clicked, tabs editing the same level no longer overwrite each other's progress, and it tells you when browser storage is full and keeps progress for the latest 30 levels
- Fingering editor: a loop whose start and end are too close is no longer turned on (it used to stall in silence), hit sounds at the end of a loop are no longer dropped with visual calibration set, the play button resets when the song ends, long songs no longer freeze the view briefly after loading, long charts play more smoothly, and the view stays sharp after moving to a display with different scaling
- Custom fingerings follow chart updates more closely: changing, adding or removing a tile inside a stream no longer shifts the run before it by one key, and unchanged charts are recognized faster when entering a level
- Fixed: opening someone else's fingering for the same chart in the fingering editor, or editing after switching to another fingering in the game, put your own earlier progress on it. Progress is now picked up automatically only when it belongs to the same fingering; otherwise the editor asks first
- Fingering editor: editing or switching levels while saving to the game no longer marks unsaved changes as saved, and pressing Ctrl+S again no longer starts a second save; discarding restored progress no longer throws away edits made afterwards; when browser storage is full, older unsaved progress is cleared and you are told how many levels were affected
- Fingering editor: music you pick right after opening a level is no longer replaced by the level's own song; switching levels quickly decodes one song at a time and uses less memory; the Reference panel shows both auto styles as soon as it opens
- Fingering backup: if some fingerings cannot be read when restoring after a UMM reinstall, the backup is no longer deleted and they are restored on the next launch; two game installs no longer delete each other's backed-up fingerings
- A damaged or unreadable settings file no longer resets everything: the original is kept as a copy, readable settings (including per-chart fingering choices) are kept, and the settings window tells you; out-of-range values in a hand-edited settings file are corrected instead of disabling the mod
- Auto-update fully restores the old files if installation fails partway, and no longer stays stuck downloading after an error
- Fixed: Edit this level's fingering failed when a hold tile was followed by a destroyed tile, or when an entry in the editor's History was damaged; levels with extremely dense speed changes no longer stall on load, levels with invalid speed values no longer disable the mod, and song titles that look like dates show as written in the editor's History
- Large fingering folders no longer cause a hitch every second while playing; file lists in English, Japanese and Korean use the right separator

## 1.3.1 · 2026-11-19

- Automatic updates: when the game starts, the mod checks for a new version, downloads and installs it in the background, and the update takes effect after you restart the game; your settings and fingerings are kept. You can turn it off on F8 → General. If the download fails, the About page lets you retry, or you can update from the group files or the download page as before. Update checks used to rely on UMM, which often never ran on some networks; the mod now checks by itself
- Fixed: in the fingering editor, music and notes fell out of sync after pausing and resuming or scrolling during playback (OGG and MP3 songs of custom levels). The song is now decoded in full before it plays, so every jump lands exactly, and custom levels get a waveform too. For levels opened before, click Edit this level's fingering in the game once more
- Fixed: with the game installed under Program Files on drive C (Steam's default location), Save to game in the fingering editor was blocked by the browser (it said the folder contains system files). The editor now saves to Documents\RhythmHint\Fingerings and the game moves the file into its fingering folder automatically

## 1.3.0 · 2026-11-19

- The settings window is redesigned: F8 opens it with pages on the left (Overview, Appearance, Fingering, Custom fingering, Calibration, General, About), icons and animations; a short guide plays the first time it opens, and the question mark in the top right shows it again. The window sits in the middle of the area the hint panels leave free and moves aside when they switch sides
- Custom fingering: make your own fingering for a song, or use one someone shared. Put it in Mods/RhythmHint/Fingerings and it's used automatically for that song; if the chart is updated, parts that no longer match use auto fingering, with a notice when the level starts
- New fingering editor: F8 → Custom fingering → Edit this level's fingering opens the level in your browser with its music, both auto styles for comparison, beat lines and a track preview (experimental); drag notes to change lanes, and saved changes take effect in the game right away; five languages
- The fingering editor can be closed halfway and keeps your changes; History on the left lists the levels you recently opened from the game, click one to pick up where you left off. The Custom fingering page can create a desktop shortcut, so the editor opens without the game
- The fingering editor uses the game's visual calibration, plays hit sounds on the musical beat, and shows a faint path through hidden tiles; practice speed changes the pitch of the music
- Estimated difficulty (experimental): in a level, the left side of the BPM box shows a TUF difficulty tier estimated from the level's tiles. It's for reference only and not an official TUF rating; turn it off on the Appearance page
- New fingering style option on the Fingering page: Standard (default) and Alternate first (experimental: fast short runs alternate both index fingers, with fewer hand changes around chords). Alternate first was contributed by Linxi-1105
- The main-hand reset gap now defaults to 130 ms instead of 165 ms: fast sections around 75 ms alternate both index fingers, bursts within 65 ms still roll on one hand, and notes 130 ms or more apart are tapped with one finger. It follows a frame-by-frame study of an Avantgarde perfect clear and matches the keys in that video 86% of the time (44% before). Settings still at 165 ms are moved to 130 ms automatically
- The four-lane panel can go on the right and the taiko bar at the bottom
- With the taiko bar off, its whole strip is gone and the game view no longer makes room for it; BPM and time left stay in the four-lane panel's column
- The on-screen keyboard for custom keys adds the numpad, F1–F12, editing keys such as Insert and Home, and mouse side buttons; the hotkey that opens the settings window can be customized the same way

## 1.2.1 · 2026-11-18

- Fixed: with Fit game view beside panel on, only the top edge of the autoplay button in the editor's bottom-right corner could be clicked during playback
- Fixed: in the level editor, notes on the lanes and the taiko bar kept moving after you died

## 1.2.0 · 2026-11-17

- Interface in Simplified Chinese, Traditional Chinese, English, Korean and Japanese; follows the game language by default, or pick one on F8 → Status
- Triple and quad chords are detected by the angle between tiles, so very fast streams are no longer taken as doubles; triples and quads are purple
- On the Fingering page, the chord time threshold (ms) is replaced by Max chord angle (°)
- New Chord hand setting on the Fingering page: chords can go to the other hand
- On the taiko bar, later notes are drawn on top of earlier ones
- The settings window's title bar tells you when a new version is out; right-click the mod on the Mods tab of the UMM installer and choose Update to update in one click
- The Status page now shows the author, the QQ group and the GitHub link

## 1.1.0 · 2026-11-17

- Custom keys: F8 → Display → Custom, pick your four keys on the on-screen keyboard; Shift, Space and arrow keys work too
- Round note style like the taiko bar, with judgment rings instead of key boxes
- The two millisecond settings on the Fingering page show the equivalent straight-path BPM
- Settings window re-laid out with shorter descriptions
- Key presets reduced to D F J K; use Custom for other keys (existing settings are kept)
- Fixed: the editor's autoplay button didn't respond during autoplay
- Fixed: pseudo-double notes moved past the judgment line after the first tile was hit

## 1.0.0 · 2026-11-16

- Fingering reworked around the gap between hits: right-hand J when slow, J F alternating when faster, same-hand rolls K J D F when very fast, back to the right hand after a pause
- Doubles go to the right hand and singles right next to them move to the left, so no more J → J+K → J and no index-then-outer-finger moves
- New Dominant hand setting (F8 → Fingering): left-handed players can mirror the whole layout
- Fit game view beside panel: the game view, combo, judgments, accuracy bar and pause menu move out from under the panel
- The panel hides when you pause with Esc
- Each settings page can be reset to its defaults
- The release zip can be installed directly with the UMM installer
