// A small wrapper around the host bridge that the editor injects as `window.abiotic`.
//
// For a directory-served web tool the bridge is injected just after the page loads, so it may
// not exist on the very first line of app code. `whenReady` resolves once it is available; the
// helpers below await it, so app code can call them immediately without racing the host.

export function whenReady() {
  return new Promise((resolve) => {
    const tick = () =>
      window.abiotic && window.abiotic.request ? resolve(window.abiotic) : setTimeout(tick, 40);
    tick();
  });
}

/** Send a request to the plugin's handleMessage and resolve with its reply (parsed JSON). */
export async function request(payload) {
  const bridge = await whenReady();
  return bridge.request(payload);
}

/** Write a line to the editor log. */
export async function log(message) {
  const bridge = await whenReady();
  bridge.log(String(message));
}

/** Subscribe to host events pushed to the page. */
export function onEvent(handler) {
  whenReady().then((bridge) => bridge.onEvent(handler));
}
