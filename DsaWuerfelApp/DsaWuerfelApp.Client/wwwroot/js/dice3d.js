// Wir nutzen jetzt die Namen aus der importmap ("three" und "three/addons/...")
import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";

let renderer, scene, camera;
let diceMap = new Map();

export async function init(canvas) {

    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    renderer.setPixelRatio(devicePixelRatio);

    scene = new THREE.Scene();

    camera = new THREE.PerspectiveCamera(35, 1, 0.1, 100);
    camera.position.set(0, 1, 2.5);

    scene.add(new THREE.AmbientLight(0xffffff, 0.8));

    const light = new THREE.DirectionalLight(0xffffff, 1);
    light.position.set(2, 3, 4);
    scene.add(light);

    const loader = new GLTFLoader();

    // WICHTIG: Dateiname angepasst auf "dice_set.glb" (mit Unterstrich), 
    // falls deine Datei so heißt wie im Upload.
    const gltf = await loader.loadAsync("./models/dice_set.glb");

    scene.add(gltf.scene);

    gltf.scene.traverse(obj => {
        if (obj.isMesh && obj.name) {
            const name = obj.name.toLowerCase();
            // Optional: Sicherstellen, dass Materialien korrekt angezeigt werden
            if (obj.material) obj.material.side = THREE.DoubleSide;

            if (["d4","d6","d8","d10","d12","d20"].includes(name)) {
                diceMap.set(name, obj);
                obj.visible = false;
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

export function showDie(sides) {
    diceMap.forEach(d => d.visible = false);
    const key = "d" + sides;
    if (diceMap.has(key)) {
        diceMap.get(key).visible = true;
    }
}

export function roll(sides) {
    const key = "d" + sides;
    const die = diceMap.get(key);
    if (!die) return;

    showDie(sides);

    const start = performance.now();
    const duration = 700;

    // Start-Rotation (zufällig für Abwechslung)
    const startRot = { x: die.rotation.x, y: die.rotation.y, z: die.rotation.z };

    // Ziel-Rotation (simuliert "wildes Trudeln")
    const targetRot = {
        x: startRot.x + Math.PI * (4 + Math.random()),
        y: startRot.y + Math.PI * (5 + Math.random()),
        z: startRot.z + Math.PI * (3 + Math.random())
    };

    function spin(t) {
        const p = Math.min(1, (t - start) / duration);
        const ease = 1 - Math.pow(1 - p, 3); // Cubic Ease Out

        die.rotation.x = startRot.x + (targetRot.x - startRot.x) * ease;
        die.rotation.y = startRot.y + (targetRot.y - startRot.y) * ease;
        die.rotation.z = startRot.z + (targetRot.z - startRot.z) * ease;

        if (p < 1) requestAnimationFrame(spin);
    }

    requestAnimationFrame(spin);
}