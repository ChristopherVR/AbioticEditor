# World list pictures

The Doors, Buttons, Breakable Objects, Elevators, Trams, World Teleporters and Resource Nodes lists
show a picture of each kind of thing, drawn on its own with the game's own models. The pictures are
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

   It writes `wwwroot/thumbs/<kind>/<class>.webp` and regenerates
   `src/AbioticEditor.Web.Shared/Services/WorldThumbnails.Index.g.cs`, which tells the editor which
   pictures exist. Commit both.
