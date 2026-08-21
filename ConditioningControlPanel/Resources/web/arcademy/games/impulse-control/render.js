/* ============================================================================
 * games/impulse-control/render.js - every pixel of THE DROP TUBE.
 *
 * The stage owns the whole class root (game-immersion law: the window IS the
 * machine). Layers, bottom to top:
 *   .g-ic-bg        the faded fullscreen media loop (two <img> crossfading over
 *                   a gradient that is ALWAYS painted - an empty pool still
 *                   looks dressed)
 *   canvas          the tube body (tube3d -> tube2d -> static, chosen here)
 *   .g-ic-flourish  the spiral pop flourish (pointer-events:none)
 *   .g-ic-basin     the reveal surface: THE bubble (the one interactive node)
 *   .g-ic-hud       score / streak / progress, pinned to the bottom edge
 *   .g-ic-break     intro card · .g-ic-debrief  the report
 *
 * INPUT TRUST: the bubble is the single tap target; every decorative layer is
 * pointer-events:none. The reveal is class-toggle + src swap only - nothing
 * ever delays the reveal paint (RT integrity).
 *
 * All methods are throw-guarded: a cosmetic failure may never take the class
 * down. Under the DOM double the tube resolves to its static tier and audio
 * resolves to silence.
 * ==========================================================================*/

import { ensureStyle } from './style.js';
import { createTube2D } from './tube2d.js';

const FLAVOR_SRC = {
  flash: './assets/flash.png',
  spiral: './assets/spiral.png',
  sub: './assets/subliminal.png',
};
const BUBBLE_SRC = './assets/bubble.png';
const DENIED_SFX = './assets/denied.mp3';

function url(rel) {
  try { return new URL(rel, import.meta.url).href; } catch (e) { return rel; }
}

export function createRender(o = {}) {
  const root = o.root;
  const t = o.t || ((k, f) => f);
  const reduced = !!o.reduced;
  const perf = !!o.perf;
  const showRt = o.showRt !== false;
  const log = o.log || (() => {});
  /* the class seed rides down into the tube: every class grows its own skin,
     and a retake (same seed, by law) wears the same one */
  const seed = o.seed == null ? '' : String(o.seed);

  ensureStyle();

  const doc = (typeof document !== 'undefined') ? document : null;
  const el = (tag, cls, parent) => {
    const n = doc ? doc.createElement(tag) : null;
    if (!n) return null;
    if (cls) n.className = cls;
    if (parent) parent.appendChild(n);
    return n;
  };

  const nodes = {};
  /* classList.toggle is missing under the DOM double - add/remove only */
  const setCls = (n, cls, on) => { try { if (n) n.classList[on ? 'add' : 'remove'](cls); } catch (e) { /* noop */ } };
  let tube = null;
  let tubeDead = false;
  let lastMood = null;   // replayed onto the 3d tube when its async import lands
  let bgActive = 0;
  let bgFade = 0.35;
  let audioCtx = null;
  let deniedAudio = null;
  let onResize = null;

  /* ------------------------------------------------------------------ sfx */
  function chirp(freq, ms, gain) {
    if (reduced && gain > 0.1) gain = 0.1;
    try {
      const AC = (typeof AudioContext === 'function') ? AudioContext
        : (typeof webkitAudioContext === 'function' ? webkitAudioContext : null);
      if (!AC) return;
      if (!audioCtx) audioCtx = new AC();
      const osc = audioCtx.createOscillator();
      const g = audioCtx.createGain();
      osc.frequency.value = freq;
      osc.type = 'sine';
      g.gain.setValueAtTime(gain, audioCtx.currentTime);
      g.gain.exponentialRampToValueAtTime(0.0001, audioCtx.currentTime + ms / 1000);
      osc.connect(g); g.connect(audioCtx.destination);
      osc.start(); osc.stop(audioCtx.currentTime + ms / 1000 + 0.02);
    } catch (e) { /* silence is legal */ }
  }
  function deniedSting() {
    try {
      if (typeof Audio !== 'function') return;
      if (!deniedAudio) { deniedAudio = new Audio(url(DENIED_SFX)); deniedAudio.volume = 0.45; }
      deniedAudio.currentTime = 0;
      const p = deniedAudio.play();
      if (p && typeof p.catch === 'function') p.catch(() => {});
    } catch (e) { /* noop */ }
  }

  /* ---------------------------------------------------------------- mount */
  function mount() {
    const stage = el('div', 'g-ic', null);
    if (!stage) return;
    nodes.stage = stage;

    const bg = el('div', 'g-ic-bg', stage);
    nodes.bgA = el('img', 'g-ic-bg-img', bg);
    nodes.bgB = el('img', 'g-ic-bg-img', bg);
    /* depth: melts the media's EDGES into atmosphere (centre stays legible),
       then the veil vignettes the whole ground - wallpaper becomes depth */
    nodes.depth = el('div', 'g-ic-bg-depth', bg);
    nodes.veil = el('div', 'g-ic-bg-veil', bg);

    nodes.tubewrap = el('div', 'g-ic-tubewrap', stage);
    nodes.flourish = el('div', 'g-ic-flourish', stage);

    const basin = el('div', 'g-ic-basin', stage);
    nodes.basin = basin;
    const bubble = el('div', 'g-ic-bubble', basin);
    if (bubble) {
      bubble.setAttribute('role', 'button');
      bubble.setAttribute('aria-label', t('ic_pop', 'POP'));
    }
    nodes.bubble = bubble;
    nodes.bubbleImg = el('img', 'g-ic-bubble-img', bubble);
    const x = el('div', 'g-ic-x', bubble);
    if (x) { el('i', 'g-ic-x-a', x); el('i', 'g-ic-x-b', x); }
    nodes.x = x;
    nodes.holdring = el('div', 'g-ic-holdring', bubble);
    nodes.stamp = el('div', 'g-ic-stamp', stage);

    const topline = el('div', 'g-ic-topline', stage);
    nodes.title = el('span', 'g-ic-topname', topline);
    if (nodes.title) nodes.title.textContent = t('ic_tube_title', 'The Drop Tube');
    nodes.subject = el('span', 'g-ic-topsub', topline);

    const hud = el('div', 'g-ic-hud', stage);
    nodes.hud = hud;
    const left = el('div', 'g-ic-hud-cell g-ic-hud-left', hud);
    nodes.counter = el('div', 'g-ic-counter', left);
    nodes.thread = el('div', 'g-ic-thread', left);
    nodes.threadFill = el('i', 'g-ic-thread-fill', nodes.thread);
    const mid = el('div', 'g-ic-hud-cell g-ic-hud-mid', hud);
    nodes.scoreLabel = el('div', 'g-ic-score-label', mid);
    if (nodes.scoreLabel) nodes.scoreLabel.textContent = t('ic_score', 'Score');
    nodes.score = el('div', 'g-ic-score', mid);
    if (nodes.score) nodes.score.textContent = '0';
    nodes.rt = el('div', 'g-ic-rt', mid);
    const right = el('div', 'g-ic-hud-cell g-ic-hud-right', hud);
    nodes.streakLabel = el('div', 'g-ic-streak-label', right);
    if (nodes.streakLabel) nodes.streakLabel.textContent = t('ic_streak', 'streak');
    nodes.pips = el('div', 'g-ic-pips', right);
    if (nodes.pips) for (let i = 0; i < 5; i++) el('i', 'g-ic-pip', nodes.pips);

    root.appendChild(stage);

    /* the tube: 3d, else 2d, else static - never a throw */
    createTubeChain();

    if (typeof window !== 'undefined' && window.addEventListener) {
      onResize = () => { try { if (tube) tube.resize(); } catch (e) { /* noop */ } };
      window.addEventListener('resize', onResize);
    }
  }

  function createTubeChain() {
    const fall2d = () => {
      if (tubeDead) return;
      try { tube = createTube2D({ mount: nodes.tubewrap, reduced, seed }); }
      catch (e) { tube = createTube2D({ mount: null }); }
      log('tube: ' + tube.kind);
    };
    let p = null;
    try {
      p = import('./tube3d.js').then((m) => m.createTube3D({ mount: nodes.tubewrap, reduced, perf, seed }));
    } catch (e) { p = null; }
    if (p && typeof p.then === 'function') {
      p.then((t3) => {
        if (tubeDead) { try { t3.destroy(); } catch (e) { /* noop */ } return; }
        tube = t3;
        log('tube: 3d');
        if (lastMood) { try { t3.setMood(lastMood); } catch (e) { /* noop */ } }
      }).catch((e) => {
        log('tube: webgl unavailable (' + ((e && e.message) || e) + ') - 2d fallback');
        fall2d();
      });
    } else fall2d();
  }
  const tubeCall = (fn, a) => { try { if (tube && tube[fn]) tube[fn](a); } catch (e) { /* noop */ } };

  /* ------------------------------------------------------------------- bg */
  function setBgFade(v) {
    bgFade = Math.max(0, Math.min(0.8, Number(v) || 0));
    applyBg();
  }
  function applyBg() {
    const act = bgActive === 0 ? nodes.bgA : nodes.bgB;
    const idle = bgActive === 0 ? nodes.bgB : nodes.bgA;
    if (act) { act.style.opacity = String(bgFade); act.classList.add('on'); }
    if (idle) { idle.style.opacity = '0'; idle.classList.remove('on'); }
  }
  /** Swap the backdrop to a new url (crossfades when it loads). */
  function swapBg(u) {
    if (!u || !nodes.bgA) return;
    const next = bgActive === 0 ? nodes.bgB : nodes.bgA;
    const flip = () => { bgActive = bgActive === 0 ? 1 : 0; applyBg(); };
    try {
      let done = false;
      next.onload = () => { if (!done) { done = true; flip(); } };
      next.onerror = () => { done = true; };
      next.src = u;
      /* a cached image may never fire onload under some webviews */
      if (next.complete) { if (!done) { done = true; flip(); } }
    } catch (e) { /* keep the old backdrop */ }
  }

  /* --------------------------------------------------------------- bubble */
  function showLoad() {
    tubeCall('loadPulse');
  }
  function setTravel(p) { tubeCall('setTravel', p); }

  /** The reveal: paint is class + src only - never delayed. */
  function revealBubble(b) {
    const bub = nodes.bubble;
    if (!bub) return;
    bub.classList.remove('pop', 'fade', 'hit');
    if (nodes.bubbleImg) nodes.bubbleImg.src = url(b.kind === 'denied' ? BUBBLE_SRC : (FLAVOR_SRC[b.flavor] || BUBBLE_SRC));
    setCls(nodes.x, 'on', b.kind === 'denied');
    if (nodes.holdring) {
      nodes.holdring.classList.remove('on');
      if (b.kind === 'denied') {
        nodes.holdring.style.setProperty('--ic-hold', b.windowMs + 'ms');
        /* restart the CSS countdown */
        void (bub.offsetWidth);
        nodes.holdring.classList.add('on');
      }
    }
    bub.classList.add('on');
    tubeCall('reveal');
    if (!reduced) chirp(b.kind === 'denied' ? 190 : 620, 90, 0.05);
  }

  function popBubble(good) {
    const bub = nodes.bubble;
    if (bub) { bub.classList.remove('on'); bub.classList.add('pop'); }
    tubeCall('pop', good);
    chirp(good ? 880 : 240, 140, 0.12);
  }
  function fadeBubble() {
    const bub = nodes.bubble;
    if (bub) { bub.classList.remove('on'); bub.classList.add('fade'); }
  }
  function deniedPassed() {
    const bub = nodes.bubble;
    if (bub) { bub.classList.remove('on'); bub.classList.add('fade'); }
    tubeCall('denyPass');
    chirp(520, 160, 0.08);
  }
  function hitDenied() {
    const bub = nodes.bubble;
    if (bub) { bub.classList.remove('on'); bub.classList.add('hit'); }
    if (nodes.stage) {
      nodes.stage.classList.remove('shake');
      void (nodes.stage.offsetWidth);
      nodes.stage.classList.add('shake');
    }
    /* the tube takes the hit too: jolt + red flash + reversed flow (denyHit);
       an older/static tier without it falls back to the plain pop beat */
    try {
      if (tube && typeof tube.denyHit === 'function') tube.denyHit();
      else if (tube && typeof tube.pop === 'function') tube.pop(false);
    } catch (e) { /* noop */ }
    deniedSting();
  }

  /** The spiral flourish - the one effect drawn in-game (engine has no spiral). */
  function flourish() {
    if (!nodes.flourish || !doc) return;
    try {
      const img = doc.createElement('img');
      img.className = 'g-ic-flourish-img';
      img.src = url(FLAVOR_SRC.spiral);
      nodes.flourish.appendChild(img);
      setTimeout(() => { try { img.remove(); } catch (e) { /* noop */ } }, reduced ? 240 : 1000);
    } catch (e) { /* noop */ }
  }

  /* ----------------------------------------------------------------- text */
  function stamp(kind, text) {
    const s = nodes.stamp;
    if (!s) return;
    s.textContent = text;
    s.className = 'g-ic-stamp on ' + (kind || '');
    void (s.offsetWidth);
  }
  function hud(h) {
    /* the living material follows the class: progress + streak drive the
       tube's pattern, spin and tint (setMood is cosmetic and throw-guarded) */
    if (h.n != null && h.total) {
      lastMood = { progress: Math.min(1, h.n / h.total), streak: h.streak || 0 };
      tubeCall('setMood', lastMood);
    }
    if (nodes.score && h.score != null) nodes.score.textContent = String(Math.max(0, Math.round(h.score)));
    if (nodes.rt) nodes.rt.textContent = (showRt && h.rt != null) ? Math.round(h.rt) + 'ms' : '';
    if (nodes.counter && h.n != null) {
      nodes.counter.textContent = t('ic_bubble_n', 'Bubble') + ' ' + h.n + ' / ' + h.total;
    }
    if (nodes.threadFill && h.n != null && h.total) {
      nodes.threadFill.style.setProperty('--ic-prog', String(Math.min(1, h.n / h.total)));
    }
    if (nodes.pips && h.streak != null) {
      const kids = nodes.pips.children || [];
      for (let i = 0; i < kids.length; i++) {
        setCls(kids[i], 'on', i < (h.streak % 5 === 0 && h.streak > 0 ? 5 : h.streak % 5));
      }
    }
    if (nodes.subject && h.subject) nodes.subject.textContent = h.subject;
  }

  /* ------------------------------------------------------------ big cards */
  function intro(o2) {
    clearCard();
    const card = el('div', 'g-ic-break', nodes.stage);
    if (!card) return;
    nodes.card = card;
    const h = el('h2', 'g-ic-break-title', card);
    if (h) h.textContent = o2.title;
    const p = el('p', 'g-ic-break-note', card);
    if (p) p.textContent = o2.note;
    const hint = el('p', 'g-ic-break-hint', card);
    if (hint) hint.textContent = o2.hint || '';
  }
  function clearCard() {
    if (nodes.card) { try { nodes.card.remove(); } catch (e) { /* noop */ } nodes.card = null; }
  }

  function debrief(d, onSubmit, onRecal) {
    clearCard();
    if (nodes.hud) nodes.hud.classList.add('off');
    if (nodes.bubble) nodes.bubble.classList.remove('on');
    const wrap = el('div', 'g-ic-debrief', nodes.stage);
    if (!wrap) return;
    nodes.card = wrap;
    const paper = el('div', 'g-ic-paper', wrap);
    const head = el('div', 'g-ic-paper-head', paper);
    const ttl = el('h2', null, head);
    if (ttl) ttl.textContent = t('ic_debrief', 'Debrief');
    const sub = el('span', 'g-ic-paper-sub', head);
    if (sub) sub.textContent = t('ic_subject', 'Subject') + ' #' + d.subject;

    const scoreRow = el('div', 'g-ic-paper-score', paper);
    const sv = el('b', null, scoreRow);
    if (sv) sv.textContent = String(Math.max(0, d.score));
    const sl = el('span', null, scoreRow);
    if (sl) sl.textContent = t('ic_score', 'Score');

    const grid = el('div', 'g-ic-paper-grid', paper);
    const cell = (label, value, cls) => {
      const c = el('div', 'g-ic-cell ' + (cls || ''), grid);
      const v = el('b', null, c);
      if (v) v.textContent = value;
      const l = el('span', null, c);
      if (l) l.textContent = label;
    };
    cell(t('ic_median_rt', 'median pop'), d.medianRt == null ? '-' : Math.round(d.medianRt) + 'ms');
    cell(t('ic_best_rt', 'best pop'), d.bestRt == null ? '-' : Math.round(d.bestRt) + 'ms', d.newBest ? 'gold' : '');
    cell(t('ic_baseline', 'baseline'), d.baselineMs ? Math.round(d.baselineMs) + 'ms' : '-');
    cell(t('ic_popped', 'popped'), d.popped + ' / ' + d.goodShown);
    cell(t('ic_x_held', 'X held'), String(d.deniedHeld), d.xClicked === 0 ? 'good' : '');
    cell(t('ic_x_popped', 'X popped'), String(d.xClicked), d.xClicked > 0 ? 'bad' : 'good');

    const line = el('p', 'g-ic-paper-line', paper);
    if (line) line.textContent = d.line || '';
    if (d.hint) {
      const hint = el('p', 'g-ic-paper-hint', paper);
      if (hint) hint.textContent = d.hint;
    }

    const row = el('div', 'g-ic-paper-actions', paper);
    const submitBtn = el('button', 'btn g-ic-submit', row);
    if (submitBtn) {
      submitBtn.textContent = t('ic_submit', 'Submit report');
      submitBtn.addEventListener('click', () => { try { onSubmit(); } catch (e) { /* noop */ } });
    }
    if (onRecal) {
      const rec = el('button', 'btn ghost g-ic-recal', row);
      if (rec) {
        rec.textContent = t('ic_recalibrate', 'Recalibrate baseline');
        let armed = false;
        rec.addEventListener('click', () => {
          if (!armed) { armed = true; rec.textContent = t('ic_recalibrate_confirm', 'Tap again to confirm'); return; }
          rec.textContent = t('ic_recalibrated', 'Baseline cleared - the next class recalibrates.');
          rec.disabled = true;
          try { onRecal(); } catch (e) { /* noop */ }
        });
      }
    }
  }

  /* ------------------------------------------------------------ lifecycle */
  function suspend(on) {
    setCls(nodes.stage, 'suspended', !!on);
    tubeCall('suspend', on);
  }
  function destroy() {
    tubeDead = true;
    try { if (tube) tube.destroy(); } catch (e) { /* noop */ }
    tube = null;
    try {
      if (onResize && typeof window !== 'undefined' && window.removeEventListener) window.removeEventListener('resize', onResize);
    } catch (e) { /* noop */ }
    try { if (audioCtx && audioCtx.close) audioCtx.close(); } catch (e) { /* noop */ }
    try { if (nodes.stage) nodes.stage.remove(); } catch (e) { /* noop */ }
  }

  return {
    nodes,
    mount, intro, clearCard, debrief,
    showLoad, setTravel, revealBubble, popBubble, fadeBubble, deniedPassed, hitDenied,
    flourish, stamp, hud, swapBg, setBgFade,
    suspend, destroy,
    tubeKind: () => (tube ? tube.kind : 'none'),
  };
}
