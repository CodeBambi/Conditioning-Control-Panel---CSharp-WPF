/* ============================================================================
 * ui/arsenal.js — the two item rails that flank the stage, and the drag-to-fire
 * gesture that turns an item into a payload aimed at the opponent's monitor.
 *
 * One tile per payload kind (plus the emote sheet opener). A tile is a BARE ITEM
 * IMAGE — a sticker cutout sitting on the desk, no plate, no border, no fill —
 * with its cost in tiny diamonds under it. It always says what state it is in
 * with a WORD, never colour alone:
 *
 *   locked     greyed sticker + "?" — you have not DROPPED one yet (see below)
 *   ready      full colour + a ×N stack badge; the priciest affordable one glows
 *   too poor   45% + amber pips; tap -> shake + "costs {n} — you have {m}"
 *   cooling    conic sweep + the shared "next payload in {n}s" readout
 *   used       brain drain only, once per match: struck through
 *   filtered   hatched, "they can't receive this" (kind outside the peer caps)
 *   hidden     the kind is outside OUR OWN caps — the slot never renders
 *
 * THE DROP ECONOMY (2026-08-03 owner redesign). Items are NOT simply "affordable
 * or not" any more. Every slot starts LOCKED and lights up only when a popped
 * bubble drops it: exec/bubbles.js dispatches `gg-bubble-pop`, ui/drops.js rolls
 * it, and a hit calls armDrop(id) here. Firing consumes ONE stack; at zero the
 * slot goes back to locked. Charges remain the WIRE truth (the receiver still
 * validates "sender charges >= cost"), so ui/drops.js credits the charge before
 * it arms — charges beyond your armed stacks are just headroom, and a charge
 * earned by SURVIVING a payload never arms anything on its own.
 *
 * The emote slot is social, costs nothing, and is therefore never locked and
 * never dropped (see needsArming()).
 *
 * FIRING, three ways, all of them arriving at match.tryFirePayload():
 *   1. drag the item onto the monitor (pointer capture + rect hit-test)
 *   2. tap the item, then tap the monitor (armed state; tapping elsewhere disarms)
 *   3. keys 1-8 (first press arms, second press fires)
 *
 * Node-import-safe: no DOM at import, only inside mountArsenal().
 * ==========================================================================*/

import { GoonConsts, GoonMatchPhase, GoonPayloadKind, costOf } from '../core/contracts.js';
import { GoonPayloadRateLimiter, GoonReceiptStatus } from '../core/scoring.js';
import { localMonotonicMs } from '../core/clock.js';
import { S } from './strings.js';

/**
 * The rails, in owner order — which is also the KEYBOARD order (1..8), so new
 * items go on the end and never renumber a finger that already knows the deck.
 * `durationMs` is the payload length we request; the engine clamps to
 * 1000..180000 so every one of these lands unchanged. `cost` is documentation
 * and a fallback only: costOf() is authority whenever it knows the kind.
 */
export const ARSENAL_ITEMS = Object.freeze([
  { id: 'flash', rail: 'left', kind: GoonPayloadKind.FlashBurst, img: 'item_flash', label: 'flash', durationMs: 6000, cost: 1 },
  { id: 'subliminal', rail: 'left', kind: GoonPayloadKind.SubliminalStorm, img: 'item_subliminal', label: 'subliminal', durationMs: 8000, cost: 1 },
  { id: 'bubbles', rail: 'left', kind: GoonPayloadKind.BubbleSwarm, img: 'item_bubbles', label: 'bubbles', durationMs: 20000, cost: 1 },
  { id: 'video', rail: 'left', kind: GoonPayloadKind.Video, img: 'item_video', label: 'video', durationMs: 45000, cost: 2 },
  { id: 'lockcard', rail: 'right', kind: GoonPayloadKind.LockCard, img: 'item_lockcard', label: 'lock card', durationMs: 30000, cost: 2 },
  { id: 'toy', rail: 'right', kind: GoonPayloadKind.ToyPattern, img: 'item_toy', label: 'toy', durationMs: 10000, cost: 2 },
  { id: 'braindrain', rail: 'right', kind: GoonPayloadKind.BrainDrain, img: 'item_braindrain', label: 'brain drain', durationMs: 60000, cost: 3 },
  { id: 'spiral', rail: 'left', kind: GoonPayloadKind.Spiral, img: 'item_spiral', label: 'spiral', durationMs: 40000, cost: 2 },
  { id: 'emote', rail: 'right', kind: null, img: 'item_emote', label: 'emote', durationMs: 0, cost: 0 },
]);

/** Payload slots, i.e. everything the number keys can reach. */
const PAYLOAD_ITEMS = ARSENAL_ITEMS.filter((i) => i.kind !== null);

/**
 * Does this slot have to be EARNED before it can fire?
 *
 * A free slot (the emote sheet: no kind, no cost) never locks and never drops —
 * chatting at someone is social, not economy. The rule is written against the
 * COST rather than against the id, so the day emote grows a price it joins the
 * drop pool with everything else and nothing here needs editing.
 *
 * @param {object} item ARSENAL_ITEMS entry
 * @param {number} cost resolved cost (costOf is authority; see buildTile)
 */
export function needsArming(item, cost) {
  return !!item && item.kind !== null && (cost | 0) > 0;
}

/** How many of an item a single drop hands you. */
const DROP_STACK = 1;
/** The drop flourish's window (must not leave a permanent animation behind). */
const DROP_FLASH_MS = 520;

/** Every payload we send goes out at this intensity in v1 (no per-item dial yet). */
const FIRE_INTENSITY = 0.7;

/** Pointer travel that turns a tap into a drag. */
const DRAG_SLOP_PX = 6;

const RECEIPT_KEEP = 3;
const RECEIPT_MS = 8000;
const TIP_MS = 1000;

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

function rectOf(node) {
  try {
    if (node && typeof node.getBoundingClientRect === 'function') {
      const r = node.getBoundingClientRect();
      if (r && typeof r.left === 'number') return r;
    }
  } catch (_e) { /* stub DOM */ }
  return { left: 0, top: 0, right: 0, bottom: 0, width: 0, height: 0 };
}

function inRect(r, x, y, pad = 0) {
  return x >= r.left - pad && x <= r.right + pad && y >= r.top - pad && y <= r.bottom + pad;
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

/**
 * Milliseconds until the next payload token. The engine owns the authoritative
 * limiter but does not publish it, so: use it when it is reachable, otherwise
 * fall back to a local mirror fed by our own successful fires (same class, same
 * constants, so the readout is right either way).
 */
function makeCooldownProbe(match) {
  const mirror = new GoonPayloadRateLimiter();
  return {
    onFired() { try { mirror.tryAdmit(localMonotonicMs()); } catch (_e) { /* ignore */ } },
    msLeft() {
      try {
        if (match && typeof match.msUntilNextPayloadMs === 'function') return match.msUntilNextPayloadMs() | 0;
        const lim = match && match._outboundLimiter;
        if (lim && typeof lim.msUntilNextToken === 'function') return lim.msUntilNextToken(localMonotonicMs()) | 0;
      } catch (_e) { /* fall through to the mirror */ }
      try { return mirror.msUntilNextToken(localMonotonicMs()) | 0; } catch (_e) { return 0; }
    },
  };
}

function receiptWord(status) {
  switch (status) {
    case GoonReceiptStatus.Accepted: return { word: 'landed', tone: 'ok' };
    case GoonReceiptStatus.Completed: return { word: 'landed', tone: 'ok' };
    case GoonReceiptStatus.Survived: return { word: 'endured', tone: 'gold', note: '+1 charge for them' };
    case GoonReceiptStatus.RejectedRate: return { word: 'too soon', tone: 'warn' };
    case GoonReceiptStatus.RejectedFiltered: return { word: 'blocked', tone: 'warn' };
    default: return { word: String(status || 'sent'), tone: 'dim' };
  }
}

// ------------------------------------------------------------------ mount

/**
 * @param {object}   o
 * @param {Element}  o.leftHost        left rail container
 * @param {Element}  o.rightHost       right rail container
 * @param {Element}  [o.receiptsHost]  strip under the monitor
 * @param {Element}  [o.coolHost]      shared "next payload in ..." readout
 * @param {object}   o.match           GoonMatchService
 * @param {object}   [o.audio]
 * @param {object}   [o.fx]            chrome-animation budget from ui/hud.js
 * @param {Function} [o.getDropTarget] () => Element (the opponent monitor)
 * @param {Function} [o.onTargeted]    (bool) => void — monitor highlight
 * @param {Function} [o.onEmote]       () => void — opens ui/emotes.js
 * @param {Function} [o.onFired]       ({id,kind,durationMs,label}) => void — a
 *                                     fire the engine ACCEPTED. ui/hud.js hands
 *                                     it to the monitor so the miniature plays
 *                                     for the payload's own window.
 * @param {Function} [o.onLog]         (entry) => void — match log
 */
export function mountArsenal({
  leftHost, rightHost, receiptsHost = null, coolHost = null,
  match, audio = null, fx = null,
  getDropTarget = null, onTargeted = null, onEmote = null, onFired = null, onLog = null,
} = {}) {
  const led = createLedger();
  const cool = makeCooldownProbe(match);
  const tiles = new Map();      // id -> tile record
  const receipts = [];          // {id, row, timer}
  let armed = null;             // tile record
  let heavyUsed = false;
  let heavyId = null;

  const ourCaps = (match && match.localCaps && match.localCaps.payloads) || null;

  for (const item of ARSENAL_ITEMS) {
    // A kind OUR client cannot run never gets a slot at all.
    if (item.kind !== null && ourCaps && !ourCaps.includes(item.kind)) continue;
    const host = item.rail === 'left' ? leftHost : rightHost;
    const tile = buildTile(item, host);
    if (tile) tiles.set(item.id, tile);
  }

  function buildTile(item, host) {
    if (!host) return null;
    // costOf is authority; the item's own `cost` only covers a kind the shared
    // contract has not landed yet (it returns Infinity for those, which would
    // otherwise pin the tile at "too poor" forever).
    const known = item.kind === null ? 0 : costOf(item.kind);
    const cost = item.kind === null ? 0 : (Number.isFinite(known) ? known : (item.cost | 0));
    // NO PLATE: the item art IS the button. Chrome would fight the sticker cutouts.
    const root = el('button', 'gg-item');
    if (!root) return null;
    root.type = 'button';
    root.setAttribute && root.setAttribute('data-gg-item', item.id);

    const img = add(root, el('img', 'gg-item-img'));
    if (img) {
      img.src = './assets/items/' + item.img + '.png';
      img.alt = '';
      img.draggable = false;
      img.decoding = 'async';
      // A missing PNG must never show a broken-image glyph: the tile falls back
      // to a drawn-in-CSS stand-in (spiral gets a real disc, the rest a mark).
      led.listen(img, 'error', () => cls(root, 'is-noart', true));
    }
    const fallbackArt = add(root, el('i', 'gg-item-fallback'));
    if (fallbackArt) fallbackArt.setAttribute && fallbackArt.setAttribute('aria-hidden', 'true');
    const nameEl = add(root, el('span', 'gg-item-name', item.label));
    const pipRow = add(root, el('span', 'gg-item-cost'));
    const costPips = [];
    for (let i = 0; i < cost; i++) costPips.push(add(pipRow, el('i', 'gg-cost-pip')));
    const stateEl = add(root, el('span', 'gg-item-state'));
    const sweep = add(root, el('i', 'gg-item-sweep'));
    // The economy chrome: a "?" while the slot is locked, a ×N badge once it is
    // armed. Both ride the sticker's alpha — still no plate anywhere.
    const lockEl = add(root, el('i', 'gg-item-lock', S.arsenal.lockGlyph));
    if (lockEl) lockEl.setAttribute && lockEl.setAttribute('aria-hidden', 'true');
    const stackEl = add(root, el('span', 'gg-item-stack'));
    if (stackEl) stackEl.hidden = true;
    const tip = add(root, el('span', 'gg-item-tip'));
    if (tip) tip.hidden = true;

    add(host, root);
    const rec = {
      item, cost, root, img, fallbackArt, nameEl, pipRow, costPips, stateEl, sweep,
      lockEl, stackEl, tip,
      state: 'ready', tipTimer: 0,
      needsArm: needsArming(item, cost),
      armed: 0,
      paintedStack: -1,     // memo for paintStack (see there)
    };
    wireTile(rec);
    return rec;
  }

  // ------------------------------------------------------------- state pass

  function charges() {
    const s = match && match.scoring;
    return s ? (s.charges | 0) : 0;
  }

  function peerCanTake(kind) {
    const list = match && match.availablePayloadKinds;
    return !Array.isArray(list) || list.includes(kind);
  }

  function isLive() {
    // Fire is only legal in Live/SuddenDeath; the engine says so too, this just
    // keeps the rails honest about it.
    const p = match && match.phase;
    return p === GoonMatchPhase.Live || p === GoonMatchPhase.SuddenDeath;
  }

  function isSpent(rec) {
    return rec.item.kind === GoonPayloadKind.BrainDrain && heavyUsed;
  }

  function paint() {
    const have = charges();
    const coolMs = cool.msLeft();
    const cooling = coolMs > 0;

    // The priciest ARMED + affordable tile earns the glow — "you could spend
    // this". A locked tile never glows: there is nothing there to spend yet.
    let glowId = null;
    let glowCost = -1;
    for (const rec of tiles.values()) {
      if (rec.item.kind === null) continue;
      if (rec.needsArm && rec.armed <= 0) continue;
      if (rec.cost <= have && rec.cost > glowCost && peerCanTake(rec.item.kind)) { glowCost = rec.cost; glowId = rec.item.id; }
    }

    for (const rec of tiles.values()) {
      const item = rec.item;
      if (!rec.needsArm) {
        // Free/social slot (the emote sheet): always available, never a drop.
        setState(rec, 'ready', '');
        cls(rec.root, 'is-glow', false);
        paintStack(rec);
        continue;
      }
      let state = 'ready';
      let word = '';
      if (isSpent(rec)) { state = 'used'; word = 'used'; }
      else if (!peerCanTake(item.kind)) { state = 'filtered'; word = "they can't receive this"; }
      else if (rec.armed <= 0) { state = 'locked'; word = S.arsenal.locked; }
      else if (rec.cost > have) { state = 'poor'; word = 'need ' + rec.cost; }
      else if (cooling) { state = 'cooling'; word = 'cooling'; }

      setState(rec, state, word);
      paintStack(rec);
      cls(rec.root, 'is-glow', state === 'ready' && item.id === glowId);
      for (const pip of rec.costPips) cls(pip, 'is-short', state === 'poor');
      if (rec.sweep && rec.sweep.style) {
        const pct = cooling ? Math.max(0, Math.min(100, 100 - (coolMs / GoonConsts.PayloadMinGapMs) * 100)) : 100;
        rec.sweep.style.setProperty && rec.sweep.style.setProperty('--gg-sweep', pct.toFixed(1) + '%');
      }
    }

    if (coolHost) {
      coolHost.hidden = !cooling;
      if (cooling) text(coolHost, 'next payload in ' + Math.ceil(coolMs / 1000) + 's');
    }
    if (armed && (armed.state !== 'ready' || !isLive())) disarm();
  }

  function setState(rec, state, word) {
    if (rec.state !== state) {
      for (const s of ['ready', 'poor', 'cooling', 'used', 'filtered', 'locked']) cls(rec.root, 'is-' + s, s === state);
      rec.state = state;
    }
    text(rec.stateEl, word);
    if (rec.root) rec.root.disabled = false;   // never trap the pointer: taps still explain themselves
  }

  /**
   * The ×N badge (armed) / the "?" (locked). Words, never colour alone.
   * Memoised: paint() runs on a 250 ms interval and the stack almost never
   * moves, so re-writing nine textContents four times a second would be pure
   * churn on a page that is already carrying an effect stack.
   */
  function paintStack(rec) {
    const showStack = rec.needsArm && rec.armed > 0;
    if (rec.paintedStack === rec.armed) return;
    rec.paintedStack = rec.armed;
    if (rec.stackEl) {
      text(rec.stackEl, showStack ? S.arsenal.stack(rec.armed) : '');
      rec.stackEl.hidden = !showStack;
    }
    if (rec.lockEl) rec.lockEl.hidden = !(rec.needsArm && rec.armed <= 0);
  }

  // ------------------------------------------------------------- firing

  function tipOn(rec, message) {
    if (!rec.tip) return;
    text(rec.tip, message);
    rec.tip.hidden = false;
    try { clearTimeout(rec.tipTimer); } catch (_e) { /* gone */ }
    rec.tipTimer = setTimeout(() => { rec.tip.hidden = true; }, TIP_MS);
    led.add(() => { try { clearTimeout(rec.tipTimer); } catch (_e) { /* gone */ } });
  }

  function shake(rec) {
    const run = () => {
      cls(rec.root, 'is-shake', true);
      setTimeout(() => cls(rec.root, 'is-shake', false), 140);
    };
    if (fx && typeof fx.play === 'function') fx.play(140, run); else run();
  }

  function refuse(rec, message) {
    shake(rec);
    tipOn(rec, message);
  }

  /** The one path to the engine. Returns the engine's {ok,error,id}. */
  function fire(rec) {
    if (!rec || rec.item.kind === null) return { ok: false, error: 'not a payload', id: null };
    if (!match || typeof match.tryFirePayload !== 'function') return { ok: false, error: 'no match', id: null };

    // LOCKED outranks every other refusal: nothing else about the tile matters
    // until a bubble has dropped one.
    if (rec.needsArm && rec.armed <= 0) { refuse(rec, S.arsenal.lockedTip); return { ok: false, error: 'locked', id: null }; }
    if (rec.state === 'poor') { refuse(rec, 'costs ' + rec.cost + ' — you have ' + charges()); return { ok: false, error: 'charges', id: null }; }
    if (rec.state === 'filtered') { refuse(rec, "they can't receive this"); return { ok: false, error: 'filtered', id: null }; }
    if (rec.state === 'used') { refuse(rec, 'one brain drain a match'); return { ok: false, error: 'used', id: null }; }
    if (rec.state === 'cooling') { refuse(rec, 'next payload in ' + Math.ceil(cool.msLeft() / 1000) + 's'); return { ok: false, error: 'cooling', id: null }; }

    const res = match.tryFirePayload({
      kind: rec.item.kind,
      durationMs: rec.item.durationMs,
      intensity: FIRE_INTENSITY,
    }) || { ok: false, error: 'no answer', id: null };

    if (res.ok) {
      cool.onFired();
      sfx(audio, 'gg-fire');
      // One stack per shot. At zero the slot goes straight back to locked and
      // only another drop can open it again.
      if (rec.needsArm && rec.armed > 0) rec.armed--;
      if (rec.item.kind === GoonPayloadKind.BrainDrain) { heavyUsed = true; heavyId = res.id; }
      spark(rec);
      addReceipt(res.id, rec.item.label);
      if (typeof onFired === 'function') {
        try {
          onFired({ id: res.id, kind: rec.item.kind, durationMs: rec.item.durationMs, label: rec.item.label });
        } catch (_e) { /* the monitor is never load-bearing for a fire */ }
      }
      if (typeof onLog === 'function') { try { onLog({ t: 'payload-out', kind: rec.item.label, id: res.id }); } catch (_e) { /* ignore */ } }
    } else {
      refuse(rec, String(res.error || 'not now'));
    }
    paint();
    return res;
  }

  /** A little pip of light thrown from the tile at the monitor. */
  function spark(rec) {
    const d = doc();
    const target = typeof getDropTarget === 'function' ? getDropTarget() : null;
    if (!d || !d.body || !target || !rec.root) return;
    const from = rectOf(rec.root);
    const to = rectOf(target);
    if (!to.width) return;
    const node = el('i', 'gg-spark');
    if (!node || !node.style) return;
    node.style.left = (from.left + from.width / 2) + 'px';
    node.style.top = (from.top + from.height / 2) + 'px';
    add(d.body, node);
    const dx = (to.left + to.width / 2) - (from.left + from.width / 2);
    const dy = (to.top + to.height / 2) - (from.top + from.height / 2);
    const run = () => {
      if (typeof requestAnimationFrame === 'function') {
        requestAnimationFrame(() => {
          if (node.style) node.style.transform = 'translate(' + dx + 'px,' + dy + 'px) scale(0.4)';
          if (node.style) node.style.opacity = '0';
        });
      }
      setTimeout(() => { try { node.remove(); } catch (_e) { /* gone */ } }, 420);
    };
    if (fx && typeof fx.play === 'function') fx.play(300, run); else run();
  }

  // ------------------------------------------------------------- drops

  /**
   * Every slot a bubble drop may legally land on, i.e. what ui/drops.js rolls
   * over. Kinds outside OUR caps never got a tile at all (see the mount loop:
   * ToyPattern is absent whenever caps.haptics is false), kinds outside THEIR
   * caps are dropped here, and a spent heavy is dropped here too — arming an
   * item that can never be fired is a dead drop.
   *
   * @returns {Array<{id:string, kind:number, cost:number, armed:number}>}
   */
  function droppable() {
    const out = [];
    for (const rec of tiles.values()) {
      if (!rec.needsArm) continue;
      if (isSpent(rec)) continue;
      if (!peerCanTake(rec.item.kind)) continue;
      out.push({ id: rec.item.id, kind: rec.item.kind, cost: rec.cost, armed: rec.armed });
    }
    return out;
  }

  /**
   * Arm one (or `count`) of an item. THE only way a slot leaves `locked`.
   * ui/drops.js calls this AFTER match.creditCharges() said yes, so the wire
   * truth (charges) and the local truth (stacks) can never disagree.
   *
   * @param {string} id ARSENAL_ITEMS id
   * @param {{count?:number, from?:{x:number,y:number}|null, silent?:boolean}} [o]
   * @returns {boolean} true when the slot took it
   */
  function armDrop(id, { count = DROP_STACK, from = null, silent = false } = {}) {
    const rec = tiles.get(id);
    if (!rec || !rec.needsArm) return false;
    rec.armed += Math.max(1, count | 0);
    paint();
    if (!silent) dropFlourish(rec, from);
    return true;
  }

  /** How many shots of an item are banked. */
  function armedCount(id) {
    const rec = tiles.get(id);
    return rec ? (rec.armed | 0) : 0;
  }

  /** Current tile state word ('locked' | 'ready' | 'poor' | ...), for tests/HUD. */
  function stateOf(id) {
    const rec = tiles.get(id);
    return rec ? rec.state : null;
  }

  /**
   * A drop landed: a spark flies from the pop to the slot and the sticker gives
   * one short pulse. Cheap, brief, and it leaves NO running animation behind —
   * both nodes are removed on a timer whether or not a frame ever ran. A drop
   * while the HUD is hot (or the drawer is open) still arms; only the flourish
   * is skipped by the animation budget.
   */
  function dropFlourish(rec, from) {
    tipOn(rec, S.arsenal.drop(rec.item.label));
    const d = doc();
    if (!d || !d.body || !rec.root) return;
    const to = rectOf(rec.root);
    const run = () => {
      cls(rec.root, 'is-dropped', true);
      setTimeout(() => cls(rec.root, 'is-dropped', false), DROP_FLASH_MS);
      if (!from || typeof from.x !== 'number' || typeof from.y !== 'number' || !to.width) return;
      const node = el('i', 'gg-drop-fly');
      if (!node || !node.style) return;
      node.style.left = from.x + 'px';
      node.style.top = from.y + 'px';
      add(d.body, node);
      const dx = (to.left + to.width / 2) - from.x;
      const dy = (to.top + to.height / 2) - from.y;
      const fly = () => {
        if (node.style) {
          node.style.transform = 'translate(' + dx + 'px,' + dy + 'px) scale(0.35)';
          node.style.opacity = '0';
        }
      };
      if (typeof requestAnimationFrame === 'function') requestAnimationFrame(fly); else fly();
      setTimeout(() => { try { node.remove(); } catch (_e) { /* gone */ } }, DROP_FLASH_MS + 120);
    };
    if (fx && typeof fx.play === 'function') fx.play(DROP_FLASH_MS, run); else run();
  }

  // ------------------------------------------------------------- receipts

  function addReceipt(id, label) {
    if (!receiptsHost || !id) return;
    const row = el('div', 'gg-receipt');
    if (!row) return;
    add(row, el('span', 'gg-receipt-kind', label));
    const st = add(row, el('span', 'gg-receipt-state', 'sent'));
    const note = add(row, el('span', 'gg-receipt-note'));
    add(receiptsHost, row);
    const entry = { id, row, st, note, timer: 0 };
    entry.timer = setTimeout(() => dropReceipt(entry), RECEIPT_MS);
    receipts.push(entry);
    while (receipts.length > RECEIPT_KEEP) dropReceipt(receipts[0]);
    led.add(() => { try { clearTimeout(entry.timer); } catch (_e) { /* gone */ } });
  }

  function dropReceipt(entry) {
    const at = receipts.indexOf(entry);
    if (at >= 0) receipts.splice(at, 1);
    try { clearTimeout(entry.timer); } catch (_e) { /* gone */ }
    try { entry.row.remove(); } catch (_e) { /* gone */ }
  }

  function onReceipt(receipt) {
    if (!receipt) return;
    const status = receipt.status;
    // A rejected heavy hands the once-per-match slot back (the engine does the same).
    if (heavyId && receipt.id === heavyId && typeof status === 'string' && status.indexOf('rejected') === 0) {
      heavyUsed = false;
      heavyId = null;
    }
    if (status === GoonReceiptStatus.Survived) sfx(audio, 'gg-endured');
    const entry = receipts.find((r) => r.id === receipt.id);
    if (!entry) { paint(); return; }
    const w = receiptWord(status);
    text(entry.st, w.word);
    text(entry.note, w.note || '');
    cls(entry.row, 'is-' + w.tone, true);
    if (typeof onLog === 'function') { try { onLog({ t: 'payload-receipt', id: receipt.id, status }); } catch (_e) { /* ignore */ } }
    paint();
  }

  // ------------------------------------------------------------- gestures

  function arm(rec) {
    if (armed === rec) { disarm(); return; }
    disarm();
    armed = rec;
    cls(rec.root, 'is-armed', true);
    if (typeof onTargeted === 'function') onTargeted(true);
  }

  function disarm() {
    if (!armed) return;
    cls(armed.root, 'is-armed', false);
    armed = null;
    if (typeof onTargeted === 'function') onTargeted(false);
  }

  function wireTile(rec) {
    const root = rec.root;
    if (!root) return;

    let dragging = false;
    let ghost = null;
    let startX = 0;
    let startY = 0;
    let pid = null;

    function makeGhost(x, y) {
      const d = doc();
      if (!d || !d.body || !rec.img) return;
      const r = rectOf(rec.img);
      ghost = el('img', 'gg-item-ghost');
      if (!ghost) return;
      ghost.src = rec.img.src;
      ghost.alt = '';
      if (ghost.style) {
        ghost.style.width = (r.width || 64) + 'px';
        ghost.style.height = (r.height || 64) + 'px';
      }
      add(d.body, ghost);
      moveGhost(x, y);
      cls(root, 'is-dragging', true);
      if (typeof onTargeted === 'function') onTargeted(true);
    }

    function moveGhost(x, y) {
      if (!ghost || !ghost.style) return;
      const r = rectOf(ghost);
      ghost.style.left = (x - (r.width || 64) / 2) + 'px';
      ghost.style.top = (y - (r.height || 64) / 2) + 'px';
    }

    function killGhost() {
      if (ghost) { try { ghost.remove(); } catch (_e) { /* gone */ } ghost = null; }
      cls(root, 'is-dragging', false);
    }

    led.listen(root, 'pointerdown', (e) => {
      if (rec.item.kind === null) { if (typeof onEmote === 'function') onEmote(); sfx(audio, 'gg-emote'); return; }
      // A LOCKED slot is not draggable and not armable: the gesture ends here
      // with a word, so no ghost is ever minted for an item you do not have.
      if (rec.needsArm && rec.armed <= 0) { refuse(rec, S.arsenal.lockedTip); return; }
      pid = e && e.pointerId;
      startX = (e && e.clientX) || 0;
      startY = (e && e.clientY) || 0;
      dragging = false;
      try { if (pid != null && typeof root.setPointerCapture === 'function') root.setPointerCapture(pid); } catch (_e) { /* not captured */ }
    });

    led.listen(root, 'pointermove', (e) => {
      if (pid == null) return;
      const x = (e && e.clientX) || 0;
      const y = (e && e.clientY) || 0;
      if (!dragging) {
        if (Math.abs(x - startX) < DRAG_SLOP_PX && Math.abs(y - startY) < DRAG_SLOP_PX) return;
        dragging = true;
        makeGhost(x, y);
      } else {
        moveGhost(x, y);
      }
    });

    function endPointer(e) {
      if (pid == null) return;
      const x = (e && e.clientX) || startX;
      const y = (e && e.clientY) || startY;
      try { if (typeof root.releasePointerCapture === 'function') root.releasePointerCapture(pid); } catch (_e) { /* gone */ }
      pid = null;

      if (!dragging) {
        // a tap: arm (or refuse loudly, which is its own kind of feedback)
        if (rec.state !== 'ready') fire(rec);
        else arm(rec);
        return;
      }
      dragging = false;
      killGhost();
      const target = typeof getDropTarget === 'function' ? getDropTarget() : null;
      const hit = target && inRect(rectOf(target), x, y, 12);
      if (typeof onTargeted === 'function') onTargeted(false);
      if (hit) fire(rec);
      else tipOn(rec, 'drop it on their monitor');
    }

    led.listen(root, 'pointerup', endPointer);
    led.listen(root, 'pointercancel', () => { pid = null; dragging = false; killGhost(); if (typeof onTargeted === 'function') onTargeted(false); });
    led.add(killGhost);
  }

  // tap-the-monitor (or anywhere) while armed
  const d = doc();
  led.listen(d, 'pointerdown', (e) => {
    if (!armed) return;
    const rec = armed;
    if (rec.root && typeof rec.root.contains === 'function' && e && rec.root.contains(e.target)) return;
    const target = typeof getDropTarget === 'function' ? getDropTarget() : null;
    const x = (e && e.clientX) || 0;
    const y = (e && e.clientY) || 0;
    if (target && inRect(rectOf(target), x, y, 12)) { disarm(); fire(rec); }
    else disarm();
  }, true);

  // keys 1-8 (one per payload slot) — first press arms, second fires
  led.listen(typeof window !== 'undefined' ? window : null, 'keydown', (e) => {
    if (!e || e.repeat || e.ctrlKey || e.altKey || e.metaKey) return;
    const active = d && d.activeElement;
    const tag = active && active.tagName ? String(active.tagName).toLowerCase() : '';
    if (tag === 'input' || tag === 'textarea' || (active && active.isContentEditable)) return;
    const n = parseInt(e.key, 10);
    if (!(n >= 1 && n <= PAYLOAD_ITEMS.length)) return;
    const item = PAYLOAD_ITEMS[n - 1];
    const rec = item && tiles.get(item.id);
    if (!rec) return;
    if (armed === rec) { disarm(); fire(rec); }
    else if (rec.state !== 'ready') fire(rec);
    else arm(rec);
  });

  // ------------------------------------------------------------- wiring

  function sub(name, fn) {
    if (!match || typeof match[name] !== 'function') return;
    const off = match[name](fn);
    led.add(typeof off === 'function' ? off : null);
  }
  sub('onPayloadReceiptReceived', onReceipt);
  sub('onPhaseChanged', () => paint());
  if (match && match.scoring && typeof match.scoring.onChargesChanged === 'function') {
    const off = match.scoring.onChargesChanged(() => paint());
    led.add(typeof off === 'function' ? off : null);
  }
  led.interval(250, () => { try { paint(); } catch (_e) { /* never break the HUD */ } });
  paint();

  return {
    items: ARSENAL_ITEMS,
    /** Test/keyboard seam: fire by item id, straight through the same gate. */
    fire(id) { return fire(tiles.get(id)); },
    arm(id) { const rec = tiles.get(id); if (rec) arm(rec); },
    disarm,
    paint,
    /* --- the drop economy (ui/drops.js is the only caller in production) --- */
    droppable,
    armDrop,
    armedCount,
    stateOf,
    /** True while the slot has nothing banked (and therefore refuses to fire). */
    isLocked(id) {
      const rec = tiles.get(id);
      return !!rec && rec.needsArm && rec.armed <= 0;
    },
    unmount() {
      disarm();
      led.run();
      for (const rec of tiles.values()) { try { rec.root.remove(); } catch (_e) { /* gone */ } }
      tiles.clear();
      while (receipts.length) dropReceipt(receipts[0]);
    },
  };
}
