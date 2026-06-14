# GrantFlag - sample plugin

A managed (.NET) **save operation** that inserts a named entry into a world save's `WorldFlags`
array. It is a forward-compatible **fix-up**: a way to add a quest flag the editor does not model
yet (for example a flag a new game patch introduced), by writing the raw property directly.

- **Runtime:** .NET (`GrantFlag.dll`)
- **Capability:** save operation `grant-flag`
- **Applies to:** world saves

## What it does

Finds the `WorldFlags` array on a world save, checks whether the named flag is already present, and
adds it if it is missing. Working at the raw property-tree level (rather than through a typed model)
is what makes it forward-compatible: it needs no built-in knowledge of the flag, so it can grant a
flag this build of the editor has never heard of. If the flag is already set it reports a no-op.

See [Plugin fix-ups](../../docs/plugin-fixups.md) for the wider pattern of using plugins to recover
or patch saves a game update changed.

## Manifest

```json
{
  "id": "com.abioticeditor.samples.grant-flag",
  "name": "Grant World Flag",
  "version": "1.0.0",
  "author": "Abiotic Editor samples",
  "description": "Adds a 'grant-flag' save operation that inserts a named entry into a world save's WorldFlags array (forward-compatible fix-up).",
  "entryAssembly": "GrantFlag.dll",
  "minHostVersion": "1.0.0",
  "capabilities": ["saveOperation"],
  "enabled": true
}
```

## Parameters

| Name | Required | Description |
|---|---|---|
| `flag` | yes | The `WorldFlag` name to add, for example `Office_PowerRestored`. |

## How it works

`GrantFlagPlugin.Configure` registers a `GrantFlagOperation : ISaveOperation` whose `AppliesTo` is
`SaveKind.World`. The operation reaches into `context.Save.Properties` to find the `WorldFlags`
array (raw `FPropertyTag`s), appends the new flag entry if absent, and calls `context.MarkChanged()`.
It depends only on the SDK and `UeSaveGame`, not on Core's typed world reader, which is why it can
edit flags the editor has no model for.

## Build

```console
dotnet build plugins/GrantFlag -c Release
```

## Try it

```console
abioticeditor plugins run grant-flag path\to\WorldSave_<Region>.sav --param flag=Office_PowerRestored
abioticeditor plugins run grant-flag path\to\WorldSave_<Region>.sav --param flag=Office_PowerRestored --dry-run
```

Or from the app: Settings, Manage Plugins, SAVE OPERATIONS, RUN (set the `flag` parameter first).
