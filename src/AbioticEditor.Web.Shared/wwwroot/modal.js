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
