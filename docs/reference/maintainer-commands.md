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

## The bundled UE4SS

The Windows desktop release bundles a build of UE4SS, the third-party mod loader live editing
needs. `tools/fetch-ue4ss.ps1` always follows upstream's rolling `experimental-latest` tag: it
asks the GitHub API for whatever asset is published there right now, downloads it into
`live-agent/ue4ss/UE4SS.zip` (gitignored), and rewrites `live-agent/ue4ss/runtime.json`
(`version`/`asset`/`url`/`sha256`/`size`) to match. There is no manual pin to bump, and CI can no
longer fail just because upstream renamed its asset. `Ue4ssBundledRuntime` in Core re-checks the
SHA-256 in `runtime.json` against the bundled zip before installing it for a player, which still
catches a corrupted download but not a bad upstream build.

Because nothing gates a new upstream build before it ships to players, periodically re-verify it
in game: run `pwsh tools/fetch-ue4ss.ps1` to pick up whatever is current, then run the local host,
use **This PC** against a game folder with no existing UE4SS install, confirm the consent screen
shows the version you expect, install, and confirm the game actually loads with the mod working.
Commit the `runtime.json` changes `fetch-ue4ss.ps1` made; `UE4SS.zip` itself is never committed.

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
