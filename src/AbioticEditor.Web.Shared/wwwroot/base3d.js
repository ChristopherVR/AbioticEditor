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
    const wide = (view.getUint32(16, true) & 1) === 1;
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
    scene.add(new THREE.HemisphereLight(0xffffff, 0x334455, 1.6));
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
        const d = camera.position.distanceTo(controls.target);
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

    function materialFor(m, level) {
        const key = `${level ? "L" : "O"}|${m.texture}|${m.color}|${m.opacity}|${m.twoSided}|${m.masked}|${m.emissive}`;
        let material = materialCache.get(key);
        if (!material) {
            const params = {
                color: new THREE.Color().setRGB(m.color[0], m.color[1], m.color[2]),
                side: m.twoSided ? THREE.DoubleSide : THREE.FrontSide,
            };
            if (m.texture) params.map = textureFor(m.texture);
            if (m.opacity < 1) Object.assign(params, { transparent: true, opacity: m.opacity, depthWrite: false });
            if (m.masked) params.alphaTest = 0.5;
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
    }

    /** Height (viewer Y) above which the level is cut away, so ceilings and upper floors do not hide the base. */
    function updateLevelCut() {
        if (levelOptions.cutAbove === null || levelOptions.cutAbove === undefined || levelOptions.cutAbove <= 0) {
            levelClip.constant = 1e6;
        } else {
            levelClip.constant = baseFloor(levelCentre ?? controls.target) + levelOptions.cutAbove;
        }
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
            const materials = batch.materials.length ? batch.materials.map(m => materialFor(m, true)) : [materialFor({ color: [0.6, 0.6, 0.6], opacity: 1 }, true)];
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
                .map(m => ({ name: m.name, count: m.count, radius: Math.round(m.boundingSphere?.radius ?? 0), textured: (Array.isArray(m.material) ? m.material : [m.material]).map(x => !!x.map) }))
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
            };
        },
        dispose() {
            disposed = true;
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
    return api;
}
