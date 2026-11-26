# Changelog

[中文](CHANGELOG.md) | English

What changed in each version, newest first.

## Unreleased

- New 6K (S D F J K L) and 8K (A S D F J K L ;), experimental. By default the key count is chosen from the estimated difficulty: P tiers stay 4K, 6K from G1, 8K from U6; a custom fingering in use is shown with its own key count. Under F8 → Keys you can turn the automatic choice off and pick 4K / 6K / 8K, or change the tiers where 6K and 8K start; the keys for each key count are set there too
- 6K and 8K have a layout of their own instead of copying 4K: slow notes on the right index finger, medium ones alternating the index fingers; fast runs roll from the outside in to the index finger on one hand and move to the other hand only once every finger of the first has played, so short runs keep off the middle whenever they can (K J for two notes and L K J for three in 6K; long runs L K J S D F in 6K, ; L K J A S D F in 8K); chords stay on one hand when it has enough fingers; where a finger would have to press again before it could rest (such as the same key right after a double), another rested finger takes the note. 4K fingering is unchanged, note for note
- 6K and 8K custom fingerings now work: a 4K fingering is used on the middle four keys in 6K and 8K; in the editor, 6K and 8K key names, unsaved drafts and history are kept apart from 4K, and the suggested file name ends with 6K or 8K so it doesn't overwrite the 4K fingering of the same song
- In 6K and 8K the falling panel uses slightly narrower lanes, 336 px wide for 6K and 384 px for 8K (1080p, scale 1); the game view makes room for it
- Two new fingering styles, both experimental (F8 → Fingering → Style), designed by Linxi-1105: Hands by beat uses the beat and the direction each tile comes from to keep each hand on its own rhythm; Steady right hand (strong and weak beats) only looks at hit intervals: slow hits stay on the right index finger, dense ones alternate, and dotted rhythms share one time unit. In both styles, fake chords made of three or more ordinary tiles are hinted one tile at a time. In 6K and 8K every note stays on the hand it has in 4K; fast runs played by one hand roll from the outside in over more of that hand's fingers, so fingers rest longer, with no more lane jumps than in 4K. The fingering editor gets a Hands by beat reference column that can be applied to a selection or the whole chart. With Hands by beat selected, the settings page hides the rhythm thresholds, hand-off around chords and same-key minimum gap, which it does not use. The default style is unchanged
- Plainer names and descriptions on the fingering page: Main-hand reset gap is now Hand-switch gap, Chord hand-off range is Hand-off around chords, Min. same-key repeat gap is Same-key minimum gap, and Max chord angle is Chord merge angle; the four style descriptions are rewritten. Your values are kept
- The falling panel can sit in the middle of the screen (F8 → Appearance → Position); it is then drawn over the game view and the BPM box moves to the left
- Pseudo chords (doubles, triples and quads made of several closely spaced tiles) now reach the judgment line halfway between their first and last tile, on the falling panel, the taiko bar and in the fingering editor. They used to arrive with the first tile, so pressing all keys at once as shown always hit the later tiles early
- New judgment hints on the falling panel: when a press is off, a small line of text appears above the judgment line of its lane and fades quickly, using the game's own judgment names and colors (following the game's language and judgment color settings, without punctuation). By default only non-perfect judgments such as early and late are shown; turn it off or also show perfect judgments in F8 → Appearance → Falling panel
- The BPM box now shows the real BPM next to the BPM label: the actual press rate, 60 divided by the seconds between two consecutive presses. It stays constant through evenly spaced sections such as snowflakes and straight lines, so it works as a reference for hand speed. The large number is still the tile BPM; a chord counts as one press, and pauses longer than 2 seconds show -. On by default; turn it off in F8 → Appearance → BPM and time left
- Panel background opacity now also applies to the key boxes and judgment circles, so a centered panel no longer hides the accuracy on the results screen when made transparent; pressed keys, outlines and key names stay readable
- Panel background opacity can now go down to 0, and a new Note opacity setting (F8 → Appearance → Size & speed) fades the notes on the falling panel and the taiko bar while the judgment line and key boxes stay as they are
- Changing the key count or the panel position, or turning the falling panel, the taiko bar or the BPM box on or off, now animates smoothly instead of jumping: the taiko bar slides in and out from the screen edge with the game view making room, and the BPM box slides away while the rhythm track stretches to fill its place; the panel no longer stutters the first time it slides in with 6K or 8K
- The display switches on the Appearance page are split into three cards, Falling panel, Taiko bar and BPM and time left: judgment hints, chord numbers, the estimated difficulty and the real BPM sit in a drawer under their card that closes while that item is off; the Light up key boxes on press switch is gone and key boxes always light up when pressed
- The settings window is regrouped so things are easier to find: a new Keys page holds the key count, the keys for each key count and the hotkey that opens the settings; the rhythm thresholds sit right under the layout choice on the Fingering page, ready to raise when single-finger streams get too fast, while rolls and other rarely changed options are folded into Advanced, whose header shows the current values; the demo key count moved to Calibration; the update notice now sits next to the automatic update switch on the General page. The settings file is unchanged, so everything you set is kept, and the guide runs once more the first time you open the window
- The fingering editor shows the key count of the level in the File panel on the left; levels opened from the game can switch between 4K, 6K and 8K there, each saved as its own file with unfinished edits kept
- The fingering editor's scrollbars are now slim and rounded instead of the system's grey bars with arrows
- The mirror site gh-proxy.com stopped working; auto-update now uses edgeone.gh-proxy.org instead (ghfast.top and ghproxy.net are unchanged)
- Fixed: with the Alternate first layout, setting Short run: shortest gap above Short run: longest gap (for example dragging it all the way right after raising the rhythm thresholds) stopped the mod with an error; the longest gap now moves up with it, and the short-run note counts behave the same way

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
