// Run with NODE_PATH pointing to a directory containing playwright and three@0.160.0.
const { chromium } = require('playwright');
const fs = require('node:fs/promises');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../../DsaWuerfelApp.Client/wwwroot');
const threeRoot = path.resolve(path.dirname(require.resolve('three')), '..');
(async () => {
  const browser = await chromium.launch({channel: 'msedge', headless: true, args: ['--enable-unsafe-swiftshader']});
  try {
    const page = await browser.newPage();
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    await page.route('http://dice.test/**', async route => {
      const url = new URL(route.request().url());
      if (url.pathname === '/') return route.fulfill({contentType:'text/html', body:`<script type="importmap">{"imports":{"three":"/three/build/three.module.js","three/addons/":"/three/examples/jsm/"}}</script><body></body>`});
      const file = url.pathname.startsWith('/three/') ? path.join(threeRoot, url.pathname.slice(7)) : path.join(root, url.pathname);
      await route.fulfill({body: await fs.readFile(file), contentType: file.endsWith('.js') ? 'text/javascript' : 'application/octet-stream'});
    });
    await page.goto('http://dice.test/');
    const result = await page.evaluate(async () => {
      const check = (condition, message) => { if (!condition) throw Error(message); };
      const THREE = await import('three');
      const {GLTFLoader} = await import('three/addons/loaders/GLTFLoader.js');
      const {createDiceScene} = await import('/js/dice3d.js');
      const frames = new Set(), listeners = new Set(), scenes = [];
      const raf = window.requestAnimationFrame.bind(window), cancel = window.cancelAnimationFrame.bind(window);
      window.requestAnimationFrame = fn => { let id = raf(t => {frames.delete(id); fn(t);}); frames.add(id); return id; };
      window.cancelAnimationFrame = id => {frames.delete(id); cancel(id);};
      const add = EventTarget.prototype.addEventListener, remove = EventTarget.prototype.removeEventListener;
      EventTarget.prototype.addEventListener = function(type, fn, ...args) { if (type === 'resize' || (type === 'click' && this instanceof HTMLCanvasElement)) listeners.add(fn); return add.call(this,type,fn,...args); };
      EventTarget.prototype.removeEventListener = function(type, fn, ...args) { listeners.delete(fn); return remove.call(this,type,fn,...args); };
      const sceneAdd = THREE.Scene.prototype.add;
      THREE.Scene.prototype.add = function(...items) {if (!scenes.includes(this)) scenes.push(this); return sceneAdd.apply(this,items);};
      const canvas = () => {const c = document.createElement('canvas'); c.style='width:600px;height:300px'; document.body.append(c); return c;};
      const meshes = scene => scene.children.filter(x => x.userData.diceIndex !== undefined);
      const cleanup = () => {check(frames.size === 0, 'remaining animation frames'); check(listeners.size === 0, 'remaining listeners');};
      for (let i=0; i<10; i++) {
        const c = canvas(), dice = createDiceScene();
        dice.updateDice([6]); dice.updateDice([20,6]);
        await dice.init(c);
        check(meshes(scenes.at(-1)).length === 2, 'latest preview missing');
        dice.rollDice([1,2]); dice.rollDice([3,4]);
        check(frames.size === 2, 'old roll loop survived');
        const resources = new Set(); scenes.at(-1).traverse(o => {if(o.geometry) resources.add(o.geometry); for(const m of (Array.isArray(o.material)?o.material:[o.material])) if(m) {resources.add(m); for(const v of Object.values(m)) if(v?.isTexture) resources.add(v);}});
        const released = new Set(); for(const r of resources) r.addEventListener('dispose', () => released.add(r));
        dice.dispose(); dice.dispose(); cleanup();
        check(released.size === resources.size, 'GPU resource not released');
        check(c.isConnected, 'JS removed component canvas'); c.remove();
      }
      const cameraUpdates = [];
      const originalUpdateProjectionMatrix = THREE.PerspectiveCamera.prototype.updateProjectionMatrix;
      THREE.PerspectiveCamera.prototype.updateProjectionMatrix = function(...args) {
        cameraUpdates.push({ aspect: this.aspect, y: this.position.y });
        return originalUpdateProjectionMatrix.apply(this, args);
      };
      const snapshotDice = scene => meshes(scene).sort((a, b) => a.userData.diceIndex - b.userData.diceIndex).map(mesh => ({
        x: mesh.position.x,
        y: mesh.position.y,
        z: mesh.position.z,
        scale: mesh.scale.x,
        rotation: { x: mesh.rotation.x, y: mesh.rotation.y, z: mesh.rotation.z }
      }));
      const snapshotsEqual = (left, right) => left.length === right.length && left.every((die, index) => {
        const other = right[index];
        return ['x', 'y', 'z', 'scale'].every(key => Math.abs(die[key] - other[key]) < 0.0001);
      });
      const waitFrames = async count => {
        for (let i = 0; i < count; i++) await new Promise(resolve => requestAnimationFrame(resolve));
      };

      const layoutCanvas = canvas();
      layoutCanvas.style.width = '960px';
      layoutCanvas.style.height = '300px';
      const layoutDice = createDiceScene();
      await layoutDice.init(layoutCanvas);
      layoutDice.updateDice([6, 6, 6, 6, 6, 6]);
      await waitFrames(2);
      const layoutScene = scenes.at(-1);
      const wideSnapshot = snapshotDice(layoutScene);
      const wideBuffer = { width: layoutCanvas.width, height: layoutCanvas.height };
      check(wideSnapshot.length === 6, 'wide layout missing dice');
      check(new Set(wideSnapshot.map(die => die.z)).size === 1, 'wide layout did not fit one row');

      const cameraUpdatesBeforeWidthChange = cameraUpdates.length;
      layoutCanvas.style.width = '360px';
      await waitFrames(4);
      const narrowSnapshot = snapshotDice(layoutScene);
      check(layoutCanvas.width < wideBuffer.width, 'renderer buffer did not follow host width');
      check(cameraUpdates.length > cameraUpdatesBeforeWidthChange, 'camera did not follow host width');
      check(new Set(narrowSnapshot.map(die => die.z)).size === 2, 'narrow layout did not recompute rows');
      check(narrowSnapshot.some((die, index) => Math.abs(die.scale - wideSnapshot[index].scale) > 0.0001), 'narrow layout scale did not change');

      const positionSetCounts = new Map();
      for (const die of meshes(layoutScene)) {
        const originalSet = die.position.set.bind(die.position);
        let count = 0;
        die.position.set = (...args) => { count++; return originalSet(...args); };
        positionSetCounts.set(die, () => count);
      }
      const stableSnapshot = snapshotDice(layoutScene);
      await waitFrames(6);
      check(snapshotsEqual(stableSnapshot, snapshotDice(layoutScene)), 'constant width changed die layout');
      check([...positionSetCounts.values()].every(getCount => getCount() === 0), 'constant width continuously recalculated layout');

      const cameraUpdatesBeforeHeightChange = cameraUpdates.length;
      const bufferBeforeHeightChange = layoutCanvas.width;
      layoutCanvas.style.height = '500px';
      await waitFrames(4);
      check(layoutCanvas.width === bufferBeforeHeightChange, 'height change altered canvas width buffer');
      check(layoutCanvas.height > wideBuffer.height, 'renderer buffer did not follow host height');
      check(snapshotsEqual(stableSnapshot, snapshotDice(layoutScene)), 'height change recalculated die layout');
      check(cameraUpdates.length > cameraUpdatesBeforeHeightChange, 'camera did not follow host height');

      layoutDice.updateDice([20, 6, 6, 6, 6, 6]);
      await waitFrames(2);
      const sameWidthUpdate = snapshotDice(layoutScene);
      check(sameWidthUpdate.length === 6, 'same-width update lost dice');
      check(new Set(sameWidthUpdate.map(die => die.z)).size === 2, 'same-width update skipped layout');
      layoutDice.rollDice([1, 2, 3, 4, 5, 6]);
      const beforeRoll = snapshotDice(layoutScene);
      await waitFrames(4);
      const duringRoll = snapshotDice(layoutScene);
      check(duringRoll.some((die, index) => Math.abs(die.rotation.x - beforeRoll[index].rotation.x) > 0.0001 || Math.abs(die.rotation.y - beforeRoll[index].rotation.y) > 0.0001 || Math.abs(die.rotation.z - beforeRoll[index].rotation.z) > 0.0001), 'roll animation did not run after resize');
      await new Promise(resolve => setTimeout(resolve, 1100));
      layoutDice.dispose(); layoutCanvas.remove(); cleanup();

      const hiddenCanvas = canvas();
      hiddenCanvas.style.width = '0px';
      hiddenCanvas.style.height = '0px';
      const hiddenDice = createDiceScene();
      await hiddenDice.init(hiddenCanvas);
      hiddenDice.updateDice([6, 20]);
      await waitFrames(2);
      hiddenCanvas.style.width = '360px';
      hiddenCanvas.style.height = '300px';
      await waitFrames(4);
      const hiddenSnapshot = snapshotDice(scenes.at(-1));
      check(hiddenSnapshot.length === 2, 'hidden scene did not restore dice');
      check(hiddenSnapshot.some(die => Math.abs(die.x) > 0.0001), 'hidden scene retained zero-width layout');
      hiddenDice.dispose(); hiddenCanvas.remove(); cleanup();
      THREE.PerspectiveCamera.prototype.updateProjectionMatrix = originalUpdateProjectionMatrix;

      const c1=canvas(), c2=canvas(), a=createDiceScene(), b=createDiceScene();
      await a.init(c1); const firstScene=scenes.at(-1); await b.init(c2); const secondScene=scenes.at(-1);
      a.updateDice([6]); b.updateDice([20,20]); a.dispose();
      check(frames.size===1 && listeners.size===2, 'second instance was disrupted');
      check(meshes(secondScene).length===2 && firstScene!==secondScene, 'instances share scene');
      b.rollDice([1,2]); b.dispose(); c1.remove(); c2.remove(); cleanup();
      const load = GLTFLoader.prototype.loadAsync;
      let release; const gate = new Promise(resolve => release=resolve);
      GLTFLoader.prototype.loadAsync = async function(...args) {const model=await load.apply(this,args); await gate; return model;};
      const c=canvas(), late=createDiceScene(); const init=late.init(c); late.updateDice([6]); late.dispose(); release(); await init; c.remove(); cleanup();
      return {navigationCycles:10, independentInstances:2, lateLoadDisposed:true, pendingFrames:frames.size, remainingListeners:listeners.size};
    });
    assert.deepEqual(errors, []); console.log(JSON.stringify(result));
  } finally {await browser.close();}
})().catch(error => {console.error(error); process.exitCode=1;});
