// HTML5 drag-and-drop plumbing Blazor's event args cannot reach, shared by every slot
// surface (transmog grid, inventory paper-doll/hotbar/pockets, shared slot grids, and the
// sidebar item palette):
// - dragstart must prime dataTransfer or Firefox refuses to start the drag at all;
// - dragover must call preventDefault for an element to accept drops, and dropEffect is what
//   gives the pointer its copy vs. not-allowed cursor over eligible/ineligible slots.
// The handlers are delegated document-level listeners keyed off the data-drag attribute on
// draggable tiles and the data-slot-drop attribute on drop targets, so installing them once
// is enough and elements from other views (which never carry the attributes) are untouched.
//
// The dragActive flag matters: Blazor renders the per-slot data-dropzone verdicts only after
// the server has processed dragstart, so a very fast drag can reach a slot before the
// attribute exists. Drops are therefore allowed on any slot surface while a tracked drag is
// active and the (re-)validation in the Blazor drop handler stays authoritative; the
// attribute, once rendered, only refines the cursor to not-allowed over ineligible slots.
let installed = false;
let dragActive = false;

export function init() {
    if (installed) return;
    installed = true;

    document.addEventListener("dragstart", (event) => {
        const source = event.target instanceof Element ? event.target.closest("[data-drag]") : null;
        if (!source || !event.dataTransfer) return;
        dragActive = true;
        event.dataTransfer.setData("text/plain", source.getAttribute("data-drag") ?? "");
        event.dataTransfer.effectAllowed = "copyMove";
        setDragGhost(event, source);
    });

    document.addEventListener("dragend", () => { dragActive = false; });
    document.addEventListener("drop", () => { dragActive = false; });

    document.addEventListener("dragover", (event) => {
        if (!dragActive || !event.dataTransfer) return;
        const zone = event.target instanceof Element ? event.target.closest("[data-slot-drop]") : null;
        if (!zone) return;
        event.preventDefault();
        // "none" both shows the not-allowed cursor and stops the drop event from firing.
        event.dataTransfer.dropEffect = zone.getAttribute("data-dropzone") === "blocked" ? "none" : "copy";
    });
}

// The picture that follows the pointer during a drag. Left to itself the engine snapshots
// the dragged element, and what it decides that element is differs between Chrome and the
// WebView2 control the desktop app runs in - in the item catalog it could end up dragging a
// picture of the whole grid. Building the ghost ourselves means one item's artwork every
// time, in every engine.
//
// The ghost has to be a real, laid-out, on-screen element or it comes out blank, so it is
// parked off-canvas and removed on the next frame (once, and only once, the engine has taken
// its snapshot - removing it synchronously loses the image).
function setDragGhost(event, source) {
    const art = source.querySelector("img");
    if (!art || !event.dataTransfer.setDragImage) return;

    const ghost = document.createElement("div");
    ghost.className = "drag-ghost";
    const copy = document.createElement("img");
    copy.src = art.currentSrc || art.src;
    copy.alt = "";
    ghost.appendChild(copy);
    document.body.appendChild(ghost);

    const size = ghost.offsetWidth || 56;
    event.dataTransfer.setDragImage(ghost, size / 2, size / 2);
    requestAnimationFrame(() => ghost.remove());
}

// True on phones/tablets and any other device whose main pointer cannot hover. Dragging a
// tile across the screen is unreliable there (the browser scrolls the page instead), so the
// slot surfaces use it to offer the pick-then-place flow and to word their on-screen tips
// for what the device can actually do. Hybrid laptops with both a mouse and a touchscreen
// report "(hover: hover)" and keep the drag wording.
export function isTouch() {
    try {
        return window.matchMedia("(hover: none)").matches
            || window.matchMedia("(pointer: coarse)").matches;
    } catch {
        return false;
    }
}
