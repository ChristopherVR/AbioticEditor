// Pane clamp bounds mirror the native ResponsivePaneController:
// file pane 220-600, slot editor pane 260-680.
const bounds = { file: { min: 220, max: 600 }, details: { min: 260, max: 680 } };

export function attach(element, dotnet, pane) {
    if (!element || element.dataset.splitterAttached === "true") return;
    element.dataset.splitterAttached = "true";

    element.addEventListener("pointerdown", event => {
        if (event.target.closest("button")) return;
        const shell = element.closest(".workspace-shell");
        // No drag-resize while in drawer mode or while this pane is collapsed;
        // the rail then only hosts the show/hide chevron (native parity).
        if (shell.classList.contains("drawer-mode")) return;
        if (shell.classList.contains(pane === "file" ? "file-collapsed" : "details-collapsed")) return;
        event.preventDefault();
        element.setPointerCapture(event.pointerId);
        const startX = event.clientX;
        const property = pane === "file" ? "--file-pane-width" : "--details-pane-width";
        const current = parseFloat(getComputedStyle(shell).getPropertyValue(property)) || (pane === "file" ? 340 : 400);

        const move = moveEvent => {
            const delta = moveEvent.clientX - startX;
            const width = Math.round(current + (pane === "file" ? delta : -delta));
            const next = Math.max(bounds[pane].min, Math.min(bounds[pane].max, width));
            shell.style.setProperty(property, `${next}px`);
        };
        const end = endEvent => {
            element.removeEventListener("pointermove", move);
            element.removeEventListener("pointerup", end);
            element.removeEventListener("pointercancel", end);
            const width = Math.round(parseFloat(getComputedStyle(shell).getPropertyValue(property)));
            dotnet.invokeMethodAsync("ResizePane", pane, width);
            if (element.hasPointerCapture(endEvent.pointerId)) element.releasePointerCapture(endEvent.pointerId);
        };
        element.addEventListener("pointermove", move);
        element.addEventListener("pointerup", end);
        element.addEventListener("pointercancel", end);
    });
}

// Responsive thresholds: below 900 the side panes become slide-in overlay drawers
// (matching the 900px stacked-layout media query, so the panes never render as a
// stack above the editor); below 1150 the inline panes auto-collapse.
let viewport = null;

export function watch(dotnet) {
    unwatch();
    const drawer = window.matchMedia("(max-width: 899.98px)");
    const compact = window.matchMedia("(max-width: 1149.98px)");
    const notify = () => dotnet
        .invokeMethodAsync("ViewportChanged", drawer.matches ? "drawer" : compact.matches ? "compact" : "wide")
        .catch(() => { });
    drawer.addEventListener("change", notify);
    compact.addEventListener("change", notify);
    viewport = { drawer, compact, notify };
    notify();
}

export function unwatch() {
    if (!viewport) return;
    viewport.drawer.removeEventListener("change", viewport.notify);
    viewport.compact.removeEventListener("change", viewport.notify);
    viewport = null;
}
