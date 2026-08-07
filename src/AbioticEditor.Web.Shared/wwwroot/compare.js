// Export helpers for the Compare tool: copy a Markdown report to the clipboard, or trigger a
// browser download of it as a .md file. A Blazor Server host has no filesystem of its own to
// write "to disk" the way the desktop app does, so a download is the closest equivalent.
export async function copyText(content) {
    await navigator.clipboard.writeText(content);
}

export function downloadText(fileName, content) {
    const blob = new Blob([content], { type: "text/markdown" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
}
