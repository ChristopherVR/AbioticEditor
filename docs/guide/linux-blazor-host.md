# Linux and Steam Deck Blazor host

`AbioticEditor.Web` is the Linux-capable UI host. It is a local Blazor Server
application that uses the shared `AbioticEditor.Core` save engine.
It listens only on the local machine during normal development, and never sends a
save file to a remote service.

The Razor desktop app is the portable UI path for Windows, Linux, and Steam Deck.
The packaged build includes its own .NET runtime and opens in a native Photino window.

## Run from source

```console
dotnet run --project src/AbioticEditor.Web
```

The desktop window opens after the local server starts. Select **DISCOVER SAVES** to scan
Steam, Steam Deck and Proton locations. Proton prefixes are detected under each
Steam library's `steamapps/compatdata` directory.

## Publish a Linux executable

```console
dotnet publish src/AbioticEditor.Web -c Release -r linux-x64 --self-contained true
```

The published executable starts the loopback service and its native window together.
On Linux it uses WebKitGTK 4.1; on Steam Deck, run it from Desktop Mode or install
the included desktop-menu entry.

If double-clicking the extracted `launch-linux.sh` does nothing, or GTK/WebKitGTK
libraries are missing (common on Steam Deck's read-only system partition), see the
terminal steps and headless fallback in the
[Linux desktop app guide](linux-local-host.md#if-double-clicking-the-download-does-nothing).

## Architecture boundary

The host owns discovery, world/save-file selection, and field-level editing through
host-neutral services. Save reads and writes remain in `AbioticEditor.Core`, preserving
the same byte-safe serialization behavior on every platform.
