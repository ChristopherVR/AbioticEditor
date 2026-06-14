# SaveStats - sample plugin

A managed (.NET) **console command** that prints a one-glance summary of a player or world save.
It shows how a plugin can graft a whole new verb into the CLI that behaves exactly like a built-in:
help text, the standard exit codes, and a `--json` switch. It is read-only and never writes.

- **Runtime:** .NET (`SaveStats.dll`)
- **Capability:** console command `save-stats`
- **Surfaced in:** the CLI only (GUI hosts ignore console commands)

## What it does

Detects the save kind and prints a short report:

- **Player saves:** money, hunger, thirst, sanity, skill count, top skill level, recipe and trait
  counts.
- **World saves:** flag, container, door, dropped-item and NPC counts, and minutes played.

Pass `--json` for machine-readable output.

## Manifest

```json
{
  "id": "com.abioticeditor.samples.save-stats",
  "name": "Save Stats",
  "version": "1.0.0",
  "author": "Abiotic Editor samples",
  "description": "Adds a 'save-stats' CLI command that prints a one-glance summary of a player or world save.",
  "entryAssembly": "SaveStats.dll",
  "minHostVersion": "1.0.0",
  "capabilities": ["consoleCommand"],
  "enabled": true
}
```

## How it works

`SaveStatsPlugin.Configure` calls `registry.AddConsoleCommand(new SaveStatsCommand())`. The command
declares one required argument (`save`) and one flag option (`--json`), then reads the save through
`PlayerSaveReader` / `WorldSaveReader` and writes to `ctx.Out`. Writing to the abstract `ctx.Out`
and `ctx.Error` writers (rather than `Console`) keeps the command unit-testable with a
`StringWriter`. It returns the standard CLI exit codes: 0 success, 1 user error (file not found),
2 unexpected failure.

A command name that collides with a built-in or another plugin is skipped with a warning, so a
plugin can never shadow a shipped verb.

## Build

```console
dotnet build plugins/SaveStats -c Release
```

## Try it

Once installed, the command is a top-level verb in `abioticeditor --help`:

```console
abioticeditor save-stats path\to\Player_<id>.sav
abioticeditor save-stats path\to\WorldSave_<Region>.sav --json
```
