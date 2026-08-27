/* ============================================================================
 * vn/index.js - FIRST BELL: the opening's whole chassis, in one controller.
 *
 * WHAT THIS IS
 * A small visual-novel layer that speaks FOUR times on a player's first night
 * and never again: two captions at the gates, one piece of paper on the
 * admissions desk (which hands the wall over to the live split-flap board), one
 * caption on the walk to Homeroom, and one slip after the first stamp. The beat
 * sheet is `<Screenshots>/arcademy-vn-proposals/FIRST-BELL.md` (owner-vetted
 * 2026-08-24, rulings 1-5); every word on screen is verbatim from it, stored in
 * vn/lex.js and mirrored in ArcademyHostService.NeutralLexicon.
 *
 * THE FOUR SAFETY LAWS, and they outrank every beat below
 *
 *   1. THE VN NEVER GATES SHIPPED FLOW. Every entry point takes a continuation
 *      and guarantees it runs exactly once: on success, on a throw, on a missing
 *      plate, on an already-spent flag, and on a watchdog. `settler()` is the one
 *      funnel and it is idempotent, so there is exactly one thing to get right.
 *   2. NO SHIPPED BEAT IS RENAMED OR REORDERED. The VN adds frames BEFORE a
 *      seam and never inside one. b02 is untouched, the enrollment intro still
 *      runs first, the class clock is still armed in beginPlay, and the stamp
 *      ceremony is exactly the ceremony it was.
 *   3. FIRST RUN ONLY. A school that has already been attended stands the whole
 *      module down and BANKS every scene as seen, so an existing player upgrading
 *      into this build never meets a single frame of it.
 *   4. ESC IS NOT OURS. boot.js owns the key at the window, and nothing here
 *      binds, reads, swallows or preventDefaults it - the shipped hold-Esc exit
 *      works at every VN frame (the beat sheet's "furniture, not a gate").
 *
 * PERSISTENCE. `vnSeen` in the C# meta store, a SIBLING of emi/voice.js's
 * `emiVoice` key and written the identical way: `store.set(key, blob)` ->
 * meta-command -> ArcademyMetaStore -> back in `init.meta` next launch. Once-ever
 * therefore means across app restarts, which is the whole point. There is no
 * localStorage in this bundle and this file adds none.
 *
 * INPUT. A tap, a click or Enter advances. The hold-to-skip pill is on screen
 * from frame one (ruling 4) and reuses boot.js's 1200ms reach-for-the-door
 * grammar; holding it ends the CURRENT scene and fast-lands its handoff edge -
 * the board for the cold open, the class for the walk, EMI's line for the mail.
 * It can never skip a shipped ceremony, only VN tissue.
 *
 * THE PACE (owner playtest, 2026-08-24: "everything happens pretty fast").
 * A beat change is not a cut. The plate on the wall slides and fades away, the
 * screen holds half a second of pure black, and the new plate slides in from
 * the other side - see applyPlate and the BEAT_* dials, which are the only
 * place the opening's tempo is written down. Two consequences worth carrying
 * in your head while reading the rest: no beat may start before its plate has
 * landed (that is what applyPlate's `done` is for), and no plate transition may
 * leave the hold-to-skip pill pointing at nothing, because a seam between two
 * scenes is now long enough for a player to reach the door inside it.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { VN_LEX, PAPERS } from './lex.js';
import { injectStyle } from './style.js';
import { COLD_OPEN, WALK, MAIL, ART, BOARD_ZONE, SCENE_IDS } from './scenes.js';

/** The store key. Sibling of emi/voice.js's VOICE_STORE_KEY, same discipline. */
export const VN_STORE_KEY = 'vnSeen';

/** Bump only for a shape change; an unreadable blob starts clean, never throws. */
const BLOB_VERSION = 1;

/** The tunables. One table, at the top, the way every other module here does it. */
export const VN_DIALS = Object.freeze({
  HOLD_MS: 1200,          // boot.js's HOLD_EXIT_MS - the same reach for the door
  FADE_MS: 600,           // the layer's own fade up / down
  ART_TIMEOUT_MS: 2500,   // a plate that has not decoded by here is a missing plate
  BOARD_WARM_MS: 700,     // the wall warming up before the first flap rolls
  BOARD_DEAL_MS: 2600,    // the cascade (rows * .4s + .95s + the meta fade)
  BOARD_DEAL_REDUCED_MS: 700,
  PAPER_OUT_MS: 300,      // the slip tucking back into the tray
  MAIL_DELAY_MS: 700,     // let the screen under the ceremony settle first
  HOLD_TICK_MS: 250,      // the clock under a held skip (AV CLUB)
  /* THE BEAT TRANSITION (owner playtest order, 2026-08-24: "we should slow down
   * the intro beats, right now everything happens pretty fast. We need to space
   * those out properly, and have about half a second of black screen with a
   * sliding fading transition when we change a beat"). ONE plate change is: the
   * old frame slides and fades away, the screen sits in pure black, the new
   * frame slides and fades in from the other side, and then it gets a moment to
   * be looked at before anyone talks over it. Retuning the opening's pace is a
   * number edit in this block and nowhere else - vn/style.js holds no copy. */
  BEAT_OUT_MS: 300,        // the outgoing plate leaving, with the whoosh on it
  BEAT_BLACK_MS: 500,      // the half second of black, and the swap is inside it
  BEAT_IN_MS: 300,         // the new plate arriving
  BEAT_SETTLE_MS: 400,     // look at the new frame before the lower third talks
  BEAT_LEAD_BLACK_MS: 700, // the black before the FIRST plate of the night; the
                           // layer's own FADE_MS fade to black runs underneath
  BEAT_CAPTION_OUT_MS: 300,// a dismissed card is gone before the next beat opens
  SPLASH_SETTLE_MS: 500,   // the splash has cleared; the opening does not pounce
  /* WATCHDOGS. If any of these fire the VN has a bug, and a bug may not cost
   * the player their night: the continuation runs and the layer comes down. */
  COLD_OPEN_CAP_MS: 150000,
  WALK_CAP_MS: 30000,
  MAIL_CAP_MS: 40000,
});

const doc = (typeof document !== 'undefined') ? document : null;
const win = (typeof window !== 'undefined') ? window : null;

function isObj(v) { return !!v && typeof v === 'object' && !Array.isArray(v); }

/** Lexicon read with this module's own English as the fallback (trap 15). */
function tx(key) { return t(key, VN_LEX[key] || ''); }

function attr(node, name, value) {
  try { if (node && typeof node.setAttribute === 'function') node.setAttribute(name, value); }
  catch (e) { /* the DOM double may not carry attributes - never fatal */ }
}

function cls(node, name, on) {
  try { if (node && node.classList) node.classList[on ? 'add' : 'remove'](name); }
  catch (e) { /* noop */ }
}

/* ONE AUDIO DOOR (AV CLUB, 2026-08-24). Every sound this file makes is a
 * REQUEST on `document` - shell/audio.js owns the only audio graph on the page
 * (trap 18) and shell/ceremonies.js set the pattern verbatim. The VN is
 * furniture (trap 76), so its voice is furniture too: a cue may never throw, a
 * dropped cue is not an error, and nothing here waits on a sound. */
function sfx(name, level, extra) {
  try {
    if (!doc || typeof doc.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    doc.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

/** Compose one paper paragraph from its clause rows (vn/lex.js PAPERS). */
export function paragraph(rows) {
  const out = [];
  for (const key of (Array.isArray(rows) ? rows : [])) {
    const s = tx(key);
    if (s) out.push(s);
  }
  return out.join(' ');
}

/* ============================================================================
 * createFirstBell
 * ==========================================================================*/
/**
 * @param {Object} o
 * @param {Object=} o.store          core/store.js handle (get/set). No store =
 *                                   no ledger = the module stands down, because
 *                                   a once-ever beat that cannot remember it ran
 *                                   is a beat that runs every night.
 * @param {Function=} o.rows         () => board rows for the handoff, exactly the
 *                                   shape shell.js's buildRows(true) returns
 * @param {Function=} o.firstNight   () => boolean. FALSE banks every scene and
 *                                   the module never speaks (law 3).
 * @param {Function=} o.canInterrupt () => boolean. False at mount time spends the
 *                                   mail rather than laying it over a live class.
 * @param {Function=} o.onMoment     (name, payload) => void. shell.js's
 *                                   fireMoment, INJECTED - this file imports
 *                                   nothing from emi/ (trap 60's discipline).
 * @param {boolean=} o.reducedMotion
 * @param {string=} o.base           prefix for the plate urls. It is resolved
 *                                   against the DOCUMENT, not against this
 *                                   module, because both an <img> src and an
 *                                   inline background-image do - so the default
 *                                   is './' (index.html sits in the web root)
 *                                   and vn/demo.html passes '../'. A module
 *                                   -relative '../' here would ask the host for
 *                                   https://ccp.game/art/... and get nothing.
 * @param {Function=} o.log
 * @returns {?Object} the controller, or null when there is nothing to play into
 */
export function createFirstBell(o) {
  const s = o || {};
  const say = typeof s.log === 'function' ? s.log : () => {};
  if (!doc || !doc.body || typeof doc.createElement !== 'function') return null;

  const store = (s.store && typeof s.store.get === 'function' && typeof s.store.set === 'function')
    ? s.store : null;
  const rowsOf = typeof s.rows === 'function' ? s.rows : () => [];
  const canInterrupt = typeof s.canInterrupt === 'function' ? s.canInterrupt : () => true;
  const fire = typeof s.onMoment === 'function' ? s.onMoment : () => {};
  const base = typeof s.base === 'string' ? s.base : './';
  const reduced = !!s.reducedMotion;

  function el(tag, klass, text) {
    const n = doc.createElement(tag);
    if (klass) n.className = klass;
    if (text != null) n.textContent = text;
    return n;
  }

  /* ---------------------- the ledger ------------------------------------ */
  const blob = readBlob();

  function readBlob() {
    let raw = null;
    try { raw = store ? store.get(VN_STORE_KEY) : null; }
    catch (e) { say('vn: store read failed - ' + ((e && e.message) || e)); }
    const b = (isObj(raw) && raw.v === BLOB_VERSION) ? raw : {};
    return { v: BLOB_VERSION, seen: Object.assign({}, isObj(b.seen) ? b.seen : {}) };
  }

  function save() {
    if (!store) return;
    try { store.set(VN_STORE_KEY, JSON.parse(JSON.stringify(blob))); }
    catch (e) { say('vn: store write failed - ' + ((e && e.message) || e)); }
  }

  function seen(id) { return !!blob.seen[String(id)]; }

  /** Spend a scene. Written through IMMEDIATELY: a scene the player watched and
   *  then closed the app on must never replay. */
  function spend(id) {
    if (blob.seen[id]) return;
    blob.seen[id] = true;
    save();
    say('vn: ' + id + ' spent');
  }

  /** Bank the whole opening without playing a frame of it (law 3, and the way
   *  out of every degraded path that must not leave a scene half-armed). */
  function bankAll(why) {
    let moved = false;
    for (const id of SCENE_IDS) if (!blob.seen[id]) { blob.seen[id] = true; moved = true; }
    if (moved) save();
    say('vn: stood down (' + why + ')');
  }

  /* ---------------------- module-wide state ----------------------------- */
  let destroyed = false;
  let layer = null;             // the fixed full-viewport element, or null
  let frame = null;
  let bgWrap = null;            // the carriage: it slides, the plate rides it
  let bg = null;
  let neon = null;
  let capNode = null;
  let tapNode = null;
  let paperNode = null;
  let zoneNode = null;
  let glowNode = null;
  let skipBtn = null;
  let skipRelease = null;       // the pill's own "let go" verb, for a teardown
  /* THE PLATE LEDGER. `lastPlate` is the plate REQUESTED - up, or still riding
   * the carriage in - so a repaint of the frame we are already standing on is
   * silent and free. `plateWaiters` are the beats owed the moment it lands,
   * which is how the lower third learned not to caption a black screen, and
   * `plateToken` is what a superseding change invalidates so a cancelled
   * transition cannot finish over the top of the one that replaced it. */
  let lastPlate = null;
  let plateBusy = false;
  let plateWaiters = [];
  let plateToken = 0;
  let boardApi = null;
  let advance = null;           // the live "next step" verb, or null
  let skipTo = null;            // the live "end this scene now" verb, or null
  const timers = new Set();

  injectStyle(doc);

  /* ---------------------- timers, all cancellable ----------------------- */
  function later(fn, ms) {
    const id = setTimeout(() => {
      timers.delete(id);
      if (destroyed) return;
      try { fn(); } catch (e) { say('vn beat threw: ' + ((e && e.message) || e)); }
    }, Math.max(0, ms || 0));
    timers.add(id);
    return id;
  }
  function killTimers() {
    for (const id of Array.from(timers)) { try { clearTimeout(id); } catch (e) { /* noop */ } }
    timers.clear();
  }

  /* ---------------------- the plates ------------------------------------
   * WARMED AT CONSTRUCTION, so no entry point ever pays a decode. A plate that
   * cannot decode is remembered as false and its scene stands down instead of
   * laying a caption over a black rectangle. */
  const plateOk = Object.create(null);

  function preload(url) {
    return new Promise((resolve) => {
      if (!win || typeof win.Image !== 'function' || !url) { resolve(false); return; }
      let settled = false;
      const done = (ok) => { if (settled) return; settled = true; plateOk[url] = ok; resolve(ok); };
      try {
        const img = new win.Image();
        img.onload = () => done(true);
        img.onerror = () => done(false);
        img.src = base + url;
        // A decode still running at the cap is a decode we will not wait on.
        setTimeout(() => done(false), VN_DIALS.ART_TIMEOUT_MS);
      } catch (e) { done(false); }
    });
  }

  function ensurePlate(url) {
    if (!url) return Promise.resolve(true);
    if (plateOk[url] !== undefined) return Promise.resolve(!!plateOk[url]);
    return preload(url);
  }

  /* ---------------------- the layer ------------------------------------- */
  function mountLayer(opts) {
    unmountLayer();
    const bare = !!(opts && opts.bare);
    layer = el('div', 'arc-vn' + (bare ? ' is-bare' : ''));
    layer.id = 'arc-vn';
    attr(layer, 'role', 'group');

    frame = el('div', 'arc-vn-frame');
    /* THE CARRIAGE HOLDS BOTH. The plate cannot slide itself (its camera
     * keyframe fills and out-ranks a transform), and the neon is the arch light
     * on one particular set - a wash left pinned to the frame would still be
     * glowing through a hold that is meant to be black. */
    bgWrap = el('div', 'arc-vn-bgwrap');
    attr(bgWrap, 'aria-hidden', 'true');
    bg = el('div', 'arc-vn-bg');
    attr(bg, 'aria-hidden', 'true');
    neon = el('div', 'arc-vn-neon');
    attr(neon, 'aria-hidden', 'true');
    bgWrap.appendChild(bg);
    bgWrap.appendChild(neon);
    frame.appendChild(bgWrap);

    /* THE CAPTION IS THE ONLY THING A SCREEN READER SHOULD MEET HERE: the plate
     * is decoration and the board copy is duplicated live on the campus under
     * this layer, so both are aria-hidden and the card is a polite status. */
    capNode = el('div', 'arc-vn-cap');
    attr(capNode, 'role', 'status');
    attr(capNode, 'aria-live', 'polite');
    frame.appendChild(capNode);

    tapNode = el('p', 'arc-vn-tap', tx('vn_tap'));
    attr(tapNode, 'aria-hidden', 'true');
    frame.appendChild(tapNode);

    frame.appendChild(buildSkip());
    layer.appendChild(frame);

    // TAP TO ADVANCE. The pill is inside the layer and stops its own clicks, so
    // reaching for the skip is never read as a tap on the scene.
    layer.addEventListener('click', onTap);
    if (win && win.addEventListener) win.addEventListener('keydown', onKey);

    doc.body.appendChild(layer);
    // One frame of nothing so the opacity transition has a from-state.
    later(() => cls(layer, 'is-up', true), 20);
    return layer;
  }

  function unmountLayer() {
    /* A SCENE CAN END UNDER A HELD PILL (a watchdog, a caption's autoMs, the
     * board settling), and a tick loop is the one thing here that would keep
     * counting into the campus. Let go of it before the button is gone. */
    if (skipRelease) { try { skipRelease(); } catch (e) { /* noop */ } skipRelease = null; }
    if (win && win.removeEventListener) { try { win.removeEventListener('keydown', onKey); } catch (e) { /* noop */ } }
    if (boardApi) { try { boardApi.destroy(); } catch (e) { /* noop */ } boardApi = null; }
    if (layer) { try { layer.remove(); } catch (e) { /* noop */ } }
    layer = null; frame = null; bgWrap = null; bg = null; neon = null;
    capNode = null; tapNode = null; paperNode = null;
    zoneNode = null; glowNode = null; skipBtn = null;
    /* THE CARRIAGE COMES DOWN WITH THE LAYER AND ITS DEBTS ARE CANCELLED, NOT
     * PAID. A beat owed to a plate that no longer exists would run into a scene
     * that has already been torn down - and the one continuation that must
     * ALWAYS run is the settler's, which is closeLayer's business and never
     * this list's. Bumping the token retires any transition step that outlived
     * killTimers. */
    stopBed();             // W3 P1-21: every hold has an owner, and this is it
    lastPlate = null;
    plateBusy = false;
    plateWaiters = [];
    plateToken++;
  }

  /** Fade the layer out, then drop it. Reduced motion cuts straight to the drop. */
  function closeLayer(after) {
    const done = () => {
      unmountLayer();
      if (typeof after === 'function') { try { after(); } catch (e) { say('vn continuation threw: ' + ((e && e.message) || e)); } }
    };
    if (!layer) { done(); return; }
    cls(layer, 'is-up', false);
    const id = setTimeout(done, reduced ? 0 : VN_DIALS.FADE_MS);
    timers.add(id);
  }

  /* ---------------------- the hold-to-skip pill ------------------------- */
  function buildSkip() {
    skipBtn = el('button', 'arc-vn-skip');
    skipBtn.type = 'button';
    attr(skipBtn, 'aria-label', tx('vn_skip'));
    const fill = el('i', 'arc-vn-skip-fill');
    attr(fill, 'aria-hidden', 'true');
    skipBtn.appendChild(fill);
    skipBtn.appendChild(el('span', 'arc-vn-skip-label', tx('vn_skip')));

    let holdTimer = 0;
    let tickTimer = 0;

    /* THE CLOCK UNDER THE HOLD (AV CLUB). 1200ms is a long time to wonder
     * whether the pill heard you, so the reach for the door counts itself out
     * loud - one soft tick every 250ms, and the fill bar is no longer the only
     * thing saying "keep holding". The loop is a self-rescheduling `later()`,
     * which is what puts it under killTimers/destroy with every other beat in
     * this file rather than beside them. It stops three ways: a release, the
     * hold completing, and the layer coming down (unmountLayer above). */
    const stopTicks = () => {
      if (!tickTimer) return;
      try { clearTimeout(tickTimer); } catch (e) { /* noop */ }
      timers.delete(tickTimer);
      tickTimer = 0;
    };
    const tick = () => {
      sfx('clock_tick', 0.2);
      tickTimer = later(tick, VN_DIALS.HOLD_TICK_MS);
    };

    const release = () => {
      if (holdTimer) { clearTimeout(holdTimer); holdTimer = 0; }
      stopTicks();
      cls(skipBtn, 'is-holding', false);
    };
    skipRelease = release;
    const start = (e) => {
      if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
      if (holdTimer) return;
      cls(skipBtn, 'is-holding', true);
      // The first tick is 250ms in, so a tap on the pill is silent.
      tickTimer = later(tick, VN_DIALS.HOLD_TICK_MS);
      holdTimer = setTimeout(() => {
        holdTimer = 0;
        stopTicks();
        cls(skipBtn, 'is-holding', false);
        // The hold landed: end the current scene at its own handoff edge.
        try { if (skipTo) skipTo(); } catch (err) { say('vn skip threw: ' + ((err && err.message) || err)); }
      }, VN_DIALS.HOLD_MS);
    };
    skipBtn.addEventListener('pointerdown', start);
    skipBtn.addEventListener('pointerup', release);
    skipBtn.addEventListener('pointercancel', release);
    skipBtn.addEventListener('pointerleave', release);
    skipBtn.addEventListener('click', (e) => {
      if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    });
    return skipBtn;
  }

  /* ---------------------- input ----------------------------------------- */
  function onTap() {
    /* ONE TICK PER PAGE ACTUALLY TURNED. A tap into a scene with no live
     * advance (a hold, the board dealing) says nothing, and a paper answers
     * with its own stamp in showPaper rather than with this. */
    if (advance && !paperNode) sfx('blip', 0.15, { pitch: 1.1 });
    try { if (advance) advance(); } catch (e) { say('vn advance threw: ' + ((e && e.message) || e)); }
  }

  /**
   * ENTER ONLY. Escape is boot.js's (law 4) and is not read, not swallowed and
   * not preventDefaulted here; Space is left alone too, because it is the
   * browser's own activation key for the focused skip pill.
   */
  function onKey(e) {
    if (!e || e.repeat) return;
    if (e.key !== 'Enter') return;
    onTap();
  }

  /* ---------------------- the scene surface ----------------------------- */
  function setCaption(text) {
    if (!capNode) return;
    if (!text) {
      cls(capNode, 'is-up', false);
      capNode.textContent = '';
      cls(tapNode, 'is-up', false);
      return;
    }
    capNode.textContent = String(text);
    cls(capNode, 'is-up', true);
    cls(tapNode, 'is-up', true);
  }

  /** Pay everything owed to the plate that just landed. */
  function plateLanded(token) {
    if (plateToken !== token) return;
    plateBusy = false;
    const owed = plateWaiters;
    plateWaiters = [];
    for (const fn of owed) {
      try { fn(); } catch (e) { say('vn plate continuation threw: ' + ((e && e.message) || e)); }
    }
  }

  /** Forget what is queued behind the live plate. The skip path owns this: a
   *  beat waiting on an arrival the player just asked to get past is a beat
   *  that must not run when it lands. */
  function dropPlateWaiters() { plateWaiters = []; }

  /**
   * CHANGE THE PLATE - the intro's one real transition (owner order,
   * 2026-08-24). Out, black, in: the frame on the wall slides and fades away,
   * the screen holds on pure black for half a second with the swap hidden
   * inside it, and the new frame slides in from the other side. `done` runs
   * once the arrival has SETTLED, and every caller hands it the next beat -
   * that callback is the whole reason the lower third no longer opens over a
   * black rectangle.
   *
   * Never awaits a decode: ensurePlate is the gate, this only paints.
   */
  /* THE AIR IN THE ROOM (W3 P1-21). Four plates, two rooms: the gates and the
   * midway are OUTSIDE, the admissions hall and Homeroom are INSIDE, and the
   * opening had no ambience under either of them. Each bed is a HOLD - the
   * mixer's only sustain - so it loops until somebody lets go of it, and the
   * somebody is this file: the swap below stops the old one before it starts
   * the new one, and unmountLayer stops whatever is left. Both are SAMPLE-ONLY
   * names, so with no mp3 shipped this is honest silence and there is nothing
   * to check. Music bus, well under the captions. */
  let bedName = null;
  function bedFor(url) {
    return (url === ART.gates || url === ART.midway) ? 'vn_bed_ext' : 'vn_bed_int';
  }
  function stopBed() {
    if (!bedName) return;
    sfx(bedName, 0.25, { bus: 'music', stop: true });
    bedName = null;
  }
  function setBed(url) {
    const want = bedFor(url);
    if (want === bedName) return;
    stopBed();
    bedName = want;
    sfx(want, 0.25, { bus: 'music', hold: true });
  }

  function applyPlate(url, motion, done) {
    const settle = typeof done === 'function' ? done : null;
    if (!bg || !bgWrap || !url) { if (settle) settle(); return; }

    /* THE FRAME WE ARE ALREADY STANDING ON. The skip path repaints the desk it
     * is on, so a repeat request is ANSWERED rather than re-staged: join the
     * queue if the carriage is still moving, otherwise land now. */
    if (url === lastPlate) {
      if (plateBusy && settle) plateWaiters.push(settle);
      else if (settle) settle();
      return;
    }

    /* A NEW PLATE OUTRANKS ONE IN FLIGHT and the token retires the old steps.
     * Whatever was queued behind the abandoned arrival goes with it - the only
     * thing that supersedes a plate in this file is a skip, and a skip always
     * arrives carrying its own continuation. */
    const token = ++plateToken;
    const hadOne = !!lastPlate;
    lastPlate = url;
    plateBusy = true;
    plateWaiters = settle ? [settle] : [];

    const outMs = (reduced || !hadOne) ? 0 : VN_DIALS.BEAT_OUT_MS;
    const blackMs = hadOne ? VN_DIALS.BEAT_BLACK_MS : VN_DIALS.BEAT_LEAD_BLACK_MS;
    const inMs = reduced ? 0 : VN_DIALS.BEAT_IN_MS;

    /* THE WHOOSH IS THE CAMERA MOVE, so it leaves WITH the plate that is
     * leaving rather than landing somewhere in the black. The first plate of
     * the night has nothing to take off the wall, so its cue rides the arrival
     * instead - and reduced motion, which renders every plate as a static
     * frame, still hears no sweep at all. */
    if (hadOne) {
      if (!reduced) sfx('whoosh', 0.3);
      bgWrap.style.transitionDuration = outMs + 'ms';
      cls(bgWrap, 'is-in', false);
      cls(bgWrap, 'is-out', true);
    }

    later(() => {
      if (plateToken !== token || !bgWrap || !bg) return;
      /* THE SWAP IS INSIDE THE BLACK. Dropping BOTH classes parks the carriage
       * back on the entry side while it is invisible, so the walk across costs
       * nothing and the new plate can never flash at the wrong offset. */
      cls(bgWrap, 'is-out', false);
      cls(bgWrap, 'is-in', false);
      bg.style.backgroundImage = 'url("' + base + url + '")';
      /* W3 P1-21: the room changes inside the black, so its air changes there
       * too - never over the outgoing plate and never after the new one has
       * settled. The `wash` is the INTERIM: one soft breath per plate, which is
       * what carries the room change on a build where the beds have no file. */
      setBed(url);
      sfx('wash', 0.12);
      attr(bg, 'data-motion', reduced ? 'still' : (motion || 'still'));
      // Reflow so a re-armed animation actually re-runs (trap 4's law).
      try { void bg.offsetWidth; } catch (e) { /* the DOM double has no layout */ }

      later(() => {
        if (plateToken !== token || !bgWrap) return;
        if (!hadOne && !reduced) sfx('whoosh', 0.3);
        bgWrap.style.transitionDuration = inMs + 'ms';
        cls(bgWrap, 'is-in', true);
        // The new frame is looked at before anything is said over it.
        later(() => plateLanded(token), inMs + (reduced ? 0 : VN_DIALS.BEAT_SETTLE_MS));
      }, blackMs);
    }, outMs);
  }

  /** Drop a live paper immediately (a skip, a teardown). */
  function clearPaper() {
    if (!paperNode) return;
    const node = paperNode;
    paperNode = null;
    try { node.remove(); } catch (e) { /* noop */ }
  }

  function showPaper(which, onDismiss) {
    const spec = PAPERS[which];
    if (!spec || !frame) { onDismiss(); return; }
    clearPaper();
    paperNode = el('div', 'arc-vn-paper');
    attr(paperNode, 'role', 'status');
    paperNode.appendChild(el('h2', 'arc-vn-paper-title', tx(spec.title)));
    const rule = el('div', 'arc-vn-paper-rule');
    attr(rule, 'aria-hidden', 'true');
    paperNode.appendChild(rule);
    for (const rows of spec.paras) {
      const text = paragraph(rows);
      if (text) paperNode.appendChild(el('p', null, text));
    }
    paperNode.appendChild(el('p', 'arc-vn-paper-sign', tx(spec.sign)));
    frame.appendChild(paperNode);
    const mine = paperNode;
    // The slip and its sound leave the tray together.
    later(() => { cls(mine, 'is-up', true); sfx('paper', 0.35); }, 20);
    cls(tapNode, 'is-up', true);

    advance = () => {
      // READ AND FILED. The dismissal is the one beat in the VN with a hand in
      // it, so it lands on the school's own stamp rather than a page-turn tick.
      sfx('stamp', 0.3, { pitch: 1.05 });
      advance = null;
      paperNode = null;
      cls(mine, 'is-up', false);
      cls(tapNode, 'is-up', false);
      later(() => { try { mine.remove(); } catch (e) { /* noop */ } }, reduced ? 0 : VN_DIALS.PAPER_OUT_MS);
      later(onDismiss, reduced ? 0 : VN_DIALS.PAPER_OUT_MS);
    };
  }

  /* ---------------------- B4: THE BOARD WAKES --------------------------- */
  /**
   * THE HANDOFF. The plate paints a BARE panel over the admissions desk and the
   * SHIPPED split-flap board (shell/splitflap.js, the same module the campus
   * hangs behind its plaque) mounts into the reserved zone and deals tonight's
   * slots. The board is never baked into art (owner order, SET-NOTES) and this
   * is the frame where the painting becomes the app.
   *
   * The module is reached for with a DYNAMIC import inside a try/catch - the
   * loadOptional discipline shell.js uses for the engine and the provider - so a
   * board that cannot be built costs the flourish and nothing else.
   */
  async function dealBoard(done) {
    if (!frame) { done(); return; }
    let box = null;
    try {
      const r = frame.getBoundingClientRect ? frame.getBoundingClientRect() : null;
      if (r && r.width > 0 && r.height > 0) box = r;
    } catch (e) { box = null; }

    const pct = (n) => (n * 100).toFixed(3) + '%';
    zoneNode = el('div', 'arc-vn-boardzone');
    attr(zoneNode, 'aria-hidden', 'true');
    zoneNode.style.left = pct(BOARD_ZONE.x);
    zoneNode.style.top = pct(BOARD_ZONE.y);
    zoneNode.style.width = pct(BOARD_ZONE.w);
    zoneNode.style.height = pct(BOARD_ZONE.h);

    // The glow spills a little past the panel; the board sits exactly in it.
    glowNode = el('div', 'arc-vn-boardglow');
    attr(glowNode, 'aria-hidden', 'true');
    glowNode.style.left = pct(Math.max(0, BOARD_ZONE.x - 0.03));
    glowNode.style.top = pct(Math.max(0, BOARD_ZONE.y - 0.05));
    glowNode.style.width = pct(BOARD_ZONE.w + 0.06);
    glowNode.style.height = pct(BOARD_ZONE.h + 0.10);

    frame.appendChild(glowNode);
    frame.appendChild(zoneNode);
    const glow = glowNode;
    later(() => cls(glow, 'is-up', true), 20);

    let rows = [];
    try { rows = rowsOf() || []; } catch (e) { say('vn: rows() threw - ' + ((e && e.message) || e)); }

    let createBoard = null;
    try {
      const mod = await import('../shell/splitflap.js');
      createBoard = mod && (mod.createBoard || mod.default);
    } catch (e) { say('vn: splitflap unavailable (' + ((e && e.message) || e) + ') - the wall stays bare'); }
    if (destroyed || !zoneNode) { done(); return; }

    if (typeof createBoard === 'function' && rows.length) {
      try {
        // NO onSelect: the rows the player actually clicks are the campus's,
        // under this layer. These are set dressing on a painted wall.
        boardApi = createBoard({ rows, reducedMotion: reduced, animate: false });
        zoneNode.appendChild(boardApi.root);
        try {
          const kids = boardApi.root.children || [];
          for (let i = 0; i < kids.length; i++) { try { kids[i].tabIndex = -1; } catch (e2) { /* noop */ } }
        } catch (e2) { /* noop */ }
        // Scale the REAL board down into the painted panel rather than
        // re-implementing it at another size.
        if (box) {
          try {
            const br = boardApi.root.getBoundingClientRect();
            const zw = box.width * BOARD_ZONE.w;
            const zh = box.height * BOARD_ZONE.h;
            if (br.width > 0 && br.height > 0) {
              const k = Math.min(zw / br.width, zh / br.height, 1);
              if (k > 0 && k < 1) boardApi.root.style.transform = 'scale(' + k.toFixed(4) + ')';
            }
          } catch (e2) { /* no layout, no scale - the board still reads */ }
        }
      } catch (e) {
        say('vn: board mount threw (' + ((e && e.message) || e) + ')');
        boardApi = null;
      }
    }

    const zone = zoneNode;
    later(() => {
      cls(zone, 'is-up', true);
      // The flaps roll HERE, not at build: the deal is the beat.
      if (boardApi) { try { boardApi.replay(); } catch (e) { /* noop */ } }
    }, reduced ? 0 : VN_DIALS.BOARD_WARM_MS);

    later(done, (reduced ? 0 : VN_DIALS.BOARD_WARM_MS)
      + (reduced ? VN_DIALS.BOARD_DEAL_REDUCED_MS : VN_DIALS.BOARD_DEAL_MS));
  }

  /* ---------------------- the step runner ------------------------------- */
  /**
   * Walk one scene's steps.
   * @param {Object} scene   from vn/scenes.js
   * @param {Function} end   the normal handoff. Runs at most once.
   * @param {Function=} onSkip  what a hold-to-skip lands on instead of `end`.
   *   Defaults to `end`; the cold open passes "jump to the board" (ruling 4).
   */
  function runScene(scene, end, onSkip) {
    let i = 0;
    let over = false;
    const close = (fn) => {
      if (over) return;
      over = true;
      advance = null;
      setCaption('');
      clearPaper();
      fn();
    };
    skipTo = () => close(typeof onSkip === 'function' ? onSkip : end);

    const step = () => {
      if (destroyed || over) return;
      if (i >= scene.steps.length) { close(end); return; }
      const st = scene.steps[i++];
      advance = null;

      if (st.hold != null) { later(step, reduced ? Math.min(200, st.hold) : st.hold); return; }
      if (st.fx === 'neon') {
        cls(neon, 'is-on', true);
        /* W3 P1-21: THE ARCH CATCHES. The sign over the gates flickered on in
         * total silence, and a neon tube striking is one of the few sounds
         * everybody already knows. Three bursts from ONE dispatch - the two
         * failed strikes at 180 and 420ms ride the mixer's own timeline, so
         * this file owns no timer for them - and the third carries the recipe's
         * hum layer, which is the tube holding. Reduced motion has no flicker
         * to score (trap 66), so it hears nothing. */
        if (!reduced) {
          sfx('neon_strike', 0.22, { steps: [{ atMs: 180 }, { atMs: 420 }] });
        }
        step();
        return;
      }
      if (st.swap) {
        ensurePlate(st.swap).then((ok) => {
          if (destroyed || over) return;
          if (!ok) { step(); return; }
          // THE NEXT BEAT WAITS FOR THE PLATE. Handing `step` to applyPlate is
          // what keeps a caption from opening while the screen is still black.
          applyPlate(st.swap, st.motion || 'still', step);
        });
        return;
      }
      if (st.board) {
        // THE DEAL IS PAST THE POINT OF SKIPPING. Holding through it would
        // re-enter dealBoard and mount a second board on the same wall.
        skipTo = null;
        dealBoard(() => close(end));
        return;
      }
      if (st.paper) { showPaper(st.paper, step); return; }
      if (st.caption) {
        setCaption(tx(st.caption));
        /* W3 P1-21: the lower third had a cue on its DISMISS and none on its
         * arrival, which is backwards - the card sliding in is the thing the
         * player did not do. Quiet and high: paper moving, not an event. The
         * tap's own answer stays where it is; an input still gets an answer. */
        sfx('slide', 0.12, { pitch: 1.1 });
        const mine = () => {
          if (advance !== mine) return;
          advance = null;
          setCaption('');
          /* THE CARD IS GONE BEFORE THE NEXT BEAT OPENS (owner order: space
           * them out). This is the lower third's OWN exit, not padding laid on
           * the player's tap - two cards crossing in the same corner is a good
           * part of what read as hurried. */
          later(step, reduced ? 0 : VN_DIALS.BEAT_CAPTION_OUT_MS);
        };
        advance = mine;
        // A caption with an autoMs advances itself if the player just watches.
        if (st.autoMs) later(mine, reduced ? Math.min(1200, st.autoMs) : st.autoMs);
        return;
      }
      step();   // an unknown step is a no-op, never a stall
    };

    step();
  }

  /* ---------------------- the one funnel -------------------------------- */
  /**
   * SETTLE. Law 1 in a dozen lines: whatever happened, the continuation runs
   * exactly once and the layer is gone. Every entry point below hands its
   * continuation to this and to nothing else.
   */
  function settler(after, watchdogMs, label) {
    let spent = false;
    const settle = (why) => {
      if (spent) return;
      spent = true;
      killTimers();
      advance = null;
      skipTo = null;
      say('vn: ' + label + ' -> ' + why);
      closeLayer(after);
    };
    if (watchdogMs) timers.add(setTimeout(() => settle('watchdog'), watchdogMs));
    return settle;
  }

  /* ============================ ENTRY POINTS =========================== */

  /**
   * B1 -> B4. The splash has been hidden and the campus is painted underneath
   * but has not been touched: play the cold open, hand the wall to the live
   * board, and get out of the way.
   *
   * @param {Function} after  called EXACTLY once, always
   */
  function splashDone(after) {
    const go = typeof after === 'function' ? after : () => {};
    if (destroyed || !store) { go(); return; }
    if (seen('s01') && seen('s02')) { go(); return; }

    let firstNight = true;
    try { firstNight = typeof s.firstNight === 'function' ? !!s.firstNight() : true; }
    catch (e) { firstNight = false; }
    if (!firstNight) { bankAll('not a first night'); go(); return; }

    const gates = COLD_OPEN[0];
    const desk = COLD_OPEN[1];

    /* THE PLATES ARE THE GATE, AND THEY ARE CHECKED BEFORE ANYTHING MOUNTS.
     * A missing file must never cost the player a black rectangle, so nothing
     * is drawn until the pixels are known to exist. The ledger is left ARMED -
     * a plate that comes back tomorrow gets its scene (B-sheet: a missed edge
     * leaves the scene armed for the next eligible night). */
    Promise.all([ensurePlate(gates.image), ensurePlate(desk.image)]).then(([a, b]) => {
      if (destroyed) { go(); return; }
      if (!a || !b) { say('vn: a plate is missing - the cold open stands down'); go(); return; }

      const settle = settler(go, VN_DIALS.COLD_OPEN_CAP_MS, 'cold open');

      /* THE SKIP'S HANDOFF EDGE IS B4, never a shipped seam: both scenes are
       * spent, the desk plate goes up and the board deals. LATCHED, because it
       * is now reachable from two places - a scene's own hold, and the seams
       * between scenes where no scene owns the pill (see below). A second
       * landing must be free, or the wall gets two boards on it. */
      let dealt = false;
      const toBoard = () => {
        if (dealt) return;
        dealt = true;
        skipTo = null;
        spend('s01'); spend('s02');
        clearPaper();
        setCaption('');
        // Whatever was queued behind the plate is a beat the player just asked
        // to get past; the deal is the only thing that rides this arrival now.
        dropPlateWaiters();
        applyPlate(desk.image, desk.motion,
          () => dealBoard(() => settle('skipped to the board')));
      };
      const runDesk = () => {
        if (seen('s02')) { settle('desk already spent'); return; }
        skipTo = toBoard;
        applyPlate(desk.image, desk.motion,
          () => runScene(desk, () => { spend('s02'); settle('done'); }, toBoard));
      };

      /* THE OPENING DOES NOT POUNCE ON THE SPLASH (owner order, 2026-08-24).
       * The loader's `hidden` has just landed and the campus is painted
       * underneath: give the player that frame before the layer arrives over
       * it. The watchdog above is already armed, so this beat of nothing is
       * covered exactly like every other beat in this file. */
      later(() => {
        try {
          mountLayer({});
          /* THE PILL IS LIVE ACROSS THE SEAMS, NOT ONLY INSIDE A SCENE. A beat
           * transition now runs over a second, and while one plays no scene
           * owns `skipTo` - so the cold open parks its handoff edge here for
           * the gaps. runScene overwrites it the moment a scene starts, and
           * the latch above makes the overlap free. */
          skipTo = toBoard;
          applyPlate(gates.image, gates.motion, () => {
            if (seen('s01')) { runDesk(); return; }
            runScene(gates, () => { spend('s01'); skipTo = toBoard; runDesk(); }, toBoard);
          });
        } catch (e) {
          say('vn cold open threw: ' + ((e && e.message) || e));
          settle('threw');
        }
      }, VN_DIALS.SPLASH_SETTLE_MS);
    });
  }

  /**
   * B7 -> B8. One caption on the midway and a threshold hold on the empty
   * Homeroom, then the SHIPPED class takeover, untouched and next.
   *
   * @param {Object} spec      {gameKey, homeroom} - the timetable row being opened
   * @param {Function} resume  starts the class. Called EXACTLY once, always.
   * @returns {boolean} true = the VN took this frame and now owns `resume`
   */
  function gateClass(spec, resume) {
    const go = typeof resume === 'function' ? resume : () => {};
    if (destroyed || !store || seen('s03')) return false;
    if (!seen('s02')) return false;            // the opening never ran; do not start here
    if (layer) return false;                   // a scene is already up: never two
    // THE WALK IS TO HOMEROOM. Any other door leaves the scene armed for the
    // night the player actually walks to room 101.
    if (!spec || !spec.homeroom) return false;
    if (!plateOk[WALK.image] || !plateOk[ART.homeroom]) return false;

    /* SPEND IT FIRST. `resume` re-enters startClass, and a flag written after
     * that would gate the same class a second time. This ordering is the whole
     * reason the seam is safe to re-enter. */
    spend('s03');
    const settle = settler(go, VN_DIALS.WALK_CAP_MS, 'walk');
    try {
      mountLayer({});
      /* SAME SEAM RULE AS THE COLD OPEN: the walk's opening transition is over
       * a second of black and slide before runScene arms the pill, so the door
       * out is parked here first. Skipping the walk lands where the walk was
       * going anyway - the shipped class takeover, one settle away. */
      skipTo = () => settle('skipped the walk');
      applyPlate(WALK.image, WALK.motion, () => runScene(WALK, () => settle('done')));
    } catch (e) {
      say('vn walk threw: ' + ((e && e.message) || e));
      settle('threw');
    }
    return true;
  }

  /**
   * B11. The first-ever punch ceremony has just cleared: the slip slides out
   * from under the live board, and once it is dismissed EMI gets her one new
   * line through the ordinary moment seam (`firstMail` -> emi/story.js b28).
   *
   * @returns {boolean} true = the paper is scheduled
   */
  function afterCeremony() {
    if (destroyed || !store || seen('m01')) return false;
    if (!seen('s02')) return false;            // the opening never ran for this player
    if (layer) return false;
    spend('m01');

    const settle = settler(() => {
      /* THE PAPER FIRST, EMI SECOND. She reacts to mail as an outsider and never
       * reads it aloud (the beat sheet's rule), so her line only lands once the
       * slip is off the screen. A missing or dismissed EMI is a silent no-op. */
      try { fire('firstMail', { paper: 'p2' }); } catch (e) { /* a mascot may never break a beat */ }
    }, VN_DIALS.MAIL_CAP_MS, 'mail');

    later(() => {
      if (destroyed) return;
      let ok = true;
      try { ok = !!canInterrupt(); } catch (e) { ok = false; }
      // A class is up: the slip is not worth laying over live play. It is spent
      // either way - this beat belongs to the FIRST stamp and to no other.
      if (!ok) { settle('screen busy'); return; }
      try {
        mountLayer({ bare: true });
        runScene(MAIL, () => settle('done'));
      } catch (e) {
        say('vn mail threw: ' + ((e && e.message) || e));
        settle('threw');
      }
    }, VN_DIALS.MAIL_DELAY_MS);
    return true;
  }

  /* ---------------------- the controller -------------------------------- */
  const api = {
    /** Anything left to play at all? shell.js logs this once; nothing branches. */
    get armed() { return !destroyed && !!store && SCENE_IDS.some((id) => !seen(id)); },
    splashDone,
    gateClass,
    afterCeremony,
    /** Test seam: the ledger, as a copy. */
    seenState() { return Object.assign({}, blob.seen); },
    /** Test/demo seam: bank the whole opening the way a returning player has it. */
    bankAll,
    /** Test/demo seam: has this plate decoded? */
    plateState() { return Object.assign({}, plateOk); },
    destroy() {
      if (destroyed) return;
      destroyed = true;
      killTimers();
      advance = null;
      skipTo = null;
      unmountLayer();
    },
  };

  // WARM THE PLATES while the campus paints. Fire and forget: nothing waits on
  // it, and a failure is simply a scene that stands down when its turn comes.
  if (api.armed) { for (const key of Object.keys(ART)) { try { ensurePlate(ART[key]); } catch (e) { /* noop */ } } }

  return api;
}

export default createFirstBell;
