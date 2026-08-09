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

// Worlds the player has opened before, so a refresh does not mean picking the folder again.
//
// IndexedDB, not localStorage, because neither of the two things worth keeping is a string.
//
// For a folder the player granted, it is a FileSystemDirectoryHandle - a live reference to the
// real folder on their disk. The browser deliberately drops the read permission that came with it
// when the tab closes, so re-opening one asks the player again; that prompt needs a click behind
// it, which is why reopening is a button and never happens on its own.
//
// For a world with no folder behind it - opened from a zip, or from a browser that can only take
// a read-only copy - it is the saves themselves, one record per file. They are stored unpacked
// rather than as the original zip so that reopening is a straight read with no unzipping, and so
// that saving an edit can replace just the file that changed. That is what lets the editor hand
// back the world as you left it rather than as you first opened it.
const RECENT_DB = "abiotic-editor";
const RECENT_STORE = "recent-worlds";
const RECENT_FILES = "recent-files";
const RECENT_LIMIT = 3;

// One connection, reused. Storing and restoring a world is one call per save - sixty-odd of them
// - and opening the database each time made restoring take twice as long as unzipping the same
// world from scratch. The connection is cheap to hold and closes with the tab.
let recentDb = null;

function openRecentDb() {
    return recentDb ??= connectRecentDb().catch(error => { recentDb = null; throw error; });
}

function connectRecentDb() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(RECENT_DB, 2);
        request.onupgradeneeded = () => {
            const db = request.result;
            if (!db.objectStoreNames.contains(RECENT_STORE)) {
                db.createObjectStore(RECENT_STORE, { keyPath: "name" });
            }
            // One record per save, not one big record per world: an edit then rewrites only the
            // file that changed instead of reading back and re-storing the whole 68 MB world.
            if (!db.objectStoreNames.contains(RECENT_FILES)) {
                db.createObjectStore(RECENT_FILES, { keyPath: ["world", "path"] });
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

function recentTransaction(db, mode) {
    return db.transaction(RECENT_STORE, mode).objectStore(RECENT_STORE);
}

function filesTransaction(db, mode) {
    return db.transaction(RECENT_FILES, mode).objectStore(RECENT_FILES);
}

/// Every stored file belonging to one world. Keys are ["world", "path"], so a bounded range
/// picks out exactly that world's saves.
function worldFileRange(world) {
    return IDBKeyRange.bound([world, ""], [world, "￿"]);
}

function awaitRequest(request) {
    return new Promise((resolve, reject) => {
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

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

    /// Drops any folder held under this name. Called when the same world is re-opened from a zip
    /// instead, so exactly one source ever answers for a given name.
    forget: (rootName) => {
        roots.delete(rootName);
        uploads.delete(rootName);
        overlays.delete(rootName);
    },

    /// Remembers a world so the home page can offer it after a refresh. Silently does nothing
    /// for a world with no folder behind it (an uploaded copy, or a zip): there would be nothing
    /// to reopen, and an entry that cannot be opened is worse than no entry.
    rememberRecent: async (rootName, openedAt, keepsContents) => {
        // A folder is remembered by its handle - a live reference the browser can re-open once
        // the player says yes again. A world with no folder behind it is remembered by its saves,
        // which .NET writes in separately through rememberRecentFile.
        const handle = roots.get(rootName) ?? null;
        if (!handle && !keepsContents) return;
        try {
            const db = await openRecentDb();
            await awaitRequest(recentTransaction(db, "readwrite")
                .put({ name: rootName, handle, hasContents: !!keepsContents, openedAt }));

            // Keep only the newest few, so the list stays a shortcut rather than a history.
            const all = await awaitRequest(recentTransaction(db, "readonly").getAll());
            all.sort((a, b) => (b.openedAt ?? "").localeCompare(a.openedAt ?? ""));
            for (const stale of all.slice(RECENT_LIMIT)) {
                await awaitRequest(recentTransaction(db, "readwrite").delete(stale.name));
                // Its saves go with it, or they would sit in storage forever unreachable.
                await awaitRequest(filesTransaction(db, "readwrite").delete(worldFileRange(stale.name)));
            }
        } catch {
            // A browser with storage switched off just does not offer the shortcut.
        }
    },

    /// Stores (or replaces) one save of a remembered world. Called when the world is first
    /// opened and again whenever an edit to it is saved, which is what keeps the remembered
    /// copy in step with what the player has actually done.
    rememberRecentFile: async (rootName, relative, contentStreamReference) => {
        try {
            const data = await contentStreamReference.arrayBuffer();
            const db = await openRecentDb();
            await awaitRequest(filesTransaction(db, "readwrite")
                .put({ world: rootName, path: relative, bytes: new Blob([data]) }));
        } catch {
            // Out of quota or storage refused: the world still works, it just will not be offered
            // back later. Not worth interrupting an edit over.
        }
    },

    /// The remembered worlds, newest first. Names only - the handles never leave this file.
    listRecent: async () => {
        try {
            const db = await openRecentDb();
            const all = await awaitRequest(recentTransaction(db, "readonly").getAll());
            all.sort((a, b) => (b.openedAt ?? "").localeCompare(a.openedAt ?? ""));
            return all.slice(0, RECENT_LIMIT).map(entry => ({
                name: entry.name,
                openedAt: entry.openedAt ?? "",
                // Tells .NET which way back in to use: ask permission for a folder, or read the
                // saves straight back out of storage.
                fromStorage: !entry.handle && !!entry.hasContents,
            }));
        } catch {
            return [];
        }
    },

    /// Every save stored for a remembered world, with its size but NOT its contents - the same
    /// thing listing a folder gives. Empty for a world that was a folder, whose files stay on the
    /// player's own disk. Reopening reads this and nothing else, so it costs a few kilobytes
    /// rather than the whole world.
    recentFileList: async (rootName) => {
        try {
            const db = await openRecentDb();
            const entries = await awaitRequest(filesTransaction(db, "readonly").getAll(worldFileRange(rootName)));
            return entries.map(entry => ({ path: entry.path, length: entry.bytes?.size ?? 0 }));
        } catch {
            return [];
        }
    },

    /// Part of one stored save. Offsets let the editor read a header or a tail without pulling
    /// a 16 MB region save across for the sake of a few hundred bytes; a length of -1 means the
    /// rest of the file. Blob.slice is lazy, so only what is asked for is ever read.
    recentFileSlice: async (rootName, relative, offset, length) => {
        const db = await openRecentDb();
        const entry = await awaitRequest(filesTransaction(db, "readonly").get([rootName, relative]));
        if (!entry?.bytes) throw new Error(`"${relative}" is no longer stored for ${rootName}.`);
        const blob = entry.bytes;
        const start = offset < 0 ? Math.max(0, blob.size + offset) : Math.min(offset, blob.size);
        const end = length < 0 ? blob.size : Math.min(start + length, blob.size);
        return await blob.slice(start, end).arrayBuffer();
    },

    /// Re-opens a remembered world, asking the player's permission again. Must be called from a
    /// click: the browser refuses a permission prompt that no gesture asked for. Returns the
    /// folder name, or null when permission was refused or the folder has gone.
    reopenRecent: async (rootName) => {
        let handle;
        try {
            const db = await openRecentDb();
            const entry = await awaitRequest(recentTransaction(db, "readonly").get(rootName));
            handle = entry?.handle;
        } catch {
            return null;
        }
        if (!handle) return null;

        try {
            let permission = await handle.queryPermission({ mode: "readwrite" });
            if (permission !== "granted") permission = await handle.requestPermission({ mode: "readwrite" });
            if (permission !== "granted") return null;
            // Proves the folder is still there before the editor commits to it.
            await handle.values().next();
        } catch {
            return null;
        }

        roots.set(handle.name, handle);
        uploads.delete(handle.name);
        return handle.name;
    },

    /// Drops a world from the remembered list, saves and all.
    forgetRecent: async (rootName) => {
        try {
            const db = await openRecentDb();
            await awaitRequest(recentTransaction(db, "readwrite").delete(rootName));
            await awaitRequest(filesTransaction(db, "readwrite").delete(worldFileRange(rootName)));
        } catch {
            // Nothing to do: the list is a convenience, not state anything depends on.
        }
    },

    /// A zip the player dropped, held until .NET asks for it (unzipping happens there - the
    /// editor already carries a zip reader and the browser has none of its own).
    droppedZip: null,

    /// Hands the dropped zip's bytes over as a stream, the same way saves are read.
    readDroppedZip: async () => {
        const file = window.abioticSaveFs.droppedZip;
        if (!file) throw new Error("There is no dropped file to read.");
        window.abioticSaveFs.droppedZip = null;
        return await file.arrayBuffer();
    },

    /// Lets a folder - or a zip of one - dragged onto the window be opened like a picked one.
    /// A dropped folder gives us a FileSystemDirectoryHandle through getAsFileSystemHandle(), so
    /// it lands in the same registry as a picked folder and behaves identically from then on. A
    /// dropped zip is held for .NET to unpack. Registers the handler once and calls back into
    /// .NET with the folder name, or with the zip's name for it to fetch and unpack.
    listenForDroppedFolder: (dotNetRef) => {
        if (window.__abioticDropWired) return;
        window.__abioticDropWired = true;

        // Without preventing dragover the browser navigates away to the dropped file instead.
        window.addEventListener("dragover", event => event.preventDefault());

        window.addEventListener("drop", async event => {
            event.preventDefault();
            const items = [...(event.dataTransfer?.items ?? [])];

            // A zip first: it is a plain file, so it never produces a directory handle and would
            // otherwise fall through the loop below and be ignored without a word.
            const zip = [...(event.dataTransfer?.files ?? [])]
                .find(file => file.name.toLowerCase().endsWith(".zip"));
            if (zip) {
                window.abioticSaveFs.droppedZip = zip;
                await dotNetRef.invokeMethodAsync("OnZipDropped", zip.name);
                return;
            }

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

    /// Turns the browser's own "leave this page?" confirmation on and off. Armed only while the
    /// editor is holding edits nobody has saved yet: those live in the page and go with it when
    /// the tab closes or reloads. Browsers ignore any wording we supply and show their own, so
    /// there is nothing to translate here - only the handler's presence matters.
    warnBeforeLeaving: (on) => {
        if (on) {
            if (window.__abioticLeaveGuard) return;
            window.__abioticLeaveGuard = (event) => { event.preventDefault(); event.returnValue = ""; };
            window.addEventListener("beforeunload", window.__abioticLeaveGuard);
            return;
        }
        if (!window.__abioticLeaveGuard) return;
        window.removeEventListener("beforeunload", window.__abioticLeaveGuard);
        window.__abioticLeaveGuard = null;
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

    /// Only the last maxBytes. Used to find the region id a save belongs to, which the game
    /// writes near the end of the file - a lazy Blob slice, so listing a world's regions costs
    /// a few hundred bytes per save instead of reading tens of megabytes.
    readTail: async (path, maxBytes) => {
        const slice = (blob) => blob.slice(Math.max(0, blob.size - maxBytes)).arrayBuffer();

        const uploaded = uploadedBlob(path);
        if (uploaded) return await slice(uploaded);

        const { rootName, relative } = splitPath(path);
        const { handle } = await fileHandle(rootName, relative);
        return await slice(await handle.getFile());
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
