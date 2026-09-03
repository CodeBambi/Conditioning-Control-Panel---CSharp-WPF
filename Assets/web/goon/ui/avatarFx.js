/* ============================================================================
 * ui/avatarFx.js — the Discord avatar bubbles REACT.
 *
 * Work Item E of the Discord sharing feature. D owns the avatar DOM and emits;
 * this file only listens and decorates. It touches no other ui/*.js.
 *
 * THE CONTRACT (docs/GOON_DISCORD_CONTRACT.md §6), frozen both ends:
 *   in   document CustomEvent `gg-ava`, detail { kind, side: 'you'|'opp', meta? }
 *        kinds: land · fire · drop · pop · emote · mercy · win · lose · draw · cue
 *   dom  `.gg-ava[data-side="you"|"opp"]` containing `.gg-ava-img` or `.gg-ava-tile`
 *   out  avatarFx.attach(rootEl) · avatarFx.detach()
 * Everything no-ops if there is no document, no `.gg-ava` on screen, or attach()
 * was never called. A bubble that never animates is a cosmetic loss; a throw in
 * an event handler on the duel page is not.
 *
 * ---------------------------------------------------------------------------
 * THE ONE-SLOT BUDGET — why the classes land where they do.
 *
 * `animation` is a SHORTHAND. Two rules declaring it on ONE element cancel each
 * other, so a forever-loop and a one-shot can never share a node (the law the
 * .gg-vwin-drift / .gg-vwin-inner split and the emote -bub / -icon split were
 * both written to obey). There are exactly two elements in D's contract, so:
 *
 *   .gg-ava            THE WRAPPER — the idle micro-bob LOOP, and nothing else.
 *                      Rides `animation-play-state: var(--gg-deco-play)` so the
 *                      heat governor parks it at html[data-gg-fx="hot"] with
 *                      every other decoration on the page.
 *   .gg-ava-img /
 *   .gg-ava-tile       THE CHILD — the one-shot REACTION, exactly one at a time.
 *                      One-shots deliberately do NOT ride --gg-deco-play: a
 *                      parked one-shot is a frozen corpse (the .gg-vwin--out
 *                      precedent), and these are 180-1100ms of information.
 *   .gg-avafx-deco/*   DECORATION CHILDREN — rings, stars, confetti. Mine, made
 *                      on demand, removed with the reaction, each with its own
 *                      free slot. pointer-events:none, inside `.gg-ava` only.
 *
 * While a reaction plays the wrapper also takes `.gg-avafx-busy`, which sets the
 * loop to `animation: none` — the `.gg-vwin.is-grabbed .gg-vwin-drift` pattern.
 * A land really does interrupt idle; the bob restarts from zero afterwards, all
 * two pixels of it.
 *
 * NO NEW STACKING LAYERS. Everything animates in place inside `.gg-ava`. MERCY
 * (z60) is not touched, dimmed, covered or filtered by anything here, and no
 * rule in ui/avatarfx.css names it.
 *
 * ---------------------------------------------------------------------------
 * SELF-CLEANING — the bubbling trap.
 *
 * Animation events BUBBLE. A decoration child's ring finishing its 420ms would
 * otherwise clear the parent's 900ms reaction, so the delegated `animationend`
 * listener on the wrapper filters on `e.target === <the img/tile>` and ignores
 * everything else. A timeout backstop rides alongside it for the cases where no
 * animationend ever arrives at all (reduced motion, a re-parented node, a tab
 * throttled mid-reaction) — the .gg-vwin removal-timer precedent.
 *
 * PRIORITY. Reactions do not queue, they OUTRANK: a land interrupts a pop, and
 * win/lose/draw/mercy outrank everything and LATCH — the terminal state class
 * stays on the bubble after the motion is over (that is the recap plate reading
 * "you won"), and only a same-or-higher beat, reset(), detach() or the node
 * leaving the DOM releases it. Mercy is a safety valve: the tapping bubble
 * deflates and the other one BOWS. It never taunts. House rule, not styling.
 *
 * REDUCED MOTION collapses every reaction to a <=1-frame state change: the class
 * goes on, no decoration is built, and it comes off on the next frame. The
 * terminal state classes (won/lost/drew/tapped) still apply, because they are
 * state, not motion. ui/avatarfx.css switches every keyframe off besides.
 * ==========================================================================*/

/** The event D emits and E consumes. Frozen by the contract. */
export const AVA_EVENT = 'gg-ava';

/** The contract's kinds, in contract order. `alarm`/`bow` below are internal. */
export const AVA_KINDS = ['land', 'fire', 'drop', 'pop', 'emote', 'mercy', 'win', 'lose', 'draw', 'cue'];

export const AVA_SIDES = ['you', 'opp'];

/* Class vocabulary — all `gg-avafx*`, so nothing here can collide with D's. */
export const ON_CLASS = 'gg-avafx-on';        // adopted by attach()
export const IDLE_CLASS = 'gg-avafx-idle';    // the wrapper loop is armed
export const BUSY_CLASS = 'gg-avafx-busy';    // a reaction is playing (parks the loop)
export const DECO_CLASS = 'gg-avafx-deco';    // the decoration container
export const FX_PREFIX = 'gg-avafx--';

/** The reaction class for a beat: `land` -> `gg-avafx--land`. */
export const fxClass = (kind) => FX_PREFIX + kind;

/** The latched recap/mercy state classes. */
export const TERMINAL_CLASS = {
  win: FX_PREFIX + 'won',
  lose: FX_PREFIX + 'lost',
  draw: FX_PREFIX + 'drew',
  mercy: FX_PREFIX + 'tapped',
};

/** A pop fires on every popped bubble — four a second is plenty of feedback. */
export const POP_MIN_MS = 250;

/** The other bubble's alarmed jiggle trails the shooter's smug bounce. */
export const FIRE_ECHO_MS = 200;

/** An emote's wiggle is synced to its dwell and never outstays it. */
export const EMOTE_MAX_MS = 2600;
const EMOTE_MIN_MS = 400;

/** How long an animationend may go missing before the backstop fires. */
const BACKSTOP_PAD_MS = 140;

/**
 * THE CATALOG. `prio` decides who wins a collision (higher takes the slot);
 * `ms` is both the keyframe duration (handed to CSS as --gga-dur) and the
 * backstop's clock, so the two can never drift apart.
 *
 *   kind    visual (see ui/avatarfx.css for the keyframes)
 *   land    flinch: shake + squash, red/white impact ring, three stars orbit.
 *           Heavier on 'you' than on 'opp' (--gga-amp, set from [data-side]).
 *   fire    wind-up lean, then a smug forward bounce.
 *   alarm   internal: the OTHER bubble's alarmed jiggle, FIRE_ECHO_MS later.
 *   drop    a happy hop out of a violet/gold sparkle ring.
 *   pop     a 180ms micro-bop. Cheap, throttled, no decoration.
 *   emote   a sympathetic wiggle for as long as the emote sits there.
 *   mercy   the tapper DEFLATES — squash, slow sad spin-out, desaturated.
 *   bow     internal: the other bubble's respectful bow. Never a taunt.
 *   win     proud double bounce, golden glow ring, six confetti bits.
 *   lose    droop and tilt with one slow recover blink, then grey.
 *   draw    a shrug tilt, on BOTH bubbles.
 *   cue     both bubbles lean in and squish. Subtle — it is a warning, not a hit.
 */
export const REACTIONS = {
  pop: { prio: 10, ms: 180 },
  alarm: { prio: 15, ms: 240 },
  cue: { prio: 20, ms: 420 },
  emote: { prio: 30, ms: EMOTE_MAX_MS },
  drop: { prio: 35, ms: 520, deco: 'sparkle' },
  fire: { prio: 40, ms: 520 },
  land: { prio: 50, ms: 620, msOpp: 460, deco: 'impact' },
  bow: { prio: 55, ms: 760 },
  mercy: { prio: 80, ms: 900, terminal: 'mercy' },
  draw: { prio: 90, ms: 760, terminal: 'draw' },
  lose: { prio: 95, ms: 1100, terminal: 'lose' },
  win: { prio: 100, ms: 900, deco: 'gold', terminal: 'win' },
};

/* ------------------------------------------------------------------ helpers */

const doc = () => (typeof document !== 'undefined' ? document : null);

/** The house reduced-motion probe (exec/flashes.js and friends, verbatim). */
function reducedMotion() {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
}

const clamp = (v, lo, hi) => (v < lo ? lo : (v > hi ? hi : v));
const other = (side) => (side === 'you' ? 'opp' : 'you');

/** `.gg-ava-img` or `.gg-ava-tile` — whichever this bubble actually got. */
function fxTargetOf(node) {
  try { return node.querySelector('.gg-ava-img, .gg-ava-tile') || null; }
  catch (_e) { return null; }
}

function sideOf(node) {
  try {
    const raw = (node.dataset && node.dataset.side) || node.getAttribute('data-side') || '';
    return raw === 'opp' ? 'opp' : 'you';
  } catch (_e) { return 'you'; }
}

function isAva(n) {
  try { return !!(n && n.nodeType === 1 && n.classList && n.classList.contains('gg-ava')); }
  catch (_e) { return false; }
}

/** Every `.gg-ava` at or under `n`. */
function avasIn(n) {
  const out = [];
  if (!n || n.nodeType !== 1) return out;
  if (isAva(n)) out.push(n);
  try {
    const kids = n.querySelectorAll ? n.querySelectorAll('.gg-ava') : [];
    for (const k of kids) if (out.indexOf(k) < 0) out.push(k);
  } catch (_e) { /* stub selector engine */ }
  return out;
}

/* ================================================================= factory */

/**
 * One instance drives every bubble on the page. `createAvatarFx()` exists so
 * tests can run isolated instances; the page uses the `avatarFx` singleton.
 */
export function createAvatarFx() {
  /** node -> { side, kind, prio, latch, timer, deco, onEnd, lastPop } */
  const reg = new Map();
  const timers = new Set();
  let root = null;
  let observer = null;
  let listening = false;
  let onEvent = null;

  const track = (id) => { timers.add(id); return id; };
  const untrack = (id) => { if (id) { try { clearTimeout(id); } catch (_e) { /* ignore */ } timers.delete(id); } };

  /* ------------------------------------------------------------ decoration */

  function mk(cls, style) {
    const d = doc();
    if (!d) return null;
    const n = d.createElement('span');
    n.className = cls;
    n.setAttribute('aria-hidden', 'true');
    if (style && n.style && n.style.setProperty) {
      for (const k of Object.keys(style)) { try { n.style.setProperty(k, style[k]); } catch (_e) { /* ignore */ } }
    }
    return n;
  }

  /**
   * Builds the decoration for a beat INSIDE `.gg-ava` — never outside it, never
   * with a z-index, never taking pointer events (the container says so in CSS).
   */
  function buildDeco(node, flavor) {
    const box = mk(DECO_CLASS + ' ' + DECO_CLASS + '--' + flavor);
    if (!box) return null;
    if (flavor === 'impact') {
      box.appendChild(mk('gg-avafx-ring gg-avafx-ring--impact'));
      for (let i = 0; i < 3; i++) box.appendChild(mk('gg-avafx-star', { '--gga-a': (i * 120) + 'deg' }));
    } else if (flavor === 'sparkle') {
      box.appendChild(mk('gg-avafx-ring gg-avafx-ring--sparkle'));
      for (let i = 0; i < 4; i++) box.appendChild(mk('gg-avafx-star gg-avafx-star--spark', { '--gga-a': (i * 90 + 45) + 'deg' }));
    } else if (flavor === 'gold') {
      box.appendChild(mk('gg-avafx-ring gg-avafx-ring--gold'));
      for (let i = 0; i < 6; i++) {
        box.appendChild(mk('gg-avafx-bit', {
          '--gga-a': (i * 60 + 12) + 'deg',
          '--gga-d': (i * 40) + 'ms',
        }));
      }
    }
    try { node.appendChild(box); } catch (_e) { return null; }
    return box;
  }

  /* -------------------------------------------------------------- lifecycle */

  /** Tears the CURRENT reaction down. Terminal state classes survive it. */
  function clearReaction(node, rec) {
    if (!rec) return;
    untrack(rec.timer);
    rec.timer = 0;
    try {
      if (rec.kind) node.classList.remove(fxClass(rec.kind));
      node.classList.remove(BUSY_CLASS);
      node.removeAttribute('data-gg-cue');
      if (node.style && node.style.removeProperty) node.style.removeProperty('--gga-dur');
    } catch (_e) { /* ignore */ }
    if (rec.deco) { try { rec.deco.remove(); } catch (_e) { /* ignore */ } rec.deco = null; }
    rec.kind = null;
    rec.prio = 0;
  }

  /**
   * Plays one beat on one bubble. Returns true if it took the slot.
   * @param {Element} node a `.gg-ava`
   * @param {string} kind a key of REACTIONS
   */
  function play(node, kind, meta) {
    const rec = reg.get(node);
    const def = REACTIONS[kind];
    if (!rec || !def) return false;

    // The pop firehose. Dropped, not queued — a backlog of micro-bops is noise.
    if (kind === 'pop') {
      const now = Date.now();
      if (now - rec.lastPop < POP_MIN_MS) return false;
      rec.lastPop = now;
    }

    // Outrank or go home. `latch` keeps a finished win holding the bubble.
    const floor = Math.max(rec.prio, rec.latch);
    if (def.prio < floor) return false;

    clearReaction(node, rec);

    let ms = (rec.side === 'opp' && def.msOpp) ? def.msOpp : def.ms;
    if (kind === 'emote') {
      const want = meta && Number(meta.ms);
      ms = clamp(Number.isFinite(want) && want > 0 ? want : EMOTE_MAX_MS, EMOTE_MIN_MS, EMOTE_MAX_MS);
    }

    rec.kind = kind;
    rec.prio = def.prio;
    if (def.terminal) rec.latch = def.prio;

    const calm = reducedMotion();
    try {
      if (node.style && node.style.setProperty) node.style.setProperty('--gga-dur', ms + 'ms');
      if (kind === 'cue' && meta && meta.element != null) node.setAttribute('data-gg-cue', String(meta.element));
      // The terminal read-out goes on BEFORE the motion and stays after it.
      if (def.terminal) node.classList.add(TERMINAL_CLASS[def.terminal]);
      node.classList.add(fxClass(kind), BUSY_CLASS);
    } catch (_e) { /* ignore */ }

    if (calm) {
      // <=1 frame: the class exists long enough to be a state change and no
      // longer. No decoration, no keyframes (ui/avatarfx.css sees to that).
      rec.timer = track(setTimeout(() => clearReaction(node, rec), 0));
      return true;
    }

    if (def.deco) rec.deco = buildDeco(node, def.deco);
    // The backstop. animationend is the fast path; this is the one that runs
    // when the node is re-parented, the tab is throttled, or the keyframe was
    // never applied because a stylesheet is missing.
    rec.timer = track(setTimeout(() => clearReaction(node, rec), ms + BACKSTOP_PAD_MS));
    return true;
  }

  /* ------------------------------------------------------------- adoption */

  function adopt(node) {
    if (!node || reg.has(node)) return;
    const rec = { side: sideOf(node), kind: null, prio: 0, latch: 0, timer: 0, deco: null, onEnd: null, lastPop: 0 };
    // THE TRAP: animation events bubble. A decoration child finishing its ring
    // must not clear the reaction on the img/tile, so the delegated listener
    // answers only to the one element that owns the reaction slot.
    rec.onEnd = (e) => {
      try {
        const t = fxTargetOf(node);
        if (!t || !e || e.target !== t) return;
      } catch (_e) { return; }
      clearReaction(node, reg.get(node));
    };
    try {
      node.addEventListener('animationend', rec.onEnd);
      node.addEventListener('animationcancel', rec.onEnd);
      node.classList.add(ON_CLASS);
      if (!reducedMotion()) node.classList.add(IDLE_CLASS);
    } catch (_e) { /* ignore */ }
    reg.set(node, rec);
  }

  function forget(node, { strip = true } = {}) {
    const rec = reg.get(node);
    if (!rec) return;
    clearReaction(node, rec);
    try {
      node.removeEventListener('animationend', rec.onEnd);
      node.removeEventListener('animationcancel', rec.onEnd);
      if (strip) {
        node.classList.remove(ON_CLASS, IDLE_CLASS, BUSY_CLASS);
        for (const c of Object.values(TERMINAL_CLASS)) node.classList.remove(c);
      }
    } catch (_e) { /* ignore */ }
    reg.delete(node);
  }

  /* ----------------------------------------------------------- the routing */

  /**
   * One beat -> the bubbles it touches. The pairings are the fun part and they
   * live HERE rather than in D, because E owns both bubbles:
   *   fire  -> shooter bounces; the target jiggles FIRE_ECHO_MS later
   *   mercy -> the tapper deflates; the other one bows
   *   draw  -> both shrug        cue -> both lean in
   * win/lose are NOT mirrored: D says which side won, and inventing the other
   * half would double up the moment D emits both.
   */
  function route(kind, side, meta) {
    const s = side === 'opp' ? 'opp' : 'you';
    if (kind === 'draw' || kind === 'cue') {
      forEachSide('you', (n) => play(n, kind, meta));
      forEachSide('opp', (n) => play(n, kind, meta));
      return;
    }
    if (kind === 'mercy') {
      forEachSide(s, (n) => play(n, 'mercy', meta));
      forEachSide(other(s), (n) => play(n, 'bow', meta));
      return;
    }
    forEachSide(s, (n) => play(n, kind, meta));
    if (kind === 'fire') {
      const id = track(setTimeout(() => {
        timers.delete(id);
        forEachSide(other(s), (n) => play(n, 'alarm', meta));
      }, FIRE_ECHO_MS));
    }
  }

  function forEachSide(side, fn) {
    for (const node of Array.from(reg.keys())) {
      const rec = reg.get(node);
      if (!rec || rec.side !== side) continue;
      try { fn(node); } catch (_e) { /* one bad bubble never stops the other */ }
    }
  }

  /* ---------------------------------------------------------------- public */

  const api = {
    /**
     * Idempotent. Adopts every `.gg-ava` under `rootEl` (default: document) and
     * keeps adopting them as D paints new ones — the VS splash, the HUD minis
     * and the recap plates are three different sets of nodes.
     * @param {Element|Document} [rootEl]
     */
    attach(rootEl) {
      const d = doc();
      if (!d) return api;
      const next = rootEl || d;
      if (listening && next === root) return api;   // same root: nothing to do
      if (listening) api.detach();                  // re-point at a new root
      root = next;

      for (const n of avasIn(root === d ? (d.body || d) : root)) adopt(n);

      onEvent = (e) => {
        const detail = (e && e.detail) || null;
        if (!detail) return;
        const kind = String(detail.kind || '');
        if (AVA_KINDS.indexOf(kind) < 0) return;    // unknown beats are ignored, never thrown on
        try { route(kind, detail.side, detail.meta || null); } catch (_e) { /* ignore */ }
      };
      try { d.addEventListener(AVA_EVENT, onEvent); } catch (_e) { /* ignore */ }

      if (typeof MutationObserver === 'function') {
        try {
          observer = new MutationObserver((records) => {
            for (const r of records || []) {
              for (const n of r.addedNodes || []) for (const a of avasIn(n)) adopt(a);
              for (const n of r.removedNodes || []) for (const a of avasIn(n)) forget(a, { strip: false });
            }
          });
          observer.observe(root, { childList: true, subtree: true });
        } catch (_e) { observer = null; }
      }

      listening = true;
      return api;
    },

    /** Everything this module ever added comes back off. Safe to call twice. */
    detach() {
      const d = doc();
      if (observer) { try { observer.disconnect(); } catch (_e) { /* ignore */ } observer = null; }
      if (d && onEvent) { try { d.removeEventListener(AVA_EVENT, onEvent); } catch (_e) { /* ignore */ } }
      onEvent = null;
      for (const node of Array.from(reg.keys())) forget(node);
      for (const id of Array.from(timers)) { try { clearTimeout(id); } catch (_e) { /* ignore */ } }
      timers.clear();
      reg.clear();
      root = null;
      listening = false;
      return api;
    },

    /**
     * Drops the latched recap/mercy state so the bubbles can react again. Not
     * in the contract — an addition, because a rematch that re-uses the same
     * nodes would otherwise stay stuck on "you lost". Node removal releases the
     * latch by itself, which is the normal path.
     */
    reset() {
      for (const node of Array.from(reg.keys())) {
        const rec = reg.get(node);
        clearReaction(node, rec);
        rec.latch = 0;
        try { for (const c of Object.values(TERMINAL_CLASS)) node.classList.remove(c); } catch (_e) { /* ignore */ }
      }
      return api;
    },

    /** Diagnostics for the selftest; not used by the page. */
    get attached() { return listening; },
    get count() { return reg.size; },
    stateOf(node) {
      const rec = reg.get(node);
      return rec ? { side: rec.side, kind: rec.kind, prio: rec.prio, latch: rec.latch, deco: !!rec.deco } : null;
    },
  };
  return api;
}

/** The page's instance. boot.js/D call attach(); nothing else needs to know. */
export const avatarFx = createAvatarFx();

export default avatarFx;
