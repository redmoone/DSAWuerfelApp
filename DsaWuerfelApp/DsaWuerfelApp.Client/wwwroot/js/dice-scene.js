import * as THREE from "three";

export function initScene(canvas, supersample) {
    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    renderer.setClearColor(0x000000, 0);

    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.1;

    renderer.setPixelRatio(Math.min(3, window.devicePixelRatio * supersample));

    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;

    const scene = new THREE.Scene();

    const camera = new THREE.PerspectiveCamera(10, 1, 0.1, 1000);

    camera.position.set(0, 20, 1);
    camera.lookAt(0, 0, 1);

    scene.add(new THREE.AmbientLight(0xffffff, 0.18));

    const key = new THREE.DirectionalLight(0xffffff, 1.1);
    key.position.set(6, 12, 8);
    key.castShadow = true;
    key.shadow.mapSize.set(2048, 2048);
    key.shadow.bias = -0.00008;
    key.shadow.camera.near = 1;
    key.shadow.camera.far = 200;
    key.shadow.camera.left = -50;
    key.shadow.camera.right = 50;
    key.shadow.camera.top = 50;
    key.shadow.camera.bottom = -50;
    scene.add(key);

    const fill = new THREE.DirectionalLight(0xffffff, 0.35);
    fill.position.set(-8, 6, -6);
    scene.add(fill);

    const rim = new THREE.DirectionalLight(0xffffff, 0.75);
    rim.position.set(0, 8, -14);
    scene.add(rim);

    const ground = new THREE.Mesh(
        new THREE.PlaneGeometry(500, 500),
        new THREE.ShadowMaterial({ opacity: 0.22 })
    );
    ground.rotation.x = -Math.PI / 2;
    ground.position.y = -0.01;
    ground.receiveShadow = true;
    scene.add(ground);

    return { renderer, scene, camera };
}

export function resizeScene(canvas, renderer, camera) {
    const w = canvas.clientWidth;
    const h = canvas.clientHeight;

    const pixelRatio = renderer.getPixelRatio();
    const targetW = Math.floor(w * pixelRatio);
    const targetH = Math.floor(h * pixelRatio);

    if (canvas.width !== targetW || canvas.height !== targetH) {
        renderer.setSize(w, h, false);
        camera.aspect = w / h;

        if (w < 800) {
            camera.position.y = 20 + ((800 - w) / 500) * 15;
        } else {
            camera.position.y = 20;
        }

        camera.updateProjectionMatrix();
    }
}