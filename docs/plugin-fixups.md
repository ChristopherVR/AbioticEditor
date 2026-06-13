# Plugin cookbook: fixing up saves over time

The plugin system exists so the editor can keep reading, writing, and **repairing** saves as
Abiotic Factor changes - without shipping a new build of the app. A "fix-up" is just an
[`ISaveOperation`](../src/AbioticEditor.Plugins.Abstractions/Saves/ISaveOperation.cs) that
repairs something: a value a patch corrupted, a flag the game dropped, content the editor's
own UI does not model yet, or a save a newer game version wrote.

This page is a recipe collection. For the full SDK reference see
[`plugin-authoring.md`](plugin-authoring.md); for the architecture see
[`plugins.md`](plugins.md).

## The shape of a fix-up

Every fix-up follows the same contract, and the host enforces the dangerous parts so the
plugin can't get them wrong:

1. The host loads the save and hands you `context.Save` (a `SaveGame`) plus the detected
   `context.Kind`.
2. You inspect and mutate the save - either through a typed Core reader/writer, or directly
   on `context.Save.Properties`.
3. You call `context.MarkChanged()` **only if** you actually changed something.
4. You return a `SaveOperationResult`.
5. The host backs the file up (`.bak`) and rewrites it **only** when you marked a change and
   it is not a dry run. Nothing is written otherwise.

Keep operations pure (no static state) and idempotent (re-running does nothing the second
time). Report `NoChange` when there is nothing to do so the host skips the write entirely.

## Recipe 1 - repair a corrupted/zeroed value (typed writer)

Game delta-serialization omits default-valued tags, so a need the game never wrote reads back
as `0`. The [`RepairNeeds`](../plugins/RepairNeeds) sample tops every survival need back to
full using the typed player reader/writer:

```csharp
public SaveKind AppliesTo => SaveKind.Player;

public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken ct = default)
{
    var data = PlayerSaveReader.ReadFrom(context.Save);   // data.Raw IS context.Save
    var below = CountNeedsBelowFull(data.Stats);
    if (below == 0)
        return Task.FromResult(SaveOperationResult.NoChange("all needs are already full."));

    PlayerSaveWriter.ApplyStats(data, data.Stats with
    {
        Hunger = 100, Thirst = 100, Sanity = 100, Fatigue = 100, Continence = 100,
    });
    context.MarkChanged();
    return Task.FromResult(SaveOperationResult.Ok($"restored {below} need(s).", below));
}
```

The typed reader gives you a model over the *same* `SaveGame` the host will persist, so the
writer's edits land in the file. Prefer this whenever Core already models the data.

## Recipe 2 - add a flag the editor doesn't model (raw property edit)

When you need to touch something Core has no vocabulary for - a brand-new quest flag, a value
a future patch introduced - edit the property tree directly. The
[`GrantFlag`](../plugins/GrantFlag) sample adds an entry to a world save's `WorldFlags` array:

```csharp
public SaveKind AppliesTo => SaveKind.World;

public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken ct = default)
{
    var flag = context.GetParameter("flag").Trim();
    if (context.Save.Properties.FindByPrefix("WorldFlags")?.Property is not ArrayProperty array)
        return Task.FromResult(SaveOperationResult.Failed("no WorldFlags array."));

    var current = (array.Value as Array)?.Cast<object?>().Select(v => v?.ToString() ?? "").ToList()
                  ?? new List<string>();
    if (current.Contains(flag, StringComparer.Ordinal))
        return Task.FromResult(SaveOperationResult.NoChange($"'{flag}' already set."));

    current.Add(flag);
    array.Value = current.Select(f => new FString(f)).ToArray();
    context.MarkChanged();
    return Task.FromResult(SaveOperationResult.Ok($"added '{flag}'.", 1));
}
```

`FindByPrefix` (from `AbioticEditor.Core.Saves.PropertyTagExtensions`) is the right way to find
a property - save property names carry blueprint hash suffixes, so always prefix-match.

This pattern generalizes to the other examples you'll want over time:

- **Restore a missing special backpack's slots.** Read the equipped backpack from
  `EquipmentInventory[3]`, look up its capacity + special-slot indices in
  `BackpackSpecialSlotCatalog`, and resize/pad the `Inventory` array to match (pad with the
  `"Empty"` sentinel). Mark changed only if the array length or special slots were actually
  wrong.
- **Fix a journal / fish / compendium entry.** Find the relevant array
  (`Compendium_*Sections_`, `Compendium_Fish`, journal arrays) and add the missing row,
  preserving existing placements.

## Recipe 3 - handle a save the editor can't yet read/write

This is the "a new game version shipped and `LoadFrom` throws" case, and it has a first-class
hook: [`ISaveUpgrader`](../src/AbioticEditor.Plugins.Abstractions/Saves/ISaveUpgrader.cs).
When the host fails to parse a save, it builds a header-only `SaveUpgradeProbe` (version
fields, save class, the load error) and offers it to each registered upgrader; the first one
whose `CanUpgrade` returns true gets the raw bytes and returns corrected bytes, which the host
then loads (and, with consent, persists after a `.preupgrade.bak`).

```csharp
public bool CanUpgrade(SaveUpgradeProbe probe) =>
    probe.SaveGameVersion is not (2 or 3)                 // an unsupported version...
    && (probe.LoadError?.Contains("support") ?? false);   // ...that LoadFrom rejected.

public Task<SaveUpgradeResult> UpgradeAsync(ISaveUpgradeContext ctx, CancellationToken ct = default)
{
    var bytes = (byte[])ctx.OriginalBytes.Clone();
    BitConverter.GetBytes(3).CopyTo(bytes, 4);            // rewrite the version field
    return Task.FromResult(SaveUpgradeResult.Ok(bytes, "version field repaired."));
}
```

Register it with `registry.AddSaveUpgrader(...)`. The host drives this through
[`SaveUpgradeService.LoadAsync`](../src/AbioticEditor.Core/Plugins/SaveUpgradeService.cs),
which falls back to the upgraders only when the normal parse fails (and rethrows the real load
error when none can help). See the [`VersionShim`](../plugins/VersionShim) sample and
[`SaveUpgradeServiceTests`](../tests/AbioticEditor.Tests/SaveUpgradeServiceTests.cs) for the
full round trip. A real game-format change would do a deeper transform than the version-field
rewrite shown here, but the contract is the same.

## Testing a fix-up

Drive your operation through the real
[`SaveOperationRunner`](../src/AbioticEditor.Core/Plugins/SaveOperationRunner.cs) against a
throwaway copy of a fixture - that exercises the whole load -> kind-check -> execute ->
backup+write path. See
[`PluginFixupTests`](../tests/AbioticEditor.Tests/PluginFixupTests.cs) for the pattern:
assert the post-condition on reload, that a `.bak` appears only on a real write, that a dry run
leaves the bytes untouched, that the wrong save kind is rejected, and that a second run is a
no-op (idempotence).
