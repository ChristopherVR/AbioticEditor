# Transfer items between saves

The desktop editor can move an item between containers in two world saves. These can be two
regions of one world or two separate playthroughs. This tool is unavailable in the browser editor.

## Move an item

1. Close the game or stop the server for both worlds. Keep a separate backup of each world.
2. Open the world's **Containers** tab and follow **Move items to a different world save**.
3. Load a `WorldSave_<Region>.sav` on side A and a different save on side B.
4. Choose a container on each side. Select a filled slot to hold its item, then an empty slot
   on the other side to place it. The move is staged in both loaded saves.
5. Save **both sides**. Each save writes independently and keeps its own `.bak`.

Saving only one side leaves the move incomplete. If a save fails, keep the tool open, resolve
the error, and save that side before playing either world. Loading another file discards that
side's staged changes, so finish and save the move first.

The tool loads its own copies of the two saves, separate from the main workspace. Reload a save
in the main editor after a transfer, and avoid editing the same file in both places at once.

## Item appearance variants

Some saved items carry a texture variant, such as poster artwork or helmet color. The slot
editor shows the variant row when the game recorded one. It is a raw game row name, not a
curated artwork picker. The editor preserves that value when moving the item; it does not
invent a missing variant entry.

See the [desktop tour](./desktop-app) for ordinary container editing.
