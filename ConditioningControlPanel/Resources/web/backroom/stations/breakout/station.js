/* ============================================================================
 * stations/breakout/station.js - CONTRACT section 7 module for the Breakout
 * cabinet. mount(ctx) -> { open, close, suspend, destroy }. One 2D canvas in
 * ctx.root, the sim in game.js, the picture in render.js, the niche layer in
 * payloads.js, the sound in audio.js. No WebGL, no three.js.
 *
 * UX rule (owner): readable at a glance, feedback on everything, almost no
 * buttons. The chrome is one thin strip (SP chip, combo from 3 up), a chevron
 * Back only when the host has none, one gear for the dev panel, a first-move
 * hint, a countdown under the ghost ball in grey, PAUSED on blur.
 *
 * Wiring: every game event goes to renderer.onGameEvent (falls back to the
 * older onEvent) and to the matching audio cue; the game gets a beat-only
 * audio shim so the sim paces on the bed but never plays sounds itself.
 * ==========================================================================*/

import { createGame, RUNG_NAMES, RUNG_AT } from './game.js';
import { createRenderer } from './render.js';
import { createMedia, createSubliminals } from './payloads.js';
import { createAudio } from './audio.js';

export const roomStage = false;

const num = (q, k, d) => (q.has(k) && !Number.isNaN(Number(q.get(k))) ? Number(q.get(k)) : d);
const DEV_KEY = 'bo.dev.open', FOCUS_R = 140;
const store = {
  get(k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
  set(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* private window */ } },
};

function loadCss() {
  if (document.querySelector('link[data-breakout-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet'; link.href = new URL('./station.css', import.meta.url).href; link.dataset.breakoutCss = '';
  document.head.append(link);
}

export async function mount(ctx) {
  loadCss();
  const root = ctx.root;
  const hostBack = !!ctx.hostBack;
  const q = new URLSearchParams(typeof location !== 'undefined' ? location.search : '');
  const reduced = !!ctx.reduced || q.has('still') ||
    (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const t = (k, f) => { try { const v = typeof ctx.lex === 'function' ? ctx.lex(k, f) : f; return v || f; } catch (e) { return f; } };

  let el = null, canvas = null, game = null, renderer = null, media = null, subs = null, audio = null;
  let raf = 0, running = false, suspended = false, paused = false, lastT = 0, dpr = 1, frames = 0, audioOn = false;
  let sizeW = 0, sizeH = 0, fieldScale = 1, fieldOx = 0, fieldOy = 0, moved = false;
  let lastSat = -1, lastState = '', lastTimeScale = 1, lastCombo = 0, lastSp = 0, sawHit = false;
  const lastCue = {};
  const input = { x: null, left: false, right: false, launch: false };
  const off = [];
  const on = (target, ev, fn, opts) => { target.addEventListener(ev, fn, opts); off.push(() => target.removeEventListener(ev, fn, opts)); };
  const ui = {};

  /* ------------------------------------------------------------ chrome */
  function build() {
    el = document.createElement('div');
    el.className = 'bo-station';
    if (hostBack) el.dataset.hostBack = '';
    const backBtn = hostBack ? '' : `<button class="bo-back" type="button" aria-label="${t('br_breakout_back', 'Back')}"></button>`;
    el.innerHTML = `
      <canvas class="bo-stage"></canvas>
      ${backBtn}
      <div class="bo-hud">
        <span class="bo-sp"><b>0</b> ${t('br_breakout_sp', 'SP')}</span>
        <span class="bo-combo" hidden>0</span>
      </div>
      <p class="bo-hint">${t('br_breakout_hint_move', 'move to play')}</p>
      <p class="bo-ghost-hint" hidden></p>
      <div class="bo-paused" hidden>${t('br_breakout_paused', 'PAUSED')}</div>
      <button class="bo-gear" type="button" aria-label="${t('br_breakout_dev', 'dev toggles')}" aria-expanded="false"></button>
      <div class="bo-dev" hidden>
        <div class="bo-dev-row"><span class="bo-dev-lab">${t('br_breakout_dev_juice', 'Juice')}</span><div class="bo-rungs"></div></div>
        <div class="bo-dev-row"><span class="bo-dev-lab">${t('br_breakout_dev_pace', 'Pace')}</span>
          <label>sat <input type="range" class="bo-sat" min="0" max="1" step="0.01"></label>
          <label>speed <input type="range" class="bo-speed" min="0.3" max="1.5" step="0.05"></label>
          <label>N <input type="number" class="bo-n" min="1" max="60" step="1"></label>
        </div>
        <div class="bo-dev-row"><span class="bo-dev-lab">${t('br_breakout_dev_force', 'Force')}</span>
          <button type="button" data-do="relapse">relapse</button>
          <button type="button" data-do="breakout">breakout</button>
          <button type="button" data-do="auto">rungs auto</button>
          <span class="bo-dev-stats"></span>
        </div>
      </div>`;
    root.appendChild(el);
    canvas = el.querySelector('.bo-stage');
    for (const k of ['hud', 'sp', 'combo', 'hint', 'ghost-hint', 'paused', 'gear', 'dev', 'dev-stats', 'back']) ui[k] = el.querySelector('.bo-' + k);
    const rungs = el.querySelector('.bo-rungs');
    RUNG_NAMES.forEach((name, i) => {
      const lab = document.createElement('label');
      lab.title = `${i + 1}. ${name} from ${RUNG_AT[i].toFixed(2)}`;
      lab.innerHTML = `<input type="checkbox" data-rung="${i}">${name}`;
      rungs.appendChild(lab);
    });
    setDevOpen(store.get(DEV_KEY) === '1');
  }
  function setDevOpen(open) {
    ui.dev.hidden = !open; ui.gear.setAttribute('aria-expanded', String(!!open)); el.classList.toggle('is-dev', !!open);
    store.set(DEV_KEY, open ? '1' : '0');
  }
  function pop(node) { node.classList.remove('is-pop'); void node.offsetWidth; node.classList.add('is-pop'); }
  function setSp(v) { if (v === lastSp) return; lastSp = v; ui.sp.querySelector('b').textContent = String(v); pop(ui.sp); }
  function setCombo(c) {
    if (c === lastCombo) return;
    const show = c >= 3;
    ui.combo.hidden = !show;
    if (show) { ui.combo.textContent = 'x' + c; if (c > lastCombo) pop(ui.combo); }
    lastCombo = c;
  }
  function setPaused(p) {
    if (paused === p) return;
    paused = p; ui.paused.hidden = !p; el.classList.toggle('is-paused', p);
    try { if (p) audio.stop(); else if (audioOn) audio.start(); } catch (e) { /* noop */ }
    if (!p) lastT = 0;
  }
  function firstMove() { if (moved) return; moved = true; el.classList.add('is-played'); }

  /* ------------------------------------------------------------ events */
  const cue = (name, gapMs = 400) => { const n = performance.now(); if (n - (lastCue[name] || -1e9) < gapMs) return false; lastCue[name] = n; return true; };
  const au = (fn, ...args) => { try { if (audio && typeof audio[fn] === 'function') audio[fn](...args); } catch (e) { /* audio optional */ } };
  const hitAu = (kind, d) => au('hit', kind, { combo: (d && d.combo) || 0, x: d && Number.isFinite(d.x) ? d.x / game.snapshot().w : 0.5 });

  function onEvent(name, d) {
    d = d || {};
    try { if (typeof renderer.onGameEvent === 'function') renderer.onGameEvent(name, d); else renderer.onEvent(name, d); } catch (e) { /* cosmetic */ }
    const s = game.snapshot();
    switch (name) {
      case 'hit': sawHit = true; hitAu(d.kind, d); break;
      // Older sims emit the raw collision names instead of 'hit'; route them until a 'hit' shows up.
      case 'wallhit': if (!sawHit) hitAu('wall', { combo: s.combo, x: d.x }); break;
      case 'paddle': if (!sawHit) hitAu('paddle', { combo: 0, x: d.x }); break;
      case 'gif': if (!sawHit) hitAu('gif', { combo: s.combo, x: d.x }); break;
      case 'spiral': if (!sawHit) hitAu('spiral', { combo: s.combo, x: d.x }); break;
      case 'brick':
        if (!sawHit) hitAu('brick', { combo: s.combo, x: d.x });
        if (!d.ghost && s.state === 'colour' && subs) subs.onBrick(nowS(), s.sat, s.balls[0]);
        break;
      case 'perfect': au('perfect'); break;
      case 'nearMiss': au('nearMiss'); break;
      case 'jackpot': au('jackpot'); if (subs && !d.ghost) subs.onJackpot(); break;
      case 'shatterWall': if (cue('shatterWall')) au('shatterWall'); break;
      case 'brickLand': au('brickLand', { x: Number.isFinite(d.x) ? d.x / s.w : 0.5 }); break;
      case 'split': au('split'); break;
      case 'wall': if (cue('wall')) au('wallCleared'); setSp(Number(d.sp) || s.stats.sp || 0); break;
      case 'crack': if (cue('crack', 2000)) au('crack'); break;
      // The slow-mo starts silent (the bed pitches down via setTimeScale); the relapse cue lands on the cut.
      case 'relapseStart': if (subs) subs.reset(); break;
      case 'relapse': if (cue('relapse')) au('relapse'); if (subs) subs.reset(); break;
      // The breakout cue carries its own riser, so it starts with the rewind and the snap is silent.
      case 'breakoutStart': case 'breakout': if (cue('breakout', 1500)) au('breakout'); break;
      default: break;
    }
  }
  const nowS = () => performance.now() / 1000;

  /* ------------------------------------------------------------ geometry */
  function resize() {
    if (!el || !canvas) return;
    const w = el.clientWidth || root.clientWidth || 480, h = el.clientHeight || root.clientHeight || 720;
    sizeW = w; sizeH = h;
    dpr = Math.min(2, window.devicePixelRatio || 1);
    canvas.style.width = w + 'px'; canvas.style.height = h + 'px';
    renderer.resize(w * dpr, h * dpr);
    const a = renderer.toField(0, 0), b = renderer.toField(1, 0);
    fieldScale = 1 / Math.max(1e-6, b.x - a.x); fieldOx = -a.x * fieldScale; fieldOy = -a.y * fieldScale;
  }
  const toCss = (fx, fy) => ({ x: (fx * fieldScale + fieldOx) / dpr, y: (fy * fieldScale + fieldOy) / dpr });
  function pointerX(e) {
    const r = canvas.getBoundingClientRect();
    return renderer.toField((e.clientX - r.left) * dpr, (e.clientY - r.top) * dpr).x;
  }
  function startAudio() { if (audioOn) return; audioOn = true; try { audio.start(); } catch (e) { /* no audio is fine */ } }

  /* ------------------------------------------------------------ dev panel */
  function syncDev() {
    if (!el || ui.dev.hidden) return;
    const s = game.snapshot();
    el.querySelectorAll('input[data-rung]').forEach((cb) => {
      const i = Number(cb.dataset.rung), f = s.force[i];
      cb.checked = !!s.rungs[i];
      cb.parentElement.classList.toggle('is-forced', f === true || f === false);
    });
    const sat = el.querySelector('.bo-sat'); if (sat && document.activeElement !== sat) sat.value = String(s.state === 'grey' ? s.savedSat : s.sat);
    const n = el.querySelector('.bo-n'); if (n && document.activeElement !== n) n.value = String(s.breakoutN);
    const sp = el.querySelector('.bo-speed'); if (sp && document.activeElement !== sp) sp.value = String(s.speedScale);
    const grey = s.state === 'grey' ? `  grey ${s.greyBricks}/${s.breakoutN}` : '';
    ui['dev-stats'].textContent = `bricks ${s.stats.bricks}  walls ${s.stats.walls}  sat ${s.sat.toFixed(2)}${grey}`;
  }
  function wireDev() {
    const dev = ui.dev;
    on(ui.gear, 'click', () => { setDevOpen(dev.hidden); syncDev(); });
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
    for (const n of [dev, ui.gear]) { on(n, 'pointerdown', (e) => e.stopPropagation()); on(n, 'keydown', (e) => e.stopPropagation()); }
  }

  /* ------------------------------------------------------------ frame */
  function syncAudio(s) {
    if (s.state !== lastState) { lastState = s.state; au('setState', s.state); }
    if (s.sat !== lastSat) { lastSat = s.sat; au('setSaturation', s.sat); }
    const ts = Number.isFinite(s.timeScale) ? s.timeScale : 1;
    if (ts !== lastTimeScale) { lastTimeScale = ts; au('setTimeScale', ts); }
  }
  function focusMedia(s) {
    if (typeof media.mark === 'function') {
      for (const br of s.bricks) if (br.alive && typeof br.gif === 'number' && br.gif >= 0) media.mark(br.gif, br.x + br.w / 2, br.y + br.h / 2);
      for (const c of s.colliders || []) if (typeof c.gif === 'number' && c.gif >= 0) media.mark(c.gif, c.x, c.y);
    }
    if (typeof media.setFocus !== 'function') return;
    const ball = s.balls.find(b => !b.lost) || s.balls[0];
    if (ball && s.state === 'colour') media.setFocus(ball.x, ball.y, FOCUS_R); else media.setFocus(NaN, NaN);
  }
  function ghostHint(s) {
    const hint = ui['ghost-hint'];
    const ghost = s.state === 'grey' ? (s.balls.find(b => b.ghost) || s.balls[0]) : null;
    if (!ghost) { if (!hint.hidden) hint.hidden = true; return; }
    const left = Math.max(0, (s.breakoutN | 0) - (s.greyBricks | 0));
    const text = t('br_breakout_hint_grey', 'break {n} to come back').replace('{n}', String(left));
    if (hint.textContent !== text) hint.textContent = text;
    const p = toCss(ghost.x, ghost.y + (ghost.r || 8) + 6);
    hint.style.transform = `translate(${p.x.toFixed(0)}px, ${p.y.toFixed(0)}px) translateX(-50%)`;
    if (hint.hidden) hint.hidden = false;
  }

  function frame(ts) {
    if (!running) return;
    raf = requestAnimationFrame(frame);
    const dt = lastT ? Math.min(0.05, (ts - lastT) / 1000) : 1 / 60;
    lastT = ts;
    if (suspended || paused) return;
    // The stylesheet lands after the first measure and the room can reshape the root without a window resize.
    if ((frames & 7) === 0 && (el.clientWidth !== sizeW || el.clientHeight !== sizeH)) resize();
    game.step(dt, input);
    input.launch = false;
    const s = game.snapshot(), now = ts / 1000;
    syncAudio(s);
    focusMedia(s);
    media.tick(performance.now());
    let word = subs.current(now);
    if (!word && s.state === 'colour' && s.balls[0]) word = subs.tick(now, s.sat, s.rungs[6], s.balls[0]);
    renderer.draw(s, { words: media.trailWords(12), media, now, dt, reduced, word });
    setCombo(s.combo | 0);
    if (s.stats && s.stats.sp !== lastSp) setSp(s.stats.sp);
    ghostHint(s);
    if ((++frames & 15) === 0) syncDev();
  }

  async function back() {
    if (typeof ctx.standUp === 'function') { try { await ctx.standUp(); return; } catch (e) { /* fall through */ } }
    await close();
  }

  /* ------------------------------------------------------------ lifecycle */
  async function open() {
    if (el) return;
    build();
    audio = createAudio({ bpm: num(q, 'bpm', 96) });
    media = createMedia({ ctx, still: reduced, count: 8 });
    // The sim paces on the bed but never plays: every sound is routed from onEvent, so nothing fires twice.
    const beatShim = { beat: audio.beat, now: audio.now };
    game = createGame({ audio: beatShim, onEvent, breakoutN: num(q, 'n', 12), saturation: Math.max(0, Math.min(1, num(q, 'sat', 0.15))),
      speedScale: num(q, 'speed', 0.55) });
    renderer = createRenderer(canvas, { reduced, media });
    sawHit = typeof game.snapshot().combo === 'number';   // a v2 sim emits 'hit'; the raw names are then cosmetic only
    const gates = ctx.gates || {};
    subs = createSubliminals({ words: () => media.words, enabled: gates.subliminal !== false, fx: typeof ctx.fx === 'function' ? ctx.fx : null,
      gifKey: () => (media.keys()[0] || null) });
    media.load().then(() => {
      if (!game || typeof game.setWords !== 'function') return;
      try { game.setWords(media.words.map(w => w.text)); } catch (e) { /* words are optional */ }
    }).catch(() => {});
    resize();
    on(window, 'resize', resize);
    on(canvas, 'pointermove', (e) => { input.x = pointerX(e); if (!moved) { firstMove(); input.launch = true; } });
    on(window, 'pointermove', () => { if (paused) setPaused(false); });
    on(canvas, 'pointerdown', (e) => { startAudio(); input.x = pointerX(e); input.launch = true; firstMove(); if (paused) setPaused(false); });
    on(canvas, 'touchstart', (e) => { if (e.cancelable) e.preventDefault(); }, { passive: false });
    on(window, 'keydown', (e) => {
      if (e.key === 'ArrowLeft') { input.left = true; input.x = null; firstMove(); }
      else if (e.key === 'ArrowRight') { input.right = true; input.x = null; firstMove(); }
      else if (e.key === ' ' || e.key === 'Enter') { startAudio(); input.launch = true; firstMove(); }
      else if (e.key === 'Escape') { back(); }
      else return;
      if (paused) setPaused(false);
      e.preventDefault();
    });
    on(window, 'keyup', (e) => {
      if (e.key === 'ArrowLeft') input.left = false;
      else if (e.key === 'ArrowRight') input.right = false;
    });
    on(window, 'blur', () => { if (moved) setPaused(true); });
    on(document, 'visibilitychange', () => { if (document.hidden && moved) setPaused(true); });
    if (ui.back) on(ui.back, 'click', back);
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
    el = canvas = null; audioOn = false; paused = false; moved = false;
    lastSat = -1; lastState = ''; lastTimeScale = 1; lastCombo = 0; lastSp = 0; sawHit = false;
  }
  function suspend(onOff) {
    suspended = !!onOff;
    if (!audio) return;
    try { if (suspended) audio.stop(); else if (audioOn && !paused) audio.start(); } catch (e) { /* noop */ }
    if (!suspended) lastT = 0;
  }
  async function destroy() {
    await close();
    try { audio && audio.destroy && audio.destroy(); } catch (e) { /* noop */ }
    audio = null; game = null; renderer = null; media = null; subs = null;
  }

  return { open, close, suspend, destroy,
    /* dev harness only */
    get game() { return game; }, get renderer() { return renderer; }, get media() { return media; }, get audio() { return audio; } };
}
