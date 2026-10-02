# World list pictures

The Doors, Buttons, Breakable Objects, Elevators, Trams, World Teleporters, Resource Nodes and
Containers lists show a picture of each kind of thing, drawn on its own with the game's own models. The pictures are
rendered once by a maintainer, checked in under `src/AbioticEditor.Web.Shared/wwwroot/thumbs`, and
ship with the editor; nothing is rendered or downloaded while the editor runs. Re-render after a game
update that changes these models, or to cover kinds from a world the list did not include.

1. Make the target list from a world folder with every region visited (one actor per kind):

   ```console
   set THUMBNAIL_SAVES_DIR=C:\path\to\Worlds\YourWorld
   set THUMBNAIL_TARGETS_OUT=%TEMP%\thumb-targets.json
   dotnet test tests/AbioticEditor.Probes -f net10.0 --filter "FullyQualifiedName~ThumbnailTargetsProbe"
   ```

2. Start the editor so it can read the game (headless is fine):

   ```console
   set ABIOTIC_EDITOR_NO_DESKTOP=1
   set ABIOTIC_EDITOR_URL=http://127.0.0.1:37361
   dotnet run --project src/AbioticEditor.Web -f net10.0
   ```

3. Render (first time: `npm install` and `npx playwright install chromium` in this folder):

   ```console
   node render.mjs http://127.0.0.1:37361 %TEMP%\thumb-targets.json 256
   ```

   Containers are drawn from their own model instead: set `THUMBNAIL_CLASSES_OUT` too in step 1
   and render that file the same way (`node render.mjs http://127.0.0.1:37361 %TEMP%\container-classes.json 256`).

   It writes `wwwroot/thumbs/<kind>/<class>.webp` and regenerates
   `src/AbioticEditor.Web.Shared/Services/WorldThumbnails.Index.g.cs`, which tells the editor which
   pictures exist. Commit both.

## Where each one is

The door card and the detail pane of those lists also show where that particular door, button,
elevator, tram, teleporter, power socket or breakable wall is: the level around it from above, ceiling cut away,
the thing outlined and pinned in orange. Level actors stand in the same place in every world, so
these ship too (`thumbs/places/<map>/<actor>.webp`, about 9 KB each), resource nodes included.

Step 1 above can also write every instance: set `THUMBNAIL_INSTANCES_OUT=%TEMP%\thumb-instances.json`.
Then, with the editor running:

```console
node places.mjs http://127.0.0.1:37361 %TEMP%\thumb-instances.json
```

Pictures already on disk are skipped, so a run can be stopped and resumed; delete a picture to
render it again. It regenerates `WorldThumbnails.Places.g.cs` from every picture on disk.
