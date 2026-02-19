import * as THREE from "three";
import {GLTFLoader} from "three/addons/loaders/GLTFLoader.js";
import {SUPERSAMPLE, DICE_SCALE, USE_EDGE_OUTLINE, diceRotations} from "./dice-constants.js";
import {initScene, resizeScene} from "./dice-scene.js";

let renderer, scene, camera;
let diceModels = new Map();
let activeDice = [];
let dotNetRef = null;

const raycaster = new THREE.Raycaster();
const mouse = new THREE.Vector2();

export function setDotNetRef(ref) {
    dotNetRef = ref;
}

export async function init(canvas) {
    const setup = initScene(canvas, SUPERSAMPLE);
    renderer = setup.renderer;
    scene = setup.scene;
    camera = setup.camera;

    const loader = new GLTFLoader();
    const gltf = await loader.loadAsync("./models/dice_set.glb");

    const maxAnisotropy = renderer.capabilities.getMaxAnisotropy();

    gltf.scene.traverse((obj) => {
        if (!obj.isMesh) return;

        const materials = Array.isArray(obj.material) ? obj.material : [obj.material];
        for (const mat of materials) {
            if (!mat) continue;
            if (mat.map) {
                mat.map.colorSpace = THREE.SRGBColorSpace;
                mat.map.anisotropy = maxAnisotropy;
                mat.map.generateMipmaps = true;
                mat.map.minFilter = THREE.LinearMipmapLinearFilter;
                mat.map.magFilter = THREE.LinearFilter;
                mat.map.needsUpdate = true;
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

            while (clickedObject && !activeDice.includes(clickedObject) && clickedObject.parent !== scene) {
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

    resizeScene(canvas, renderer, camera);
    window.addEventListener("resize", () => resizeScene(canvas, renderer, camera));
    animate(canvas);
}

function animate(canvas) {
    requestAnimationFrame(() => animate(canvas));
    resizeScene(canvas, renderer, camera);
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

    return {x, y: 0, z};
}

function addEdgeOverlay(root) {
    root.traverse((o) => {
        if (!o.isMesh) return;

        const edges = new THREE.EdgesGeometry(o.geometry, 25);
        const lines = new THREE.LineSegments(edges, new THREE.LineBasicMaterial({
            color: 0x000000, transparent: true, opacity: 0.35,
        }));
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
        mesh.userData = {diceIndex: index, sides: sides};

        const pos = getPosition(index, sidesArray.length);
        mesh.position.set(pos.x, pos.y, pos.z);

        mesh.scale.setScalar(DICE_SCALE);

        let targetRot = {x: 0, y: 0, z: 0};
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

        let faceTarget = {x: 0, y: 0, z: 0};

        if (diceRotations["d" + sides] && diceRotations["d" + sides][rolledValue]) {
            faceTarget = diceRotations["d" + sides][rolledValue];
        }

        const randomSpinsX = Math.floor(Math.random() * 3) + 2;
        const randomSpinsY = Math.floor(Math.random() * 3) + 2;
        const randomSpinsZ = Math.floor(Math.random() * 3) + 2;

        return {
            mesh, start: {x: mesh.rotation.x, y: mesh.rotation.y, z: mesh.rotation.z}, target: {
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