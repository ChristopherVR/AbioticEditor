// 3D base view: a view and interaction layer only. All save knowledge (which objects exist,
// which are player-built, what a move means in the save) stays in C#. This module draws the
// objects it is handed and reports clicks and gizmo results back.
//
// Coordinates: the C# side already converts the save's transforms into THIS scene's space
// (PlacedSceneSpace in Core): right-handed, Y up, metres. Save space is Unreal's left-handed Z-up
// centimetres; the mapping is viewer = (X, Z, Y) / 100, and rotation quaternions map
// (x, y, z, w) -> (-x, -z, -y, w). This file never converts, it only draws what it receives and
// sends back viewer-space values for C# to convert. Axes drawn in the scene: red = save X,
// blue = save Y (the viewer's Z), green = up (save Z, the viewer's Y).
//
// Three.js is vendored under ./lib/three (MIT); nothing is loaded from a CDN so the desktop app
// works offline.
//
// Game models (optional): when the desktop host has a 3D model plugin installed, the host answers
// under ./scene-models/ with each class's parts (already in this scene's space), mesh geometry in
// a small binary layout (SceneMeshFormat in the plugin SDK) and PNG textures. Objects whose class
// has a model are drawn with it; everything else keeps its box. Without the plugin, or in the
// browser build, those requests fail and nothing changes.
import * as THREE from "./lib/three/three.module.min.js";
import { OrbitControls } from "./lib/three/OrbitControls.min.js";
import { TransformControls } from "./lib/three/TransformControls.min.js";
import { computeBoundsTree, disposeBoundsTree, acceleratedRaycast } from "./lib/three/three-mesh-bvh.min.js";

// Raycasts (walking, placement checks, picking) test a mesh's triangles through a bounding-volume
// tree when its geometry has one, instead of every triangle. Trees are built lazily, for the
// geometries a ray actually reaches (see nearSolids).
THREE.BufferGeometry.prototype.computeBoundsTree = computeBoundsTree;
THREE.BufferGeometry.prototype.disposeBoundsTree = disposeBoundsTree;
THREE.Mesh.prototype.raycast = acceleratedRaycast;

// Index = PlacedObjectCategory enum value: Unknown, Bench, Container, Power, Light, Structure, Other.
const CATEGORY_COLORS = [0x9aa0a8, 0xf08418, 0x8ccb58, 0x56c4e8, 0xf5d020, 0xd14a30, 0xb08ce0];
// Placeholder box sizes in metres (viewer x, up, z). Real meshes are not loaded; scale3D multiplies these.
const CATEGORY_SIZES = [
    [0.5, 0.5, 0.5], [1.6, 0.9, 0.8], [0.8, 0.9, 0.6], [0.4, 0.4, 0.3],
    [0.3, 0.5, 0.3], [1.5, 1.2, 0.3], [0.6, 0.8, 0.6],
];
const LEVEL_PLACED_DIM = 0.5;
// Staged base edits: an object staged for deletion is drawn red, a staged copy (not in the save yet) cyan.
const MARK_DELETED_COLOR = 0xff3b30;
const MARK_COPY_COLOR = 0x22e6e6;
const MAX_LABELS = 70;
// Game models tint by multiplying the texture, so the marks are lighter than the box colours.
const MODEL_TINT_DELETED = 0xff6a60;
const MODEL_TINT_COPY = 0x7ff6f6;
const MODEL_TINT_LEVEL_PLACED = 0xc4c4c4;
const MODEL_BASE = "scene-models";
const MODEL_FETCH_CONCURRENCY = 6;
const CLASS_BATCH = 24; // small batches, so models start appearing while the rest are still being read
const CLASS_REQUESTS = 3; // batches asked for at once
const LEVEL_MAX_INSTANCES = 25000;
const LEVEL_RETRY_MS = 4000;
// The ceiling cut measures the base floor from player-built objects within this reach of the view
// centre (bases group objects within about 30 m of a bench).
const BASE_REACH_M = 30;
// A level piece whose texture would repeat less than once per this many metres (a plane scaled
// over a reservoir) is textured from above in world space instead, every DEFAULT_WORLD_TILE_M
// metres unless its material says otherwise.
const STRETCHED_REPEAT_M = 12;
const DEFAULT_WORLD_TILE_M = 4;

/** Fetches with a JSON body and a JSON answer; null for "no content", throws on failure. */
async function postJson(url, body) {
    const response = await fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
    if (response.status === 204) return null;
    if (!response.ok) throw new Error(`${url}: ${response.status}`);
    return response.json();
}

function assetUrl(id) {
    return `${MODEL_BASE}/asset/${id.split("/").map(encodeURIComponent).join("/")}`;
}

/** Decodes one mesh in the ABM1 layout (see SceneMeshFormat) into a BufferGeometry with one group per section. */
function parseMesh(buffer) {
    const view = new DataView(buffer);
    const magic = String.fromCharCode(view.getUint8(0), view.getUint8(1), view.getUint8(2), view.getUint8(3));
    if (magic !== "ABM1") throw new Error("not an ABM1 mesh");
    const vertexCount = view.getUint32(4, true);
    const indexCount = view.getUint32(8, true);
    const sectionCount = view.getUint32(12, true);
    const flags = view.getUint32(16, true);
    const wide = (flags & 1) === 1;
    const colored = (flags & 2) === 2;
    let at = 20;
    const geometry = new THREE.BufferGeometry();
    const sections = [];
    for (let i = 0; i < sectionCount; i++, at += 12) {
        sections.push([view.getUint32(at, true), view.getUint32(at + 4, true), view.getUint32(at + 8, true)]);
    }
    const align4 = n => (n + 3) & ~3;
    geometry.setAttribute("position", new THREE.BufferAttribute(new Float32Array(buffer, at, vertexCount * 3), 3));
    at += vertexCount * 12;
    geometry.setAttribute("normal", new THREE.BufferAttribute(new Int16Array(buffer, at, vertexCount * 3), 3, true));
    at += align4(vertexCount * 6);
    geometry.setAttribute("uv", new THREE.BufferAttribute(new Float32Array(buffer, at, vertexCount * 2), 2));
    at += vertexCount * 8;
    if (colored) {
        // Terrain layer weights (see SceneMaterial.Layers), four bytes per vertex.
        geometry.setAttribute("color", new THREE.BufferAttribute(new Uint8Array(buffer, at, vertexCount * 4), 4, true));
        at += vertexCount * 4;
    }
    geometry.setIndex(new THREE.BufferAttribute(wide ? new Uint32Array(buffer, at, indexCount) : new Uint16Array(buffer, at, indexCount), 1));
    for (const [material, first, count] of sections) geometry.addGroup(first, count, material);
    geometry.computeBoundingSphere();
    geometry.computeBoundingBox();
    return geometry;
}

export function isWebGlAvailable() {
    try {
        const canvas = document.createElement("canvas");
        return !!(canvas.getContext("webgl2") || canvas.getContext("webgl"));
    } catch {
        return false;
    }
}

// A view the player left (another tab, the map) is kept for a while instead of being thrown away:
// its renderer, compiled shaders, models, textures and level stay in memory, so coming back shows
// it at once instead of reading and uploading everything again. One per save, the latest only.
const PARK_MS = 15 * 60 * 1000;
const parked = new Map(); // key -> { api, timer }

/** The nearest ancestor that scrolls (the editor's page body), or null for the window itself. */
function scrollParent(el) {
    for (let p = el.parentElement; p; p = p.parentElement) {
        const overflow = getComputedStyle(p).overflowY;
        if ((overflow === "auto" || overflow === "scroll") && p.clientHeight > 0) return p;
    }
    return null;
}

/**
 * Sizes the 3D view so it, and the Map / 3D bar above it, fill the editor's scrolling area exactly,
 * and scrolls them to the top of it: everything the view needs is in sight without scrolling the
 * page. Kept up to date as the window is resized. Returns a handle whose stop() ends that.
 */
export function fitToWindow(root) {
    const scroller = scrollParent(root);
    const bar = () => document.querySelector('[data-b3d="bar"]');
    const apply = () => {
        const canvas = root.querySelector('[data-b3d="canvas"]');
        const stage = root.querySelector(".b3d-stage");
        if (!canvas || !stage) return;
        const available = scroller ? scroller.clientHeight : window.innerHeight;
        const top = (bar() ?? canvas).getBoundingClientRect().top;
        const above = canvas.getBoundingClientRect().top - top;
        const below = stage.getBoundingClientRect().bottom - canvas.getBoundingClientRect().bottom;
        root.style.setProperty("--b3d-fit", `${Math.max(360, Math.floor(available - above - below - 14))}px`);
    };
    apply();
    (bar() ?? root).scrollIntoView({ block: "start" });
    const observer = new ResizeObserver(apply);
    observer.observe(scroller ?? document.documentElement);
    window.addEventListener("resize", apply);
    return {
        refit() { apply(); (bar() ?? root).scrollIntoView({ block: "start", behavior: "smooth" }); },
        stop() { observer.disconnect(); window.removeEventListener("resize", apply); },
    };
}

/**
 * The Bases map's picture of the level under it, drawn by a hidden view kept for these shots (the
 * desktop editor, with the game's files). Null when the game's models are not available here.
 */
let shotView = null;
export async function mapBackdrop(options) {
    try {
        const status = await (await fetch(`${MODEL_BASE}/status`)).json();
        if (!status?.available) return null;
    } catch {
        return null;
    }
    if (!shotView) {
        const host = document.createElement("div");
        host.style.cssText = "position:fixed;left:-10000px;top:0;width:64px;height:64px;pointer-events:none;";
        document.body.appendChild(host);
        shotView = buildView(host, { invokeMethodAsync: async () => { } });
    }
    return shotView.topDownShot(options);
}

/**
 * Puts the Bases map's level picture into its image element straight from the browser (the picture
 * never travels to the editor and back). Pictures are kept for the session by key. True when the
 * map got a picture.
 */
const backdrops = new Map();
export async function fillMapBackdrop(element, key, options) {
    if (!element) return "none";
    element.dataset.shot = JSON.stringify(options); // what was asked for (diagnostics and UI tests)
    let picture = backdrops.get(key);
    if (picture === undefined) {
        element.removeAttribute("href"); // the old framing's picture would sit misaligned meanwhile
        picture = await mapBackdrop(options).catch(() => null);
        // Only a finished picture is kept: an empty answer or one made while level files were still
        // being read would otherwise stay for the whole session.
        if (picture?.pending) return "pending";
        if (picture) backdrops.set(key, picture);
    }
    if (!picture) return "none";
    element.setAttribute("href", picture);
    return "shown";
}

export function createView(host, dotnet, parkKey) {
    const waiting = parkKey ? parked.get(parkKey) : null;
    if (waiting) {
        parked.delete(parkKey);
        clearTimeout(waiting.timer);
        waiting.api.reattach(host, dotnet);
        return waiting.api;
    }
    for (const [key, entry] of parked) { clearTimeout(entry.timer); entry.api.dispose(); parked.delete(key); }
    return buildView(host, dotnet);
}

function buildView(host, dotnet) {
    // Smoothing the edges (multisampling) costs most on a high-resolution screen, where the edges are
    // already fine: it is only used on ordinary screens. "high-performance" asks a laptop for its
    // faster graphics chip.
    const DPR = window.devicePixelRatio || 1;
    const renderer = new THREE.WebGLRenderer({ antialias: DPR < 1.5, powerPreference: "high-performance" });
    // Full sharpness when the view is still; while it moves (orbit, pan, walk) it is drawn at a lower
    // resolution, which on a high-DPI screen is most of the cost of a frame. Sharp again 200 ms after
    // the last movement. How low it goes while moving follows how fast frames actually are: a fast
    // machine keeps near full sharpness, a slow one drops further until moving is smooth again.
    const SHARP_RATIO = Math.min(DPR, 2);
    let movingRatio = Math.min(SHARP_RATIO, 1) * 0.75;
    let movingUntil = 0, sharpTimer = 0, lastFrameAt = 0, slowFrames = 0, fastFrames = 0;
    renderer.setPixelRatio(SHARP_RATIO);
    function markMoving() {
        movingUntil = performance.now() + 200;
        if (renderer.getPixelRatio() !== movingRatio) { renderer.setPixelRatio(movingRatio); resize(); }
        clearTimeout(sharpTimer);
        sharpTimer = setTimeout(() => {
            if (disposed || performance.now() < movingUntil) return;
            lastFrameAt = 0;
            renderer.setPixelRatio(SHARP_RATIO);
            resize();
        }, 220);
    }
    /** Called each frame while moving: frames slower than 30 a second lower the moving resolution, fast ones raise it. */
    function tuneMovingRatio(now) {
        if (performance.now() >= movingUntil) { lastFrameAt = 0; return; }
        if (lastFrameAt) {
            const ms = now - lastFrameAt;
            if (ms > 34) { slowFrames++; fastFrames = 0; } else if (ms < 18) { fastFrames++; slowFrames = 0; }
            const top = Math.min(SHARP_RATIO, 1.25);
            if (slowFrames >= 6 && movingRatio > 0.45) { movingRatio = Math.max(0.45, movingRatio * 0.85); slowFrames = 0; renderer.setPixelRatio(movingRatio); resize(); }
            else if (fastFrames >= 30 && movingRatio < top) { movingRatio = Math.min(top, movingRatio * 1.1); fastFrames = 0; renderer.setPixelRatio(movingRatio); resize(); }
        }
        lastFrameAt = now;
    }
    renderer.localClippingEnabled = true; // the level's ceiling cut
    // Reading every shader's compile log (three's error check) makes the browser finish each compile on
    // the spot, which defeats parallel compiling (compileAsync). The view's shaders are fixed and tested.
    renderer.debug.checkShaderErrors = false;
    host.appendChild(renderer.domElement);
    renderer.domElement.classList.add("b3d-canvas-el");

    const labelLayer = document.createElement("div");
    labelLayer.className = "b3d-labels";
    host.appendChild(labelLayer);

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x101418);
    const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 5000);
    camera.position.set(30, 30, 30);
    const ambient = new THREE.HemisphereLight(0xffffff, 0x334455, 1.6);
    scene.add(ambient);
    const sun = new THREE.DirectionalLight(0xffffff, 1.4);
    sun.position.set(0.4, 1, 0.6);
    scene.add(sun);

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.14;
    controls.screenSpacePanning = true;
    // The wheel zooms towards what is under the pointer, not only the orbit centre, so a detail
    // anywhere on screen can be reached; the near limit lets the camera get right up to a small item.
    controls.zoomToCursor = true;
    controls.zoomSpeed = 1.4;
    controls.minDistance = 0.15;
    controls.maxPolarAngle = Math.PI * 0.499 + 0.02;

    // Orientation aids.
    const axes = new THREE.AxesHelper(5);
    scene.add(axes);
    let grid = null;

    const boxGeometry = new THREE.BoxGeometry(1, 1, 1);
    const edgesGeometry = new THREE.EdgesGeometry(boxGeometry);

    let objects = [];
    let keyToIndex = new Map();
    let visible = [];
    let meshes = []; // per category InstancedMesh
    let instanceToObject = []; // per category: instance id -> object index
    let selectedKey = null; // the primary selection (the gizmo and the inspector follow it)
    let selectedKeys = new Set(); // every selected key, primary included
    let labelsOn = false;
    let disposed = false;
    let reattached = false;
    let dirty = false; // true while a frame is already scheduled

    // One outline per selected object; the primary is white, the rest amber.
    const selectionGroup = new THREE.Group();
    scene.add(selectionGroup);
    const primaryMaterial = new THREE.LineBasicMaterial({ color: 0xffffff, depthTest: false, transparent: true });
    const secondaryMaterial = new THREE.LineBasicMaterial({ color: 0xffb020, depthTest: false, transparent: true });

    // A fixed-size dot per drawn object, so far-away or tiny objects stay visible when the box is
    // smaller than a pixel. Boxes are still what gets picked.
    let dots = null;

    const ghostGroup = new THREE.Group();
    scene.add(ghostGroup);

    // Power cables of the selected object (drawn through geometry so they stay visible).
    const cableGroup = new THREE.Group();
    scene.add(cableGroup);
    const cableMaterial = new THREE.LineBasicMaterial({ color: 0xffd23f, depthTest: false, transparent: true, opacity: 0.9 });

    // Markers for things of the level the view can reach: doors (round, coloured by state) and NPCs
    // (diamonds: alive, dead, pets). Each is drawn over everything at a fixed pixel size and picked in
    // screen space before objects; C# picks the colours and handles the click.
    const MARKER_PICK_PX = 10;
    const MARKER_LIFT_M = 1.2; // markers float at head height over the actor's root
    function markerTexture(draw) {
        const c = document.createElement("canvas");
        c.width = c.height = 64;
        const g = c.getContext("2d");
        g.beginPath();
        draw(g);
        g.closePath();
        g.fillStyle = "#ffffff";
        g.fill();
        g.lineWidth = 8;
        g.strokeStyle = "rgba(0,0,0,0.75)";
        g.stroke();
        const t = new THREE.CanvasTexture(c);
        t.colorSpace = THREE.SRGBColorSpace;
        return t;
    }

    function createMarkerLayer(texture, size, lift = MARKER_LIFT_M) {
        const group = new THREE.Group();
        scene.add(group);
        const material = new THREE.PointsMaterial({ size, sizeAttenuation: false, map: texture, vertexColors: true,
            transparent: true, alphaTest: 0.3, depthTest: false });
        const ringMaterial = new THREE.PointsMaterial({ size: size + 10, sizeAttenuation: false, map: texture, color: 0xffffff,
            transparent: true, alphaTest: 0.3, depthTest: false });
        const layer = {
            items: [], // [{ id, p: [x, y, z], color }]
            on: true,
            selected: null,
            rebuild() {
                for (const child of [...group.children]) {
                    group.remove(child);
                    child.geometry.dispose();
                }
                group.visible = layer.on;
                const items = layer.items;
                if (!items.length) return;
                const pos = new Float32Array(items.length * 3), col = new Float32Array(items.length * 3);
                const c = new THREE.Color();
                items.forEach((d, i) => {
                    pos.set([d.p[0], d.p[1] + lift, d.p[2]], i * 3);
                    c.setHex(d.color ?? 0x8e9aaf, THREE.SRGBColorSpace);
                    col.set([c.r, c.g, c.b], i * 3);
                });
                const g = new THREE.BufferGeometry();
                g.setAttribute("position", new THREE.BufferAttribute(pos, 3));
                g.setAttribute("color", new THREE.BufferAttribute(col, 3));
                const points = new THREE.Points(g, material);
                points.renderOrder = 12;
                points.frustumCulled = false;
                group.add(points);
                const sel = items.find(d => d.id === layer.selected);
                if (sel) {
                    const sg = new THREE.BufferGeometry();
                    sg.setAttribute("position", new THREE.BufferAttribute(new Float32Array([sel.p[0], sel.p[1] + lift, sel.p[2]]), 3));
                    const ring = new THREE.Points(sg, ringMaterial);
                    ring.renderOrder = 11; // under the coloured marker, so it reads as a white outline
                    ring.frustumCulled = false;
                    group.add(ring);
                }
            },
            /** Screen position (CSS pixels relative to the page) of a marker, or null. */
            screen(d) {
                const v = new THREE.Vector3(d.p[0], d.p[1] + lift, d.p[2]).project(camera);
                const rect = renderer.domElement.getBoundingClientRect();
                return { x: rect.left + ((v.x + 1) / 2) * rect.width, y: rect.top + ((1 - v.y) / 2) * rect.height, depth: v.z };
            },
            /** The marker nearest a screen point (within a few pixels, in front of the camera), and its distance. */
            at(clientX, clientY) {
                if (!layer.on) return null;
                let best = null, bestD = MARKER_PICK_PX;
                for (const d of layer.items) {
                    const sp = layer.screen(d);
                    if (sp.depth < -1 || sp.depth > 1) continue;
                    const dist = Math.hypot(sp.x - clientX, sp.y - clientY);
                    if (dist < bestD) { bestD = dist; best = d; }
                }
                return best ? { item: best, dist: bestD } : null;
            },
            dispose() {
                for (const child of group.children) child.geometry.dispose();
                material.dispose();
                ringMaterial.dispose();
                texture.dispose();
            },
        };
        return layer;
    }

    const doorLayer = createMarkerLayer(markerTexture(g => g.arc(32, 32, 26, 0, Math.PI * 2)), 14);
    const npcLayer = createMarkerLayer(markerTexture(g => {
        g.moveTo(32, 4); g.lineTo(60, 32); g.lineTo(32, 60); g.lineTo(4, 32);
    }), 16);
    // Items lying on the ground (small triangles just above the floor) and things of the level from
    // the world lists: buttons, breakable walls, resource nodes and the like (squares).
    const itemLayer = createMarkerLayer(markerTexture(g => { g.moveTo(32, 6); g.lineTo(60, 56); g.lineTo(4, 56); }), 12, 0.35);
    const thingLayer = createMarkerLayer(markerTexture(g => g.rect(8, 8, 48, 48)), 12, 0.6);
    const markerLayers = { door: doorLayer, npc: npcLayer, item: itemLayer, thing: thingLayer };

    // Walk mode: a first-person camera (drag to look, WASD to move) that keeps eye height above
    // whatever is under it when "stay on the floor" is on. The orbit camera is off meanwhile.
    let walkOn = false;
    let walkFloor = true;
    const EYE_M = 1.7;
    const walkKeys = new Set();
    let walkYaw = 0, walkPitch = 0;
    let walkLast = 0;
    let walkFloorCheck = 0;
    let walkLookFrom = null;

    // Game models (see the header). classModels: class path -> { state: "pending" | "ready" | "none",
    // parts: [{ geometry, materials, matrix }], box } in the object's own space.
    let modelsOn = false;
    const classModels = new Map();
    const geometryCache = new Map(); // mesh id -> Promise<BufferGeometry | null>
    const textureCache = new Map(); // texture id -> Texture
    const materialCache = new Map(); // material description -> Material
    let modelMeshes = []; // InstancedMesh per (class, part) for placed objects
    let objInstances = new Map(); // object index -> [[InstancedMesh, instance id]]
    let modelRebuildQueued = false;
    let fetchActive = 0;
    const fetchWaiting = [];

    // The level's own lamps (point, spot and rectangle lights): the nearest few become real lights,
    // every one gets a soft glow. Their count changes the shaders, so it only changes on a level load.
    const lampGroup = new THREE.Group();
    scene.add(lampGroup);
    const LAMP_CANDELA = 3; // a default engine lamp, in the view's physical light units
    let lampsOn = true;
    const glowTexture = (() => {
        const c = document.createElement("canvas");
        c.width = c.height = 64;
        const g = c.getContext("2d");
        const grad = g.createRadialGradient(32, 32, 0, 32, 32, 32);
        grad.addColorStop(0, "rgba(255,255,255,1)");
        grad.addColorStop(0.25, "rgba(255,255,255,0.55)");
        grad.addColorStop(1, "rgba(255,255,255,0)");
        g.fillStyle = grad;
        g.fillRect(0, 0, 64, 64);
        const t = new THREE.CanvasTexture(c);
        t.colorSpace = THREE.SRGBColorSpace;
        return t;
    })();

    // A fixed set of real lights, made once and pointed at the nearest lamps on each level load.
    // Three.js builds its shaders for a number of lights: when that number changed with every load,
    // every material (hundreds) was compiled again, which froze the view for seconds each time.
    // Lights without a lamp are turned down to nothing (still counted, so nothing recompiles).
    const POINT_POOL = 4, SPOT_POOL = 2;
    const pointPool = Array.from({ length: POINT_POOL }, () => new THREE.PointLight(0xffffff, 0, 10, 2));
    const spotPool = Array.from({ length: SPOT_POOL }, () => new THREE.SpotLight(0xffffff, 0, 10, Math.PI / 4, 0.5, 2));
    for (const light of pointPool) lampGroup.add(light);
    for (const light of spotPool) { lampGroup.add(light); lampGroup.add(light.target); }

    function clearLamps() {
        for (const child of [...lampGroup.children]) {
            if (!child.isSprite) continue;
            lampGroup.remove(child);
            child.material.dispose();
        }
        for (const light of [...pointPool, ...spotPool]) { light.intensity = 0; light.userData.intensity = 0; }
    }

    let levelLamps = [];
    function setLamps(list) {
        levelLamps = list ?? [];
        refreshLamps();
    }
    function refreshLamps() {
        clearLamps();
        const lamps = [...levelLamps, ...objects.filter(o => o.lampOn === true && visible.includes(keyToIndex.get(o.key)))
            .map(o => ({ position: lampPosition(o), color: [1, 0.8, 0.55], brightness: 1, rangeMetres: 6 }))]
            .sort((a, b) => new THREE.Vector3(...a.position).distanceToSquared(controls.target) - new THREE.Vector3(...b.position).distanceToSquared(controls.target));
        let points = 0, spots = 0;
        lamps.forEach(l => {
            const colour = new THREE.Color(l.color[0], l.color[1], l.color[2]);
            const glow = new THREE.Sprite(new THREE.SpriteMaterial({ map: glowTexture, color: colour, blending: THREE.AdditiveBlending,
                depthWrite: false, transparent: true, opacity: Math.min(0.9, 0.35 + 0.15 * l.brightness) }));
            glow.position.set(l.position[0], l.position[1], l.position[2]);
            const size = Math.min(1.6, 0.35 + 0.25 * Math.sqrt(Math.max(0, l.brightness)));
            glow.scale.set(size, size, 1);
            glow.userData.lamp = true;
            lampGroup.add(glow);
            const intensity = Math.min(20, LAMP_CANDELA * Math.max(0, l.brightness));
            const range = Math.max(2, l.rangeMetres || 10);
            // Lamps come nearest first; each takes a free light of its kind (a spot without a free spot
            // light lights as a point, which is close enough for context).
            const light = l.direction && spots < SPOT_POOL ? spotPool[spots++] : points < POINT_POOL ? pointPool[points++] : null;
            if (!light) return;
            light.color.copy(colour);
            light.userData.intensity = intensity;
            light.distance = range;
            light.position.set(l.position[0], l.position[1], l.position[2]);
            if (light.isSpotLight) {
                const cone = l.coneDegrees ?? 75;
                light.angle = THREE.MathUtils.degToRad(Math.min(85, Math.max(5, cone)));
                light.target.position.set(l.position[0] + l.direction[0], l.position[1] + l.direction[1], l.position[2] + l.direction[2]);
                light.target.updateMatrixWorld();
            }
        });
        applyLampsOn();
        updateLevelCut();
    }

    function lampPosition(o) {
        const box = classModels.get(modelKey(o))?.box;
        const local = box ? box.getCenter(new THREE.Vector3()) : new THREE.Vector3(0, 0.3, 0);
        if (box) local.y = box.max.y - 0.08;
        local.multiply(new THREE.Vector3(...o.s)).applyQuaternion(new THREE.Quaternion(...o.q)).add(new THREE.Vector3(...o.p));
        return local.toArray();
    }

    /** Lamps on or off: lights dimmed to nothing and glows hidden (the light count never changes). */
    function applyLampsOn() {
        let any = false;
        for (const light of [...pointPool, ...spotPool]) {
            light.intensity = lampsOn ? (light.userData.intensity ?? 0) : 0;
            if (light.intensity > 0) any = true;
        }
        for (const child of lampGroup.children) if (child.isSprite) child.visible = lampsOn && child.position.y <= levelClip.constant;
        // Lit by its own lamps, the level needs less of the flat fill light.
        ambient.intensity = any ? 1.35 : 1.6;
    }

    // Level geometry around the camera target (context only: never picked or edited).
    const levelGroup = new THREE.Group();
    scene.add(levelGroup);
    // The level drawn in few calls: once a load has finished, its pieces are merged into one batched
    // mesh per material (three's BatchedMesh), which draws hundreds of different pieces in one call
    // and skips the ones out of view one by one. The pieces themselves stay in levelGroup, hidden,
    // for clicking, walking into walls and the camera's line of sight (rays ignore visibility).
    const mergedGroup = new THREE.Group();
    scene.add(mergedGroup);
    const levelClip = new THREE.Plane(new THREE.Vector3(0, -1, 0), 1e6);
    // Stairs are cut a storey higher than the rest, so a staircase shows whole up to the next floor
    // instead of sawn off half way up (the floor above is still cut away).
    const STAIR_EXTRA_M = 2.6;
    const stairClip = new THREE.Plane(new THREE.Vector3(0, -1, 0), 1e6);
    const stairMaterials = new Map();
    function stairMaterial(material) {
        let copy = stairMaterials.get(material.uuid);
        if (!copy) {
            copy = material.clone();
            copy.clippingPlanes = [stairClip];
            stairMaterials.set(material.uuid, copy);
        }
        return copy;
    }
    let levelOptions = { enabled: false, region: null, radius: 40, cutAbove: 3, excludeActors: [] };
    let levelToken = 0;
    let levelRetry = 0;
    let levelInstances = 0;
    let levelCentre = null; // where the level was last loaded around (the ceiling cut follows it)

    // ---- helpers -----------------------------------------------------------------------------
    const tmpPos = new THREE.Vector3();
    const tmpQuat = new THREE.Quaternion();
    const tmpScale = new THREE.Vector3();
    const tmpBase = new THREE.Matrix4();
    const tmpLift = new THREE.Matrix4();
    const tmpSize = new THREE.Matrix4();
    const tmpColor = new THREE.Color();

    function baseColor(o) {
        if (o.mark === 1) return tmpColor.setHex(MARK_DELETED_COLOR);
        if (o.mark === 2) return tmpColor.setHex(MARK_COPY_COLOR);
        tmpColor.setHex(CATEGORY_COLORS[o.cat]);
        if (!o.built) tmpColor.multiplyScalar(LEVEL_PLACED_DIM);
        return tmpColor;
    }

    /** Which model an object wears: its class, plus what changes its look (paint, crops; see Base3DScene.VariantOf). */
    function modelKey(o) {
        if (!o.cls) return null;
        return o.variant ? o.cls + o.variant : o.cls;
    }

    function readyModel(o) {
        const key = modelKey(o);
        if (!modelsOn || !key) return null;
        const model = classModels.get(key);
        return model && model.state === "ready" ? model : null;
    }

    /** The object's own transform (position, rotation, per-axis scale). */
    function objectMatrix(target, o) {
        tmpPos.set(o.p[0], o.p[1], o.p[2]);
        tmpQuat.set(o.q[0], o.q[1], o.q[2], o.q[3]);
        tmpScale.set(o.s?.[0] || 1, o.s?.[1] || 1, o.s?.[2] || 1);
        return target.compose(tmpPos, tmpQuat, tmpScale);
    }

    function boxMatrix(target, o) {
        const model = readyModel(o);
        if (model) {
            // The unit box stretched over the model's real bounds, in the object's space.
            const box = model.box;
            tmpLift.makeTranslation((box.min.x + box.max.x) / 2, (box.min.y + box.max.y) / 2, (box.min.z + box.max.z) / 2);
            tmpSize.makeScale(Math.max(box.max.x - box.min.x, 0.05), Math.max(box.max.y - box.min.y, 0.05), Math.max(box.max.z - box.min.z, 0.05));
            return objectMatrix(target, o).multiply(tmpLift).multiply(tmpSize);
        }
        const [w, h, d] = CATEGORY_SIZES[o.cat] ?? CATEGORY_SIZES[0];
        tmpPos.set(o.p[0], o.p[1], o.p[2]);
        tmpQuat.set(o.q[0], o.q[1], o.q[2], o.q[3]);
        tmpScale.set(1, 1, 1);
        tmpBase.compose(tmpPos, tmpQuat, tmpScale);
        const sx = Math.abs(o.s[0]) || 1, sy = Math.abs(o.s[1]) || 1, sz = Math.abs(o.s[2]) || 1;
        tmpLift.makeTranslation(0, (h * sy) / 2, 0); // the actor origin sits on the floor
        tmpSize.makeScale(w * sx, h * sy, d * sz);
        return target.copy(tmpBase).multiply(tmpLift).multiply(tmpSize);
    }

    function requestRender() {
        if (disposed || dirty) return;
        dirty = true;
        requestAnimationFrame(frame);
    }

    function updateClipping() {
        const d = walkOn ? 0.5 : camera.position.distanceTo(controls.target);
        camera.near = Math.max(0.05, d / 400);
        camera.far = Math.max(2000, d * 300);
        camera.updateProjectionMatrix();
    }

    function frame(now) {
        if (disposed) return;
        dirty = false;
        // Orbit easing keeps the camera gliding for a moment after a drag; fly keys move it.
        const flying = !walkOn && flyKeys.size > 0 && flyStep(now);
        const easing = !walkOn && controls.enableDamping && controls.update();
        if (flying || easing) { markMoving(); requestRender(); }
        else flyLast = 0;
        updateClipping();
        renderer.render(scene, camera);
        tuneMovingRatio(now);
        renderLabels();
    }

    // ---- keyboard flying (orbit view) ------------------------------------------------------------
    // W A S D (or the arrows) slide the view across the area, Q / E lower and raise it, Shift is
    // faster: getting around without switching to Walk. Speed follows how far out the camera is.
    const flyKeys = new Set();
    let flyLast = 0, flyRamp = 0;
    function flyStep(now) {
        const dt = flyLast ? Math.min(0.1, (now - flyLast) / 1000) : 0;
        flyLast = now;
        if (!dt) return true;
        const forward = controls.target.clone().sub(camera.position).setY(0);
        if (forward.lengthSq() < 1e-6) forward.set(0, 0, -1);
        forward.normalize();
        const right = new THREE.Vector3(-forward.z, 0, forward.x);
        const move = new THREE.Vector3();
        if (flyKeys.has("w") || flyKeys.has("arrowup")) move.add(forward);
        if (flyKeys.has("s") || flyKeys.has("arrowdown")) move.sub(forward);
        if (flyKeys.has("d") || flyKeys.has("arrowright")) move.add(right);
        if (flyKeys.has("a") || flyKeys.has("arrowleft")) move.sub(right);
        if (flyKeys.has("e")) move.y += 1;
        if (flyKeys.has("q")) move.y -= 1;
        if (move.lengthSq() === 0) return true;
        const distance = camera.position.distanceTo(controls.target);
        // Speeds up over a quarter of a second (like walking) instead of jumping to full speed.
        flyRamp = Math.min(1, flyRamp + dt * 4);
        const speed = Math.min(60, Math.max(2, distance * 0.5)) * (flyKeys.has("shift") ? 3 : 1) * dt * (0.3 + 0.7 * flyRamp);
        move.normalize().multiplyScalar(speed);
        camera.position.add(move);
        controls.target.add(move);
        return true;
    }
    function onFlyKeyDown(e) {
        if (walkOn || isTyping(e) || e.ctrlKey || e.metaKey || e.altKey) return;
        if (!hostHasFocus()) return;
        const k = e.key.toLowerCase();
        if (!["w", "a", "s", "d", "q", "e", "shift", "arrowup", "arrowdown", "arrowleft", "arrowright"].includes(k)) return;
        e.preventDefault();
        flyKeys.add(k);
        requestRender();
    }
    function onFlyKeyUp(e) { flyKeys.delete(e.key.toLowerCase()); if (flyKeys.size === 0) flyRamp = 0; }
    // Keys only steer the view after it was clicked (or hovered), so typing elsewhere never moves it.
    let pointerInside = false;
    function hostHasFocus() { return pointerInside || document.activeElement === renderer.domElement; }
    window.addEventListener("keydown", onFlyKeyDown);
    window.addEventListener("keyup", onFlyKeyUp);
    window.addEventListener("blur", () => flyKeys.clear());

    // ---- scene building ----------------------------------------------------------------------
    function disposeMeshes() {
        for (const m of meshes) {
            scene.remove(m);
            m.material.dispose();
            m.dispose();
        }
        meshes = [];
    }

    function buildMeshes() {
        disposeMeshes();
        const counts = CATEGORY_COLORS.map(() => 0);
        for (const o of objects) counts[o.cat] = (counts[o.cat] ?? 0) + 1;
        meshes = CATEGORY_COLORS.map((color, cat) => {
            const mesh = new THREE.InstancedMesh(boxGeometry,
                new THREE.MeshLambertMaterial({ color: 0xffffff }), Math.max(1, counts[cat]));
            mesh.count = 0;
            mesh.frustumCulled = false;
            mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
            scene.add(mesh);
            return mesh;
        });
    }

    function modelTint(o) {
        if (o.mark === 1) return tmpColor.setHex(MODEL_TINT_DELETED);
        if (o.mark === 2) return tmpColor.setHex(MODEL_TINT_COPY);
        return tmpColor.setHex(o.built ? 0xffffff : MODEL_TINT_LEVEL_PLACED);
    }

    function disposeModelMeshes() {
        for (const m of modelMeshes) {
            scene.remove(m);
            m.dispose(); // geometry and materials are shared through the caches
        }
        modelMeshes = [];
        objInstances = new Map();
        builtClasses.clear();
    }

    /** Model classes with instances in the scene (so arriving models add only what is new). */
    const builtClasses = new Set();

    /** One InstancedMesh per part of every class that has a ready model and a visible object. */
    // Shaders for materials seen for the first time are compiled in parallel (compileAsync) before
    // their meshes join the scene, instead of synchronously inside the next frame.
    const compiledMaterials = new WeakSet();
    const pendingCompile = new THREE.Group();
    const materialsOf = mesh => (Array.isArray(mesh.material) ? mesh.material : [mesh.material]);
    const isCompiled = mesh => materialsOf(mesh).every(m => compiledMaterials.has(m));

    /** Adds meshes to a parent now when their shaders are ready, or after compiling them in parallel. */
    function addWhenCompiled(meshes, parent, stillWanted) {
        const ready = meshes.filter(isCompiled), waiting = meshes.filter(m => !isCompiled(m));
        for (const m of ready) parent.add(m);
        if (!waiting.length) return;
        if (typeof renderer.compileAsync !== "function") {
            for (const m of waiting) { materialsOf(m).forEach(x => compiledMaterials.add(x)); parent.add(m); }
            return;
        }
        const group = new THREE.Group();
        for (const m of waiting) group.add(m);
        pendingCompile.add(group);
        renderer.compileAsync(group, camera, scene).catch(() => { }).then(() => {
            pendingCompile.remove(group);
            for (const m of [...group.children]) {
                materialsOf(m).forEach(x => compiledMaterials.add(x));
                if (!disposed && stillWanted(m)) parent.add(m);
                else { group.remove(m); m.dispose(); }
            }
            requestRender();
        });
    }

    /** The visible objects with a ready model, grouped by model class (only classes not built yet when onlyNew). */
    function visibleByClass(onlyNew) {
        const byClass = new Map();
        for (const idx of visible) {
            const o = objects[idx];
            if (!readyModel(o)) continue;
            const key = modelKey(o);
            if (onlyNew && builtClasses.has(key)) continue;
            if (!byClass.has(key)) byClass.set(key, []);
            byClass.get(key).push(idx);
        }
        return byClass;
    }

    function buildClassInstances(byClass) {
        const built = [];
        const base = new THREE.Matrix4();
        const m = new THREE.Matrix4();
        for (const [cls, list] of byClass) {
            builtClasses.add(cls);
            for (const part of classModels.get(cls).parts) {
                const mesh = new THREE.InstancedMesh(part.geometry, part.materials, list.length);
                mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
                mesh.userData.objIndexes = list;
                mesh.userData.part = part.matrix;
                list.forEach((objIndex, i) => {
                    const o = objects[objIndex];
                    mesh.setMatrixAt(i, m.multiplyMatrices(objectMatrix(base, o), part.matrix));
                    mesh.setColorAt(i, modelTint(o));
                    if (!objInstances.has(objIndex)) objInstances.set(objIndex, []);
                    objInstances.get(objIndex).push([mesh, i]);
                });
                mesh.instanceMatrix.needsUpdate = true;
                if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
                mesh.computeBoundingSphere();
                built.push(mesh);
                modelMeshes.push(mesh);
            }
        }
        addWhenCompiled(built, scene, mesh => modelMeshes.includes(mesh));
    }

    function rebuildModelInstances() {
        disposeModelMeshes();
        if (!modelsOn) return;
        buildClassInstances(visibleByClass(false));
    }

    /**
     * Models arrived: adds instances for the newly ready classes only. Rebuilding every class each
     * time a batch arrived threw away and recreated hundreds of meshes dozens of times per load.
     */
    function addArrivedModels() {
        if (!modelsOn) return;
        buildClassInstances(visibleByClass(true));
        rebuildBoxes();
    }

    function rebuildInstances() {
        rebuildModelInstances();
        rebuildBoxes();
        rebuildDots();
    }

    /** The coloured boxes, for every visible object without a ready model. */
    function rebuildBoxes() {
        const perCat = CATEGORY_COLORS.map(() => []);
        for (const idx of visible) {
            if (!(modelsOn && readyModel(objects[idx]))) perCat[objects[idx].cat].push(idx);
        }
        const m = new THREE.Matrix4();
        instanceToObject = perCat;
        perCat.forEach((list, cat) => {
            const mesh = meshes[cat];
            mesh.count = list.length;
            list.forEach((objIndex, i) => {
                const o = objects[objIndex];
                mesh.setMatrixAt(i, boxMatrix(m, o));
                mesh.setColorAt(i, baseColor(o));
            });
            mesh.instanceMatrix.needsUpdate = true;
            if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
            mesh.computeBoundingSphere();
        });
        updateSelectionBox();
        requestRender();
    }

    function rebuildDots() {
        if (dots) {
            scene.remove(dots);
            dots.geometry.dispose();
            dots.material.dispose();
            dots = null;
        }
        if (visible.length === 0) return;
        const positions = new Float32Array(visible.length * 3);
        const colors = new Float32Array(visible.length * 3);
        visible.forEach((idx, i) => {
            const o = objects[idx];
            positions.set(o.p, i * 3);
            baseColor(o);
            colors.set([tmpColor.r, tmpColor.g, tmpColor.b], i * 3);
        });
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
        geometry.setAttribute("color", new THREE.BufferAttribute(colors, 3));
        dots = new THREE.Points(geometry, new THREE.PointsMaterial({ size: 4, sizeAttenuation: false, vertexColors: true }));
        dots.frustumCulled = false;
        scene.add(dots);
    }

    function updateOneInstance(objIndex) {
        const o = objects[objIndex];
        const list = instanceToObject[o.cat];
        const i = list ? list.indexOf(objIndex) : -1;
        if (i >= 0) {
            const mesh = meshes[o.cat];
            mesh.setMatrixAt(i, boxMatrix(new THREE.Matrix4(), o));
            mesh.instanceMatrix.needsUpdate = true;
            mesh.computeBoundingSphere();
        }
        const parts = objInstances.get(objIndex);
        if (parts) {
            const base = objectMatrix(new THREE.Matrix4(), o);
            const m = new THREE.Matrix4();
            for (const [mesh, instance] of parts) {
                mesh.setMatrixAt(instance, m.multiplyMatrices(base, mesh.userData.part));
                mesh.instanceMatrix.needsUpdate = true;
                mesh.computeBoundingSphere();
            }
        }
        updateSelectionBox();
        requestRender();
    }

    function updateSelectionBox() {
        for (const child of [...selectionGroup.children]) selectionGroup.remove(child);
        for (const key of selectedKeys) {
            const idx = keyToIndex.get(key);
            if (idx === undefined) continue;
            const line = new THREE.LineSegments(edgesGeometry, key === selectedKey ? primaryMaterial : secondaryMaterial);
            line.renderOrder = 10;
            line.matrixAutoUpdate = false;
            boxMatrix(line.matrix, objects[idx]);
            line.matrixWorldNeedsUpdate = true;
            selectionGroup.add(line);
        }
    }

    function updateGrid() {
        if (grid) {
            scene.remove(grid);
            grid.geometry.dispose();
            grid.material.dispose();
            grid = null;
        }
        if (objects.length === 0) return;
        const box = new THREE.Box3();
        for (const o of objects) box.expandByPoint(tmpPos.set(o.p[0], o.p[1], o.p[2]));
        const extent = Math.max(box.max.x - box.min.x, box.max.z - box.min.z, 20);
        const size = Math.min(4000, Math.ceil(extent * 1.3 / 10) * 10);
        const divisions = Math.min(200, Math.max(10, Math.round(size / 10)));
        grid = new THREE.GridHelper(size, divisions, 0x3a4a5a, 0x1f2933);
        grid.position.set((box.min.x + box.max.x) / 2, box.min.y - 0.01, (box.min.z + box.max.z) / 2);
        grid.visible = levelGroup.children.length === 0;
        scene.add(grid);
        axes.position.set(box.min.x, box.min.y, box.min.z);
        axes.scale.setScalar(Math.max(1, Math.min(200, extent / 20)));
    }

    // ---- game models ---------------------------------------------------------------------------
    function report(kind, done, total, note) {
        if (disposed) return;
        dotnet.invokeMethodAsync("OnModelProgress", kind, done, total, note ?? null).catch(() => { });
    }

    /** Runs fetches a few at a time so hundreds of meshes do not flood the local host. */
    async function throttled(task) {
        if (fetchActive >= MODEL_FETCH_CONCURRENCY) await new Promise(resolve => fetchWaiting.push(resolve));
        fetchActive++;
        try {
            return await task();
        } finally {
            fetchActive--;
            fetchWaiting.shift()?.();
        }
    }

    function loadGeometry(id) {
        if (!geometryCache.has(id)) {
            geometryCache.set(id, throttled(async () => {
                const response = await fetch(assetUrl(id));
                return response.ok ? parseMesh(await response.arrayBuffer()) : null;
            }).catch(() => null));
        }
        return geometryCache.get(id);
    }

    // Textures are decoded off the main thread (an ImageBitmap) instead of during the GPU upload,
    // which with plain images stalled the first frames of the view by half a second on a big base.
    const bitmapLoader = typeof createImageBitmap === "function"
        ? new THREE.ImageBitmapLoader().setOptions({ imageOrientation: "none", premultiplyAlpha: "none" })
        : null;

    // Texture progress, so the view can say what it is still waiting for.
    let texturesAsked = 0, texturesDone = 0, textureReportQueued = false;
    function textureSettled() {
        texturesDone++;
        if (textureReportQueued) return;
        textureReportQueued = true;
        setTimeout(() => { textureReportQueued = false; report("textures", texturesDone, texturesAsked); }, 250);
    }

    function textureFor(id) {
        let texture = textureCache.get(id);
        if (!texture) {
            texturesAsked++;
            let settle;
            const ready = new Promise(resolve => { settle = resolve; });
            const done = () => { textureSettled(); settle(); requestRender(); };
            if (bitmapLoader) {
                texture = new THREE.Texture();
                bitmapLoader.load(assetUrl(id), bitmap => {
                    texture.image = bitmap;
                    texture.needsUpdate = true;
                    done();
                }, undefined, done);
            } else {
                texture = new THREE.TextureLoader().load(assetUrl(id), done, undefined, done);
            }
            texture.userData.ready = ready;
            texture.colorSpace = THREE.SRGBColorSpace;
            texture.flipY = false; // Unreal's texture coordinates start at the top-left, like glTF
            texture.wrapS = texture.wrapT = THREE.RepeatWrapping;
            texture.anisotropy = Math.min(4, renderer.capabilities.getMaxAnisotropy());
            textureCache.set(id, texture);
        }
        return texture;
    }

    /**
     * A blended terrain material: up to five layer textures mixed by the mesh's vertex colours
     * (red, green, blue, alpha = layers 2 to 5; layer 1 takes what is left), each tinted and tiled
     * by its own repeat size. Built on the ordinary lit material so lighting and the ceiling cut
     * work as for every other level piece.
     */
    function terrainMaterialFor(m) {
        const key = `T|${JSON.stringify(m.layers)}`;
        let material = materialCache.get(key);
        if (material) return material;
        const layers = [0, 1, 2, 3, 4].map(i => m.layers[i] ?? null);
        const textures = layers.map(l => (l && l.texture ? textureFor(l.texture) : null));
        const fallback = textures.find(Boolean) ?? null;
        material = new THREE.MeshLambertMaterial({ color: 0xffffff, map: fallback, vertexColors: true, clippingPlanes: [levelClip] });
        material.userData.textures = textures.filter(Boolean);
        material.onBeforeCompile = shader => {
            for (let i = 0; i < 5; i++) {
                shader.uniforms[`terrainMap${i}`] = { value: textures[i] ?? fallback };
                shader.uniforms[`terrainTint${i}`] = { value: new THREE.Color().setRGB(...(layers[i]?.color ?? [1, 1, 1])) };
                shader.uniforms[`terrainRepeat${i}`] = { value: Math.max(0.1, layers[i]?.repeatMetres ?? 3) };
                shader.uniforms[`terrainUsed${i}`] = { value: textures[i] ? 1 : 0 };
            }
            const declarations = [0, 1, 2, 3, 4].map(i =>
                `uniform sampler2D terrainMap${i}; uniform vec3 terrainTint${i}; uniform float terrainRepeat${i}; uniform float terrainUsed${i};`).join("\n");
            shader.fragmentShader = shader.fragmentShader
                .replace("#include <common>", `#include <common>\n${declarations}`)
                .replace("#include <map_fragment>", `
                    // Layer weights: an unused slot gives its share back to the base layer.
                    vec4 terrainW = vColor * vec4(terrainUsed1, terrainUsed2, terrainUsed3, terrainUsed4);
                    float terrainBase = clamp(1.0 - terrainW.r - terrainW.g - terrainW.b - terrainW.a, 0.0, 1.0);
                    vec3 terrainColor = texture2D(terrainMap0, vMapUv / terrainRepeat0).rgb * terrainTint0 * terrainBase
                        + texture2D(terrainMap1, vMapUv / terrainRepeat1).rgb * terrainTint1 * terrainW.r
                        + texture2D(terrainMap2, vMapUv / terrainRepeat2).rgb * terrainTint2 * terrainW.g
                        + texture2D(terrainMap3, vMapUv / terrainRepeat3).rgb * terrainTint3 * terrainW.b
                        + texture2D(terrainMap4, vMapUv / terrainRepeat4).rgb * terrainTint4 * terrainW.a;
                    diffuseColor.rgb *= terrainColor;`)
                .replace("#include <color_fragment>", "");
        };
        material.customProgramCacheKey = () => "abiotic-terrain";
        materialCache.set(key, material);
        return material;
    }

    /**
     * A level material whose texture is laid over the world from above instead of by the mesh's
     * own texture coordinates: for pieces that would otherwise stretch one texture repeat over
     * many metres (a water plane scaled to cover a reservoir). The game maps those surfaces by
     * world position too; the material's own tiling is used when it has one.
     */
    function worldProjectedMaterialFor(m, tileMetres) {
        const key = `W|${tileMetres}|${m.texture}|${m.color}|${m.opacity}|${m.twoSided}`;
        let material = materialCache.get(key);
        if (material) return material;
        const base = materialFor(m, true);
        material = base.clone();
        // Cloning copies the cut plane, frozen at whatever height the cut had at that moment (often
        // a previous view's floor, far below): a floor made this way vanished for good. It shares
        // the live plane instead, so it follows the cut like every other level piece.
        material.clippingPlanes = base.clippingPlanes;
        material.onBeforeCompile = shader => {
            shader.uniforms.abioticTile = { value: tileMetres };
            shader.vertexShader = shader.vertexShader
                .replace("#include <common>", "#include <common>\nvarying vec3 vAbioticWorld;")
                .replace("#include <project_vertex>", `#include <project_vertex>
                    vec4 abioticWorld = vec4(transformed, 1.0);
                    #ifdef USE_INSTANCING
                    abioticWorld = instanceMatrix * abioticWorld;
                    #endif
                    vAbioticWorld = (modelMatrix * abioticWorld).xyz;`);
            shader.fragmentShader = shader.fragmentShader
                .replace("#include <common>", "#include <common>\nvarying vec3 vAbioticWorld;\nuniform float abioticTile;")
                .replace("#include <map_fragment>", `
                    #ifdef USE_MAP
                    diffuseColor *= texture2D(map, vAbioticWorld.xz / abioticTile);
                    #endif`);
        };
        material.customProgramCacheKey = () => "abiotic-world-projected";
        materialCache.set(key, material);
        return material;
    }

    /**
     * Metres one texture repeat covers on the largest instance of a level batch, from the mesh's
     * size against its texture coordinate range; large values mean a stretched texture.
     */
    function metresPerTextureRepeat(geometry, matrices) {
        const uv = geometry.getAttribute("uv");
        if (!uv || !geometry.boundingBox) return 0;
        let minU = Infinity, maxU = -Infinity, minV = Infinity, maxV = -Infinity;
        for (let i = 0; i < uv.count; i++) {
            const u = uv.getX(i), v = uv.getY(i);
            if (u < minU) minU = u; if (u > maxU) maxU = u;
            if (v < minV) minV = v; if (v > maxV) maxV = v;
        }
        const span = Math.max(maxU - minU, maxV - minV);
        if (!(span > 0)) return 0;
        const size = new THREE.Vector3();
        geometry.boundingBox.getSize(size);
        let scale = 0;
        for (let i = 0; i < matrices.length; i += 16) {
            const sx = Math.hypot(matrices[i], matrices[i + 1], matrices[i + 2]);
            const sz = Math.hypot(matrices[i + 8], matrices[i + 9], matrices[i + 10]);
            scale = Math.max(scale, sx, sz);
        }
        return (Math.max(size.x, size.z) / span) * scale;
    }

    /**
     * Waits (up to a limit) for every texture the materials use, so a model or level piece appears
     * finished instead of flat white first and textured a moment later. A texture that never comes
     * does not hold the piece back for longer than the limit.
     */
    const TEXTURE_WAIT_MS = 6000;
    function texturesReady(materials, patient = false) {
        const waits = [];
        for (const material of materials) {
            for (const t of [material.map, ...(material.userData.textures ?? [])]) {
                if (t && t.userData.ready) waits.push(t.userData.ready);
            }
        }
        if (!waits.length) return Promise.resolve();
        // A picture (tools/thumbnails) waits for every texture: full-size ones can take longer than
        // the view's limit to read, and a picture taken early would show them missing.
        if (patient) return Promise.all(waits);
        return Promise.race([Promise.all(waits), new Promise(resolve => setTimeout(resolve, TEXTURE_WAIT_MS))]);
    }

    // Pictures (tools/thumbnails) are taken with the finest detail the game has: its most detailed
    // meshes and its textures at full size, sharply filtered, instead of the lighter ones the live
    // view uses to stay quick.
    const PICTURE_TEXTURE_SIZE = 2048;
    const pictureMesh = id => id.replace(/^mesh\/\d+\//, "mesh/0/");
    const pictureTexture = id => (id ? id.replace(/^tex\/\d+\//, `tex/${PICTURE_TEXTURE_SIZE}/`) : id);
    function pictureMaterialFor(m) {
        const material = materialFor({ ...m, texture: pictureTexture(m.texture) }, false);
        const sharp = renderer.capabilities.getMaxAnisotropy();
        if (material.map && material.map.anisotropy !== sharp) {
            material.map.anisotropy = sharp;
            if (material.map.image) material.map.needsUpdate = true;
        }
        return material;
    }

    function materialFor(m, level) {
        if (level && m.layers && m.layers.length) return terrainMaterialFor(m);
        const key = `${level ? "L" : "O"}|${m.texture}|${m.color}|${m.opacity}|${m.twoSided}|${m.masked}|${m.emissive}|${m.decal ? 1 : 0}`;
        let material = materialCache.get(key);
        if (!material) {
            const params = {
                color: new THREE.Color().setRGB(m.color[0], m.color[1], m.color[2]),
                side: m.twoSided ? THREE.DoubleSide : THREE.FrontSide,
            };
            if (m.texture) params.map = textureFor(m.texture);
            if (m.opacity < 1) Object.assign(params, { transparent: true, opacity: m.opacity, depthWrite: false });
            if (m.masked) params.alphaTest = 0.5;
            if (m.decal) {
                // Decals lie on a surface: see-through by their texture's alpha, pulled slightly
                // towards the camera so they win against the wall or floor they sit on.
                Object.assign(params, { transparent: true, depthWrite: false, alphaTest: 0.03, side: THREE.DoubleSide,
                    polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -4 });
            }
            if (level) params.clippingPlanes = [levelClip];
            material = m.emissive ? new THREE.MeshBasicMaterial(params) : new THREE.MeshLambertMaterial(params);
            materialCache.set(key, material);
        }
        return material;
    }

    function scheduleModelRebuild() {
        if (modelRebuildQueued || disposed) return;
        modelRebuildQueued = true;
        setTimeout(() => {
            modelRebuildQueued = false;
            if (!disposed) addArrivedModels();
        }, 120);
    }

    async function buildClassModel(cls, description) {
        const parts = await Promise.all(description.parts.map(async part => {
            const geometry = await loadGeometry(part.mesh);
            if (!geometry) return null;
            return {
                geometry,
                materials: part.materials.length ? part.materials.map(m => materialFor(m, false)) : [materialFor({ color: [0.6, 0.6, 0.6], opacity: 1 }, false)],
                matrix: new THREE.Matrix4().fromArray(part.matrix),
            };
        }));
        const ready = parts.filter(Boolean);
        if (ready.length === 0) {
            classModels.set(cls, { state: "none" });
            return;
        }
        await texturesReady(ready.flatMap(p => p.materials));
        const box = new THREE.Box3(new THREE.Vector3(...description.boundsMin), new THREE.Vector3(...description.boundsMax));
        classModels.set(cls, { state: "ready", parts: ready, box });
    }

    /** Asks for every class in the scene not asked about yet, and swaps boxes for models as they arrive. */
    async function loadClassModels() {
        const wanted = [...new Set(objects.map(modelKey).filter(c => c && !classModels.has(c)))];
        if (!modelsOn || wanted.length === 0) return;
        for (const cls of wanted) classModels.set(cls, { state: "pending" });
        let done = 0;
        report("models", 0, wanted.length);
        // Batches are asked for together (the host works each one out in parallel).
        const chunks = [];
        for (let i = 0; i < wanted.length; i += CLASS_BATCH) chunks.push(wanted.slice(i, i + CLASS_BATCH));
        const queue = [...chunks];
        const runChunk = async chunk => {
            let answer;
            try {
                answer = await postJson(`${MODEL_BASE}/classes`, chunk) ?? {};
            } catch {
                for (const cls of chunk) classModels.set(cls, { state: "none" });
                done += chunk.length;
                report("models", done, wanted.length, "error");
                return;
            }
            await Promise.all(chunk.map(async cls => {
                const description = answer[cls];
                if (description && description.parts?.length) await buildClassModel(cls, description);
                else classModels.set(cls, { state: "none" });
                done++;
                if (done % 10 === 0 || done === wanted.length) report("models", done, wanted.length);
                scheduleModelRebuild();
            }));
        };
        // A few batches at a time, each started as soon as one finishes.
        await Promise.all(Array.from({ length: Math.min(CLASS_REQUESTS, queue.length) }, async () => {
            while (queue.length && !disposed) await runChunk(queue.shift());
        }));
        report("models", wanted.length, wanted.length);
    }

    /** Renders a group on its own into a transparent square picture, framed from a three-quarter front view. */
    function shootSquare(group, box, size, front, quality = 0.88) {
        const stage = new THREE.Scene();
        stage.add(new THREE.HemisphereLight(0xffffff, 0x445566, 1.7));
        const key = new THREE.DirectionalLight(0xffffff, 1.6);
        key.position.set(0.6, 1, 0.8);
        stage.add(key);
        stage.add(group);
        const centre = box.getCenter(new THREE.Vector3());
        const extent = box.getSize(new THREE.Vector3());
        const fitRadius = Math.max(extent.length() / 2, 0.2);
        const shot = new THREE.PerspectiveCamera(30, 1, 0.01, 1000);
        const dir = viewDirection(front, 0.45);
        shot.position.copy(centre).addScaledVector(dir, fitRadius / Math.sin((shot.fov * Math.PI) / 360) * 1.05);
        shot.lookAt(centre);
        shot.updateProjectionMatrix();

        // Drawn at twice the size and scaled down, so edges and fine texture detail come out smooth.
        const big = Math.min(size * 2, renderer.capabilities.maxTextureSize);
        const target = new THREE.WebGLRenderTarget(big, big, { samples: 4 });
        target.texture.colorSpace = THREE.SRGBColorSpace;
        const oldTarget = renderer.getRenderTarget();
        const oldClear = renderer.getClearColor(new THREE.Color());
        const oldAlpha = renderer.getClearAlpha();
        renderer.setRenderTarget(target);
        renderer.setClearColor(0x000000, 0);
        renderer.clear();
        renderer.render(stage, shot);
        const pixels = new Uint8Array(big * big * 4);
        renderer.readRenderTargetPixels(target, 0, 0, big, big, pixels);
        renderer.setRenderTarget(oldTarget);
        renderer.setClearColor(oldClear, oldAlpha);
        target.dispose();
        for (const mesh of group.children) mesh.dispose?.();

        const full = document.createElement("canvas");
        full.width = full.height = big;
        const fullContext = full.getContext("2d");
        const image = fullContext.createImageData(big, big);
        for (let y = 0; y < big; y++) image.data.set(pixels.subarray((big - 1 - y) * big * 4, (big - y) * big * 4), y * big * 4);
        fullContext.putImageData(image, 0, 0);
        const canvas = document.createElement("canvas");
        canvas.width = canvas.height = size;
        const g = canvas.getContext("2d");
        g.imageSmoothingEnabled = true;
        g.imageSmoothingQuality = "high";
        g.drawImage(full, 0, 0, size, size);
        requestRender();
        const pieces = group.children.length;
        return { image: canvas.toDataURL("image/webp", quality), pieces };
    }

    // ---- level geometry -------------------------------------------------------------------------
    function clearLevel() {
        for (const child of [...levelGroup.children]) {
            levelGroup.remove(child);
            child.dispose();
        }
        clearMergedLevel();
        levelInstances = 0;
        clearLamps();
        ambient.intensity = 1.6;
    }

    // ---- merged level ---------------------------------------------------------------------------
    let mergeToken = 0;
    function clearMergedLevel() {
        mergeToken++;
        for (const child of [...mergedGroup.children]) {
            mergedGroup.remove(child);
            child.dispose();
        }
        for (const mesh of levelGroup.children) mesh.visible = true;
    }

    /** True for a material three.js can batch as it is (no custom shader code, opaque). */
    function batchable(material) {
        return !!material && !material.transparent
            && material.onBeforeCompile === THREE.Material.prototype.onBeforeCompile;
    }

    const sectionCache = new WeakMap(); // geometry -> Map(start -> section geometry)
    /** One section of a multi-material mesh as its own geometry, with only the vertices it uses. */
    function sectionGeometry(geometry, start, count) {
        if (start === 0 && count >= geometry.index.count) return geometry;
        let byStart = sectionCache.get(geometry);
        if (!byStart) sectionCache.set(geometry, byStart = new Map());
        const known = byStart.get(start);
        if (known) return known;
        const index = geometry.index.array;
        const remap = new Int32Array(geometry.attributes.position.count).fill(-1);
        const order = [];
        const newIndex = new Uint32Array(count);
        for (let i = 0; i < count; i++) {
            const v = index[start + i];
            if (remap[v] < 0) { remap[v] = order.length; order.push(v); }
            newIndex[i] = remap[v];
        }
        const part = new THREE.BufferGeometry();
        for (const name of ["position", "normal", "uv"]) {
            const source = geometry.attributes[name];
            if (!source) continue;
            const size = source.itemSize;
            const out = new source.array.constructor(order.length * size);
            for (let i = 0; i < order.length; i++) for (let c = 0; c < size; c++) out[i * size + c] = source.array[order[i] * size + c];
            part.setAttribute(name, new THREE.BufferAttribute(out, size, source.normalized));
        }
        part.setIndex(new THREE.BufferAttribute(newIndex, 1));
        byStart.set(start, part);
        return part;
    }

    /**
     * Merges the loaded level into batched meshes, a few milliseconds at a time between frames so it
     * never stalls the view, then swaps them in once their shaders are ready.
     */
    async function mergeLevel(levelLoad) {
        if (typeof THREE.BatchedMesh !== "function") return;
        clearMergedLevel();
        const token = ++mergeToken;
        const plans = new Map(); // material -> { parts, instances }
        const merged = [];
        const matrix = new THREE.Matrix4();
        for (const mesh of levelGroup.children) {
            if (!mesh.isInstancedMesh || mesh.geometry.attributes.color || !mesh.geometry.index) continue;
            const materials = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
            const g = mesh.geometry;
            const sections = g.groups.length ? g.groups : [{ start: 0, count: g.index.count, materialIndex: 0 }];
            if (!sections.every(sec => batchable(materials[sec.materialIndex] ?? materials[0]))) continue;
            const matrices = [];
            for (let i = 0; i < mesh.count; i++) { mesh.getMatrixAt(i, matrix); matrices.push(matrix.clone()); }
            for (const sec of sections) {
                const material = materials[sec.materialIndex] ?? materials[0];
                let plan = plans.get(material);
                if (!plan) plans.set(material, plan = { parts: [], instances: 0 });
                plan.parts.push({ source: g, start: sec.start, count: sec.count, matrices });
                plan.instances += matrices.length;
            }
            merged.push(mesh);
        }
        if (merged.length < 8) return; // a handful of pieces: nothing to gain
        const batches = [];
        const abandon = () => { for (const b of batches) b.dispose(); };
        let budget = performance.now() + 8;
        for (const [material, plan] of plans) {
            const geometries = plan.parts.map(part => sectionGeometry(part.source, part.start, part.count));
            const unique = [...new Set(geometries)];
            const vertices = unique.reduce((n, geo) => n + geo.attributes.position.count, 0);
            const indices = unique.reduce((n, geo) => n + geo.index.count, 0);
            const batched = new THREE.BatchedMesh(plan.instances, vertices, indices, material);
            batched.sortObjects = false;
            batches.push(batched);
            const ids = new Map();
            for (let i = 0; i < plan.parts.length; i++) {
                const geo = geometries[i];
                let id = ids.get(geo);
                if (id === undefined) { id = batched.addGeometry(geo); ids.set(geo, id); }
                for (const m of plan.parts[i].matrices) batched.setMatrixAt(batched.addInstance(id), m);
                if (performance.now() > budget) {
                    await new Promise(resolve => requestAnimationFrame(resolve));
                    if (token !== mergeToken || levelLoad !== levelToken || disposed) { abandon(); return; }
                    budget = performance.now() + 8;
                }
            }
        }
        const staging = new THREE.Group();
        for (const b of batches) staging.add(b);
        if (typeof renderer.compileAsync === "function") {
            pendingCompile.add(staging);
            try { await renderer.compileAsync(staging, camera, scene); } catch { /* drawn when ready */ }
            pendingCompile.remove(staging);
        }
        if (token !== mergeToken || levelLoad !== levelToken || disposed) { abandon(); return; }
        for (const b of batches) mergedGroup.add(b);
        for (const mesh of merged) mesh.visible = false;
        mergedStats = { calls: batches.length, pieces: merged.length };
        requestRender();
    }
    let mergedStats = { calls: 0, pieces: 0 };

    let levelFloorY = null; // the level's own floor under the view centre (null until the level is in)

    let levelCeilingY = null; // the level's ceiling over the view centre (null until loaded, or none)

    /** The lowest level surface straight above a point, up to 6 m (ignoring the cut), or null. */
    function levelCeilingAbove(at, floorY) {
        // From a little above the floor, so a table or shelf the point stands on is not taken for the ceiling.
        const from = new THREE.Vector3(at.x, Math.max(at.y, (floorY ?? at.y) + 1.2), at.z);
        raycaster.set(from, new THREE.Vector3(0, 1, 0));
        raycaster.far = 6;
        const hits = raycaster.intersectObjects(nearSolids(at, 3), false);
        raycaster.far = Infinity;
        return hits.length ? hits[0].point.y : null;
    }

    /** The highest level surface straight below a point (ignoring the cut), or null. */
    function levelFloorUnder(at) {
        raycaster.set(new THREE.Vector3(at.x, at.y + 0.3, at.z), new THREE.Vector3(0, -1, 0));
        raycaster.far = 30;
        const hits = raycaster.intersectObjects(nearSolids(at, 3), false);
        raycaster.far = Infinity;
        return hits.length ? hits[0].point.y : null;
    }

    /** Height (viewer Y) above which the level is cut away, so ceilings and upper floors do not hide the base. */
    function updateLevelCut() {
        if (levelOptions.cutAbove === null || levelOptions.cutAbove === undefined || levelOptions.cutAbove <= 0) {
            levelClip.constant = 1e6;
            stairClip.constant = 1e6;
        } else {
            const c = levelCentre ?? controls.target;
            // The floor the level itself has under the view centre, once it has loaded; until then
            // (and where it has none) the nearby objects' heights. Objects alone could put the cut
            // a storey too high: a shelf or wall lamp near the centre counted as the floor, and the
            // floor above stayed in, hiding the room (a fridge shown from "Show in 3D").
            const objects = baseFloor(c);
            const floor = levelFloorY === null ? objects : (objects < levelFloorY && levelFloorY - objects < 1.5 ? objects : levelFloorY);
            // A ceiling lower than the cut (many rooms are under 3 m) would stay and hide the room:
            // the cut then sits just under the ceiling found above the view centre.
            const cut = floor + levelOptions.cutAbove;
            levelClip.constant = levelCeilingY !== null && levelCeilingY < cut && levelCeilingY > floor + 1.2 ? levelCeilingY - 0.05 : cut;
            stairClip.constant = Math.max(levelClip.constant, floor + levelOptions.cutAbove) + STAIR_EXTRA_M;
        }
        // A lamp's glow above the cut would float where its (cut away) fixture was; its light still falls below.
        for (const child of lampGroup.children) if (child.isSprite) child.visible = lampsOn && child.position.y <= levelClip.constant;
        requestRender();
    }

    /**
     * The height of the base floor being looked at: player-built objects (level-placed lamps and
     * shelves hang higher) within a base's reach of the view centre, the one whose height is
     * nearest the centre. Object origins sit at their feet, so this is the floor they stand on, and
     * a multi-storey base is cut above whichever floor the view is on.
     */
    function baseFloor(c) {
        let floor = null;
        let any = false;
        for (const i of visible) {
            const o = objects[i];
            if (Math.hypot(o.p[0] - c.x, o.p[2] - c.z) > BASE_REACH_M) continue;
            if (o.built && !any) { any = true; floor = null; }
            if (any && !o.built) continue;
            if (floor === null || Math.abs(o.p[1] - c.y) < Math.abs(floor - c.y)) floor = o.p[1];
        }
        return floor ?? c.y;
    }

    async function loadLevel() {
        clearTimeout(levelRetry);
        const token = ++levelToken;
        if (!levelOptions.enabled || !levelOptions.region) {
            clearLevel();
            if (grid) grid.visible = true;
            requestRender();
            return;
        }
        const r = Math.max(5, levelOptions.radius || 40);
        const c = controls.target.clone();
        levelCentre = c;
        levelFloorY = null;
        levelCeilingY = null;
        let slice;
        report("level", 0, 0, "query");
        try {
            slice = await postJson(`${MODEL_BASE}/level`, {
                region: levelOptions.region,
                min: [c.x - r, c.y - r / 2, c.z - r],
                max: [c.x + r, c.y + r / 2, c.z + r],
                maxInstances: LEVEL_MAX_INSTANCES,
                excludeActors: levelOptions.excludeActors ?? [],
                openDoors: levelOptions.openDoors ?? [],
            });
        } catch {
            if (token === levelToken) report("level", 0, 0, "error");
            return;
        }
        if (token !== levelToken || disposed) return;
        if (!slice) {
            clearLevel();
            report("level", 0, 0, "none");
            return;
        }
        const batches = slice.batches ?? [];
        // Pieces are shown in batches as they arrive (the old level stays until the first batch is
        // ready), instead of all at once after the last one: on a first look the whole slice could
        // take ten seconds or more, with nothing to see meanwhile.
        let loaded = 0, cleared = false, pending = [];
        const flush = async () => {
            if (!pending.length) return;
            const chunk = pending;
            pending = [];
            await texturesReady(chunk.flatMap(mesh => (Array.isArray(mesh.material) ? mesh.material : [mesh.material])));
            if (token !== levelToken || disposed) { for (const mesh of chunk) mesh.dispose(); return; }
            if (!cleared) { clearLevel(); cleared = true; }
            for (const mesh of chunk) levelInstances += mesh.count;
            addWhenCompiled(chunk, levelGroup, () => token === levelToken);
            if (grid) grid.visible = false;
            updateLevelCut();
        };
        let flushing = Promise.resolve();
        await Promise.all(batches.map(async batch => {
            const geometry = await loadGeometry(batch.mesh);
            loaded++;
            if (loaded % 25 === 0) report("level", loaded, batches.length, "meshes");
            if (!geometry || token !== levelToken) return;
            const count = batch.matrices.length / 16;
            const stretched = metresPerTextureRepeat(geometry, batch.matrices) > STRETCHED_REPEAT_M;
            const materials = batch.materials.length
                ? batch.materials.map(m => stretched && m.texture && !m.decal && !(m.layers && m.layers.length)
                    ? worldProjectedMaterialFor(m, m.worldTileMetres > 0 ? m.worldTileMetres : DEFAULT_WORLD_TILE_M)
                    : materialFor(m, true))
                : [materialFor({ color: [0.6, 0.6, 0.6], opacity: 1 }, true)];
            const isStair = /stair/i.test(batch.name ?? "");
            const mesh = new THREE.InstancedMesh(geometry, isStair ? materials.map(stairMaterial) : materials, count);
            const m = new THREE.Matrix4();
            for (let i = 0; i < count; i++) mesh.setMatrixAt(i, m.fromArray(batch.matrices, i * 16));
            mesh.instanceMatrix.needsUpdate = true;
            mesh.computeBoundingSphere();
            mesh.name = batch.name ?? "";
            // Which level actor each instance belongs to, so a click on a piece (a wall plug) can
            // open the thing it is part of.
            if (batch.actors && slice.actors) { mesh.userData.actors = batch.actors; mesh.userData.actorNames = slice.actors; }
            pending.push(mesh);
            if (pending.length >= 24) flushing = flushing.then(flush);
        }));
        flushing = flushing.then(flush);
        await flushing;
        if (token !== levelToken || disposed) return;
        if (!cleared) { clearLevel(); cleared = true; if (grid) grid.visible = true; }
        setLamps(slice.lights);
        if (grid) grid.visible = levelGroup.children.length === 0;
        levelFloorY = levelGroup.children.length ? levelFloorUnder(levelCentre) : null;
        levelCeilingY = levelGroup.children.length ? levelCeilingAbove(levelCentre, levelFloorY) : null;
        updateLevelCut();
        unblockView();
        mergeLevel(token);
        report("level", slice.totalInBox ?? levelInstances, slice.pendingMaps ?? 0, slice.note ?? null);
        // Level files still being read in the background: ask again shortly.
        if ((slice.pendingMaps ?? 0) > 0) levelRetry = setTimeout(() => { if (token === levelToken) loadLevel(); }, LEVEL_RETRY_MS);
    }

    // ---- framing -----------------------------------------------------------------------------
    // Frames the densest part of a set: with a whole region's worth of objects a few outliers
    // (islands kilometres away) would otherwise shrink the actual base to a speck. Centres on the
    // per-axis median and keeps the 85% of objects nearest to it.
    function robustSubset(list) {
        if (list.length < 12) return list;
        const med = axis => {
            const v = list.map(i => objects[i].p[axis]).sort((a, b) => a - b);
            return v[Math.floor(v.length / 2)];
        };
        const c = [med(0), med(1), med(2)];
        const dist = i => {
            const p = objects[i].p;
            return Math.hypot(p[0] - c[0], p[1] - c[1], p[2] - c[2]);
        };
        return [...list].sort((a, b) => dist(a) - dist(b)).slice(0, Math.ceil(list.length * 0.85));
    }

    function frameIndices(indices, robust) {
        let list = indices.filter(i => objects[i]);
        if (robust) list = robustSubset(list);
        if (list.length === 0) return;
        const box = new THREE.Box3();
        // Each object's own extent (its model's bounds, or its marker box), so one small piece fills
        // the view instead of sitting in the middle of a 10 m wide frame.
        const corner = new THREE.Matrix4();
        for (const i of list) box.union(new THREE.Box3(new THREE.Vector3(-0.5, -0.5, -0.5), new THREE.Vector3(0.5, 0.5, 0.5)).applyMatrix4(boxMatrix(corner, objects[i])));
        const center = box.getCenter(new THREE.Vector3());
        const radius = Math.max(box.getSize(new THREE.Vector3()).length() / 2, list.length === 1 ? 0.6 : 2.5);
        const dir = camera.position.clone().sub(controls.target);
        if (dir.lengthSq() < 1e-6) dir.set(1, 0.8, 1);
        dir.normalize();
        if (dir.y < 0.25) dir.y = 0.25;
        dir.normalize();
        const dist = radius / Math.sin((camera.fov * Math.PI) / 360) * 1.15;
        controls.target.copy(center);
        camera.position.copy(center).addScaledVector(dir, dist);
        controls.update();
        // A few things picked: make sure no wall stands between the camera and them (again once
        // the level around them has loaded).
        if (list.length <= 6) { aimedAt = { at: center.clone(), until: performance.now() + 20000 }; unblockView(); }
        requestRender();
    }

    // ---- a clear view ------------------------------------------------------------------------
    // After a jump to something, the camera could end up behind a wall or inside a pillar, which
    // filled the view with one grey surface. The line from the camera to the target is tested
    // against the level (below the cut); when blocked, other angles around the target are tried
    // (looking down first, as the ceiling is cut away), and failing those the camera moves in
    // front of whatever blocks it.
    let aimedAt = null;
    const sightRay = new THREE.Raycaster();
    function blockedAt(from, to) {
        const dir = to.clone().sub(from);
        const length = dir.length();
        if (length < 0.05) return null;
        sightRay.set(from, dir.normalize());
        sightRay.far = Math.max(0, length - 0.3);
        for (const hit of sightRay.intersectObjects(nearSolids(to, length + 1), false)) {
            if (levelClip.distanceToPoint(hit.point) < 0) continue; // cut away: not drawn
            return hit.distance;
        }
        return null;
    }
    function unblockView() {
        if (!aimedAt || performance.now() > aimedAt.until || walkOn || levelGroup.children.length === 0) return;
        const target = aimedAt.at;
        if (controls.target.distanceTo(target) > 0.05) { aimedAt = null; return; } // moved on since
        const offset = camera.position.clone().sub(target);
        const dist = offset.length();
        if (blockedAt(camera.position, target) === null) return;
        const yaw0 = Math.atan2(offset.x, offset.z);
        for (const lift of [1.0, 0.75, 0.45, 0.2]) {
            for (let i = 0; i < 8; i++) {
                const yaw = yaw0 + (i % 2 ? -1 : 1) * Math.ceil(i / 2) * (Math.PI / 4);
                const flat = Math.cos(lift);
                const candidate = target.clone().add(new THREE.Vector3(Math.sin(yaw) * flat, Math.sin(lift), Math.cos(yaw) * flat).multiplyScalar(dist));
                if (blockedAt(candidate, target) === null) {
                    camera.position.copy(candidate);
                    controls.update();
                    requestRender();
                    return;
                }
            }
        }
        // Nothing clear at that distance: come in front of the nearest blocker on the original line.
        const along = blockedAt(camera.position, target);
        if (along !== null) {
            camera.position.copy(target).addScaledVector(offset.normalize(), Math.max(0.5, dist - along - 0.4));
            controls.update();
            requestRender();
        }
    }

    // ---- labels ------------------------------------------------------------------------------
    const labelPool = [];
    let labelOrder = null, labelOrderVisible = null;
    const labelOrderFrom = new THREE.Vector3();
    const labelVector = new THREE.Vector3();

    function renderLabels() {
        if (!labelsOn) {
            for (const el of labelPool) el.style.display = "none";
            return;
        }
        const w = host.clientWidth, h = host.clientHeight;
        const camPos = camera.position;
        // The nearest objects only change when the camera moves a fair way (or the shown set changes),
        // so they are sorted then, not on every frame of an orbit.
        if (!labelOrder || labelOrderVisible !== visible || labelOrderFrom.distanceToSquared(camPos) > 1) {
            const items = [];
            for (const idx of visible) {
                const o = objects[idx];
                const dx = o.p[0] - camPos.x, dy = o.p[1] - camPos.y, dz = o.p[2] - camPos.z;
                items.push([dx * dx + dy * dy + dz * dz, idx]);
            }
            items.sort((a, b) => a[0] - b[0]);
            labelOrder = items.slice(0, MAX_LABELS * 4).map(x => x[1]);
            labelOrderVisible = visible;
            labelOrderFrom.copy(camPos);
        }
        let used = 0;
        const v = labelVector;
        for (const idx of labelOrder) {
            if (used >= MAX_LABELS) break;
            const o = objects[idx];
            v.set(o.p[0], o.p[1] + 0.9, o.p[2]).project(camera);
            if (v.z > 1 || v.z < -1 || Math.abs(v.x) > 1.05 || Math.abs(v.y) > 1.05) continue;
            let el = labelPool[used];
            if (!el) {
                el = document.createElement("span");
                el.className = "b3d-label";
                labelLayer.appendChild(el);
                labelPool.push(el);
            }
            if (el.textContent !== o.label) el.textContent = o.label;
            el.style.display = "block";
            el.style.transform = `translate(${((v.x + 1) / 2) * w}px, ${((1 - v.y) / 2) * h}px) translate(-50%, -100%)`;
            el.classList.toggle("selected", selectedKeys.has(o.key));
            used++;
        }
        for (let i = used; i < labelPool.length; i++) labelPool[i].style.display = "none";
    }

    /**
     * Where a picture is taken from: in front of the thing (its facing, flattened), a little to its
     * side and raised by `lift`, so a door, button or lift is seen from the side it is used from.
     * Without a facing, a fixed three-quarter view.
     */
    function viewDirection(front, lift) {
        const f = front ? new THREE.Vector3(front[0], 0, front[2]) : new THREE.Vector3();
        if (f.lengthSq() < 1e-6) return new THREE.Vector3(0.55, lift, 0.8).normalize();
        f.normalize();
        const side = new THREE.Vector3(-f.z, 0, f.x);
        return f.addScaledVector(side, 0.4).setY(lift).normalize();
    }

    // ---- locating -----------------------------------------------------------------------------
    // "Show in 3D" from another tab: the camera looks down at the spot at an angle, and a pin marks
    // it, drawn over everything so it shows even inside walls or under a floor.
    const pinGroup = new THREE.Group();
    pinGroup.visible = false;
    pinGroup.renderOrder = 20;
    {
        const pinMaterial = new THREE.MeshBasicMaterial({ color: 0xf08418, depthTest: false, transparent: true, opacity: 0.95 });
        const stem = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 2.2, 8), pinMaterial);
        stem.position.y = 1.1 + 0.25;
        const head = new THREE.Mesh(new THREE.SphereGeometry(0.28, 16, 12), pinMaterial);
        head.position.y = 2.6;
        const tip = new THREE.Mesh(new THREE.ConeGeometry(0.16, 0.5, 12), pinMaterial);
        tip.rotation.x = Math.PI;
        tip.position.y = 0.25;
        for (const part of [stem, head, tip]) { part.renderOrder = 20; pinGroup.add(part); }
    }
    scene.add(pinGroup);

    function setPin(at) {
        pinGroup.visible = !!at;
        if (at) pinGroup.position.copy(at);
        requestRender();
    }

    function flyTo(target, distance) {
        if (walkOn) setWalk(false);
        const d = Math.max(2, distance || 12);
        const direction = camera.position.clone().sub(controls.target);
        direction.y = 0;
        if (direction.lengthSq() < 1e-6) direction.set(1, 0, 1);
        direction.normalize().multiplyScalar(Math.cos(0.6)).setY(Math.sin(0.6)).normalize();
        controls.target.copy(target);
        camera.position.copy(target).addScaledVector(direction, d);
        controls.update();
        aimedAt = { at: target.clone(), until: performance.now() + 20000 };
        if (levelOptions.enabled) loadLevel();
        unblockView();
        requestRender();
    }

    // ---- picking -----------------------------------------------------------------------------
    const raycaster = new THREE.Raycaster();
    const pointer = new THREE.Vector2();
    let downAt = null;

    function pick(clientX, clientY) {
        const rect = renderer.domElement.getBoundingClientRect();
        pointer.set(((clientX - rect.left) / rect.width) * 2 - 1, -(((clientY - rect.top) / rect.height) * 2 - 1));
        raycaster.setFromCamera(pointer, camera);
        let best = null;
        const hits = raycaster.intersectObjects([...meshes.filter(m => m.count > 0), ...modelMeshes], false);
        for (const hit of hits) {
            if (hit.instanceId === undefined) continue;
            const objIndex = hit.object.userData.objIndexes
                ? hit.object.userData.objIndexes[hit.instanceId]
                : instanceToObject[meshes.indexOf(hit.object)][hit.instanceId];
            best = objects[objIndex];
            break;
        }
        return best ?? nearestOnScreen(clientX, clientY);
    }

    // A whole region's pieces are a few pixels across. A click that hits none of them takes the
    // visible piece whose centre is closest to the pointer on screen, within a small radius.
    const PICK_SLOP_PX = 14;
    const pickProjected = new THREE.Vector3();
    function nearestOnScreen(clientX, clientY) {
        const rect = renderer.domElement.getBoundingClientRect();
        let best = null, bestDistance = PICK_SLOP_PX, bestDepth = Infinity;
        for (const idx of visible) {
            const o = objects[idx];
            pickProjected.set(o.p[0], o.p[1], o.p[2]).project(camera);
            if (pickProjected.z < -1 || pickProjected.z > 1) continue;
            const x = rect.left + (pickProjected.x + 1) / 2 * rect.width;
            const y = rect.top + (1 - pickProjected.y) / 2 * rect.height;
            const d = Math.hypot(x - clientX, y - clientY);
            if (d < bestDistance - 0.5 || (Math.abs(d - bestDistance) <= 0.5 && pickProjected.z < bestDepth)) {
                best = o; bestDistance = d; bestDepth = pickProjected.z;
            }
        }
        return best;
    }

    // ---- walk mode ---------------------------------------------------------------------------
    function walkForward() {
        return new THREE.Vector3(-Math.sin(walkYaw) * Math.cos(walkPitch), Math.sin(walkPitch), -Math.cos(walkYaw) * Math.cos(walkPitch));
    }

    function applyWalkCamera() {
        camera.rotation.set(walkPitch, walkYaw, 0, "YXZ");
        // The level loads around the controls' target, so keep it just ahead of the eye.
        controls.target.copy(camera.position).addScaledVector(walkForward(), 2);
        requestRender();
    }

    // ---- nearby solids -----------------------------------------------------------------------
    // A level slice holds thousands of instances. Rays from the walker (several per frame) and the
    // placement checks only ever reach a few metres, so they test a cached list of the level pieces
    // near the point instead: one plain mesh per nearby instance, sharing the instance's geometry
    // (with its bounding-volume tree) and material. Rebuilt when the point moves a few metres or a
    // new level slice arrives.
    const NEAR_RADIUS_M = 14;
    const WALK_NEAR_RADIUS_M = 6; // walking rays reach about 1.5 m; floor rays look straight down
    const NEAR_REFRESH_M = 3;
    const nearCaches = new Map(); // radius -> { at: Vector3, token, count, list: Mesh[] }
    const nearMatrix = new THREE.Matrix4();
    const nearSphere = new THREE.Sphere();

    function nearSolids(at, radius = NEAR_RADIUS_M) {
        const nearCache = nearCaches.get(radius);
        if (nearCache && nearCache.token === levelToken && nearCache.count === levelGroup.children.length
            && nearCache.at.distanceTo(at) < NEAR_REFRESH_M) return nearCache.list;
        const list = [];
        for (const mesh of levelGroup.children) {
            const g = mesh.geometry;
            if (!g.boundingSphere) g.computeBoundingSphere();
            for (let i = 0; i < mesh.count; i++) {
                mesh.getMatrixAt(i, nearMatrix);
                nearSphere.copy(g.boundingSphere).applyMatrix4(nearMatrix);
                if (nearSphere.center.distanceTo(at) - nearSphere.radius > radius) continue;
                if (!g.boundsTree) g.computeBoundsTree();
                const proxy = new THREE.Mesh(g, mesh.material);
                proxy.matrixAutoUpdate = false;
                proxy.matrix.copy(mesh.matrixWorld).multiply(nearMatrix);
                proxy.matrixWorld.copy(proxy.matrix);
                list.push(proxy);
            }
        }
        nearCaches.set(radius, { at: at.clone(), token: levelToken, count: levelGroup.children.length, list });
        return list;
    }

    /** The height of the floor under a viewer-space point (level, models and boxes), or null. */
    function floorBelow(at) {
        raycaster.set(new THREE.Vector3(at.x, at.y + 0.5, at.z), new THREE.Vector3(0, -1, 0));
        raycaster.far = 40;
        const targets = [...nearSolids(at, WALK_NEAR_RADIUS_M), ...modelMeshes, ...meshes.filter(m => m.count > 0)];
        const hits = raycaster.intersectObjects(targets, false);
        raycaster.far = Infinity;
        for (const hit of hits) {
            if (levelClip.distanceToPoint(hit.point) < 0) continue;
            return hit.point.y;
        }
        return null;
    }

    const WALK_RADIUS_M = 0.35;
    const walkProbe = new THREE.Raycaster();

    /** The nearest solid surface along a horizontal step from the walker (waist and chest height), or null. */
    function wallAhead(dir, dist) {
        const targets = [...nearSolids(camera.position, WALK_NEAR_RADIUS_M), ...modelMeshes];
        let best = null;
        for (const above of [0.8, 1.4]) {
            const from = new THREE.Vector3(camera.position.x, camera.position.y - EYE_M + above, camera.position.z);
            walkProbe.set(from, dir);
            walkProbe.far = dist + WALK_RADIUS_M;
            for (const hit of walkProbe.intersectObjects(targets, false)) {
                if (levelClip.distanceToPoint(hit.point) < 0 || !hit.face || !isSolid(hit)) continue;
                if (!best || hit.distance < best.distance) best = hit;
                break;
            }
        }
        return best;
    }

    // ---- placement checks --------------------------------------------------------------------
    // The game puts a piece exactly where the save says and never checks that it fits, so the view
    // warns: a piece cutting into the level, overlapping another piece, or with nothing to hold it up.
    const checkRay = new THREE.Raycaster();
    const SUPPORT_REACH_M = 0.35;

    /** An object's oriented box: centre, unit axes and half sizes (metres), from its model or category box. */
    function orientedBox(o) {
        const m = new THREE.Matrix4();
        boxMatrix(m, o);
        const centre = new THREE.Vector3().setFromMatrixPosition(m);
        const axes = [0, 1, 2].map(i => new THREE.Vector3().setFromMatrixColumn(m, i));
        const half = axes.map(a => a.length() / 2);
        axes.forEach(a => a.normalize());
        const aabb = new THREE.Box3();
        for (let i = 0; i < 8; i++) {
            const c = centre.clone()
                .addScaledVector(axes[0], (i & 1 ? 1 : -1) * half[0])
                .addScaledVector(axes[1], (i & 2 ? 1 : -1) * half[1])
                .addScaledVector(axes[2], (i & 4 ? 1 : -1) * half[2]);
            aabb.expandByPoint(c);
        }
        return { centre, axes, half, aabb };
    }

    /** The first solid surface along a segment, ignoring one object's own meshes and anything above the ceiling cut. */
    function solidHit(from, dir, length, targets, ownIndex) {
        checkRay.set(from, dir);
        checkRay.far = length;
        for (const hit of checkRay.intersectObjects(targets, false)) {
            if (levelClip.distanceToPoint(hit.point) < 0 || !hit.face || !isSolid(hit)) continue;
            const owner = hit.object.userData.objIndexes ? hit.object.userData.objIndexes[hit.instanceId] : undefined;
            if (owner !== undefined && owner === ownIndex) continue;
            return hit;
        }
        return null;
    }

    function placementReport(key) {
        const idx = keyToIndex.get(key);
        if (idx === undefined) return null;
        const o = objects[idx];
        const box = orientedBox(o);
        const levelLoaded = levelGroup.children.length > 0;
        const level = levelLoaded ? nearSolids(box.centre) : [];
        const [ax, ay, az] = box.axes;
        const [hx, hy, hz] = box.half;

        // Cutting into the level: segments across the footprint at two heights, inside the box only.
        let inside = false;
        if (levelLoaded) {
            const directions = [[ax, hx], [az, hz], [ax.clone().add(az).normalize(), Math.hypot(hx, hz)], [ax.clone().sub(az).normalize(), Math.hypot(hx, hz)]];
            outer: for (const f of [-0.35, 0.35]) {
                for (const [dir, extent] of directions) {
                    const reach = extent * 0.8;
                    if (reach < 0.05) continue;
                    const start = box.centre.clone().addScaledVector(ay, f * hy * 2 * 0.5).addScaledVector(dir, -reach);
                    if (solidHit(start, dir, reach * 2, level, idx)) { inside = true; break outer; }
                }
            }
        }

        // Overlapping other pieces: their boxes, shrunk so things merely touching (a lamp on a desk) do not count.
        const shrink = (b, k) => {
            const c = b.getCenter(new THREE.Vector3()), size = b.getSize(new THREE.Vector3()).multiplyScalar(k / 2);
            return new THREE.Box3(c.clone().sub(size), c.clone().add(size));
        };
        const mine = shrink(box.aabb, 0.75);
        const overlaps = [];
        for (const j of visible) {
            if (j === idx || overlaps.length >= 3) continue;
            const other = objects[j];
            if (Math.abs(other.p[0] - o.p[0]) > 15 || Math.abs(other.p[2] - o.p[2]) > 15) continue;
            if (shrink(orientedBox(other).aabb, 0.75).intersectsBox(mine)) overlaps.push(other.label ?? other.key);
        }

        // Held up: something solid just below, above (hung from a ceiling) or beside it (fixed to a wall).
        let supported = null, gapM = null;
        if (levelLoaded) {
            const solids = [...level, ...modelMeshes];
            const bottom = box.aabb.min.y;
            const down = solidHit(new THREE.Vector3(box.centre.x, bottom + 0.2, box.centre.z), new THREE.Vector3(0, -1, 0), 40, solids, idx);
            gapM = down ? Math.max(0, bottom - down.point.y) : null;
            supported = gapM !== null && gapM <= SUPPORT_REACH_M;
            if (!supported) {
                const up = solidHit(new THREE.Vector3(box.centre.x, box.aabb.max.y - 0.05, box.centre.z), new THREE.Vector3(0, 1, 0), SUPPORT_REACH_M + 0.05, solids, idx);
                supported = !!up;
            }
            if (!supported) {
                for (const [dir, extent] of [[ax, hx], [ax.clone().negate(), hx], [az, hz], [az.clone().negate(), hz]]) {
                    if (solidHit(box.centre, dir, extent + SUPPORT_REACH_M, solids, idx)) { supported = true; break; }
                }
            }
        }
        return { key, levelLoaded, inside, overlaps, supported, gapM };
    }

    /** False for see-through surfaces (leaves, grates, glass, decals, water), which the game lets players pass or see through. */
    function isSolid(hit) {
        const mats = hit.object.material;
        const m = Array.isArray(mats) ? mats[hit.face.materialIndex] : mats;
        return !!m && !m.transparent && !(m.alphaTest > 0);
    }

    /** Shortens or turns a horizontal step so the walker stops at a wall and slides along it. */
    function collide(move) {
        for (let pass = 0; pass < 2 && move.lengthSq() > 1e-8; pass++) {
            const dist = move.length();
            const hit = wallAhead(move.clone().normalize(), dist);
            if (!hit) return;
            const normal = hit.face.normal.clone().transformDirection(hit.object.matrixWorld);
            if (hit.object.isInstancedMesh && hit.instanceId !== undefined) {
                const m = new THREE.Matrix4();
                hit.object.getMatrixAt(hit.instanceId, m);
                normal.copy(hit.face.normal).transformDirection(m).transformDirection(hit.object.matrixWorld);
            }
            normal.y = 0;
            if (normal.lengthSq() < 1e-6) return; // a floor or ceiling face, not a wall
            normal.normalize();
            if (normal.dot(move) > 0) normal.negate(); // face seen from behind
            // Split the step into "towards the wall" and "along the wall": keep all of the second,
            // and only as much of the first as leaves the walker a body radius from the wall.
            const approach = -move.dot(normal); // > 0: how far this step goes towards the wall
            const cosine = approach / dist;
            const room = Math.max(0, (hit.distance - WALK_RADIUS_M) * cosine);
            move.addScaledVector(normal, approach - Math.min(approach, room));
        }
    }

    function setWalk(on) {
        if (on === walkOn) return;
        walkOn = on;
        walkKeys.clear();
        walkEyeY = null;
        controls.enabled = !on;
        if (!on && document.pointerLockElement === renderer.domElement) document.exitPointerLock();
        if (on) {
            const f = controls.target.clone().sub(camera.position).normalize();
            walkYaw = Math.atan2(-f.x, -f.z);
            walkPitch = Math.max(-1.4, Math.min(1.4, Math.asin(Math.max(-1, Math.min(1, f.y)))));
            if (walkFloor) {
                // Step down to eye height where the view is looking (the base's floor), not where the orbit camera hovered.
                const ground = floorBelow(controls.target) ?? controls.target.y;
                camera.position.set(controls.target.x, ground + EYE_M, controls.target.z);
                camera.position.addScaledVector(new THREE.Vector3(-Math.sin(walkYaw), 0, -Math.cos(walkYaw)), -3);
                const back = floorBelow(camera.position);
                if (back !== null && Math.abs(back - ground) < 2) camera.position.y = back + EYE_M;
                walkPitch = 0;
            }
            applyWalkCamera();
        } else {
            controls.target.copy(camera.position).addScaledVector(walkForward(), 5);
            controls.update();
        }
        requestRender();
    }

    let walkRamp = 0, walkEyeY = null;
    function walkTick(now) {
        if (!walkOn || disposed || walkKeys.size === 0) { walkLast = 0; walkRamp = 0; return; }
        const dt = walkLast ? Math.min(0.1, (now - walkLast) / 1000) : 0;
        walkLast = now;
        const fast = walkKeys.has("shift") ? 3 : 1;
        // Speeds up over a quarter of a second instead of starting at full speed.
        walkRamp = Math.min(1, walkRamp + dt * 4);
        const speed = 4 * fast * dt * (0.35 + 0.65 * walkRamp);
        const flat = new THREE.Vector3(-Math.sin(walkYaw), 0, -Math.cos(walkYaw));
        const right = new THREE.Vector3(Math.cos(walkYaw), 0, -Math.sin(walkYaw));
        const move = new THREE.Vector3();
        if (walkKeys.has("w") || walkKeys.has("arrowup")) move.add(walkFloor ? flat : walkForward());
        if (walkKeys.has("s") || walkKeys.has("arrowdown")) move.sub(walkFloor ? flat : walkForward());
        if (walkKeys.has("d") || walkKeys.has("arrowright")) move.add(right);
        if (walkKeys.has("a") || walkKeys.has("arrowleft")) move.sub(right);
        // E / Space go up and Q / C down, on the floor too: while held the floor is not followed, and
        // on letting go the walker lands on whatever is below (so another storey can be reached).
        const rising = walkKeys.has("e") || walkKeys.has(" ");
        const sinking = walkKeys.has("q") || walkKeys.has("c");
        if (rising) move.y += 1;
        if (sinking) move.y -= 1;
        const vertical = rising || sinking;
        if (vertical) walkEyeY = null;
        if (move.lengthSq() > 0) {
            move.normalize().multiplyScalar(speed);
            // On the floor the walker is solid: walls, level pieces and placed objects stop it, and
            // it slides along them. Flying passes through everything.
            if (walkFloor && !vertical) collide(move);
            camera.position.add(move);
        }
        if (walkFloor && !vertical && now - walkFloorCheck > 90) {
            walkFloorCheck = now;
            // Steps and ramps are climbed (up to knee height); a drop is followed down.
            const ground = floorBelow(new THREE.Vector3(camera.position.x, camera.position.y - EYE_M + 0.6, camera.position.z));
            if (ground !== null) walkEyeY = ground + EYE_M;
        }
        // Steps and drops are followed smoothly rather than in jumps.
        if (walkFloor && walkEyeY !== null) camera.position.y += (walkEyeY - camera.position.y) * Math.min(1, dt * 12);
        markMoving();
        applyWalkCamera();
        maybeFollowLevel();
        requestAnimationFrame(walkTick);
    }

    /** Reloads the level once the walker has gone far from where it was last loaded. */
    function maybeFollowLevel() {
        if (!levelOptions.enabled || !levelCentre) return;
        const r = Math.max(5, levelOptions.radius || 40);
        if (Math.hypot(camera.position.x - levelCentre.x, camera.position.z - levelCentre.z) > r * 0.5) loadLevel();
    }

    function isTyping(e) {
        const t = e.target;
        if (!(t instanceof HTMLElement)) return false;
        if (t.isContentEditable || t.tagName === "TEXTAREA" || t.tagName === "SELECT") return true;
        // Ticking a checkbox (such as "Stay on the floor") leaves it focused; that is not typing.
        return t.tagName === "INPUT" && !/^(checkbox|radio|button|submit|range|color)$/i.test(t.type);
    }

    function onWalkKeyDown(e) {
        if (!walkOn || isTyping(e) || e.ctrlKey || e.metaKey || e.altKey) return;
        const k = e.key.toLowerCase();
        if (k === "escape") { setWalk(false); dotnet.invokeMethodAsync("OnWalkEnded").catch(() => { }); return; }
        if (!["w", "a", "s", "d", "q", "e", "c", " ", "shift", "arrowup", "arrowdown", "arrowleft", "arrowright"].includes(k)) return;
        e.preventDefault();
        const was = walkKeys.size;
        walkKeys.add(k);
        if (was === 0) requestAnimationFrame(walkTick);
    }

    function onWalkKeyUp(e) { walkKeys.delete(e.key.toLowerCase()); }

    window.addEventListener("keydown", onWalkKeyDown);
    window.addEventListener("keyup", onWalkKeyUp);
    window.addEventListener("blur", () => walkKeys.clear());
    // Walking looks around like a game: click the view to capture the mouse (Escape lets it go), or
    // drag when it is not captured.
    const locked = () => document.pointerLockElement === renderer.domElement;
    // The browser takes the first Escape to let the mouse go and never passes the key on, so walking
    // used to carry on: losing the captured mouse while walking now ends walking too.
    let wasLocked = false;
    document.addEventListener("pointerlockchange", () => {
        const now = locked();
        if (wasLocked && !now && walkOn && !disposed) {
            setWalk(false);
            dotnet.invokeMethodAsync("OnWalkEnded").catch(() => { });
        }
        wasLocked = now;
    });
    renderer.domElement.addEventListener("pointermove", e => {
        if (!walkOn) return;
        let dx, dy;
        if (locked()) { dx = e.movementX; dy = e.movementY; }
        else if (walkLookFrom) { dx = e.clientX - walkLookFrom[0]; dy = e.clientY - walkLookFrom[1]; walkLookFrom = [e.clientX, e.clientY]; }
        else return;
        walkYaw -= dx * 0.0032;
        walkPitch = Math.max(-1.45, Math.min(1.45, walkPitch - dy * 0.0032));
        markMoving();
        applyWalkCamera();
    });
    renderer.domElement.addEventListener("pointerdown", e => {
        if (!walkOn) return;
        if (!locked() && renderer.domElement.requestPointerLock) {
            try { renderer.domElement.requestPointerLock(); } catch { /* not allowed here: drag instead */ }
        }
        walkLookFrom = [e.clientX, e.clientY];
    });
    window.addEventListener("pointerup", () => { walkLookFrom = null; });

    /** The level piece under a screen point (visible side of the ceiling cut): its name and its actor ("Map:Actor"), or null. */
    function levelPieceAt(clientX, clientY) {
        if (levelGroup.children.length === 0) return null;
        const rect = renderer.domElement.getBoundingClientRect();
        pointer.set(((clientX - rect.left) / rect.width) * 2 - 1, -(((clientY - rect.top) / rect.height) * 2 - 1));
        raycaster.setFromCamera(pointer, camera);
        for (const hit of raycaster.intersectObjects(levelGroup.children, false)) {
            if (levelClip.distanceToPoint(hit.point) < 0) continue;
            const { actors, actorNames } = hit.object.userData;
            const actor = actors && hit.instanceId !== undefined ? actorNames[actors[hit.instanceId]] ?? null : null;
            return { name: hit.object.name || null, actor };
        }
        return null;
    }
    function levelNameAt(clientX, clientY) { return levelPieceAt(clientX, clientY)?.name ?? null; }

    renderer.domElement.addEventListener("pointerdown", e => { downAt = [e.clientX, e.clientY]; });
    renderer.domElement.addEventListener("pointerup", e => {
        if (!downAt || transform.dragging || transform.axis) { downAt = null; return; }
        const moved = Math.hypot(e.clientX - downAt[0], e.clientY - downAt[1]);
        downAt = null;
        if (moved > 4) return;
        let marker = null;
        for (const [kind, layer] of Object.entries(markerLayers)) {
            const found = layer.at(e.clientX, e.clientY);
            if (found && (!marker || found.dist < marker.dist)) marker = { kind, ...found };
        }
        if (marker) {
            if (marker.kind === "npc") dotnet.invokeMethodAsync("OnNpcPicked", marker.item.id).catch(() => { });
            else if (marker.kind === "door") dotnet.invokeMethodAsync("OnDoorPicked", marker.item.id).catch(() => { });
            else dotnet.invokeMethodAsync("OnMarkerPicked", marker.kind, marker.item.id).catch(() => { });
            return;
        }
        const hit = pick(e.clientX, e.clientY);
        const additive = e.ctrlKey || e.shiftKey || e.metaKey;
        // Level pieces are reference only: a click on one names it, and never selects anything.
        const piece = hit ? null : levelPieceAt(e.clientX, e.clientY);
        dotnet.invokeMethodAsync("OnLevelPicked", piece?.name ?? null, piece?.actor ?? null).catch(() => { });
        if (additive && !hit) return; // a modified click on nothing keeps the selection
        // C# owns the selection (it also drives the list and the inspector) and pushes it back.
        dotnet.invokeMethodAsync("OnPicked", hit ? hit.key : null, additive);
    });

    // ---- hover and direct dragging ----------------------------------------------------------------
    // What is under the pointer gets a thin outline and a hand cursor, so it is clear what a click
    // will pick. With moving on (a gizmo mode set), the selected piece can be dragged straight across
    // the floor; hold Alt to drag it up and down instead. The drop is staged like a gizmo move.
    const hoverMaterial = new THREE.LineBasicMaterial({ color: 0x7fd8ff, depthTest: false, transparent: true, opacity: 0.85 });
    const hoverLine = new THREE.LineSegments(edgesGeometry, hoverMaterial);
    hoverLine.matrixAutoUpdate = false;
    hoverLine.renderOrder = 9;
    hoverLine.visible = false;
    scene.add(hoverLine);
    let hoverKey = null, hoverQueued = false, hoverAt = null;
    let drag = null; // { idx, plane, offset, vertical }

    function setHover(o) {
        const key = o ? o.key : null;
        renderer.domElement.style.cursor = drag ? "grabbing" : o ? (canDrag(o) ? "grab" : "pointer") : "";
        if (key === hoverKey) return;
        hoverKey = key;
        hoverLine.visible = !!o && !selectedKeys.has(key);
        if (o) boxMatrix(hoverLine.matrix, o);
        hoverLine.matrixWorldNeedsUpdate = true;
        requestRender();
    }

    // Edit mode (set by the editor): any piece a player built can be pressed and dragged in one go,
    // whether it was selected first or not; a piece that is part of a selection moves the whole
    // selection. Moves snap to 10 cm (hold Shift to move freely).
    let draggable = false;
    const SNAP_M = 0.1;
    function canDrag(o) {
        return draggable && o && o.built && !o.mark && !walkOn; // not a piece staged for removal, nor a copy not yet saved
    }

    renderer.domElement.addEventListener("pointermove", e => {
        if (drag) { dragTo(e); return; }
        if (walkOn || e.buttons) return;
        hoverAt = [e.clientX, e.clientY];
        if (hoverQueued) return;
        hoverQueued = true;
        requestAnimationFrame(() => {
            hoverQueued = false;
            if (!hoverAt || drag) return;
            setHover(pick(hoverAt[0], hoverAt[1]));
        });
    });
    renderer.domElement.addEventListener("pointerleave", () => { hoverAt = null; if (!drag) setHover(null); });

    renderer.domElement.addEventListener("pointerdown", e => {
        if (e.button !== 0 || transform.dragging || transform.axis) return;
        const o = pick(e.clientX, e.clientY);
        if (!canDrag(o)) return;
        const idx = keyToIndex.get(o.key);
        // Pressing an unselected piece selects it (and tells the editor) and drags it at once.
        if (!selectedKeys.has(o.key)) {
            setSelection([o.key], o.key);
            dotnet.invokeMethodAsync("OnPicked", o.key, false).catch(() => { });
        }
        const group = [...selectedKeys].map(k => keyToIndex.get(k)).filter(i => i !== undefined && objects[i].built && !objects[i].mark);
        const starts = new Map(group.map(i => [i, objects[i].p.slice()]));
        const at = new THREE.Vector3(o.p[0], o.p[1], o.p[2]);
        const vertical = e.altKey;
        const normal = vertical ? camera.getWorldDirection(new THREE.Vector3()).setY(0).normalize().negate() : new THREE.Vector3(0, 1, 0);
        if (vertical && normal.lengthSq() < 1e-6) normal.set(0, 0, 1);
        const plane = new THREE.Plane().setFromNormalAndCoplanarPoint(normal, at);
        const hit = planeHit(e, plane);
        if (!hit) return;
        drag = { idx, plane, offset: at.clone().sub(hit), vertical, moved: false, group, starts, origin: at.clone() };
        controls.enabled = false;
        downAt = null; // not a click
        renderer.domElement.setPointerCapture(e.pointerId);
        setHover(o);
        e.stopImmediatePropagation();
    }, true);

    function planeHit(e, plane) {
        const rect = renderer.domElement.getBoundingClientRect();
        pointer.set(((e.clientX - rect.left) / rect.width) * 2 - 1, -(((e.clientY - rect.top) / rect.height) * 2 - 1));
        raycaster.setFromCamera(pointer, camera);
        return raycaster.ray.intersectPlane(plane, new THREE.Vector3());
    }

    function dragTo(e) {
        const hit = planeHit(e, drag.plane);
        if (!hit) return;
        const next = hit.add(drag.offset);
        const delta = next.sub(drag.origin);
        if (drag.vertical) { delta.x = 0; delta.z = 0; } else delta.y = 0;
        if (!e.shiftKey) {
            delta.x = Math.round(delta.x / SNAP_M) * SNAP_M;
            delta.y = Math.round(delta.y / SNAP_M) * SNAP_M;
            delta.z = Math.round(delta.z / SNAP_M) * SNAP_M;
        }
        drag.delta = delta.clone();
        for (const i of drag.group) {
            const start = drag.starts.get(i);
            objects[i].p = [start[0] + delta.x, start[1] + delta.y, start[2] + delta.z];
            updateOneInstance(i);
        }
        drag.moved = delta.lengthSq() > 0;
        const o = objects[drag.idx];
        proxy.position.set(o.p[0], o.p[1], o.p[2]);
        proxy.quaternion.set(o.q[0], o.q[1], o.q[2], o.q[3]);
        updateSelectionBox();
        if (hoverLine.visible) { boxMatrix(hoverLine.matrix, o); hoverLine.matrixWorldNeedsUpdate = true; }
        requestRender();
    }

    window.addEventListener("pointerup", () => {
        if (!drag) return;
        const moved = drag.moved;
        const finished = drag;
        drag = null;
        controls.enabled = !walkOn;
        renderer.domElement.style.cursor = "grab";
        if (moved && finished.group.length > 1) {
            // A whole selection moved: staged as one group move (the editor converts the step).
            const from = finished.origin, to = finished.origin.clone().add(finished.delta);
            dotnet.invokeMethodAsync("OnGroupDragged", [from.x, from.y, from.z], [to.x, to.y, to.z]).catch(() => { });
        } else if (moved && selectedKey !== null) {
            const prev = gizmoMode;
            gizmoMode = "translate";
            commitGizmo();
            gizmoMode = prev;
        }
    });

    // Editing keys, while the view is being used: R turns the selection 45 degrees (Shift+R 15),
    // Delete removes it, Ctrl+D copies it beside itself.
    window.addEventListener("keydown", e => {
        if (!draggable || walkOn || isTyping(e) || !hostHasFocus() || selectedKeys.size === 0) return;
        const k = e.key.toLowerCase();
        if (k === "r" && !e.ctrlKey && !e.metaKey && !e.altKey) {
            e.preventDefault();
            dotnet.invokeMethodAsync("OnRotateKey", e.shiftKey ? 15 : 45).catch(() => { });
        } else if (k === "delete" || (k === "backspace" && !e.ctrlKey)) {
            e.preventDefault();
            dotnet.invokeMethodAsync("OnDeleteKey").catch(() => { });
        } else if (k === "d" && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            dotnet.invokeMethodAsync("OnDuplicateKey").catch(() => { });
        }
    });

    window.addEventListener("keydown", e => {
        if (e.key === "Escape" && !walkOn && !isTyping(e)) dotnet.invokeMethodAsync("OnEscapePressed").catch(() => { });
    });

    // Double-click a spot to orbit around it and move in closer: the quick way to look at a detail.
    renderer.domElement.addEventListener("dblclick", e => {
        if (walkOn) return;
        const rect = renderer.domElement.getBoundingClientRect();
        pointer.set(((e.clientX - rect.left) / rect.width) * 2 - 1, -(((e.clientY - rect.top) / rect.height) * 2 - 1));
        raycaster.setFromCamera(pointer, camera);
        const targets = [...modelMeshes, ...meshes.filter(m => m.count > 0), ...levelGroup.children];
        const hit = raycaster.intersectObjects(targets, false).find(h => levelClip.distanceToPoint(h.point) >= 0);
        if (!hit) return;
        const offset = camera.position.clone().sub(controls.target).multiplyScalar(0.4);
        if (offset.length() < 1.5) offset.setLength(1.5);
        controls.target.copy(hit.point);
        camera.position.copy(hit.point).add(offset);
        controls.update();
        requestRender();
    });

    function setSelection(keys, primary) {
        selectedKeys = new Set((keys ?? []).filter(k => keyToIndex.has(k)));
        selectedKey = primary !== null && primary !== undefined && selectedKeys.has(primary) ? primary : (selectedKeys.size ? [...selectedKeys].pop() : null);
        updateSelectionBox();
        attachGizmo();
        requestRender();
    }

    function select(key) {
        setSelection(key === null || key === undefined ? [] : [key], key);
    }

    // ---- gizmo -------------------------------------------------------------------------------
    const proxy = new THREE.Object3D();
    scene.add(proxy);
    const transform = new TransformControls(camera, renderer.domElement);
    const gizmoHelper = transform.getHelper();
    scene.add(gizmoHelper);
    transform.setSpace("world");
    transform.setSize(0.9);
    // Rotation is limited to the vertical axis: hide the two horizontal rings, and lock the
    // screen-space and free-rotation handles (E, XYZE) out so they cannot be grabbed.
    transform.showX = false;
    transform.showZ = false;
    gizmoHelper.traverse(child => {
        if (child.name === "E" || child.name === "XYZE") {
            Object.defineProperty(child, "visible", { get: () => false, set: () => { } });
        }
    });
    let gizmoMode = null; // null (off) | "translate" | "rotate"
    let startQuat = new THREE.Quaternion();
    const yAxis = new THREE.Vector3(0, 1, 0);

    function attachGizmo() {
        const idx = selectedKey === null ? undefined : keyToIndex.get(selectedKey);
        if (gizmoMode === null || idx === undefined || selectedKeys.size > 1) {
            transform.detach();
            gizmoHelper.visible = false;
            return;
        }
        const o = objects[idx];
        proxy.position.set(o.p[0], o.p[1], o.p[2]);
        proxy.quaternion.set(o.q[0], o.q[1], o.q[2], o.q[3]);
        startQuat.copy(proxy.quaternion);
        transform.setMode(gizmoMode);
        transform.attach(proxy);
        gizmoHelper.visible = true;
    }

    transform.addEventListener("dragging-changed", e => {
        controls.enabled = !e.value;
        if (e.value) {
            startQuat.copy(proxy.quaternion);
        } else if (selectedKey !== null) {
            commitGizmo();
        }
    });

    transform.addEventListener("objectChange", () => {
        const idx = selectedKey === null ? undefined : keyToIndex.get(selectedKey);
        if (idx === undefined) return;
        if (gizmoMode === "rotate") constrainToYaw();
        const o = objects[idx];
        o.p = [proxy.position.x, proxy.position.y, proxy.position.z];
        o.q = [proxy.quaternion.x, proxy.quaternion.y, proxy.quaternion.z, proxy.quaternion.w];
        updateOneInstance(idx);
    });

    // Keep only the twist about the viewer's up axis of the rotation applied since the drag began,
    // so a stray drag can never pitch or roll a piece.
    function constrainToYaw() {
        const delta = proxy.quaternion.clone().multiply(startQuat.clone().invert());
        const angle = 2 * Math.atan2(delta.y, delta.w);
        proxy.quaternion.setFromAxisAngle(yAxis, angle).multiply(startQuat);
    }

    function commitGizmo() {
        dotnet.invokeMethodAsync("OnGizmoCommitted", selectedKey, gizmoMode,
            [proxy.position.x, proxy.position.y, proxy.position.z],
            [proxy.quaternion.x, proxy.quaternion.y, proxy.quaternion.z, proxy.quaternion.w]);
    }

    // ---- resize / lifecycle ------------------------------------------------------------------
    function resize() {
        const w = Math.max(1, host.clientWidth), h = Math.max(1, host.clientHeight);
        renderer.setSize(w, h, false);
        renderer.domElement.style.width = "100%";
        renderer.domElement.style.height = "100%";
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
        requestRender();
    }
    const observer = new ResizeObserver(resize);
    observer.observe(host);
    controls.addEventListener("change", () => { markMoving(); requestRender(); followLevelSoon(); });
    controls.addEventListener("start", () => { aimedAt = null; }); // the player moves the view themselves

    // The level follows the view: once it settles (after a pan, a fly or a jump to something picked
    // in a list) far enough from where the level was loaded, the level around the new spot loads.
    let followTimer = 0;
    function followLevelSoon() {
        clearTimeout(followTimer);
        followTimer = setTimeout(followLevelNow, 700);
    }
    /** Loads the level around the view's centre when it has moved far from the last load. */
    function followLevelNow() {
        clearTimeout(followTimer);
        if (disposed || walkOn || !levelOptions.enabled || !levelCentre) return false;
        const r = Math.max(5, levelOptions.radius || 40);
        const t = controls.target;
        if (Math.hypot(t.x - levelCentre.x, t.z - levelCentre.z) <= r * 0.5 && Math.abs(t.y - levelCentre.y) <= r * 0.25) return false;
        loadLevel();
        return true;
    }
    renderer.domElement.tabIndex = 0;
    renderer.domElement.addEventListener("pointerenter", () => { pointerInside = true; });
    renderer.domElement.addEventListener("pointerleave", () => { pointerInside = false; });
    resize();
    requestRender();

    const api = {
        /** Replaces every object. Each is {key, cat, p:[x,y,z], q:[x,y,z,w], s:[x,y,z], built, label, cls?, paint?, variant?}. */
        setScene(list) {
            objects = list;
            labelOrder = null;
            keyToIndex = new Map(list.map((o, i) => [o.key, i]));
            buildMeshes();
            updateGrid();
            visible = list.map((_, i) => i);
            refreshLamps();
            rebuildInstances();
            setSelection([...selectedKeys], selectedKey);
            attachGizmo();
            loadClassModels();
        },
        /**
         * Whether game models can be shown: {available, installed, title, plugin}. Never throws; a
         * host without the endpoints (the browser build) answers "not installed".
         */
        async modelStatus() {
            try {
                const response = await fetch(`${MODEL_BASE}/status`);
                if (!response.ok) return { available: false, installed: false };
                return await response.json();
            } catch {
                return { available: false, installed: false };
            }
        },
        /** Draws objects with their game models (true) or boxes (false). */
        setModelsEnabled(on) {
            modelsOn = !!on;
            rebuildInstances();
            updateSelectionBox();
            if (modelsOn) loadClassModels();
        },
        /**
         * Level geometry around the view: {enabled, region, radius (m), cutAbove (m above the base floor, 0 = no cut),
         * excludeActors}. Any field left out keeps its value. Reloads around the current view centre.
         */
        setLevel(options) {
            levelOptions = { ...levelOptions, ...(options ?? {}) };
            loadLevel();
        },
        /** Shows or hides the level's lamps (their light and glow). */
        setLampsVisible(on) {
            lampsOn = !!on;
            applyLampsOn();
            requestRender();
        },
        /** Reloads the level geometry around the current view centre. */
        reloadLevel() { loadLevel(); },
        /** Moves the ceiling cut without reloading (metres above the base floor; 0 or null = no cut). */
        setLevelCut(metres) {
            levelOptions.cutAbove = metres;
            updateLevelCut();
        },
        /** Which object indexes are drawn (filters live in C#). */
        setVisible(indices) {
            visible = indices;
            rebuildInstances();
            refreshLamps();
        },
        /** Updates one object's transform in place (after a staged edit or a revert). */
        setObjectTransform(key, p, q) {
            const idx = keyToIndex.get(key);
            if (idx === undefined) return;
            objects[idx].p = p;
            objects[idx].q = q;
            updateOneInstance(idx);
            if (key === selectedKey) attachGizmo();
        },
        /** Power cables as [ax, ay, az, bx, by, bz] segments in viewer space (replaces any drawn before). */
        setCables(list) {
            for (const child of [...cableGroup.children]) {
                cableGroup.remove(child);
                child.geometry.dispose();
            }
            const points = [];
            for (const c of list ?? []) {
                points.push(new THREE.Vector3(c[0], c[1] + 0.15, c[2]), new THREE.Vector3(c[3], c[4] + 0.15, c[5]));
            }
            if (points.length) {
                const line = new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(points), cableMaterial);
                line.renderOrder = 11;
                line.frustumCulled = false;
                cableGroup.add(line);
            }
            requestRender();
        },
        /** Wire boxes showing where staged objects were saved. Each {cat, p, q, s, color?}. */
        setGhosts(list) {
            for (const child of [...ghostGroup.children]) {
                ghostGroup.remove(child);
                child.material.dispose();
            }
            const m = new THREE.Matrix4();
            for (const g of list) {
                const line = new THREE.LineSegments(edgesGeometry,
                    new THREE.LineBasicMaterial({ color: g.color ?? 0xff5ec4, depthTest: false, transparent: true, opacity: 0.8 }));
                boxMatrix(m, g);
                line.matrixAutoUpdate = false;
                line.matrix.copy(m);
                line.renderOrder = 9;
                ghostGroup.add(line);
            }
            requestRender();
        },
        /** The region's doors as markers: each {id, p:[x,y,z] (viewer space), color}. Replaces any drawn before. */
        setDoors(list) { doorLayer.items = list ?? []; doorLayer.rebuild(); requestRender(); },
        /** Shows or hides the door markers. */
        setDoorsVisible(on) { doorLayer.on = !!on; doorLayer.rebuild(); requestRender(); },
        /** Rings one door marker (null clears it). */
        setDoorSelection(id) { doorLayer.selected = id ?? null; doorLayer.rebuild(); requestRender(); },
        /** Screen position (CSS pixels relative to the page) of a door marker, or null. Used by UI tests. */
        doorScreenPosition(id) { const d = doorLayer.items.find(x => x.id === id); return d ? doorLayer.screen(d) : null; },
        /** The door markers drawn (id and viewer position), for UI tests. */
        doorList() { return doorLayer.items.map(d => ({ id: d.id, p: d.p })); },
        /** The region's NPCs and pets as markers: each {id, p:[x,y,z] (viewer space), color}. */
        setNpcs(list) { npcLayer.items = list ?? []; npcLayer.rebuild(); requestRender(); },
        /** Shows or hides the NPC markers. */
        setNpcsVisible(on) { npcLayer.on = !!on; npcLayer.rebuild(); requestRender(); },
        /** Outlines one NPC marker (null clears it). */
        setNpcSelection(id) { npcLayer.selected = id ?? null; npcLayer.rebuild(); requestRender(); },
        /** Screen position of an NPC marker, or null. Used by UI tests. */
        npcScreenPosition(id) { const d = npcLayer.items.find(x => x.id === id); return d ? npcLayer.screen(d) : null; },
        /** The NPC markers drawn, for UI tests. */
        npcList() { return npcLayer.items.map(d => ({ id: d.id, p: d.p })); },
        /** Walk mode on or off; floor=true keeps the eye at standing height over what is underneath. */
        setWalk(on, floor) {
            walkFloor = floor !== false;
            setWalk(!!on);
        },
        /**
         * The point under the middle of the view (the floor, a level piece, a model or a box the view
         * looks at, above the ceiling cut ignored), or the orbit target when nothing is there. Viewer space.
         */
        /** The surface under a page point (a piece dropped on the view lands there), or the view centre's. */
        placementPointAt(clientX, clientY) {
            const rect = renderer.domElement.getBoundingClientRect();
            const at = new THREE.Vector2(((clientX - rect.left) / rect.width) * 2 - 1, -(((clientY - rect.top) / rect.height) * 2 - 1));
            raycaster.setFromCamera(at, camera);
            const targets = [...levelGroup.children, ...modelMeshes, ...meshes.filter(m => m.count > 0)];
            for (const hit of raycaster.intersectObjects(targets, false)) {
                if (levelClip.distanceToPoint(hit.point) < 0) continue;
                return hit.point.toArray();
            }
            // Nothing under the pointer: where the ray meets the floor height of the view centre.
            const floor = new THREE.Plane(new THREE.Vector3(0, 1, 0), -controls.target.y);
            const point = raycaster.ray.intersectPlane(floor, new THREE.Vector3());
            return (point ?? controls.target).toArray();
        },
        placementPoint() {
            raycaster.setFromCamera(new THREE.Vector2(0, 0), camera);
            const targets = [...levelGroup.children, ...modelMeshes, ...meshes.filter(m => m.count > 0)];
            for (const hit of raycaster.intersectObjects(targets, false)) {
                if (levelClip.distanceToPoint(hit.point) < 0) continue;
                return hit.point.toArray();
            }
            return controls.target.toArray();
        },
        /** Distance (metres) to the nearest solid surface straight ahead of the walker within 5 m, or null. Used by UI tests. */
        wallAheadDistance() {
            const dir = new THREE.Vector3(-Math.sin(walkYaw), 0, -Math.cos(walkYaw));
            const hit = wallAhead(dir, 5);
            return hit ? hit.distance : null;
        },
        /**
         * Placement checks for the given objects (staged moves and new pieces): {key, levelLoaded,
         * inside (cuts into the level), overlaps (labels of pieces it overlaps), supported (something
         * holds it up), gapM (metres to the floor below, or null)}. Inside and support need the level.
         */
        checkPlacement(keys) { return (keys ?? []).map(placementReport).filter(Boolean); },
        /** The floor height (viewer Y) under a viewer-space point, looking from 1 m above it, or null. */
        floorAt(point) {
            const solids = [...nearSolids(new THREE.Vector3(point[0], point[1], point[2])), ...modelMeshes];
            const hit = solidHit(new THREE.Vector3(point[0], point[1] + 1, point[2]), new THREE.Vector3(0, -1, 0), 41, solids, -1);
            return hit ? hit.point.y : null;
        },
        /**
         * Diagnostics: milliseconds for n horizontal rays from the camera against the whole level (how
         * walking used to test walls) and against the nearby list with bounding-volume trees (how it
         * does now), plus whether both found the same nearest wall.
         */
        rayBenchmark(n = 50) {
            const from = camera.position.clone(), dir = new THREE.Vector3(-Math.sin(walkYaw), 0, -Math.cos(walkYaw));
            const run = targets => {
                const t0 = performance.now(); let hit = null;
                for (let i = 0; i < n; i++) {
                    walkProbe.set(from, dir); walkProbe.far = 6;
                    hit = walkProbe.intersectObjects(targets, false)[0] ?? null;
                }
                return { ms: performance.now() - t0, distance: hit ? hit.distance : null };
            };
            nearCaches.clear();
            const built = performance.now(); const near = nearSolids(from, WALK_NEAR_RADIUS_M); const buildMs = performance.now() - built;
            const fast = run([...near]);
            const saved = THREE.Mesh.prototype.raycast;
            const brute = run([...levelGroup.children]);
            return { rays: n, levelInstances, nearPieces: near.length, nearBuildMs: buildMs, nearMs: fast.ms, wholeLevelMs: brute.ms,
                nearDistance: fast.distance, wholeDistance: brute.distance, raycast: saved === acceleratedRaycast };
        },
        /** Where the camera is and looks (viewer space), for tests and the place tool. */
        cameraState() {
            return { position: camera.position.toArray(), target: controls.target.toArray(), walk: walkOn };
        },
        select(key) { select(key); },
        /** Replaces the whole selection: every selected key, and which one is primary (gizmo and inspector). */
        setSelection(keys, primary) { setSelection(keys, primary); },
        setLabels(on) { labelsOn = !!on; requestRender(); },
        /** Whether the selected piece may be dragged in the view (Edit mode on and the piece is movable). */
        setDraggable(on) { draggable = !!on; },
        /** mode: null | "translate" | "rotate". */
        setGizmo(mode) { gizmoMode = mode; attachGizmo(); requestRender(); },
        /**
         * Shows the player where something is: looks at a viewer-space point from above at an angle,
         * from the given distance (metres), drops a pin on it that shows through walls, and loads the
         * level around it when the level is on. Used by "Show in 3D" from the other tabs.
         */
        focusPoint(point, distance) {
            const target = new THREE.Vector3(point[0], point[1], point[2]);
            flyTo(target, distance);
            setPin(target);
            return true;
        },
        /** Frames a door ("door") or character ("npc") marker by id. False when no such marker is drawn. */
        focusMarker(kind, id, distance) {
            const layer = markerLayers[kind];
            if (!layer) return false;
            const item = layer.items.find(x => x.id === id);
            if (!item) return false;
            flyTo(new THREE.Vector3(item.p[0], item.p[1], item.p[2]), distance);
            setPin(null);
            return true;
        },
        /** Ground items ("item") or level things ("thing") as markers: each {id, p:[x,y,z] (viewer space), color}. */
        setMarkers(kind, list) { const layer = markerLayers[kind]; if (!layer) return; layer.items = list ?? []; layer.rebuild(); requestRender(); },
        /** Shows or hides one kind of marker. */
        setMarkersVisible(kind, on) { const layer = markerLayers[kind]; if (!layer) return; layer.on = !!on; layer.rebuild(); requestRender(); },
        /** Rings one marker of a kind (null clears it). */
        setMarkerSelection(kind, id) { const layer = markerLayers[kind]; if (!layer) return; layer.selected = id ?? null; layer.rebuild(); requestRender(); },
        /** The markers of a kind (id and viewer position), for UI tests. */
        markerList(kind) { return (markerLayers[kind]?.items ?? []).map(d => ({ id: d.id, p: d.p })); },
        /** Screen position of a marker of any kind, or null. Used by UI tests. */
        markerScreenPosition(kind, id) { const layer = markerLayers[kind]; const d = layer?.items.find(x => x.id === id); return d ? layer.screen(d) : null; },
        /**
         * Draws one level actor (a door, button, tram...) on its own and returns its picture: a
         * transparent square image, lit and framed from a three-quarter view. Used by the maintainer
         * tool that renders the pictures the editor's 2D lists ship with (tools/thumbnails).
         * Returns null when the level has no pieces for the actor.
         */
        async thumbnail(options) {
            const { region, actor, center, front, size = 512, radius = 25 } = options ?? {};
            const slice = await postJson(`${MODEL_BASE}/level`, {
                region,
                min: [center[0] - radius, center[1] - radius, center[2] - radius],
                max: [center[0] + radius, center[1] + radius, center[2] + radius],
                maxInstances: 5000,
                onlyActors: [actor],
            });
            const batches = slice?.batches ?? [];
            if (!batches.length) return null;
            const group = new THREE.Group();
            const box = new THREE.Box3();
            const instance = new THREE.Matrix4();
            const materials = [];
            for (const batch of batches) {
                const geometry = await loadGeometry(pictureMesh(batch.mesh)) ?? await loadGeometry(batch.mesh);
                if (!geometry) continue;
                if (!geometry.boundingBox) geometry.computeBoundingBox();
                const mats = batch.materials.length ? batch.materials.map(pictureMaterialFor) : [materialFor({ color: [0.6, 0.6, 0.6], opacity: 1 }, false)];
                materials.push(...mats);
                const count = batch.matrices.length / 16;
                const mesh = new THREE.InstancedMesh(geometry, mats, count);
                for (let i = 0; i < count; i++) {
                    instance.fromArray(batch.matrices, i * 16);
                    mesh.setMatrixAt(i, instance);
                    box.union(geometry.boundingBox.clone().applyMatrix4(instance));
                }
                mesh.instanceMatrix.needsUpdate = true;
                mesh.computeBoundingSphere();
                group.add(mesh);
            }
            if (!group.children.length || box.isEmpty()) return null;
            await texturesReady(materials, true);
            return shootSquare(group, box, size, front);
        },
        /**
         * Draws one kind of placed object (a locker, a fridge, a crate) from its own model on its own
         * and returns its picture, like thumbnail() does for level actors. Used by tools/thumbnails for
         * the containers list. Returns null when the game has no model for the class.
         */
        async classThumbnail(options) {
            const { cls, size = 512 } = options ?? {};
            const answer = await postJson(`${MODEL_BASE}/classes`, [cls]) ?? {};
            const description = answer[cls];
            if (!description?.parts?.length) return null;
            const group = new THREE.Group();
            const box = new THREE.Box3();
            const materials = [];
            for (const part of description.parts) {
                const geometry = await loadGeometry(pictureMesh(part.mesh)) ?? await loadGeometry(part.mesh);
                if (!geometry) continue;
                if (!geometry.boundingBox) geometry.computeBoundingBox();
                const mats = part.materials.length ? part.materials.map(pictureMaterialFor) : [materialFor({ color: [0.6, 0.6, 0.6], opacity: 1 }, false)];
                materials.push(...mats);
                const mesh = new THREE.Mesh(geometry, mats);
                mesh.matrixAutoUpdate = false;
                mesh.matrix.fromArray(part.matrix);
                box.union(geometry.boundingBox.clone().applyMatrix4(mesh.matrix));
                group.add(mesh);
            }
            if (!group.children.length || box.isEmpty()) return null;
            await texturesReady(materials, true);
            // A placed object's front faces along its own +X (the save's forward axis).
            if (cls.includes("#lamp=1")) {
                const at = box.getCenter(new THREE.Vector3());
                at.y = box.max.y - 0.08;
                const glow = new THREE.Sprite(new THREE.SpriteMaterial({ map: glowTexture, color: 0xffcc88,
                    blending: THREE.AdditiveBlending, transparent: true, depthWrite: false, opacity: 0.8 }));
                glow.position.copy(at);
                glow.scale.set(0.28, 0.28, 1);
                group.add(glow);
                const light = new THREE.PointLight(0xffcc88, LAMP_CANDELA, 6, 2);
                light.position.copy(at);
                group.add(light);
            }
            return shootSquare(group, box, size, [1, 0, 0]);
        },
        /**
         * The level straight from above, for the Bases map: an orthographic picture of a rectangle of
         * the world with everything more than cutAbove metres over the floor cut away. right and down
         * are the viewer-space directions the picture's x and y follow (the map's own axes), so the
         * picture lies exactly under the map's markers. Returns a data URL, or null when the level has
         * nothing there.
         */
        async topDownShot(options) {
            const { region, center, focus, right, down, metresWide, metresHigh, floorY, cutAbove = 3, width = 1280, height = 712 } = options ?? {};
            const c = new THREE.Vector3(...center);
            // Kept quick on a big area: the pieces nearest the base looked at first (the box is centred
            // on it and capped). The same meshes and textures as the 3D view, which are usually already
            // on disk (other sizes had to be read from the game files again, a minute and more).
            const f = focus ? new THREE.Vector3(...focus) : c;
            const r = Math.max(Math.abs(c.x - f.x) + metresWide / 2, Math.abs(c.z - f.z) + metresHigh / 2) + 2;
            const slice = await postJson(`${MODEL_BASE}/level`, {
                region, min: [f.x - r, floorY - 4, f.z - r], max: [f.x + r, floorY + cutAbove + 1, f.z + r], maxInstances: 12000,
            });
            // Level files still being read (the first time, or after a game update): the picture would be
            // missing parts, so the map waits and asks again rather than keeping a half picture.
            if ((slice?.pendingMaps ?? 0) > 0) return { pending: true };
            const small = m => ({ ...m, layers: null });
            const group = new THREE.Group(), materials = [];
            const instance = new THREE.Matrix4();
            for (const batch of slice?.batches ?? []) {
                const geometry = await loadGeometry(batch.mesh);
                if (!geometry) continue;
                const mats = batch.materials.length ? batch.materials.map(m => materialFor(small(m), true)) : [materialFor({ color: [0.6, 0.6, 0.6], opacity: 1 }, true)];
                materials.push(...mats);
                const count = batch.matrices.length / 16;
                const mesh = new THREE.InstancedMesh(geometry, mats, count);
                for (let i = 0; i < count; i++) mesh.setMatrixAt(i, instance.fromArray(batch.matrices, i * 16));
                mesh.instanceMatrix.needsUpdate = true;
                mesh.computeBoundingSphere();
                group.add(mesh);
            }
            if (!group.children.length) return null;
            await texturesReady(materials);

            const stage = new THREE.Scene();
            stage.background = new THREE.Color(0x101418);
            stage.add(new THREE.HemisphereLight(0xffffff, 0x445566, 1.9));
            const sunLight = new THREE.DirectionalLight(0xffffff, 1.1);
            sunLight.position.set(0.3, 1, 0.2);
            stage.add(sunLight);
            stage.add(group);

            // A camera high above, looking straight down, its picture's x along "right" and y along "down".
            const xAxis = new THREE.Vector3(...right).normalize();
            const yAxis = new THREE.Vector3(...down).normalize().negate(); // the picture's up
            const zAxis = new THREE.Vector3().crossVectors(xAxis, yAxis); // towards the camera
            const mirrored = zAxis.y < 0;
            if (mirrored) zAxis.negate();
            const shot = new THREE.OrthographicCamera(
                mirrored ? metresWide / 2 : -metresWide / 2, mirrored ? -metresWide / 2 : metresWide / 2,
                metresHigh / 2, -metresHigh / 2, 0.1, 500);
            const eye = new THREE.Vector3(c.x, floorY + 200, c.z);
            shot.matrixAutoUpdate = false;
            shot.matrix.makeBasis(mirrored ? xAxis.clone().negate() : xAxis, yAxis, zAxis).setPosition(eye);
            shot.matrixWorld.copy(shot.matrix);
            shot.matrixWorldInverse.copy(shot.matrixWorld).invert();
            shot.updateProjectionMatrix();

            const savedCut = levelClip.constant;
            levelClip.constant = floorY + cutAbove;
            const target = new THREE.WebGLRenderTarget(width, height, { samples: 4 });
            target.texture.colorSpace = THREE.SRGBColorSpace;
            const oldTarget = renderer.getRenderTarget();
            renderer.setRenderTarget(target);
            renderer.render(stage, shot);
            const pixels = new Uint8Array(width * height * 4);
            renderer.readRenderTargetPixels(target, 0, 0, width, height, pixels);
            renderer.setRenderTarget(oldTarget);
            levelClip.constant = savedCut;
            target.dispose();
            for (const mesh of group.children) mesh.dispose();

            const canvas = document.createElement("canvas");
            canvas.width = width;
            canvas.height = height;
            const g = canvas.getContext("2d");
            const image = g.createImageData(width, height);
            for (let y = 0; y < height; y++) image.data.set(pixels.subarray((height - 1 - y) * width * 4, (height - y) * width * 4), y * width * 4);
            g.putImageData(image, 0, 0);
            requestRender();
            return canvas.toDataURL("image/webp", 0.7);
        },
        /**
         * A picture of where one level actor (a door, button, elevator...) is: the level around it seen
         * from above at an angle, with the ceiling cut away a little above it, and the actor outlined
         * and pinned in orange. Used by tools/thumbnails for the places shown in the editor's lists.
         * Returns null when the level has nothing there.
         */
        async locationShot(options) {
            const { region, actor, center, front, width = 384, height = 240, radius = 16, cutAbove = 2.6, distance = 12 } = options ?? {};
            const query = r => ({ region, min: [center[0] - r, center[1] - r, center[2] - r], max: [center[0] + r, center[1] + r, center[2] + r], maxInstances: 6000 });
            const [around, own] = await Promise.all([
                postJson(`${MODEL_BASE}/level`, query(radius)),
                postJson(`${MODEL_BASE}/level`, { ...query(radius), onlyActors: [actor] }),
            ]);
            if ((around?.pendingMaps ?? 0) > 0 || (own?.pendingMaps ?? 0) > 0) return { pending: true };
            const build = async (slice, group, box, materials) => {
                const instance = new THREE.Matrix4();
                for (const batch of slice?.batches ?? []) {
                    const geometry = await loadGeometry(batch.mesh);
                    if (!geometry) continue;
                    if (!geometry.boundingBox) geometry.computeBoundingBox();
                    const mats = batch.materials.length ? batch.materials.map(m => materialFor(m, true)) : [materialFor({ color: [0.6, 0.6, 0.6], opacity: 1 }, true)];
                    materials.push(...mats);
                    const count = batch.matrices.length / 16;
                    const mesh = new THREE.InstancedMesh(geometry, mats, count);
                    for (let i = 0; i < count; i++) {
                        instance.fromArray(batch.matrices, i * 16);
                        mesh.setMatrixAt(i, instance);
                        if (box) box.union(geometry.boundingBox.clone().applyMatrix4(instance));
                    }
                    mesh.instanceMatrix.needsUpdate = true;
                    mesh.computeBoundingSphere();
                    group.add(mesh);
                }
            };
            const group = new THREE.Group(), materials = [], ownBox = new THREE.Box3();
            await build(around, group, null, materials);
            const ownGroup = new THREE.Group();
            await build(own, ownGroup, ownBox, []);
            for (const mesh of ownGroup.children) mesh.dispose();
            if (!group.children.length) return null;
            await texturesReady(materials);

            // The floor the ceiling is cut above is the thing's own height in the level, not the bottom of
            // its pieces: an elevator carries trims that reach metres down its shaft, and cutting from
            // there hid the car and the room around it. Only what stands above that floor is outlined.
            const floorY = ownBox.isEmpty() ? center[1] : Math.min(Math.max(ownBox.min.y, center[1]), ownBox.max.y);
            if (!ownBox.isEmpty()) ownBox.min.y = Math.max(ownBox.min.y, floorY - 0.5);
            const focus = ownBox.isEmpty() ? new THREE.Vector3(...center) : ownBox.getCenter(new THREE.Vector3());
            const stage = new THREE.Scene();
            stage.background = new THREE.Color(0x101418);
            stage.add(new THREE.HemisphereLight(0xffffff, 0x445566, 1.6));
            const sunLight = new THREE.DirectionalLight(0xffffff, 1.3);
            sunLight.position.set(0.4, 1, 0.6);
            stage.add(sunLight);
            stage.add(group);
            if (!ownBox.isEmpty()) {
                const outline = new THREE.LineSegments(edgesGeometry, new THREE.LineBasicMaterial({ color: 0xff9a2e, depthTest: false, transparent: true }));
                outline.renderOrder = 20;
                outline.matrixAutoUpdate = false;
                const size = ownBox.getSize(new THREE.Vector3()).max(new THREE.Vector3(0.2, 0.2, 0.2));
                outline.matrix.compose(focus, new THREE.Quaternion(), size.multiplyScalar(1.08));
                stage.add(outline);
            }
            const pin = pinGroup.clone();
            pin.visible = true;
            pin.position.set(focus.x, ownBox.isEmpty() ? focus.y : ownBox.max.y + 0.1, focus.z);
            stage.add(pin);

            const shot = new THREE.PerspectiveCamera(45, width / height, 0.05, 2000);
            const dir = viewDirection(front, 1.1);
            // Aimed a little above the thing so the pin over it stays in the picture.
            const aim = focus.clone().add(new THREE.Vector3(0, 1, 0));
            // Stepped back far enough for a big thing (a wide elevator platform) to fit in the picture.
            const span = ownBox.isEmpty() ? 0 : Math.max(ownBox.max.x - ownBox.min.x, ownBox.max.z - ownBox.min.z);
            shot.position.copy(aim).addScaledVector(dir, Math.max(distance, span * 1.2));
            shot.lookAt(aim);
            shot.updateProjectionMatrix();

            const savedCut = levelClip.constant;
            levelClip.constant = floorY + cutAbove;
            const target = new THREE.WebGLRenderTarget(width, height, { samples: 4 });
            target.texture.colorSpace = THREE.SRGBColorSpace;
            const oldTarget = renderer.getRenderTarget();
            renderer.setRenderTarget(target);
            renderer.render(stage, shot);
            const pixels = new Uint8Array(width * height * 4);
            renderer.readRenderTargetPixels(target, 0, 0, width, height, pixels);
            // A ceiling cut can hide every nearby surface. Retry without that cut before
            // returning a picture consisting only of the orange marker and background.
            let surfaces = 0;
            for (let i = 0; i < pixels.length; i += 4) {
                const r = pixels[i], g = pixels[i + 1], b = pixels[i + 2];
                const background = Math.abs(r - 16) + Math.abs(g - 20) + Math.abs(b - 24) < 15;
                const marker = r > 200 && g > 70 && g < 190 && b < 100;
                if (!background && !marker) surfaces++;
            }
            if (surfaces < 200) {
                levelClip.constant = 1e7;
                renderer.render(stage, shot);
                renderer.readRenderTargetPixels(target, 0, 0, width, height, pixels);
            }
            renderer.setRenderTarget(oldTarget);
            levelClip.constant = savedCut;
            target.dispose();
            for (const mesh of group.children) mesh.dispose();

            const canvas = document.createElement("canvas");
            canvas.width = width;
            canvas.height = height;
            const g = canvas.getContext("2d");
            const image = g.createImageData(width, height);
            for (let y = 0; y < height; y++) image.data.set(pixels.subarray((height - 1 - y) * width * 4, (height - y) * width * 4), y * width * 4);
            g.putImageData(image, 0, 0);
            requestRender();
            return { image: canvas.toDataURL("image/webp", 0.5), pieces: group.children.length, found: !ownBox.isEmpty() };
        },
        /** Removes the pin left by focusPoint. */
        clearPin() { setPin(null); },
        /** Loads the level around where the view now looks, at once (Show in 3D waits for it). */
        followLevel() { return followLevelNow(); },
        /** Scrolls the page so the whole view is in sight. */
        reveal() { (document.querySelector('[data-b3d="bar"]') ?? host).scrollIntoView({ block: "start", behavior: "smooth" }); },
        frameSelection() {
            const list = [...selectedKeys].map(k => keyToIndex.get(k)).filter(i => i !== undefined);
            if (list.length) frameIndices(list);
        },
        /** Frames every drawn object; with robust=true, the densest 85% (ignores far outliers). */
        frameVisible(robust) { frameIndices(visible, !!robust); },
        /** Frames the given objects (a base's deployables); unknown keys are skipped. False when none is drawn. */
        frameKeys(keys) {
            const list = (keys ?? []).map(k => keyToIndex.get(k)).filter(i => i !== undefined);
            if (!list.length) return false;
            frameIndices(list, true);
            if (levelOptions.enabled) loadLevel();
            return true;
        },
        /**
         * Points the camera at a viewer-space point from the given distance (metres), keeping the
         * current viewing direction. Used by UI tests to look at level details such as decals.
         */
        lookAt(point, distance) {
            const target = new THREE.Vector3(point[0], point[1], point[2]);
            const direction = camera.position.clone().sub(controls.target).normalize();
            controls.target.copy(target);
            camera.position.copy(target).addScaledVector(direction, Math.max(0.5, distance || 5));
            controls.update();
            requestRender();
        },
        /** Points the camera at a viewer-space point from a given viewer-space offset (metres). Used by UI tests. */
        lookFrom(point, offset) {
            controls.target.set(point[0], point[1], point[2]);
            camera.position.set(point[0] + offset[0], point[1] + offset[1], point[2] + offset[2]);
            controls.update();
            requestRender();
        },
        /** Screen position (CSS pixels relative to the page) of an object's origin, or null. Used by UI tests. */
        screenPositionOf(key) {
            const idx = keyToIndex.get(key);
            if (idx === undefined) return null;
            const o = objects[idx];
            const v = new THREE.Vector3(o.p[0], o.p[1] + 0.3, o.p[2]).project(camera);
            const rect = renderer.domElement.getBoundingClientRect();
            return { x: rect.left + ((v.x + 1) / 2) * rect.width, y: rect.top + ((1 - v.y) / 2) * rect.height, depth: v.z };
        },
        /** Name of the level piece under a page point, or null (diagnostics and tests). */
        levelNameAt(clientX, clientY) { return levelNameAt(clientX, clientY); },
        /** The level piece under a screen point: its name and actor ("Map:Actor"). Used by UI tests. */
        levelPieceAt(clientX, clientY) { return levelPieceAt(clientX, clientY); },
        /** The largest level pieces drawn (name, instances, radius in metres, texture), for diagnostics. */
        levelSummary(limit = 20) {
            return levelGroup.children
                .map(m => ({ name: m.name, count: m.count, radius: Math.round(m.boundingSphere?.radius ?? 0),
                    textured: (Array.isArray(m.material) ? m.material : [m.material]).map(x => !!x.map),
                    materials: (Array.isArray(m.material) ? m.material : [m.material]).map(x => ({
                        color: x.color?.getHexString(), transparent: x.transparent, vertexColors: x.vertexColors,
                        map: x.map?.image?.currentSrc?.split("/").pop() ?? x.map?.image?.src?.split("/").pop() ?? null })) }))
                .sort((a, b) => b.radius - a.radius)
                .slice(0, limit);
        },
        /** Reports what the view is drawing, for diagnostics and tests. */
        stats() {
            return {
                objects: objects.length,
                visible: visible.length,
                drawCalls: renderer.info.render.calls,
                instancedMeshes: meshes.filter(m => m.count > 0).length,
                selected: selectedKey,
                selectedCount: selectedKeys.size,
                deleted: objects.filter(o => o.mark === 1).length,
                copies: objects.filter(o => o.mark === 2).length,
                gizmo: gizmoMode,
                modelsOn,
                modelClasses: [...classModels.values()].filter(m => m.state === "ready").length,
                modelMeshes: modelMeshes.length,
                modelObjects: objInstances.size,
                levelMeshes: levelGroup.children.length,
                cables: cableGroup.children.reduce((n, c) => n + c.geometry.attributes.position.count / 2, 0),
                levelInstances,
                lamps: lampGroup.children.filter(c => c.isSprite).length,
                liveLamps: lampGroup.children.filter(c => c.isLight && c.intensity > 0).length,
                mergedCalls: mergedStats.calls,
                mergedPieces: mergedStats.pieces,
                doors: doorLayer.items.length,
                selectedDoor: doorLayer.selected,
                npcs: npcLayer.items.length,
                selectedNpc: npcLayer.selected,
                walk: walkOn,
            };
        },
        /** True when this view was kept from a previous visit (nothing to load again). */
        isReattached() { return reattached; },
        /** Moves the kept view into a new host element and reports to a new component. */
        reattach(newHost, newDotnet) {
            observer.unobserve(host);
            while (host.firstChild) newHost.appendChild(host.firstChild);
            host = newHost;
            dotnet = newDotnet;
            host.__base3d = api;
            observer.observe(host);
            reattached = true;
            resize();
            requestRender();
        },
        /** Keeps the view for a while (see PARK_MS) instead of disposing it; createView with the same key takes it back. */
        park(key) {
            if (!key) { api.dispose(); return; }
            if (walkOn) setWalk(false);
            observer.unobserve(host);
            const old = parked.get(key);
            if (old && old.api !== api) { clearTimeout(old.timer); old.api.dispose(); }
            parked.set(key, { api, timer: setTimeout(() => { parked.delete(key); api.dispose(); }, PARK_MS) });
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            window.removeEventListener("keydown", onWalkKeyDown);
            window.removeEventListener("keyup", onWalkKeyUp);
            window.removeEventListener("keydown", onFlyKeyDown);
            window.removeEventListener("keyup", onFlyKeyUp);
            clearTimeout(sharpTimer);
            clearLamps();
            glowTexture.dispose();
            for (const layer of Object.values(markerLayers)) layer.dispose();
            clearTimeout(levelRetry);
            observer.disconnect();
            transform.dispose();
            controls.dispose();
            disposeMeshes();
            disposeModelMeshes();
            clearLevel();
            for (const p of geometryCache.values()) p.then(g => g?.dispose());
            for (const t of textureCache.values()) t.dispose();
            for (const m of materialCache.values()) m.dispose();
            renderer.dispose();
            host.replaceChildren();
        },
    };
    host.__base3d = api; // test and diagnostics hook (read-only use)
    // The latest view, for UI tests driving the page (lookAt, screenPositionOf).
    globalThis.__abioticBase3d = api;
    return api;
}

/** Location context for generated live actors that have no fixed thumbnail name. */
export async function locationPicture(options) {
    const previousView = globalThis.__abioticBase3d;
    const host = document.createElement("div");
    host.style.cssText = "position:fixed;left:-10000px;top:0;width:400px;height:300px;pointer-events:none";
    document.body.appendChild(host);
    let view;
    try {
        view = createView(host, { invokeMethodAsync: async () => {} });
        for (let attempt = 0; attempt < 240; attempt++) {
            const result = await view.locationShot(options);
            if (result?.image || !result?.pending) return result;
            await new Promise(resolve => setTimeout(resolve, 250));
        }
        return null;
    } finally {
        view?.dispose(); host.remove();
        if (globalThis.__abioticBase3d === view) globalThis.__abioticBase3d = previousView;
    }
}
