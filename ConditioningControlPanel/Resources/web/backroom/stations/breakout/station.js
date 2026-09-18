/* ============================================================================
 * stations/breakout/station.js - CONTRACT section 7 module for the Breakout
 * cabinet. mount(ctx) -> { open, close, suspend, destroy }. One 2D canvas in
 * ctx.root, the sim in game.js, the picture in render.js, the niche layer in
 * payloads.js, the sound in audio.js. No WebGL, no three.js.
 * ==========================================================================*/

import { createGame, RUNG_NAMES, RUNG_AT } from './game.js';
import { createRenderer } from './render.js';
import { createMedia, createSubliminals } from './payloads.js';
import { createAudio } from './audio.js';

export const roomStage = false;

const num = (q, k, d) => (q.has(k) && !Number.isNaN(Number(q.get(k))) ? Number(q.get(k)) : d);

function loadCss() {
  if (document.querySelector('link[data-breakout-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet'; link.href = new URL('./station.css', import.meta.url).href; link.dataset.breakoutCss = '';
  document.head.append(link);
}

export async function mount(ctx) {
  loadCss();
  const root = ctx.root;
  const hostBack = ctx.hostBack === true;
  const q = new URLSearchParams(typeof location !== 'undefined' ? location.search : '');
  const reduced = !!ctx.reduced || q.has('still') ||
    (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const t = (k, f) => { try { const v = typeof ctx.lex === 'function' ? ctx.lex(k, f) : f; return v || f; } catch (e) { return f; } };

  let el = null, canvas = null, game = null, renderer = null, media = null, subs = null, audio = null;
  let raf = 0, running = false, suspended = false, lastT = 0, dpr = 1, frames = 0, audioOn = false, sizeW = 0, sizeH = 0;
  const input = { x: null, left: false, right: false, launch: false };
  const off = [];
  const on = (target, ev, fn, opts) => { target.addEventListener(ev, fn, opts); off.push(() => target.removeEventListener(ev, fn, opts)); };

  function build() {
    el = document.createElement('div');
    el.className = 'bo-station';
    if (hostBack) el.dataset.hostBack = '';
    el.innerHTML = `
      <canvas class="bo-stage"></canvas>
      <div class="bo-top">
        <button class="bo-back" type="button" ${hostBack ? 'hidden' : ''}>${t('br_breakout_back', 'Back')}</button>
        <span class="bo-sp">${t('br_breakout_sp', 'SP')} <b>0</b></span>
      </div>
      <p class="bo-hint">${t('br_breakout_hint', 'Click or tap to launch. Move to steer.')}</p>
      <details class="bo-dev"><summary>${t('br_breakout_dev', 'dev toggles')}</summary>
        <div class="bo-dev-body">
          <div class="bo-rungs"></div>
          <label>saturation <input type="range" class="bo-sat" min="0" max="1" step="0.01"></label>
          <label>speed <input type="range" class="bo-speed" min="0.3" max="1.5" step="0.05"></label>
          <label>force breakout N <input type="number" class="bo-n" min="1" max="60" step="1"></label>
          <div class="bo-dev-row">
            <button type="button" data-do="relapse">relapse now</button>
            <button type="button" data-do="breakout">breakout now</button>
            <button type="button" data-do="auto">rungs auto</button>
          </div>
        </div>
      </details>`;
    root.appendChild(el);
    canvas = el.querySelector('.bo-stage');
    const rungs = el.querySelector('.bo-rungs');
    RUNG_NAMES.forEach((name, i) => {
      const lab = document.createElement('label');
      lab.innerHTML = `<input type="checkbox" data-rung="${i}"> ${i + 1}. ${name} <i>${RUNG_AT[i].toFixed(2)}</i>`;
      rungs.appendChild(lab);
    });
  }

  function onEvent(name, d) {
    renderer.onEvent(name, d);
    const s = game.snapshot();
    if (name === 'brick' && !d.ghost && subs) subs.onBrick(nowS(), s.sat, s.balls[0]);
    else if (name === 'relapse' && subs) subs.reset();
    else if (name === 'wall') { const b = el && el.querySelector('.bo-sp b'); if (b) b.textContent = String(d.sp); }
  }
  const nowS = () => performance.now() / 1000;

  function resize() {
    if (!el || !canvas) return;
    const w = el.clientWidth || root.clientWidth || 480, h = el.clientHeight || root.clientHeight || 720;
    sizeW = w; sizeH = h;
    dpr = Math.min(2, window.devicePixelRatio || 1);
    canvas.style.width = w + 'px'; canvas.style.height = h + 'px';
    renderer.resize(w * dpr, h * dpr);
  }
  function pointerX(e) {
    const r = canvas.getBoundingClientRect();
    return renderer.toField((e.clientX - r.left) * dpr, (e.clientY - r.top) * dpr).x;
  }
  function startAudio() { if (audioOn) return; audioOn = true; try { audio.start(); } catch (e) { /* no audio is fine */ } }

  function syncDev() {
    if (!el) return;
    const s = game.snapshot();
    el.querySelectorAll('input[data-rung]').forEach((cb) => {
      const i = Number(cb.dataset.rung), f = s.force[i];
      cb.checked = !!s.rungs[i];
      cb.parentElement.classList.toggle('is-forced', f === true || f === false);
    });
    const sat = el.querySelector('.bo-sat'); if (sat && document.activeElement !== sat) sat.value = String(s.state === 'grey' ? s.savedSat : s.sat);
    const n = el.querySelector('.bo-n'); if (n && document.activeElement !== n) n.value = String(s.breakoutN);
    const sp = el.querySelector('.bo-speed'); if (sp && document.activeElement !== sp) sp.value = String(s.speedScale);
  }
  function wireDev() {
    const dev = el.querySelector('.bo-dev');
    on(dev, 'change', (e) => {
      const x = e.target;
      if (x.dataset.rung !== undefined) game.setForce(Number(x.dataset.rung), x.checked);
      else if (x.classList.contains('bo-n')) game.setBreakoutN(x.value);
    });
    on(dev, 'input', (e) => {
      const x = e.target;
      if (x.classList.contains('bo-sat')) game.setSaturation(x.value);
      else if (x.classList.contains('bo-speed')) game.setSpeedScale(x.value);
    });
    on(dev, 'click', (e) => {
      const act = e.target.dataset && e.target.dataset.do;
      if (act === 'relapse') game.relapseNow();
      else if (act === 'breakout') game.breakoutNow();
      else if (act === 'auto') game.clearForce();
    });
    on(dev, 'pointerdown', (e) => e.stopPropagation());
    on(dev, 'keydown', (e) => e.stopPropagation());
  }

  function frame(ts) {
    if (!running) return;
    raf = requestAnimationFrame(frame);
    const dt = lastT ? Math.min(0.05, (ts - lastT) / 1000) : 1 / 60;
    lastT = ts;
    if (suspended) return;
    // The stylesheet lands after the first measure and the room can reshape the root without a window resize.
    if ((frames & 7) === 0 && (el.clientWidth !== sizeW || el.clientHeight !== sizeH)) resize();
    game.step(dt, input);
    input.launch = false;
    const s = game.snapshot(), now = ts / 1000;
    media.tick(performance.now());
    let word = subs.current(now);
    if (!word && s.state === 'colour' && s.balls[0]) word = subs.tick(now, s.sat, s.rungs[6], s.balls[0]);
    renderer.draw(s, now, dt, word);
    if ((++frames & 15) === 0) syncDev();
  }

  async function back() {
    if (typeof ctx.standUp === 'function') { try { await ctx.standUp(); return; } catch (e) { /* fall through */ } }
    await close();
  }

  async function open() {
    if (el) return;
    build();
    audio = createAudio({ bpm: num(q, 'bpm', 96) });
    media = createMedia({ ctx, still: reduced, count: 8 });
    game = createGame({ audio, onEvent, breakoutN: num(q, 'n', 12), saturation: Math.max(0, Math.min(1, num(q, 'sat', 0.05))),
      speedScale: num(q, 'speed', 0.55) });
    renderer = createRenderer(canvas, { reduced, media });
    const gates = ctx.gates || {};
    subs = createSubliminals({ words: () => media.words, enabled: gates.subliminal !== false, fx: typeof ctx.fx === 'function' ? ctx.fx : null });
    media.load().catch(() => {});
    resize();
    on(window, 'resize', resize);
    on(canvas, 'pointermove', (e) => { input.x = pointerX(e); });
    on(canvas, 'pointerdown', (e) => { startAudio(); input.x = pointerX(e); input.launch = true; el.classList.add('is-played'); });
    on(canvas, 'touchstart', (e) => { if (e.cancelable) e.preventDefault(); }, { passive: false });
    on(window, 'keydown', (e) => {
      if (e.key === 'ArrowLeft') { input.left = true; input.x = null; }
      else if (e.key === 'ArrowRight') { input.right = true; input.x = null; }
      else if (e.key === ' ' || e.key === 'Enter') { startAudio(); input.launch = true; }
      else if (e.key === 'Escape') { back(); }
      else return;
      e.preventDefault();
    });
    on(window, 'keyup', (e) => {
      if (e.key === 'ArrowLeft') input.left = false;
      else if (e.key === 'ArrowRight') input.right = false;
    });
    const backBtn = el.querySelector('.bo-back');
    if (backBtn) on(backBtn, 'click', back);
    wireDev();
    syncDev();
    running = true; suspended = false; lastT = 0;
    raf = requestAnimationFrame(frame);
  }

  async function close() {
    running = false;
    if (raf) cancelAnimationFrame(raf); raf = 0;
    while (off.length) { try { off.pop()(); } catch (e) { /* noop */ } }
    try { audio && audio.stop(); } catch (e) { /* noop */ }
    try { media && media.dispose(); } catch (e) { /* noop */ }
    try { renderer && renderer.dispose(); } catch (e) { /* noop */ }
    if (el && el.parentNode) el.parentNode.removeChild(el);
    el = canvas = null; audioOn = false;
  }
  function suspend(onOff) {
    suspended = !!onOff;
    if (!audio) return;
    try { if (suspended) audio.stop(); else if (audioOn) audio.start(); } catch (e) { /* noop */ }
    if (!suspended) lastT = 0;
  }
  async function destroy() {
    await close();
    try { audio && audio.destroy && audio.destroy(); } catch (e) { /* noop */ }
    audio = null; game = null; renderer = null; media = null; subs = null;
  }

  return { open, close, suspend, destroy,
    /* dev harness only */
    get game() { return game; }, get renderer() { return renderer; }, get media() { return media; } };
}
