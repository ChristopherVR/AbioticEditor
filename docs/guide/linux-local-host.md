# Linux and Steam Deck desktop app

Abiotic Editor has a native Linux desktop release. It opens its own local window and keeps your saves on your computer. It can also find Abiotic Factor saves inside Proton prefixes, the folders Steam uses to run Windows games on Linux. Proton is for finding the game saves, not for running the editor.

## Start here

1. On Steam Deck, switch to **Desktop Mode** first: press the Power button, then choose **Switch to Desktop**.
2. Download and extract the Linux / Steam Deck release ZIP somewhere you will keep it.
3. Double-click **launch-linux.desktop**.
4. On KDE Plasma and Steam Deck Desktop Mode, choose **Trust and Launch** the first time.

That should open Abiotic Editor in its own window.

## If the launcher does not open

Open a terminal in the extracted folder and run:

```bash
bash launch-linux.sh
```

This works even if the launcher does not yet have permission to run. In Dolphin, right-click empty space in the folder and choose **Open Terminal Here**.

The desktop window needs GTK 3, WebKitGTK 4.1, and libnotify. On Debian or Ubuntu, install missing parts with:

```bash
sudo apt-get install libgtk-3-0 libwebkit2gtk-4.1-0 libnotify4
```

Ubuntu 24.04 may call the first package `libgtk-3-0t64`. Other Linux distributions use different package names.

::: tip Steam Deck fallback
Steam Deck's system partition can make those libraries awkward to install. You usually do not need to change the read-only system setting. Use the browser fallback below instead.
:::

## Browser fallback

If the desktop window cannot start, run:

```bash
./launch-linux.sh --headless
```

Then open `http://127.0.0.1:37246` in a browser on the same machine. Keep the terminal open while you use the editor. This address only works on your own computer.

## Add it to your app menu

Once the release is in a permanent home, run this from its folder:

```bash
chmod +x launch-linux.sh install-linux-desktop.sh
./install-linux-desktop.sh
```

You can then open **Abiotic Editor** from your desktop's app menu. To update, close the editor, replace the files in that same folder with the new release, and run `./install-linux-desktop.sh` again. To remove the menu entry, run `./install-linux-desktop.sh --uninstall`.

## Optional local port

The normal launcher uses `http://127.0.0.1:37246`. If that port is occupied, choose another local port:

```bash
ABIOTIC_EDITOR_URL=http://127.0.0.1:41000 ./launch-linux.sh
```

Use only a `127.0.0.1` address. The editor refuses network, wildcard, HTTPS, path, query, and privileged-port addresses because it can open your local saves.
