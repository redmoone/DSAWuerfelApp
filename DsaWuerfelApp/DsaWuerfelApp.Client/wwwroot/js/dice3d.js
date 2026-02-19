import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";

let renderer, scene, camera;
let diceModels = new Map();
let activeDice = [];
let dotNetRef = null;

const raycaster = new THREE.Raycaster();
const mouse = new THREE.Vector2();

const SUPERSAMPLE = 1.5;
const DICE_SCALE = 1.25;
const USE_EDGE_OUTLINE = false;

const diceRotations = {
    "d4": {
        1: { x: -1.343, y: -0.029, z: 0.020 },
        2: { x: -1.883, y: -0.155, z: -2.084 },
        3: { x: -0.170, y: -0.003, z: -3.131 },
        4: { x: -1.810, y: 0.215, z: 2.113 }
    },
    "d6": {
        1: { x: 3.106, y: -0.002, z: 0.004 },
        2: { x: -2.846, y: -1.559, z: 1.872 },
        3: { x: 1.602, y: 0.001, z: -3.137 },
        4: { x: -1.569, y: 0.001, z: -3.137 },
        5: { x: 2.299, y: 1.566, z: -0.693 },
        6: { x: 0.000, y: 0.000, z: 0.000 }
    },
    "d8": {
        1: { x: -0.927, y: -0.771, z: -3.127 },
        2: { x: 2.262, y: 0.786, z: -0.016 },
        3: { x: 2.276, y: 0.781, z: 3.122 },
        4: { x: -0.971, y: -0.803, z: -0.022 },
        5: { x: 2.181, y: -0.790, z: -0.004 },
        6: { x: -0.950, y: 0.782, z: -3.129 },
        7: { x: -0.942, y: 0.786, z: -0.016 },
        8: { x: 2.202, y: -0.801, z: 3.096 }
    },
    "d10": {
        1: { x: -2.857, y: -1.540, z: 1.247 },
        2: { x: 2.267, y: 0.944, z: -0.088 },
        3: { x: 2.244, y: -0.378, z: 0.030 },
        4: { x: 2.030, y: 0.912, z: -3.009 },
        5: { x: -1.044, y: -0.273, z: 3.114 },
        6: { x: 2.105, y: -0.301, z: 3.138 },
        7: { x: -1.046, y: 0.869, z: -3.140 },
        8: { x: -0.902, y: -0.338, z: 0.024 },
        9: { x: -0.845, y: 0.944, z: -0.088 },
        10: { x: 0.183, y: -1.540, z: 1.247 }
    },
    "d12": {
        1: { x: 0.544, y: -0.521, z: -2.783 },
        2: { x: -2.591, y: 0.557, z: -0.936 },
        3: { x: -1.034, y: -1.008, z: -2.155 },
        4: { x: -1.578, y: 0.023, z: -1.883 },
        5: { x: -0.515, y: -0.023, z: 1.261 },
        6: { x: -2.590, y: 0.560, z: 0.332 },
        7: { x: 2.176, y: 1.038, z: -1.624 },
        8: { x: 2.663, y: -0.023, z: 1.261 },
        9: { x: 1.593, y: 0.005, z: -1.871 },
        10: { x: 0.581, y: -0.523, z: 2.212 },
        11: { x: 2.018, y: -1.037, z: 1.522 },
        12: { x: -1.052, y: 1.011, z: 0.978 }
    },
    "d20": {
        1: { x: -0.662, y: 0.294, z: -3.116 },
        2: { x: -3.114, y: -0.511, z: -0.658 },
        3: { x: 1.378, y: 0.943, z: -3.106 },
        4: { x: -0.716, y: -0.933, z: 0.003 },
        5: { x: -3.079, y: 0.486, z: 1.712 },
        6: { x: -1.224, y: -0.020, z: 2.120 },
        7: { x: -1.708, y: -0.920, z: -2.014 },
        8: { x: 2.448, y: -0.920, z: 3.121 },
        9: { x: 1.488, y: 0.921, z: -0.031 },
        10: { x: -1.893, y: 0.333, z: 1.119 },
        11: { x: 1.356, y: -0.298, z: -3.113 },
        12: { x: -0.688, y: -0.331, z: -2.012 },
        13: { x: -0.698, y: -0.931, z: 3.120 },
        14: { x: 0.651, y: 0.507, z: 2.478 },
        15: { x: 1.456, y: -0.372, z: -0.013 },
        16: { x: 3.132, y: 0.530, z: -1.367 },
        17: { x: 0.360, y: -0.001, z: -2.581 },
        18: { x: -2.407, y: -0.563, z: -1.403 },
        19: { x: 1.881, y: 1.554, z: -2.644 },
        20: { x: 1.332, y: -0.968, z: 1.109 }
    }
};



export function setDotNetRef(ref) {
    dotNetRef = ref;
}

export async function init(canvas) {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    renderer.setClearColor(0x000000, 0);

    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.1;

    renderer.setPixelRatio(Math.min(3, window.devicePixelRatio * SUPERSAMPLE));

    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;

    scene = new THREE.Scene();

    camera = new THREE.PerspectiveCamera(35, 1, 0.1, 500);
    camera.position.set(0, 16, 0);
    camera.lookAt(0, 0, 0);

    scene.add(new THREE.AmbientLight(0xffffff, 0.18));

    const key = new THREE.DirectionalLight(0xffffff, 1.1);
    key.position.set(6, 12, 8);
    key.castShadow = true;
    key.shadow.mapSize.set(2048, 2048);
    key.shadow.bias = -0.00008;
    key.shadow.camera.near = 1;
    key.shadow.camera.far = 60;
    key.shadow.camera.left = -25;
    key.shadow.camera.right = 25;
    key.shadow.camera.top = 25;
    key.shadow.camera.bottom = -25;
    scene.add(key);

    const fill = new THREE.DirectionalLight(0xffffff, 0.35);
    fill.position.set(-8, 6, -6);
    scene.add(fill);

    const rim = new THREE.DirectionalLight(0xffffff, 0.75);
    rim.position.set(0, 8, -14);
    scene.add(rim);

    const ground = new THREE.Mesh(
        new THREE.PlaneGeometry(200, 200),
        new THREE.ShadowMaterial({ opacity: 0.22 })
    );
    ground.rotation.x = -Math.PI / 2;
    ground.position.y = -0.01;
    ground.receiveShadow = true;
    scene.add(ground);

    const loader = new GLTFLoader();
    const gltf = await loader.loadAsync("./models/dice_set.glb");

    const maxAnisotropy = renderer.capabilities.getMaxAnisotropy();

    gltf.scene.traverse((obj) => {
        if (!obj.isMesh) return;

        const materials = Array.isArray(obj.material) ? obj.material : [obj.material];
        for (const mat of materials) {
            if (!mat) continue;
            if (mat.map) {
                const map = mat.map;
                map.colorSpace = THREE.SRGBColorSpace;
                map.anisotropy = maxAnisotropy;
                map.generateMipmaps = true;
                map.minFilter = THREE.LinearMipmapLinearFilter;
                map.magFilter = THREE.LinearFilter;
                map.needsUpdate = true;
                mat.needsUpdate = true;
            }
        }

        obj.castShadow = true;
        obj.receiveShadow = false;

        if (obj.name) {
            const name = obj.name.toLowerCase();
            if (["d4", "d6", "d8", "d10", "d12", "d20"].includes(name)) {
                obj.geometry.center();
                diceModels.set(name, obj);
            }
        }
    });

    canvas.addEventListener('click', (event) => {
        const rect = canvas.getBoundingClientRect();
        mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
        mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;

        raycaster.setFromCamera(mouse, camera);

        const intersects = raycaster.intersectObjects(activeDice, true);

        if (intersects.length > 0) {
            let clickedObject = intersects[0].object;

            while(clickedObject && !activeDice.includes(clickedObject) && clickedObject.parent !== scene) {
                clickedObject = clickedObject.parent;
            }

            if (activeDice.includes(clickedObject)) {
                const idx = clickedObject.userData.diceIndex;
                if (dotNetRef && idx !== undefined) {
                    dotNetRef.invokeMethodAsync('OnDiceRemovedCallback', idx);
                }
            }
        }
    });

    resize(canvas);
    window.addEventListener("resize", () => resize(canvas));
    animate(canvas);
}

function resize(canvas) {
    const w = canvas.clientWidth;
    const h = canvas.clientHeight;

    const pixelRatio = renderer.getPixelRatio();
    const targetW = Math.floor(w * pixelRatio);
    const targetH = Math.floor(h * pixelRatio);

    if (canvas.width !== targetW || canvas.height !== targetH) {
        renderer.setSize(w, h, false);
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
    }
}

function animate(canvas) {
    requestAnimationFrame(() => animate(canvas));
    resize(canvas);
    renderer.render(scene, camera);
}

function getPosition(index, total) {
    const spread = 3.5;
    const rowSize = 5;

    const col = index % rowSize;
    const row = Math.floor(index / rowSize);

    const itemsInThisRow = Math.min(total - row * rowSize, rowSize);
    const xOffset = ((itemsInThisRow - 1) * spread) / 2;

    const x = col * spread - xOffset;
    const z = row * spread - ((Math.ceil(total / rowSize) - 1) * spread) / 2;

    return { x, y: 0, z };
}

function addEdgeOverlay(root) {
    root.traverse((o) => {
        if (!o.isMesh) return;

        const edges = new THREE.EdgesGeometry(o.geometry, 25);
        const lines = new THREE.LineSegments(
            edges,
            new THREE.LineBasicMaterial({
                color: 0x000000,
                transparent: true,
                opacity: 0.35,
            })
        );
        lines.renderOrder = 999;
        o.add(lines);
    });
}

export function updateDice(sidesArray) {
    activeDice.forEach((mesh) => scene.remove(mesh));
    activeDice = [];

    sidesArray.forEach((sides, index) => {
        const key = "d" + sides;
        const template = diceModels.get(key);
        if (!template) return;

        const mesh = template.clone(true);
        mesh.userData = { diceIndex: index, sides: sides };

        const pos = getPosition(index, sidesArray.length);
        mesh.position.set(pos.x, pos.y, pos.z);

        mesh.scale.setScalar(DICE_SCALE);

        let targetRot = { x: 0, y: 0, z: 0 };
        if (diceRotations[key] && diceRotations[key][sides]) {
            targetRot = diceRotations[key][sides];
        }

        mesh.rotation.set(targetRot.x, targetRot.y, targetRot.z);

        mesh.traverse((o) => {
            if (o.isMesh) {
                o.castShadow = true;
                o.receiveShadow = false;
            }
        });

        if (USE_EDGE_OUTLINE) addEdgeOverlay(mesh);

        scene.add(mesh);
        activeDice.push(mesh);
    });
}

export function rollDice(resultsArray) {
    const start = performance.now();
    const duration = 1000;

    const animations = activeDice.map((mesh, index) => {
        const sides = mesh.userData.sides;
        const rolledValue = resultsArray[index];

        let faceTarget = { x: 0, y: 0, z: 0 };

        if (diceRotations["d" + sides] && diceRotations["d" + sides][rolledValue]) {
            faceTarget = diceRotations["d" + sides][rolledValue];
        }

        const randomSpinsX = Math.floor(Math.random() * 3) + 2;
        const randomSpinsY = Math.floor(Math.random() * 3) + 2;
        const randomSpinsZ = Math.floor(Math.random() * 3) + 2;

        return {
            mesh,
            start: { x: mesh.rotation.x, y: mesh.rotation.y, z: mesh.rotation.z },
            target: {
                x: faceTarget.x + (Math.PI * 2 * randomSpinsX),
                y: faceTarget.y + (Math.PI * 2 * randomSpinsY),
                z: faceTarget.z + (Math.PI * 2 * randomSpinsZ)
            }
        };
    });

    function loop(t) {
        const p = Math.min(1, (t - start) / duration);
        const ease = 1 - Math.pow(1 - p, 3);

        for (const a of animations) {
            a.mesh.rotation.x = a.start.x + (a.target.x - a.start.x) * ease;
            a.mesh.rotation.y = a.start.y + (a.target.y - a.start.y) * ease;
            a.mesh.rotation.z = a.start.z + (a.target.z - a.start.z) * ease;
        }

        if (p < 1) requestAnimationFrame(loop);
    }

    requestAnimationFrame(loop);
}