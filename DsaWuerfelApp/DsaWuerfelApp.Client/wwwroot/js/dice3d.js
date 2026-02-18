import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";

let renderer, scene, camera;
let diceModels = new Map();
let activeDice = [];

export async function init(canvas) {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    renderer.setPixelRatio(devicePixelRatio);
    renderer.setClearColor(0x000000, 0);

    scene = new THREE.Scene();

    // SETUP: Top-Down Orthographic-style view
    // Position camera high up on Y axis, looking straight down at 0,0,0
    camera = new THREE.PerspectiveCamera(30, 1, 1, 100);
    camera.position.set(0, 20, 0);
    camera.lookAt(0, 0, 0);

    // Light coming from top-left to cast nice shadows on the "floor"
    scene.add(new THREE.AmbientLight(0xffffff, 0.7));
    const dirLight = new THREE.DirectionalLight(0xffffff, 0.8);
    dirLight.position.set(-5, 10, 5);
    scene.add(dirLight);

    const loader = new GLTFLoader();
    const gltf = await loader.loadAsync("./models/dice_set.glb");

    gltf.scene.traverse(obj => {
        if (obj.isMesh && obj.name) {
            const name = obj.name.toLowerCase();
            if (["d4","d6","d8","d10","d12","d20"].includes(name)) {
                obj.geometry.center();
                diceModels.set(name, obj);
            }
        }
    });

    resize(canvas);
    window.addEventListener("resize", () => resize(canvas));
    animate();
}

function resize(canvas) {
    const w = canvas.clientWidth;
    const h = canvas.clientHeight;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
}

function animate() {
    requestAnimationFrame(animate);
    renderer.render(scene, camera);
}

function getPosition(index, total) {
    // INCREASED SPACING:
    // "spread" determines how far apart dice are.
    const spread = 3.5;
    const rowSize = 5;

    // Calculate Grid Positions (X, Z) looking from top
    // Centering logic: (index % cols) - (totalWidth / 2)

    const col = index % rowSize;
    const row = Math.floor(index / rowSize);

    // Center the row based on how many items are ACTUALLY in this row
    const itemsInThisRow = Math.min(total - (row * rowSize), rowSize);
    const xOffset = (itemsInThisRow - 1) * spread / 2;

    const x = (col * spread) - xOffset;
    const z = (row * spread) - ((Math.ceil(total/rowSize)-1) * spread / 2);

    return { x, y: 0, z };
}

export function updateDice(sidesArray) {
    activeDice.forEach(mesh => scene.remove(mesh));
    activeDice = [];

    sidesArray.forEach((sides, index) => {
        const key = "d" + sides;
        const template = diceModels.get(key);
        if (!template) return;

        const mesh = template.clone();
        const pos = getPosition(index, sidesArray.length);

        mesh.position.set(pos.x, pos.y, pos.z);

        // Random rotation on Y axis (spinning on the table), 
        // but keep X/Z relatively flat so we can read numbers from top view
        mesh.rotation.set(
            Math.random() * 0.5,       // Slight tilt X
            Math.random() * Math.PI * 2, // Full rotation Y
            Math.random() * 0.5        // Slight tilt Z
        );

        scene.add(mesh);
        activeDice.push(mesh);
    });
}

export function rollDice() {
    const start = performance.now();
    const duration = 600;

    const animations = activeDice.map(mesh => {
        return {
            mesh: mesh,
            start: { x: mesh.rotation.x, y: mesh.rotation.y, z: mesh.rotation.z },
            target: {
                // Spin primarily on Y (like a top) + some tumbling
                x: mesh.rotation.x + Math.PI * 2,
                y: mesh.rotation.y + Math.PI * 4 + (Math.random() * Math.PI),
                z: mesh.rotation.z + Math.PI * 2
            }
        };
    });

    function loop(t) {
        const p = Math.min(1, (t - start) / duration);
        const ease = 1 - Math.pow(1 - p, 3);

        animations.forEach(a => {
            a.mesh.rotation.x = a.start.x + (a.target.x - a.start.x) * ease;
            a.mesh.rotation.y = a.start.y + (a.target.y - a.start.y) * ease;
            a.mesh.rotation.z = a.start.z + (a.target.z - a.start.z) * ease;
        });

        if (p < 1) requestAnimationFrame(loop);
    }
    requestAnimationFrame(loop);
}