/* ============================================================================
 * ui/menu.js — the MAIN MENU (title screen) for "Graded Intake".
 *
 * The storefront. Bubbly font, bouncing letters, clouds, sparkles, a rainbow —
 * and one small red paragraph telling you exactly what is about to be done with
 * your answers. The clash IS the product: this screen is deliberately the
 * friendliest thing in the app, and it sits directly in front of the clinical
 * briefing (boot.js runBriefing) which is deliberately the coldest.
 *
 * Contract:
 *   showMainMenu(ctx) -> Promise<'begin' | 'quit'>
 *
 *   ctx = { theme?, config?, audio?, background?, stats? }  (every field optional)
 *
 *   `stats` is boot's core/stats.js handle. When it reports at least one
 *   completed run, the menu grows a fourth button ("Your file") that opens the
 *   archive (ui/history.js) — invisible on a first-ever run, present from the
 *   second onward. Absent stats simply means no such button.
 *
 * Guarantees:
 *   - resolves EXACTLY once, and only after its DOM, listeners and timers are
 *     gone, so nothing from the storefront leaks into the run;
 *   - never throws: a missing ui/options.js, a dead audio graph or a hostile
 *     stylesheet all degrade to "the menu still works";
 *   - zero rAF and (in the steady state) zero timers — all motion is CSS
 *     keyframes with randomized inline delays, because a three.js tube is
 *     already rendering behind this panel.
 *
 * Ownership: this module owns ui/menu.js and the `MAIN MENU` section at the end
 * of ui/kawaii.css. It does NOT own ui/options.js — that module is imported
 * dynamically and treated as optional.
 * ========================================================================== */

import { getPrefs, subscribe } from './prefs.js';
import { startMenuMusic, stopMenuMusic, isMusicOn, toggleMusic } from './menuMusic.js';
import { playWelcome, stopWelcome } from './menuVoice.js';

/* ----------------------------------------------------------------------------
 * COPY — the user-facing text, verbatim (typos fixed, register untouched).
 * -------------------------------------------------------------------------- */

const TITLE = 'Graded Intake';
const TITLE_KICKER = 'subject onboarding · welcome!';

/** The welcome speech. One entry per rendered line; the letter-bounce stagger
 *  restarts per line so a long paragraph never ends on a 4-second delay. */
const WELCOME_LINES = [
  'Welcome to the graded intake!',
  'We are sooooo happy to have you here!',
  "We can't wait to have fun together!",
  "I'm gonna ask you some questions now to get to know you better!",
  'answer honestly! :3',
];

const DISCLAIMER =
  '[DISCLAIMER: your answers WILL be recorded. By proceeding you accept that ' +
  'your answers will be used to enhance and deepen your conditioning experience]';

/* ----------------------------------------------------------------------------
 * SFX — ids verified against assets/sfx/sfx_manifest.json. NEVER invent one:
 * an unknown id falls through audio.js's synth fallback and sounds wrong.
 *   sticker-drag (2 variants, gain .15) — the quiet paper-shuffle for hover
 *   giggle       (7 variants, gain .30) — the storefront's click
 *   surface-bloom(1 variant,  gain .45) — the swell on Begin
 * -------------------------------------------------------------------------- */
const SFX_HOVER = 'sticker-drag';
const SFX_CLICK = 'giggle';
const SFX_BEGIN = 'surface-bloom';

/** Hover cues fire on every pointer graze; throttle so a shaky mouse over three
 *  buttons cannot machine-gun the sfx bus. */
const HOVER_SFX_MIN_GAP_MS = 260;

/** Hard ceiling on decor nodes (clouds + arc + sparkles + hearts). */
const DECOR_BUDGET = 24;

/* ----------------------------------------------------------------------------
 * PUBLIC ENTRY
 * -------------------------------------------------------------------------- */

/**
 * Mount the kawaii title screen and wait for the player's choice.
 * @param {{theme?:object, config?:object, audio?:object, background?:object, stats?:object}} [ctx]
 * @returns {Promise<'begin'|'quit'>}
 */
export async function showMainMenu(ctx) {
  ctx = ctx || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || !doc.body) return 'begin';   // headless import — never block a run

  ensureKawaiiCss(doc);

  const reduced = prefersReducedMotion();
  const prefs = safePrefs();
  const audio = ctx.audio || null;

  /* --- teardown ledger: every listener/timer registers here ---------------- */
  const cleanups = [];
  const timers = new Set();
  const later = (fn, ms) => {
    const t = setTimeout(() => { timers.delete(t); fn(); }, Math.max(0, ms | 0));
    timers.add(t);
    return t;
  };
  const listen = (target, type, fn, opts) => {
    try { target.addEventListener(type, fn, opts); } catch (_e) { return; }
    cleanups.push(() => { try { target.removeEventListener(type, fn, opts); } catch (_e) {} });
  };

  /* --- structure ----------------------------------------------------------- */
  const root = doc.createElement('div');
  root.className = 'kw-root kw-menu-root';
  root.setAttribute('role', 'dialog');
  root.setAttribute('aria-modal', 'true');
  root.setAttribute('aria-label', TITLE + ' — main menu');
  if (prefs.sparkles === false) root.classList.add('kw-no-decor');

  const scrim = doc.createElement('div');
  scrim.className = 'kw-scrim';
  root.appendChild(scrim);

  const decor = buildDecor(doc, DECOR_BUDGET, reduced);
  root.appendChild(decor);

  const panel = doc.createElement('div');
  panel.className = 'kw-panel kw-menu-panel' + (reduced ? '' : ' kw-pop-in');
  root.appendChild(panel);

  // title
  const h1 = doc.createElement('h1');
  h1.className = 'kw-menu-title';
  h1.textContent = TITLE;
  panel.appendChild(h1);

  const kicker = doc.createElement('p');
  kicker.className = 'kw-menu-subtitle';
  kicker.textContent = TITLE_KICKER;
  panel.appendChild(kicker);

  const rule = doc.createElement('hr');
  rule.className = 'kw-rainbow-rule';
  panel.appendChild(rule);

  // welcome copy — bouncing glyphs, with a flat SR-only twin
  const welcome = doc.createElement('div');
  welcome.className = 'kw-menu-welcome';
  const sr = doc.createElement('p');
  sr.className = 'kw-sr-only';
  sr.textContent = WELCOME_LINES.join(' ');
  welcome.appendChild(sr);
  for (const line of WELCOME_LINES) welcome.appendChild(bounceLine(doc, line, reduced));
  panel.appendChild(welcome);

  // the knife
  const disc = doc.createElement('p');
  disc.className = 'kw-disclaimer kw-menu-disclaimer';
  disc.textContent = DISCLAIMER;
  panel.appendChild(disc);

  // buttons
  const btnRow = doc.createElement('div');
  btnRow.className = 'kw-menu-btns';
  panel.appendChild(btnRow);

  const btnBegin = mkButton(doc, 'Begin ♡', 'kw-btn');
  const btnOptions = mkButton(doc, 'Options', 'kw-btn kw-btn--soft');
  const btnQuit = mkButton(doc, 'Quit', 'kw-btn kw-btn--soft');
  /* THE ARCHIVE (ui/history.js). Hidden at build time and revealed only if the
   * terminal already holds a completed assessment — a first-ever subject has no
   * file to consult and must not be shown a drawer that opens on nothing. The
   * check is async (IndexedDB), so the button is created here, kept out of the
   * arrow-key ring, and spliced in when the count comes back non-zero. */
  const btnArchive = mkButton(doc, 'Your file', 'kw-btn kw-btn--soft');
  btnArchive.hidden = true;
  btnRow.appendChild(btnBegin);
  btnRow.appendChild(btnOptions);
  btnRow.appendChild(btnArchive);
  btnRow.appendChild(btnQuit);
  const buttons = [btnBegin, btnOptions, btnQuit];

  const note = doc.createElement('p');
  note.className = 'kw-menu-note';
  note.setAttribute('role', 'status');
  panel.appendChild(note);

  /* --- music mute ----------------------------------------------------------
   * A dedicated, always-visible switch for the shell song, parked on the panel
   * corner rather than buried in Options. It is a licensed track playing
   * unprompted the moment the window opens, so silencing it must never cost
   * more than one click — some players will want it gone immediately and should
   * not have to go looking. The Options panel carries the same pref plus a
   * level slider for everyone else. */
  const btnMusic = doc.createElement('button');
  btnMusic.type = 'button';
  btnMusic.className = 'kw-menu-music';
  const paintMusic = () => {
    const on = isMusicOn();
    btnMusic.textContent = on ? '♪' : '♪̸';
    btnMusic.setAttribute('aria-pressed', on ? 'true' : 'false');
    btnMusic.setAttribute('aria-label', on ? 'Mute music' : 'Unmute music');
    btnMusic.title = on ? 'Mute music' : 'Unmute music';
    btnMusic.setAttribute('data-off', on ? '0' : '1');
  };
  paintMusic();
  panel.appendChild(btnMusic);

  doc.body.appendChild(root);

  /* --- audio helpers ------------------------------------------------------- */
  let lastHoverAt = 0;
  const sfx = (id, intensity) => {
    try {
      if (audio && typeof audio.sfx === 'function') audio.sfx(id, intensity);
    } catch (_e) { /* audio is decoration, never a dependency */ }
  };
  const hoverSfx = () => {
    const now = Date.now();
    if (now - lastHoverAt < HOVER_SFX_MIN_GAP_MS) return;
    lastHoverAt = now;
    sfx(SFX_HOVER, 0.5);
  };

  /* --- resolution ---------------------------------------------------------- */
  let settled = false;
  let optionsBusy = false;

  return new Promise((resolve) => {
    const finish = (choice) => {
      if (settled) return;
      settled = true;
      // Silence the greeter FIRST, so her duck is lifted before the song starts
      // fading out. The reverse order lets teardown's stopWelcome cleanup land
      // mid-fade and re-raise the volume (duckMusic guards against this too —
      // belt and braces, because the failure mode is the song following you
      // into the run at full gain).
      stopWelcome();
      // Fade the song out of the way of the briefing rather than cutting it.
      // stopMenuMusic keeps the element and its playhead, so the pause menu
      // later resumes mid-song instead of restarting the intro every time.
      // Quitting kills the window outright, so there is nothing to fade into.
      stopMenuMusic(choice === 'quit' ? { immediate: true } : undefined);
      teardown();
      resolve(choice);
    };

    function teardown() {
      for (const t of timers) { try { clearTimeout(t); } catch (_e) {} }
      timers.clear();
      while (cleanups.length) { const fn = cleanups.pop(); try { fn(); } catch (_e) {} }
      try { if (root.parentNode) root.parentNode.removeChild(root); } catch (_e) {}
    }

    function showNote(text, warn) {
      note.textContent = text;
      if (warn) note.setAttribute('data-warn', '1'); else note.removeAttribute('data-warn');
      later(() => { if (!settled) { note.textContent = ''; note.removeAttribute('data-warn'); } }, 4200);
    }

    /* --- Options: another agent owns ui/options.js. Treat it as optional. --- */
    async function openOptionsPanel() {
      if (optionsBusy || settled) return;
      optionsBusy = true;
      btnOptions.setAttribute('aria-busy', 'true');
      try {
        const mod = await import('./options.js');
        const open = mod && (mod.openOptions || (mod.default && mod.default.openOptions));
        if (typeof open !== 'function') throw new Error('openOptions export missing');
        // Superset of both documented shapes: `parent`/`mount` are the same node,
        // `onClose` fires for a callback-style implementation, and the returned
        // promise (if any) is awaited for a promise-style one.
        await Promise.resolve(open({
          parent: root,
          mount: root,
          audio,
          onClose: () => {},
        }));
      } catch (e) {
        showNote('Options unavailable right now — you can still begin! ('
          + String((e && e.message) || e).slice(0, 60) + ')', true);
      } finally {
        optionsBusy = false;
        btnOptions.removeAttribute('aria-busy');
        if (!settled) { try { btnOptions.focus(); } catch (_e) {} }
      }
    }

    /* --- The archive: optional module, optional button ----------------------
     * Same contract as Options — ui/history.js is imported dynamically and any
     * failure (missing file, unreadable storage) simply leaves the drawer shut.
     * The reveal is deliberately quiet: no badge, no count, no "NEW!". */
    let archiveBusy = false;

    (async () => {
      try {
        const mod = await import('./history.js');
        if (settled || !mod || typeof mod.hasArchive !== 'function') return;
        const count = await mod.hasArchive(ctx.stats);
        if (settled || !count) return;
        btnArchive.hidden = false;
        // Only now does it join the arrow-key ring, in its visual position.
        if (buttons.indexOf(btnArchive) < 0) buttons.splice(2, 0, btnArchive);
        listen(btnArchive, 'pointerenter', hoverSfx);
        listen(btnArchive, 'focus', hoverSfx);
        listen(btnArchive, 'click', () => { sfx(SFX_CLICK, 0.5); openArchivePanel(mod); });
      } catch (_e) { /* no archive, no button — never a broken menu */ }
    })();

    async function openArchivePanel(mod) {
      if (archiveBusy || settled) return;
      archiveBusy = true;
      btnArchive.setAttribute('aria-busy', 'true');
      try {
        const open = mod && (mod.openHistory || (mod.default && mod.default.openHistory) || mod.default);
        if (typeof open !== 'function') throw new Error('openHistory export missing');
        await Promise.resolve(open({ parent: root, audio, stats: ctx.stats }));
      } catch (e) {
        showNote('Your file is unavailable right now — you can still begin! ('
          + String((e && e.message) || e).slice(0, 60) + ')', true);
      } finally {
        archiveBusy = false;
        btnArchive.removeAttribute('aria-busy');
        if (!settled) { try { btnArchive.focus(); } catch (_e) {} }
      }
    }

    /* --- wiring -------------------------------------------------------------
     * <button> handles Enter/Space natively, so the keydown handler only owns
     * arrow-key focus movement. Escape is intentionally NOT handled here: the
     * pause system owns Escape, and the menu must not teach it a second meaning.
     * ---------------------------------------------------------------------- */
    for (const b of buttons) {
      listen(b, 'pointerenter', hoverSfx);
      listen(b, 'focus', hoverSfx);
    }

    listen(btnBegin, 'click', () => { sfx(SFX_BEGIN, 0.9); finish('begin'); });
    listen(btnOptions, 'click', () => { sfx(SFX_CLICK, 0.6); openOptionsPanel(); });
    listen(btnQuit, 'click', () => { sfx(SFX_CLICK, 0.4); finish('quit'); });

    /* --- shell music --------------------------------------------------------
     * The mute button writes the pref; menuMusic's own prefs subscription does
     * the fade. This listener therefore only repaints the glyph — and so does
     * the subscription below, which catches the Options panel flipping the same
     * pref while the menu is still mounted underneath it. One source of truth,
     * two affordances that can never disagree. */
    listen(btnMusic, 'click', () => { toggleMusic(); paintMusic(); sfx(SFX_CLICK, 0.35); });
    listen(btnMusic, 'pointerenter', hoverSfx);
    const unsubMusic = subscribe((_p, key) => { if (!key || key === 'musicOn') paintMusic(); });
    cleanups.push(unsubMusic);

    startMenuMusic();
    // ...and the greeter reads the welcome over it (ducking the song for the
    // duration). Honours the voiceover pref, so Options -> Voiceover off keeps
    // the menu quiet too. Registered as a cleanup so leaving the screen mid
    // sentence cuts her off rather than letting her talk over the briefing.
    playWelcome();
    cleanups.push(stopWelcome);

    listen(root, 'keydown', (e) => {
      const k = e.key;
      if (k !== 'ArrowRight' && k !== 'ArrowLeft' && k !== 'ArrowDown' && k !== 'ArrowUp') return;
      if (optionsBusy || archiveBusy) return;        // an open panel owns focus
      const at = buttons.indexOf(doc.activeElement);
      const dir = (k === 'ArrowRight' || k === 'ArrowDown') ? 1 : -1;
      const next = at < 0 ? 0 : (at + dir + buttons.length) % buttons.length;
      e.preventDefault();
      try { buttons[next].focus(); } catch (_e) {}
    });

    // Focus Begin, but only after the pop-in has settled so the outline does not
    // ride the scale animation.
    later(() => { try { btnBegin.focus({ preventScroll: true }); } catch (_e) { try { btnBegin.focus(); } catch (_e2) {} } },
      reduced ? 0 : 440);
  });
}

/* ----------------------------------------------------------------------------
 * TEXT — per-letter bounce with the plain string kept for assistive tech.
 * -------------------------------------------------------------------------- */

/**
 * Split `text` into one animated <span class="kw-bl"> per glyph, staggered by
 * --kw-i. The whole wrapper is aria-hidden — a screen reader would otherwise
 * spell the sentence out letter by letter — and the caller supplies a flat
 * .kw-sr-only twin alongside.
 */
function bounceLine(doc, text, reduced) {
  const line = doc.createElement('div');
  line.className = 'kw-bounce-line';
  line.setAttribute('aria-hidden', 'true');

  // WORD GROUPING IS LOAD-BEARING, not tidiness. Every glyph is its own
  // inline-block, and the browser is free to line-break between any two
  // inline-blocks — so a flat run of letter spans wraps MID-WORD ("to kno /
  // w you better!"). Each word gets an inline-block, nowrap wrapper, which
  // makes the word the unbreakable unit while the letters inside it still
  // animate independently. The stagger index keeps counting across words so
  // the wave still travels along the whole line.
  const words = String(text).split(/(\s+)/);   // keep the separators
  let i = 0;
  for (const word of words) {
    if (!word) continue;
    if (/^\s+$/.test(word)) {
      // Breakable whitespace lives BETWEEN wrappers — this is where wrapping
      // is allowed to happen, and it is the only place.
      line.appendChild(doc.createTextNode(word));
      continue;
    }
    const w = doc.createElement('span');
    w.className = 'kw-word';
    for (const ch of Array.from(word)) {
      const s = doc.createElement('span');
      s.className = 'kw-bl';
      s.textContent = ch;
      if (!reduced) s.style.setProperty('--kw-i', String(i));
      w.appendChild(s);
      i++;
    }
    line.appendChild(w);
    i++;                                       // count the gap, keep the wave even
  }
  return line;
}

/* ----------------------------------------------------------------------------
 * DECOR — randomized per mount so the storefront never looks canned, and hard
 * capped so it cannot compete with the tube for frame budget.
 * -------------------------------------------------------------------------- */

function buildDecor(doc, budget, reduced) {
  const layer = doc.createElement('div');
  layer.className = 'kw-decor-layer';
  layer.setAttribute('aria-hidden', 'true');
  if (budget <= 0 || reduced) return layer;   // reduced-motion: an empty layer

  let left = Math.max(0, budget | 0);
  const take = (n) => { const k = Math.min(n, left); left -= k; return k; };

  // 1) the rainbow arc — one node, sets the whole backdrop's mood
  if (take(1)) {
    const arc = doc.createElement('div');
    arc.className = 'kw-rainbow-arc';
    arc.style.top = rnd(20, 34).toFixed(1) + '%';
    arc.style.animationDelay = '-' + rnd(0, 11).toFixed(2) + 's';
    layer.appendChild(arc);
  }

  // 2) two drifting clouds, different sizes/speeds/heights
  const clouds = take(2);
  for (let i = 0; i < clouds; i++) {
    const c = doc.createElement('div');
    c.className = 'kw-cloud';
    // font-size scales the body AND both puffs (they are em-sized), so the whole
    // cloud resizes without touching `transform`, which the drift keyframe owns.
    c.style.fontSize = rnd(11, 24).toFixed(1) + 'px';
    c.style.top = rnd(6, 74).toFixed(1) + '%';
    c.style.opacity = rnd(0.45, 0.85).toFixed(2);
    c.style.animationDuration = rnd(42, 78).toFixed(1) + 's';
    c.style.animationDelay = '-' + rnd(0, 60).toFixed(1) + 's';   // already mid-drift
    layer.appendChild(c);
  }

  // 3) sparkles — the bulk of the budget, biased toward the screen edges so the
  //    panel's text stays legible.
  const sparkles = take(pick([11, 12, 13]));
  for (let i = 0; i < sparkles; i++) {
    const s = doc.createElement('div');
    s.className = 'kw-sparkle';
    s.style.left = edgeBiased().toFixed(1) + '%';
    s.style.top = rnd(4, 94).toFixed(1) + '%';
    const size = rnd(9, 22);
    s.style.width = size.toFixed(0) + 'px';
    s.style.height = size.toFixed(0) + 'px';
    s.style.animationDuration = rnd(2.1, 4.4).toFixed(2) + 's';
    s.style.animationDelay = '-' + rnd(0, 4.4).toFixed(2) + 's';
    layer.appendChild(s);
  }

  // 4) a few rising hearts (long durations + negative delays => "occasional").
  //    Deliberately does NOT spend the whole remaining budget: the cap is a
  //    ceiling, not a quota, and a heart storm reads as clutter, not charm.
  const hearts = take(Math.min(left, pick([4, 5, 6])));
  const glyphs = ['♥', '♡', '✿', '✦'];
  for (let i = 0; i < hearts; i++) {
    const h = doc.createElement('div');
    h.className = 'kw-heart';
    h.textContent = pick(glyphs);
    h.style.left = edgeBiased().toFixed(1) + '%';
    h.style.top = rnd(55, 96).toFixed(1) + '%';
    h.style.fontSize = rnd(0.85, 1.7).toFixed(2) + 'rem';
    h.style.color = pick(['var(--kw-pink)', 'var(--kw-lilac)', 'var(--kw-mint)', 'var(--kw-lemon)']);
    h.style.animationDuration = rnd(6.5, 13).toFixed(1) + 's';
    h.style.animationDelay = '-' + rnd(0, 13).toFixed(1) + 's';
    layer.appendChild(h);
  }

  return layer;
}

/** Uniform in [a,b). */
function rnd(a, b) { return a + Math.random() * (b - a); }
function pick(arr) { return arr[Math.floor(Math.random() * arr.length) % arr.length]; }

/** A left/right percentage that avoids the central 30% where the panel sits. */
function edgeBiased() {
  return Math.random() < 0.5 ? rnd(1, 34) : rnd(66, 97);
}

/* ----------------------------------------------------------------------------
 * SMALL HELPERS
 * -------------------------------------------------------------------------- */

function mkButton(doc, label, cls) {
  const b = doc.createElement('button');
  b.type = 'button';
  b.className = cls;
  b.textContent = label;
  return b;
}

function prefersReducedMotion() {
  try { return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches); }
  catch (_e) { return false; }
}

function safePrefs() {
  try { return getPrefs(); } catch (_e) { return { sparkles: true }; }
}

/**
 * index.html only links styles.css (the run's sheet). The shell screens pull in
 * kawaii.css themselves, once — options.js and pause.js do the same, so the tag
 * is keyed by a data attribute rather than by who got there first.
 */
function ensureKawaiiCss(doc) {
  try {
    if (doc.querySelector('link[data-kw-kit="1"]')) return;
    const href = new URL('./kawaii.css', import.meta.url).href;
    const link = doc.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    link.setAttribute('data-kw-kit', '1');
    (doc.head || doc.documentElement).appendChild(link);
  } catch (_e) { /* worst case the menu renders unstyled but still works */ }
}
