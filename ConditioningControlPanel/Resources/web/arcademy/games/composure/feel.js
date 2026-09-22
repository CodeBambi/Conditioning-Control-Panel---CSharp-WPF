import { PIECES, createWorld } from './feel-physics.js';
import { style } from './feel-style.js';

let matterPromise;
function loadMatter() {
  if (globalThis.Matter?.version === '0.20.0') return Promise.resolve(globalThis.Matter);
  if (!matterPromise) matterPromise = new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = new URL('./feel-vendor/matter-0.20.0.min.js', import.meta.url).href;
    script.onload = () => { script.remove(); resolve(globalThis.Matter); };
    script.onerror = () => { script.remove(); matterPromise = null; reject(new Error('Physics could not load')); };
    document.head.append(script);
  });
  return matterPromise;
}

export function create(ctx) {
  const t = (key, fallback) => ctx.lexicon?.(`composure.feel.${key}`, fallback) || fallback;
  const shell = document.createElement('section');
  shell.className = 'cf';
  const sheet = document.createElement('style'); sheet.textContent = style;
  shell.innerHTML = `<header><h2></h2><span class="cf-count"></span></header><p class="cf-instruction"></p>
    <svg class="cf-scene" viewBox="0 0 360 410" role="img" tabindex="0">
      <defs><radialGradient id="cf-glow"><stop stop-color="#ffe2a5" stop-opacity=".32"/><stop offset="1" stop-color="#ffe2a5" stop-opacity="0"/></radialGradient></defs>
      <ellipse cx="180" cy="342" rx="132" ry="16" fill="#09121a88"/>
      <path d="M105 365L122 398H238L255 365" fill="#192a35" stroke="#6c797955"/>
      <rect x="105" y="344" width="150" height="20" rx="5" fill="#8c9a98"/>
      <path d="M111 348H249" stroke="#d4d9bc" stroke-width="2"/>
      <path d="M180 40V326" stroke="#c1d0c41f" stroke-dasharray="2 8"/>
      <ellipse class="cf-finale" cx="180" cy="215" rx="178" ry="175" fill="url(#cf-glow)"/>
      <g class="cf-bodies"></g><g class="cf-preview"></g><g class="cf-particles"></g>
    </svg><p class="cf-status" role="status" aria-live="polite"></p><div class="cf-tray"></div>
    <div class="cf-actions"><button type="button" class="cf-rotate"></button><button type="button" class="cf-drop"></button></div>
    <p class="cf-hint"></p>`;
  shell.prepend(sheet); ctx.root.append(shell);
  const $ = s => shell.querySelector(s), svg = $('.cf-scene'), tray = $('.cf-tray');
  $('h2').textContent = t('title', 'Make it stand');
  $('.cf-instruction').textContent = t('instruction', 'Pick a piece. Drag it above the platform. Let go.');
  $('.cf-rotate').textContent = t('rotate', 'Rotate');
  $('.cf-drop').textContent = t('drop', 'Drop');
  $('.cf-hint').textContent = t('keys', 'Arrows to aim. R to rotate. Space to drop. Fallen pieces come back.');
  svg.setAttribute('aria-label', t('scene', 'Sculpture. Pick a piece below, then drag to place it.'));
  let M, world, ghost, selected = -1, raf = 0, last = 0, accumulator = 0, stable = 0;
  let running = false, paused = false, suspended = false, destroyed = false, finished = false;
  let pointer = null, audio, voices = [], elapsed = 0, falls = 0, burst = [];
  const bodyNodes = new Map(), abort = new AbortController();
  const signal = { signal: abort.signal };
  const reduced = () => ['off', 'reduced', 0].includes(ctx.motion?.motionLevel);
  const audible = () => typeof ctx.audioAudible === 'function' ? ctx.audioAudible() : ctx.audioAudible !== false;
  const canPlay = () => running && !paused && !suspended && !document.hidden && !finished;
  const status = text => { $('.cf-status').textContent = text; };
  function node(tag, attrs, parent) {
    const n = document.createElementNS('http://www.w3.org/2000/svg', tag);
    for (const [key, value] of Object.entries(attrs)) n.setAttribute(key, value);
    parent?.append(n); return n;
  }
  function sound(frequency, strength = .07, delay = 0) {
    if (!audible() || !audio || audio.state !== 'running' || voices.length >= 8) return;
    const osc = audio.createOscillator(), gain = audio.createGain(), now = audio.currentTime + delay;
    osc.type = 'sine'; osc.frequency.setValueAtTime(frequency, now);
    osc.frequency.exponentialRampToValueAtTime(frequency * .7, now + .15);
    gain.gain.setValueAtTime(0, now); gain.gain.linearRampToValueAtTime(strength, now + .006);
    gain.gain.exponentialRampToValueAtTime(.001, now + .24);
    osc.connect(gain); gain.connect(audio.destination); voices.push(osc);
    osc.onended = () => { osc.disconnect(); gain.disconnect(); voices = voices.filter(v => v !== osc); };
    osc.start(now); osc.stop(now + .25);
  }
  function unlockAudio() {
    if (!audible()) return;
    const Audio = globalThis.AudioContext || globalThis.webkitAudioContext;
    if (!audio && Audio) audio = new Audio();
    audio?.resume().catch(() => {});
  }
  function quiet() {
    for (const voice of voices) { try { voice.stop(); } catch {} }
    audio?.suspend().catch(() => {});
  }
  function sparks(x, y, color, count = 10) {
    if (reduced()) return;
    for (let i = 0; i < count && burst.length < 36; i++) {
      const a = Math.random() * Math.PI * 2, speed = 25 + Math.random() * 55;
      burst.push({ x, y, vx: Math.cos(a) * speed, vy: Math.sin(a) * speed - 20, life: .5,
        el: node('circle', { cx: x, cy: y, r: 1 + Math.random() * 2, fill: color, class: 'cf-spark' }, $('.cf-particles')) });
    }
  }
  function renderTray() {
    for (let id = 0; id < PIECES.length; id++) {
      const b = tray.children[id]; b.disabled = !!world?.pieces.has(id) || finished || !running;
      b.setAttribute('aria-pressed', String(selected === id));
    }
    $('.cf-count').textContent = `${world?.pieces.size || 0} / 6`;
    $('.cf-rotate').disabled = !ghost || finished; $('.cf-drop').disabled = !ghost || finished;
  }
  function select(id) {
    if (!canPlay() || world.pieces.has(id)) return;
    selected = id; ghost = world.make(id); stable = 0;
    status(t('aim', 'Find its balance. Rotate if you like.')); renderTray(); draw();
  }
  function polygon(body, id, parent, preview = false) {
    const el = node('polygon', { fill: PIECES[id].color, class: preview ? 'cf-piece cf-ghost' : 'cf-piece' }, parent);
    el.setAttribute('points', body.vertices.map(v => `${v.x},${v.y}`).join(' ')); return el;
  }
  function draw() {
    if (!world) return;
    shell.dataset.reduced = String(reduced());
    for (const [id, body] of world.pieces) {
      let el = bodyNodes.get(id);
      if (!el) { el = polygon(body, id, $('.cf-bodies')); bodyNodes.set(id, el); }
      el.setAttribute('points', body.vertices.map(v => `${v.x},${v.y}`).join(' '));
    }
    for (const [id, el] of bodyNodes) if (!world.pieces.has(id)) { el.remove(); bodyNodes.delete(id); }
    $('.cf-preview').replaceChildren();
    if (ghost) {
      node('ellipse', { cx: ghost.position.x, cy: 339, rx: 20, ry: 4, fill: '#080f1844' }, $('.cf-preview'));
      const el = polygon(ghost, selected, $('.cf-preview'), true);
      el.classList.toggle('cf-invalid', !world.valid(ghost));
    }
  }
  function drop() {
    if (!canPlay() || !ghost) return;
    if (!world.drop(ghost, selected)) { status(t('higher', 'A little higher. Give it room to land.')); return; }
    ghost = null; selected = -1; stable = 0;
    status(t('settling', 'Let it settle. Pick the next piece.')); renderTray(); draw();
  }
  function rotate() {
    if (!canPlay() || !ghost) return;
    M.Body.rotate(ghost, Math.PI / 2); sound(360, .025); draw();
  }
  function aim(event) {
    const rect = svg.getBoundingClientRect(), scale = Math.min(rect.width / 360, rect.height / 410);
    const ox = (rect.width - 360 * scale) / 2, oy = (rect.height - 410 * scale) / 2;
    const x = (event.clientX - rect.left - ox) / scale;
    const y = (event.clientY - rect.top - oy) / scale;
    return { x: Math.max(35, Math.min(325, x)), y: Math.max(40, Math.min(322, y)), scale };
  }
  function move(event) {
    if (pointer !== event.pointerId || !ghost || !canPlay()) return;
    const p = aim(event), offset = event.pointerType === 'touch' ? 48 / p.scale : 0;
    M.Body.setPosition(ghost, { x: p.x, y: Math.max(40, p.y - offset) }); draw();
  }
  svg.addEventListener('pointerdown', event => {
    if (!canPlay()) return;
    unlockAudio();
    if (!ghost) {
      const p = aim(event), hit = M.Query.point([...world.pieces.values()], p)[0];
      if (!hit) return;
      selected = Number(hit.label); ghost = world.take(selected); stable = 0; renderTray();
    }
    pointer = event.pointerId; svg.setPointerCapture(pointer); move(event); event.preventDefault();
  }, signal);
  svg.addEventListener('pointermove', move, signal);
  svg.addEventListener('pointerup', event => {
    if (pointer !== event.pointerId) return;
    move(event); pointer = null; drop();
  }, signal);
  svg.addEventListener('pointercancel', () => { pointer = null; }, signal);
  svg.addEventListener('keydown', event => {
    if (!canPlay() || !ghost) return;
    const delta = { ArrowLeft: [-8, 0], ArrowRight: [8, 0], ArrowUp: [0, -8], ArrowDown: [0, 8] }[event.key];
    unlockAudio();
    if (delta) M.Body.setPosition(ghost, { x: Math.max(35, Math.min(325, ghost.position.x + delta[0])),
      y: Math.max(40, Math.min(322, ghost.position.y + delta[1])) });
    else if (event.key.toLowerCase() === 'r') rotate();
    else if (event.key === ' ') drop(); else return;
    event.preventDefault(); draw();
  }, signal);
  $('.cf-rotate').addEventListener('click', () => { unlockAudio(); rotate(); }, signal);
  $('.cf-drop').addEventListener('click', () => { unlockAudio(); drop(); }, signal);
  for (let id = 0; id < PIECES.length; id++) {
    const button = document.createElement('button'); button.type = 'button';
    button.title = PIECES[id].name; button.setAttribute('aria-label', t(`piece${id}`, PIECES[id].name));
    const icon = node('svg', { viewBox: '0 0 120 65', 'aria-hidden': 'true' });
    button.append(icon); tray.append(button);
    button.addEventListener('click', () => { unlockAudio(); select(id); svg.focus({ preventScroll: true }); }, signal);
  }
  function finish() {
    if (finished || destroyed) return;
    finished = true; shell.dataset.finished = 'true';
    status(t('finished', 'It stands. You made that.')); renderTray();
    sparks(180, 180, '#ffe5ac', 36);
    [262, 330, 392].forEach((f, i) => sound(f, .045, i * .09));
    ctx.endClass?.({ metrics: { composite: 1, pieces: 6, recoveries: falls, seconds: elapsed },
      hardGates: {}, flavorXp: 0, feelVersion: 2 });
  }
  function frame(now) {
    raf = 0;
    if (!running || paused || suspended || document.hidden || destroyed) return;
    const dt = last ? Math.min((now - last) / 1000, .05) : 0; last = now;
    if (!audible() && voices.length) quiet();
    if (!finished) {
      accumulator += dt;
      while (accumulator >= 1 / 60) {
        accumulator -= 1 / 60; elapsed += 1 / 60;
        const fallen = world.step();
        if (fallen.length) { falls += fallen.length; status(t('recovery', 'Back in the tray. The rest stays.')); renderTray(); }
        stable = !ghost && world.settled() ? stable + 1 : 0;
        if (stable === 42 && world.pieces.size < 6) { status(t('steady', 'Steady. Make it yours.')); sound(523, .035); }
        if (stable >= 90 && world.pieces.size === 6) { finish(); break; }
      }
    }
    for (const p of burst) {
      p.life -= dt; p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 100 * dt;
      p.el.setAttribute('cx', p.x); p.el.setAttribute('cy', p.y); p.el.setAttribute('opacity', Math.max(0, p.life * 2));
      if (p.life <= 0 || reduced()) p.el.remove();
    }
    burst = burst.filter(p => p.life > 0 && !reduced()); draw();
    if (!finished || burst.length) raf = requestAnimationFrame(frame);
  }
  function wake() { last = 0; accumulator = 0; shell.getAnimations({ subtree: true }).forEach(a => a.play());
    if (!raf && running && !destroyed) raf = requestAnimationFrame(frame); }
  function stop() { cancelAnimationFrame(raf); raf = 0; last = 0; accumulator = 0; pointer = null; quiet();
    shell.getAnimations({ subtree: true }).forEach(a => a.pause()); }
  document.addEventListener('visibilitychange', () => { if (document.hidden) stop(); else if (!paused && !suspended) wake(); }, signal);
  renderTray(); status(t('loading', 'Preparing the pieces.'));
  return {
    async start() {
      if (running || destroyed) return;
      unlockAudio();
      try { M = await loadMatter(); } catch { if (!destroyed) status(t('unavailable', 'The pieces could not load. Reopen this game.')); return; }
      if (destroyed || running) return;
      world = createWorld(M); running = true;
      for (let id = 0; id < 6; id++) polygon(world.make(id, 60, 32), id, tray.children[id].firstChild);
      M.Events.on(world.engine, 'collisionStart', event => {
        if (!canPlay()) return;
        let impact = 0, at;
        for (const pair of event.pairs) {
          const strength = Math.max(pair.bodyA.speed, pair.bodyB.speed);
          if (strength > impact) { impact = strength; at = pair.collision.supports[0]; }
        }
        if (impact > .4 && at) {
          sound(145 + Math.random() * 30, Math.min(.09, impact * .012)); sparks(at.x, at.y, '#dfd5bd', 5);
          if (!reduced()) for (const pair of event.pairs) for (const body of [pair.bodyA, pair.bodyB]) {
            const el = bodyNodes.get(Number(body.label));
            if (el) { el.style.transformBox = 'fill-box'; el.style.transformOrigin = 'center bottom';
              el.animate([{ transform: 'scale(1.025,.97)' }, { transform: 'scale(1,1)' }], { duration: 120 }); }
          }
        }
      });
      select(0); wake();
    },
    pause() { paused = true; stop(); },
    setAudioAudible(on) { ctx.audioAudible = on; if (!on) quiet(); else if (canPlay()) unlockAudio(); },
    resume() { paused = false; if (!suspended && !document.hidden) { unlockAudio(); wake(); } },
    suspend(on) { suspended = on; if (on) stop(); else if (!paused && !document.hidden) wake(); },
    destroy() { if (destroyed) return; destroyed = true; running = false; stop(); abort.abort();
      world?.destroy(); audio?.close().catch(() => {}); shell.remove(); }
  };
}
