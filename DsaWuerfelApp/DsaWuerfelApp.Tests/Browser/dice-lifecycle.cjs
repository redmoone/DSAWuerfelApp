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
