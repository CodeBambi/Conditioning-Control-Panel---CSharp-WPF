/* ============================================================================
 * games/impulse-control/render.js - every pixel of THE DROP TUBE.
 *
 * The stage owns the whole class root (game-immersion law: the window IS the
 * machine). Layers, bottom to top:
 *   .g-ic-bg        the faded fullscreen media loop (two <img> crossfading over
 *                   a gradient that is ALWAYS painted - an empty pool still
 *                   looks dressed)
 *   canvas          the tube body (tube3d -> tube2d -> static, chosen here)
 *   .g-ic-marquee-slot  an EMPTY square ring anchor hugging the basin. The
 *                   casino deck mounts its bulb chase in here; this file only
 *                   sizes and places the box (--ic-basin-d) and swears it will
 *                   never take a pointer.
 *   .g-ic-flourish  the spiral pop flourish (pointer-events:none)
 *   .g-ic-basin     the reveal surface: THE bubble (the one interactive node)
 *   .g-ic-hud       THE LIT MACHINE, pinned to the bottom edge
 *   .g-ic-howto     the class rules SHEET · .g-ic-break intro card
 *   .g-ic-debrief   the machine TICKET
 *
 * HOUSE RULES WAVE - what this file gained, and why it is here and not in a
 * deck:
 *   THE SHEET (Deck VI, Law IV - drawn, not told). showHowto() draws three
 *     vignettes in pure CSS - the mouth dropping a bubble into the dish under a
 *     racing speed bar, the X bubble with its ring running out beside a hand
 *     that must not move, a pale bubble drifting off the rim and costing
 *     nothing - and captions them from the lexicon. The 150-character
 *     ic_tube_rules paragraph it replaces is gone from lex.js. The sheet is
 *     STAGE's because the shapes it draws are the stage's own furniture; a deck
 *     drawing the game's rules would be a deck teaching the class.
 *   THE LIT MACHINE (Deck IV/VI). The score is an ODOMETER: one <i> per digit
 *     in a fixed-width slot, so the casino's scale punch (transform only) can
 *     never reflow a number. The bubble counter is a thread whose head glows;
 *     the topline wears a slow sweep; the HUD's own rail breathes. All of it is
 *     CSS on pseudo-elements and pointer-events:none, so a deck animating
 *     .g-ic-score itself never fights this file.
 *   THE METER SLOT. `nodes.meterSlot` is an empty anchor and stays empty: the
 *     10-segment streak meter is a SHELL primitive (SYNTHESIS #10) that
 *     index.js mounts through ctx.ceremonies.streakMeter. The old private
 *     5-pip meter that used to live here is DELETED - it was a fork.
 *   THE TICKET (Deck V + VI). debrief() prints a receipt: perforated edges, a
 *     slow light sweep, the backdrop still breathing behind it, the grade
 *     arriving as an OBJECT in `nodes.ticketStamp` (index.js drops the shell's
 *     stamp ceremony there). Submit is lit, pulsing and pre-focused; recal is a
 *     ghost. A class that went badly still gets light: `is-dim`, never dark.
 *
 * AUDIO. This file used to synthesise its own chirps off its own AudioContext.
 * That was a straight violation of web CLAUDE.md trap 18 - shell/audio.js is
 * the only thing in the Arcademy that may hold an audio node, and the engine's
 * `audio_trigger` is the sanctioned road to it. Every oscillator is GONE; the
 * casino deck owns the cue ladder now. deniedSting() (assets/denied.mp3, the
 * owner-approved X-hit sample) rides the engine's audio_trigger clip path as of
 * W0 2026-08-24 - mixer laws apply - and its raw <Audio> survives only as the
 * engine-less fallback, deliberately the only such element in this folder.
 *
 * INPUT TRUST (Law II): the bubble is the single tap target; every decorative
 * layer, slot and sheet figure is pointer-events:none. The reveal is
 * class-toggle + src swap only - nothing ever delays the reveal paint (RT
 * integrity).
 *
 * NEVER STILL (Law III): the lamps breathe, the thread head pulses, the topline
 * sweeps, the ticket's light crawls. Every one of those is a CSS animation, so
 * `.suspended` (animation-play-state:paused, pseudo-elements included) and
 * prefers-reduced-motion both freeze the whole room from the stylesheet, even
 * if a JS path forgets.
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
  /* W0: index.js hands us the sanctioned road for the denied sample - a hook
     onto the engine's audio_trigger clip path, so the mixer's laws apply. */
  const sting = typeof o.sting === 'function' ? o.sting : null;
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
  let deniedAudio = null;
  let onResize = null;
  let scoreText = null;  // the odometer's last painted string

  /* ------------------------------------------------------------------ sfx */
  /* THE ONE SURVIVING SAMPLE. Every synthesised chirp this file used to make is
     gone (trap 18: shell/audio.js owns audio, the engine's audio_trigger is the
     road, and casino.js drives the ladder). denied.mp3 is the owner-approved
     X-hit sample; since W0 it travels that same road. */
  function deniedSting() {
    /* W0 (2026-08-24): the grandfather clause is repaid. The sample is now
       REQUESTED through the engine's clip path (o.sting -> audio_trigger with a
       url), so mute, master volume, the fx bus level and ducking all finally
       apply to the one real sample in the build. The raw element below survives
       ONLY as the engine-less fallback (sting missing, or fire() answered null
       because the class has no engine) - the X-hit never goes silent. */
    if (sting) {
      try { if (sting(url(DENIED_SFX)) != null) return; } catch (e) { /* fall through */ }
    }
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
    nodes.bg = bg;
    nodes.bgA = el('img', 'g-ic-bg-img', bg);
    nodes.bgB = el('img', 'g-ic-bg-img', bg);
    /* depth: melts the media's EDGES into atmosphere (centre stays legible),
       then the veil vignettes the whole ground - wallpaper becomes depth */
    nodes.depth = el('div', 'g-ic-bg-depth', bg);
    nodes.veil = el('div', 'g-ic-bg-veil', bg);

    nodes.tubewrap = el('div', 'g-ic-tubewrap', stage);
    /* THE DUSK: a dimmer laid OVER the tube and under everything the player
       reads. pressure.js drives its opacity with the rung: the brighter the
       fullscreen effects get, the darker the tube under them, so the engine's
       washes and gifs read IN FRONT of a chute that would otherwise out-shine
       them (owner 2026-08-22: "effects still behind the tube" - they were not;
       they were drowned). Empty, pointer-events:none, opacity 0 at rest. */
    nodes.dusk = el('div', 'g-ic-dusk', stage);
    /* the casino's ring hangs here: a square --ic-basin-d box centred on the
       basin, empty, and pointer-events:none forever */
    nodes.marqueeSlot = el('div', 'g-ic-marquee-slot', stage);
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
    nodes.topline = topline;
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
    paintScore(0);
    nodes.rt = el('div', 'g-ic-rt', mid);
    const right = el('div', 'g-ic-hud-cell g-ic-hud-right', hud);
    nodes.streakLabel = el('div', 'g-ic-streak-label', right);
    if (nodes.streakLabel) nodes.streakLabel.textContent = t('ic_streak', 'streak');
    /* EMPTY ON PURPOSE. ctx.ceremonies.streakMeter({target: meterSlot}) fills
       it from the shell; this class does not own a streak meter. */
    nodes.meterSlot = el('div', 'g-ic-meter-slot', right);

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
      p = import('./tube3d.js').then((m) => m.createTube3D({
        mount: nodes.tubewrap, reduced, perf, seed,
        /* THE LANDING: the 3D tube says where its visible hole is; the basin,
           the marquee ring, the flourish and the stamp all hang off these two
           custom properties (style.js). tube2d never calls it: 50%/50% stands. */
        onLanding(pt) {
          try {
            const st = nodes.stage;
            if (!st || !st.style || !pt) return;
            st.style.setProperty('--ic-basin-x', (Number(pt.x) || 50).toFixed(2) + '%');
            st.style.setProperty('--ic-basin-y', (Number(pt.y) || 50).toFixed(2) + '%');
          } catch (e) { /* cosmetic */ }
        },
      }));
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
  }

  function popBubble(good) {
    const bub = nodes.bubble;
    if (bub) { bub.classList.remove('on'); bub.classList.add('pop'); }
    tubeCall('pop', good);
  }
  function fadeBubble() {
    const bub = nodes.bubble;
    if (bub) { bub.classList.remove('on'); bub.classList.add('fade'); }
  }
  function deniedPassed() {
    const bub = nodes.bubble;
    if (bub) { bub.classList.remove('on'); bub.classList.add('fade'); }
    tubeCall('denyPass');
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

  /* THE ODOMETER. One element per digit in a fixed-width slot: the casino
     punches .g-ic-score with a transform and the number never reflows, and a
     trickster that writes over .g-ic-score's textContent is repaired by the
     next hud() (the digit children are gone, so the string is repainted). */
  function paintScore(v) {
    const host = nodes.score;
    if (!host) return;
    const txt = String(Math.max(0, Math.round(Number(v) || 0)));
    const kids = (host.children && host.children.length) || 0;
    if (txt === scoreText && kids === txt.length) return;
    scoreText = txt;
    try {
      host.textContent = '';
      for (let i = 0; i < txt.length; i++) {
        const d = el('i', 'g-ic-dig', host);
        if (d) d.textContent = txt.charAt(i);
      }
    } catch (e) { /* a plain string is still a score */ }
  }

  function hud(h) {
    /* the living material follows the class: progress + streak drive the
       tube's pattern, spin and tint (setMood is cosmetic and throw-guarded) */
    if (h.n != null && h.total) {
      lastMood = { progress: Math.min(1, h.n / h.total), streak: h.streak || 0 };
      tubeCall('setMood', lastMood);
    }
    if (h.score != null) paintScore(h.score);
    if (nodes.rt) nodes.rt.textContent = (showRt && h.rt != null) ? Math.round(h.rt) + 'ms' : '';
    if (nodes.counter && h.n != null) {
      nodes.counter.textContent = t('ic_bubble_n', 'Bubble') + ' ' + h.n + ' / ' + h.total;
    }
    if (nodes.threadFill && h.n != null && h.total) {
      nodes.threadFill.style.setProperty('--ic-prog', String(Math.min(1, h.n / h.total)));
    }
    /* the streak meter is the SHELL's (ceremonies.streakMeter into meterSlot);
       this class never draws one of its own */
    if (nodes.subject && h.subject) nodes.subject.textContent = h.subject;
  }

  /* ------------------------------------------------------ class rules sheet */
  /* DECK VI, LAW IV. Three drawn vignettes and one way out. No raster art: the
     mouth, the dish, the bubbles, the ring, the hand and the speed bar are all
     gradients and borders, so the sheet costs nothing and reads at any size.
     Every figure is pointer-events:none; the GO button is the only live thing
     on it and the ONLY dismissal (index.js binds no key to the sheet). */
  function showHowto(opts) {
    const o2 = opts || {};
    hideHowto();
    if (!nodes.stage) return null;
    const sheet = el('div', 'g-ic-howto', nodes.stage);
    if (!sheet) return null;
    nodes.howto = sheet;

    const h = el('h2', 'g-ic-hw-title', sheet);
    if (h) h.textContent = t('ic_howto_title', 'Class rules');

    const row = (build, caption) => {
      const r = el('div', 'g-ic-hw-row', sheet);
      if (!r) return null;
      const fig = el('span', 'g-ic-hw-fig', r);
      if (fig) { try { build(fig); } catch (e) { /* a caption alone still teaches */ } }
      const p = el('p', 'g-ic-hw-cap', r);
      if (p) p.textContent = caption;
      return r;
    };

    /* 1 - THE DROP. The mouth spits a pink bubble into the dish, a tap lands on
       it, and the speed bar races down beside it: pop fast, speed is the score. */
    row((fig) => {
      const scene = el('span', 'g-ic-hw-scene', fig);
      el('span', 'g-ic-hw-mouth', scene);
      el('span', 'g-ic-hw-spark', scene);
      const dish = el('span', 'g-ic-hw-dish', scene);
      el('span', 'g-ic-hw-bub', dish);
      el('span', 'g-ic-hw-ping', dish);
      const tap = el('span', 'g-ic-hw-tap' + (o2.coarse ? ' finger' : ' cap'), fig);
      if (tap && !o2.coarse) tap.textContent = String(o2.keyLabel || '');
      const bar = el('span', 'g-ic-hw-speed', fig);
      el('i', null, bar);
    }, t('ic_howto_pop', 'A bubble lands in the dish. Pop it at once. The faster you are, the more it pays.'));

    /* 2 - THE X. The ring drains around a crossed bubble; the hand beside it
       wears a bar through it. Touch nothing for two seconds. */
    row((fig) => {
      const scene = el('span', 'g-ic-hw-scene', fig);
      const xb = el('span', 'g-ic-hw-xbub', scene);
      el('span', 'g-ic-hw-ring', xb);
      const cross = el('span', 'g-ic-hw-cross', xb);
      if (cross) { el('i', 'g-ic-hw-xa', cross); el('i', 'g-ic-hw-xb', cross); }
      el('span', 'g-ic-hw-nohand', fig);
    }, t('ic_howto_x', 'A bubble wearing an X is a trap. Touch nothing until its ring runs out.'));

    /* 3 - THE DRIFT. A pale bubble slides off the rim and simply stops being
       there. Deliberately NO penalty glyph: the absence is the lesson. */
    row((fig) => {
      const scene = el('span', 'g-ic-hw-scene', fig);
      const dish = el('span', 'g-ic-hw-dish', scene);
      el('span', 'g-ic-hw-pale', dish);
    }, t('ic_howto_drift', 'A bubble you miss just drifts off the dish. Nothing is taken from you.'));

    const go = el('button', 'g-ic-hw-go', sheet);
    if (go) {
      go.type = 'button';
      go.textContent = t('ic_howto_go', 'Start the drop');
      go.setAttribute('autofocus', '');
      go.addEventListener('click', () => {
        try { if (typeof o2.onGo === 'function') o2.onGo(); } catch (e) { /* noop */ }
      });
      try { if (typeof go.focus === 'function') go.focus(); } catch (e) { /* noop */ }
    }
    return sheet;
  }

  function hideHowto() {
    if (nodes.howto) { try { nodes.howto.remove(); } catch (e) { /* noop */ } nodes.howto = null; }
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

  /* THE TICKET (Deck V + Deck VI). Same signature, same fields, plus the two
     optional flags the casino decides: d.perfect (no X popped, nothing drifted)
     and d.royal. The grade arrives as an OBJECT: index.js drops the shell's
     stamp ceremony into nodes.ticketStamp. Nothing here grades anything - every
     number on this receipt was handed to it. */
  function debrief(d, onSubmit, onRecal) {
    clearCard();
    hideHowto();
    if (nodes.hud) nodes.hud.classList.add('off');
    if (nodes.bubble) nodes.bubble.classList.remove('on');
    const wrap = el('div', 'g-ic-debrief', nodes.stage);
    if (!wrap) return;
    nodes.card = wrap;

    const royal = !!d.royal;
    const perfect = !!d.perfect;
    /* losses disguised: a bad class is DIM, never dark - the machine does not
       go silent on you, because silence is where people stand up */
    const dim = !royal && !perfect && (d.xClicked > 0);
    const paper = el('div', 'g-ic-paper g-ic-ticket'
      + (royal ? ' is-royal' : '') + (perfect ? ' is-perfect' : '') + (dim ? ' is-dim' : ''), wrap);
    el('i', 'g-ic-ticket-sweep', paper);

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
    /* the stamp bed: empty, pointer-events:none, waiting for the ceremony */
    nodes.ticketStamp = el('div', 'g-ic-ticket-stamp', scoreRow);

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

    /* one-more framing (Deck V): submit is lit, pulsing and pre-focused; the
       ghost beside it is honest, live and never moves */
    const row = el('div', 'g-ic-paper-actions', paper);
    const submitBtn = el('button', 'btn g-ic-submit', row);
    if (submitBtn) {
      submitBtn.type = 'button';
      submitBtn.textContent = t('ic_submit', 'Submit report');
      submitBtn.setAttribute('autofocus', '');
      submitBtn.addEventListener('click', () => { try { onSubmit(); } catch (e) { /* noop */ } });
      try { if (typeof submitBtn.focus === 'function') submitBtn.focus(); } catch (e) { /* noop */ }
    }
    if (onRecal) {
      const rec = el('button', 'btn ghost g-ic-recal', row);
      if (rec) {
        rec.type = 'button';
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
    try { if (deniedAudio && deniedAudio.pause) deniedAudio.pause(); } catch (e) { /* noop */ }
    deniedAudio = null;
    try { if (nodes.stage) nodes.stage.remove(); } catch (e) { /* noop */ }
  }

  return {
    nodes,
    mount, intro, clearCard, debrief,
    showHowto, hideHowto,
    showLoad, setTravel, revealBubble, popBubble, fadeBubble, deniedPassed, hitDenied,
    flourish, stamp, hud, swapBg, setBgFade,
    suspend, destroy,
    tubeKind: () => (tube ? tube.kind : 'none'),
  };
}
