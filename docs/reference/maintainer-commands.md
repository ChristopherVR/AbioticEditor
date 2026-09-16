# Maintainer commands

These CLI commands regenerate data that ships **with** the editor. They are for maintainers preparing
a release, not for everyday editing: you run them when a new game build or a wiki change means the
bundled data needs refreshing, and you commit their output to the repo. Everyday CLI usage is covered
in the [command-line tool guide](/guide/cli).

```console
abioticeditor dump-registry -o registry.json        # dump the game's data tables (needs the game installed)
abioticeditor download-wiki-images -o assets/wiki    # fetch the offline wiki-image fallback (needs network)
```

## `dump-registry`

Dumps the game's item / recipe / skill / flag / fish / trait data tables to JSON. Needs the game
installed (it reads the pak archives through the bundled type-mappings). This is the registry the
editor falls back on, so regenerate and commit it when a game update changes the catalogs.

## `download-wiki-images`

Downloads the verified fish / vehicle / world-feature / door reference pictures from
[abioticfactor.wiki.gg](https://abioticfactor.wiki.gg) into a folder the app and CLI bundle as the
**offline fallback**. The live wiki is still tried first at runtime, so the bundled art only shows
when the wiki is unreachable, and stays current otherwise. The command throttles its requests
because the wiki rate-limits rapid bursts. The images are CC BY-NC-SA 4.0; see `assets/wiki/README.md`.

The bundled set lives in `assets/wiki/`. See where these pictures surface in the app under
[Reference pictures from the wiki](/guide/desktop-app#reference-pictures-from-the-wiki).

## Regenerating the bundled usmap

The bundled `Mappings.usmap` is validated for a known-good game build. Refreshing it for a new build
uses the same dump-and-replace flow an end user follows to
[keep game data current](/guide/game-data#the-usmap-matching-the-game-build), except the maintainer
replaces the file bundled in `assets/` and commits it, rather than dropping it in the per-user
mappings folder.

## Bumping the bundled UE4SS

The Windows desktop release bundles a pinned build of UE4SS, the third-party mod loader live
editing needs, pinned in `live-agent/ue4ss/runtime.json` by exact file name and SHA-256 (see
`Ue4ssBundledRuntime` in Core). To move to a newer upstream build:

1. Edit `live-agent/ue4ss/runtime.json`: update `version`, `asset`, `url`, `sha256`, and `size`
   for the new release.
2. Run `pwsh tools/fetch-ue4ss.ps1`. It downloads the new package into
   `live-agent/ue4ss/UE4SS.zip` (gitignored) and verifies it against the manifest you just edited.
3. Test it in game: run the local host, use **This PC** against a game folder with no existing
   UE4SS install, confirm the consent screen shows the new version, install, and confirm the
   game actually loads with the mod working.
4. Commit the updated `runtime.json`. `UE4SS.zip` itself is not committed; release CI fetches it
   fresh with the same script before publishing.

## Related screens

See the [screenshot tour](/guide/screenshots) for the player-facing controls. Screenshots illustrate the interface; the schemas and behavior above remain the reference.

## Updating documentation screenshots

Keep application screenshots in `docs/public/screenshots/`. Use PNG files with descriptive names and reference them in handbook pages as `![What the screen shows](/screenshots/name.png)`. VitePress adds the deployment base path. Repository READMEs should use a relative path to the same image.

1. Run the local host against a copy of a save. Use the browser to open the actual screen and select a representative item or setting.
2. Wait for names, icons and details to finish loading. Capture the relevant panel at a readable desktop width. Keep account IDs, personal paths and tokens outside the frame.
3. Add the image beside the steps it illustrates, with useful alt text and a short caption. Explain when a screenshot shows shared Windows controls rather than a different platform, and when a live screen shows setup rather than a connected game.
4. Update the capture date and coverage in the [screenshot tour](/guide/screenshots). Link technical or historical pages to the current tour rather than presenting a new screenshot as historical evidence.
5. Run `npm --prefix docs run docs:build` from the repository root. This checks generated page links and image paths. Preview the site and check image loading, click-to-enlarge, and narrow-screen layout.

The September 2026 captures use the Hazard Orange theme and copied saves in the Windows local host. Live captures cover the mode choice, local helper setup after UE4SS detection, and the remote form with an empty token. They are not evidence of connected-game behavior.
