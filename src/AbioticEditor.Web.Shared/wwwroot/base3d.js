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
const CLASS_BATCH = 120;
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

export function createView(host, dotnet) {
    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.localClippingEnabled = true; // the level's ceiling cut
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
    controls.enableDamping = false;
    controls.screenSpacePanning = true;
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

    function createMarkerLayer(texture, size) {
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
                    pos.set([d.p[0], d.p[1] + MARKER_LIFT_M, d.p[2]], i * 3);
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
                    sg.setAttribute("position", new THREE.BufferAttribute(new Float32Array([sel.p[0], sel.p[1] + MARKER_LIFT_M, sel.p[2]]), 3));
                    const ring = new THREE.Points(sg, ringMaterial);
                    ring.renderOrder = 11; // under the coloured marker, so it reads as a white outline
                    ring.frustumCulled = false;
                    group.add(ring);
                }
            },
            /** Screen position (CSS pixels relative to the page) of a marker, or null. */
            screen(d) {
                const v = new THREE.Vector3(d.p[0], d.p[1] + MARKER_LIFT_M, d.p[2]).project(camera);
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
    const MAX_LIVE_LAMPS = 12;
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

    function clearLamps() {
        for (const child of [...lampGroup.children]) {
            lampGroup.remove(child);
            if (child.isSprite) child.material.dispose();
            if (child.isLight) child.dispose();
        }
    }

    function setLamps(list) {
        clearLamps();
        const lamps = list ?? [];
        lamps.forEach((l, i) => {
            const colour = new THREE.Color(l.color[0], l.color[1], l.color[2]);
            const glow = new THREE.Sprite(new THREE.SpriteMaterial({ map: glowTexture, color: colour, blending: THREE.AdditiveBlending,
                depthWrite: false, transparent: true, opacity: Math.min(0.9, 0.35 + 0.15 * l.brightness) }));
            glow.position.set(l.position[0], l.position[1], l.position[2]);
            const size = Math.min(1.6, 0.35 + 0.25 * Math.sqrt(Math.max(0, l.brightness)));
            glow.scale.set(size, size, 1);
            glow.userData.lamp = true;
            lampGroup.add(glow);
            if (i >= MAX_LIVE_LAMPS) return;
            const intensity = Math.min(20, LAMP_CANDELA * Math.max(0, l.brightness));
            const range = Math.max(2, l.rangeMetres || 10);
            let light;
            if (l.direction) {
                const cone = l.coneDegrees ?? 75;
                light = new THREE.SpotLight(colour, intensity, range, THREE.MathUtils.degToRad(Math.min(85, Math.max(5, cone))), 0.5, 2);
                light.target.position.set(l.position[0] + l.direction[0], l.position[1] + l.direction[1], l.position[2] + l.direction[2]);
                lampGroup.add(light.target);
            } else {
                light = new THREE.PointLight(colour, intensity, range, 2);
            }
            light.position.set(l.position[0], l.position[1], l.position[2]);
            lampGroup.add(light);
        });
        lampGroup.visible = lampsOn;
        // Lit by its own lamps, the level needs less of the flat fill light.
        ambient.intensity = lampsOn && lamps.length ? 1.35 : 1.6;
        updateLevelCut();
    }

    // Level geometry around the camera target (context only: never picked or edited).
    const levelGroup = new THREE.Group();
    scene.add(levelGroup);
    const levelClip = new THREE.Plane(new THREE.Vector3(0, -1, 0), 1e6);
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

    function frame() {
        if (disposed) return;
        dirty = false;
        updateClipping();
        renderer.render(scene, camera);
        renderLabels();
    }

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
    }

    /** One InstancedMesh per part of every class that has a ready model and a visible object. */
    function rebuildModelInstances() {
        disposeModelMeshes();
        if (!modelsOn) return;
        const byClass = new Map();
        for (const idx of visible) {
            const o = objects[idx];
            if (!readyModel(o)) continue;
            const key = modelKey(o);
            if (!byClass.has(key)) byClass.set(key, []);
            byClass.get(key).push(idx);
        }
        const base = new THREE.Matrix4();
        const m = new THREE.Matrix4();
        for (const [cls, list] of byClass) {
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
                scene.add(mesh);
                modelMeshes.push(mesh);
            }
        }
    }

    function rebuildInstances() {
        const perCat = CATEGORY_COLORS.map(() => []);
        for (const idx of visible) {
            if (!readyModel(objects[idx])) perCat[objects[idx].cat].push(idx);
        }
        rebuildModelInstances();
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
        rebuildDots();
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

    function textureFor(id) {
        let texture = textureCache.get(id);
        if (!texture) {
            texture = new THREE.TextureLoader().load(assetUrl(id), requestRender);
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
            if (!disposed) {
                rebuildInstances();
                updateSelectionBox();
            }
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
        for (let i = 0; i < wanted.length; i += CLASS_BATCH) {
            const chunk = wanted.slice(i, i + CLASS_BATCH);
            let answer;
            try {
                answer = await postJson(`${MODEL_BASE}/classes`, chunk) ?? {};
            } catch {
                for (const cls of chunk) classModels.set(cls, { state: "none" });
                done += chunk.length;
                report("models", done, wanted.length, "error");
                continue;
            }
            await Promise.all(chunk.map(async cls => {
                const description = answer[cls];
                if (description && description.parts?.length) await buildClassModel(cls, description);
                else classModels.set(cls, { state: "none" });
                done++;
                if (done % 10 === 0 || done === wanted.length) report("models", done, wanted.length);
                scheduleModelRebuild();
            }));
        }
        report("models", wanted.length, wanted.length);
    }

    // ---- level geometry -------------------------------------------------------------------------
    function clearLevel() {
        for (const child of [...levelGroup.children]) {
            levelGroup.remove(child);
            child.dispose();
        }
        levelInstances = 0;
        clearLamps();
        ambient.intensity = 1.6;
    }

    /** Height (viewer Y) above which the level is cut away, so ceilings and upper floors do not hide the base. */
    function updateLevelCut() {
        if (levelOptions.cutAbove === null || levelOptions.cutAbove === undefined || levelOptions.cutAbove <= 0) {
            levelClip.constant = 1e6;
        } else {
            levelClip.constant = baseFloor(levelCentre ?? controls.target) + levelOptions.cutAbove;
        }
        // A lamp's glow above the cut would float where its (cut away) fixture was; its light still falls below.
        for (const child of lampGroup.children) if (child.isSprite) child.visible = child.position.y <= levelClip.constant;
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
        const built = [];
        let loaded = 0;
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
            const mesh = new THREE.InstancedMesh(geometry, materials, count);
            const m = new THREE.Matrix4();
            for (let i = 0; i < count; i++) mesh.setMatrixAt(i, m.fromArray(batch.matrices, i * 16));
            mesh.instanceMatrix.needsUpdate = true;
            mesh.computeBoundingSphere();
            mesh.name = batch.name ?? "";
            built.push(mesh);
        }));
        if (token !== levelToken || disposed) {
            for (const mesh of built) mesh.dispose();
            return;
        }
        clearLevel();
        for (const mesh of built) {
            levelGroup.add(mesh);
            levelInstances += mesh.count;
        }
        setLamps(slice.lights);
        if (grid) grid.visible = built.length === 0;
        updateLevelCut();
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
        for (const i of list) box.expandByPoint(tmpPos.set(objects[i].p[0], objects[i].p[1], objects[i].p[2]));
        const center = box.getCenter(new THREE.Vector3());
        const radius = Math.max(box.getSize(new THREE.Vector3()).length() / 2, 5);
        const dir = camera.position.clone().sub(controls.target);
        if (dir.lengthSq() < 1e-6) dir.set(1, 0.8, 1);
        dir.normalize();
        if (dir.y < 0.25) dir.y = 0.25;
        dir.normalize();
        const dist = radius / Math.sin((camera.fov * Math.PI) / 360) * 1.15;
        controls.target.copy(center);
        camera.position.copy(center).addScaledVector(dir, dist);
        controls.update();
        requestRender();
    }

    // ---- labels ------------------------------------------------------------------------------
    const labelPool = [];
    function renderLabels() {
        if (!labelsOn) {
            for (const el of labelPool) el.style.display = "none";
            return;
        }
        const w = host.clientWidth, h = host.clientHeight;
        const camPos = camera.position;
        const items = [];
        for (const idx of visible) {
            const o = objects[idx];
            const dx = o.p[0] - camPos.x, dy = o.p[1] - camPos.y, dz = o.p[2] - camPos.z;
            items.push([dx * dx + dy * dy + dz * dz, idx]);
        }
        items.sort((a, b) => a[0] - b[0]);
        let used = 0;
        const v = new THREE.Vector3();
        for (const [, idx] of items) {
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
            el.textContent = o.label;
            el.style.display = "block";
            el.style.transform = `translate(${((v.x + 1) / 2) * w}px, ${((1 - v.y) / 2) * h}px) translate(-50%, -100%)`;
            el.classList.toggle("selected", selectedKeys.has(o.key));
            used++;
        }
        for (let i = used; i < labelPool.length; i++) labelPool[i].style.display = "none";
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

    /** The height of the floor under a viewer-space point (level, models and boxes), or null. */
    function floorBelow(at) {
        raycaster.set(new THREE.Vector3(at.x, at.y + 0.5, at.z), new THREE.Vector3(0, -1, 0));
        raycaster.far = 40;
        const targets = [...levelGroup.children, ...modelMeshes, ...meshes.filter(m => m.count > 0)];
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
        const targets = [...levelGroup.children, ...modelMeshes];
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
        controls.enabled = !on;
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

    function walkTick(now) {
        if (!walkOn || disposed || walkKeys.size === 0) { walkLast = 0; return; }
        const dt = walkLast ? Math.min(0.1, (now - walkLast) / 1000) : 0;
        walkLast = now;
        const fast = walkKeys.has("shift") ? 3 : 1;
        const speed = 4 * fast * dt;
        const flat = new THREE.Vector3(-Math.sin(walkYaw), 0, -Math.cos(walkYaw));
        const right = new THREE.Vector3(Math.cos(walkYaw), 0, -Math.sin(walkYaw));
        const move = new THREE.Vector3();
        if (walkKeys.has("w") || walkKeys.has("arrowup")) move.add(walkFloor ? flat : walkForward());
        if (walkKeys.has("s") || walkKeys.has("arrowdown")) move.sub(walkFloor ? flat : walkForward());
        if (walkKeys.has("d") || walkKeys.has("arrowright")) move.add(right);
        if (walkKeys.has("a") || walkKeys.has("arrowleft")) move.sub(right);
        if (!walkFloor && (walkKeys.has("e") || walkKeys.has(" "))) move.y += 1;
        if (!walkFloor && (walkKeys.has("q") || walkKeys.has("c"))) move.y -= 1;
        if (move.lengthSq() > 0) {
            move.normalize().multiplyScalar(speed);
            // On the floor the walker is solid: walls, level pieces and placed objects stop it, and
            // it slides along them. Flying passes through everything.
            if (walkFloor) collide(move);
            camera.position.add(move);
        }
        if (walkFloor && now - walkFloorCheck > 90) {
            walkFloorCheck = now;
            // Steps and ramps are climbed (up to knee height); a drop is followed down.
            const ground = floorBelow(new THREE.Vector3(camera.position.x, camera.position.y - EYE_M + 0.6, camera.position.z));
            if (ground !== null) camera.position.y = ground + EYE_M;
        }
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
    renderer.domElement.addEventListener("pointermove", e => {
        if (!walkOn || !walkLookFrom) return;
        walkYaw -= (e.clientX - walkLookFrom[0]) * 0.005;
        walkPitch = Math.max(-1.4, Math.min(1.4, walkPitch - (e.clientY - walkLookFrom[1]) * 0.005));
        walkLookFrom = [e.clientX, e.clientY];
        applyWalkCamera();
    });
    renderer.domElement.addEventListener("pointerdown", e => { if (walkOn) walkLookFrom = [e.clientX, e.clientY]; });
    window.addEventListener("pointerup", () => { walkLookFrom = null; });

    /** The name of the level piece under a screen point (visible side of the ceiling cut), or null. */
    function levelNameAt(clientX, clientY) {
        if (levelGroup.children.length === 0) return null;
        const rect = renderer.domElement.getBoundingClientRect();
        pointer.set(((clientX - rect.left) / rect.width) * 2 - 1, -(((clientY - rect.top) / rect.height) * 2 - 1));
        raycaster.setFromCamera(pointer, camera);
        for (const hit of raycaster.intersectObjects(levelGroup.children, false)) {
            if (levelClip.distanceToPoint(hit.point) >= 0) return hit.object.name || null;
        }
        return null;
    }

    renderer.domElement.addEventListener("pointerdown", e => { downAt = [e.clientX, e.clientY]; });
    renderer.domElement.addEventListener("pointerup", e => {
        if (!downAt || transform.dragging || transform.axis) { downAt = null; return; }
        const moved = Math.hypot(e.clientX - downAt[0], e.clientY - downAt[1]);
        downAt = null;
        if (moved > 4) return;
        const door = doorLayer.at(e.clientX, e.clientY), npc = npcLayer.at(e.clientX, e.clientY);
        if (door || npc) {
            if (npc && (!door || npc.dist < door.dist)) dotnet.invokeMethodAsync("OnNpcPicked", npc.item.id).catch(() => { });
            else dotnet.invokeMethodAsync("OnDoorPicked", door.item.id).catch(() => { });
            return;
        }
        const hit = pick(e.clientX, e.clientY);
        const additive = e.ctrlKey || e.shiftKey || e.metaKey;
        // Level pieces are reference only: a click on one names it, and never selects anything.
        dotnet.invokeMethodAsync("OnLevelPicked", hit ? null : levelNameAt(e.clientX, e.clientY)).catch(() => { });
        if (additive && !hit) return; // a modified click on nothing keeps the selection
        // C# owns the selection (it also drives the list and the inspector) and pushes it back.
        dotnet.invokeMethodAsync("OnPicked", hit ? hit.key : null, additive);
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
    controls.addEventListener("change", requestRender);
    resize();
    requestRender();

    const api = {
        /** Replaces every object. Each is {key, cat, p:[x,y,z], q:[x,y,z,w], s:[x,y,z], built, label, cls?, paint?, variant?}. */
        setScene(list) {
            objects = list;
            keyToIndex = new Map(list.map((o, i) => [o.key, i]));
            buildMeshes();
            updateGrid();
            visible = list.map((_, i) => i);
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
            lampGroup.visible = lampsOn;
            ambient.intensity = lampsOn && lampGroup.children.some(c => c.isLight) ? 1.35 : 1.6;
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
        /** Where the camera is and looks (viewer space), for tests and the place tool. */
        cameraState() {
            return { position: camera.position.toArray(), target: controls.target.toArray(), walk: walkOn };
        },
        select(key) { select(key); },
        /** Replaces the whole selection: every selected key, and which one is primary (gizmo and inspector). */
        setSelection(keys, primary) { setSelection(keys, primary); },
        setLabels(on) { labelsOn = !!on; requestRender(); },
        /** mode: null | "translate" | "rotate". */
        setGizmo(mode) { gizmoMode = mode; attachGizmo(); requestRender(); },
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
                liveLamps: lampGroup.children.filter(c => c.isLight).length,
                doors: doorLayer.items.length,
                selectedDoor: doorLayer.selected,
                npcs: npcLayer.items.length,
                selectedNpc: npcLayer.selected,
                walk: walkOn,
            };
        },
        dispose() {
            disposed = true;
            window.removeEventListener("keydown", onWalkKeyDown);
            window.removeEventListener("keyup", onWalkKeyUp);
            clearLamps();
            glowTexture.dispose();
            doorLayer.dispose();
            npcLayer.dispose();
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
