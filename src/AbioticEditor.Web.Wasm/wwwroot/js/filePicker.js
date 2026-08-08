// Picking individual files for the browser host, backing AbioticEditor.Ui.IFilePicker.
//
// Folder picking is NOT here: a granted directory handle has to be kept by the code that later
// reads and writes the saves inside it, so it lives in saveFileSystem.js and IFolderPicker
// delegates there. Keeping one registry of handles is what stops the editor opening a folder it
// then cannot touch.

// A media type per extension. showOpenFilePicker groups the accepted extensions under one, and
// naming a real one is what makes the operating system's own dialog treat them as a known kind
// (and, on macOS, actually enable those files) rather than as anonymous binary blobs.
const mediaTypes = {
    ".json": "application/json",
    ".zip": "application/zip",
    ".txt": "text/plain",
    ".ini": "text/plain",
    ".cfg": "text/plain",
};

function toAcceptTypes(fileTypes) {
    if (!fileTypes || fileTypes.length === 0) return undefined;
    return fileTypes.map(ft => {
        const accept = {};
        for (const extension of ft.extensions ?? []) {
            const media = mediaTypes[extension.toLowerCase()] ?? "application/octet-stream";
            (accept[media] ??= []).push(extension);
        }
        return { description: ft.name, accept };
    });
}

/// The everywhere-fallback for choosing files: a hidden <input type="file">. Resolves to the same
/// shape showOpenFilePicker produces, and to an empty list when the player cancels.
function pickWithInput(multiple, fileTypes) {
    return new Promise((resolve, reject) => {
        const input = document.createElement("input");
        input.type = "file";
        input.multiple = !!multiple;
        const extensions = (fileTypes ?? []).flatMap(ft => ft.extensions ?? []);
        if (extensions.length > 0) input.accept = extensions.join(",");
        input.style.display = "none";
        document.body.appendChild(input);

        let settled = false;
        const finish = async (files) => {
            if (settled) return;
            settled = true;
            input.remove();
            try {
                const result = [];
                for (const file of files) {
                    result.push({ name: file.name, bytes: new Uint8Array(await file.arrayBuffer()) });
                }
                resolve(result);
            } catch (error) {
                reject(error);
            }
        };

        input.addEventListener("change", () => finish([...input.files]));
        input.addEventListener("cancel", () => finish([]));
        input.click();
    });
}

window.abioticFilePicker = {
    // True everywhere now: the fallback below covers browsers without the File System Access API.
    isSupported: () => true,

    pickFiles: async (title, multiple, fileTypes) => {
        // Firefox and Safari have no showOpenFilePicker, but a plain file input works everywhere
        // and is all this needs: the bytes are read into memory either way, and nothing here ever
        // writes back through the handle.
        if (!window.showOpenFilePicker) return await pickWithInput(multiple, fileTypes);

        const types = toAcceptTypes(fileTypes);
        let handles;
        try {
            handles = await window.showOpenFilePicker({
                multiple: !!multiple,
                types,
                // Without this the dialog still offers "All files", and Chrome remembers that
                // choice per site - so a picker asked for one kind of file quietly went back to
                // showing everything, and the caller got handed something it cannot read.
                excludeAcceptAllOption: types !== undefined,
            });
        } catch (error) {
            if (error && error.name === "AbortError") return [];
            throw error;
        }

        const result = [];
        for (const handle of handles) {
            const file = await handle.getFile();
            result.push({ name: file.name, bytes: new Uint8Array(await file.arrayBuffer()) });
        }
        return result;
    },
};
