# RepairNeeds - sample plugin

A managed (.NET) **save operation** that tops every survival need back to full on a player save.
A good example of a parameter-free operation and of handling the game's delta-serialization.

- **Runtime:** .NET (`RepairNeeds.dll`)
- **Capability:** save operation `repair-needs`
- **Applies to:** player saves

## What it does

Restores hunger, thirst, sanity, fatigue, and continence to 100. It also fixes the case where a
need reads as 0 because it was at its blueprint default and therefore omitted from the file (the
game delta-serializes, so a missing property is a real value, not "no data"). Money and everything
else are left untouched. If every need is already full it reports a no-op.

## Manifest

```json
{
  "id": "com.abioticeditor.samples.repair-needs",
  "name": "Repair Needs",
  "version": "1.0.0",
  "author": "Abiotic Editor samples",
  "description": "Adds a 'repair-needs' save operation that tops every survival need back to full on a player save (also fixes needs that read as 0).",
  "entryAssembly": "RepairNeeds.dll",
  "minHostVersion": "1.0.0",
  "capabilities": ["saveOperation"],
  "enabled": true
}
```

## How it works

`RepairNeedsPlugin.Configure` registers a `RepairNeedsOperation : ISaveOperation` with no
parameters. It reads the typed model with `PlayerSaveReader`, counts the needs below full, sets
them all to 100 with `PlayerSaveWriter.ApplyStats(...)`, then calls `context.MarkChanged()`. If
nothing was below full it returns `SaveOperationResult.NoChange(...)` and the host writes nothing.

## Build

```console
dotnet build plugins/RepairNeeds -c Release
```

## Try it

```console
abioticeditor plugins run repair-needs path\to\Player_<id>.sav
abioticeditor plugins run repair-needs path\to\Player_<id>.sav --dry-run
```

Or from the app: Settings, Manage Plugins, SAVE OPERATIONS, RUN.
