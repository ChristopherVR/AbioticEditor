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

window.abioticFilePicker = {
    isSupported: () => typeof window.showOpenFilePicker === "function",

    pickFiles: async (title, multiple, fileTypes) => {
        if (!window.showOpenFilePicker) {
            throw new Error("This browser cannot open a file from a page action. Try Chrome or Edge, or use the Choose File button instead.");
        }
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
