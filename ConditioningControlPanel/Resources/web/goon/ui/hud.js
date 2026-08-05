/* ============================================================================
 * ui/hud.js — the duel desk.
 *
 * Composes the live-match HUD inside #gg-hud and owns the layout grid:
 *
 *   ┌ top ────────────────────────────────────────────────────────────────┐
 *   │ score · charges               11:47 ▮▮▯▯▯      attention  ⊟  ⚙      │
 *   ├ body ───────────────────────────────────────────────────────────────┤
 *   │ ┌ arsenal ──┐◂                                            [monitor] │
 *   │ │ HEAT ▰▰▱  │      (the stage well — never covered)       [receipts]│
 *   │ │ ▣▣ ▣▣     │                                                       │
 *   │ │ ▣▣ ▣▣     │                                                       │
 *   │ └───────────┘                                                       │
 *   ├ bottom ─────────────────────────────────────────────────────────────┤
 *   │ own effects                                     you're telling them │
 *   └──────────────────────── MERCY (its own layer, ui/mercy.js) ─────────┘
 *
 * THE ARSENAL IS A COLLAPSIBLE SIDEBAR (owner, 2026-08-04). The eight stickers,
 * their cooldown dials and the heat gauge that feeds them live in one panel on
 * the LEFT behind a handle that never hides — collapsed it is the handle and a
 * count badge, nothing more. Keys 1-7 fire either way (ui/arsenal.js binds them
 * on `window`), and the panel is a slide-in drawer on a phone rather than a
 * squeeze on the play area. See the sidebar's own block below the top bar.
 *
 * THE MIDDLE IS SACRED. The HUD frames #gg-stage, it never lands on top of it:
 * everything here is edge-anchored and the centre column is pointer-transparent.
 *
 * ZEN (the ⊟ beside the gear). One bit, and everything it hides — score, the
 * closeness dial, MERCY — is hidden by ui/hud.css off .gg-hud--zen and
 * html[data-gg-zen]. The monitor, the arsenal and the heat gauge that feeds it
 * stay: they are what the duel is played on. See the toggle's own block below
 * for why hiding MERCY is safe — and why the voice mic is the one thing zen
 * has to hide in JS as well as in CSS.
 *
 * ANIMATION BUDGET. Chrome animations go through fx.play(): at most two run at
 * once and anything that waited more than 600 ms is dropped rather than played
 * late. The effect layers already flip html[data-gg-fx]; goon.css's armor rule
 * parks decorative motion at "hot" and we do not fight it.
 *
 * Node-import-safe: no DOM at import, only inside mountHud().
 * ==========================================================================*/

import { GoonConsts, GoonElement, GoonMatchPhase, GoonPayloadKind } from '../core/contracts.js';
import { localMonotonicMs } from '../core/clock.js';
import { mountOpponent, EMOTE_MINI_MS } from './opponent.js';
import { mountArsenal, ARSENAL_ITEMS } from './arsenal.js';
import { mountCloseness } from './closeness.js';
import { mountAttention } from './attention.js';
import { mountEmotes } from './emotes.js';
import { mountAnnouncer } from './announcer.js';
import { mountMicHud } from './voice/micHud.js';
import { createDropRoller } from './drops.js';
import { avatarNode, emitAva } from './avatar.js';
import { setPreviewMedia } from './throwPreview.js';
import { resolveArsenalOpen, ARSENAL_OPEN_ON, ARSENAL_OPEN_OFF } from './prefs.js';
import { S } from './strings.js';

/** exec/bubbles.js's economy seam. Kept as a literal so ui/ never imports exec/. */
export const BUBBLE_POP_EVENT = 'gg-bubble-pop';

/**
 * The desk's ask for "open their Discord DM". Same shape and same reason as
 * `gg-options-open` above it: the HUD is mounted by a dynamically loaded
 * sibling and can reach neither boot's `sheets` nor its `discord` singleton, so
 * it raises an event and boot.js confirms and posts the verb. The page never
 * holds an id — the detail carries a NAME and nothing else.
 */
export const DISCORD_DM_EVENT = 'gg-discord-dm';

/**
 * THE ZEN BIT — one class on the frame, one attribute on <html>.
 *
 * "add a button that hides the ui" (owner, 2026-08-04). Everything the toggle
 * takes away is hidden by ui/hud.css off these two names and nothing else, so
 * the whole feature is one boolean: no per-element `hidden` flags to keep in
 * sync and nothing to leave behind if a paint throws.
 *
 * The attribute exists because MERCY is NOT inside the HUD: #gg-mercy is its own
 * fixed layer (z60, goon.css) and no descendant selector from .gg-hud-frame can
 * reach it. Exported so the suite pins the same two strings the CSS does.
 */
export const HUD_ZEN_CLASS = 'gg-hud--zen';
export const HUD_ZEN_ATTR = 'data-gg-zen';

/**
 * THE ARSENAL SIDEBAR's one bit — a class on `.gg-arsenal` and nothing else.
 *
 * Unlike zen there is no <html> mirror and there must not be one: everything the
 * collapse touches is a descendant of the sidebar itself, MERCY is not involved
 * at any point, and a stray attribute outliving a match is exactly the failure
 * mode zen already had to be hand-unwound for.
 */
export const ARSENAL_OPEN_CLASS = 'is-open';

/**
 * The breakpoint the drawer behaviour hangs off, spelled ONCE. It is the same
 * query ui/hud.css's portrait/narrow block uses — the sidebar goes from a grid
 * column to a slide-in drawer there, and resolveArsenalOpen() starts it shut —
 * so the two must not be able to disagree. selftest-hud pins them equal.
 */
export const ARSENAL_NARROW_MQ = '(orientation: portrait), (max-width: 720px)';

/** Keys 1..N, i.e. one per payload slot. ui/arsenal.js owns the binding. */
const ARSENAL_KEY_COUNT = ARSENAL_ITEMS.filter((i) => i.kind !== null).length;

/** Is this a phone-shaped screen? Absent matchMedia (node, old hosts) = no. */
function isNarrowViewport() {
  try {
    return typeof matchMedia === 'function' && !!matchMedia(ARSENAL_NARROW_MQ).matches;
  } catch (_e) { return false; }
}

/** Own-draft element cues -> the chip that shows them on the bottom-left rail. */
const ELEMENT_META = Object.freeze({
  [GoonElement.Flashes]: { glyph: '✦', label: 'flashes' },
  [GoonElement.Videos]: { glyph: '▶', label: 'video' },
  [GoonElement.Subliminals]: { glyph: '≋', label: 'subliminals' },
  [GoonElement.Bubbles]: { glyph: '○', label: 'bubbles' },
  [GoonElement.LockCards]: { glyph: '▢', label: 'lock card' },
  [GoonElement.ToyPatterns]: { glyph: '∿', label: 'toy' },
  [GoonElement.BrainDrain]: { glyph: '◍', label: 'brain drain' },
  [GoonElement.BouncingText]: { glyph: '⇄', label: 'bouncing text' },
  [GoonElement.Spiral]: { glyph: '◎', label: 'spiral' },
});

/** Admitted inbound payloads -> the same chip, violet-edged: THEY did this. */
const PAYLOAD_META = Object.freeze({
  [GoonPayloadKind.FlashBurst]: { glyph: '✦', label: 'flash burst' },
  [GoonPayloadKind.SubliminalStorm]: { glyph: '≋', label: 'subliminal storm' },
  [GoonPayloadKind.BubbleSwarm]: { glyph: '○', label: 'bubble swarm' },
  [GoonPayloadKind.Video]: { glyph: '▶', label: 'video' },
  [GoonPayloadKind.LockCard]: { glyph: '▢', label: 'lock card' },
  [GoonPayloadKind.ToyPattern]: { glyph: '∿', label: 'toy pattern' },
  [GoonPayloadKind.BrainDrain]: { glyph: '◍', label: 'brain drain' },
  [GoonPayloadKind.Spiral]: { glyph: '◎', label: 'spiral' },
});

/**
 * How far ahead the engine schedules an outbound payload. core/match.js uses
 * max(GoonConsts.MinScheduleBufferMs, 1500) and does not publish the number, so
 * the miniature leads by the same amount rather than starting the animation the
 * instant the tile is tapped. Being ~0.5s out here costs nothing: the window is
 * seconds-to-minutes long and it closes on the receipt either way.
 */
const PAYLOAD_LEAD_MS = Math.max(GoonConsts.MinScheduleBufferMs, 1500);

/** Score count-up window. */
const SCORE_LERP_MS = 250;
/** Concurrent chrome animations, and how long a queued one may wait. */
const ANIM_BUDGET = 2;
const ANIM_STALE_MS = 600;

// ------------------------------------------------------------------ helpers

const doc = () => (typeof document !== 'undefined' ? document : null);

function el(tag, cls, text) {
  const d = doc();
  if (!d || typeof d.createElement !== 'function') return null;
  const n = d.createElement(tag);
  if (cls && n) n.className = cls;
  if (text != null && n) n.textContent = String(text);
  return n;
}

function add(parent, child) {
  if (parent && child && typeof parent.appendChild === 'function') parent.appendChild(child);
  return child;
}

function cls(node, name, on) {
  if (!node || !node.classList) return;
  try { node.classList[on ? 'add' : 'remove'](name); } catch (_e) { /* stub DOM */ }
}

function text(node, value) {
  if (node) node.textContent = value == null ? '' : String(value);
}

function sfx(audio, id) {
  try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ }
}

function nowMs() {
  try {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') return performance.now();
  } catch (_e) { /* fall through */ }
  return Date.now();
}

function mmss(ms) {
  const total = Math.max(0, Math.round(ms / 1000));
  const m = Math.floor(total / 60);
  const s = total % 60;
  return m + ':' + (s < 10 ? '0' : '') + s;
}

function createLedger() {
  const list = [];
  return {
    add(fn) { if (typeof fn === 'function') list.push(fn); },
    listen(target, type, fn, opts) {
      if (!target || typeof target.addEventListener !== 'function') return;
      target.addEventListener(type, fn, opts);
      list.push(() => { try { target.removeEventListener(type, fn, opts); } catch (_e) { /* gone */ } });
    },
    interval(ms, fn) {
      if (typeof setInterval !== 'function') return 0;
      const id = setInterval(fn, ms);
      list.push(() => { try { clearInterval(id); } catch (_e) { /* gone */ } });
      return id;
    },
    run() { while (list.length) { const fn = list.pop(); try { fn(); } catch (_e) { /* keep unwinding */ } } },
  };
}

/** The chrome-animation budget + a single shared rAF pump. */
function createFx() {
  const frames = new Set();
  const queue = [];
  let raf = 0;
  let running = 0;

  function start() {
    if (raf || frames.size === 0 || typeof requestAnimationFrame !== 'function') return;
    raf = requestAnimationFrame(tick);
  }
  function tick() {
    raf = 0;
    for (const fn of Array.from(frames)) { try { fn(); } catch (_e) { /* one bad painter must not stop the rest */ } }
    start();
  }
  function drain() {
    const t = nowMs();
    while (queue.length && running < ANIM_BUDGET) {
      const job = queue.shift();
      if (t - job.at > ANIM_STALE_MS) continue;   // waited too long: it would read as lag, not feedback
      running++;
      try { job.fn(); } catch (_e) { /* ignore */ }
      setTimeout(() => { running = Math.max(0, running - 1); drain(); }, Math.max(60, job.ms | 0));
    }
  }

  return {
    play(ms, fn) { if (typeof fn !== 'function') return; queue.push({ ms, fn, at: nowMs() }); drain(); },
    onFrame(fn) {
      if (typeof fn !== 'function') return () => {};
      frames.add(fn);
      start();
      return () => frames.delete(fn);
    },
    stop() {
      frames.clear();
      queue.length = 0;
      if (raf && typeof cancelAnimationFrame === 'function') { try { cancelAnimationFrame(raf); } catch (_e) { /* gone */ } }
      raf = 0;
    },
  };
}

function logTo(sink, entry) {
  if (!sink) return;
  try {
    if (typeof sink === 'function') sink(entry);
    else if (typeof sink.push === 'function') sink.push(entry);
    else if (typeof sink.add === 'function') sink.add(entry);
    else if (typeof sink.log === 'function') sink.log(entry);
  } catch (_e) { /* the log is never load-bearing */ }
}

// ------------------------------------------------------------------ mount

/**
 * @param {object} o
 * @param {object} o.match      GoonMatchService
 * @param {object} [o.session]  boot.js session (identity/prefs/caps)
 * @param {object} [o.audio]    {sfx(id)}
 * @param {object} [o.prefs]    user prefs (reduced motion etc.)
 * @param {object} [o.media]    exec/media pool (unused by the HUD in v1)
 * @param {object} [o.matchLog] array/fn/sink for a human-readable match log
 * @param {object} [o.discord]  ui/discord.js handle — the avatar minis + DM
 * @param {object} [o.voice]    ui/voice/voiceService.js handle — the mic + the
 *                              incoming chip. null (or a build with the feature
 *                              off) simply means there is no mic on the desk.
 * @returns {{unmount:Function}}
 */
export function mountHud({ match, session = null, audio = null, prefs = null, media = null,
  matchLog = null, discord = null, voice = null } = {}) {
  const led = createLedger();
  const fx = createFx();
  const d = doc();
  const host = d && typeof d.getElementById === 'function' ? d.getElementById('gg-hud') : null;
  if (!host || !match) return { unmount() { fx.stop(); led.run(); } };

  /* THE SHARED LOG SINK — and, since 2026-08-04, the seam two `gg-ava` beats
   * ride out on (docs/GOON_DISCORD_CONTRACT.md §6).
   *
   * ui/emotes.js and ui/announcer.js each already write ONE tagged entry at
   * exactly the instant the bubbles should react — `emote-out` as a line is
   * sent, `announce` as a ribbon goes on screen — and each tag has exactly one
   * producer on the page. Reading them here means neither file is touched and
   * neither sub-mount grows a second callback that fires on the same click.
   *
   * It also keeps both mount calls in their pristine shorthand form, which the
   * hud suite pins by source: a wrapper argument at either call site is a
   * silent test failure (it was, for one commit). */
  const onLog = (entry) => {
    try {
      if (entry) {
        if (entry.t === 'emote-out') emitAva('emote', 'you', { emote: entry.text || entry.icon || '', ms: EMOTE_MINI_MS });
        else if (entry.t === 'announce') emitAva('cue', 'you', { element: entry.element });
      }
    } catch (_e) { /* a decoration must never break the log */ }
    logTo(matchLog, entry);
  };

  host.hidden = false;
  const root = el('div', 'gg-hud-frame');
  if (!root) return { unmount() { fx.stop(); led.run(); } };
  add(host, root);

  // ---------------------------------------------------------------- top bar
  const top = add(root, el('div', 'gg-hud-top'));

  const scoreBox = add(top, el('div', 'gg-scorebox'));
  const scoreEl = add(scoreBox, el('div', 'gg-score', '0'));
  const chargeEl = add(scoreBox, el('div', 'gg-charges', S.hud.charges(0, GoonConsts.ChargeCap)));

  /* NO SCORE-MULTIPLIER READOUT (owner, 2026-08-05: "the risk level indicator
   * is not intuitive — the heat gauge already does the job").
   *
   * A third line used to sit here under the charge readout: `.gg-mult`, née
   * `.gg-risk`, painting `match.scoring.riskMultiplier` as "×1.30 score". It
   * was the LAST player-facing limb of the risk system — batch 7 took the
   * seven-segment tier meters and the per-tile pips off the draft screen and
   * left this one behind, renamed. Renaming it never fixed the thing that was
   * wrong with it: a bare multiplier with no key, on a number the player agreed
   * to once at the draft and can no longer change, that never moves for the
   * whole match. Nothing here reads scoring.riskMultiplier any more.
   *
   * THE ENGINE KEEPS IT. core/scoring.js multiplies every second by it and
   * core/draft.js still sums the tier that produces it (C#-parity names, wire
   * and RNG vectors depend on them) — this was a UI removal, not a mechanics
   * change. The score in the line above is the multiplier's whole visible
   * effect, which is the part a player could ever act on.
   *
   * AND THE CHARGE PIPS WENT TOO (owner, 2026-08-05, second pass: "the little
   * yellow squares that we were supposed to have removed"). The first pass kept
   * them on the argument that they are NOT the risk indicator — they are what
   * you are holding to throw, the wire validates throws against them and the
   * arsenal pays its costs out of them, so they were real and load-bearing in a
   * way the multiplier never was. That argument was about the DATA and the
   * owner's complaint was about the SHAPE. A row of five rotated squares under
   * the score is indistinguishable, on a phone, from the row of rotated squares
   * that had just been taken off the draft tiles: the player cannot tell "your
   * ammunition" from "the risk meter you asked me to delete" by looking, and
   * being right about the difference does not help them at arm's length.
   *
   * So the DATA stays and the SHAPE goes: one small line, "3 / 5 charges", in
   * words. It is strictly more legible than the pips ever were — it survives a
   * squint, it reads aloud, it does not depend on a fill colour, and it says the
   * cap out loud instead of asking you to count outlines to find it. There is no
   * diamond left anywhere in this HUD; the only pips on the desk now are the
   * arsenal's amber `.gg-cost-pip` cost dots, which are round, attached to the
   * tile whose price they are, and were never part of this complaint.
   *
   * THE GAIN JUICE SURVIVES INTACT — sfx('gg-charge') and the floating "+1"
   * chip both still fire in paintCharges(). Only the per-pip pop animation died
   * with the pips, because there is no longer a pip to pop. */

  const timerBox = add(top, el('div', 'gg-timer gg-plate'));
  const timerClock = add(timerBox, el('div', 'gg-timer-clock', '0:00'));
  const timerTrack = add(timerBox, el('div', 'gg-timer-track'));
  const timerFill = add(timerTrack, el('i', 'gg-timer-fill'));

  /* ---- THE PERSISTENT AVATAR MINIS (contract §6) -------------------------
   * Two bubbles that stay up for the whole of Live: yours at the head of your
   * own column, theirs directly over their monitor miniature.
   *
   * PLACEMENT IS A SAFETY DECISION, not a layout one. Both sit in edge-anchored
   * grid cells the desk already owns, which keeps them out of the two bands
   * nothing may enter: MERCY's bottom-centre keep-out (.gg-hud-bottom already
   * reserves 4.6rem for it) and the announcer ribbon's top-centre strip. They
   * are the flying targets the VS splash aims at, too — ui/avatar.js finds them
   * by `.gg-ava--mini[data-side]`.
   *
   * They are also POINTER-TRANSPARENT except for the one button on theirs: see
   * the 0,2,0 opt-out in ui/hud.css, written after `.gg-hud-frame .gg-plate`.  */
  function mini(side, name, uri) {
    const node = avatarNode({ side, name, dataUri: uri, size: 'mini' });
    if (node && node.classList) node.classList.add('gg-ava--mini');
    return node;
  }

  const youName = (session && session.identity && session.identity.displayName)
    || match.localDisplayName || '';
  const youAva = mini('you', youName, null);
  const youAvaBox = add(scoreBox, el('div', 'gg-ava-minibox gg-ava-minibox--you'));
  if (youAva) add(youAvaBox, youAva);

  const rightTop = add(top, el('div', 'gg-hud-topright'));
  const attSlot = add(rightTop, el('div', 'gg-att-slot'));

  /* ---- THE ZEN TOGGLE ---------------------------------------------------
   * One button, always on screen, that takes the score, the closeness dial and
   * the mercy button away and gives them back. What is left is what the duel is
   * actually played on: their monitor and the arsenal.
   *
   * IT IS ITS OWN UNDO. The button never hides — it would be a one-way door on
   * a phone, where there is no Escape key to fall back to — and it keeps the
   * gear's 48px hit area for the same reason.
   *
   * HIDING MERCY IS A PREFERENCE, NOT A CHANGE TO THE SAFETY CONTRACT. Escape
   * concedes through boot.js's ladder, which calls match.declareMercy() itself
   * and has never touched this button (ui/mercy.js's own pointerdown handler is
   * a second, independent path). Nothing here reads or focuses the button
   * either, so a hidden mercy is a mercy that still works — and the title says
   * so out loud while it is away.
   */
  const zenBtn = add(rightTop, el('button', 'gg-zen'));
  let zenOn = false;
  if (zenBtn) zenBtn.type = 'button';

  /* THE MIC IS THE ONE THING ZEN CANNOT HIDE WITH CSS ALONE. Everything else on
   * this toggle is chrome that stops being painted; the mic can be HELD, and a
   * captured pointer bypasses hit testing — `display:none` would take the strip
   * away and leave the recording (and the OS microphone light) running behind
   * it. So the bit is pushed into ui/voice/micHud.js as well, which cancels a
   * live hold on the same path the feature going away takes.
   *
   * Declared here and assigned after the mount ~300 lines down, because setZen
   * is hoisted but the mic is not: null is the whole of the startup case, and
   * the remembered preference is replayed into the handle the moment it exists. */
  let micRef = null;

  function paintZen() {
    const label = zenOn ? S.hud.zenShow : S.hud.zenHide;
    text(zenBtn, zenOn ? S.hud.zenShowGlyph : S.hud.zenHideGlyph);
    if (zenBtn && zenBtn.setAttribute) {
      try {
        zenBtn.setAttribute('aria-pressed', zenOn ? 'true' : 'false');
        zenBtn.setAttribute('aria-label', label);
        zenBtn.title = zenOn ? S.hud.zenShowTitle : label;
      } catch (_e) { /* stub DOM */ }
    }
    cls(root, HUD_ZEN_CLASS, zenOn);
    try {
      const de = d && d.documentElement;
      if (de && typeof de.setAttribute === 'function') {
        if (zenOn) de.setAttribute(HUD_ZEN_ATTR, 'on');
        else de.removeAttribute(HUD_ZEN_ATTR);
      }
    } catch (_e) { /* no <html>: the frame class still does the HUD half */ }
  }

  function setZen(on, remember) {
    const next = !!on;
    if (next === zenOn) return;
    zenOn = next;
    paintZen();
    try { micRef?.setHudHidden(zenOn); } catch (_e) { /* a no-op handle has no bit */ }
    // Persisting is a nicety, not the state: the bit above is already true.
    if (remember && prefs && typeof prefs.set === 'function') {
      try { prefs.set('hudZen', zenOn); } catch (_e) { /* no store, no problem */ }
    }
  }

  // Remembered across matches when there is a store to remember it in.
  if (prefs && typeof prefs.get === 'function') {
    try { if (prefs.get('hudZen')) zenOn = true; } catch (_e) { /* ignore */ }
  }
  paintZen();
  led.listen(zenBtn, 'click', () => { sfx(audio, 'ui-select'); setZen(!zenOn, true); });
  // The <html> attribute outlives this node, so it is unwound by hand: a stray
  // data-gg-zen would hide the mercy button of the NEXT match.
  led.add(() => {
    try {
      const de = d && d.documentElement;
      if (de && typeof de.removeAttribute === 'function') de.removeAttribute(HUD_ZEN_ATTR);
    } catch (_e) { /* gone */ }
  });

  const gear = add(rightTop, el('button', 'gg-gear', '⚙'));
  if (gear) {
    gear.type = 'button';
    gear.setAttribute && gear.setAttribute('aria-label', 'options');
    led.listen(gear, 'click', () => {
      try {
        if (d && typeof d.dispatchEvent === 'function' && typeof CustomEvent === 'function') {
          d.dispatchEvent(new CustomEvent('gg-options-open', { bubbles: true }));
        }
      } catch (_e) { /* the drawer owner may not be listening yet */ }
    });
  }

  // ------------------------------------------------------------------ body
  const body = add(root, el('div', 'gg-hud-body'));

  /* ==== THE ARSENAL SIDEBAR ================================================
   * Everything you can THROW lives in one collapsible panel on the LEFT, behind
   * a handle that never hides. (owner, 2026-08-04: "the throwable items should
   * live in a sidebar that can be collapsed".)
   *
   * WHY LEFT. The right edge is spoken for three times over — the opponent's
   * monitor column, the receipts strip under it, and exec/videos.js's
   * keepOutRects(), which refuses to BEAR a floating window on the right 22vw or
   * the bottom-centre mercy gutter. The left gutter is the only side of the desk
   * with nothing structural in it, and it is nowhere near MERCY's keep-out
   * (0.28w..0.72w), so a sidebar there cannot cover the one button that must
   * never be covered.
   *
   * WHAT IS IN IT: the eight stickers (as two four-tall columns — a single
   * vertical stack of eight is ~720px, which does not fit a laptop, and a
   * 2-column grid keeps every tile a full finger wide), and the HEAT GAUGE,
   * which is the arsenal's own drop meter and belongs with the economy it feeds
   * rather than under a monitor it has nothing to do with.
   *
   * IT DOES NOT EAT THE FIELD. `.gg-arsenal` is pointer-events:none (ui/hud.css,
   * at the same 0,2,0 as the .gg-plate rule that would otherwise re-enable it);
   * only the tiles and the handle opt back in. Bubbles rise on #gg-fx BELOW the
   * HUD, so a bubble drifting behind the panel is still poppable through it —
   * which matters more here than anywhere, because popping bubbles is the only
   * way the arsenal is ever fed. There is NO scrim, on any breakpoint: a
   * full-screen scrim is what froze the drop economy the last time a sheet
   * opened at Live, and one above z60 would bury MERCY.
   *
   * COLLAPSED IS A REAL COLLAPSE. `.gg-arsenal-panel` is display:none, so the
   * tiles are not merely invisible — there is no box left to hit. Keys 1-7 are
   * bound on `window` by ui/arsenal.js and never look at the DOM, so the whole
   * deck still fires from a shut drawer; the handle pulses and carries a count
   * badge so a shut drawer still says what you are holding. */
  const sideBox = add(body, el('aside', 'gg-arsenal'));
  const sidePanel = add(sideBox, el('div', 'gg-arsenal-panel'));

  /* ---- THE HEAT GAUGE -----------------------------------------------------
   * What popping bubbles is FOR, made visible. It sits at the head of the
   * arsenal panel, directly on top of the rails it fills: gauge climbs ->
   * sticker lights up, in one glance and one column. (It rode the foot of the
   * monitor column until the sidebar existed; it moved WITH the arsenal, because
   * it is the item economy's readout and not the monitor's.)
   *
   * IT IS A READOUT, NOT A CONTROL. No .gg-plate (which would re-enable pointer
   * events at 0,2,0 and put a dead 190px box over the desk), no click target, no
   * focus. ui/hud.css opts it out explicitly at the same specificity anyway —
   * and the sidebar around it is pointer-transparent for the same reason.
   *
   * IT NEVER TOUCHES MERCY. #gg-mercy is its own fixed z60 layer in goon.css;
   * this lives inside .gg-hud-frame, whose whole layer (#gg-hud, z40) is a
   * stacking context BELOW it — no z-index written in this tier can reach past
   * it. It gates nothing and covers nothing.
   *
   * role=progressbar + aria-valuetext because the fill is a colour, and colour
   * is never the only channel on this tier. */
  const heatBox = add(sidePanel, el('div', 'gg-heat'));
  if (heatBox && heatBox.setAttribute) {
    try {
      heatBox.setAttribute('role', 'progressbar');
      heatBox.setAttribute('aria-label', S.hud.heatLabel);
      heatBox.setAttribute('aria-valuemin', '0');
      heatBox.setAttribute('aria-valuemax', '100');
      heatBox.setAttribute('aria-valuenow', '0');
    } catch (_e) { /* stub DOM */ }
  }
  add(heatBox, el('span', 'gg-heat-label', S.hud.heatLabel));
  const heatTrack = add(heatBox, el('span', 'gg-heat-track'));
  const heatFill = add(heatTrack, el('i', 'gg-heat-fill'));

  // The two rails keep their names and their four-and-four split: they are the
  // sidebar's two columns now instead of the desk's two edges, and ui/arsenal.js
  // is handed exactly the same pair of hosts it always was.
  const railBox = add(sidePanel, el('div', 'gg-arsenal-rails'));
  const leftRail = add(railBox, el('div', 'gg-rail gg-rail--left'));
  const rightRail = add(railBox, el('div', 'gg-rail gg-rail--right'));

  /* ---- THE MIC SLOT — under the emote tile, which is where it was looked for
   *
   * IT LIVED IN THE OPPONENT'S COLUMN UNTIL 2026-08-05, under their receipts.
   * The reasoning was placement safety (a flow child of a column cannot land on
   * MERCY's gutter or the dial), and it was correct and beside the point: the
   * owner play-tested this three times across three devices and looked for the
   * mic "right under the Emote icon" every time, because holding a mic is a
   * thing you DO — it belongs with the other things you do, not with the readout
   * of what is being done to you. The emote tile is the last item on the right
   * rail (ui/arsenal.js ARSENAL_ITEMS), so a full-width row immediately after
   * the rails is literally underneath it.
   *
   * IT IS STILL FLOW, NOT COORDINATES. Last child of `.gg-arsenal-panel`, so the
   * browser keeps it inside the drawer at every viewport, including the
   * short-viewport pass where the rails turn ninety degrees. Nothing here is
   * positioned against the frame and there is no keep-out band to miss.
   *
   * WHAT IT COSTS, honestly: the drawer is SHUT by default on a phone
   * (resolveArsenalOpen), so the mic is behind the handle there — exactly like
   * every other control a player throws. That is the trade, and it is the same
   * one the eight tiles already make.
   *
   * WHAT IT DOES NOT COST: the strip still cannot eat the stage. The panel is
   * pointer-events:none at 0,2,0 (rule 1 up in the sidebar block) and ui/hud.css
   * re-opts ONLY `.gg-voice-btn` back in, later in the file, at the same weight.
   *
   * The CHIP stays in their column, under their bezel — it is about THEIR voice
   * and it belongs as close to their face as this file can put it. */
  const voiceHost = add(sidePanel, el('div', 'gg-voice-host'));

  /* ---- THE HANDLE — the one node that never hides -------------------------
   * Last in the DOM so it sits on the panel's INNER edge (the side facing the
   * field) in both states, which is where a thumb reaches for it and where it
   * stays put when the panel comes and goes. Sized >= 48px both ways: it is the
   * only way back to your own items on a phone, so it is never one of the things
   * that shrinks. */
  const sideTab = add(sideBox, el('button', 'gg-arsenal-tab'));
  if (sideTab) sideTab.type = 'button';
  const sideTabGlyph = add(sideTab, el('i', 'gg-arsenal-tab-glyph'));
  if (sideTabGlyph) sideTabGlyph.setAttribute && sideTabGlyph.setAttribute('aria-hidden', 'true');
  const sideTabCount = add(sideTab, el('span', 'gg-arsenal-tab-count'));
  if (sideTabCount) sideTabCount.hidden = true;

  add(body, el('div', 'gg-well'));                         // the stage window: never covered
  const rightCol = add(body, el('div', 'gg-rightcol'));
  const oppAvaBox = add(rightCol, el('div', 'gg-ava-minibox gg-ava-minibox--opp'));
  const monHost = add(rightCol, el('div', 'gg-mon-host'));
  /* THEIR VOICE, IN THEIR COLUMN (ui/voice/micHud.js) — the CHIP half.
   *
   * A FLOW child of the monitor column, and that is the whole of the placement
   * decision: the browser lays it out, so it cannot land on MERCY's
   * bottom-centre gutter, the closeness dial, either rail, the announcer ribbon
   * or the monitor itself at ANY viewport — including the portrait pass, where
   * this column moves to the top of the body (order: -1). Nothing here is
   * absolutely placed against the frame, so there is no band to keep clear of
   * and nothing to re-measure when the phone rules move one.
   *
   * Directly under the bezel: the speaking indicator for a note THEY sent, as
   * close to their face as this file can put it without reaching into
   * ui/opponent.js. The MIC that sends yours moved to the arsenal drawer, under
   * the emote tile (see THE MIC SLOT above) — the two halves of this feature
   * point in opposite directions and now sit accordingly.
   *
   * It collapses to nothing when the feature is not live (hidden host, not an
   * emptied one — see micHud's header on why it must not rebuild its nodes). */
  const voiceChipHost = add(rightCol, el('div', 'gg-voice-chiphost'));
  const receiptsHost = add(rightCol, el('div', 'gg-receipts'));
  const coolHost = add(rightCol, el('div', 'gg-cool'));
  if (coolHost) coolHost.hidden = true;

  /* ---- THE COLLAPSE BIT --------------------------------------------------
   * One class on one node; every hide, every slide and every layout consequence
   * hangs off it in ui/hud.css. Nothing about firing reads it — that is the
   * point of putting the keyboard on `window` in ui/arsenal.js — so a shut
   * drawer is a hidden control panel, never a disabled one.
   *
   * The starting state is resolveArsenalOpen(pref, narrow) in ui/prefs.js: open
   * on a desk, shut on a phone, until the player says otherwise ONCE, after
   * which their answer travels with them (see the pref's own block).
   */
  let sideOpen = resolveArsenalOpen(
    (prefs && typeof prefs.get === 'function') ? prefs.get('arsenalOpen') : undefined,
    isNarrowViewport(),
  );

  function paintSide() {
    cls(sideBox, ARSENAL_OPEN_CLASS, sideOpen);
    text(sideTabGlyph, sideOpen ? S.arsenal.sidebarHideGlyph : S.arsenal.sidebarShowGlyph);
    if (sideTab && sideTab.setAttribute) {
      try {
        const label = sideOpen ? S.arsenal.sidebarHide : S.arsenal.sidebarShow;
        sideTab.setAttribute('aria-expanded', sideOpen ? 'true' : 'false');
        sideTab.setAttribute('aria-label', label);
        sideTab.title = sideOpen ? label : S.arsenal.sidebarTitle;
      } catch (_e) { /* stub DOM */ }
    }
  }

  function setSide(on, remember) {
    const next = !!on;
    if (next === sideOpen) return;
    sideOpen = next;
    paintSide();
    // Persisting is a nicety, not the state: the bit above is already true.
    if (remember && prefs && typeof prefs.set === 'function') {
      try { prefs.set('arsenalOpen', sideOpen ? ARSENAL_OPEN_ON : ARSENAL_OPEN_OFF); }
      catch (_e) { /* no store, no problem */ }
    }
  }
  paintSide();
  led.listen(sideTab, 'click', () => { sfx(audio, 'ui-select'); setSide(!sideOpen, true); });

  /* A key press into a SHUT drawer has to land somewhere the eye can see, or the
   * arsenal reads as broken from behind its own handle. The count badge covers
   * "what do I have"; this covers "that did something". It is deliberately a
   * plain listener rather than a new ui/arsenal.js callback: it fires on the
   * gesture, not on the outcome, which is exactly what the player needs told
   * when the tile that would have shaken or lit up is not on screen. */
  let tabPulseTimer = 0;
  function pulseTab() {
    if (!sideTab) return;
    cls(sideTab, 'is-bump', true);
    try { clearTimeout(tabPulseTimer); } catch (_e) { /* gone */ }
    tabPulseTimer = setTimeout(() => cls(sideTab, 'is-bump', false), 320);
  }
  led.add(() => { try { clearTimeout(tabPulseTimer); } catch (_e) { /* gone */ } });
  led.listen(typeof window !== 'undefined' ? window : null, 'keydown', (e) => {
    if (sideOpen || !e || e.repeat || e.ctrlKey || e.altKey || e.metaKey) return;
    const active = d && d.activeElement;
    const tag = active && active.tagName ? String(active.tagName).toLowerCase() : '';
    if (tag === 'input' || tag === 'textarea' || (active && active.isContentEditable)) return;
    const n = parseInt(e.key, 10);
    if (n >= 1 && n <= ARSENAL_KEY_COUNT) pulseTab();
  });

  // ---------------------------------------------------------------- bottom
  const bottom = add(root, el('div', 'gg-hud-bottom'));
  const fxRail = add(bottom, el('div', 'gg-fxrail'));
  const dialHost = add(bottom, el('div', 'gg-dial-host'));

  // ------------------------------------------------------------ sub-mounts
  /* The media pool reaches the THROW PREVIEW here and nowhere else. It is a
   * module-level handoff rather than a mount argument because the two things
   * that need it — the inbound projectile (ui/opponent.js) and the arsenal's
   * drag ghost — are minted deep inside closures that take no media, and
   * threading one through the arsenal would be a real edit to a file this
   * feature has no other business in. `media` is already a mountHud option
   * (boot.js has always passed it); it was simply unused until now. */
  setPreviewMedia(media);
  led.add(() => setPreviewMedia(null));

  // `prefs` is threaded in only so the monitor can remember where it was dragged
  // to and how big it was wheeled (ui/opponent.js, THE LOOSE MONITOR). Optional
  // there: no store, no memory, everything else identical.
  const opponent = mountOpponent({ host: monHost, match, audio, fx, prefs });
  const emotes = mountEmotes({ host: root, match, audio, onLog });
  const arsenal = mountArsenal({
    leftHost: leftRail,
    rightHost: rightRail,
    receiptsHost,
    coolHost,
    match,
    audio,
    fx,
    getDropTarget: () => opponent.dropTarget,
    onTargeted: (on) => opponent.setTargeted && opponent.setTargeted(on),
    onEmote: () => emotes.toggle(),
    // The glue the owner asked for: what we FIRED drives their little screen,
    // without waiting for (or needing) their tick to mention it.
    onFired: (shot) => {
      // The FX beat first and unconditionally: it is about the ACT of firing,
      // which happened whether or not the monitor can draw the flight.
      emitAva('fire', 'you', shot && shot.kind !== undefined ? { kind: shot.kind } : null);
      if (!shot || typeof opponent.markPayloadFired !== 'function') return;
      opponent.markPayloadFired({
        id: shot.id,
        kind: shot.kind,
        durationMs: shot.durationMs,
        leadMs: PAYLOAD_LEAD_MS,
      });
    },
    onLog,
  });
  // ------------------------------------------------------- the drop economy
  // Popping a bubble is how the arsenal gets fed. exec/bubbles.js knows nothing
  // about any of this: it dispatches one event per pop on `document` and this is
  // the only listener. A pop while a drawer is open still rolls — the roll is
  // silent bookkeeping, only its flourish rides the animation budget.
  const drops = createDropRoller({ match, arsenal, audio, onLog });
  led.listen(d, BUBBLE_POP_EVENT, (e) => {
    try {
      const res = drops.onPop(e && e.detail);
      // Two beats, and they are different facts: `pop` is every pop (E throttles
      // it to four a second), `drop` is the rare one that armed a slot.
      emitAva('pop', 'you');
      if (res && res.dropped) emitAva('drop', 'you', res.id ? { item: res.id } : null);
    } catch (_e) { /* a drop must never break the desk */ }
  });

  const dial = mountCloseness({ host: dialHost, match, audio, onLog });
  const attention = mountAttention({
    host: attSlot,
    tokenHost: root,
    match,
    audio,
    /* `sideBox` rather than the two rails: it is the whole sidebar including the
     * handle, and it is the only one of the three whose rect is HONEST in both
     * states — a collapsed panel is display:none, so a rail measures 0x0 and an
     * attention token would happily land on the handle. */
    getAvoid: () => [sideBox, dialHost, monHost, d && d.getElementById ? d.getElementById('gg-mercy') : null].filter(Boolean),
    onLog,
  });

  // The ribbon that calls the shot before it lands. Absolutely placed inside the
  // frame (under the timer, clear of the monitor), so it takes no grid row.
  const announcer = mountAnnouncer({ host: root, match, audio, onLog });

  /* The mic. Mounted unconditionally and hidden by itself: whether a duel has
   * voice in it is five facts wide (both consents, the peer's build, the phase,
   * your own opt-in) and ui/voice/voiceService.js is the one place that knows —
   * the desk subscribes to the answer instead of re-deriving any of it. With no
   * service at all this is a no-op handle and no DOM. */
  const mic = mountMicHud({ host: voiceHost, chipHost: voiceChipHost, voice, audio, onLog });
  /* Zen is restored from prefs BEFORE this mount, so the handle is told the bit
   * it was not there to hear. setZen's own early-return means it would never be
   * re-sent, and a remembered zen with a visible mic under it is the one state
   * that would make the toggle look broken on the first click. */
  micRef = mic;
  try { mic.setHudHidden(zenOn); } catch (_e) { /* a no-op handle has no bit */ }

  led.add(() => { try { mic.unmount(); } catch (_e) { /* gone */ } });
  led.add(() => { try { announcer.unmount(); } catch (_e) { /* gone */ } });
  led.add(() => { try { attention.unmount(); } catch (_e) { /* gone */ } });
  led.add(() => { try { dial.unmount(); } catch (_e) { /* gone */ } });
  led.add(() => { try { arsenal.unmount(); } catch (_e) { /* gone */ } });
  led.add(() => { try { emotes.unmount(); } catch (_e) { /* gone */ } });
  led.add(() => { try { opponent.unmount(); } catch (_e) { /* gone */ } });

  /* -------------------------------------------- the opponent's mini + its DM
   * Rebuilt rather than mutated whenever the card or the name changes: a peer
   * card can land mid-match (it is fetched fire-and-forget) and a name can
   * arrive with the hello, and rebuilding is the only way the picture can
   * replace the tile without the box ever moving.
   *
   * The DM affordance exists ONLY when THEY shared DMs. There is no disabled
   * variant and no explanatory tooltip: a button that cannot work is worse than
   * no button, and the flag is theirs to set, not ours to editorialise. */
  function oppName() {
    const fromMatch = match.opponent && match.opponent.displayName;
    const fromCard = discord && discord.peer && discord.peer.name;
    return String(fromMatch || fromCard || S.lobby.them);
  }

  let lastOppKey = null;
  function paintOppAva() {
    if (!oppAvaBox) return;
    const card = discord ? discord.peer : null;
    const show = !discord || discord.showOpponentAvatars;
    const uri = (show && card) ? card.avatarDataUri : null;
    const dm = !!(show && card && card.dm);
    const name = oppName();
    const key = name + '|' + (uri ? uri.length : 0) + '|' + (dm ? 1 : 0);
    if (key === lastOppKey) return;
    lastOppKey = key;

    const node = mini('opp', name, uri);
    if (!node) return;
    const kids = [node];
    if (dm) {
      const b = el('button', 'gg-ava-dm', S.discord.dmShort);
      if (b) {
        b.type = 'button';
        try { b.setAttribute('aria-label', S.discord.messageOn(name)); b.title = S.discord.messageOn(name); }
        catch (_e) { /* stub DOM */ }
        led.listen(b, 'click', () => {
          sfx(audio, 'ui-select');
          try {
            if (d && typeof d.dispatchEvent === 'function' && typeof CustomEvent === 'function') {
              d.dispatchEvent(new CustomEvent(DISCORD_DM_EVENT, {
                bubbles: true,
                detail: { which: 'peer', name },
              }));
            }
          } catch (_e) { /* boot may not be listening; the desk is unaffected */ }
        });
        kids.push(b);
      }
    }
    try { oppAvaBox.replaceChildren(...kids); } catch (_e) { for (const k of kids) add(oppAvaBox, k); }
  }
  paintOppAva();
  if (discord && typeof discord.subscribe === 'function') led.add(discord.subscribe(paintOppAva));

  /* Your own bubble only ever changes when the host echoes a new `discord`
   * frame (you switched your picture on mid-lobby, say). Same repaint path. */
  function paintYouAva() {
    if (!youAvaBox) return;
    const st = discord ? discord.state : null;
    const uri = (discord && discord.sharingAvatar && st) ? st.avatarDataUri : null;
    try { if (youAva && typeof youAva.setPicture === 'function') youAva.setPicture(uri); }
    catch (_e) { /* a bubble that keeps its tile is not a failure */ }
  }
  paintYouAva();
  if (discord && typeof discord.subscribe === 'function') led.add(discord.subscribe(paintYouAva));

  // ---------------------------------------------------------- top painting

  let shownScore = 0;
  let targetScore = 0;
  let lerpFrom = 0;
  let lerpAt = 0;
  let lastCharges = -1;
  let lastTickSecond = -1;

  function durationMs() {
    try {
      const sheet = match.consentSheet;
      return sheet ? (sheet.live_duration_sec | 0) * 1000 : 0;
    } catch (_e) { return 0; }
  }

  function paintScore() {
    const s = match.scoring;
    const next = s ? (s.score | 0) : 0;
    if (next !== targetScore) {
      lerpFrom = shownScore;
      targetScore = next;
      lerpAt = nowMs();
    }
    const t = Math.min(1, (nowMs() - lerpAt) / SCORE_LERP_MS);
    shownScore = Math.round(lerpFrom + (targetScore - lerpFrom) * t);
    text(scoreEl, String(shownScore));
  }

  /* THE CHARGE COUNT. Memoised on `lastCharges` exactly as the pip row was, for
   * the same two reasons: the 5 Hz poll would otherwise rewrite an unchanged
   * string twenty times a second, and the "+1" flourish has to fire ONCE on the
   * edge rather than on every tick that finds the number high.
   *
   * `lastCharges >= 0` is the mount guard: the very first paint moves 0 -> N and
   * is not a gain the player did anything to earn, so it must not bang the sfx
   * on the way into a match that resumed with charges already banked. */
  function paintCharges() {
    const s = match.scoring;
    const c = s ? (s.charges | 0) : 0;
    if (c === lastCharges) return;
    const gained = c > lastCharges && lastCharges >= 0;
    lastCharges = c;
    text(chargeEl, S.hud.charges(c, GoonConsts.ChargeCap));
    if (!gained) return;
    sfx(audio, 'gg-charge');
    fx.play(400, () => {
      const chip = el('span', 'gg-plus1', '+1');
      if (!chip) return;
      add(scoreBox, chip);
      setTimeout(() => { try { chip.remove(); } catch (_e) { /* gone */ } }, 900);
    });
  }

  function paintTimer() {
    const total = durationMs();
    const phase = match.phase;
    const sd = phase === GoonMatchPhase.SuddenDeath;
    let remaining = 0;
    try { remaining = match.liveRemainingMs | 0; } catch (_e) { remaining = 0; }

    text(timerClock, sd ? 'sudden death' : mmss(remaining));
    cls(timerBox, 'is-sd', sd);
    const pct = total > 0 ? Math.max(0, Math.min(100, ((total - remaining) / total) * 100)) : 0;
    if (timerFill && timerFill.style) timerFill.style.width = pct.toFixed(2) + '%';

    const urgent = !sd && remaining > 0 && remaining < 60000;
    cls(timerBox, 'is-urgent', urgent);
    if (urgent) {
      const sec = Math.ceil(remaining / 1000);
      if (sec !== lastTickSecond) { lastTickSecond = sec; sfx(audio, 'gg-tick'); }
    } else {
      lastTickSecond = -1;
    }
  }

  /* THE HEAT GAUGE. Polled, not subscribed: heat decays on the wall clock with
   * no timer behind it (ui/drops.js keeps it lazy), so the only way the bar can
   * show a cool-down is for the painter to ask. paintAll already runs at 5 Hz
   * and the fill has its own CSS transition, so that reads as continuous.
   * A poll also cannot leak a subscription, which the suite counts. */
  function paintHeat() {
    if (!heatFill || !heatBox) return;
    const f = drops && typeof drops.heatFraction === 'number' ? drops.heatFraction : 0;
    const pct = Math.max(0, Math.min(100, f * 100));
    if (heatFill.style) heatFill.style.width = pct.toFixed(1) + '%';
    // The word travels with the colour, always: the gauge is never the only channel.
    cls(heatBox, 'is-hot', pct >= 66);
    if (heatBox.setAttribute) {
      try {
        heatBox.setAttribute('aria-valuenow', String(Math.round(pct)));
        heatBox.setAttribute('aria-valuetext', S.hud.heatValue(pct));
      } catch (_e) { /* stub DOM */ }
    }
  }

  /* THE HANDLE's COUNT BADGE. A shut drawer hides every ×N sticker badge at
   * once, so the handle carries their sum: "you are holding four things" is the
   * one fact a collapsed arsenal still has to tell, and it is the difference
   * between a tidy desk and a player who forgot they had items.
   *
   * Summed off arsenal.droppable(), which is already the honest set — a kind
   * outside either side's caps and a spent brain drain are both absent from it,
   * so the badge can never count a stack that cannot be fired. Painted on the
   * same 5 Hz poll as the heat gauge and memoised, because the number moves
   * perhaps twice a minute. */
  let paintedArmed = -1;
  function armedTotal() {
    try {
      const list = arsenal && typeof arsenal.droppable === 'function' ? arsenal.droppable() : null;
      if (!Array.isArray(list)) return 0;
      let n = 0;
      for (const c of list) n += (c && c.armed) | 0;
      return n;
    } catch (_e) { return 0; }
  }

  function paintArmedBadge() {
    if (!sideTabCount) return;
    const n = armedTotal();
    if (n === paintedArmed) return;
    const gained = n > paintedArmed && paintedArmed >= 0;
    paintedArmed = n;
    text(sideTabCount, n > 0 ? S.arsenal.tabCount(n) : '');
    sideTabCount.hidden = n <= 0;
    cls(sideTab, 'is-stocked', n > 0);
    if (sideTab && sideTab.setAttribute) {
      try { sideTab.setAttribute('data-gg-armed', String(n)); } catch (_e) { /* stub DOM */ }
    }
    if (sideTabCount.setAttribute) {
      try { sideTabCount.setAttribute('aria-label', S.arsenal.tabCountLabel(n)); } catch (_e) { /* stub DOM */ }
    }
    // A drop that lands behind a shut drawer still gets ONE beat of feedback.
    if (gained && !sideOpen) pulseTab();
  }

  function paintAll() {
    try { paintScore(); paintCharges(); paintTimer(); paintHeat(); paintArmedBadge(); }
    catch (_e) { /* a paint must never take the match down */ }
  }

  led.add(fx.onFrame(paintScore));
  led.interval(200, paintAll);
  paintAll();

  // ------------------------------------------------- own / incoming effects

  const chips = new Map();   // key -> {node, timer}

  function chipKey(kind, id) { return kind + ':' + id; }

  function addChip(key, meta, durationMs2, incoming) {
    if (chips.has(key)) { refreshChip(key, durationMs2); return; }
    const node = el('div', 'gg-fxchip' + (incoming ? ' is-incoming' : ''));
    if (!node) return;
    add(node, el('span', 'gg-fxchip-glyph', meta.glyph));
    add(node, el('span', 'gg-fxchip-name', meta.label));
    const bar = add(node, el('i', 'gg-fxchip-bar'));
    add(fxRail, node);
    const rec = { node, bar, timer: 0 };
    chips.set(key, rec);
    startBar(rec, durationMs2);
  }

  function startBar(rec, ms) {
    if (!rec.bar || !rec.bar.style) return;
    rec.bar.style.transition = 'none';
    rec.bar.style.width = '100%';
    const run = () => {
      if (!rec.bar || !rec.bar.style) return;
      rec.bar.style.transition = 'width ' + Math.max(200, ms | 0) + 'ms linear';
      rec.bar.style.width = '0%';
    };
    if (typeof requestAnimationFrame === 'function') requestAnimationFrame(run); else run();
    try { clearTimeout(rec.timer); } catch (_e) { /* gone */ }
    if (ms > 0) rec.timer = setTimeout(() => dropChipRec(rec), Math.max(200, ms | 0) + 200);
  }

  function refreshChip(key, ms) {
    const rec = chips.get(key);
    if (rec) startBar(rec, ms);
  }

  function dropChipRec(rec) {
    for (const [k, v] of chips) if (v === rec) chips.delete(k);
    try { clearTimeout(rec.timer); } catch (_e) { /* gone */ }
    try { rec.node.remove(); } catch (_e) { /* gone */ }
  }

  function dropChip(key) {
    const rec = chips.get(key);
    if (rec) dropChipRec(rec);
  }

  function sub(name, fn) {
    if (typeof match[name] !== 'function') return;
    const off = match[name](fn);
    led.add(typeof off === 'function' ? off : null);
  }

  sub('onElementStartRequested', (cue) => {
    const meta = ELEMENT_META[cue && cue.element];
    if (!meta) return;
    addChip(chipKey('e', cue.element), meta, cue.durationMs || 0, false);
    onLog({ t: 'element-start', element: meta.label });
  });
  sub('onElementIntensityChanged', (cue) => refreshChip(chipKey('e', cue && cue.element), (cue && cue.durationMs) || 0));
  sub('onElementStopRequested', (cue) => dropChip(chipKey('e', cue && cue.element)));

  sub('onPayloadAccepted', (e) => {
    const p = e && e.payload;
    const meta = p && PAYLOAD_META[p.kind];
    if (!meta) return;
    const wait = Math.max(0, (e.fireAtLocalMs || 0) - localMonotonicMs());
    // THE THROW. Their monitor flares and the thing they threw flies at us,
    // fitted INSIDE the lead the engine already reserved so it lands on the very
    // instant the effect starts. Nothing about the schedule, the receipts or the
    // cue below moves — see THE THROW in ui/opponent.js.
    if (typeof opponent.markInbound === 'function') {
      opponent.markInbound({ kind: p.kind, waitMs: wait, payload: p });
    }
    // ONE landing cue for every family. A per-kind sting would be eight sounds
    // to learn for information the fx chip already spells out in words.
    const t = setTimeout(() => {
      sfx(audio, 'payload-in');
      addChip(chipKey('p', p.id), meta, p.duration_ms || 0, true);
      // The FLINCH lands with the payload, not when it was accepted: the engine
      // schedules ahead (PAYLOAD_LEAD_MS) and a bubble that recoiled a second
      // and a half early would read as a glitch, not a hit.
      emitAva('land', 'you', { kind: p.kind });
    }, Math.min(wait, 30000));
    led.add(() => { try { clearTimeout(t); } catch (_e) { /* gone */ } });
    onLog({ t: 'payload-in', kind: meta.label, id: p.id });
  });

  // Receipts for what WE sent: 'survived' is the green flash + checkmark on the
  // monitor; every other terminal status just closes the window. (The arsenal
  // takes its own subscription for the receipt strip — these are independent.)
  sub('onPayloadReceiptReceived', (r) => {
    if (!r) return;
    // A receipt is the only proof anything reached THEIR screen — this is the
    // `land` beat for the other bubble, and the receipt/executor seam the
    // contract points at.
    emitAva('land', 'opp', { status: r.status });
    if (typeof opponent.markReceipt !== 'function') return;
    opponent.markReceipt(r.id, r.status);
  });

  // Their emote arriving is the mirror of ours going out (see onLog above).
  // `ms` is the mini's real dwell, so the wiggle ends when the bubble does
  // rather than on avatarFx's 2600ms fallback.
  sub('onEmoteReceived', (e) => {
    emitAva('emote', 'opp', { emote: (e && (e.text || e.icon)) || '', ms: EMOTE_MINI_MS });
  });

  // The hello carries their display name; the mini is built before it lands.
  sub('onOpponentStateChanged', () => { try { paintOppAva(); } catch (_e) { /* ignore */ } });

  // ---------------------------------------------------------------- phases

  sub('onPhaseChanged', (phase) => {
    cls(root, 'is-sd', phase === GoonMatchPhase.SuddenDeath);
    cls(root, 'is-over', phase === GoonMatchPhase.Recap);
    if (phase === GoonMatchPhase.Live) sfx(audio, 'gg-go');
    paintAll();
  });
  cls(root, 'is-sd', match.phase === GoonMatchPhase.SuddenDeath);

  // Name plate for our own side, when the shell gave us one.
  const who = session && session.identity && session.identity.displayName;
  if (who) {
    const me = el('div', 'gg-me', String(who));
    if (me) add(scoreBox, me);
  }
  if (prefs && prefs.reducedMotion) cls(root, 'is-calm', true);
  if (media) { /* the HUD does not draw media in v1; exec/ owns the pool */ }

  return {
    /** Exposed so H (or a play-test driver) can poke the desk without re-deriving it. */
    parts: {
      root, arsenal, opponent, dial, attention, emotes, announcer, fx, drops, mic,
      /** The heat gauge: the node the drop economy paints itself onto. */
      heat: { box: heatBox, fill: heatFill, get fraction() { return drops.heatFraction; } },
      /** The zen toggle, for a driver that wants the state without the click. */
      zen: { button: zenBtn, set: (on) => setZen(on, true), get on() { return zenOn; } },
      /** The collapsible arsenal sidebar — same shape, same reason as zen. */
      sidebar: {
        root: sideBox,
        panel: sidePanel,
        tab: sideTab,
        count: sideTabCount,
        set: (on) => setSide(on, true),
        get open() { return sideOpen; },
        get armed() { return armedTotal(); },
      },
    },
    unmount() {
      fx.stop();
      for (const rec of Array.from(chips.values())) dropChipRec(rec);
      led.run();
      try { root.remove(); } catch (_e) { /* gone */ }
      if (host) host.hidden = true;
    },
  };
}
