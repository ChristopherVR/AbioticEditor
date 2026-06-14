# VersionShim - sample plugin

A managed (.NET) **save upgrader** that recovers a save whose `SaveGameVersion` header field holds a
value the editor cannot load (which is what you see right after a game update bumps the format). It
rewrites that field to the modern supported version, touching only the four version bytes and
leaving the rest of the file intact.

- **Runtime:** .NET (`VersionShim.dll`)
- **Capability:** save upgrader
- **Surfaced in:** the save load path (not a save operation; it runs when a normal load fails)

## What it does

A save **upgrader** is different from a save **operation**: an operation edits an already-loaded
save, but an upgrader runs precisely when loading *failed*, so it works on the raw bytes. When the
host cannot parse a save (unsupported version), it offers the file to each registered upgrader in
turn. `VersionShim` claims a file whose version is neither of the known-good values, then rewrites
the `SaveGameVersion` int32 (at offset 4, just past the `GVAS` magic) to the modern supported value.
The host then loads the corrected bytes and, with your consent, can persist them after a
`.preupgrade.bak`.

## Manifest

```json
{
  "id": "com.abioticeditor.samples.version-shim",
  "name": "Version Shim",
  "version": "1.0.0",
  "author": "Abiotic Editor samples",
  "description": "Recovers a save whose SaveGameVersion field is an unsupported value by rewriting it to the modern supported version (demonstrates the ISaveUpgrader hook).",
  "entryAssembly": "VersionShim.dll",
  "minHostVersion": "1.0.0",
  "capabilities": ["saveUpgrader"],
  "enabled": true
}
```

## How it works

`VersionShimPlugin.Configure` registers a `FixSaveVersionUpgrader : ISaveUpgrader`:

- `CanUpgrade(SaveUpgradeProbe probe)` is a cheap, header-only claim check. It returns `true` only
  when the version is not one the editor already knows, the `GVAS` magic is readable, and the load
  error looks like a version-support problem. It must not parse full properties: it is called for
  every upgrader, on every failed load.
- `UpgradeAsync(...)` clones the original bytes, rewrites the 4-byte `SaveGameVersion` to the modern
  supported value, and returns them via `SaveUpgradeResult.Ok(...)`.

This is a deliberately conservative example: it changes only the version field. A real upgrader for
a structural format change would transform more, but the contract is the same - return corrected
bytes and let the host decide whether to persist.

## Build

```console
dotnet build plugins/VersionShim -c Release
```

## Try it

The upgrader runs automatically when you open a save the editor cannot otherwise load. Install it,
then open the affected save in the app (or load it via the CLI); the editor reports which upgrader
recovered the file and offers to persist the corrected version.
