import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";

let renderer, scene, camera;
let diceMap = new Map();
let rootGroup; // Container für das Zentrieren

export async function init(canvas) {

    // 1. Renderer Setup (wieder transparent)
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    renderer.setPixelRatio(devicePixelRatio);
    renderer.setClearColor(0x000000, 0); // Transparent

    scene = new THREE.Scene();

    // 2. Kamera und Licht
    camera = new THREE.PerspectiveCamera(35, 1, 0.1, 100);
    camera.position.set(0, 0, 10); // Kamera schaut von vorne drauf
    scene.add(camera);

    // Licht an die Kamera hängen (Headlight), damit der Würfel immer beleuchtet ist
    const light = new THREE.PointLight(0xffffff, 1.5);
    camera.add(light);
    scene.add(new THREE.AmbientLight(0xffffff, 0.5));

    const loader = new GLTFLoader();

    try {
        const gltf = await loader.loadAsync("./models/dice_set.glb");

        // Gruppe erstellen, um alles gemeinsam zu verschieben
        rootGroup = new THREE.Group();
        rootGroup.add(gltf.scene);
        scene.add(rootGroup);

        // Würfel identifizieren
        gltf.scene.traverse(obj => {
            if (obj.isMesh && obj.name) {
                const name = obj.name.toLowerCase();
                if (["d4","d6","d8","d10","d12","d20"].includes(name)) {
                    diceMap.set(name, obj);
                    obj.visible = false;
                }
            }
        });

        // 3. WICHTIG: Auto-Fix dauerhaft anwenden (Zentrieren & Skalieren)
        const refDie = diceMap.get("d20") || diceMap.values().next().value;
        if (refDie) {
            // Kurz sichtbar machen für Messung
            const wasVisible = refDie.visible;
            refDie.visible = true;

            const box = new THREE.Box3().setFromObject(refDie);
            const center = new THREE.Vector3();
            box.getCenter(center);
            const size = new THREE.Vector3();
            box.getSize(size);

            refDie.visible = wasVisible;

            // Modell so verschieben, dass der Würfel im Nullpunkt (0,0,0) liegt
            gltf.scene.position.x = -center.x;
            gltf.scene.position.y = -center.y;
            gltf.scene.position.z = -center.z;

            // Skalieren, damit er gut ins Bild passt (Zielgröße ca. 2.2 Einheiten)
            const maxDim = Math.max(size.x, size.y, size.z);
            if (maxDim > 0) {
                const scaleFactor = 2.2 / maxDim;
                rootGroup.scale.set(scaleFactor, scaleFactor, scaleFactor);
            }
        }

    } catch (err) {
        console.error("Fehler beim Laden der Würfel:", err);
    }

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
    const duration = 800; // Animation dauert 0.8 Sekunden

    // Aktuelle Rotation merken
    const startRot = { x: die.rotation.x, y: die.rotation.y, z: die.rotation.z };

    // Ziel: Einfach wild drehen (Zufall)
    // HINWEIS: Das landet auf einer zufälligen Seite, nicht zwingend auf dem echten Ergebnis!
    const targetRot = {
        x: startRot.x + Math.PI * (4 + Math.random() * 2),
        y: startRot.y + Math.PI * (4 + Math.random() * 2),
        z: startRot.z + Math.PI * (4 + Math.random() * 2)
    };

    function spin(t) {
        const p = Math.min(1, (t - start) / duration);
        const ease = 1 - Math.pow(1 - p, 3); // Cubic Ease Out (schnell starten, langsam enden)

        die.rotation.x = startRot.x + (targetRot.x - startRot.x) * ease;
        die.rotation.y = startRot.y + (targetRot.y - startRot.y) * ease;
        die.rotation.z = startRot.z + (targetRot.z - startRot.z) * ease;

        if (p < 1) requestAnimationFrame(spin);
    }

    requestAnimationFrame(spin);
}