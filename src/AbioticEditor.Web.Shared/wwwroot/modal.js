window.abiotic = window.abiotic || {};
window.abiotic.modal = (() => {
    let active;
    function onKeyDown(event) {
        if (event.key !== "Tab" || !active) return;
        const items = [...active.querySelectorAll('button:not([disabled]), [href], summary, input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
            .filter(element => element.getClientRects().length > 0);
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
