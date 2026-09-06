# Live-editing area modules

`main.lua` owns the transport (file mailbox, game-thread dispatch, the original areas). Every
area added from round 76 on is its own file in this folder, listed in `manifest.lua`, so
several areas can be built in parallel without everyone editing `main.lua`.

## Contract

```lua
-- Scripts/areas/story.lua
return function(ctx)
    ctx.handlers["story.get"] = function(payload, respond)
        ctx.runOnGameThread(function()
            -- touch live UObjects ONLY inside runOnGameThread (off-thread reflection froze the
            -- game once, see docs/PROGRESS.md round 67); `error("player-safe text")` becomes
            -- the ok:false message the editor shows verbatim.
            return { chapter = "Office" }
        end, respond)
    end
end
```

- The module is `return function(ctx) ... end`; register handlers on `ctx.handlers`.
- Command names are `<area>.<action>` (`story.get`, `story.set`, `bases.list`...). The native
  helper forwards every command by name, so nothing else needs registering or rebuilding.
- Host-only writes must check `ctx.isHost()` first and `error("only the host can ...")`.
- Everything in `ctx` is a function main.lua already uses live: `runOnGameThread`, `isHost`,
  `getMyPlayer`, `resolvePlayer(payload)` (honours `payload.playerId`), `allPlayerStates`,
  `playerId`, `inventoryComponent(player, kind)`, `slotRowName`, `slotRow(slot, index)`,
  `writeSlot(slot, row)`, `findAll(className)`, `findByFullName(className, fullName)`,
  `fullName(obj)`, `classLabel(fullName)`, `actorLocation(actor)` -> x, y, z, `outNames(fill)`
  (reads a `TArray<FName>&` out-param), `dayNightManager`, `weatherLibrary`,
  `worldFlagSubsystem`, `worldFlagLibrary`, `currentWorldFlags`, `containerInventory`,
  plus `json` and `UEHelpers`.
- Lists return `{ <plural> = { __forceArray = true, ... }, isHost = ctx.isHost() }`
  (`__forceArray` makes json.lua emit `[]` for an empty list). Object ids are `ctx.fullName(obj)`
  and are re-resolved with `ctx.findByFullName` on every write.
- Ground every property/function name in the game's own class layouts
  (`tests/AbioticEditor.Probes/LiveClassPropsProbe.cs` dumps them from the paks; the shipped
  `AbioticFactor-Win64-Shipping.pdb` yields native signatures via `grep -a`) or in a real mod
  (`ue4ss/Mods/CheatConsoleCommands`). Wrap anything without a working precedent in `pcall`
  and say so in a comment.
- Syntax-check with a Lua 5.4 interpreter before installing (`lua -e "assert(loadfile(...))"`).
- Add the module name to `manifest.lua`, and document the wire shape in
  `docs/reference/live-editing-protocol.md`.
