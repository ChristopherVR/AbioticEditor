// Picking individual files for the browser host, backing AbioticEditor.Ui.IFilePicker.
//
// Folder picking is NOT here: a granted directory handle has to be kept by the code that later
// reads and writes the saves inside it, so it lives in saveFileSystem.js and IFolderPicker
// delegates there. Keeping one registry of handles is what stops the editor opening a folder it
// then cannot touch.

function toAcceptTypes(fileTypes) {
    if (!fileTypes || fileTypes.length === 0) return undefined;
    return fileTypes.map(ft => ({
        description: ft.name,
        accept: { "application/octet-stream": ft.extensions },
    }));
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

        let handles;
        try {
            handles = await window.showOpenFilePicker({
                multiple: !!multiple,
                types: toAcceptTypes(fileTypes),
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
