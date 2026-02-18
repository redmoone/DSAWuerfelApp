import * as THREE from "https://unpkg.com/three@0.160.0/build/three.module.js";
import { GLTFLoader } from "https://unpkg.com/three@0.160.0/examples/jsm/loaders/GLTFLoader.js";

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
    const gltf = await loader.loadAsync("./models/dice-set.glb");

    scene.add(gltf.scene);

    gltf.scene.traverse(obj => {
        if (obj.isMesh && obj.name) {
            const name = obj.name.toLowerCase();
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

    function spin(t) {
        const p = Math.min(1, (t - start) / duration);
        const ease = 1 - Math.pow(1 - p, 3);

        die.rotation.x = ease * Math.PI * 4;
        die.rotation.y = ease * Math.PI * 5;
        die.rotation.z = ease * Math.PI * 3;

        if (p < 1) requestAnimationFrame(spin);
    }

    requestAnimationFrame(spin);
}
