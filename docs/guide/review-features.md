# More world tools

Coatings, paint, gardens, benches, pets and the other smaller tools. They all follow the usual routine: close the game, make the change, press **SAVE**, then load the world. The editor keeps its usual `.bak` copy.

Lists and pickers here come from your installed game, so they match the version you play.

## Weapon coatings

Open a player inventory or a container, select a weapon, and expand **Weapon coating** in the slot editor. Choose a coating and set how much of it is left. A new coating starts at the amount the game gives it. Choose **None** to remove it.

Coating durability is separate from weapon durability. Weapons the game says cannot be coated do not show the picker.

![Expanded weapon coating selector](/screenshots/11-weapon-coating.png)

## Painted objects

Select a base in **Bases** to see **Painted objects**: every placed object in that base that the game lets you paint, such as benches, crates, cubicles, barricades, beds, rugs and lamps. Choose a colour, or **Unpainted / default** to remove it. You can also pick a colour on a piece's card in the [3D view](./3d-view).

## Bench upgrades

Benches list their upgrades in **Bases** and on their card in the 3D view. Tick an upgrade on or off.

## Garden plots

Open a region and choose **Garden plots**. Choose a plot, then a **Planting spot**.

- **Water** belongs to the whole plot.
- **Fertilizer** belongs to one spot. Zero means none; 1,000 is the normal amount.
- **Crop** offers the plants the game lets you grow. Choosing a different crop starts that spot as a fresh planting.
- **Growth stage** uses the game's names: Sprout, Budding, Juvenile, Flowering, Grown, Harvested, Regrowing and Dead. **Growth progress** runs from 0 to 10,000 towards the next stage.

A spot that has never held a crop cannot be planted from the editor, and a planted spot cannot be emptied. Plant it in the game first.

![Garden plot water, fertilizer and growth controls](/screenshots/26-world-garden.png)

## Digital garden plots

**Digital garden plots** have their own tab. Choose a cartridge slot and pick a cartridge: Blank, food, ammunition or Lamogi. Changing a cartridge starts its printing again; the powered plot does the printing in the game.

## Chemistry benches

**Chemistry benches** shows the three input flasks and the output flask of each bench. **Edit flask contents** opens the bench in the container editor. Mixing happens in the game: run the bench there to make something.

![Chemistry bench contents](/screenshots/27-world-chemistry.png)

## Pet feeding and mutation

Select a carried pet under **Companions**, or a placed pet under **Pets**, and expand **Feeding and mutation**. It lists the foods that tame that pet and the foods that mutate it.

Carried pets also show their mutation progress and which mutation they have, and you can change both.

![Carried companions](/screenshots/18-player-companions.png)

## Power Chairs

A region with a placed Power Chair gets a **Power chairs** tab. Set its battery charge from 0 to 200.

## Sconce lamps

A region with placed Sconce lamps gets a **Sconce lamps** tab. Turn **Lamp on** on or off for each lamp, including the Christmas Sconce.

## Story characters

The **NPCs** tab names each story character and says when they appear and leave. **Story stage** is folded away by default. Stage numbers mean different things for different characters, and changing one can skip dialogue, so change it with care.

![World story characters](/screenshots/22-world-npcs.png)

## Traders and containment cells

The story save (**WorldSave_MetaData.sav**) has a **Traders** tab and a **Containment** tab. Traders shows what each trader sells and how to unlock them. Containment sets which creature each cell holds. Both have **Show in 3D** buttons that open the right area and take you there.

![The Traders tab with a trader selected](/screenshots/55-world-traders.png)

## Add a missing sandbox setting

Open `SandboxSettings.ini` and expand **Add a setting**. Pick an option, optionally expand **What this changes**, then add it. It starts at the game's default value and waits until you save the file. **Revert** removes additions you have not saved.

![Sandbox settings editor](/screenshots/25-config-ini.png)

## Breakable objects

**Breakable objects** lists every ice wall, spore web, ceiling tile and other breakable prop the save records as broken. The save only remembers an object once it breaks, so untouched ones do not appear. Turn **Broken** off to repair one.

## Corpses

**Corpses** lists every body still lying in that region by creature type, and whether it was gibbed or looted. **Remove this Corpse** clears it from the save.
