# MaxSkills - sample plugin

A managed (.NET) **save operation** that levels every skill on a player save up to a target level.
This is the smallest useful managed plugin and the canonical starting point for authoring one.

- **Runtime:** .NET (`MaxSkills.dll`)
- **Capability:** save operation `max-skills`
- **Applies to:** player saves

## What it does

Reads the player save's skill array, computes the XP threshold for the target level, and raises any
skill below it to that level. It **never lowers** a skill that is already over the target, and if
nothing needs raising it reports a no-op so the host writes nothing. The default level is 10
(the maximum is 20).

## Manifest

```json
{
  "id": "com.abioticeditor.samples.max-skills",
  "name": "Max Skills",
  "version": "1.0.0",
  "author": "Abiotic Editor samples",
  "description": "Adds a 'max-skills' save operation that levels every skill on a player save to a target level.",
  "entryAssembly": "MaxSkills.dll",
  "minHostVersion": "1.0.0",
  "capabilities": ["saveOperation"],
  "enabled": true
}
```

## How it works

`MaxSkillsPlugin.Configure` registers a single `MaxSkillsOperation : ISaveOperation`. The operation:

1. Reads the `level` parameter (default `10`) and clamps it to 1-20.
2. Loads the typed model with `PlayerSaveReader.ReadFrom(context.Save)`. The reader's `.Raw` is the
   same `SaveGame` the host will persist, so edits land in the right instance.
3. Raises each skill below the target XP, counting the changes.
4. Calls `PlayerSaveWriter.ApplySkills(...)` and then `context.MarkChanged()`. Without
   `MarkChanged()` the host skips the write **and** the backup.

The host owns persistence: it loads the save, enforces the player-save kind match and the
parameter, runs the operation, and only if a change was marked (and it is not a dry run) keeps a
`.bak` and rewrites the file.

## Build

```console
dotnet build plugins/MaxSkills -c Release
```

Output is `MaxSkills.dll` plus `plugin.json` (the SDK/Core references are compile-only).

## Try it

```console
abioticeditor plugins run max-skills path\to\Player_<id>.sav --param level=15
abioticeditor plugins run max-skills path\to\Player_<id>.sav --dry-run
```

Or from the app: Settings, Manage Plugins, SAVE OPERATIONS, RUN (against the open save; the editor
reloads afterward).
