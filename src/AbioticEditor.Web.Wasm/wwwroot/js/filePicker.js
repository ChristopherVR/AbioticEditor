// Browser-native file/folder pickers for the Wasm host, backing AbioticEditor.Ui.IFilePicker /
// IFolderPicker. Prefers the File System Access API (Chromium only); throws a clear,
// catchable error everywhere else so BrowserFilePickerService can surface host-appropriate
// guidance instead of a raw JS exception.

function toAcceptTypes(fileTypes) {
    if (!fileTypes || fileTypes.length === 0) return undefined;
    return fileTypes.map(ft => ({
        description: ft.name,
        accept: { "application/octet-stream": ft.extensions },
    }));
}

window.abioticFilePicker = {
    isSupported: () => typeof window.showOpenFilePicker === "function",
    isFolderPickerSupported: () => typeof window.showDirectoryPicker === "function",

    pickFiles: async (title, multiple, fileTypes) => {
        if (!window.showOpenFilePicker) {
            throw new Error("This browser does not support picking files from a page action (only drag-and-drop or the Choose File button). Try Chrome or Edge, or use the on-page file input instead.");
        }
        let handles;
        try {
            handles = await window.showOpenFilePicker({
                multiple: !!multiple,
                types: toAcceptTypes(fileTypes),
            });
        } catch (e) {
            if (e && e.name === "AbortError") return [];
            throw e;
        }
        const result = [];
        for (const handle of handles) {
            const file = await handle.getFile();
            const bytes = new Uint8Array(await file.arrayBuffer());
            result.push({ name: file.name, bytes });
        }
        return result;
    },

    pickFolder: async (title) => {
        if (!window.showDirectoryPicker) {
            throw new Error("This browser does not support picking a folder from a page action. Try Chrome or Edge.");
        }
        try {
            const handle = await window.showDirectoryPicker();
            return { name: handle.name };
        } catch (e) {
            if (e && e.name === "AbortError") return null;
            throw e;
        }
    },
};
