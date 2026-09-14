# Sample plugin catalog

These examples live in the repository and are not installed automatically. Each linked folder
contains its own README and manifest. Plugins run with full trust; inspect an example before loading it.

## The samples

| Plugin | Runtime | Capability | What it shows |
|---|---|---|---|
| [`MaxSkills`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins/MaxSkills) | .NET | save operation | the smallest managed save operation, with a parameter |
| [`RepairNeeds`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins/RepairNeeds) | .NET | save operation | a no-parameter operation that tops up survival needs |
| [`GrantFlag`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins/GrantFlag) | .NET | save operation | a forward-compatible fix-up: add a raw world flag |
| [`SaveStats`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins/SaveStats) | .NET | console command | a new CLI verb that behaves like a built-in |
| [`VersionShim`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins/VersionShim) | .NET | save upgrader | recovering a save with an unsupported version |
| [`HelloScript`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins/HelloScript) | JavaScript | save op + command + menu action + event handler | one script, four capabilities, no build |
| [`WebStats`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins/WebStats) | JavaScript | web tool | an offline HTML UI served from a bundled folder |
| [`ReactDashboard`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins/ReactDashboard) | JavaScript | web tool | a React UI (React from a CDN), no build step |
| [`ReactAppDashboard`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins/ReactAppDashboard) | JavaScript | web tool + save op | a full Vite + React app that also drives the editor |


See [Building and installing](./plugin-building) for setup and the [Authoring guide](./plugin-authoring) for the SDK.
