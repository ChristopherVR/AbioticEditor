# Technical reference

For contributors, plugin authors, and anyone exploring what the game saves. To edit a world,
start with the [player guides](/guide/).

[Browse the screenshot tour](/guide/screenshots) for current player, world, settings and live-setup screens.

## Build and contribute

- [Architecture and contributing](./architecture): projects, save contract, build commands, and documentation checks.
- [Localization](./localization): shared strings and translations.
- [Maintainer commands](./maintainer-commands): regenerate registries and wiki images.

## Save formats

- [How saves work](./save-format)
- [Player save schema](./player-save-schema)
- [World save schema](./world-save-schema)
- [Game Pass container format](./game-pass-format)

## Plugins

- [Plugin system](./plugin-system): capabilities, loading, and trust model.
- [Authoring guide](./plugin-authoring): write managed and JavaScript extensions.
- [Building and installing](./plugin-building)
- [Sample catalog](./plugin-samples)
- [Fix-up cookbook](./plugin-fixups)

## Live editing and research

- [Live protocol](./live-editing-protocol): commands, capability limits, and verification evidence.
- [Live setup guide](/guide/live-editing): the player-facing connection workflow.
- The **Research notes** sidebar contains investigations for specific game builds.
- [Historical Razor parity audit](/architecture/razor-parity-audit): the migration record for the retired MAUI app.

Treat older investigations as dated evidence. Current behavior is defined by the source and its tests;
research notes can describe restrictions that later work removed.
