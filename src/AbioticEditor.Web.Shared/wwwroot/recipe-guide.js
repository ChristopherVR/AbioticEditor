// An explicit guide navigation should reveal its result, even when the book was scrolled
// or the window is too short to show the instructions and the tree together.
export function reveal(tree) {
    if (!tree?.isConnected) return;
    tree.focus({ preventScroll: true });
    tree.scrollIntoView({ block: "start", inline: "nearest", behavior: "instant" });
}
