# Game Pass and Microsoft Store saves

Game Pass saves work in Abiotic Editor, but Xbox cloud sync makes them more delicate than Steam saves. The editor can safely open and edit them. The danger comes later, when Xbox sees a different cloud copy and quietly puts it back.

Use this routine every time. It is the best way to make your edited save the copy Xbox keeps.

## The offline routine

1. Close Abiotic Factor and the Xbox app. Check the system tray too, because closing the Xbox window may leave it running.
2. Wait about a minute for the last upload to finish.
3. Turn off Wi-Fi or use airplane mode.
4. While still offline, edit your save and choose **SAVE**.
5. Still offline, start Abiotic Factor, load the world, save in-game, and quit.
6. Reconnect to the internet. If Xbox asks which copy to keep, choose **this device**, **keep local**, or **upload to cloud**.

Step 5 matters. The game needs to open your edit and save it as the newest copy before Xbox syncs again. The editor creates a backup of the whole Game Pass save folder before it writes, but it cannot stop the cloud copy winning later.

::: danger The sync choice removes the copy you do not pick
If Xbox asks you to choose between the PC and cloud copies, the other one is deleted. It does not show a preview or offer undo. If you are uncertain, copy the entire Game Pass save folder somewhere safe before choosing.

The prompt may not appear at all. A save can simply revert, which is why the offline routine is important.
:::

There is no per-game PC switch to disable Xbox cloud saves and no PC Xbox-app button to delete a cloud save. Going offline for the edit is the reliable control you have. A game may also refuse to launch offline until it has been played online on that PC at least once.

## Something already went wrong

### A world will not load

Close the game and Xbox app. Open the save in the desktop editor and choose **Repair now** if it appears. Repair creates a backup first and fixes save information that older editor versions could leave in a state Xbox does not understand.

After repair, follow the offline routine: launch and save in-game while offline, then reconnect. If the world still does not load, stop editing and [request help on GitHub](https://github.com/ChristopherVR/AbioticEditor/issues/new/choose) or [ask on Nexus Mods](https://www.nexusmods.com/abioticfactor/mods/244?tab=posts). Include the editor version and what happened, but never share your save publicly.

### An edit disappeared

The cloud copy probably replaced it. Do not keep reopening and saving the affected world. Go offline, look for the editor's backup, and ask for help if you cannot confidently identify the right copy. Once it is restored, use the offline routine.

### A world vanished from the list

Xbox can leave a world in the folder while no longer listing it. Do not overwrite the save folder. Request help so the recovery can start from the safest copy.

## Where saves are found

The editor looks for Game Pass saves automatically, on every drive. You should not need to enter a path. Typical locations are:

```text
%LOCALAPPDATA%\Packages\<AbioticFactor package>\SystemAppData\wgs\<account id>_<id>\
<drive>:\XboxGames\GameSave\wgs\<account id>_<id>\
```

Each folder belongs to one Xbox account. The discovered-worlds list shows the account ID and folder so you can check before opening anything.


![Game Pass conversion settings](/screenshots/34-convert.png)

*Conversion tools are separate from normal save editing.*

## Moving a world between Steam and Game Pass

Choose **Settings ▸ Convert** to convert a world either way. Difficulty settings travel with it. You can enter a player account ID if you want to hand a character to a different account; leave it blank to retain its current ID.

::: warning Give converted saves a safe destination
A converted Steam world belongs in `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<your steam id>\Worlds\`.

A converted Game Pass save must be merged into an existing Xbox save folder with `gamepass to-gamepass --into`. Do not drag it over your existing Game Pass save folder, because that can hide the worlds already there. Close the game and Xbox app first.
:::

## Command reference

The desktop app is the right tool for ordinary Game Pass editing and repair. Experienced command-line users can find the complete recovery and conversion reference in [Game Pass format: command-line recovery](/reference/game-pass-format#command-reference).
