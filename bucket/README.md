# Scoop bucket

This folder is a [Scoop](https://scoop.sh/) bucket: it lets Windows users install and update
Abiotic Editor from the command line, which sidesteps the SmartScreen / "unknown publisher"
prompt you get when double-clicking an unsigned download from a browser.

```console
scoop bucket add abiotic-editor https://github.com/ChristopherVR/AbioticEditor
scoop install abiotic-editor          # desktop app
scoop install abiotic-editor-cli      # command-line tool
scoop update abiotic-editor           # later, to upgrade
```

Each manifest pins the exact release asset and its SHA-256, so Scoop verifies the download
before extracting it. The release workflow (`.github/workflows/release.yml`) rewrites the
`version`, `url`, and `hash` fields on every release, so these manifests always track the
latest build.
