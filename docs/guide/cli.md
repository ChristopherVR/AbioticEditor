# Command-line tool

`abioticeditor` is a headless CLI for scripting and server administration. It wraps the same
Core library as the desktop app, so it writes the same output the app would. Run
`abioticeditor --help` for the full surface.

## Common commands

```console
abioticeditor scan <folder>                      # list saves with kind and version
abioticeditor info <save.sav>                    # key facts about one save
abioticeditor export-json <save.sav> -o out.json # lossless JSON dump
abioticeditor import-json <save.sav> in.json     # rebuild the save from JSON
abioticeditor flags list <world.sav>             # quest flags (--filter to narrow)
abioticeditor flags set <world.sav> <flag>       # set a flag (--clear, --force)
abioticeditor steamid <player.sav> <newid64>     # reassign the owning Steam account
abioticeditor ini list <file.ini>                # sections of an ini file
abioticeditor ini get/set <file.ini> ...         # read or edit ini values
abioticeditor version                            # tool + supported save versions
```

Exit codes: `0` success, `1` usage or data error, `2` unexpected failure. `--json` switches
`scan` / `info` / `flags list` to machine-readable output.

## Plugins from the CLI

```console
abioticeditor plugins list                       # installed plugins + load state + capabilities
abioticeditor plugins info <id>                  # one plugin's details
abioticeditor plugins run <operation> <save>     # run a save operation (keeps a .bak)
abioticeditor plugins run <operation> <save> --param name=value --dry-run
abioticeditor <plugin-command> ...               # plugin console commands are top-level verbs
```

See the **[plugin system](/plugins)** for what plugins can do and how to install them.

## Updating

```console
abioticeditor update                 # check, and install if newer
abioticeditor update check [--json]  # report only
abioticeditor update install [-y]    # install (use --pre for pre-releases)
```

The CLI honours `GITHUB_TOKEN` to lift the unauthenticated GitHub rate limit. It downloads
the `cli` asset matching your OS/arch and replaces the running install in place.
