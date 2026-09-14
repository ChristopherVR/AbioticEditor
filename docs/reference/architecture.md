# Architecture and contributing

Abiotic Editor shares one save engine across the desktop application, browser editor, and CLI.
The desktop window uses Photino around a local Razor server. The browser edition uses Blazor
WebAssembly and runs the shared UI and save engine on the player's computer.

## Projects

| Project | Responsibility |
| --- | --- |
| `src/AbioticEditor.Core` | Save models, catalogs, serialization, editing services, infrastructure, plugins, and live protocol clients |
| `src/AbioticEditor.Web.Shared` | Razor screens, editor sessions, localization, and shared UI services |
| `src/AbioticEditor.Ui.Abstractions` | Host-neutral UI and platform contracts |
| `src/AbioticEditor.Web` | Windows/Linux local host, Photino window, and local filesystem access |
| `src/AbioticEditor.Web.Wasm` | Browser entry point and browser filesystem implementation |
| `src/AbioticEditor.Cli` | Headless commands over Core |
| `src/AbioticEditor.Plugins.Abstractions` | Public plugin SDK |
| `src/AbioticEditor.Updater` | Release download and install replacement |
| `live-agent/` | Separate native helper and UE4SS Lua mod for live editing |

The old MAUI app has been retired. Its [parity audit](/architecture/razor-parity-audit) is historical.

## Save contract

Keep parsing and editing in Core. Readers expose typed models backed by the original raw GVAS
tree; writers mutate that tree and preserve fields they do not change. Readers find hashed
properties by prefix. Writers creating an omitted default property need its exact full name.
See [How saves work](./save-format) and the player/world schemas.

Core folders separate Domain, Catalogs, Serialization, Services, and Infrastructure. Namespaces
remain stable for consumers of the published Core package even when files move between folders.
Plugins have their own hosting layer; save operations write through `SaveOperationRunner`.

File sessions stage edits until SAVE. Live sessions use the agent protocol and can apply changes
immediately. Keep that distinction visible in UI and documentation. The local desktop server
must remain restricted to loopback because it can access user-selected files.

## Build and verify

Install the .NET 10 SDK and clone with submodules:

```console
git clone --recursive https://github.com/ChristopherVR/AbioticEditor.git
cd AbioticEditor
dotnet build src/AbioticEditor.Web
dotnet build src/AbioticEditor.Cli
dotnet test tests/AbioticEditor.Tests -f net10.0
```

Real-save tests use fixtures under `tests/fixtures` and can skip when fixtures are absent.
`tests/AbioticEditor.Probes` contains research probes, not the normal assertion suite.
The Lua harness runs when a Lua 5.4 interpreter is available (`ABIOTIC_LUA_EXE` or PATH).
A missing optional CUE4Parse native texture decoder does not prevent managed save parsing.

If the desktop host is running, its DLLs may be locked. Build to a temporary output directory.
Builds can also regenerate scoped CSS; reload an open editor after a build if its styles disappear.

## Documentation and Pages

From `docs/`, using Node.js and npm:

```console
npm ci
npm run docs:dev
npm run docs:build
npm run docs:preview
```

The production build rejects broken internal page links, anchors, and images. `/app/` is the only exception: the
Pages workflow builds the WebAssembly editor separately and assembles it alongside the docs.
The preview server shows documentation only; it does not publish the browser editor.

Add new guides to the directory and sidebar in `.vitepress/config.mts`. Link repository source
with GitHub URLs, since source files are not deployed to Pages. Keep browser-editor links as
full navigations so VitePress does not try to load them as documentation pages.

`docs/PROGRESS.md` is an internal, dated session log and is excluded from the site. Record new
verification there without presenting old test counts as the current state. Research notes
should retain their historical context. Use Conventional Commits with player-facing descriptions;
work goes on `main`, and publishing requires an explicitly authorized push.
