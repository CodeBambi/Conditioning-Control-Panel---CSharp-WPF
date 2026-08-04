/* ============================================================================
 * ui/hud.js — the duel desk.
 *
 * Composes the live-match HUD inside #gg-hud and owns the layout grid:
 *
 *   ┌ top ────────────────────────────────────────────────────────────────┐
 *   │ score · charges · risk        11:47 ▮▮▯▯▯        attention   ⚙      │
 *   ├ body ───────────────────────────────────────────────────────────────┤
 *   │ [rail]              (the stage well — never covered)      [monitor] │
 *   │  4 items                                                  [receipts]│
 *   │                                                           [rail 4]  │
 *   ├ bottom ─────────────────────────────────────────────────────────────┤
 *   │ own effects                                     you're telling them │
 *   └──────────────────────── MERCY (its own layer, ui/mercy.js) ─────────┘
 *
 * THE MIDDLE IS SACRED. The HUD frames #gg-stage, it never lands on top of it:
 * everything here is edge-anchored and the centre column is pointer-transparent.
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
import { mountArsenal } from './arsenal.js';
import { mountCloseness } from './closeness.js';
import { mountAttention } from './attention.js';
import { mountEmotes } from './emotes.js';
import { mountAnnouncer } from './announcer.js';
import { createDropRoller } from './drops.js';
import { avatarNode, emitAva } from './avatar.js';
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
 * @returns {{unmount:Function}}
 */
export function mountHud({ match, session = null, audio = null, prefs = null, media = null,
  matchLog = null, discord = null } = {}) {
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
  const pipRow = add(scoreBox, el('div', 'gg-pips'));
  const pips = [];
  for (let i = 0; i < GoonConsts.ChargeCap; i++) pips.push(add(pipRow, el('i', 'gg-pip')));
  const riskEl = add(scoreBox, el('div', 'gg-risk', '×1.00 risk'));

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
  const leftRail = add(body, el('div', 'gg-rail gg-rail--left'));
  add(body, el('div', 'gg-well'));                         // the stage window: never covered
  const rightCol = add(body, el('div', 'gg-rightcol'));
  const oppAvaBox = add(rightCol, el('div', 'gg-ava-minibox gg-ava-minibox--opp'));
  const monHost = add(rightCol, el('div', 'gg-mon-host'));
  const receiptsHost = add(rightCol, el('div', 'gg-receipts'));
  const coolHost = add(rightCol, el('div', 'gg-cool'));
  if (coolHost) coolHost.hidden = true;
  const rightRail = add(rightCol, el('div', 'gg-rail gg-rail--right'));

  // ---------------------------------------------------------------- bottom
  const bottom = add(root, el('div', 'gg-hud-bottom'));
  const fxRail = add(bottom, el('div', 'gg-fxrail'));
  const dialHost = add(bottom, el('div', 'gg-dial-host'));

  // ------------------------------------------------------------ sub-mounts
  const opponent = mountOpponent({ host: monHost, match, audio, fx });
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
    getAvoid: () => [leftRail, rightRail, dialHost, monHost, d && d.getElementById ? d.getElementById('gg-mercy') : null].filter(Boolean),
    onLog,
  });

  // The ribbon that calls the shot before it lands. Absolutely placed inside the
  // frame (under the timer, clear of the monitor), so it takes no grid row.
  const announcer = mountAnnouncer({ host: root, match, audio, onLog });

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

  function paintCharges() {
    const s = match.scoring;
    const c = s ? (s.charges | 0) : 0;
    if (c === lastCharges) return;
    const gained = c > lastCharges && lastCharges >= 0;
    lastCharges = c;
    for (let i = 0; i < pips.length; i++) cls(pips[i], 'is-on', i < c);
    if (!gained) return;
    sfx(audio, 'gg-charge');
    const pip = pips[Math.max(0, c - 1)];
    fx.play(400, () => {
      cls(pip, 'is-pop', true);
      setTimeout(() => cls(pip, 'is-pop', false), 400);
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

  function paintRisk() {
    const s = match.scoring;
    const mult = s && typeof s.riskMultiplier === 'number' ? s.riskMultiplier : 1;
    text(riskEl, '×' + mult.toFixed(2) + ' risk');
  }

  function paintAll() {
    try { paintScore(); paintCharges(); paintTimer(); paintRisk(); }
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
    parts: { root, arsenal, opponent, dial, attention, emotes, announcer, fx, drops },
    unmount() {
      fx.stop();
      for (const rec of Array.from(chips.values())) dropChipRec(rec);
      led.run();
      try { root.remove(); } catch (_e) { /* gone */ }
      if (host) host.hidden = true;
    },
  };
}
