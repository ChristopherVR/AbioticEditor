window.abiotic = window.abiotic || {};
window.abiotic.modal = (() => {
    let active;
    function onKeyDown(event) {
        if (!active) return;
        const tab = event.target.closest('[role="tab"]');
        if (tab && active.contains(tab) && ["ArrowDown", "ArrowUp", "ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
            const tabs = [...tab.closest('[role="tablist"]').querySelectorAll('[role="tab"]')];
            const step = ["ArrowUp", "ArrowLeft"].includes(event.key) ? -1 : 1;
            const index = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1
                : (tabs.indexOf(tab) + step + tabs.length) % tabs.length;
            event.preventDefault();
            tabs[index].focus();
            tabs[index].click();
            return;
        }
        if (event.key !== "Tab") return;
        const items = [...active.querySelectorAll('button:not([disabled]), [href], summary, input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
            .filter(element => element.tabIndex >= 0 && element.getClientRects().length > 0);
        if (!items.length) { event.preventDefault(); active.focus(); return; }
        const first = items[0], last = items[items.length - 1];
        if (!active.contains(document.activeElement) || document.activeElement === active) {
            event.preventDefault(); (event.shiftKey ? last : first).focus();
        }
        else if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }
    return {
        activate(dialog, initialFocus) {
            if (active !== dialog) {
                document.removeEventListener("keydown", onKeyDown, true);
                active = dialog;
                document.addEventListener("keydown", onKeyDown, true);
            }
            (initialFocus || dialog).focus();
        },
        deactivate() { document.removeEventListener("keydown", onKeyDown, true); active = undefined; }
    };
})();

// Pictures marked data-zoom open large on click: scroll to zoom in and out, drag to move round,
// Escape or a click outside to close. Used for the "where it is" pictures in the world lists.
window.abiotic.zoom = (() => {
    let overlay, image, scale = 1, x = 0, y = 0, drag = null;
    function apply() { image.style.transform = `translate(${x}px, ${y}px) scale(${scale})`; }
    function close() { if (overlay) { overlay.remove(); overlay = null; document.removeEventListener("keydown", onKey, true); } }
    function onKey(e) { if (e.key === "Escape") { e.stopPropagation(); close(); } }
    function open(src, alt) {
        close();
        scale = 1; x = 0; y = 0;
        overlay = document.createElement("div");
        overlay.className = "zoom-overlay";
        overlay.setAttribute("role", "dialog");
        overlay.setAttribute("aria-label", alt || "Picture");
        image = document.createElement("img");
        image.src = src;
        image.alt = alt || "";
        image.className = "zoom-image";
        image.draggable = false;
        const hint = document.createElement("span");
        hint.className = "zoom-hint";
        hint.textContent = "Scroll to zoom, drag to move, Esc to close";
        overlay.append(image, hint);
        overlay.addEventListener("click", e => { if (e.target === overlay) close(); });
        overlay.addEventListener("wheel", e => {
            e.preventDefault();
            const next = Math.min(8, Math.max(1, scale * (e.deltaY < 0 ? 1.15 : 1 / 1.15)));
            const rect = image.getBoundingClientRect();
            const cx = e.clientX - (rect.left + rect.width / 2), cy = e.clientY - (rect.top + rect.height / 2);
            x -= cx * (next / scale - 1); y -= cy * (next / scale - 1);
            if (next === 1) { x = 0; y = 0; }
            scale = next;
            apply();
        }, { passive: false });
        image.addEventListener("pointerdown", e => { drag = [e.clientX - x, e.clientY - y]; image.setPointerCapture(e.pointerId); });
        image.addEventListener("pointermove", e => { if (drag) { x = e.clientX - drag[0]; y = e.clientY - drag[1]; apply(); } });
        image.addEventListener("pointerup", () => { drag = null; });
        document.body.appendChild(overlay);
        document.addEventListener("keydown", onKey, true);
    }
    document.addEventListener("click", e => {
        const target = e.target instanceof Element ? e.target.closest("img[data-zoom]") : null;
        if (!target) return;
        e.preventDefault();
        open(target.currentSrc || target.src, target.alt);
    });
    return { open, close };
})();
