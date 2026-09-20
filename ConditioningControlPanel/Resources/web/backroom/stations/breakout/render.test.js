import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRenderer, prefersSoftwareCanvas } from './render.js';
import { createGame } from './game.js';

function canvasStub(log = []) {
  const gradient = { addColorStop(...args) {log.push(['addColorStop',...args]);} };
  const context = new Proxy({}, { get(target, key) {
    if (key in target) return target[key];
    if (key === 'measureText') return text => ({ width: String(text).length * 8 });
    if (key === 'createImageData') return (w, h) => ({ data: new Uint8ClampedArray(w * h * 4) });
    if (key === 'createRadialGradient' || key === 'createLinearGradient') return () => gradient;
    if (key === 'createPattern') return () => ({});
    return (...args) => { log.push([key, ...args]); };
  } });
  return { width: 480, height: 720, getContext: () => context };
}

for (const reduced of [false, true]) test(`breakout shell stays above flash and expires, reduced=${reduced}`, () => {
  const oldDocument = globalThis.document;
  globalThis.document = { createElement: () => canvasStub() };
  try {
    const log = [], frame = { width: 100, height: 100 }, selected = [];
    const renderer = createRenderer(canvasStub(log), { reduced, rng: () => 0.5,
      media: { frame: i => { selected.push(i); return frame; } } });
    renderer.resize(480, 720);
    const snap = { ...createGame({ rng: () => 0.5 }).snapshot(), state: 'colour', balls: [{ x: 120, y: 300, r: 8, vx: 100, vy: -100, trail: [] }], colliders: [], pops: [], bricks: [] };
    renderer.onEvent('breakout', { x: 240, y: 650, gifIndex: 3 });
    renderer.draw(snap, { now: 1, dt: 0.05 });
    const shell = log.findLastIndex(op => op[0] === 'fillText' && op[1] === 'OLD SELF');
    assert.ok(shell >= 0, 'breakout emits an Old Self without a split event');
    const picture = log.findLastIndex(op => op[0] === 'drawImage' && op[1] === frame);
    if (reduced) {
      assert.equal(picture, -1, 'reduced motion cuts to colour without a picture flash');
      assert.equal(log[shell][2], 240); assert.equal(log[shell][3], 650);
    } else {
      assert.ok(picture >= 0 && picture < shell, 'shell is drawn above its picture flash');
      assert.ok(selected.includes(3), 'uses the station-selected picture');
      const ball = log.findLastIndex(op => op[0] === 'translate' && op[1] === 120 && op[2] === 300);
      assert.ok(ball > shell, 'playable ball stays above both flash and Old Self');
    }
    for (let i = 1; i < 68; i++) renderer.draw(snap, { now: 1 + i * 0.05, dt: 0.05 });
    const lateShell = log.findLast(op => op[0] === 'fillText' && op[1] === 'OLD SELF');
    if (!reduced) assert.ok(lateShell[3] < 0, 'shell reaches the top even from the bottom of the field');
    log.length = 0;
    for (let i = 0; i < 6; i++) renderer.draw(snap, { now: 5 + i * 0.05, dt: 0.05 });
    log.length = 0;
    renderer.draw(snap, { now: 6, dt: 0.05 });
    assert.equal(log.some(op => op[0] === 'fillText' && op[1] === 'OLD SELF'), false);
    renderer.dispose();
  } finally { globalThis.document = oldDocument; }
});

 test('grey bricks conceal every special identity without changing the game data', () => {
  const oldDocument = globalThis.document;
  globalThis.document = { createElement: () => canvasStub() };
  try {
    const log = [], renderer = createRenderer(canvasStub(log), { rng: () => 0.5 });
    renderer.resize(480, 720);
    const snap = createGame({ rng: () => 0.5 }).snapshot();
    snap.bricks = [{ x: 30, y: 60, w: 60, h: 20, alive: true, row: 0, col: 0,
      word: 'BLANK', letter: 'X', spiral: 'spiral', split: true, jackpot: true, gif: 0 }];
    const before = JSON.stringify(snap.bricks);
    renderer.draw(snap, { now: 1, dt: 0.016 });
    assert.equal(log.some(op => op[0] === 'fillText' && ['BLANK', 'X'].includes(op[1])), false);
    assert.equal(JSON.stringify(snap.bricks), before);
    renderer.dispose();
  } finally { globalThis.document = oldDocument; }
});


test('two-skin pop paints two different pictures together and expires', () => {
  const oldDocument = globalThis.document;
  globalThis.document = { createElement: () => canvasStub() };
  try {
    const log = [], frames = [{ width: 100, height: 100 }, { width: 100, height: 100 }];
    const renderer = createRenderer(canvasStub(log), { rng: () => .5,
      media: { keys: () => ['a', 'b'], frame: i => frames[i], pin() {} } });
    renderer.resize(480, 720);
    const snap = { ...createGame({ rng: () => .5 }).snapshot(), state: 'colour', balls: [], bricks: [], colliders: [] };
    renderer.onEvent('bubblePop', { x: 240, y: 400, r: 46, gif: 0, tier: 2 });
    renderer.draw(snap, { dt: .05, now: 1 });
    for (const frame of frames) assert.ok(log.some(op => op[0] === 'drawImage' && op[1] === frame));
    for (let i = 0; i < 34; i++) renderer.draw(snap, { dt: .05, now: 2 + i * .05 });
    log.length = 0; renderer.draw(snap, { dt: .05, now: 4 });
    assert.equal(log.some(op => op[0] === 'drawImage' && frames.includes(op[1])), false);
    renderer.dispose();
  } finally { globalThis.document = oldDocument; }
});

 test('picture tiers use cached auras without labels and stay hidden in grey', () => {
  const oldDocument = globalThis.document;
  globalThis.document = { createElement: () => canvasStub() };
  try {
    const log = [], renderer = createRenderer(canvasStub(log), { reduced: true });
    renderer.resize(480, 720);
    const snap = createGame().snapshot(); snap.state = 'colour'; snap.wallAge = 2;
    snap.bricks = [1, 2, 3].map((tier, i) => ({ x: 30 + i * 70, y: 60, w: 60, h: 20,
      alive: true, row: 0, col: i, gif: i, tier }));
    renderer.draw(snap, { now: 1, dt: 0.016 });
    const glows = log.filter(op => op[0] === 'drawImage' && op[1]?.width === 128 && op[1]?.height === 128).map(op => op[1]);
    assert.equal(new Set(glows).size, 3, 'each tier draws its own glow before media loads');
    assert.equal(log.some(op => op[0] === 'fillText' && /PIC|FULL/.test(op[1])), false);
    log.length = 0;
    renderer.draw(snap, { now: 2, dt: 0.016 });
    assert.equal(log.filter(op => op[0] === 'drawImage' && glows.includes(op[1])).length, 3, 'sprites are reused');
    snap.colliders = [1, 2, 3].map((tier, i) => ({ x: 90 + i * 120, y: 350, r: 30, age: 2, alpha: 1, pulse: 0, hits: 0, gif: i, tier }));
    log.length = 0;
    renderer.draw(snap, { now: 2.5, dt: 0.016 });
    assert.equal(log.filter(op => op[0] === 'drawImage' && glows.includes(op[1])).length, 6, 'bubbles share the same tier auras');
    snap.bricks = []; snap.colliders = [snap.colliders[2]];
    for (let hits = 0; hits < 3; hits++) {
      snap.colliders[0].hits = hits; log.length = 0;
      renderer.draw(snap, { now: 2.6, dt: .016 });
      assert.ok(log.some(op => op[0] === 'drawImage' && op[1] === glows[2 - hits]), 'a hit advances toward the pink aura');
    }
    snap.state = 'grey'; log.length = 0;
    renderer.draw(snap, { now: 3, dt: 0.016 });
    assert.equal(log.some(op => op[0] === 'drawImage' && glows.includes(op[1])), false, 'grey conceals tier colours');
    renderer.dispose();
  } finally { globalThis.document = oldDocument; }
});

for (const reduced of [false, true]) test(`Spell fills fly to background slots, completion expires and grey conceals, reduced=${reduced}`, async () => {
  const { createSpellRender } = await import('./spell-render.js');
  const log = [], particles = [];
  const fx = createSpellRender({ W: 480, H: 720, FONT: 'Arial', reduced,
    particles: { burst: (...args) => particles.push(args) } });
  const g = canvasStub(log).getContext('2d');
  const snap = { state: 'colour', spell: { word: 'REST', filled: [true, false, false, false] } };
  fx.background(g, snap);
  assert.equal(log.filter(op => op[0] === 'fillText').length, 4, 'all target slots visible');
  fx.event('spellFill', { word: 'REST', index: 0, letter: 'R', x: 70, y: 80, style: 'drift' });
  log.length = 0; fx.front(g, snap, 0.05);
  const early = log.find(op => op[0] === 'fillText');
  if (reduced) assert.equal(early[3], 720 * .48, 'reduced motion fills in place');
  else assert.ok(early[3] > 80 && early[3] < 720 * .48, 'letter travels toward slot');
  for (let i = 0; i < 21; i++) fx.front(g, snap, .05);
  fx.event('spellComplete', { word: 'REST' });
  log.length = 0; fx.front(g, snap, .05);
  assert.ok(log.some(op => op[0] === 'fillText' && op[1] === 'REST'));
  for (let i = 0; i < 33; i++) fx.front(g, snap, .05);
  log.length = 0; fx.front(g, snap, .05);
  assert.equal(log.some(op => op[0] === 'fillText'), false, 'completion expires');
  snap.state = 'grey'; log.length = 0; fx.background(g, snap); fx.front(g, snap, .05);
  assert.equal(log.some(op => op[0] === 'fillText'), false);
  if (reduced) assert.equal(particles.length, 0);
});


test('host fullscreen spiral gets a separate ball layer that disappears with the effect', () => {
  const oldDocument = globalThis.document, layers = [], log = [];
  let active = true, removed = 0;
  globalThis.document = {
    createElement: () => Object.assign(canvasStub(log), { style: {}, remove() { removed++; } }),
    querySelector: () => active ? {} : null,
    body: { appendChild: layer => layers.push(layer) }
  };
  try {
    const canvas = canvasStub(); canvas.getBoundingClientRect = () => ({ left: 0, top: 0, width: 1280, height: 720 });
    const renderer = createRenderer(canvas, { reduced: true }); renderer.resize(1280, 720);
    const s = createGame().snapshot(); s.bricks = [];
    renderer.draw(s, { now: 1, dt: .016 });
    assert.equal(layers.length, 1);
    assert.match(layers[0].style.cssText, /2147483001/);
    assert.ok(log.some(op => op[0] === 'arc' && op[1] === s.balls[0].x && op[2] === s.balls[0].y));
    active = false; renderer.draw(s, { now: 2, dt: .016 });
    assert.equal(removed, 1); renderer.dispose();
  } finally { globalThis.document = oldDocument; }
});


test('spiral brick tiles retain their Loom drawing across animation frames', async () => {
  const { createWellFx } = await import('./render-well.js');
  const oldDocument = globalThis.document, operations = [];
  globalThis.document = { createElement: () => canvasStub(operations) };
  const fx = createWellFx();
  try {
    const first = fx.tile('whirl', 0, 12, .5, 56, 1);
    assert.ok(first);
    operations.length = 0;
    for (let frame = 2; frame < 180; frame++) {
      assert.equal(fx.tile('whirl', frame / 60, 12, .51, 56, frame), first);
    }
    assert.equal(operations.length, 0, 'rotation must not redraw or read back the Loom shader');
    const brighter = fx.tile('whirl', 3, 12, .8, 56, 180);
    assert.notEqual(brighter, first, 'colour changes retain their own bounded tile');
  } finally { fx.dispose(); globalThis.document = oldDocument; }
});

test('wall five fixed-size top bricks render through the paddle and ball',()=>{
 const previous=globalThis.document;globalThis.document={createElement:()=>canvasStub()};
 try{
  const log=[], renderer=createRenderer(canvasStub(log),{rng:()=>.5});
  const game=createGame({rng:()=>.5});game.jumpToWall(5);const s=game.snapshot();
  s.state='colour';s.wallAge=2;s.balls=[{x:640,y:600,r:8,vx:0,vy:-100,trail:[]}];
  renderer.resize(1280,720);renderer.draw(s,{dt:.016,now:1});
  assert.ok(log.some(op=>op[0]==='translate'&&op[1]===640&&op[2]===600));
  renderer.dispose();
 }finally{globalThis.document=previous;}
});

for(const reduced of [false,true]) test(`iris core melt renders with live gameplay, reduced=${reduced}`,()=>{
 const before=globalThis.document;globalThis.document={createElement:()=>canvasStub()};
 try {const game=createGame({rng:()=>.6});game.jumpToWall(6);game.breakoutNow();
 const renderer=createRenderer(canvasStub(),{reduced});renderer.resize(1280,720);
 renderer.onEvent('irisCore',{});renderer.draw(game.snapshot(),{now:1,dt:.016});renderer.dispose();
 }finally{globalThis.document=before;}
});

for(const reduced of [false,true])test(`pendulum renders chains, anchors and bobs, reduced=${reduced}`,()=>{
 const oldDocument=globalThis.document;globalThis.document={createElement:()=>canvasStub()};
 try {
  const log=[],renderer=createRenderer(canvasStub(log),{reduced,rng:()=>.6});renderer.resize(1280,720);
  const game=createGame({reduced,rng:()=>.6});game.jumpToWall(7);const s=game.snapshot();s.state='colour';
  renderer.draw(s,{now:1,dt:.016});assert.equal(log.filter(op=>op[0]==='fillText'&&op[1]==='CUT').length,0);
  assert.ok(log.some(op=>op[0]==='arc'&&op[3]===22),'circular anchor health ring');
  assert.ok(log.some(op=>op[0]==='arc'&&op[3]===30));renderer.dispose();
 } finally {globalThis.document=oldDocument;}
});

test('menu handoff can reset an unchanged canvas size and retain valid field mapping',()=>{
 const oldDocument=globalThis.document;globalThis.document={createElement:()=>canvasStub()};
 try {
  const canvas=canvasStub(),renderer=createRenderer(canvas,{rng:()=>.5});
  let writes=0,width=canvas.width,height=canvas.height;
  Object.defineProperty(canvas,'width',{get:()=>width,set:v=>{width=v;writes++;}});
  Object.defineProperty(canvas,'height',{get:()=>height,set:v=>{height=v;writes++;}});
  renderer.resize(1761,851);const before=renderer.toField(880.5,425.5);writes=0;
  renderer.resize(1761,851);assert.equal(writes,0);
  renderer.resize(1761,851,true);assert.equal(writes,2);
  assert.deepEqual(renderer.toField(880.5,425.5),before);
  assert.ok(Math.abs(before.x-640)<1e-6 && Math.abs(before.y-360)<1e-6);
  renderer.dispose();
 } finally {globalThis.document=oldDocument;}
});

test('Firefox gets software canvas by default while Chromium keeps its existing path',()=>{
 assert.equal(prefersSoftwareCanvas('Mozilla/5.0 Gecko/20100101 Firefox/153.0'),true);
 assert.equal(prefersSoftwareCanvas('Mozilla/5.0 Chrome/145.0 Safari/537.36 Edg/145.0'),false);
});
for(const reduced of [false,true])test(`locked finale has silver metal in grey only while sealed, reduced=${reduced}`,()=>{
 const oldDocument=globalThis.document;globalThis.document={createElement:()=>canvasStub()};
 try {
  const log=[],renderer=createRenderer(canvasStub(log),{reduced,rng:()=>.5});renderer.resize(1280,720);
  const game=createGame({rng:()=>.5});game.jumpToWall(8);const s=game.snapshot();
  s.state='grey';s.finale.phase='locked';s.finale.age=40;s.wallAge=2;
  renderer.draw(s,{now:1,dt:.016});assert.ok(log.some(op=>op[0]==='addColorStop'&&op[2]==='#edf0f2'));
  log.length=0;s.bricks=s.bricks.filter(b=>b.finaleDefense);renderer.draw(s,{now:2,dt:.016});
  assert.equal(log.some(op=>op[0]==='addColorStop'&&op[2]==='#edf0f2'),false,'defensive row stays ordinary');
  game.jumpToWall(8);s.finale.phase='released';log.length=0;renderer.draw(s,{now:3,dt:.016});
  assert.equal(log.some(op=>op[0]==='addColorStop'&&op[2]==='#edf0f2'),false,'breakout removes invincible finish');
  renderer.dispose();
 }finally{globalThis.document=oldDocument;}
});

test('stable word glyphs are reused while plates and rotation remain live', () => {
  const oldDocument = globalThis.document, cached = [], main = [];
  globalThis.document = { createElement: () => {
    const c = canvasStub(cached); c.getContext('2d').measureText = text => ({ width: text.length * 7 }); return c;
  } };
  try {
    const canvas = canvasStub(main); canvas.getContext('2d').measureText = text => ({ width: text.length * 7 });
    const renderer = createRenderer(canvas, { rng: () => .5, software: true });
    renderer.resize(1280, 720);
    const snap = createGame({ rng: () => .5 }).snapshot();
    Object.assign(snap, { state: 'colour', sat: .5, wallAge: 3, balls: [], colliders: [], pops: [], well: null });
    snap.bricks = [{ x: 80, y: 80, w: 80, h: 24, row: 0, col: 0, alive: true, gif: -1, word: 'DRIFT', angle: .2 }];
    renderer.draw(snap, { now: 1, dt: .016 });
    assert.equal(cached.filter(op => op[0] === 'fillText' && op[1] === 'DRIFT').length, 1);
    main.length = 0;
    snap.bricks[0].angle = .4; snap.bricks[0].w = 72;
    renderer.draw(snap, { now: 2, dt: .016 });
    assert.equal(cached.filter(op => op[0] === 'fillText' && op[1] === 'DRIFT').length, 1, 'movement and size reuse the same glyph image');
    assert.ok(main.some(op => op[0] === 'rotate' && op[1] === -.4), 'text still counter-rotates to stay readable');
    snap.bricks[0].word = 'FLOAT';
    renderer.draw(snap, { now: 3, dt: .016 });
    assert.equal(cached.filter(op => op[0] === 'fillText' && op[1] === 'FLOAT').length, 1, 'a changed word gets its own glyphs');
    snap.bricks[0].glitch = .2; main.length = 0;
    renderer.draw(snap, { now: 4, dt: .016 });
    assert.ok(main.some(op => op[0] === 'fillText' && op[1] === 'FLOAT'), 'glitch text still draws live');
    renderer.dispose();
  } finally { globalThis.document = oldDocument; }
});

for (const iris of [false, true]) test(`routine camera feedback respects hit kind and Iris, iris=${iris}`, () => {
  const oldDocument = globalThis.document;
  globalThis.document = { createElement: () => canvasStub() };
  try {
    const log = [], renderer = createRenderer(canvasStub(log), { rng: () => .8 });
    renderer.resize(1280, 720);
    const s = createGame().snapshot();
    Object.assign(s, { state: 'colour', sat: 1, wallAge: 3, bricks: [], balls: [], iris: iris ? {eye: {fade:0}} : null });
    s.rungs.fill(true);
    renderer.draw(s, {now:1,dt:.016});
    renderer.onEvent('hit', {kind:'wall',combo:20});
    log.length=0;renderer.draw(s,{now:2,dt:.016});
    assert.deepEqual(log.filter(x=>x[0]==='translate').slice(0,2),[['translate',640,360],['translate',-640,-360]],'side walls never inherit combo shake');
    renderer.onEvent('brick',{x:640,y:300});renderer.onEvent('hit',{kind:'brick',combo:20});
    log.length=0;renderer.draw(s,{now:3,dt:.016});
    const camera=log.filter(x=>x[0]==='translate')[1];
    assert.equal(camera[1]===-640 && camera[2]===-360,iris,'Iris hit feedback stays local; other levels retain brick shake');
    renderer.dispose();
  } finally { globalThis.document=oldDocument; }
});
test('software brick pictures downsample once per frame while keeping animation live',()=>{
  const oldDocument=globalThis.document,cached=[];
  globalThis.document={createElement:()=>canvasStub(cached)};
  try {
    const source={width:640,height:480},main=[],renderer=createRenderer(canvasStub(main),{software:true,media:{frame:()=>source}});
    renderer.resize(1280,720);const s=createGame().snapshot();s.state='colour';s.wallAge=3;s.rungs.fill(true);
    s.bricks=[0,1,2].map(col=>({alive:true,x:col*80,y:100,w:60,h:24,col,row:0,gif:0}));
    renderer.draw(s,{now:1,dt:.016});
    assert.equal(cached.filter(x=>x[0]==='drawImage'&&x[1]===source).length,1);
    assert.equal(main.filter(x=>x[0]==='drawImage'&&x[1]?.width===96&&x[1]?.height===72).length,3);
    renderer.draw(s,{now:2,dt:.016});
    assert.equal(cached.filter(x=>x[0]==='drawImage'&&x[1]===source).length,2,'mutable animated source refreshes next frame');
    renderer.dispose();
  }finally{globalThis.document=oldDocument;}
});

test('reinforced faces cache each crack stage separately in grey and colour',()=>{
 const oldDocument=globalThis.document;globalThis.document={createElement:()=>canvasStub()};
 try{
  const log=[],renderer=createRenderer(canvasStub(log),{reduced:true});renderer.resize(1280,720);
  const s=createGame({brickStrength:[]}).snapshot();s.wallAge=2;s.balls=[];
  const br={alive:true,x:200,y:100,w:60,h:30,row:0,col:0,gif:-1,strength:3,hp:3};s.bricks=[br];
  const faces=[];
  for(const state of ['grey','colour'])for(const hp of [3,2,1]){
   s.state=state;br.hp=hp;log.length=0;renderer.draw(s,{now:1,dt:.016});
   const face=log.find(op=>op[0]==='drawImage'&&op[1]?.width===96&&op[1]?.height===56)?.[1];
   assert.ok(face);faces.push(face);log.length=0;renderer.draw(s,{now:2,dt:.016});
   assert.ok(log.some(op=>op[0]==='drawImage'&&op[1]===face),'same state reuses face');
  }
  assert.equal(new Set(faces).size,6,'each skin and damage state has its own cached face');renderer.dispose();
 }finally{globalThis.document=oldDocument;}
});

test('grey metal shares a cached silver face and restores normal colour face',()=>{
 const oldDocument=globalThis.document;globalThis.document={createElement:()=>canvasStub()};
 try {
  const log=[],renderer=createRenderer(canvasStub(log),{reduced:true});renderer.resize(1280,720);
  const s=createGame({rng:()=>.5}).snapshot();s.wallAge=2;s.balls=[];
  s.bricks=[0,1].map(col=>({alive:true,x:50+col*70,y:80,w:60,h:24,row:0,col,gif:-1,greyMetal:true}));
  renderer.draw(s,{now:1,dt:.016});
  const faces=log.filter(op=>op[0]==='drawImage'&&op[1]?.width===96&&op[1]?.height===40);
  assert.equal(faces.length,2);assert.equal(faces[0][1],faces[1][1]);const face=faces[0][1];
  log.length=0;renderer.draw(s,{now:2,dt:.016});assert.equal(log.filter(op=>op[0]==='drawImage'&&op[1]===face).length,2);
  s.state='colour';log.length=0;renderer.draw(s,{now:3,dt:.016});assert.equal(log.some(op=>op[0]==='drawImage'&&op[1]===face),false);
  renderer.dispose();
 }finally{globalThis.document=oldDocument;}
});
import { createLandscape } from './landscape.js';
import { createWellFx } from './render-well.js';

test('landscape reuses native backing and avoids new surfaces during camera zoom',()=>{
  const oldDocument=globalThis.document,created=[],log=[];
  globalThis.document={createElement:()=>{const c=canvasStub();created.push(c);return c;}};
  try {
    const landscape=createLandscape(1280,720,()=>.5,true),g=canvasStub(log).getContext('2d');
    let transform={a:.9,d:.9,b:0,c:0,e:24,f:0};g.getTransform=()=>transform;
    landscape.resize(.9);landscape.draw(g,false,.016);
    const count=created.length,first=log.findLast(op=>op[0]==='drawImage')[1];
    assert.equal(first.width,1152);assert.equal(first.height,648);
    landscape.draw(g,false,.016);assert.equal(created.length,count);
    assert.equal(log.findLast(op=>op[0]==='drawImage')[1],first);
    transform={...transform,a:.93,d:.93};landscape.draw(g,false,.016);
    assert.equal(created.length,count,'animated zoom uses original art instead of reallocating');
    assert.equal(log.findLast(op=>op[0]==='drawImage')[1].width,1280);
    landscape.resize(.8);transform={...transform,a:.8,d:.8};landscape.draw(g,false,.016);
    assert.equal(log.findLast(op=>op[0]==='drawImage')[1].width,1024);
  } finally {globalThis.document=oldDocument;}
});

test('software wells retain morph frames while rotation stays live and replacement repaints',()=>{
  const oldDocument=globalThis.document,created=[],main=[];
  globalThis.document={createElement:()=>{const ops=[],c=canvasStub(ops);c.ops=ops;created.push(c);return c;}};
  try {
    const fx=createWellFx({software:true,rng:()=>.5}),g=canvasStub(main).getContext('2d');
    const well={r:65,x:300,y:200,fade:1,born:1,age:1,rot:0,preset:'whirl'};
    const o={mix:1,col:()=> '#fff',dt:1/60,pink:[255,0,0],violet:[120,0,255],mint:[0,255,0],spiral(){}};
    for(let i=0;i<12;i++){well.rot+=.01;fx.draw(g,well,o);}
    const disc=created.find(c=>c.width===144&&c.ops.some(op=>op[0]==='clearRect'));
    assert.ok(disc);assert.equal(disc.ops.filter(op=>op[0]==='clearRect').length,3,'15 Hz composition at 60 Hz draw');
    assert.ok(main.some(op=>op[0]==='rotate'&&op[1]>0),'rotation advances between field redraws');
    well.r=65.2;fx.draw(g,well,o);assert.ok(created.includes(disc),'small radius changes keep the surface');
    const before=disc.ops.filter(op=>op[0]==='clearRect').length;
    fx.draw(g,{...well},o);assert.equal(disc.ops.filter(op=>op[0]==='clearRect').length,before+1,'replacement well never displays previous field');
    fx.dispose();
  }finally{globalThis.document=oldDocument;}
});


test('mixed landscape caches its shards and native size without consuming gameplay RNG', () => {
  const oldDocument = globalThis.document, created = [], log = [];
  globalThis.document = { createElement: () => {
    const ops = [], c = canvasStub(ops); c.ops = ops; created.push(c); return c;
  } };
  try {
    let randomCalls = 0;
    const landscape = createLandscape(1280, 720, () => { randomCalls++; return .5; }, true);
    const g = canvasStub(log).getContext('2d');
    let transform = { a: .9, d: .9, b: 0, c: 0, e: 0, f: 0 };
    g.getTransform = () => transform; landscape.resize(.9);
    landscape.draw(g, false, .016, true);
    const mixed = log.findLast(op => op[0] === 'drawImage')[1], count = created.length;
    assert.equal(randomCalls, 0);
    assert.equal(mixed.width, 1152);
    assert.ok(created.some(c => c.ops.filter(op => op[0] === 'clip').length > 5));
    landscape.draw(g, false, .016, true);
    assert.equal(created.length, count);
    assert.equal(log.findLast(op => op[0] === 'drawImage')[1], mixed);
    transform = { ...transform, a: .93, d: .93 };
    landscape.draw(g, false, .016, true);
    assert.equal(created.length, count, 'zoom retains the original mixed composition');
    assert.equal(log.findLast(op => op[0] === 'drawImage')[1].width, 1280);
    transform = { ...transform, a: .9, d: .9 };
    landscape.draw(g, true, .016, true);
    assert.notEqual(log.findLast(op => op[0] === 'drawImage')[1], mixed, 'dull state still wins');
  } finally { globalThis.document = oldDocument; }
});

for (const reduced of [false, true]) test(`finale suction preserves simulation and finishes on the ending card, reduced=${reduced}`, () => {
  const oldDocument=globalThis.document;
  let surfaces=0;
  globalThis.document={createElement:()=>{surfaces++;return canvasStub();}};
  try {
    const log=[],renderer=createRenderer(canvasStub(log),{reduced,rng:()=>.5});
    renderer.resize(480,720);
    const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('spiral');
    const snap=game.snapshot();snap.finale.phase='outro';snap.finale.outroAge=1.2;
    const before=JSON.stringify({bricks:snap.bricks,balls:snap.balls,paddle:snap.paddle});
    renderer.draw(snap,{now:1,dt:.016});
    assert.equal(JSON.stringify({bricks:snap.bricks,balls:snap.balls,paddle:snap.paddle}),before);
    assert.ok(log.some(op=>op[0]==='drawImage'),'pieces and world remain visible during suction');
    snap.finale.outroAge=3;renderer.draw(snap,{now:2,dt:.016});
    const shutdownSurfaces=surfaces;
    snap.finale.outroAge=3.2;renderer.draw(snap,{now:3,dt:.016});
    assert.equal(surfaces,shutdownSurfaces,'shutdown reuses a single small frame');
    log.length=0;snap.finale.outroAge=3.8;renderer.draw(snap,{now:4,dt:.016});
    assert.equal(log.some(op=>op[0]==='drawImage'),true,'ending card replaces the indefinite black screen');
    assert.ok(log.some(op=>op[0]==='fillRect'&&op[1]===0&&op[2]===0));
    renderer.dispose();
  } finally {globalThis.document=oldDocument;}
});

for (const reduced of [false, true]) test(`chaotic finale stays finite and ripple respects reduced motion=${reduced}`, () => {
  const oldDocument=globalThis.document, glyphs=new Map();
  globalThis.document={createElement:()=>{const ops=[],c=canvasStub(ops);glyphs.set(c,ops);return c;}};
  try {
    const log=[],renderer=createRenderer(canvasStub(log),{reduced,rng:()=>.5});
    renderer.resize(1280,720);
    const game=createGame({reduced,rng:()=>.5});game.jumpToFinaleBeat('spiral');
    for(let i=0;i<360;i++)game.step(1/60);
    const snap=game.snapshot();snap.finale.stageAge=2;
    assert.ok(snap.bricks.some(b=>b.finaleLetter),'real formation contains Spell pieces');
    const before=JSON.stringify(snap.bricks);
    log.length=0;renderer.draw(snap,{now:2,dt:1/60});
    assert.equal(JSON.stringify(snap.bricks),before,'drawing preserves collision coordinates and payloads');
    for(const op of log)for(const value of op.slice(1)) {
      if(typeof value==='number')assert.ok(Number.isFinite(value),`${String(op[0])} has finite arguments`);
    }
    const radius=(2-.8)*240, firstX=snap.finale.centreX+radius+Math.sin(-radius*.025)*3;
    const ripple=log.some(op=>op[0]==='moveTo'&&Math.abs(op[1]-firstX)<.0001&&op[2]===snap.finale.centreY);
    assert.equal(ripple,!reduced,'water trace follows the shared pulse radius only with motion enabled');
    assert.ok(log.some(op=>op[0]==='drawImage'&&glyphs.get(op[1])?.some(g=>g[0]==='fillText'&&typeof g[1]==='string'&&g[1].length===1)),'Spell letters remain readable');
    renderer.dispose();
  }finally{globalThis.document=oldDocument;}
});


test('finale pupil keeps one live target across ball reordering and eases to a replacement',()=>{
 const oldDocument=globalThis.document;globalThis.document={createElement:()=>canvasStub()};
 try {
  const log=[],renderer=createRenderer(canvasStub(log),{rng:()=>.5});renderer.resize(1280,720);
  const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('rings');const s=game.snapshot();
  const left={...s.balls[0],x:100,y:s.finale.centreY},right={...s.balls[0],x:1100,y:s.finale.centreY};s.balls=[left,right];
  const draw=()=>{log.length=0;renderer.draw(s,{now:2,dt:.1});return log.find(op=>op[0]==='arc'&&op[3]===4.5)[1];};
  for(let i=0;i<20;i++)draw();assert.ok(draw()<s.finale.centreX);
  s.balls=[right,left];for(let i=0;i<20;i++)draw();assert.ok(draw()<s.finale.centreX);
  left.lost=true;const first=draw();assert.ok(first<s.finale.centreX,'replacement does not snap');
  for(let i=0;i<20;i++)draw();assert.ok(draw()>s.finale.centreX);renderer.dispose();
 }finally{globalThis.document=oldDocument;}
});
