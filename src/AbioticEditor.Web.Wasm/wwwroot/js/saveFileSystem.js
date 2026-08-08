// Reaching the player's save folder from a browser tab.
//
// The File System Access API hands out FileSystemDirectoryHandle objects, which cannot cross
// the JavaScript/.NET boundary. They are kept here instead, keyed by the folder's own name, and
// .NET refers to files by "<folderName>/<pathInsideFolder>". That is the "opaque identifier"
// ISaveFileSystem documents: it looks path-shaped so the existing editor code is happy, but only
// this file may interpret it. Re-picking a folder of the same name replaces its handle, which is
// what a player re-opening the same world expects.
//
// Two kinds of folder live here. Chromium grants a real directory handle, which the editor reads
// and writes in place. Firefox and Safari have no such API, so there a folder is opened through
// <input type="file" webkitdirectory> instead: every file is handed over as a read-only snapshot,
// edits are kept in memory, and the player takes them away with EXPORT. isSupported() is what
// decides between the two.

const roots = new Map();

// The read-only counterpart of `roots`, for browsers with no File System Access API at all.
// An <input type="file" webkitdirectory> gives every file in a chosen folder as a File, each
// carrying its path within that folder - enough to read a whole world, but the File objects are
// snapshots, so nothing here can ever write back. Those folders are served from this map instead,
// and the editor sends the player to EXPORT rather than SAVE.
const uploads = new Map();

// Edits made to an uploaded folder. The File objects in `uploads` are read-only snapshots, so a
// save cannot go back where it came from - it goes here instead, and reads prefer it. That keeps
// every existing write path working unchanged, including the ones that touch saves the player
// never opened (story flags, moving players), and EXPORT then hands back the edited set rather
// than the originals. Lost when the tab closes, which is why the UI says EXPORT rather than SAVE.
const overlays = new Map();

function overlayFor(rootName, create = false) {
    let overlay = overlays.get(rootName);
    if (!overlay && create) { overlay = new Map(); overlays.set(rootName, overlay); }
    return overlay;
}

function splitPath(path) {
    const separator = path.indexOf("/");
    if (separator < 0) return { rootName: path, relative: "" };
    return { rootName: path.slice(0, separator), relative: path.slice(separator + 1) };
}

function rootHandle(rootName) {
    const handle = roots.get(rootName);
    if (!handle) {
        throw new Error(`The folder "${rootName}" is no longer open. Pick your save folder again.`);
    }
    return handle;
}

/// The current contents of a path inside an uploaded folder as a Blob, or null when that folder
/// was not uploaded. An edit made this session wins over the file the player chose.
function uploadedBlob(path) {
    const { rootName, relative } = splitPath(path);
    const folder = uploads.get(rootName);
    if (!folder) return null;

    const edited = overlayFor(rootName)?.get(relative);
    if (edited) return new Blob([edited]);

    const file = folder.get(relative);
    if (!file) {
        throw new Error(`"${relative}" is not in the folder you opened. Open the folder again.`);
    }
    return file;
}

// Walks down a "a/b/c.sav" relative path and returns the file handle at the end.
async function fileHandle(rootName, relative, { create = false } = {}) {
    let directory = rootHandle(rootName);
    const segments = relative.split("/").filter(segment => segment.length > 0);
    const fileName = segments.pop();
    for (const segment of segments) {
        directory = await directory.getDirectoryHandle(segment, { create });
    }
    return { directory, handle: await directory.getFileHandle(fileName, { create }), fileName };
}

/// Copies the current contents to "<name>.bak" and then replaces the file. The editor's promise
/// is that one bad save can always be undone, so a failed backup stops the write rather than
/// pressing on - unlike on the desktop, there is no file history to fall back on.
async function writeToHandle(path, contentStreamReference) {
    const { rootName, relative } = splitPath(path);
    const { directory, handle, fileName } = await fileHandle(rootName, relative);
    // .NET hands the bytes over as a stream reference rather than a JSON-encoded array, so a
    // 16 MB region save does not go through base64 on the way here.
    const data = await contentStreamReference.arrayBuffer();

    const existing = await handle.getFile();
    if (existing.size > 0) {
        const backup = await directory.getFileHandle(`${fileName}.bak`, { create: true });
        const backupStream = await backup.createWritable();
        await backupStream.write(await existing.arrayBuffer());
        await backupStream.close();
    }

    // createWritable() buffers into a swap file and only replaces the target on close, so a
    // failure partway cannot leave a truncated save behind.
    const stream = await handle.createWritable();
    await stream.write(data);
    await stream.close();
}

async function* walk(directory, prefix) {
    for await (const entry of directory.values()) {
        const path = prefix ? `${prefix}/${entry.name}` : entry.name;
        if (entry.kind === "directory") {
            yield* walk(entry, path);
        } else if (entry.name.toLowerCase().endsWith(".sav")) {
            yield { entry, path };
        }
    }
}

window.abioticSaveFs = {
    isSupported: () => typeof window.showDirectoryPicker === "function",

    /// Prompts for a save folder and remembers it. Returns its name, or null if cancelled.
    pickFolder: async () => {
        if (!window.showDirectoryPicker) {
            throw new Error("This browser cannot open a folder. Try Chrome or Edge, or open a single save file instead.");
        }
        let handle;
        try {
            // readwrite up front: asking again at save time would be a second permission prompt
            // at the worst possible moment, right when the player expects their edit to land.
            handle = await window.showDirectoryPicker({ mode: "readwrite", id: "abiotic-saves" });
        } catch (error) {
            if (error && error.name === "AbortError") return null;
            // Chrome refuses folders it considers sensitive - a whole drive, Windows, Program
            // Files, your user folder's root - with a bare "system files" message that does not
            // say what to do instead. Say it here.
            if (error && (error.name === "SecurityError" || /system files/i.test(error.message ?? ""))) {
                throw new Error(
                    "The browser will not grant access to that folder because it is a system location. "
                    + "Pick the world folder itself (the one holding WorldSave_*.sav and PlayerData), "
                    + "usually under AbioticFactor/Saved/SaveGames, rather than a whole drive or your user folder.");
            }
            throw error;
        }
        roots.set(handle.name, handle);
        // A read-only copy of the same folder opened earlier must not shadow this one.
        uploads.delete(handle.name);
        return handle.name;
    },

    /// Opens a folder read-only on a browser with no File System Access API (Firefox, Safari).
    /// Returns its name, or null if the player cancelled.
    ///
    /// webkitdirectory is non-standard by name but implemented everywhere, and unlike the picker
    /// it needs no permission prompt because the player chose the folder in the OS dialog itself.
    uploadFolder: () => new Promise((resolve, reject) => {
        const input = document.createElement("input");
        input.type = "file";
        input.webkitdirectory = true;
        input.multiple = true;
        input.style.display = "none";
        document.body.appendChild(input);

        let settled = false;
        const finish = (value) => {
            if (settled) return;
            settled = true;
            input.remove();
            resolve(value);
        };

        input.addEventListener("change", () => {
            try {
                const files = [...input.files];
                if (files.length === 0) { finish(null); return; }

                // webkitRelativePath is "<chosen folder>/a/b.sav"; the first segment names the
                // folder the player picked, which is what the editor shows and keys files by.
                const rootName = files[0].webkitRelativePath.split("/")[0];
                const folder = new Map();
                for (const file of files) {
                    const relative = file.webkitRelativePath.split("/").slice(1).join("/");
                    if (relative) folder.set(relative, file);
                }
                uploads.set(rootName, folder);
                // A folder of the same name opened writably earlier would otherwise win.
                roots.delete(rootName);
                finish(rootName);
            } catch (error) {
                settled = true;
                input.remove();
                reject(error);
            }
        });

        // Fired when the OS dialog is dismissed without choosing. Not universal, hence the
        // change handler above still being the one that resolves the happy path.
        input.addEventListener("cancel", () => finish(null));

        input.click();
    }),

    /// True when this folder was opened read-only and so cannot be saved back to.
    isReadOnly: (rootName) => uploads.has(rootName),

    folderExists: async (rootName) => roots.has(rootName) || uploads.has(rootName),

    /// Lets a folder dragged onto the window be opened like a picked one. A drop gives us a
    /// FileSystemDirectoryHandle through getAsFileSystemHandle(), so it lands in the same
    /// registry as a picked folder and behaves identically from then on. Registers the handler
    /// once and calls back into .NET with the folder name.
    listenForDroppedFolder: (dotNetRef) => {
        if (window.__abioticDropWired) return;
        window.__abioticDropWired = true;

        // Without preventing dragover the browser navigates away to the dropped file instead.
        window.addEventListener("dragover", event => event.preventDefault());

        window.addEventListener("drop", async event => {
            event.preventDefault();
            const items = [...(event.dataTransfer?.items ?? [])];
            for (const item of items) {
                if (item.kind !== "file" || !item.getAsFileSystemHandle) continue;
                let handle;
                try {
                    handle = await item.getAsFileSystemHandle();
                } catch {
                    continue;
                }
                if (!handle || handle.kind !== "directory") continue;

                // A drop grants read access only; writing needs explicit consent, and asking
                // now (while the drop gesture still counts as user activation) avoids a prompt
                // appearing later at the moment the player presses SAVE.
                try {
                    if (handle.queryPermission && await handle.queryPermission({ mode: "readwrite" }) !== "granted") {
                        await handle.requestPermission({ mode: "readwrite" });
                    }
                } catch {
                    // Carry on read-only; the write itself will report if it is refused.
                }

                roots.set(handle.name, handle);
                await dotNetRef.invokeMethodAsync("OnFolderDropped", handle.name);
                return;
            }
        });
    },

    listSaves: async (rootName) => {
        const uploaded = uploads.get(rootName);
        if (uploaded) {
            const results = [];
            for (const [relative, file] of uploaded) {
                if (!relative.toLowerCase().endsWith(".sav")) continue;
                results.push({
                    path: `${rootName}/${relative}`,
                    relativePath: relative,
                    name: file.name,
                    length: file.size,
                });
            }
            return results;
        }

        const directory = rootHandle(rootName);
        const results = [];
        for await (const found of walk(directory, "")) {
            const file = await found.entry.getFile();
            results.push({
                path: `${rootName}/${found.path}`,
                relativePath: found.path,
                name: found.entry.name,
                length: file.size,
            });
        }
        return results;
    },

    /// Only the first maxBytes. A Blob slice is lazy, so identifying 65 saves does not read
    /// 16 MB region files off the disk.
    readHeader: async (path, maxBytes) => {
        const uploaded = uploadedBlob(path);
        if (uploaded) return await uploaded.slice(0, Math.min(maxBytes, uploaded.size)).arrayBuffer();

        const { rootName, relative } = splitPath(path);
        const { handle } = await fileHandle(rootName, relative);
        const file = await handle.getFile();
        return await file.slice(0, Math.min(maxBytes, file.size)).arrayBuffer();
    },

    readAll: async (path) => {
        const uploaded = uploadedBlob(path);
        if (uploaded) return await uploaded.arrayBuffer();

        const { rootName, relative } = splitPath(path);
        const { handle } = await fileHandle(rootName, relative);
        return await (await handle.getFile()).arrayBuffer();
    },

    /// Cheap "has this changed?" token, so a screen that glances at a 16 MB region save does
    /// not re-parse it every time. Null when the file has gone, which callers read as "changed".
    versionStamp: async (path) => {
        try {
            const uploaded = uploadedBlob(path);
            // An uploaded File is a snapshot taken when the folder was chosen, so it never
            // changes underneath us - the stamp is constant for as long as it is open.
            if (uploaded) return `${uploaded.lastModified}:${uploaded.size}`;

            const { rootName, relative } = splitPath(path);
            const { handle } = await fileHandle(rootName, relative);
            const file = await handle.getFile();
            return `${file.lastModified}:${file.size}`;
        } catch {
            return null;
        }
    },

    /// Copies the current contents to "<name>.bak" and then replaces the file. The editor's
    /// promise is that one bad save can always be undone, so a failed backup stops the write
    /// rather than pressing on - unlike on the desktop, there is no file history to fall back on.
    write: async (path, contentStreamReference) => {
        const { rootName, relative } = splitPath(path);

        // A folder the browser could only read: keep the new bytes in memory instead. Every
        // write path in the editor then behaves normally, and EXPORT hands back what was edited.
        // The player's own file is untouched, so the originals are the backup.
        if (uploads.has(rootName)) {
            const data = await contentStreamReference.arrayBuffer();
            overlayFor(rootName, true).set(relative, new Uint8Array(data));
            return;
        }

        return await writeToHandle(path, contentStreamReference);
    },

};
