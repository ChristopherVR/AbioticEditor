# Linux desktop app

`AbioticEditor.Web` ships as a self-contained Linux desktop application. The existing
Razor UI is displayed in a Photino native window backed by WebKitGTK, while its local
server remains restricted to the loopback interface. Save files never leave the computer.

## Runtime requirements

The archive contains the .NET runtime and the Photino native library. The desktop must
provide GTK 3, WebKitGTK 4.1, and libnotify. Steam Deck Desktop Mode already provides the
required graphical environment. On Debian or Ubuntu, install missing libraries with:

```bash
sudo apt-get install libgtk-3-0 libwebkit2gtk-4.1-0 libnotify4
```

Package names can vary by distribution. Ubuntu 24.04 names the GTK package
`libgtk-3-0t64`. A web browser is not required to display the editor.

## Build a release folder

From the repository root on Linux, publish the supported x64 build:

```bash
dotnet publish src/AbioticEditor.Web -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=false -o out/AbioticEditor-desktop-linux-x64
chmod +x out/AbioticEditor-desktop-linux-x64/launch-linux.sh
chmod +x out/AbioticEditor-desktop-linux-x64/install-linux-desktop.sh
```

Archive the contents of that output directory. Do not publish a single-file build:
the native window library, static assets, and save templates remain beside the executable.

## Run it

```bash
./launch-linux.sh
```

The command stays attached to the desktop window and exits when the window closes. The
default internal endpoint is `http://127.0.0.1:37246`. A different port can be chosen only
with a loopback HTTP URL:

```bash
ABIOTIC_EDITOR_URL=http://127.0.0.1:41000 ./launch-linux.sh
```

The executable rejects wildcard, LAN, HTTPS, path, query, and privileged-port bindings.
This is deliberate because the editor can access local saves selected by the user.

`--headless` is reserved for automated health checks. It starts the same host without a
window and is not the player-facing launch mode:

```bash
./launch-linux.sh --headless
```

## Desktop-menu install and updates

Extract each release into a stable per-user directory such as
`~/.local/opt/abiotic-editor-web`, then install the desktop-menu entry:

```bash
cd ~/.local/opt/abiotic-editor-web
chmod +x launch-linux.sh install-linux-desktop.sh
./install-linux-desktop.sh
```

The **Abiotic Editor** menu item launches the same desktop app. To update, close the app,
replace the release directory contents, and run `./install-linux-desktop.sh` again. Remove
the menu integration with `./install-linux-desktop.sh --uninstall`.

## Release contract

The release workflow publishes `AbioticEditor-desktop-linux-x64-v<version>.zip`. It includes
the self-contained executable, `Photino.Native.so`, Razor assets, templates,
`launch-linux.sh`, and `install-linux-desktop.sh`. CI verifies both headless health and a
real WebKitGTK window under a virtual display before publishing.

The same archive is published as the **Linux / Steam Deck (Proton saves)** main file on Nexus
Mods. It is a native Linux application that discovers and edits saves inside Proton
prefixes; Proton is not needed to run the editor itself.

Release maintainers configure the Nexus upload with the shared `NEXUSMODS_API_KEY` Actions
secret and the `NEXUS_PROTON_FILE_ID` repository variable. If either value is absent, the
Nexus Linux upload is skipped without blocking the GitHub release or Windows Nexus file.

## Release smoke check

Run the repository verification script against a published folder on Ubuntu:

```bash
sudo apt-get install libgtk-3-0t64 libwebkit2gtk-4.1-0 libnotify4 xvfb
bash tools/verify-web-host-linux.sh out/AbioticEditor-desktop-linux-x64
```

Then perform the manual Linux acceptance pass with real Steam and Proton save locations,
folder selection and reveal, player and world save/revert, INI save, and plugin tools.
