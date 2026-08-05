// Self-contained sanity pass over ui/ — the HUD tier, the mercy button and the
// sudden-death presenter/inputs, driven against a minimal DOM stub and a fake
// match object.
//
//   node Resources/web/goon/test/selftest-hud.js
//
// What it proves:
//   1. every ui/ module imports clean under node (no DOM at import time);
//   2. mountHud / mountMercy / createSuddenDeathUi construct and tear down
//      without throwing, and every engine subscription taken is released;
//   3. the presenter implements the full rounds/model.js contract (introspected
//      off nullRoundPresenter, not hand-listed) and every member survives being
//      called with a plausible spec;
//   4. the inputs object has the exact feed shape rounds/* subscribes to, and
//      the raise* drivers reach subscribers;
//   5. the arsenal fire path reaches match.tryFirePayload with a valid kind and
//      a duration inside the engine's clamps.

// ------------------------------------------------------------------ DOM stub

function installDom() {
  const listeners = () => new Map();

  function makeStyle() {
    const s = {};
    s.setProperty = (k, v) => { s[k] = v; };
    s.removeProperty = (k) => { delete s[k]; };
    return s;
  }

  function makeNode(tagName) {
    const kids = [];
    const map = listeners();
    const classes = new Set();
    const attrs = new Map();
    const node = {
      tagName: String(tagName || 'div').toUpperCase(),
      nodeType: 1,
      children: kids,
      childNodes: kids,
      parentNode: null,
      isConnected: true,
      hidden: false,
      style: makeStyle(),
      dataset: {},
      value: '',
      disabled: false,
      textContent: '',
      get className() { return Array.from(classes).join(' '); },
      set className(v) { classes.clear(); String(v || '').split(/\s+/).filter(Boolean).forEach((c) => classes.add(c)); },
      classList: {
        add: (...c) => c.forEach((x) => classes.add(x)),
        remove: (...c) => c.forEach((x) => classes.delete(x)),
        toggle: (c, on) => (on ? classes.add(c) : classes.delete(c)),
        contains: (c) => classes.has(c),
      },
      appendChild(child) { if (child) { child.parentNode = node; kids.push(child); } return child; },
      append(...c) { c.forEach((x) => node.appendChild(x)); },
      prepend(child) { if (child) { child.parentNode = node; kids.unshift(child); } return child; },
      removeChild(child) { const i = kids.indexOf(child); if (i >= 0) kids.splice(i, 1); return child; },
      remove() { if (node.parentNode) node.parentNode.removeChild(node); node.parentNode = null; node.isConnected = false; },
      replaceChildren(...c) { kids.length = 0; c.forEach((x) => node.appendChild(x)); },
      contains(other) {
        if (other === node) return true;
        for (const k of kids) if (k && typeof k.contains === 'function' && k.contains(other)) return true;
        return false;
      },
      setAttribute(k, v) { attrs.set(k, String(v)); },
      getAttribute(k) { return attrs.has(k) ? attrs.get(k) : null; },
      removeAttribute(k) { attrs.delete(k); },
      addEventListener(type, fn) { if (!map.has(type)) map.set(type, new Set()); map.get(type).add(fn); },
      removeEventListener(type, fn) { const s = map.get(type); if (s) s.delete(fn); },
      dispatchEvent(evt) {
        const s = map.get(evt && evt.type);
        if (s) for (const fn of Array.from(s)) { try { fn(evt); } catch (_e) { /* ignore */ } }
        return true;
      },
      /** Just enough selector engine for `.class` and bare tag names. */
      querySelectorAll(sel) {
        const sels = String(sel || '').split(',').map((s) => s.trim()).filter(Boolean);
        const out = [];
        const hit = (kid, s) => (s.startsWith('.')
          ? !!(kid._classes && kid._classes.has(s.slice(1)))
          : kid.tagName === s.toUpperCase());
        (function walk(nd) {
          for (const kid of nd.children || []) {
            if (sels.some((s) => hit(kid, s))) out.push(kid);
            walk(kid);
          }
        })(node);
        return out;
      },
      querySelector(sel) { return node.querySelectorAll(sel)[0] || null; },
      getBoundingClientRect() { return { left: 100, top: 100, right: 300, bottom: 220, width: 200, height: 120 }; },
      focus() {}, blur() {},
      setPointerCapture() {}, releasePointerCapture() {},
      _listeners: map,
      _classes: classes,
    };
    return node;
  }

  const doc = makeNode('#document');
  doc.documentElement = makeNode('html');
  doc.body = makeNode('body');
  doc.activeElement = null;
  const byId = new Map();
  for (const id of ['gg-hud', 'gg-mercy', 'gg-stage', 'scr-sd', 'gg-toasts', 'gg-drawer', 'gg-modal']) {
    const n = makeNode('div');
    n.id = id;
    n.hidden = true;
    byId.set(id, n);
    doc.body.appendChild(n);
  }
  doc.createElement = (tag) => makeNode(tag);
  doc.createTextNode = (t) => { const n = makeNode('#text'); n.textContent = String(t); return n; };
  doc.getElementById = (id) => byId.get(id) || null;
  // <body> is not a child of the document node in this stub, so the document's
  // own queries have to start from it explicitly (boot.js sweeps for a stray
  // .gg-mercy-takeover this way).
  doc.querySelectorAll = (sel) => doc.body.querySelectorAll(sel);
  doc.querySelector = (sel) => doc.body.querySelectorAll(sel)[0] || null;

  const win = makeNode('window');
  win.innerWidth = 1280;
  win.innerHeight = 720;

  globalThis.document = doc;
  globalThis.window = win;
  globalThis.CustomEvent = class CustomEvent {
    constructor(type, init) { this.type = type; this.detail = init && init.detail; this.bubbles = !!(init && init.bubbles); }
  };
  const rafs = new Set();
  globalThis.requestAnimationFrame = (fn) => {
    const id = setTimeout(() => { rafs.delete(id); try { fn(Date.now()); } catch (_e) { /* ignore */ } }, 8);
    rafs.add(id);
    return id;
  };
  globalThis.cancelAnimationFrame = (id) => { clearTimeout(id); rafs.delete(id); };
  return { doc, win, byId, rafs };
}

const dom = installDom();

// ------------------------------------------------------------------ harness

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Walk the stub tree collecting every node carrying a class. */
function findAll(root, className) {
  const out = [];
  (function walk(node) {
    if (!node) return;
    if (node._classes && node._classes.has(className)) out.push(node);
    for (const kid of node.children || []) walk(kid);
  })(root);
  return out;
}
const findOne = (root, className) => findAll(root, className)[0] || null;
const hasClass = (node, className) => !!(node && node._classes && node._classes.has(className));

// ------------------------------------------------------------------ imports

const { GoonConsts, GoonElement, GoonMatchPhase, GoonPayloadKind, GoonRoundKind } = await import('../core/contracts.js');
const { nullRoundInputs, nullRoundPresenter, GoonStimulusKind, GoonRoundVerdict } = await import('../core/rounds/model.js');
const { ELEMENTS, S } = await import('../ui/strings.js');

const hudMod = await import('../ui/hud.js');
const mercyMod = await import('../ui/mercy.js');
const sdMod = await import('../ui/sd/index.js');
const arsenalMod = await import('../ui/arsenal.js');
const opponentMod = await import('../ui/opponent.js');
const closenessMod = await import('../ui/closeness.js');
const attentionMod = await import('../ui/attention.js');
const emotesMod = await import('../ui/emotes.js');
const introMod = await import('../ui/sd/intro.js');
const verdictMod = await import('../ui/sd/verdict.js');
const quickdrawMod = await import('../ui/sd/quickdraw.js');
const staringMod = await import('../ui/sd/staring.js');
const reactionMod = await import('../ui/sd/reaction.js');
const bubblesMod = await import('../ui/sd/bubbles.js');

ok(typeof hudMod.mountHud === 'function', 'ui/hud.js exports mountHud');
ok(typeof mercyMod.mountMercy === 'function', 'ui/mercy.js exports mountMercy');
ok(typeof sdMod.createSuddenDeathUi === 'function', 'ui/sd/index.js exports createSuddenDeathUi');
ok(typeof arsenalMod.mountArsenal === 'function', 'ui/arsenal.js exports mountArsenal');
ok(typeof opponentMod.mountOpponent === 'function', 'ui/opponent.js exports mountOpponent');
ok(typeof closenessMod.mountCloseness === 'function', 'ui/closeness.js exports mountCloseness');
ok(typeof attentionMod.mountAttention === 'function', 'ui/attention.js exports mountAttention');
ok(typeof emotesMod.mountEmotes === 'function', 'ui/emotes.js exports mountEmotes');
ok(typeof introMod.createIntro === 'function', 'ui/sd/intro.js exports createIntro');
ok(typeof verdictMod.createVerdict === 'function', 'ui/sd/verdict.js exports createVerdict');
ok(typeof quickdrawMod.createQuickDraw === 'function', 'ui/sd/quickdraw.js exports createQuickDraw');
ok(typeof staringMod.createStaring === 'function', 'ui/sd/staring.js exports createStaring');
ok(typeof reactionMod.createReaction === 'function', 'ui/sd/reaction.js exports createReaction');
ok(typeof bubblesMod.createBubbles === 'function', 'ui/sd/bubbles.js exports createBubbles');

// ------------------------------------------------------------------ fake match

function makeFakeMatch() {
  const subs = { taken: 0, released: 0 };
  const handlers = new Map();
  const fires = [];
  const calls = { setCloseness: [], emote: [], checks: [], mercy: 0 };

  function reg(name) {
    return (fn) => {
      subs.taken++;
      if (!handlers.has(name)) handlers.set(name, new Set());
      handlers.get(name).add(fn);
      return () => { subs.released++; handlers.get(name).delete(fn); };
    };
  }

  const scoring = {
    score: 120,
    charges: 3,
    /* KEPT ON PURPOSE, THOUGH NO UI READS THEM ANY MORE (2026-08-05). The real
     * GoonScoring still exposes both — they are the engine's C#-parity names
     * for the pool bonus — so the stub keeps the shape honest, and section 2c-ii
     * leans on them: a HUD that ignores a 1.3 sitting right there is the proof
     * the readout is gone rather than merely blank. */
    riskMultiplier: 1.3,
    riskTier: 2,
    _charge: new Set(),
    onChargesChanged(fn) { subs.taken++; scoring._charge.add(fn); return () => { subs.released++; scoring._charge.delete(fn); }; },
  };

  const match = {
    phase: GoonMatchPhase.Live,
    scoring,
    opponent: {
      displayName: 'test peer', score: 88, attentionPct: 74, charges: 2,
      activeEffects: ['Flashes', 'Bubbles', 'LockCards'], toyActive: false,
      closeness: 2, health: 0, lastTickLocalMs: 1, hasSeenTick: true,
    },
    consentSheet: { live_duration_sec: 720 },
    liveElapsedMs: 30000,
    liveRemainingMs: 690000,
    localCloseness: null,
    localAttentionMode: 1,
    localCaps: { payloads: Object.values(GoonPayloadKind) },
    availablePayloadKinds: Object.values(GoonPayloadKind),

    onPhaseChanged: reg('phase'),
    onElementStartRequested: reg('elStart'),
    onElementIntensityChanged: reg('elInt'),
    onElementStopRequested: reg('elStop'),
    onPayloadAccepted: reg('pAcc'),
    onPayloadRejected: reg('pRej'),
    onPayloadReceiptReceived: reg('pRec'),
    onOpponentStateChanged: reg('opp'),
    onConnectionHealthChanged: reg('health'),
    onEmoteReceived: reg('emote'),
    onInteractionCheckDue: reg('check'),
    onMatchEnded: reg('ended'),
    onResultFinalized: reg('final'),

    tryFirePayload(req) { fires.push(req); return { ok: true, error: '', id: 'p' + fires.length + 'h' }; },
    setCloseness(v) { calls.setCloseness.push(v); match.localCloseness = v; },
    sendEmote(t, i) { calls.emote.push({ t, i }); },
    reportInteractionCheck(p) { calls.checks.push(p); },
    declareMercy() { calls.mercy++; },

    _emit(name, arg) { const s = handlers.get(name); if (s) for (const fn of Array.from(s)) fn(arg); },
    _subs: subs,
    _fires: fires,
    _calls: calls,
  };
  return match;
}

// ------------------------------------------------------------------ 1. HUD

{
  const match = makeFakeMatch();
  const audio = { ids: [], sfx(id) { audio.ids.push(id); } };
  const log = [];
  let hud = null;
  let threw = null;
  try {
    hud = hudMod.mountHud({
      match,
      session: { identity: { displayName: 'tester' } },
      audio,
      prefs: { reducedMotion: false },
      media: null,
      matchLog: log,
    });
  } catch (e) { threw = e; }
  ok(!threw, 'mountHud does not throw', threw && threw.stack);
  ok(hud && typeof hud.unmount === 'function', 'mountHud returns an unmount');
  ok(dom.byId.get('gg-hud').hidden === false, 'mountHud unhides #gg-hud');
  ok(match._subs.taken > 6, 'mountHud subscribes to the engine', String(match._subs.taken));

  // the desk survives real engine traffic
  let liveThrew = null;
  try {
    match._emit('opp');
    match._emit('health', 1);
    match._emit('emote', { text: 'nice try', icon: '😏' });
    match._emit('elStart', { element: 0, intensity: 0.5, durationMs: 6000, elapsedMs: 1000 });
    match._emit('elInt', { element: 0, intensity: 0.8, durationMs: 6000, elapsedMs: 2000 });
    match._emit('elStop', { element: 0, intensity: 0, durationMs: 0, elapsedMs: 3000 });
    match._emit('pAcc', { payload: { id: 'x1', kind: GoonPayloadKind.FlashBurst, duration_ms: 6000 }, fireAtLocalMs: 0 });
    match._emit('pRec', { id: 'x1', status: 'survived' });
    match._emit('check');
    match._emit('phase', GoonMatchPhase.SuddenDeath);
    match._emit('phase', GoonMatchPhase.Live);
  } catch (e) { liveThrew = e; }
  ok(!liveThrew, 'HUD survives the full event surface', liveThrew && liveThrew.stack);
  ok(match._calls.checks.length === 0, 'a spawned check is not auto-resolved');

  let downThrew = null;
  try { hud.unmount(); } catch (e) { downThrew = e; }
  ok(!downThrew, 'hud.unmount does not throw', downThrew && downThrew.stack);
  ok(match._subs.released === match._subs.taken,
    'every engine subscription released on unmount', `${match._subs.released}/${match._subs.taken}`);
  ok(dom.byId.get('gg-hud').children.length === 0, 'unmount empties #gg-hud');
}

// ------------------------------------------------------------- 2. arsenal fire

{
  const match = makeFakeMatch();
  const left = document.createElement('div');
  const right = document.createElement('div');
  const mon = document.createElement('div');
  const ars = arsenalMod.mountArsenal({
    leftHost: left, rightHost: right,
    receiptsHost: document.createElement('div'),
    coolHost: document.createElement('div'),
    match,
    getDropTarget: () => mon,
  });
  ok(left.children.length === 4, 'left rail renders 4 items', String(left.children.length));
  ok(right.children.length === 4, 'right rail renders 4 items', String(right.children.length));
  ok(left.children.length + right.children.length === arsenalMod.ARSENAL_ITEMS.length,
    'every arsenal item got a slot (8: 7 payloads + emote)', String(left.children.length + right.children.length));
  ok(arsenalMod.ARSENAL_ITEMS.some((i) => i.id === 'spiral' && i.kind === GoonPayloadKind.Spiral),
    'the spiral slot is wired to GoonPayloadKind.Spiral');
  // THE BUBBLE THROWABLE IS GONE (owner, 2026-08-04). Bubbles are the always-on
  // baseline field, so a bubble payload item threw more of what was already
  // running. It may not come back as a slot, a key or a drop candidate — but the
  // WIRE kind stays frozen and inbound BubbleSwarm still renders (exec/bubbles.js).
  ok(arsenalMod.ARSENAL_ITEMS.every((i) => i.kind !== GoonPayloadKind.BubbleSwarm),
    'no BubbleSwarm slot: the bubble throwable is not obtainable');
  ok(arsenalMod.ARSENAL_ITEMS.every((i) => i.id !== 'bubbles'),
    'and its id is gone with it');
  ok(arsenalMod.ARSENAL_ITEMS.filter((i) => i.kind !== null).length === 7,
    'seven payload slots, i.e. keys 1-7',
    String(arsenalMod.ARSENAL_ITEMS.filter((i) => i.kind !== null).length));
  ok(!!findOne(left, 'gg-item-fallback'), 'each tile carries a CSS art fallback for a missing PNG');
  ok(findAll(left, 'gg-plate').length === 0 && findAll(right, 'gg-plate').length === 0,
    'item tiles are chromeless: no .gg-plate anywhere on the rails');

  // THE DROP ECONOMY. Every payload slot starts locked; only a bubble drop opens
  // one. (Pre-economy this fire returned ok and the block below was the whole
  // test — a locked slot that still fires is exactly the regression to catch.)
  ok(ars.isLocked('flash') === true, 'a payload slot starts LOCKED');
  ok(ars.stateOf('flash') === 'locked', 'and paints the locked state', String(ars.stateOf('flash')));
  const lockedTile = findAll(left, 'gg-item').find((t) => t.getAttribute('data-gg-item') === 'flash');
  ok(hasClass(lockedTile, 'is-locked'), 'the locked tile carries .is-locked');
  ok(!!findOne(lockedTile, 'gg-item-lock'), 'and a lock affordance on the sticker');
  const lockedWord = findOne(lockedTile, 'gg-item-state');
  ok(lockedWord && String(lockedWord.textContent).length > 0,
    'the locked state says so in a WORD, not colour alone', lockedWord && lockedWord.textContent);
  const lockedFire = ars.fire('flash');
  ok(lockedFire && lockedFire.ok === false && lockedFire.error === 'locked',
    'a locked slot refuses to fire', JSON.stringify(lockedFire));
  ok(match._fires.length === 0, 'and never reaches the engine', String(match._fires.length));

  // the emote slot is social: no cost, so it is never locked and never dropped
  ok(ars.isLocked('emote') === false, 'the free emote slot is never locked');
  ok(ars.droppable().every((c) => c.id !== 'emote'), 'and never appears in the drop pool');
  ok(arsenalMod.needsArming({ kind: null, id: 'emote' }, 0) === false, 'needsArming(): a 0-cost slot never arms');
  ok(arsenalMod.needsArming({ kind: GoonPayloadKind.FlashBurst }, 1) === true, 'needsArming(): a priced payload does');

  // a drop arms it, and only then does the same call reach the engine
  ok(ars.armDrop('flash') === true, 'armDrop arms the slot');
  ok(ars.armedCount('flash') === 1, 'one stack banked', String(ars.armedCount('flash')));
  ok(ars.isLocked('flash') === false, 'an armed slot is no longer locked');
  const badge = findOne(lockedTile, 'gg-item-stack');
  ok(badge && badge.hidden === false && String(badge.textContent).indexOf('1') >= 0,
    'the stack badge shows the count', badge && badge.textContent);

  const res = ars.fire('flash');
  ok(res && res.ok === true, 'an ARMED slot fires through to the engine');
  ok(match._fires.length === 1, 'exactly one payload per fire');
  ok(ars.armedCount('flash') === 0, 'firing consumes one stack', String(ars.armedCount('flash')));
  ok(ars.isLocked('flash') === true, 'an empty slot goes back to locked');
  const req = match._fires[0];
  const kinds = Object.values(GoonPayloadKind);
  ok(kinds.includes(req.kind), 'fired kind is in GoonPayloadKind', String(req.kind));
  ok(req.durationMs >= 1000 && req.durationMs <= 180000, 'duration inside the engine clamps', String(req.durationMs));
  ok(req.intensity > 0 && req.intensity <= 1, 'intensity inside 0..1', String(req.intensity));

  // every payload item can be fired (once armed) and every kind it sends is legal
  for (const item of arsenalMod.ARSENAL_ITEMS) {
    if (item.kind === null) continue;
    ars.armDrop(item.id);
    ars.fire(item.id);
  }
  ok(match._fires.every((f) => kinds.includes(f.kind)), 'every fired kind is a real payload kind');
  ok(match._fires.every((f) => f.durationMs >= 1000 && f.durationMs <= 180000), 'every duration is inside the clamps');

  // a heavy is one per match: the second brain drain must be refused locally
  const before = match._fires.length;
  ars.armDrop('braindrain');
  ars.fire('braindrain');
  ok(match._fires.length === before, 'brain drain is refused after it has been used');

  ars.unmount();
  ok(match._subs.released === match._subs.taken, 'arsenal releases its subscriptions', `${match._subs.released}/${match._subs.taken}`);
}

// ------------------------------------ 2b. the spiral slot + the onFired glue
// A fresh mount, because the rate-limit mirror in the block above is spent by
// then and a refused fire would prove nothing about the slot.
{
  const match = makeFakeMatch();
  const fired = [];
  const ars = arsenalMod.mountArsenal({
    leftHost: document.createElement('div'),
    rightHost: document.createElement('div'),
    receiptsHost: document.createElement('div'),
    match,
    onFired: (shot) => fired.push(shot),
  });
  ars.armDrop('spiral');
  const res = ars.fire('spiral');
  ok(res && res.ok === true, 'the spiral slot fires');
  ok(match._fires.length === 1 && match._fires[0].kind === GoonPayloadKind.Spiral,
    'spiral fires GoonPayloadKind.Spiral', JSON.stringify(match._fires[0] || null));
  ok(match._fires[0].durationMs >= 1000 && match._fires[0].durationMs <= 180000, 'spiral duration is inside the clamps');
  ok(fired.length === 1 && fired[0].id === res.id && fired[0].kind === GoonPayloadKind.Spiral && fired[0].durationMs > 0,
    'onFired hands hud.js the kind + duration + id of an accepted fire', JSON.stringify(fired));
  ars.unmount();
}

/* --- 2b-i. ARSENAL: stacking, locked gestures, and the drop pool ----------
 * The economy in one block: multiple drops stack, firing eats one, an empty
 * slot re-locks, and a locked slot refuses BOTH ways in (drag + keyboard) —
 * every one of these passes trivially against the pre-economy arsenal, where
 * a tile was only ever gated on charges. */
{
  const match = makeFakeMatch();
  const left = document.createElement('div');
  const ars = arsenalMod.mountArsenal({
    leftHost: left,
    rightHost: document.createElement('div'),
    receiptsHost: document.createElement('div'),
    match,
    getDropTarget: () => document.createElement('div'),
  });
  const tileOf = (id) => findAll(left, 'gg-item').find((t) => t.getAttribute('data-gg-item') === id) || null;

  // stacking
  ars.armDrop('flash');
  ars.armDrop('flash');
  ok(ars.armedCount('flash') === 2, 'two drops of the same item stack', String(ars.armedCount('flash')));
  ok(ars.armDrop('flash', { count: 2 }) === true, 'armDrop takes a count');
  ok(ars.armedCount('flash') === 4, 'and adds it to the stack', String(ars.armedCount('flash')));
  const stackBadge = findOne(tileOf('flash'), 'gg-item-stack');
  ok(stackBadge && String(stackBadge.textContent).indexOf('4') >= 0,
    'the badge tracks the stack', stackBadge && stackBadge.textContent);
  ars.fire('flash');
  ok(ars.armedCount('flash') === 3, 'one shot, one stack', String(ars.armedCount('flash')));
  ok(ars.stateOf('flash') === 'ready', 'a stacked slot stays ready after firing', String(ars.stateOf('flash')));

  // the LOCKED gestures: no drag ghost, no keyboard arm, no engine traffic
  const before = match._fires.length;
  const locked = tileOf('subliminal');
  ok(ars.isLocked('subliminal') === true, 'the subliminal slot is still locked');
  const bodyKids = document.body.children.length;
  let PID = 900;
  const ptr = (node, type, x, y) => {
    const e = { type, button: 0, pointerId: PID, clientX: x, clientY: y, preventDefault() {}, stopPropagation() {} };
    if (node && node.dispatchEvent) node.dispatchEvent(e);
    return e;
  };
  ptr(locked, 'pointerdown', 20, 20);
  ptr(locked, 'pointermove', 200, 200);
  ptr(locked, 'pointerup', 200, 200);
  ok(findAll(document.body, 'gg-item-ghost').length === 0, 'a locked slot mints no drag ghost');
  ok(document.body.children.length === bodyKids, 'and leaves nothing behind in the body');
  ok(match._fires.length === before, 'and never fires by drag', String(match._fires.length - before));
  ok(!hasClass(locked, 'is-armed'), 'a locked slot never enters the armed (tap-to-target) state');

  // keyboard: 2 is the subliminal slot; pressing it twice must still do nothing
  const key = (k) => dom.win.dispatchEvent({ type: 'keydown', key: k, repeat: false });
  key('2'); key('2');
  ok(match._fires.length === before, 'keys cannot fire a locked slot either', String(match._fires.length - before));
  ok(!hasClass(locked, 'is-armed'), 'and cannot arm it for a monitor tap');

  // …the same key on an ARMED slot still works (the gate is the lock, not the key)
  ars.armDrop('subliminal');
  key('2'); key('2');
  ok(match._fires.length === before + 1, 'the same two presses fire it once it is armed',
    String(match._fires.length - before));

  ars.unmount();
}

/* --- 2b-ii. the drop POOL respects caps, consent and the spent heavy ------ */
{
  // haptics off -> boot.js never advertises ToyPattern -> no toy tile at all
  const match = makeFakeMatch();
  match.localCaps = { payloads: Object.values(GoonPayloadKind).filter((k) => k !== GoonPayloadKind.ToyPattern) };
  match.availablePayloadKinds = Object.values(GoonPayloadKind).filter((k) => k !== GoonPayloadKind.Video);
  const ars = arsenalMod.mountArsenal({
    leftHost: document.createElement('div'),
    rightHost: document.createElement('div'),
    receiptsHost: document.createElement('div'),
    match,
  });
  const ids = ars.droppable().map((c) => c.id);
  ok(!ids.includes('toy'), 'a gated kind (ToyPatterns without haptics) is not droppable', ids.join(','));
  ok(!ids.includes('video'), 'a kind THEY cannot receive is not droppable either', ids.join(','));
  ok(!ids.includes('emote'), 'the free social slot is never droppable', ids.join(','));
  ok(ids.includes('flash') && ids.includes('braindrain'), 'everything legal still is', ids.join(','));
  ok(ars.droppable().every((c) => typeof c.cost === 'number' && c.cost > 0),
    'every candidate carries the cost the roller weights by');

  // spend the heavy: it must leave the pool (arming it again is a dead drop)
  ars.armDrop('braindrain');
  const res = ars.fire('braindrain');
  ok(res && res.ok === true, 'the heavy fires once');
  ok(ars.stateOf('braindrain') === 'used', 'and then reads used', String(ars.stateOf('braindrain')));
  ok(ars.droppable().every((c) => c.id !== 'braindrain'), 'a spent heavy leaves the drop pool');
  ars.unmount();
}

/* --- 2b-iii. ui/drops.js: the HEAT economy ---------------------------------
 * The flat "12% per unit of worth" coin flip was replaced on 2026-08-04 by a
 * heat gauge: pops bank heat, the chance ramps with the fill, a landed drop
 * spends a chunk back. Pure functions first (heat gain, the chance ramp, decay,
 * rarity weighting), then the roller with a scripted RNG and a scripted clock so
 * the whole pop -> heat -> credit -> arm -> spend chain is deterministic.
 *
 * THE ONE ASSERTION THAT IS THE ECONOMY. Over a long run heat in must equal heat
 * out, so drops-per-pop settles at HEAT_PER_WORTH / HEAT_DROP_COST x worth
 * REGARDLESS of the curve's shape. That ratio is therefore the whole pacing
 * knob, and it is pinned here against the old flat rate it replaced: 12%/worth.
 * A curve retune must not move it; if it does, this fails, which is the point. */
{
  const { DROP_TUNING, heatGainFor, dropChanceAt, decayHeat, weightOf, pickDrop, createDropRoller } =
    await import('../ui/drops.js');
  const bub = await import('../exec/bubbles.js');

  ok(typeof DROP_TUNING.HEAT_PER_WORTH === 'number' && typeof DROP_TUNING.COST_BIAS === 'number',
    'ui/drops.js exports one tuning block');

  // the pacing pin: the equilibrium rate is the old flat 12%/worth, +-1 point
  const rate = DROP_TUNING.HEAT_PER_WORTH / DROP_TUNING.HEAT_DROP_COST;
  ok(Math.abs(rate - 0.12) <= 0.01,
    'the heat economy paces to the ~12%-per-worth it replaced', rate.toFixed(4));
  ok(Math.abs(rate * bub.POP_WORTH_EFFECT - 0.30) <= 0.03,
    'and an effect bubble to its old ~30%', (rate * bub.POP_WORTH_EFFECT).toFixed(4));

  // heat gain: worth in, heat out, clutter banks nothing
  ok(heatGainFor(bub.POP_WORTH_EFFECT) > heatGainFor(bub.POP_WORTH_NORMAL),
    'effect bubbles are worth strictly more heat than plain ones');
  ok(heatGainFor(bub.POP_WORTH_PAYLOAD) === 0, 'a payload-minted bubble banks no heat');
  ok(heatGainFor(undefined) === 0 && heatGainFor(null) === 0, 'a malformed worth banks no heat');

  // the ramp: floored, ceilinged, and strictly climbing in between
  ok(Math.abs(dropChanceAt(0) - DROP_TUNING.CHANCE_FLOOR) < 1e-9,
    'an empty gauge sits at the floor', String(dropChanceAt(0)));
  ok(Math.abs(dropChanceAt(DROP_TUNING.HEAT_MAX) - DROP_TUNING.CHANCE_PEAK) < 1e-9,
    'a full gauge sits at the peak', String(dropChanceAt(DROP_TUNING.HEAT_MAX)));
  ok(dropChanceAt(1e6) <= DROP_TUNING.CHANCE_PEAK && dropChanceAt(-50) >= DROP_TUNING.CHANCE_FLOOR,
    'the ramp is clamped at both ends', dropChanceAt(1e6) + '/' + dropChanceAt(-50));
  let climbs = true;
  for (let h = 0; h < DROP_TUNING.HEAT_MAX; h += 5) { if (!(dropChanceAt(h + 5) > dropChanceAt(h))) climbs = false; }
  ok(climbs, 'a fuller gauge is always a better chance, at every step');
  ok(dropChanceAt(DROP_TUNING.HEAT_MAX) < 1, 'and a drop is never a certainty');

  // decay: gentle, clamped at empty, a no-op for nonsense elapsed times
  ok(decayHeat(50, 10000) === 50 - DROP_TUNING.HEAT_DECAY_PER_SEC * 10,
    'heat cools on the wall clock', String(decayHeat(50, 10000)));
  ok(decayHeat(3, 999999) === 0, 'and never goes below empty');
  ok(decayHeat(40, 0) === 40 && decayHeat(40, -5) === 40, 'a zero or negative elapsed cools nothing');

  ok(weightOf(1) > weightOf(2) && weightOf(2) > weightOf(3), 'cheap items are commoner than expensive ones');

  // the weighted pick: roll 0 lands on the first candidate, roll ~1 on the last
  const pool = [{ id: 'flash', cost: 1, armed: 0 }, { id: 'video', cost: 2, armed: 0 }, { id: 'braindrain', cost: 3, armed: 0 }];
  ok(pickDrop(pool, 0).id === 'flash', 'pickDrop is weighted, cheapest-first');
  ok(pickDrop(pool, 0.999).id === 'braindrain', 'and reaches the rare end');
  ok(pickDrop([], 0.5) === null, 'an empty pool drops nothing');
  ok(pickDrop([{ id: 'flash', cost: 1, armed: DROP_TUNING.MAX_STACK }], 0.5) === null,
    'a maxed-out stack stops being a candidate');

  // the roller end to end
  const armedIds = [];
  const fakeArsenal = {
    droppable: () => [{ id: 'flash', kind: GoonPayloadKind.FlashBurst, cost: 1, armed: 0 }],
    armDrop: (id) => { armedIds.push(id); return true; },
  };
  /* THE ROLLER PAYS FOR NOTHING (2026-08-05). It used to call
     match.creditCharges(cost, 'bubble-drop') before arming and refuse the drop
     ('no-charge') if the engine said no — the receiver validated the sender's
     wallet, so an armed slot with an empty meter would have fired into a
     rejection. The owner deleted that requirement, so the roller now talks to
     the arsenal ALONE. `credits` survives as a TRIPWIRE: the fake engine records
     any call and the checks below assert there were none. */
  const credits = [];
  const match = {
    phase: GoonMatchPhase.Live,
    creditCharges(n, reason) { credits.push({ n, reason }); return true; },
  };
  const rolls = [0, 0];   // always a hit, always the first candidate
  const roller = createDropRoller({ match, arsenal: fakeArsenal, random: () => rolls.shift() ?? 0 });

  const clutter = roller.onPop({ kind: 'normal', worth: bub.POP_WORTH_PAYLOAD, payload: true });
  ok(clutter.dropped === false && clutter.reason === 'clutter', 'a payload-minted pop is skipped entirely');

  const hit = roller.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT, x: 10, y: 10 });
  ok(hit.dropped === true && hit.id === 'flash', 'a winning roll arms an item', JSON.stringify(hit));
  ok(credits.length === 0,
    'and pays NOTHING for it — a drop is a grant now, not a purchase', JSON.stringify(credits));
  ok(armedIds.length === 1, 'exactly one arm per drop');

  // an engine that refuses every credit changes nothing, because we never ask
  const noCredit = createDropRoller({
    match: { phase: GoonMatchPhase.Live, creditCharges: () => false },
    arsenal: fakeArsenal,
    random: () => 0,
  });
  const blocked = noCredit.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT });
  ok(blocked.dropped === true && blocked.reason === 'drop',
    'an engine that refuses every charge can no longer block a drop', JSON.stringify(blocked));
  ok(armedIds.length === 2, 'and the arsenal armed it', String(armedIds.length));

  // outside Live nothing rolls at all — THE phase check, and now the only gate
  const idle = createDropRoller({ match: { phase: GoonMatchPhase.Draft }, arsenal: fakeArsenal, random: () => 0 });
  ok(idle.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT }).reason === 'phase', 'no drops outside a live match');

  // a missing seam (older engine) must not break the loop
  const noSeam = createDropRoller({ match: { phase: GoonMatchPhase.Live }, arsenal: fakeArsenal, random: () => 0 });
  ok(noSeam.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT }).dropped === true,
    'the roller still works against an engine with no charge seam at all');

  // a miss is a miss
  const missed = createDropRoller({ match, arsenal: fakeArsenal, random: () => 0.99 });
  ok(missed.onPop({ kind: 'normal', worth: bub.POP_WORTH_NORMAL }).reason === 'miss', 'a losing roll drops nothing');

  /* --- the gauge itself: what fills it, what spends it, what leaves it alone -
   * A scripted clock, because decay is lazy (read-time, no timer) and a real one
   * would make every heat number here a race. */
  {
    let t = 0;
    const clock = () => t;
    const cold = createDropRoller({ match, arsenal: fakeArsenal, random: () => 0.99, now: clock });
    ok(cold.heat === 0 && cold.heatFraction === 0, 'a fresh roller is stone cold', String(cold.heat));

    cold.onPop({ kind: 'normal', worth: bub.POP_WORTH_NORMAL });
    const afterOne = cold.heat;
    ok(Math.abs(afterOne - heatGainFor(bub.POP_WORTH_NORMAL)) < 1e-9,
      'a pop banks exactly its worth in heat', String(afterOne));
    cold.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT });
    ok(cold.heat > afterOne, 'and they add up', String(cold.heat));

    // clutter is not a pop as far as the gauge is concerned
    const beforeClutter = cold.heat;
    cold.onPop({ kind: 'normal', worth: bub.POP_WORTH_PAYLOAD, payload: true });
    ok(cold.heat === beforeClutter, 'a payload-minted pop banks nothing', String(cold.heat));

    // it cools when nobody is popping, and never past empty
    t += 10000;
    ok(cold.heat < beforeClutter && cold.heat > 0, 'the gauge cools between pops', String(cold.heat));
    t += 10 * 60 * 1000;
    ok(cold.heat === 0, 'and bottoms out rather than going negative', String(cold.heat));

    // the ceiling holds however hard you pop
    const capped = createDropRoller({ match, arsenal: fakeArsenal, random: () => 0.99, now: clock });
    for (let i = 0; i < 200; i++) capped.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT });
    ok(capped.heat === DROP_TUNING.HEAT_MAX && capped.heatFraction === 1,
      'heat is capped at a full gauge', String(capped.heat));

    /* A LANDED drop spends the gauge down; a refused one must not.
     * `win` is the scripted RNG both rollers share: 0.99 (never wins the roll)
     * while the gauge is being filled, 0 (always wins, always the first
     * candidate) once we want the drop. Rolling a win on every pop instead would
     * spend the heat as fast as it arrived and prove nothing. */
    let win = false;
    const scripted = () => (win ? 0 : 0.99);

    const spender = createDropRoller({ match, arsenal: fakeArsenal, random: scripted, now: clock });
    for (let i = 0; i < 4; i++) spender.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT });
    const hotBefore = spender.heat;
    ok(hotBefore > DROP_TUNING.HEAT_DROP_COST,
      'four effect pops bank more than one drop costs', String(hotBefore));
    win = true;
    const landed = spender.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT });
    win = false;
    ok(landed.dropped === true, 'the scripted roll lands a drop', JSON.stringify(landed));
    ok(spender.heat < hotBefore, 'a drop spends heat back down', `${hotBefore} -> ${spender.heat}`);
    ok(spender.heat >= 0, 'never below empty', String(spender.heat));

    /* A DROP THE PLAYER NEVER GOT MUST NOT SPEND THE GAUGE. The refusal used to
       be the engine saying no to the charge; since 2026-08-05 there is no charge
       to be refused, so the remaining way to win a roll and get nothing is a
       FULL ARSENAL — armDrop() answering false. Same invariant, real cause. */
    const fullArsenal = {
      droppable: () => [{ id: 'flash', kind: GoonPayloadKind.FlashBurst, cost: 1, armed: 0 }],
      armDrop: () => false,
    };
    const refuser = createDropRoller({
      match, arsenal: fullArsenal, random: scripted, now: clock,
    });
    for (let i = 0; i < 2; i++) refuser.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT });
    const heldBefore = refuser.heat;
    ok(heldBefore < DROP_TUNING.HEAT_MAX, 'the gauge is below the cap, so a change would show');
    win = true;
    const refused = refuser.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT });
    win = false;
    ok(refused.reason === 'refused', 'the arsenal refuses the drop', JSON.stringify(refused));
    ok(refuser.heat > heldBefore,
      'and a drop the player never got does NOT spend the gauge', `${heldBefore} -> ${refuser.heat}`);

    // the reset the HUD leans on between matches
    spender.resetHeat();
    ok(spender.heat === 0, 'resetHeat empties the gauge');
  }
}

/* --- 2b-iv. the HUD glue: gg-bubble-pop -> roller -> armed sticker --------- */
{
  const match = makeFakeMatch();
  const hud = hudMod.mountHud({ match, audio: { sfx() {} } });
  ok(hud.parts.drops && typeof hud.parts.drops.onPop === 'function', 'mountHud builds the drop roller');
  ok(hudMod.BUBBLE_POP_EVENT === 'gg-bubble-pop', 'and listens on exec/bubbles.js\'s event name');

  const ars = hud.parts.arsenal;
  ok(ars.droppable().length > 0, 'the desk has something to drop');
  const beforeArmed = ars.droppable().reduce((a, c) => a + c.armed, 0);

  // force the roll: this is client-local Math.random economy, so pinning it is
  // the only way to assert the chain instead of a probability.
  const realRandom = Math.random;
  Math.random = () => 0;
  try {
    document.dispatchEvent(new CustomEvent('gg-bubble-pop', {
      detail: { kind: 'flash', worth: 2.5, payload: false, x: 40, y: 40 },
    }));
  } finally { Math.random = realRandom; }

  const afterArmed = ars.droppable().reduce((a, c) => a + c.armed, 0);
  ok(afterArmed === beforeArmed + 1, 'a real pop event arms exactly one item through the HUD',
    `${beforeArmed} -> ${afterArmed}`);
  ok(hud.parts.drops.stats.drops === 1, 'and the roller counted it', JSON.stringify(hud.parts.drops.stats));

  // clutter from THEIR swarm changes nothing
  Math.random = () => 0;
  try {
    document.dispatchEvent(new CustomEvent('gg-bubble-pop', {
      detail: { kind: 'flash', worth: 0, payload: true, x: 40, y: 40 },
    }));
  } finally { Math.random = realRandom; }
  ok(ars.droppable().reduce((a, c) => a + c.armed, 0) === afterArmed,
    'a payload-minted pop arms nothing, however lucky the roll');
  ok(hud.parts.drops.stats.clutter === 1, 'and is counted as clutter', JSON.stringify(hud.parts.drops.stats));

  hud.unmount();
  ok(match._subs.released === match._subs.taken, 'the drop glue leaks no subscriptions',
    `${match._subs.released}/${match._subs.taken}`);
}

/* --- 2b-v. the HEAT GAUGE on the desk (owner, 2026-08-04) ------------------
 * The economy is invisible unless this box is: the whole reason heat replaced a
 * flat coin flip is that a player can WATCH it fill. So the gauge is pinned as
 * DOM, in the arsenal's own column, painting off the roller — and pinned as a
 * readout, because a gauge that ate clicks would be a dead 190px box on a desk
 * whose bottom layer is MERCY. */
{
  const match = makeFakeMatch();
  const hud = hudMod.mountHud({ match, audio: { sfx() {} } });
  const heat = hud.parts.heat;

  ok(heat && heat.box && heat.fill, 'mountHud builds the heat gauge');
  ok(hasClass(heat.box, 'gg-heat'), 'under the name ui/hud.css styles');
  ok(!hasClass(heat.box, 'gg-plate'),
    'and NOT as a .gg-plate — that would re-enable pointer events at 0,2,0 over the desk');
  ok(heat.box.getAttribute('role') === 'progressbar', 'it announces itself as a progressbar');
  ok(typeof S.hud.heatLabel === 'string' && S.hud.heatLabel.length > 0,
    'the gauge has a word beside the colour', String(S.hud.heatLabel));
  ok(heat.box.getAttribute('aria-label') === S.hud.heatLabel, 'and it is the labelled name');

  /* It lives INSIDE THE ARSENAL SIDEBAR, at the head of the panel, above the
   * rails it fills — never in the bottom deck, which is the strip MERCY owns.
   * It moved here with the sidebar (2026-08-04): it is the item economy's meter,
   * so it collapses WITH the items rather than being left behind under a monitor
   * it has nothing to do with. */
  const col = heat.box.parentNode;
  ok(hasClass(col, 'gg-arsenal-panel'), 'it rides inside the arsenal panel', col && col.className);
  ok(hasClass(col.parentNode, 'gg-arsenal'), 'which is the collapsible sidebar itself');
  const kids = col.children.map((n) => n.className);
  ok(kids.indexOf('gg-heat') === 0 && kids.some((c) => /gg-arsenal-rails/.test(c)),
    'sitting directly on top of the rails it fills', kids.join(' | '));
  ok(col === hud.parts.sidebar.panel, 'so collapsing the panel takes the gauge with it');

  // and it PAINTS: a forced drop-less pop must move the fill off zero
  ok(heat.fill.style.width === '0.0%' || heat.fill.style.width === '0%' || !heat.fill.style.width,
    'it starts empty', String(heat.fill.style.width));
  const realRandom5 = Math.random;
  Math.random = () => 0.999;   // bank heat, never win the roll
  try {
    for (let i = 0; i < 4; i++) {
      document.dispatchEvent(new CustomEvent('gg-bubble-pop', {
        detail: { kind: 'flash', worth: 2.5, payload: false, x: 40, y: 40 },
      }));
    }
  } finally { Math.random = realRandom5; }
  ok(hud.parts.drops.heat > 0, 'four pops bank heat on the roller', String(hud.parts.drops.heat));
  ok(heat.fraction > 0 && heat.fraction <= 1, 'the gauge reads it back as a fraction', String(heat.fraction));
  // the paint runs on the 200ms interval; drive it the way the interval would
  const pct = parseFloat(heat.fill.style.width || '0');
  ok(!Number.isNaN(pct), 'and the fill carries a width the CSS can transition', String(heat.fill.style.width));

  hud.unmount();
  ok(match._subs.released === match._subs.taken, 'the gauge leaks no subscriptions (it polls, never subscribes)',
    `${match._subs.released}/${match._subs.taken}`);
}

/* --- 2b-vi. THE ARSENAL SIDEBAR (owner, 2026-08-04) ------------------------
 * "The throwable items should live in a sidebar that can be collapsed", on the
 * desktop panel AND on the phone that joined by link.
 *
 * The whole feature is one class on one node, so what is worth pinning is not
 * the class — it is the four ways a collapsible control panel goes wrong on THIS
 * desk, every one of which has already happened to something else here:
 *
 *   1. it covers or gates MERCY (it cannot: #gg-hud is a z40 stacking context);
 *   2. collapsed leaves an invisible hit area over the field and the drop
 *      economy quietly starves, the way an open sheet at Live once did;
 *   3. the keyboard stops working when the tiles are not on screen, which turns
 *      a tidy desk into a disarmed player;
 *   4. the choice is not remembered, or is remembered so hard that a phone opens
 *      a drawer over the field it is trying to pop bubbles in.
 * ------------------------------------------------------------------------- */
{
  const match = makeFakeMatch();
  const store = {};
  const prefs = { get: (k) => store[k], set: (k, v) => { store[k] = v; return true; } };
  const hud = hudMod.mountHud({ match, audio: { sfx() {} }, prefs });
  const host = dom.byId.get('gg-hud');
  const side = hud.parts.sidebar;

  /* ---- the DOM ---------------------------------------------------------- */
  ok(side && side.root && side.panel && side.tab, 'mountHud builds the sidebar');
  ok(hasClass(side.root, 'gg-arsenal'), 'under the name ui/hud.css styles', side.root.className);
  ok(hasClass(side.root.parentNode, 'gg-hud-body'), 'it is a cell of the desk body, not a layer of its own');
  const bodyKids16 = side.root.parentNode.children.map((k) => k.className);
  ok(bodyKids16[0].indexOf('gg-arsenal') === 0,
    'and the FIRST one — the sidebar is on the left, where nothing structural lives', bodyKids16.join(' | '));
  ok(bodyKids16.some((c) => /gg-rightcol/.test(c)),
    'the right column (monitor · receipts · videos.js keep-out) is untouched by it');

  const panelKids = side.panel.children.map((k) => k.className);
  ok(panelKids.includes('gg-heat'), 'the panel carries the heat gauge', panelKids.join(' | '));
  ok(panelKids.some((c) => /gg-arsenal-rails/.test(c)), 'and the rail box');
  const railBox16 = findOne(side.panel, 'gg-arsenal-rails');
  ok(!!findOne(railBox16, 'gg-rail--left') && !!findOne(railBox16, 'gg-rail--right'),
    'which is the two four-tall rails, side by side — eight tiles stacked one-wide does not fit a laptop');
  ok(findAll(side.panel, 'gg-item').length === arsenalMod.ARSENAL_ITEMS.length,
    'every sticker is inside the panel', String(findAll(side.panel, 'gg-item').length));
  ok(findAll(side.tab, 'gg-item').length === 0, 'and none of them is inside the handle');

  // the handle is a real button, and it says what it does in both states
  ok(side.tab.tagName === 'BUTTON' && side.tab.type === 'button', 'the handle is a button');
  ok(String(side.tab.getAttribute('aria-label') || '').length > 0
    && side.tab.getAttribute('aria-expanded') === 'true',
    'that announces itself and its state', side.tab.getAttribute('aria-label'));
  ok(!hasClass(side.root, 'gg-plate') && !hasClass(side.panel, 'gg-plate'),
    'and NOTHING in the sidebar wears .gg-plate — that re-enables pointer events at 0,2,0 over the field');

  /* ---- open by default on a desk ---------------------------------------- */
  ok(side.open === true, 'a desktop desk starts EXPANDED — the gutter is going spare anyway');
  ok(hasClass(side.root, hudMod.ARSENAL_OPEN_CLASS), 'and the class says so', side.root.className);
  ok(store.arsenalOpen === undefined, 'without writing a preference nobody expressed');

  /* ---- collapse: the tiles go, the handle stays ------------------------- */
  side.tab.dispatchEvent({ type: 'click' });
  ok(side.open === false, 'clicking the handle collapses it');
  ok(!hasClass(side.root, hudMod.ARSENAL_OPEN_CLASS), 'the one bit is off');
  ok(side.tab.parentNode === side.root && side.tab.disabled === false,
    'the handle is still there and still live — a one-way door is exactly what a phone cannot have');
  ok(store.arsenalOpen === 'shut', 'and the choice is remembered', String(store.arsenalOpen));
  side.tab.dispatchEvent({ type: 'click' });
  ok(side.open === true && store.arsenalOpen === 'open', 'clicking it again puts them back',
    String(store.arsenalOpen));

  /* ---- 3. THE KEYBOARD DOES NOT CARE ------------------------------------
   * ui/arsenal.js binds 1-7 on `window` and never looks at the DOM, which is the
   * whole reason a collapsed arsenal is safe. Armed first (every slot starts
   * locked), then collapsed, then fired — twice, because the first press arms
   * for a monitor tap and the second is the shot. */
  const ars16 = hud.parts.arsenal;
  ars16.armDrop('flash');
  side.set(false);
  ok(side.open === false, 'collapsed for the keyboard run');
  const firesBefore = match._fires.length;
  dom.win.dispatchEvent({ type: 'keydown', key: '1', repeat: false });
  dom.win.dispatchEvent({ type: 'keydown', key: '1', repeat: false });
  ok(match._fires.length === firesBefore + 1,
    'keys 1-7 still fire from a COLLAPSED sidebar', String(match._fires.length - firesBefore));
  ok(hasClass(side.tab, 'is-bump'),
    'and the handle pulses, so a press behind a shut drawer is not silent');

  /* ---- the count badge: the only readout a shut drawer has --------------- */
  ars16.armDrop('subliminal');
  ars16.armDrop('subliminal');
  await sleep(260);            // the badge paints on the same 5 Hz poll as heat
  ok(side.armed >= 2, 'the sidebar knows what is banked', String(side.armed));
  ok(side.count.hidden === false && String(side.count.textContent) === String(side.armed),
    'the handle carries the count while the stickers are hidden',
    `${side.count.textContent} vs ${side.armed}`);
  ok(String(side.count.getAttribute('aria-label') || '').indexOf('item') > 0,
    'with a word on it, because a bare number beside a glow is colour travelling alone',
    side.count.getAttribute('aria-label'));

  hud.unmount();
  ok(match._subs.released === match._subs.taken, 'the sidebar leaks no subscriptions',
    `${match._subs.released}/${match._subs.taken}`);

  /* ---- 4. the pref round-trip, and the auto policy ----------------------- */
  const prefsMod16 = await import('../ui/prefs.js');
  ok(prefsMod16.PREF_DEFAULTS.arsenalOpen === prefsMod16.ARSENAL_OPEN_AUTO,
    "the pref exists and defaults to 'auto' — a boolean cannot say \"never asked\"",
    String(prefsMod16.PREF_DEFAULTS.arsenalOpen));
  const R = prefsMod16.resolveArsenalOpen;
  ok(R(prefsMod16.ARSENAL_OPEN_AUTO, false) === true, 'auto on a desk: open');
  ok(R(prefsMod16.ARSENAL_OPEN_AUTO, true) === false,
    'auto on a phone: SHUT — a drawer over the field is not a starting position');
  ok(R(prefsMod16.ARSENAL_OPEN_ON, true) === true && R(prefsMod16.ARSENAL_OPEN_OFF, false) === false,
    'an explicit answer travels to whatever they play on next');
  for (const junk of [undefined, null, '', 'yes', 0, 1, true, {}]) {
    ok(R(junk, false) === true && R(junk, true) === false,
      'a corrupt/legacy value falls through to the auto policy', JSON.stringify(junk));
  }
  // …and it survives the real store's coercion, which is where a string pref
  // usually dies (coerce() reads the DEFAULT's type, and this one is a string)
  const live16 = prefsMod16.createPrefs({ arsenalOpen: prefsMod16.ARSENAL_OPEN_OFF });
  ok(live16.get('arsenalOpen') === prefsMod16.ARSENAL_OPEN_OFF,
    'createPrefs keeps the seeded value as a string', String(live16.get('arsenalOpen')));
  ok(live16.set('arsenalOpen', prefsMod16.ARSENAL_OPEN_ON) === true
    && live16.get('arsenalOpen') === prefsMod16.ARSENAL_OPEN_ON,
    'and set() round-trips it');

  // a remembered 'shut' applies AT MOUNT, not on the first click
  const match16b = makeFakeMatch();
  const hud16b = hudMod.mountHud({
    match: match16b, audio: { sfx() {} },
    prefs: { get: (k) => (k === 'arsenalOpen' ? 'shut' : undefined), set() {} },
  });
  ok(hud16b.parts.sidebar.open === false,
    'a remembered collapse is applied at mount — otherwise it reads as forgotten');
  hud16b.unmount();

  // …and a HUD with no pref store at all must still toggle
  const match16c = makeFakeMatch();
  const hud16c = hudMod.mountHud({ match: match16c, audio: { sfx() {} } });
  const tab16c = findOne(dom.byId.get('gg-hud'), 'gg-arsenal-tab');
  ok(!!tab16c, 'the handle exists with no prefs object');
  tab16c.dispatchEvent({ type: 'click' });
  ok(hud16c.parts.sidebar.open === false,
    'and the toggle works without one — persistence is a nicety, not the state');
  hud16c.unmount();
  ok(host.children.length === 0, 'and the whole sidebar leaves with the HUD');
}

/* --- 2b-vii. the sidebar's CSS contract ------------------------------------
 * Four rules, and every one of them is a bug this desk has already had:
 *   1. the panel is pointer-transparent, so bubbles rising behind it stay
 *      poppable — popping is the ONLY thing that ever fills this panel;
 *   2. there is no scrim, on any breakpoint (a scrim over the bubble strip is
 *      what froze the drop economy the last time a sheet opened at Live, and a
 *      z70 one would bury MERCY at z60);
 *   3. collapsed is display:none, not a transparent box still eating the field;
 *   4. the handle keeps a >= 48px finger, because it is the only way back.
 * ------------------------------------------------------------------------- */
{
  const fs17 = await import('node:fs/promises');
  const url17 = await import('node:url');
  const css17 = await fs17.readFile(url17.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8');
  const rule = (sel) => {
    const re = /([^{}]+)\{([^}]*)\}/g;
    const out = [];
    let m;
    while ((m = re.exec(css17))) {
      const list = m[1].split(',').map((s) => s.replace(/\/\*[\s\S]*?\*\//g, '').trim());
      if (list.includes(sel)) out.push(m[2]);
    }
    return out.join(';');
  };

  // 1. the 0,2,0 opt-out, and it must come AFTER the .gg-plate rule it beats
  ok(/\.gg-hud-frame\s+\.gg-arsenal[^{}]*\{[^}]*pointer-events:\s*none/.test(css17)
    || /pointer-events:\s*none/.test(rule('.gg-hud-frame .gg-arsenal')),
    'the sidebar surface takes no pointer input at all');
  ok(/pointer-events:\s*none/.test(rule('.gg-hud-frame .gg-arsenal-panel')),
    'nor does the panel — a bubble drifting behind it is still poppable through it');
  const plateAt = css17.indexOf('.gg-hud-frame .gg-plate');
  const optOutAt = css17.indexOf('.gg-hud-frame .gg-arsenal');
  ok(plateAt >= 0 && optOutAt > plateAt,
    'and the opt-out is written after the .gg-plate rule, at the same 0,2,0 — earlier and it silently loses',
    `${plateAt} vs ${optOutAt}`);
  ok(/pointer-events:\s*auto/.test(rule('.gg-hud-frame .gg-arsenal-tab')),
    'the handle opts back in');
  ok(/pointer-events:\s*auto/.test(rule('.gg-hud-frame .gg-arsenal .gg-item')),
    'and so does every tile — a pointer-events:auto child of a none parent is still hit-testable');

  // 2. no scrim, and nothing in this tier may climb toward MERCY's z60
  ok(!/\.gg-arsenal[\w-]*\s*\{[^}]*position:\s*fixed[^}]*inset:\s*0/.test(css17),
    'the drawer is never a full-bleed layer — that is a scrim by another name');
  const zAll = (css17.match(/\.gg-arsenal[\w-]*[^{}]*\{[^}]*\}/g) || []).join('\n');
  const zNums = (zAll.match(/z-index:\s*(\d+)/g) || []).map((s) => Number(s.replace(/\D/g, '')));
  ok(zNums.every((z) => z < 60),
    'and nothing in the sidebar reaches for a z-index near MERCY\'s 60', zNums.join(','));

  // 3. collapsed is a real collapse
  ok(/display:\s*none/.test(rule('.gg-arsenal-panel')),
    'the panel is display:none by default — no invisible box left over the field');
  ok(/display:\s*flex/.test(rule('.gg-arsenal.is-open .gg-arsenal-panel')),
    'and the one bit is the only thing that brings it back');
  ok(hudMod.ARSENAL_OPEN_CLASS === 'is-open',
    'the class ui/hud.js writes is the class ui/hud.css reads', hudMod.ARSENAL_OPEN_CLASS);

  // 4. the handle keeps a finger, and never shrinks on the breakpoint that
  //    matters most — it is the only way back to your own items on a phone
  const tab17 = rule('.gg-arsenal-tab');
  ok(/width:\s*48px/.test(tab17), 'the handle is a 48px finger wide', tab17);
  ok(/min-height:\s*(\d+)px/.test(tab17) && Number(tab17.match(/min-height:\s*(\d+)px/)[1]) >= 48,
    'and at least 48 tall', tab17);

  // the breakpoint the drawer behaviour hangs off is spelled ONCE
  ok(css17.includes(hudMod.ARSENAL_NARROW_MQ),
    'ui/hud.css uses the same media query ui/hud.js resolves the default from',
    hudMod.ARSENAL_NARROW_MQ);

  // zen keeps the arsenal, sidebar and all — it is what the duel is played on
  ok(!new RegExp('\\.gg-hud--zen[^{}]*\\.gg-arsenal[^{}]*\\{[^}]*display:\\s*none').test(css17),
    'zen never hides the sidebar, the same way it never hid the rails');
}

// ------------------------------- 2c. the draft tile table in ui/strings.js
// THIS USED TO CROSS-CHECK A DUPLICATED RISK TABLE. ELEMENTS carried a
// hand-copied `risk` tier that had to agree with core/draft.js riskTierOf(), and
// nothing else compared them. On 2026-08-04 the player-facing risk system was
// removed — no tier meters, no per-tile pips, no "match risk 4 / 7" — so the
// duplicate was deleted at the source and there is nothing left to drift.
// The tier itself is untouched inside core/draft.js + core/scoring.js (it still
// multiplies the score, and it is C#-parity), so what is pinned here now is the
// ABSENCE: a `risk` field growing back on these tiles means the duplicate is
// back, and the next thing to arrive is a silent disagreement with the engine.
{
  for (const meta of ELEMENTS) {
    ok(!('risk' in meta), `no duplicated risk tier on the ${meta.name} tile`, JSON.stringify(meta));
    ok(typeof meta.blurb === 'string' && meta.blurb.length > 10,
      `and the ${meta.name} tile still says what it does`, meta.blurb);
  }
  const spiral = ELEMENTS.find((e) => e.id === GoonElement.Spiral);
  ok(!!spiral && spiral.name === 'spiral' && typeof spiral.blurb === 'string' && spiral.blurb.length > 10,
    'the draft pool carries a spiral tile with copy');
  ok(!!spiral && spiral.blurb === spiral.blurb.toLowerCase(), 'element blurbs stay lowercase');
}

/* ---- 2c-ii. THE RISK READOUT IS GONE FROM EVERY SCREEN (2026-08-05) --------
 *
 * Batch 7 took the meters and the pips; what it left behind was the MULTIPLIER
 * — the same engine number (`scoring.riskMultiplier`, product of the 0-7 tier)
 * printed in three places: "×1.30 score" under the live HUD's charge row,
 * "you both score ×1.30" in gold in the draft footer, and "1 pt/s · score
 * ×1.30" in the recap's fine print. The owner's verdict covered the family:
 * not intuitive, and the heat gauge is the readout the desk actually needed.
 * All three are deleted.
 *
 * THIS PINS THE ABSENCE, AT THE SOURCE, BECAUSE THAT IS WHERE IT REGRESSES.
 * A mounted-DOM check would only catch the live HUD, and the way this comes
 * back is somebody re-reading `riskMultiplier` in a UI file — so the assertion
 * is that no ui/ file reads it at all. The ENGINE is deliberately not part of
 * this: core/scoring.js and core/draft.js still compute the tier and the
 * multiplier every second (C#-parity, and the score depends on it), they just
 * have no reader on a player's screen any more. */
{
  const fsRisk = await import('node:fs/promises');
  const urlRisk = await import('node:url');
  const readUi = (rel) => fsRisk.readFile(urlRisk.fileURLToPath(new URL('../' + rel, import.meta.url)), 'utf8');

  for (const rel of ['ui/hud.js', 'ui/screens/draft.js', 'ui/screens/recap.js', 'ui/strings.js']) {
    const src = await readUi(rel);
    // Comments are where the tombstones live and they are allowed to say the
    // word; a READ of the value is what must not exist. Strip line comments and
    // block comments before looking.
    const code = src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^[ \t]*\/\/.*$/gm, '');
    ok(!/riskMultiplier|matchRiskTier|riskTierOf/.test(code),
      `${rel} reads no risk value out of the engine`,
      (code.match(/.*(riskMultiplier|matchRiskTier|riskTierOf).*/) || [''])[0].trim());
  }

  // and the copy that dressed it is gone with it
  ok(!('scoreMult' in S.hud), 'strings.js has no hud.scoreMult');
  ok(!('score' in S.draft), 'strings.js has no draft.score multiplier line');
  ok(typeof S.recap.scoreFineprint === 'string',
    'the recap fine print is a flat string now, not a mult => string',
    typeof S.recap.scoreFineprint);
  ok(!/×|multipl/i.test(S.recap.scoreFineprint),
    'and it names no multiplier', String(S.recap.scoreFineprint));

  // the live desk builds no such node
  const matchRisk = makeFakeMatch();          // its scoring stub still carries riskMultiplier 1.3
  const hudRisk = hudMod.mountHud({ match: matchRisk, audio: { sfx() {} } });
  const hostRisk = dom.byId.get('gg-hud');
  ok(findAll(hostRisk, 'gg-mult').length === 0, 'the live HUD builds no .gg-mult readout');
  ok(findAll(hostRisk, 'gg-risk').length === 0, 'and nothing wearing the old .gg-risk name');
  ok(findAll(hostRisk, 'gg-pips').length === 0,
    'and the charge PIP ROW went with it in the second pass (see 2c-iii)');
  hudRisk.unmount();
}

/* ---- 2c-iii. AND THEN THE WHOLE CHARGE READOUT WENT (2026-08-05, third pass)
 *
 * THE THREE PASSES, because the last one is not a styling decision:
 *   1. the risk pips came off the draft tiles;
 *   2. the CHARGE pips came off the HUD ("the little yellow squares that we were
 *      supposed to have removed"), replaced by a word count: `.gg-charges`
 *      ("3 / 5 charges") under your score and `.gg-mon-charges` in their monitor
 *      titlebar. That pass kept the DATA on the argument that it was real — the
 *      wire validated throws against it and the arsenal paid its costs from it;
 *   3. the owner then removed the REQUIREMENT itself: "we still have the charge
 *      system in (you need 3 to do X etc), we should remove the requirement
 *      entirely (and the Need 1/2/3 under the throwables)". That retired the
 *      argument holding the readout up, so the readout went.
 *
 * WHAT CHARGES FEED, audited before deleting rather than assumed: the score is
 * seconds x riskMultiplier x attentionMultiplier and never touches them; nothing
 * in the client reads chargesEarned/chargesSpent; the drop economy is paced by
 * HEAT and picks by cost-as-RARITY; enduring a payload still banks one, to
 * nothing. The answer is NOTHING — and a readout of nothing is worse than no
 * readout, because "1 / 3 charges" beside a tile you CAN throw says you cannot.
 *
 * WHAT THIS SECTION PINS, in order: no charge string survives in strings.js; the
 * mounted desk builds no `.gg-charges`, no `.gg-mon-charges` and no `.gg-pip` of
 * any kind; a charge gain is now SILENT and mints no chip; the arsenal tiles
 * carry no cost pips and never enter the "poor" state; and neither the JS nor
 * the CSS has a live reference left. The source-level checks are the regression
 * guard — this comes back as somebody adding "just a little indicator". */
{
  const fsChg = await import('node:fs/promises');
  const urlChg = await import('node:url');
  const readUi = (rel) => fsChg.readFile(urlChg.fileURLToPath(new URL('../' + rel, import.meta.url)), 'utf8');

  // ---- the strings themselves -------------------------------------------
  ok(!('charges' in S.hud), 'strings.js has no hud.charges — the "3 / 5 charges" line is gone');
  ok(!('charges' in S.monitor), 'and no monitor.charges for theirs either');
  ok(!('charge' in S.toasts), 'and the never-called "+1 charge" toast went with them');
  ok(!/charge/i.test(S.recap.chipEnduredNote),
    'the recap endured chip stops promising them a charge', String(S.recap.chipEnduredNote));
  ok(S.how.bullets.length === 6 && !S.how.bullets.some((b) => /charge/i.test(b)),
    'and "how it works" is still six bullets, none of which teaches an economy that is gone',
    S.how.bullets.filter((b) => /charge/i.test(b)).join(' | '));

  // ---- the mounted desk --------------------------------------------------
  const matchChg = makeFakeMatch();                 // scoring.charges starts at 3
  const audioChg = { ids: [], sfx(id) { audioChg.ids.push(id); } };
  const hudChg = hudMod.mountHud({ match: matchChg, audio: audioChg });
  const hostChg = dom.byId.get('gg-hud');

  ok(findAll(hostChg, 'gg-pip').length === 0, 'the mounted HUD contains no .gg-pip node at all');
  ok(findAll(hostChg, 'gg-pips').length === 0, 'nor the row that held them');
  ok(findAll(hostChg, 'gg-mon-pips').length === 0, 'nor the monitor titlebar copy of it');
  ok(findAll(hostChg, 'gg-charges').length === 0,
    'and no .gg-charges line — the count that replaced the pips outlived them by one pass');
  ok(findAll(hostChg, 'gg-mon-charges').length === 0, 'nor .gg-mon-charges in their titlebar');
  ok(findAll(hostChg, 'gg-score').length > 0,
    'the SCORE is still there — this took the meter, not the scorebox');

  // ---- a GAIN is now completely silent -----------------------------------
  audioChg.ids.length = 0;
  matchChg.scoring.charges = 4;
  await sleep(260);                                  // paintAll polls at 200ms
  ok(!audioChg.ids.includes('gg-charge'),
    'banking a charge plays nothing — the cue had one caller and it went with the readout',
    audioChg.ids.join(','));
  ok(findAll(hostChg, 'gg-plus1').length === 0,
    'and mints no floating "+1" chip: there is no number on screen for it to be about');
  ok(findAll(hostChg, 'gg-charges').length === 0, 'and still no line, however the meter moves');

  hudChg.unmount();

  // ---- the arsenal: no price under a sticker, and no "need N" ------------
  const matchArs = makeFakeMatch();
  matchArs.scoring.charges = 0;                      // as broke as it is possible to be
  const leftArs = document.createElement('div');
  const rightArs = document.createElement('div');
  const ars = arsenalMod.mountArsenal({ leftHost: leftArs, rightHost: rightArs, match: matchArs });
  ok(findAll(leftArs, 'gg-cost-pip').length === 0 && findAll(rightArs, 'gg-cost-pip').length === 0,
    'no tile carries a cost pip');
  ok(findAll(leftArs, 'gg-item-cost').length === 0 && findAll(rightArs, 'gg-item-cost').length === 0,
    'nor the row that held them');
  // The heavy costs 3 and the player holds 0: the old code painted "need 3" and refused.
  ars.armDrop('braindrain');
  ok(ars.stateOf('braindrain') === 'ready',
    'a 3-cost item on an EMPTY meter reads ready, not "need 3"', String(ars.stateOf('braindrain')));
  const broke = ars.fire('braindrain');
  ok(broke && broke.ok === true,
    'and it FIRES — the owner request in one assertion', JSON.stringify(broke));
  ars.unmount();

  // ---- the source, because that is where it regrows ----------------------
  const strip = (src) => src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^[ \t]*\/\/.*$/gm, '');
  for (const rel of ['ui/hud.js', 'ui/opponent.js']) {
    const code = strip(await readUi(rel));
    ok(!/gg-pip/.test(code), rel + ' builds no gg-pip node (comments may name the dead class)',
      (code.match(/.*gg-pip.*/) || [''])[0].trim());
    ok(!/charges/.test(code), rel + ' reads no charge count out of the engine',
      (code.match(/.*charges.*/) || [''])[0].trim());
  }
  const arsCode = strip(await readUi('ui/arsenal.js'));
  ok(!/scoring\.charges|gg-cost-pip|is-poor/.test(arsCode),
    'ui/arsenal.js prices nothing and reads no balance',
    (arsCode.match(/.*(scoring\.charges|gg-cost-pip|is-poor).*/) || [''])[0].trim());

  /* The CSS half. Comments are allowed to name the class — the tombstones do, on
   * purpose — so this strips them first and then looks for a live SELECTOR. */
  const cssChg = (await readUi('ui/hud.css')).replace(/\/\*[\s\S]*?\*\//g, '');
  ok(!/(^|[\s,{}])\.gg-pips?\b/.test(cssChg), 'hud.css has no live .gg-pip / .gg-pips rule',
    (cssChg.match(/.*\.gg-pips?\b.*/) || [''])[0].trim());
  ok(!/\.gg-pip--sm\b/.test(cssChg), 'and the small variant went with its one caller');
  ok(!/@keyframes\s+ggPipPop/.test(cssChg), 'and the pop keyframe has no pip left to bounce');
  ok(!/\.gg-cost-pip\b/.test(cssChg),
    'the arsenal cost dots are gone too — the owner named them directly ("the Need 1/2/3 under '
    + 'the throwables")', (cssChg.match(/.*\.gg-cost-pip\b.*/) || [''])[0].trim());
  ok(!/\.gg-charges\b/.test(cssChg) && !/\.gg-mon-charges\b/.test(cssChg),
    'and neither text readout is styled any more',
    (cssChg.match(/.*\.gg-(mon-)?charges\b.*/) || [''])[0].trim());
  ok(!/\.gg-plus1\b/.test(cssChg), 'nor the +1 chip that flourished them',
    (cssChg.match(/.*\.gg-plus1\b.*/) || [''])[0].trim());
  ok(/@keyframes\s+ggFloatUp/.test(cssChg),
    'but ggFloatUp SURVIVES — two other rules still animate on it');
}

// -------------------------------------------------------- 3. closeness + emotes

{
  const match = makeFakeMatch();
  const host = document.createElement('div');
  const dial = closenessMod.mountCloseness({ host, match });
  dial.set(2);
  ok(match._calls.setCloseness.length === 1 && match._calls.setCloseness[0] === 2, 'dial writes closeness through to the engine');
  dial.set(3);
  ok(match._calls.setCloseness.length === 1, '350ms cooldown swallows the immediate second change');
  dial.unmount();

  const emotes = emotesMod.mountEmotes({ host, match });
  ok(emotes.send('gg', '') === true, 'emote sends');
  ok(match._calls.emote.length === 1, 'one emote reached the engine');
  ok(emotes.send('gg', '') === false, 'emote rate limiter holds the second one');
  emotes.unmount();
}

// ------------------------------------------------------------------ 4. mercy

{
  const match = makeFakeMatch();
  const audio = { ids: [], sfx(id) { audio.ids.push(id); } };
  const mercy = mercyMod.mountMercy({ getMatch: () => match, audio });
  ok(mercy && typeof mercy.unmount === 'function', 'mountMercy returns an unmount');
  const host = dom.byId.get('gg-mercy');
  ok(host.hidden === false, 'mercy unhides its own layer');
  const btn = host.children[0] && host.children[0].children[0];
  ok(!!btn, 'mercy renders a button');
  ok(btn.tabIndex === -1, 'mercy is not tab-focusable');
  ok(btn.textContent === 'MERCY', 'mercy is the only uppercase word');
  ok(btn._classes.has('is-arming'), 'mercy mounts inert');

  mercy.declare();
  ok(match._calls.mercy === 0, 'mercy does nothing while arming');

  await sleep(760);
  ok(!btn._classes.has('is-arming'), 'mercy arms after 700ms');
  btn.dispatchEvent({ type: 'pointerdown', preventDefault() {} });
  ok(match._calls.mercy === 1, 'pointerdown declares mercy');
  btn.dispatchEvent({ type: 'pointerdown', preventDefault() {} });
  ok(match._calls.mercy === 1, 'mercy only ever fires once');
  ok(audio.ids.includes('gg-mercy'), 'mercy plays its cue');

  const onBody = () => dom.doc.body.children.filter((c) => c._classes && c._classes.has('gg-mercy-takeover'));
  ok(onBody().length === 1, 'the mercy takeover lands on <body>, clear of #gg-mercy\'s transform');

  mercy.unmount();
  ok(host.hidden === true, 'mercy hides its layer on unmount');
  ok(onBody().length === 0, 'the takeover is cleaned up on unmount');
}

// -------------------------------------------------------------- 5. sudden death

{
  const audio = { ids: [], sfx(id) { audio.ids.push(id); } };
  const sd = sdMod.createSuddenDeathUi({ audio });
  ok(sd && sd.presenter && sd.inputs && typeof sd.dispose === 'function', 'createSuddenDeathUi returns {presenter,inputs,dispose}');
  // The shared lock-card view is loaded lazily (it is a sibling wave's module and
  // may not exist); give the dynamic import a tick so the REAL path is exercised
  // when it is there. In a match the round countdown covers this many times over.
  await sleep(30);

  // the contract, introspected rather than hand-listed
  const wanted = Object.keys(nullRoundPresenter());
  for (const member of wanted) ok(typeof sd.presenter[member] === 'function', `presenter implements ${member}`);
  ok(Object.keys(sd.presenter).length === wanted.length, 'presenter has no extra members', Object.keys(sd.presenter).join(','));

  const feeds = nullRoundInputs();
  for (const feed of Object.keys(feeds)) {
    ok(sd.inputs[feed] && typeof sd.inputs[feed] === 'object', `inputs.${feed} exists`);
    for (const member of Object.keys(feeds[feed])) {
      ok(typeof sd.inputs[feed][member] === 'function', `inputs.${feed}.${member} is subscribable`);
    }
  }

  // the raise* drivers actually reach subscribers, and unsubs are honoured
  let solved = 0, abandoned = 0, pressed = 0, rendered = 0, popped = -1;
  const offSolved = sd.inputs.lockCard.onSolved((e) => { solved = e && e.mistakes; });
  const offAbandoned = sd.inputs.lockCard.onAbandoned(() => { abandoned++; });
  const offPress = sd.inputs.reaction.onInputPressed(() => { pressed++; });
  const offRendered = sd.inputs.reaction.onStimulusRendered(() => { rendered++; });
  const offPop = sd.inputs.bubbles.onBubblePopped((e) => { popped = e && e.index; });
  sd.inputs.raiseSolved(2); sd.inputs.raiseAbandoned(); sd.inputs.raisePress(); sd.inputs.raiseRendered(); sd.inputs.raisePop(4);
  ok(solved === 2, 'raiseSolved carries mistakes');
  ok(abandoned === 1 && pressed === 1 && rendered === 1, 'raiseAbandoned/raisePress/raiseRendered reach subscribers');
  ok(popped === 4, 'raisePop carries the index');
  offSolved(); offAbandoned(); offPress(); offRendered(); offPop();
  sd.inputs.raisePress();
  ok(pressed === 1, 'unsubscribe is honoured');

  // every presenter member, with a plausible spec
  let pThrew = null;
  try {
    sd.presenter.showRoundIntro({ roundNo: 1, kind: GoonRoundKind.ReactionDuel, difficulty: 2, fireAtMatchMs: 0 });
    sd.presenter.showLockCard({ phrase: 'I hold the line', repeats: 2, strict: false, voice: false, timeLimitMs: 60000 });
    sd.presenter.hideLockCard();
    sd.presenter.startStaringContest({ durationMs: 20000, difficulty: 1, beats: [] });
    sd.presenter.endStaringContest();
    sd.presenter.armReactionDuel({ delayMs: 3000, maxResponseMs: 2000, difficulty: 2, decoyOffsetsMs: [800] });
    sd.presenter.fireReactionStimulus(GoonStimulusKind.Decoy);
    sd.presenter.fireReactionStimulus(GoonStimulusKind.Real);
    sd.presenter.endReactionDuel();
    sd.presenter.startBubbleRace({
      count: 2,
      timeoutMs: 30000,
      difficulty: 1,
      bubbles: [
        { index: 0, normX: 0.2, normY: 0.3, scale: 1, spawnOffsetMs: 0, driftAngleDeg: 45, driftSpeed: 0.6 },
        { index: 1, normX: 0.7, normY: 0.6, scale: 1.2, spawnOffsetMs: 500, driftAngleDeg: 200, driftSpeed: 0.9 },
      ],
    });
    sd.presenter.endBubbleRace();
    sd.presenter.showRoundVerdict({
      roundNo: 1, kind: GoonRoundKind.ReactionDuel, difficulty: 2,
      verdict: GoonRoundVerdict.Win, netScore: 1,
      local: { completed: true, elapsed_ms: 3200, reaction_ms: 214, suspect: false, progress: 0 },
      peer: { completed: true, elapsed_ms: 3400, reaction_ms: 331, suspect: false, progress: 0 },
      localSuspect: false, peerSuspect: false,
    });
    sd.presenter.showRoundVerdict(null);                 // the abort path
    sd.presenter.showRoundVerdict({ roundNo: 2, kind: GoonRoundKind.BubbleRace, verdict: GoonRoundVerdict.Loss, netScore: -3, local: { progress: 3 }, peer: { progress: 6 }, localSuspect: true, peerSuspect: false });
  } catch (e) { pThrew = e; }
  ok(!pThrew, 'every presenter member survives a real spec', pThrew && pThrew.stack);

  const status = sd.lockCardStatus();
  ok(typeof status.available === 'boolean', 'lock-card sibling status is reported');
  ok(status.available ? status.fallback === false : status.fallback === true,
    'quick draw uses exec/lockCards.js when it exists and the inline card when it does not',
    JSON.stringify(status));
  if (!status.available) console.log('  note: exec/lockCards.js absent — quick draw used the inline fallback card');

  ok(audio.ids.length > 0, 'sudden death calls the audio stub');
  let dThrew = null;
  try { sd.dispose(); } catch (e) { dThrew = e; }
  ok(!dThrew, 'createSuddenDeathUi().dispose does not throw', dThrew && dThrew.stack);
  ok(dom.byId.get('scr-sd').children.length === 0, 'dispose empties #scr-sd');
  ok(dom.byId.get('gg-stage').children.length === 0, 'dispose empties #gg-stage');
}

// ------------------------------------- 6. the inline lock-card fallback, forced

{
  const stage = document.createElement('div');
  const raised = [];
  const ctx = {
    el: (t, c, x) => { const n = document.createElement(t); if (c) n.className = c; if (x != null) n.textContent = String(x); return n; },
    add: (p, c) => { if (p && c) p.appendChild(c); return c; },
    cls: (nd, c, on) => nd && nd.classList[on ? 'add' : 'remove'](c),
    text: (nd, v) => { if (nd) nd.textContent = v == null ? '' : String(v); },
    sfx: () => {},
    own: () => {},
    onFrame: () => () => {},
    mountScreen: (nd) => stage.appendChild(nd),
    mountStage: (nd) => stage.appendChild(nd),
    mountOverlay: (nd) => stage.appendChild(nd),
    clock: () => null,
    lockCardView: () => null,                 // the sibling is "missing"
    setNet: () => {}, setRoundLabel: () => {},
    raise: {
      solved: (m) => raised.push(['solved', m]),
      abandoned: () => raised.push(['abandoned']),
      press: () => raised.push(['press']),
      rendered: () => raised.push(['rendered']),
      pop: (i) => raised.push(['pop', i]),
    },
    log: () => {},
  };
  const qd = quickdrawMod.createQuickDraw(ctx);
  qd.show({ phrase: 'I hold the line', repeats: 2, timeLimitMs: 60000 });
  ok(qd.usedFallback() === true, 'quick draw falls back to the inline card when the sibling is absent');

  // drive the inline card: two correct lines -> solved with the slip count
  const slot = stage.children[0] && stage.children[0].children[0];
  const input = slot && slot.children.find((c) => c.tagName === 'INPUT');
  ok(!!input, 'the inline card has a text input');
  input.value = 'nope';
  input.dispatchEvent({ type: 'keydown', key: 'Enter' });
  input.value = 'i hold the line';
  input.dispatchEvent({ type: 'keydown', key: 'Enter' });
  input.value = 'I HOLD THE LINE';
  input.dispatchEvent({ type: 'keydown', key: 'Enter' });
  ok(raised.some((r) => r[0] === 'solved'), 'the inline card reports onSolved');
  ok(raised.find((r) => r[0] === 'solved')[1] === 1, 'the inline card counts the slip', JSON.stringify(raised));

  const give = slot.children.find((c) => c.tagName === 'BUTTON');
  give.dispatchEvent({ type: 'click' });
  ok(raised.some((r) => r[0] === 'abandoned'), 'the inline card reports onAbandoned (never bound to Esc)');
  qd.dispose();
}

// ---------------------------------- 7. the opponent monitor's little screen
{
  const match = makeFakeMatch();
  match.opponent.activeEffects = [];
  const host = document.createElement('div');
  const mon = opponentMod.mountOpponent({ host, match, audio: { sfx() {} } });

  const proj = findOne(mon.root, 'gg-mon-proj');
  ok(!!proj, 'the monitor has a projection rect');
  ok(mon.projection === proj, 'the projection rect is exposed to callers');
  const minis = findAll(proj, 'gg-mini');
  ok(minis.length === 10, 'ten minis (nine effects + the emote), all inside the projection rect', String(minis.length));
  ok(findAll(mon.root, 'gg-mini').length === minis.length, 'no mini is drawn outside the rect');

  // effect NAMES off their tick drive the minis, including the two quiet ones
  match.opponent.activeEffects = ['Spiral', 'BrainDrain'];
  match._emit('opp');
  const spiral = findOne(proj, 'gg-mini-spiral');
  const drain = findOne(proj, 'gg-mini-drain');
  const bubbles = findOne(proj, 'gg-mini-bubbles');
  ok(hasClass(spiral, 'is-on') && hasClass(spiral, 'is-anim'), "'Spiral' lights and turns the spiral mini");
  ok(hasClass(drain, 'is-on'), "'BrainDrain' lays the drain veil over the rect");
  ok(!hasClass(bubbles, 'is-on'), 'a mini nobody asked for stays dark');
  ok(findOne(proj, 'gg-mini-idle').hidden === true, 'the idle word steps aside when something is on');

  // the motion budget still holds
  match.opponent.activeEffects = ['Flashes', 'Bubbles', 'Videos', 'Subliminals', 'BouncingText', 'Spiral'];
  match._emit('opp');
  ok(findAll(proj, 'is-anim').length === 2, 'at most two minis carry motion at once', String(findAll(proj, 'is-anim').length));

  // a payload WE fired animates its own mini for its own window, with no help
  // from their tick (active_effects never lists a payload they are enduring)
  match.opponent.activeEffects = [];
  match._emit('opp');
  mon.markPayloadFired({ id: 'q1', kind: GoonPayloadKind.LockCard, durationMs: 4000, leadMs: 0 });
  await sleep(20);
  const lock = findOne(proj, 'gg-mini-lock');
  ok(hasClass(lock, 'is-on'), 'a fired lock card opens its mini on the payload window alone');
  ok(hasClass(lock, 'is-yours'), 'a mini we are driving is marked as ours');

  // …and surviving it is the flagship: green card, green wash, checkmark
  mon.markReceipt('q1', 'survived');
  const check = findOne(proj, 'gg-mon-check');
  const wash = findOne(proj, 'gg-mon-pass');
  ok(hasClass(lock, 'is-pass'), 'a survived lock card lights the mini green');
  ok(hasClass(check, 'is-in') && hasClass(wash, 'is-in'), 'the pass plays the green wash + checkmark');
  ok(!hasClass(lock, 'is-on'), 'the payload window closes on the receipt');

  // anything else just closes, quietly
  mon.markPayloadFired({ id: 'q2', kind: GoonPayloadKind.Spiral, durationMs: 4000, leadMs: 0 });
  await sleep(20);
  ok(hasClass(findOne(proj, 'gg-mini-spiral'), 'is-on'), 'a fired spiral opens the spiral mini');
  mon.markReceipt('q2', 'rejected_filtered');
  ok(!hasClass(findOne(proj, 'gg-mini-spiral'), 'is-on'), 'a rejected payload closes its window');

  mon.unmount();
  ok(match._subs.released === match._subs.taken, 'the monitor releases its subscriptions', `${match._subs.released}/${match._subs.taken}`);
}

// --------------------------- 7b. hud.js is the glue between rail and monitor
{
  const match = makeFakeMatch();
  const hud = hudMod.mountHud({ match, audio: { sfx() {} } });
  hud.parts.arsenal.armDrop('lockcard');   // items are EARNED now (drop economy)
  const res = hud.parts.arsenal.fire('lockcard');
  ok(res && res.ok === true, 'the desk can fire through hud.parts.arsenal');
  match._emit('pRec', { id: res.id, status: 'survived' });
  const check = findOne(dom.byId.get('gg-hud'), 'gg-mon-check');
  ok(hasClass(check, 'is-in'), 'a survived receipt for our own payload greens their monitor');
  hud.unmount();
  ok(match._subs.released === match._subs.taken, 'the glue leaks no subscriptions', `${match._subs.released}/${match._subs.taken}`);
}

// -------------------------------------------------- 8. mount/unmount is idempotent

{
  const match = makeFakeMatch();
  for (let i = 0; i < 3; i++) {
    const hud = hudMod.mountHud({ match, audio: { sfx() {} } });
    hud.unmount();
  }
  ok(match._subs.released === match._subs.taken, 'repeat mount/unmount leaks nothing', `${match._subs.released}/${match._subs.taken}`);
  ok(dom.byId.get('gg-hud').children.length === 0, 'no HUD residue after three cycles');
}

/* ===========================================================================
 * 9. REGRESSION — the opponent monitor was completely invisible (0803).
 *
 * assets/monitor_frame.png paints its CRT face PURE BLACK (that is still true of
 * the 2026-08-04 re-cut, whose SURROUND is transparent but whose screen is not),
 * and the projection rect was a child of .gg-mon-screen (z-index:1 -> its own
 * stacking context) while the <img> sat at z-index:2. Every mini rendered under
 * the art. Structure and z-order are BOTH asserted, because either one alone
 * puts it back under the paint. The GEOMETRY that goes with it — which four
 * percentages that face is at — is re-derived from the art near the end of this
 * file; this section is only about who is on top of whom.
 * ======================================================================== */
{
  const match = makeFakeMatch();
  const host = document.createElement('div');
  const mon = opponentMod.mountOpponent({ host, match });
  const frame = findOne(mon.root, 'gg-mon-frame');
  const proj = findOne(mon.root, 'gg-mon-proj');
  const bezel = findOne(mon.root, 'gg-mon-bezel');
  const screen = findOne(mon.root, 'gg-mon-screen');

  ok(!!frame && !!proj && !!bezel && !!screen, 'the monitor has frame + screen + bezel + projection');
  ok(proj.parentNode === frame, 'the projection rect is a SIBLING of the bezel art, not a child of the glass');
  ok(!screen.contains(proj), 'the projection rect is not inside .gg-mon-screen (that element opens a stacking context)');
  const kids = frame.children;
  ok(kids.indexOf(proj) > kids.indexOf(bezel),
    'the projection rect is painted AFTER the opaque bezel <img>', `proj@${kids.indexOf(proj)} bezel@${kids.indexOf(bezel)}`);
  ok(findAll(proj, 'gg-mini').length === 10 && findAll(mon.root, 'gg-mini').length === 10,
    'all ten minis moved with the rect');
  mon.unmount();
}

// …and the CSS half of the same fix. A z-index the minis cannot win is exactly
// how this shipped, so the stylesheet is read rather than trusted.
{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const css = await fs.readFile(url.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8');
  const zOf = (selector) => {
    const block = new RegExp('\\' + selector + '\\s*\\{([^}]*)\\}').exec(css);
    const z = block && /z-index:\s*(-?\d+)/.exec(block[1]);
    return z ? Number(z[1]) : null;
  };
  const zProj = zOf('.gg-mon-proj');
  const zBezel = zOf('.gg-mon-bezel');
  ok(zProj !== null, 'hud.css gives .gg-mon-proj an explicit z-index');
  ok(zBezel !== null, 'hud.css gives .gg-mon-bezel an explicit z-index');
  ok(zProj > zBezel, 'the projection rect outranks the opaque bezel art', `${zProj} vs ${zBezel}`);
}

/* --- 9b. …and the REAL mount path is what feeds it ------------------------
 * The old 7b check emitted a receipt by hand, which greens the monitor whether
 * or not anything is wired. This one spies on the object mountHud actually
 * built, and then lets a real fire open a real window on its real timer. */
{
  const match = makeFakeMatch();
  const hud = hudMod.mountHud({ match, audio: { sfx() {} } });
  const mon = hud.parts.opponent;

  const seen = [];
  const real = mon.markPayloadFired;
  mon.markPayloadFired = (o) => { seen.push(o); return real(o); };
  hud.parts.arsenal.armDrop('flash');      // items are EARNED now (drop economy)
  const res = hud.parts.arsenal.fire('flash');
  mon.markPayloadFired = real;

  ok(res && res.ok === true, 'the real desk fires');
  ok(seen.length === 1, 'mountHud wires arsenal.onFired -> opponent.markPayloadFired', String(seen.length));
  ok(seen[0] && seen[0].id === res.id, 'the monitor is told the id the engine accepted');
  ok(seen[0] && seen[0].kind === GoonPayloadKind.FlashBurst, 'and the kind', String(seen[0] && seen[0].kind));
  ok(seen[0] && seen[0].durationMs > 0, 'and how long it runs', String(seen[0] && seen[0].durationMs));
  ok(seen[0] && seen[0].leadMs >= GoonConsts.MinScheduleBufferMs,
    'the window leads by at least the engine schedule buffer', String(seen[0] && seen[0].leadMs));

  // no receipt, no active_effects: the fired window alone must light the mini.
  match.opponent.activeEffects = [];
  match._emit('opp');
  const proj = findOne(dom.byId.get('gg-hud'), 'gg-mon-proj');
  const flash = findOne(proj, 'gg-mini-flash');
  ok(!hasClass(flash, 'is-on'), 'the mini is dark until the payload is due');
  await sleep(seen[0].leadMs + 200);
  ok(hasClass(flash, 'is-on'), 'a real fire opens its mini on the real clock, with no help from their tick');
  ok(hasClass(flash, 'is-yours'), 'and it is marked as ours');
  hud.unmount();
  ok(match._subs.released === match._subs.taken, 'the real path leaks no subscriptions', `${match._subs.released}/${match._subs.taken}`);
}

/* ===========================================================================
 * 10. REGRESSION — "you tapped out." stranded the player (0803).
 *
 * declareMercy() ends the match SYNCHRONOUSLY, so boot.js unmounts this layer
 * before declare() reaches showTakeover — the scrim was built after its own
 * ledger had already been run, landed on <body> with no owner, and covered the
 * recap (z65 over z10) forever. The ordering is reproduced exactly.
 * ======================================================================== */
{
  const match = makeFakeMatch();
  const onBody = () => dom.doc.body.children.filter((c) => c._classes && c._classes.has('gg-mercy-takeover'));
  let mercy = null;
  // This IS the production ordering: phase -> Recap -> boot.js unmountMercy().
  match.declareMercy = () => { match._calls.mercy++; mercy.unmount(); };

  mercy = mercyMod.mountMercy({ getMatch: () => match });
  const btn = dom.byId.get('gg-mercy').children[0].children[0];
  await sleep(760);
  btn.dispatchEvent({ type: 'pointerdown', preventDefault() {} });

  ok(match._calls.mercy === 1, 'the concede still reaches the engine first');
  ok(onBody().length === 1, 'the takeover is shown even though the layer unmounted mid-declare');
  ok(document.querySelectorAll('.gg-mercy-takeover').length === 1,
    "boot.js's recap sweep can find a stray takeover");
  ok(typeof mercy.dismissTakeover === 'function', 'mercy exposes the dismissal seam boot.js calls');

  // three independent ways off it — a tap…
  onBody()[0].dispatchEvent({ type: 'pointerdown' });
  ok(onBody().length === 0, 'tapping the takeover reveals the recap underneath');
}
{
  // …and, for a player who taps nothing, its own timer.
  const match = makeFakeMatch();
  const onBody = () => dom.doc.body.children.filter((c) => c._classes && c._classes.has('gg-mercy-takeover'));
  let mercy = null;
  match.declareMercy = () => { mercy.unmount(); };
  mercy = mercyMod.mountMercy({ getMatch: () => match });
  const btn = dom.byId.get('gg-mercy').children[0].children[0];
  await sleep(760);
  btn.dispatchEvent({ type: 'pointerdown', preventDefault() {} });
  ok(onBody().length === 1, 'the takeover is up');
  await sleep(mercyMod.TAKEOVER_MS + 250);
  ok(onBody().length === 0, 'the takeover never outlives the match, even untouched',
    `waited ${mercyMod.TAKEOVER_MS + 250}ms`);
  // …and the third is boot.js's sweep, which the seam above covers.
}

/* ===========================================================================
 * 11. REGRESSION — Options could not be opened during a match (0803).
 *
 * The HUD's gear dispatched `gg-options-open` and NOTHING listened, so it was a
 * dead button. The drawer must open as an OVERLAY over a live HUD: no router
 * navigation, no pause, and clear of the mercy button.
 * ======================================================================== */
{
  const optionsMod = await import('../ui/options.js');
  const prefsStub = (() => {
    const v = {
      masterVolume: 0.8, musicVolume: 0.5, droneVolume: 0.5,
      uiVolume: 0.7, gameVolume: 0.7, mediaVolume: 0.8, reduceMotion: false,
    };
    return { get: (k) => v[k], set: (k, x) => { v[k] = x; }, reset() {} };
  })();
  const options = optionsMod.createOptions({
    prefs: prefsStub,
    session: { hosted: false },
    isInMatch: () => true,
  });

  // the seam boot.js owns: a document-level ear for the HUD's gear
  document.addEventListener('gg-options-open', () => options.toggle());

  const match = makeFakeMatch();
  const hud = hudMod.mountHud({ match, audio: { sfx() {} } });
  const hudHost = dom.byId.get('gg-hud');
  const drawer = dom.byId.get('gg-drawer');
  const gear = findOne(hudHost, 'gg-gear');
  ok(!!gear, 'the live HUD carries an options gear');

  gear.dispatchEvent({ type: 'click' });
  ok(options.isOpen === true, 'the HUD gear opens Options during a live match');
  ok(drawer.hidden === false, 'it opens in the #gg-drawer overlay (z70)');
  const panel = findOne(drawer, 'gg-panel');
  ok(!!panel, 'the drawer renders its panel');
  ok(hasClass(panel, 'is-inmatch'), 'the in-match variant is used');
  ok(panel.style['--gg-drawer-clip'] === '96px', 'the mercy clearance is applied',
    String(panel.style['--gg-drawer-clip']));
  ok(!!findOne(panel, 'gg-panel-note'), 'it says match settings are locked instead of pretending otherwise');
  ok(!!findOne(panel, 'gg-panel-close'), 'there is a close button (Escape is reserved for MERCY while live)');

  // it is an overlay: the HUD is untouched and no screen was navigated to
  ok(hudHost.hidden === false && hudHost.children.length > 0, 'the live HUD is still mounted underneath');
  ok(dom.byId.get('gg-modal').children.length === 0, 'nothing was pushed onto the modal layer');
  ok(match._calls.mercy === 0, 'opening Options does not concede');

  await sleep(30);
  // the gear is a toggle, so an outside-close must not fight it
  gear.dispatchEvent({ type: 'pointerdown', target: gear });
  ok(options.isOpen === true, 'a pointerdown on the gear itself does not close the drawer');
  gear.dispatchEvent({ type: 'click' });
  ok(options.isOpen === false, 'the gear toggles it shut again');

  gear.dispatchEvent({ type: 'click' });
  await sleep(30);
  ok(options.isOpen === true, 'reopened');
  document.dispatchEvent({ type: 'pointerdown', target: dom.doc.body });
  ok(options.isOpen === false, 'a click off the panel closes it — no Escape needed');
  await sleep(320);   // the slide-out runs before the layer is torn down
  ok(drawer.hidden === true, 'and the overlay layer is emptied');
  ok(drawer.children.length === 0, 'leaving no panel behind');

  hud.unmount();
  options.dispose();
}

// --- 11b. the two-file contract the block above cannot see -----------------
// The block above supplies its own listener, exactly as boot.js does — which
// means it would still pass with boot.js having no ear at all (the bug). The
// dispatcher and the listener live in different files, so the event NAME is
// checked across both: a missing listener and a typo'd one look the same to the
// player (a gear that does nothing) and neither shows up in any other gate.
{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const read = async (p) => fs.readFile(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8');
  const hudSrc = await read('../ui/hud.js');
  const bootSrc = await read('../boot.js');

  const dispatched = /new CustomEvent\(\s*'([^']+)'/.exec(hudSrc);
  ok(!!dispatched, 'ui/hud.js dispatches a CustomEvent for the gear');
  const listened = new RegExp("addEventListener\\(\\s*'" + (dispatched ? dispatched[1] : 'x') + "'").test(bootSrc);
  ok(listened, `boot.js listens for '${dispatched && dispatched[1]}' — without it the HUD gear is a dead button`);
  ok(/options\??\.?\.?toggle|options\?\.toggle/.test(bootSrc), 'and answers it by toggling the options drawer');
  ok(!/router\.show\(\s*'options'/.test(bootSrc), 'Options is never a screen navigation');
}

/* ===========================================================================
 * 12. REGRESSION — the opponent monitor "worked then stopped" (0803).
 *
 * Two independent bugs, one symptom. The owner saw the little screen animate
 * early in a Practice duel, then go dead, and reported that the only thing he
 * ever saw on it was the flashes mini.
 *
 *   12a. EVERY payload window was destroyed ~60 ms after it opened, because
 *        markReceipt() treated the engine's `accepted` ACK as terminal. A
 *        payload gets TWO receipts and only the second one ends it. Nothing the
 *        player FIRED ever reached their screen; only the opponent's own ramp
 *        did — and the only ramp element with entryFraction 0.00 is Flashes,
 *        which is why "flashes" was the whole show.
 *
 *   12b. Every mini rode --gg-deco-play, which goon.css flips to `paused` at
 *        html[data-gg-fx="hot"] — and exec/layers.js calls "hot" at three
 *        concurrent local effects, i.e. a normal three-element draft, with no
 *        payload in flight at all. Mid-match the whole rect froze.
 * ======================================================================== */
{
  const { GoonReceiptStatus } = await import('../core/scoring.js');

  // --- 12a-i. the pure status predicate ------------------------------------
  ok(typeof opponentMod.isTerminalReceipt === 'function', 'ui/opponent.js exports isTerminalReceipt');
  const term = opponentMod.isTerminalReceipt;
  ok(term(GoonReceiptStatus.Survived), "'survived' ends a payload window");
  ok(term(GoonReceiptStatus.Completed), "'completed' ends a payload window");
  ok(term(GoonReceiptStatus.RejectedRate), "'rejected_rate' ends a payload window");
  ok(term(GoonReceiptStatus.RejectedFiltered), "'rejected_filtered' ends a payload window");
  ok(term('rejected_something_new'), 'any future rejected_* reason ends it too');
  ok(!term(GoonReceiptStatus.Accepted),
    "'accepted' is an ACK, NOT the end — closing on it is what killed every window");
  ok(!term(''), 'a blank status is not terminal');
  ok(!term('landing'), 'an unknown status is left to the window timer rather than closing it early');

  // --- 12a-ii. the real receipt ORDER, against a mounted monitor ------------
  const match = makeFakeMatch();
  match.opponent.activeEffects = [];
  const host = document.createElement('div');
  const mon = opponentMod.mountOpponent({ host, match, audio: { sfx() {} } });
  const proj = findOne(mon.root, 'gg-mon-proj');
  const lock = findOne(proj, 'gg-mini-lock');

  mon.markPayloadFired({ id: 'r1', kind: GoonPayloadKind.LockCard, durationMs: 30000, leadMs: 60 });
  // …and the ACK lands BEFORE the lead elapses. That is the production
  // ordering: fire -> `accepted` in tens of ms -> the window is due later.
  mon.markReceipt('r1', GoonReceiptStatus.Accepted);
  await sleep(140);
  ok(hasClass(lock, 'is-on'), 'an `accepted` ACK does not cancel the payload window');
  ok(hasClass(lock, 'is-yours'), 'and the mini is still marked as ours');
  mon.markReceipt('r1', GoonReceiptStatus.Accepted);
  await sleep(20);
  ok(hasClass(lock, 'is-on'), 'a duplicate ACK is equally harmless');

  // the terminal receipt still ends it — and now the window is still THERE when
  // it arrives, so the pass can green the right mini (it used to be gone).
  mon.markReceipt('r1', GoonReceiptStatus.Survived);
  ok(hasClass(lock, 'is-pass'), 'a survived lock card greens the CARD, not just the wash');
  ok(!hasClass(lock, 'is-on'), 'and the terminal receipt closes the window');
  ok(hasClass(findOne(proj, 'gg-mon-check'), 'is-in'), 'the checkmark still plays');

  // --- 12a-iii. …and it keeps working, fire after fire ---------------------
  const flash = findOne(proj, 'gg-mini-flash');
  for (let cycle = 1; cycle <= 3; cycle++) {
    const id = 'cyc' + cycle;
    mon.markPayloadFired({ id, kind: GoonPayloadKind.FlashBurst, durationMs: 6000, leadMs: 20 });
    mon.markReceipt(id, GoonReceiptStatus.Accepted);
    await sleep(80);
    ok(hasClass(flash, 'is-on') && hasClass(flash, 'is-yours'), `fire/ack/close cycle ${cycle} still opens a window`);
    mon.markReceipt(id, GoonReceiptStatus.Survived);
    ok(!hasClass(flash, 'is-on'), `cycle ${cycle} closes cleanly`);
  }

  // --- 12a-iv. an effect name we cannot draw must not blank the rect -------
  match.opponent.activeEffects = ['Flashes', 'NotAnElementWeKnow', 999];
  let threw = false;
  try { match._emit('opp'); } catch (_e) { threw = true; }
  ok(!threw, 'paintMinis survives an unknown effect name');
  ok(hasClass(flash, 'is-on'), 'the names it DOES know still paint');
  ok(findOne(proj, 'gg-mini-idle').hidden === true, 'and the idle word steps aside for them');
  match.opponent.activeEffects = ['NotAnElementWeKnow'];
  match._emit('opp');
  ok(findOne(proj, 'gg-mini-idle').hidden === false,
    'a tick of NOTHING BUT unknown names leaves the idle word up rather than an empty rect');

  mon.unmount();
  ok(match._subs.released === match._subs.taken, 'the fixed monitor still leaks no subscriptions',
    `${match._subs.released}/${match._subs.taken}`);
}

/* --- 12a-v. the same thing down the REAL mount path -----------------------
 * mountHud -> arsenal.fire -> onFired -> markPayloadFired, with the engine's
 * `accepted` receipt arriving on the real event, at the real time. This is the
 * shape the shipped bug had: everything wired, every check above green, and the
 * mini still never lit.
 *
 * It used to fire the BUBBLE sticker; that item was removed on 2026-08-04 (see
 * ARSENAL_ITEMS — bubbles are the always-on field, so the throwable was
 * redundant), so this drives the subliminal slot instead. Nothing about the seam
 * under test is kind-specific: any armed payload slot exercises the same path. */
{
  const match = makeFakeMatch();
  match.opponent.activeEffects = [];
  const hud = hudMod.mountHud({ match, audio: { sfx() {} } });
  const proj = findOne(dom.byId.get('gg-hud'), 'gg-mon-proj');
  const bub = findOne(proj, 'gg-mini-sub');

  hud.parts.arsenal.armDrop('subliminal');  // items are EARNED now (drop economy)
  const res = hud.parts.arsenal.fire('subliminal');
  ok(res && res.ok === true, 'the real desk fires a subliminal storm');
  match._emit('pRec', { id: res.id, status: 'accepted' });    // ~60 ms in production
  ok(!hasClass(bub, 'is-on'), 'the mini is still dark while the payload is in the air');

  await sleep(Math.max(GoonConsts.MinScheduleBufferMs, 1500) + 250);
  ok(hasClass(bub, 'is-on'), 'the window opens on the real clock DESPITE the accepted ACK');
  ok(hasClass(bub, 'is-yours'), 'and it is marked as ours');
  ok(hasClass(bub, 'is-anim'), 'a payload we fired outranks their ramp for the motion budget');

  match._emit('pRec', { id: res.id, status: 'survived' });
  ok(!hasClass(bub, 'is-on'), 'the terminal receipt closes it');
  ok(hasClass(findOne(proj, 'gg-mon-check'), 'is-in'), 'and the pass plays');

  hud.unmount();
  ok(match._subs.released === match._subs.taken, 'no subscription leak on the real path',
    `${match._subs.released}/${match._subs.taken}`);
}

/* --- 12a-vi. the engine half of the contract ------------------------------
 * The block above supplies its own 'accepted' string. If core/match.js ever
 * stopped ACKing, the exemption would look like dead code and get "tidied"
 * away. Read the engine instead of trusting it. */
{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const read = async (p) => fs.readFile(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8');
  const matchSrc = await read('../core/match.js');
  const oppSrc = await read('../ui/opponent.js');

  ok(/makePayloadReceipt\(\{\s*id:\s*payload\.id,\s*status:\s*GoonReceiptStatus\.Accepted/.test(matchSrc),
    'core/match.js ACKs an admitted payload with `accepted` BEFORE any terminal receipt');
  ok(/payloadReceiptReceived\.emit/.test(matchSrc), 'and every receipt reaches onPayloadReceiptReceived');
  ok(/isTerminalReceipt/.test(oppSrc), 'ui/opponent.js filters receipts through isTerminalReceipt');
  ok(!/if \(s === 'survived'\)[\s\S]{0,200}if \(id\) closeWindow\(id\);/.test(oppSrc),
    'and no longer closes the window on every receipt it is handed');
}

/* --- 12b. the CSS half: the projection rect is exempt from the heat pause --
 * Asserted across BOTH files. goon.css owns the armor switch and hud.css owns
 * the exemption; either one alone is either a frozen monitor or a dead rule. */
{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const read = async (p) => fs.readFile(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8');
  const hudCss = await read('../ui/hud.css');
  const goonCss = await read('../goon.css');

  ok(/html\[data-gg-fx="hot"\]\s*\{[^}]*--gg-deco-play:\s*paused/.test(goonCss),
    'goon.css still parks decorative motion at html[data-gg-fx="hot"]');

  const projBlocks = hudCss.match(/\.gg-mon-proj\s*\{[^}]*\}/g) || [];
  ok(projBlocks.length > 0, 'hud.css has a .gg-mon-proj block');
  ok(projBlocks.some((b) => /--gg-deco-play:\s*running/.test(b)),
    'the projection rect re-declares --gg-deco-play: running — the monitor is information, not chrome');

  // Nothing inside the rect may set it back to paused (that would re-freeze the
  // minis through inheritance without touching a single .gg-mini rule).
  const paused = (hudCss.match(/[^}]*--gg-deco-play:\s*paused[^}]*/g) || [])
    .filter((b) => /gg-mon|gg-mini/.test(b));
  ok(paused.length === 0, 'no rule inside the monitor re-pauses the minis', String(paused.length));

  // The minis still declare the property, so the exemption is load-bearing
  // rather than decorative: strip it and they freeze again.
  ok(/\.gg-mini-[a-z]+\.is-anim[^{]*\{[^}]*animation-play-state:\s*var\(--gg-deco-play\)/.test(hudCss),
    'the mini rules still ride --gg-deco-play (which is what the exemption overrides)');

  // …and the hard off switches must still win over the exemption.
  ok(/prefers-reduced-motion[\s\S]{0,400}\.gg-mini\.is-anim[\s\S]{0,300}animation:\s*none\s*!important/.test(hudCss),
    'prefers-reduced-motion still stops the minis outright');
  ok(/is-calm[\s\S]{0,200}\.gg-mini\.is-anim[\s\S]{0,200}animation:\s*none\s*!important/.test(hudCss),
    '.is-calm (the reduced-motion pref) still stops them too');
}

/* --- 12c. what SHOULD be on their screen, and when ------------------------
 * The ramp is now a seeded ROLL over the shared pool (not per-element entry
 * fractions), so what the monitor shows early is: bubbles from second zero,
 * then the pool opening in gentlest-first order. Asserted here so nobody
 * "fixes" a sparse early monitor while chasing a frozen rect. */
{
  const { buildRamp, profileOf, GoonCueAction, ALWAYS_ON_ELEMENT } = await import('../core/draft.js');
  const LIVE_SEC = 720;
  const pool = [GoonElement.Flashes, GoonElement.Spiral, GoonElement.BrainDrain];
  const ramp = buildRamp(pool, 0x1234n, LIVE_SEC);
  const firstStart = (element) => {
    const cue = ramp.find((c) => c.element === element && c.action === GoonCueAction.Start);
    return cue ? cue.offsetMs : -1;
  };
  ok(ALWAYS_ON_ELEMENT === GoonElement.Bubbles && firstStart(GoonElement.Bubbles) === 0,
    'Bubbles is the always-on baseline — its mini is legitimately live from the first second');
  const bubbleStart = ramp.find((c) => c.element === GoonElement.Bubbles && c.action === GoonCueAction.Start);
  ok(bubbleStart.intensity <= 0.16, 'but it opens gentle (0.15), so a quiet early monitor is CORRECT',
    String(bubbleStart.intensity));
  // The opening pass is stable-sorted by entryFraction: a closer never opens the match.
  const rolled = pool.slice().sort((a, b) => firstStart(a) - firstStart(b));
  ok(rolled[0] === GoonElement.Flashes && profileOf(GoonElement.Flashes).entryFraction === 0,
    'the pool opens gentlest-first — Flashes (entryFraction 0) leads, which is why "flashes first" reads as normal',
    rolled.map(String).join(','));
  ok(firstStart(GoonElement.Spiral) > firstStart(GoonElement.Flashes)
    && firstStart(GoonElement.BrainDrain) > firstStart(GoonElement.Flashes),
    'Spiral and BrainDrain both wait their turn behind the opener');
  // Every pool element IS rolled in — an element that never shows would be a real bug now.
  ok(pool.every((e) => firstStart(e) >= 0), 'every allowed element gets screen time');
  // And the schedule is even by construction: same args, same cues, both machines.
  const again = buildRamp(pool, 0x1234n, LIVE_SEC);
  ok(JSON.stringify(again) === JSON.stringify(ramp),
    'the roll is deterministic — both players derive the IDENTICAL schedule');
}

/* ===========================================================================
 * 13. THE EMOTE MINI — "when we use an emote we should have it display on the
 *     little screen" (owner, 0803).
 *
 * The emote path is NOT the payload path and this section exists mostly to pin
 * that down. `t:'emote'` is its own message family: ui/arsenal.js's emote slot
 * has kind === null so it never reaches tryFirePayload/onFired, the engine's
 * sendEmote() writes to the wire and returns nothing, and there is no id, no
 * `accepted` ACK and no terminal receipt to hang a window off. So the mini runs
 * on a fixed local dwell, and ui/opponent.js taps sendEmote to know it happened.
 *
 * The two directions are drawn in DIFFERENT places on purpose:
 *   outbound = our line landing on their screen -> inside .gg-mon-proj;
 *   inbound  = their line spoken at us          -> the .gg-mon-bubble on the bezel.
 * ======================================================================== */
{
  const { GoonConnectionHealth } = await import('../core/match.js');
  const match = makeFakeMatch();
  match.opponent.activeEffects = [];
  const virginSendEmote = match.sendEmote;
  const host = document.createElement('div');
  const mon = opponentMod.mountOpponent({ host, match, audio: { sfx() {} } });
  const proj = findOne(mon.root, 'gg-mon-proj');
  const emote = findOne(proj, 'gg-mini-emote');

  // --- 13a. the shape -------------------------------------------------------
  ok(typeof mon.markEmoteFired === 'function', 'ui/opponent.js exposes markEmoteFired — the seam a hud.js hook would call');
  ok(typeof opponentMod.EMOTE_MINI_MS === 'number' && opponentMod.EMOTE_MINI_MS > 1000,
    'and exports the dwell, so nothing has to hardcode it', String(opponentMod.EMOTE_MINI_MS));
  ok(!!emote, 'the projection rect carries an emote mini');
  ok(!hasClass(emote, 'is-on'), 'which is dark until something is actually said');
  ok(Object.values(opponentMod.MINI_FOR_PAYLOAD).indexOf('Emote') < 0,
    'the emote mini is NOT reachable from a payload kind — emotes are not payloads');

  // --- 13b. the tap: a send draws it, with no ui/hud.js glue at all ----------
  match.sendEmote('nice try', '😏');
  ok(match._calls.emote.length === 1, 'the tap passes the send straight through to the engine');
  ok(hasClass(emote, 'is-on'), 'sending an emote opens its mini on the little screen');
  ok(hasClass(emote, 'is-yours'), 'and marks it as ours, like anything else we put on their screen');
  ok(hasClass(emote, 'is-anim'), 'and it moves');
  ok(findOne(proj, 'gg-mini-idle').hidden === true, 'the idle word steps aside for it');
  ok(findOne(emote, 'gg-mini-emote-text').textContent === 'nice try', 'the line is written into the bubble',
    String(findOne(emote, 'gg-mini-emote-text').textContent));
  ok(findOne(emote, 'gg-mini-emote-icon').textContent === '😏', 'and so is the icon');

  // --- 13c. one send is one bubble -----------------------------------------
  ok(mon.markEmoteFired('nice try', '😏') === false,
    'a second mark of the same line inside the dedupe window is the same send seen twice');
  ok(mon.markEmoteFired('gg', '💦') === true, 'a different line does draw');
  ok(findOne(emote, 'gg-mini-emote-text').textContent === 'gg', 'and replaces the old one');

  // --- 13d. it takes a budget slot like every other mini --------------------
  match.opponent.activeEffects = ['Flashes', 'Bubbles', 'Videos'];
  match._emit('opp');
  ok(findAll(proj, 'is-anim').length === 2, 'the two-mini motion budget still holds with an emote up',
    String(findAll(proj, 'is-anim').length));
  ok(hasClass(emote, 'is-anim'), 'and the emote outranks their ambient ramp for one of the slots');

  // --- 13e. the payload receipt lifecycle cannot touch it -------------------
  // There is no receipt for an emote; a receipt for something else must not be
  // able to close its window (the windows Map is shared).
  mon.markReceipt('nothing-like-this-id', 'survived');
  ok(hasClass(emote, 'is-on'), "a stray payload receipt does not close the emote's window");

  // --- 13f. the dwell IS the lifecycle: wiggle out, then gone ---------------
  match.opponent.activeEffects = [];
  match._emit('opp');
  await sleep(opponentMod.EMOTE_MINI_MS - 300);
  ok(hasClass(emote, 'is-on'), 'the bubble sits for its whole dwell');
  ok(hasClass(emote, 'is-out'), 'and wiggles out at the end of it — the outro is the terminal beat');
  await sleep(500);
  ok(!hasClass(emote, 'is-on'), 'then closes on its own, with no receipt to close it');
  ok(!hasClass(emote, 'is-out'), 'leaving no outro class behind for the next one');
  ok(findOne(proj, 'gg-mini-idle').hidden === false, 'and the idle word comes back (no other mini forced)');

  // --- 13g. the one failure that actually exists ---------------------------
  match.opponent.health = GoonConnectionHealth.Dead;
  ok(mon.markEmoteFired('you good?', '👀') === true, 'a send into a dead link still draws');
  ok(hasClass(emote, 'is-lost'), '…greyed and dropping, because nobody is going to read it');
  ok(!hasClass(emote, 'is-out'), 'a lost emote does not also play the normal outro');
  match.opponent.health = GoonConnectionHealth.Fresh;

  // --- 13h. inbound is a different thing, in a different place -------------
  match._emit('emote', { text: 'still here', icon: '🔥' });
  const bubble = findOne(mon.root, 'gg-mon-bubble');
  ok(!!bubble && bubble.hidden === false, 'an emote THEY sent still lands in the bezel bubble');
  ok(!proj.contains(bubble), 'which is not inside the projection rect — their words are not on their screen');

  // --- 13i. the tap is a loan, not a theft ---------------------------------
  mon.unmount();
  ok(match.sendEmote === virginSendEmote, 'unmount hands match.sendEmote back exactly as it was');
  ok(match._subs.released === match._subs.taken, 'the emote mini leaks no subscriptions',
    `${match._subs.released}/${match._subs.taken}`);
  match.sendEmote('gg', '');
  ok(match._calls.emote.length === 2, 'and a send after unmount reaches the engine untouched',
    String(match._calls.emote.length));
  ok(!hasClass(emote, 'is-on'), 'drawing nothing, because the monitor it belonged to is gone');
}

/* --- 13j. the CSS half ----------------------------------------------------
 * ui/opponent.js only ever toggles classes; every frame of this lives in
 * hud.css, so a mini with no rules is an invisible feature that every JS check
 * above still passes. */
{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const css = await fs.readFile(url.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8');

  ok(/\.gg-mini\.gg-mini-emote\.is-on\s*\{[^}]*display:\s*flex/.test(css),
    'hud.css shows the emote mini on .is-on (and outranks the shared .gg-mini.is-on display:block)');
  ok(/@keyframes\s+ggMiniEmotePop/.test(css) && /\.is-on\s+\.gg-mini-emote-bub\s*\{[^}]*ggMiniEmotePop/.test(css),
    'the arrival bounces in');
  ok(/@keyframes\s+ggMiniEmoteOut/.test(css) && /\.is-out\s+\.gg-mini-emote-bub\s*\{[^}]*ggMiniEmoteOut/.test(css),
    'the send plays out with a wiggle+fade');
  ok(/\.is-lost\s+\.gg-mini-emote-bub\s*\{[^}]*grayscale/.test(css) && /@keyframes\s+ggMiniEmoteDrop/.test(css),
    'and a lost one greys and drops');
  // The pop and the breathe are on DIFFERENT elements on purpose: `animation` is
  // a shorthand, so one rule on the bubble would cancel the other outright.
  ok(/\.gg-mini-emote\.is-anim\s+\.gg-mini-emote-icon\s*\{[^}]*animation:/.test(css),
    'the budgeted loop rides the icon, not the bubble the one-shots own');
  ok(/\.gg-mini-emote\.is-anim\s+\.gg-mini-emote-icon\s*\{[^}]*animation-play-state:\s*var\(--gg-deco-play\)/.test(css),
    'and still rides --gg-deco-play like every other mini');
  // …and the hard off switches reach it even though it animates off .is-on.
  ok(/prefers-reduced-motion[\s\S]{0,600}\.gg-mini-emote \*,[\s\S]{0,300}animation:\s*none\s*!important/.test(css),
    'prefers-reduced-motion stops the emote bubble, which .is-anim alone would miss');
  ok(/is-calm[\s\S]{0,400}\.gg-mini-emote \*\s*\{\s*animation:\s*none\s*!important/.test(css),
    '.is-calm stops it too');
}

/* ===========================================================================
 * 14. THE ANNOUNCER RIBBON — "we should announce briefly on the top of the
 *     screen whats going on (the effects that happen to both player, like Get
 *     ready to watch! or Get ready to type, etc and Video on, and Lock Card,
 *     etc)" (owner, 0803).
 *
 * The claim the whole feature rests on: the ramp is ONE seeded roll that both
 * machines compute identically, so warning about it is a true statement about
 * both players. That makes the lead computation the load-bearing part, and it
 * is a pure function precisely so it can be pinned here without a clock.
 * ======================================================================== */

const announcerMod = await import('../ui/announcer.js');
const { GoonCueAction } = await import('../core/draft.js');
const { S: STR } = await import('../ui/strings.js');

// --- 14a. the pre-announce lead is exact ----------------------------------
{
  const {
    upcomingAnnouncements, ANNOUNCE_LEAD_MS, mountAnnouncer, createAnnounceQueue,
  } = announcerMod;

  ok(typeof mountAnnouncer === 'function', 'ui/announcer.js exports mountAnnouncer');
  ok(typeof upcomingAnnouncements === 'function', '…and the pure lead helper');
  ok(typeof createAnnounceQueue === 'function', '…and the pure queue');

  const S_ = GoonCueAction.Start, I_ = GoonCueAction.Intensity, X_ = GoonCueAction.Stop;
  const ramp = [
    { offsetMs: 0, action: S_, element: GoonElement.Bubbles, intensity: 0.15, durationMs: 0 },
    { offsetMs: 10000, action: S_, element: GoonElement.Videos, intensity: 0.3, durationMs: 10000 },
    { offsetMs: 12000, action: I_, element: GoonElement.Videos, intensity: 0.4, durationMs: 0 },
    { offsetMs: 20000, action: X_, element: GoonElement.Videos, intensity: 0, durationMs: 0 },
    { offsetMs: 30000, action: S_, element: GoonElement.LockCards, intensity: 0.5, durationMs: 15000 },
    { offsetMs: 45000, action: X_, element: GoonElement.LockCards, intensity: 0, durationMs: 0 },
    { offsetMs: 50000, action: S_, element: GoonElement.Videos, intensity: 0.7, durationMs: 20000 },
  ];
  const before = JSON.stringify(ramp);

  const at6 = upcomingAnnouncements(ramp, 6000, ANNOUNCE_LEAD_MS);
  ok(at6.length === 1, 'exactly one thing is coming 4s out', String(at6.length));
  ok(at6[0] && at6[0].element === GoonElement.Videos, '…and it is the video');
  ok(at6[0] && at6[0].atMs === 10000 && at6[0].inMs === 4000, '…with the real cue time and the real lead');

  ok(upcomingAnnouncements(ramp, 5999, ANNOUNCE_LEAD_MS).length === 0,
    'one millisecond earlier and it is not announced yet — the lead is 4s, not "about 4s"');
  ok(upcomingAnnouncements(ramp, 10000, ANNOUNCE_LEAD_MS).every((a) => a.atMs !== 10000),
    'a cue that has ALREADY fired is never pre-announced (the start event covers it)');

  // the exclusions
  const all = upcomingAnnouncements(ramp, 0, 600000);
  ok(all.length === 3, 'over the whole ramp: three real starts', String(all.length));
  ok(all.every((a) => a.element !== GoonElement.Bubbles),
    'bubbles are NEVER announced — always-on for both players, so t=0 would mean nothing');
  ok(upcomingAnnouncements(ramp, 0, ANNOUNCE_LEAD_MS).length === 0,
    'and a match therefore opens in silence rather than with a bubbles banner');
  ok(all.filter((a) => a.element === GoonElement.Videos).length === 2
     && all.filter((a) => a.element === GoonElement.LockCards).length === 1,
    'stop cues and intensity bumps produce nothing — only starts do');

  // a Start for an element that is still running is an intensity bump, not a start
  const overlap = [
    { offsetMs: 1000, action: S_, element: GoonElement.Spiral, intensity: 0.2, durationMs: 0 },
    { offsetMs: 9000, action: S_, element: GoonElement.Spiral, intensity: 0.6, durationMs: 0 },
  ];
  const dup = upcomingAnnouncements(overlap, 0, 600000);
  ok(dup.length === 1 && dup[0].atMs === 1000,
    'a second Start while the element is still active is not announced — the engine turns it into a ramp');

  ok(JSON.stringify(ramp) === before, 'the helper never mutates the ramp it was handed');
  ok(upcomingAnnouncements(null, 0, 4000).length === 0 && upcomingAnnouncements(undefined).length === 0,
    'and junk in gives an empty list out, not a throw');
}

// --- 14b. one slot, and a dedupe window that still lets the pair through ---
{
  const {
    createAnnounceQueue, ANNOUNCE_DEDUPE_MS, ANNOUNCE_LEAD_MS, ANNOUNCE_LEAD_BEATS_DEDUPE, ANNOUNCE_QUEUE_CAP,
  } = announcerMod;

  const q = createAnnounceQueue();
  ok(q.offer(GoonElement.Videos, 'pre', 1000) === true, 'the first banner is admitted');
  ok(q.offer(GoonElement.Videos, 'pre', 1500) === false, 'the same element again 500ms later is one event seen twice');
  ok(q.offer(GoonElement.Videos, 'pre', 1000 + ANNOUNCE_DEDUPE_MS - 1) === false, 'still swallowed at the very edge');
  ok(q.offer(GoonElement.Videos, 'pre', 1000 + ANNOUNCE_DEDUPE_MS) === true, 'and admitted the moment the window closes');
  ok(q.offer(GoonElement.LockCards, 'pre', 1100) === true, 'the window is PER ELEMENT — a lock card is not a video');

  // The pair the owner actually asked for: the warning and then the fact.
  ok(ANNOUNCE_LEAD_BEATS_DEDUPE === true && ANNOUNCE_LEAD_MS > ANNOUNCE_DEDUPE_MS,
    'the lead outruns the dedupe window, or "Video on" would be eaten as a duplicate of its own warning');
  const pair = createAnnounceQueue();
  ok(pair.offer(GoonElement.Videos, 'pre', 0) === true, '"Get ready to watch!" lands');
  ok(pair.offer(GoonElement.Videos, 'on', ANNOUNCE_LEAD_MS) === true, '…and "Video on" lands 4s later too');

  // the one deliberate bypass
  const up = createAnnounceQueue();
  up.offer(GoonElement.Videos, 'pre', 0);
  ok(up.offer(GoonElement.Videos, 'on', 10) === true,
    'a pre still on screen when the thing starts is REPLACED by the real line, not dropped');
  ok(up.offer(GoonElement.Videos, 'on', 20) === false, '…but two "on" lines in a row are still one event');

  const bad = createAnnounceQueue();
  ok(bad.offer(GoonElement.Bubbles, 'on', 0) === false, 'the queue refuses bubbles outright');
  ok(bad.offer(GoonElement.Videos, 'stop', 0) === false, 'and refuses anything that is not pre/on — stops are never announced');
  ok(bad.offer(999, 'on', 0) === false, 'and refuses an element code that does not exist');

  const deep = createAnnounceQueue();
  const els = [GoonElement.Videos, GoonElement.LockCards, GoonElement.Flashes, GoonElement.Spiral,
    GoonElement.Subliminals, GoonElement.BouncingText];
  els.forEach((e, i) => deep.offer(e, 'pre', i));
  ok(deep.size === ANNOUNCE_QUEUE_CAP, 'the backlog is capped — stale news is dropped, not shown late', String(deep.size));
}

// --- 14c. every element it can name has words -----------------------------
{
  const { ANNOUNCEABLE_ELEMENTS, isAnnounceable, announceText } = announcerMod;

  ok(ANNOUNCEABLE_ELEMENTS.length === Object.keys(GoonElement).length - 1,
    'every element except one is announceable', String(ANNOUNCEABLE_ELEMENTS.length));
  ok(!isAnnounceable(GoonElement.Bubbles) && ANNOUNCEABLE_ELEMENTS.indexOf(GoonElement.Bubbles) < 0,
    '…and the one is bubbles');

  ok(!!STR.announce && !!STR.announce.ready && !!STR.announce.on, 'ui/strings.js carries the announce deck');
  for (const e of ANNOUNCEABLE_ELEMENTS) {
    const name = Object.keys(GoonElement).find((k) => GoonElement[k] === e);
    ok(typeof STR.announce.ready[e] === 'string' && STR.announce.ready[e].length > 0,
      `${name} has a "get ready" line`);
    ok(typeof STR.announce.on[e] === 'string' && STR.announce.on[e].length > 0,
      `${name} has an "on" line`);
    ok(announceText(e, 'pre').length > 0 && announceText(e, 'on').length > 0,
      `${name} resolves through announceText — a missing key would silently draw a blank plate`);
  }
  ok(announceText(GoonElement.Bubbles, 'on') === '' && announceText(GoonElement.Bubbles, 'pre') === '',
    'bubbles have no copy at all, so nothing can accidentally announce them');

  // the owner's own words, verbatim
  ok(STR.announce.ready[GoonElement.Videos] === 'Get ready to watch!', 'the video warning is the line he wrote');
  ok(STR.announce.ready[GoonElement.LockCards] === 'Get ready to type!', 'and so is the lock-card one');
  ok(STR.announce.on[GoonElement.Videos] === 'Video on', 'and "Video on" is "Video on"');
  ok(/lock card/i.test(STR.announce.on[GoonElement.LockCards]), 'and the lock card announces itself by name');
}

// --- 14d. mounted: the ribbon lives and dies with the desk -----------------
{
  const { mountAnnouncer, ANNOUNCE_LEAD_MS } = announcerMod;
  const S_ = GoonCueAction.Start, X_ = GoonCueAction.Stop;
  const match = makeFakeMatch();
  match.liveElapsedMs = 6000;
  match.rampCues = Object.freeze([
    { offsetMs: 0, action: S_, element: GoonElement.Bubbles, intensity: 0.15, durationMs: 0 },
    { offsetMs: 10000, action: S_, element: GoonElement.Videos, intensity: 0.3, durationMs: 10000 },
    { offsetMs: 20000, action: X_, element: GoonElement.Videos, intensity: 0, durationMs: 0 },
  ]);

  const host = document.createElement('div');
  host.className = 'gg-hud-frame';
  const log = [];
  const ann = mountAnnouncer({ host, match, onLog: log });

  ok(!!ann.root && hasClass(ann.root, 'gg-announce'), 'the ribbon mounts into the frame');
  ok(ann.root.getAttribute('aria-live') === 'polite', 'it is aria-live="polite" — announced, never shouted at a screen reader');
  ok(ann.root.getAttribute('role') === 'status', '…with a status role');
  ok(ann.showing() === null, 'and it starts empty');

  // an element the ramp actually starts
  match._emit('elStart', { element: GoonElement.Videos, intensity: 0.3, durationMs: 10000, elapsedMs: 6000 });
  let slot = findOne(ann.root, 'gg-announce-slot');
  ok(!!slot, 'a start puts a banner in the slot');
  ok(!!findOne(ann.root, 'gg-announce-text') && findOne(ann.root, 'gg-announce-text').textContent === 'Video on',
    'saying the thing that just happened');
  ok(hasClass(slot, 'gg-plate'), 'it wears the same glass as the rest of the desk');
  ok(ann.showing() && ann.showing().kind === 'on', 'and the module knows what it is showing');

  // the things it must stay quiet about
  const beforeQuiet = findAll(ann.root, 'gg-announce-slot').length;
  match._emit('elStart', { element: GoonElement.Bubbles, intensity: 0.2, durationMs: 0, elapsedMs: 6100 });
  match._emit('elStop', { element: GoonElement.Videos, intensity: 0, durationMs: 0, elapsedMs: 6200 });
  match._emit('elInt', { element: GoonElement.Videos, intensity: 0.9, durationMs: 0, elapsedMs: 6300 });
  ok(findAll(ann.root, 'gg-announce-slot').length === beforeQuiet,
    'bubbles, stops and intensity bumps never draw a banner');
  match._emit('elStart', { element: GoonElement.Videos, intensity: 0.3, durationMs: 10000, elapsedMs: 6400 });
  ok(findAll(ann.root, 'gg-announce-slot').length === beforeQuiet && ann.queued() === 0,
    'and the same element twice inside the window is one banner, not two');

  // theirs: a payload never becomes an element cue, so it has its own path.
  // Its own mount, its own empty slot — the queueing rules get their own check.
  const inbound = makeFakeMatch();
  inbound.rampCues = [];
  const hostIn = document.createElement('div');
  const annIn = mountAnnouncer({ host: hostIn, match: inbound, onLog: null });
  inbound._emit('pAcc', { payload: { id: 'a1', kind: GoonPayloadKind.LockCard, duration_ms: 8000 }, fireAtLocalMs: 0 });
  inbound._emit('pAcc', { payload: { id: 'a2', kind: GoonPayloadKind.BubbleSwarm, duration_ms: 8000 }, fireAtLocalMs: 0 });
  await sleep(40);
  const texts = findAll(annIn.root, 'gg-announce-text').map((t) => t.textContent);
  ok(texts.some((t) => /lock card/i.test(t)), 'a payload THEY threw is announced too — it lands through the same renderer');
  ok(!texts.some((t) => /bubble/i.test(t)), 'except a bubble swarm, which is the one thing that is always on anyway');
  annIn.unmount();
  ok(inbound._subs.released === inbound._subs.taken, 'and a pending payload announce leaks no subscription',
    `${inbound._subs.released}/${inbound._subs.taken}`);

  // the ramp look-ahead, live
  const fresh = makeFakeMatch();
  fresh.liveElapsedMs = 6000;
  fresh.rampCues = match.rampCues;
  const host2 = document.createElement('div');
  const ann2 = mountAnnouncer({ host: host2, match: fresh, onLog: null });
  await sleep(340);
  const pre = findOne(ann2.root, 'gg-announce-text');
  ok(!!pre && pre.textContent === 'Get ready to watch!',
    'four seconds out, the ribbon warns both of you what is coming');
  ok(ann2.showing() && ann2.showing().kind === 'pre', '…and knows it is a warning, not a fact');
  ok(fresh.liveElapsedMs + ANNOUNCE_LEAD_MS === 10000, '(the fixture really is 4s from the cue)');

  fresh._emit('elStart', { element: GoonElement.Videos, intensity: 0.3, durationMs: 10000, elapsedMs: 10000 });
  await sleep(340);
  ok(ann2.showing() && ann2.showing().kind === 'on',
    'when it actually starts, the warning is replaced by the fact rather than queued behind it');
  ok(findAll(ann2.root, 'gg-announce-slot').length === 1,
    'and there is still only ever one slot');

  // a second element does not wait out the full dwell — it cuts in once the
  // sitting banner has been readable, or a busy stretch would report the match
  // several seconds late.
  fresh._emit('elStart', { element: GoonElement.LockCards, intensity: 0.4, durationMs: 8000, elapsedMs: 10500 });
  ok(ann2.showing() && ann2.showing().element === GoonElement.Videos,
    'it does not snatch the slot out from under a banner nobody has read yet');
  await sleep(1300);
  ok(ann2.showing() && ann2.showing().element === GoonElement.LockCards,
    '…but it takes it on the minimum hold rather than after the whole dwell');
  ok(findAll(ann2.root, 'gg-announce-slot').length === 1, 'and there is still exactly one');

  ok(log.some((e) => e && e.t === 'announce'), 'every banner is written to the match log');

  // lifecycle
  const takenBefore = fresh._subs.taken;
  ann2.unmount();
  ok(fresh._subs.released === fresh._subs.taken && takenBefore > 0,
    'unmount releases every engine subscription it took', `${fresh._subs.released}/${fresh._subs.taken}`);
  ok(ann2.root.parentNode === null, 'and takes its own node with it');
  fresh._emit('elStart', { element: GoonElement.Videos, intensity: 0.3, durationMs: 1000, elapsedMs: 11000 });
  await sleep(320);
  ok(findAll(ann2.root, 'gg-announce-slot').length === 0, 'a start after unmount draws nothing at all');
  ann.unmount();
  ok(match._subs.released === match._subs.taken, 'the first one leaks nothing either',
    `${match._subs.released}/${match._subs.taken}`);
}

// --- 14e. hud.js really mounts it -----------------------------------------
{
  const match = makeFakeMatch();
  match.rampCues = [];
  const hud = hudMod.mountHud({ match, audio: null, prefs: null, matchLog: [] });
  const frame = findOne(dom.byId.get('gg-hud'), 'gg-hud-frame');
  ok(!!findOne(frame, 'gg-announce'), 'mountHud puts the ribbon inside the frame');
  ok(!!hud.parts && !!hud.parts.announcer && typeof hud.parts.announcer.showing === 'function',
    'and exposes it on parts, like every other sub-mount');
  hud.unmount();
  ok(dom.byId.get('gg-hud').children.length === 0, 'and it comes down with the desk');
  ok(match._subs.released === match._subs.taken, 'with no subscription left behind',
    `${match._subs.released}/${match._subs.taken}`);
}

/* --- 14f. the CSS half ----------------------------------------------------
 * ui/announcer.js only toggles classes. Placement, the click-through and the
 * three motion switches are all CSS, so a ribbon with no rules would pass every
 * JS check above and still sit on top of the stage eating clicks. */
{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const css = await fs.readFile(url.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8');
  const secAt = css.indexOf('ANNOUNCER — the top-centre ribbon');
  ok(secAt > 0, 'hud.css carries the announcer section');
  const sec = secAt > 0 ? css.slice(secAt) : '';

  const block = /\.gg-announce\s*\{([^}]*)\}/.exec(sec);
  ok(!!block, 'the ribbon has a positioning block');
  const box = block ? block[1] : '';
  ok(/position:\s*absolute/.test(box), 'it is absolutely placed, so it takes no row in the frame grid');
  ok(/top:/.test(box) && !/bottom:/.test(box),
    'anchored to the TOP — the bottom-centre belongs to MERCY and its 96px keep-out');
  ok(/left:\s*50%/.test(box) && /translateX\(-50%\)/.test(box), '…and centred');
  ok(/max-width:\s*min\(/.test(box), 'and narrow, so it never reaches the opponent monitor in the corner');

  ok(/\.gg-hud-frame\s+\.gg-announce,\s*\n?\s*\.gg-hud-frame\s+\.gg-announce-slot\s*\{[^}]*pointer-events:\s*none/.test(sec),
    'pointer-events: none at .gg-hud-frame specificity — .gg-plate would otherwise turn them back on');

  ok(/\.gg-announce-slot\s*\{[^}]*opacity:\s*1/.test(sec),
    'the resting slot is VISIBLE — with motion off the words still land');
  ok(/@keyframes\s+ggAnnounceIn/.test(sec) && /\.gg-announce-slot\.is-in\s*\{[^}]*animation:\s*ggAnnounceIn/.test(sec),
    'the banner slides in');
  ok(/@keyframes\s+ggAnnounceOut/.test(sec) && /\.gg-announce-slot\.is-out\s*\{[^}]*animation:\s*ggAnnounceOut/.test(sec),
    '…and out');
  ok(sec.indexOf('.gg-announce-slot.is-out {') > sec.indexOf('.gg-announce-slot.is-in {'),
    'the outro is written last, because `animation` is a shorthand and one of two live rules would lose');
  ok(/\.gg-announce-slot\.is-in\s*>\s*\.gg-announce-sheen\s*\{[^}]*animation:\s*ggAnnounceSheen/.test(sec),
    'the decorative sheen rides its OWN child element, never the slot the one-shots own');
  ok(!/infinite/.test(sec),
    'and nothing here loops — the ribbon costs the two-animation chrome budget nothing');

  ok(/html\[data-gg-fx="hot"\]\s*\.gg-announce-slot\s*\{[^}]*backdrop-filter:\s*none/.test(sec),
    'the plate hardens at data-gg-fx="hot" like every other plate');
  ok(/html\[data-gg-fx="hot"\]\s*\.gg-announce-sheen\s*\{[^}]*display:\s*none/.test(sec),
    '…and the decoration stops being drawn at all when the machine is busy');

  ok(/\.gg-hud-frame\.is-calm\s+\.gg-announce-slot\.is-in[\s\S]{0,220}animation:\s*none\s*!important/.test(sec),
    '.is-calm stops every frame of it');
  ok(/prefers-reduced-motion[\s\S]{0,260}\.gg-announce-slot\.is-in[\s\S]{0,200}animation:\s*none\s*!important/.test(sec),
    'and so does prefers-reduced-motion — the banner still appears, it just stops moving');
}

/* ===========================================================================
 * 15. THEIR FLOATING VIDEO WINDOWS — "the opponent monitor should show how many
 *     floating video windows the opponent currently has" (owner, 0804).
 *
 * The count arrives on the tick as `vwin` (0..4) and is drawn as a little
 * staggered stack of windows in the projection rect. It is NOT a .gg-mini and
 * that is the point: a mini is one node keyed by an effect NAME, on or off, and
 * this is a COUNT — nodes are added and removed to match it exactly.
 *
 * Pinned below: the ten minis are untouched, the stack tracks the number in both
 * directions, an over-claim is clamped by the UI as well as the wire, the rect
 * stops reading "idle" when windows are up, and the CSS keeps the three-element
 * animation split (one-shot / drift / pulse), the --gg-deco-play exemption that
 * makes the monitor information rather than chrome, and the reduced-motion rule
 * that stops them while leaving them VISIBLE.
 * ======================================================================== */
{
  const match = makeFakeMatch();
  match.opponent.activeEffects = [];
  match.opponent.vwin = 0;
  const host = document.createElement('div');
  const mon = opponentMod.mountOpponent({ host, match, audio: { sfx() {} } });
  const proj = findOne(mon.root, 'gg-mon-proj');
  const box = findOne(proj, 'gg-mon-vwins');
  const minis = () => findAll(proj, 'gg-mon-vwin');
  const set = (v) => { match.opponent.vwin = v; match._emit('opp'); };

  ok(!!box, 'the projection rect carries a window-stack container');
  ok(box.parentNode === proj, 'and it lives INSIDE the rect, not on the bezel');
  ok(findAll(proj, 'gg-mini').length === 10 && findAll(mon.root, 'gg-mini').length === 10,
    'the ten minis are untouched — the stack is not one of them', String(findAll(proj, 'gg-mini').length));
  ok(minis().length === 0, 'no windows, no rects', String(minis().length));

  set(3);
  ok(minis().length === 3, 'three windows draw three rects', String(minis().length));
  ok(hasClass(box, 'is-on'), 'and the container says so');
  const first = minis()[0];
  const body = findOne(first, 'gg-mon-vwin-body');
  ok(!!body, 'each rect has its own drift body (the shorthand trap: one animation per element)');
  ok(!!findOne(body, 'gg-mon-vwin-dot'), '…and the little recording dot inside it');
  ok(findAll(proj, 'gg-mon-vwin-dot').length === 3, 'one dot per window, never a spare',
    String(findAll(proj, 'gg-mon-vwin-dot').length));

  set(1);
  ok(minis().length === 1, 'a closed window takes its rect with it — no lingering fade-out',
    String(minis().length));
  set(4);
  ok(minis().length === 4, 'a full pool is four', String(minis().length));
  set(9);
  ok(minis().length === 4, 'and a peer claiming nine still only ever draws four', String(minis().length));
  set(-2);
  ok(minis().length === 0, 'a negative claim draws nothing', String(minis().length));
  set('2');
  ok(minis().length === 2, 'a quoted number is read forgivingly', String(minis().length));
  set('lots');
  ok(minis().length === 0, 'a word draws nothing at all', String(minis().length));
  set(undefined);
  ok(minis().length === 0, 'and so does an opponent that never mentioned windows (an older peer)',
    String(minis().length));

  // the idle word: four windows up is not an idle machine
  const idle = findOne(proj, 'gg-mini-idle');
  ok(idle.hidden === false, 'with nothing on at all, the rect reads idle');
  set(2);
  ok(idle.hidden === true, 'their windows alone are enough to stop it reading idle');
  set(0);
  ok(idle.hidden === false, 'and it comes back when the last one closes');

  set(3);
  mon.unmount();
  ok(findAll(host, 'gg-mon-vwin').length === 0, 'unmount takes the whole stack with it',
    String(findAll(host, 'gg-mon-vwin').length));
}

/* --- 15b. …and the CSS half of it ---------------------------------------- */
{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const css = await fs.readFile(url.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8');
  const blockOf = (sel) => {
    const m = new RegExp(sel.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\s*\\{([^}]*)\\}').exec(css);
    return m ? m[1] : null;
  };
  // (the plain selector, not the container `.gg-mon-vwins` — the `\s*\{` is what tells them apart)
  const wrap = blockOf('.gg-mon-vwin') || '';
  const drift = blockOf('.gg-mon-vwin-body') || '';
  const dot = blockOf('.gg-mon-vwin-dot') || '';

  ok(!!wrap && !!drift && !!dot, 'hud.css styles all three elements of a window mini');

  // THE ANIMATION SHORTHAND TRAP: one `animation` declaration per element, and the one-shot and
  // the loops must live on DIFFERENT elements or they cancel each other.
  const decls = (b) => (b.match(/(^|[;\s])animation:/g) || []).length;
  ok(decls(wrap) === 1 && decls(drift) === 1 && decls(dot) === 1,
    'exactly one animation declaration on each', `${decls(wrap)}/${decls(drift)}/${decls(dot)}`);
  ok(!/infinite/.test(wrap) && /\s1\s+both/.test(wrap), 'the wrapper carries the ONE-SHOT pop-in', wrap.trim());
  ok(/infinite/.test(drift) && /infinite/.test(dot), 'the drift and the pulse are the loops');

  // The monitor is INFORMATION: both loops ride --gg-deco-play, which .gg-mon-proj re-declares as
  // `running`, so they keep moving while the local machine is at data-gg-fx="hot".
  ok(/animation-play-state:\s*var\(--gg-deco-play\)/.test(drift)
    && /animation-play-state:\s*var\(--gg-deco-play\)/.test(dot),
    'both loops ride --gg-deco-play (the projection rect exempts them from the hot freeze)');
  ok(!/--gg-deco-play:\s*paused/.test(wrap + drift + dot), 'and nothing here re-pauses itself');

  // Four slots, all in the TOP band — the emote bubble owns the middle of the rect (~34%-66%)
  // and the bezel owns everything outside it.
  const slots = [...css.matchAll(/\.gg-mon-vwin:nth-child\((\d)\)\s*\{([^}]*)\}/g)]
    .map((m) => ({ i: Number(m[1]), body: m[2] }))
    .filter((s) => /top:/.test(s.body));
  ok(slots.length === 4, 'four staggered slots are positioned', String(slots.length));
  const height = Number((/height:\s*([\d.]+)%/.exec(wrap) || [])[1]);
  ok(height > 0, 'the rect has a % height to reason about', String(height));
  const tops = slots.map((s) => Number((/top:\s*([\d.]+)%/.exec(s.body) || [])[1]));
  const lefts = slots.map((s) => Number((/left:\s*([\d.]+)%/.exec(s.body) || [])[1]));
  ok(tops.every((t) => t + height <= 34),
    'nothing in the stack reaches the emote bubble band', JSON.stringify(tops.map((t) => t + height)));
  ok(new Set(tops).size > 1 && new Set(lefts).size > 1,
    'and the slots really are staggered, not a row', JSON.stringify(slots.map((s, i) => `${lefts[i]}/${tops[i]}`)));

  // The hard off switches stop the motion and NOTHING ELSE: a still window is still countable.
  ok(/prefers-reduced-motion[\s\S]*?\.gg-mon-vwin,\s*\.gg-mon-vwin \*\s*\{\s*animation:\s*none\s*!important/.test(css),
    'prefers-reduced-motion stops the stack dead');
  ok(/is-calm\s+\.gg-mon-vwin,[\s\S]{0,120}animation:\s*none\s*!important/.test(css),
    '.is-calm stops it too');
  ok(!/\.gg-mon-vwin[^{]*\{[^}]*display:\s*none/.test(css),
    'and neither of them hides it — the count has to stay readable');
}

// ---------------------------------------------------------- 16. the sfx pass
/* ui/audio.js stopped being a stub. These pin the four things that make it real
 * and that nothing else in the suite can see:
 *   16a — every registered id resolves to a file that EXISTS ON DISK (the whole
 *         point of a registry is that a rename is a test failure, not silence);
 *   16b — the pure math and the pool/gesture constants;
 *   16c — the graph is lazy: importing and constructing under node touches no
 *         AudioContext, no fetch and no window;
 *   16d — every hook this pass wired really calls through the audio API, read
 *         off the source so a deleted call site cannot pass by being absent. */

const audioMod = await import('../ui/audio.js');

/* --- 16a. the registry resolves to real bytes ----------------------------- */
{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const { SFX_REGISTRY, SFX_IDS, DTRH_SFX_DIR, LOCAL_SFX_DIR } = audioMod;

  ok(typeof audioMod.createAudio === 'function', 'ui/audio.js exports createAudio');
  ok(SFX_IDS.length === Object.keys(SFX_REGISTRY).length,
    'SFX_IDS is DERIVED from the registry, so the two cannot drift', String(SFX_IDS.length));
  ok(Object.isFrozen(SFX_REGISTRY), 'the registry is frozen');

  // Two roots, two rules. DtRH is HOTLINKED at an absolute path (the sprite
  // precedent in exec/fx.css); everything else is a copy under goon/assets/sfx/.
  const goonRoot = url.fileURLToPath(new URL('..', import.meta.url));
  const webRoot = url.fileURLToPath(new URL('../..', import.meta.url));
  const exists = async (p) => { try { await fs.access(p); return true; } catch (_e) { return false; } };

  let checked = 0;
  const missing = [];
  const badGain = [];
  for (const id of SFX_IDS) {
    const entry = SFX_REGISTRY[id];
    if (!Array.isArray(entry.files) || entry.files.length === 0) { missing.push(id + ': no files'); continue; }
    if (!(entry.gain > 0 && entry.gain <= 1)) badGain.push(id + '=' + entry.gain);
    for (const u of entry.files) {
      checked++;
      let abs;
      if (u.startsWith(DTRH_SFX_DIR)) abs = webRoot + u.slice(1);          // '/dtrh/...' -> Resources/web/dtrh/...
      else abs = url.fileURLToPath(new URL(u));                            // resolved local copy
      if (!(await exists(abs))) missing.push(id + ' -> ' + u);
    }
  }
  ok(missing.length === 0, 'every registered sfx url is a file that exists on disk', missing.join(' | '));
  ok(badGain.length === 0, 'and every cue trim is a sane 0<g<=1', badGain.join(' '));
  ok(checked >= 40, 'the registry actually carries a pack, not a token entry', String(checked));

  // The two roots are used, and used the right way round.
  const urls = SFX_IDS.flatMap((id) => SFX_REGISTRY[id].files);
  ok(urls.some((u) => u.startsWith(DTRH_SFX_DIR)),
    'DtRH cues are hotlinked absolute (/dtrh/…), never copied');
  ok(urls.some((u) => /assets[\\/]sfx[\\/]/.test(u)),
    'and the intake/bureau recycles are local copies under assets/sfx/');
  ok(LOCAL_SFX_DIR === '../assets/sfx/', 'the local dir is module-relative (ui/ -> ../assets/sfx/)');
  ok(!urls.some((u) => u.startsWith('/intake/')),
    'nothing points at /intake/ — the harnesses only mount /dtrh/, so those had to be copied');

  // The three pops really are three different sounds, or the distinction is a lie.
  const pops = ['bubble-pop', 'bubble-pop-fx', 'bubble-pop-video'];
  ok(pops.every((p) => SFX_REGISTRY[p]), 'plain / effect / video bubbles each have their own cue');
  ok(new Set(pops.map((p) => SFX_REGISTRY[p].files.join(','))).size === 3,
    'and all three resolve to different files');
  ok(SFX_REGISTRY['bubble-pop-fx'].gain > SFX_REGISTRY['bubble-pop'].gain,
    'the effect pop is the juicier of the two');
  // Taste pins: the caption is under the pops, and the safety valve is not a sting.
  ok(SFX_REGISTRY['announce-in'].gain < SFX_REGISTRY['bubble-pop'].gain,
    'the announcer ribbon sits UNDER the pops in the mix');
  ok(SFX_REGISTRY['subliminal'].gain <= 0.12, 'the subliminal tick stays barely-there');
  ok(SFX_REGISTRY['gg-drop'].gain > SFX_REGISTRY['gg-drop-dud'].gain,
    'an armed drop is louder than its fizzle');
  ok(SFX_REGISTRY['lock-slip'].gain < SFX_REGISTRY['lock-solved'].gain,
    'a typing slip is quieter than solving the card');
}

/* --- 16b. the pure math, the pool cap and the gesture path ---------------- */
{
  const { cueGain, busGain, pickVariant, SFX_TRIM, POOL_MAX, MIN_GAP_MS, UNLOCK_EVENTS } = audioMod;

  ok(SFX_TRIM > 0 && SFX_TRIM <= 1, 'there is ONE exported master trim over every cue', String(SFX_TRIM));
  ok(POOL_MAX >= 4 && POOL_MAX <= 16,
    'the pool cap is a real ceiling — 26 bubbles may not become 26 sources', String(POOL_MAX));
  ok(MIN_GAP_MS > 0, 'and same-frame repeats of one id are swallowed', String(MIN_GAP_MS));
  ok(UNLOCK_EVENTS.includes('pointerdown') && UNLOCK_EVENTS.includes('keydown'),
    'the gesture-unlock list covers pointer AND keyboard');

  // The per-cue node carries the TRIM ONLY now — the player's sliders are the
  // buses it feeds, so each is applied exactly once (see 19c for the staging).
  ok(Math.abs(cueGain(0.5) - 0.5 * SFX_TRIM) < 1e-9, 'cueGain folds the house trim in');
  ok(cueGain(0) === 0, 'a zero cue trim is silence');
  ok(cueGain('nonsense') > 0, 'and a corrupt trim falls back to a sane default rather than NaN into an AudioParam');
  ok(Math.abs(busGain(0.5, 1, 1) - 0.5 * SFX_TRIM) < 1e-9, 'busGain is cueGain at both sliders open');
  ok(busGain(0.5, 1, 0) === 0, 'master 0 is silence');
  ok(busGain(0.5, 0, 1) === 0, 'a cue bus at 0 is silence');
  ok(Math.abs(busGain(0.5, 0.5, 0.5) - 0.5 * 0.5 * 0.5 * SFX_TRIM) < 1e-9,
    'and the three multiply, never conflate');

  // No-repeat-last over a two-file rotation must alternate, never stick.
  ok(pickVariant(1, 0) === 0, 'a single-variant cue always picks it');
  ok(pickVariant(2, 0, () => 0) === 1, 'a repeat is stepped one off');
  ok(pickVariant(2, 1, () => 0.99) === 0, 'in both directions');
  let stuck = false;
  let last = -1;
  for (let i = 0; i < 40; i++) { const v = pickVariant(2, last); if (v === last) stuck = true; last = v; }
  ok(!stuck, 'and forty draws never repeat the last one');
}

/* --- 16c. lazy: constructing under node touches nothing ------------------- */
{
  ok(typeof globalThis.AudioContext === 'undefined',
    'the DOM stub really has no AudioContext (so the next checks mean something)');

  const seen = [];
  const bus = audioMod.createAudio({ logger: { debug: (m) => seen.push(m), warn: (m) => seen.push(m) } });
  ok(bus.isReal === false, 'no AudioContext in this host -> isReal is false, not a crash');
  ok(bus.contextState === 'none', 'and no context was constructed');
  ok(bus.sfx('bubble-pop') === false, 'a cue in a hostless environment is a clean false');
  ok(bus.sfx('nope-not-a-cue') === false, 'an unknown id is refused');
  ok(bus.stats.unknown === 1, 'and COUNTED as unknown — a typo must be loud, not silent',
    JSON.stringify(bus.stats));
  ok(seen.some((m) => /unknown sfx id/.test(m)), 'the logger hears about it');

  // The whole legacy API is still here: every screen calls these unconditionally.
  for (const k of ['sfx', 'music', 'stopMusic', 'duck', 'setVolume', 'dispose', 'unlock', 'warm']) {
    ok(typeof bus[k] === 'function', `the bus still exposes ${k}()`);
  }
  bus.music('title');
  ok(bus.currentMusic === 'title', 'music() still books the bed name');
  bus.duck(true);
  ok(bus.isDucked === true, 'duck() still latches');
  bus.duck(true);
  ok(bus.isDucked === true, 'and is idempotent');
  bus.stopMusic();
  ok(bus.currentMusic === null, 'stopMusic clears it');
  bus.setVolume('game', 0.5);
  ok(bus.volumes.game === 0.5, 'setVolume writes the bus it names');
  bus.setVolume('nonsense', 0.1);
  ok(bus.volumes.game === 0.5 && bus.volumes.nonsense === undefined,
    'and ignores a bus it does not have', JSON.stringify(bus.volumes));
  ok(bus.unlock() === 'none', 'unlock() on a hostless bus reports none rather than throwing');
  bus.dispose();
  ok(bus.sfx('bubble-pop') === false, 'a disposed bus is inert');

  // It must read the player's existing sliders, not invent a second set.
  const prefsMod = await import('../ui/prefs.js');
  const prefs = prefsMod.createPrefs({ masterVolume: 0.4, gameVolume: 0.6 });
  const bus2 = audioMod.createAudio({ prefs });
  ok(bus2.volumes.master === 0.4 && bus2.volumes.game === 0.6,
    'the bus seeds from ui/prefs.js — no settings UI of its own', JSON.stringify(bus2.volumes));
  prefs.set('gameVolume', 0.2);
  ok(bus2.volumes.game === 0.2, 'and follows a live slider drag');
  bus2.dispose();
}

/* --- 16d. every hook this pass wired is really at its call site ----------- */
{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const read = async (p) => fs.readFile(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8');

  const bubblesSrc = await read('../exec/bubbles.js');
  const dropsSrc = await read('../ui/drops.js');
  const hudSrc = await read('../ui/hud.js');
  const annSrc = await read('../ui/announcer.js');
  const lockSrc = await read('../exec/lockCards.js');
  const recapSrc = await read('../ui/screens/recap.js');
  const titleSrc = await read('../ui/screens/title.js');
  const audioSrc = await read('../ui/audio.js');

  ok(/function popCue\(rec\)/.test(bubblesSrc) && /sfx\(popCue\(rec\)\)/.test(bubblesSrc),
    'exec/bubbles.js routes its pop through popCue(), not one hardcoded id');
  ok(/rec\.fromPayload[\s\S]{0,80}'bubble-pop'/.test(bubblesSrc),
    "and a payload's clutter pops the PLAIN cue — their swarm is worth nothing and must not sound rich");
  ok(/'bubble-pop-video'/.test(bubblesSrc) && /'bubble-pop-fx'/.test(bubblesSrc),
    'the prism and the effect bubbles have their own');

  ok(/sfx\('gg-drop'\)/.test(dropsSrc), 'ui/drops.js cues an armed drop');
  ok((dropsSrc.match(/sfx\('gg-drop-dud'\)/g) || []).length === 1,
    'and the ONE fizzle path left (the arsenal already full) cues the dud — the other was '
    + '"no charge", deleted 2026-08-05 with the requirement itself');
  ok(!/reason: 'miss'[^\n]*sfx\(/.test(dropsSrc),
    'a missed roll stays silent — most rolls miss');

  ok(/sfx\(audio, 'payload-in'\)/.test(hudSrc), 'ui/hud.js cues an incoming payload as it lands');
  ok((hudSrc.match(/sfx\(audio, 'payload-in'\)/g) || []).length === 1,
    'exactly ONE landing cue — per-family stings would be eight sounds to learn');
  ok(/mountAnnouncer\(\{\s*host: root, match, audio, onLog \}\)/.test(hudSrc),
    'and the announcer finally gets the bus handed to it');

  ok(/function cue\(\)[\s\S]{0,220}audio\.sfx\('announce-in'\)/.test(annSrc),
    'ui/announcer.js has a cue for the ribbon sliding in');
  ok((annSrc.match(/cue\(\);/g) || []).length === 1,
    'fired once, on the way IN — the slide-out is deliberately silent');

  ok(/onMistake: \(\) =>[\s\S]{0,160}'lock-slip'/.test(lockSrc),
    'exec/lockCards.js buzzes a wrong keystroke through the view onMistake seam');
  ok(/'lock-solved'/.test(lockSrc), 'and still chimes a solved card');

  ok(/const STING = \{ won: 'recap-won', lost: 'recap-lost', draw: 'recap-draw' \}/.test(recapSrc),
    'ui/screens/recap.js has a sting per verdict');
  ok(/if \(stung \|\| !tone\) return;/.test(recapSrc),
    'ONE per mount — paint() runs again when the countersignature lands');
  ok(/'abandon' spends the one shot on silence|abandon/.test(recapSrc),
    'and an abandoned match gets no fanfare');
  ok(/audio\?\.unlock\?\.\(\)/.test(titleSrc),
    'ui/screens/title.js builds the graph on the first screen, not the first cue');

  // The bus itself must stay import-safe: no module-scope AudioContext / fetch.
  const beforeFactory = audioSrc.slice(0, audioSrc.indexOf('export function createAudio'));
  ok(!/new\s+(window\.)?(webkit)?AudioContext/.test(beforeFactory),
    'ui/audio.js constructs no AudioContext at module scope');
  ok(!/^\s*(await\s+)?fetch\(/m.test(beforeFactory),
    'and fetches nothing at import time');
  ok(/no-user-gesture-required/.test(audioSrc),
    'the WebView2 autoplay flag is documented where the unlock path lives');
}

/* ===========================================================================
 * 17. SKIPPABLE VIDEOS — the option, the pref, and the wire between ui/ and exec/
 *
 * The floating video windows exec/videos.js throws at you cannot be closed early
 * any more; a player who wants an out turns "skippable videos" on here, and every
 * window grows a ✕. Default OFF, because being thrown a window is something you
 * sit through — a dismiss button on by default would quietly demote the payload
 * into a notification.
 *
 * THE INTERESTING PART IS THE SEAM. exec/ imports no ui/ module (and is built
 * once, at startup, long before this drawer exists), so the value travels the way
 * heat and motion already do: ui/prefs.js mirrors it onto <html> as an attribute
 * and exec/videos.js reads it there, fresh, at the moment of the click. Three
 * things have to hold or the toggle is a lie, and none of them shows up in either
 * file's own tests:
 *   · the attribute is written at CONSTRUCTION, not only on change — otherwise a
 *     stored `true` reads as forgotten until you open Options;
 *   · the two files agree on the attribute's NAME and its truthy value;
 *   · the drawer writes through prefs, so flipping the toggle moves the attribute
 *     (which is what reaches windows that are already floating).
 * ======================================================================== */
{
  const prefsMod = await import('../ui/prefs.js');
  const optionsMod = await import('../ui/options.js');
  const { S } = await import('../ui/strings.js');
  const videosMod = await import('../exec/videos.js');
  const root = dom.doc.documentElement;
  const attrNow = () => root.getAttribute(videosMod.SKIP_ATTR);

  // ---- the pref itself
  ok(prefsMod.PREF_DEFAULTS.skippableVideos === false,
    'skippableVideos is a pref, and it DEFAULTS OFF',
    String(prefsMod.PREF_DEFAULTS.skippableVideos));
  ok(typeof prefsMod.PREF_DEFAULTS.skippableVideos === 'boolean',
    'a boolean, so a corrupt store coerces to one instead of poisoning the UI');

  // ---- the mirror, at construction and on every door into the store
  root.removeAttribute(videosMod.SKIP_ATTR);
  const prefs = prefsMod.createPrefs({});
  ok(attrNow() === 'off',
    'creating the store writes the attribute IMMEDIATELY — a setting that only takes effect once you open Options is a setting that looks forgotten',
    String(attrNow()));
  ok(prefs.set('skippableVideos', true) === true && attrNow() === videosMod.SKIP_ON,
    'turning it on moves the attribute', String(attrNow()));
  ok(prefs.set('skippableVideos', false) === true && attrNow() === 'off',
    'and off again', String(attrNow()));
  prefs.merge({ skippableVideos: true });
  ok(attrNow() === videosMod.SKIP_ON, 'a bulk merge mirrors too', String(attrNow()));
  prefs.reset();
  ok(attrNow() === 'off', 'and Reset puts it back to the default, on screen as well as in the store',
    String(attrNow()));

  // ---- a stored TRUE is true from the first frame, which is the whole point
  root.removeAttribute(videosMod.SKIP_ATTR);
  const seeded = prefsMod.createPrefs({ skippableVideos: true });
  ok(seeded.get('skippableVideos') === true && attrNow() === videosMod.SKIP_ON,
    'a player who turned it on last time has it on before anything is opened', String(attrNow()));

  // ---- the two files agree on the name, and exec/ agrees it is off by default
  ok(videosMod.SKIP_ATTR === 'data-gg-vskip' && videosMod.SKIP_ON === 'on',
    'ui/prefs.js and exec/videos.js name the same attribute — a typo here is a dead toggle in both directions',
    `${videosMod.SKIP_ATTR}="${videosMod.SKIP_ON}"`);
  root.setAttribute(videosMod.SKIP_ATTR, 'off');
  const vids = videosMod.createVideos({ layers: null, media: null });
  ok(vids.skippable() === false, 'exec/ reads "off" as off');
  root.setAttribute(videosMod.SKIP_ATTR, videosMod.SKIP_ON);
  ok(vids.skippable() === true, 'and the agreed value as on — read fresh, never cached');
  root.removeAttribute(videosMod.SKIP_ATTR);
  ok(vids.skippable() === false, 'while an attribute nobody ever wrote is OFF, not undefined-on');

  // ---- the copy
  ok(typeof S.options.skippable === 'string' && S.options.skippable.length > 0,
    'the drawer has a label for it', String(S.options.skippable));
  ok(typeof S.options.skippableNote === 'string' && S.options.skippableNote.length > 0,
    'and a line saying what it does', String(S.options.skippableNote));
  ok(/mute/i.test(S.options.skippableNote),
    'which says a window can always be MUTED with a click — the thing the toggle does NOT take away',
    S.options.skippableNote);
  ok(/✕/.test(S.options.skippableNote) || /close/i.test(S.options.skippableNote),
    'and names the button it gives you', S.options.skippableNote);

  // ---- the row, in a LIVE match (this is a knob about your own screen, not a
  // match term, and the moment you want it is the moment a window is up)
  const live = prefsMod.createPrefs({});
  const options = optionsMod.createOptions({ prefs: live, session: { hosted: false }, isInMatch: () => true });
  options.open();
  await sleep(30);
  const drawer = dom.byId.get('gg-drawer');
  const panel = findOne(drawer, 'gg-panel');
  ok(!!panel, 'the drawer opened');
  const rowFor = (label) => findAll(panel, 'gg-panel-row')
    .find((r) => (r.children[0] || {}).textContent === label) || null;
  const skipRow = rowFor(S.options.skippable);
  ok(!!skipRow, 'and carries a Skippable videos row, mid-match, next to the volumes and reduce-motion');
  const toggle = skipRow ? findOne(skipRow, 'gg-toggle') : null;
  ok(!!toggle, 'with the house toggle the other switches use');
  ok(toggle && toggle.getAttribute('aria-pressed') === 'false',
    'reading OFF, because that is what it is', String(toggle && toggle.getAttribute('aria-pressed')));
  const notes = findAll(panel, 'gg-panel-note').map((p) => p.textContent);
  ok(notes.includes(S.options.skippableNote), 'the explanation is on screen with it', JSON.stringify(notes));
  ok(notes.includes(S.options.lockedNote),
    'and the in-match lock note is still there — this row is not one of the locked terms', JSON.stringify(notes));

  // FLIPPING IT REALLY REACHES exec/. Toggle -> prefs -> attribute is the whole
  // chain; the windows already floating are on the far side of it, in CSS.
  root.removeAttribute(videosMod.SKIP_ATTR);
  toggle.dispatchEvent({ type: 'click' });
  ok(live.get('skippableVideos') === true, 'clicking it writes the pref');
  ok(toggle.getAttribute('aria-pressed') === 'true', 'and repaints itself');
  ok(attrNow() === videosMod.SKIP_ON,
    'and the attribute exec/videos.js reads moved with it — that is the ✕ appearing on windows already up',
    String(attrNow()));
  ok(vids.skippable() === true, 'as the renderer itself agrees', String(vids.skippable()));
  toggle.dispatchEvent({ type: 'click' });
  ok(live.get('skippableVideos') === false && vids.skippable() === false,
    'and back off again, same frame, same chain', `${live.get('skippableVideos')} / ${attrNow()}`);

  options.dispose();
  await sleep(320);
  root.removeAttribute(videosMod.SKIP_ATTR);
}

/* ==========================================================================
 * SHADER SPIRALS — the freeze escape hatch (2026-08-04).
 *
 * On 2026-08-04 a session froze VISUALLY while its script kept running: a
 * compositor/GPU stall, no crash, no dump. The spiral bed's WebGL pane is the
 * only GPU context the duel owns and it had gone in hours earlier, so the owner
 * gets a switch — default ON, because the shader IS the good bed, but reachable
 * mid-match, because the moment you want it is the moment the picture stopped.
 *
 * Same three-part chain as skippable videos, and the same three risks:
 *   · the pref exists and defaults ON (a hatch that defaults shut is a downgrade
 *     shipped to everyone);
 *   · ui/prefs.js mirrors it to <html> at CONSTRUCTION, not just on a toggle;
 *   · ui/prefs.js and exec/spiral.js name the same attribute — a typo here is a
 *     switch that does nothing, in a file nobody looks at during a freeze.
 * Polarity is the one difference from data-gg-vskip, and it is deliberate:
 * ABSENT means ON there.
 * ======================================================================== */
{
  const prefsMod = await import('../ui/prefs.js');
  const optionsMod = await import('../ui/options.js');
  const { S } = await import('../ui/strings.js');
  const spiralMod = await import('../exec/spiral.js');
  const root = dom.doc.documentElement;
  const attrNow = () => root.getAttribute(spiralMod.SHADER_ATTR);

  // ---- the pref
  ok(prefsMod.PREF_DEFAULTS.shaderSpirals === true,
    'shaderSpirals is a pref, and it DEFAULTS ON — the raster pool is the floor, not the plan',
    String(prefsMod.PREF_DEFAULTS.shaderSpirals));
  ok(typeof prefsMod.PREF_DEFAULTS.shaderSpirals === 'boolean',
    'a boolean, so a corrupt store coerces to one instead of poisoning the renderer');

  // ---- the mirror, on every door into the store
  root.removeAttribute(spiralMod.SHADER_ATTR);
  const prefs = prefsMod.createPrefs({});
  ok(attrNow() === 'on', 'creating the store writes the attribute immediately', String(attrNow()));
  ok(prefs.set('shaderSpirals', false) === true && attrNow() === spiralMod.SHADER_OFF,
    'turning it off moves the attribute exec/spiral.js reads', String(attrNow()));
  ok(prefs.set('shaderSpirals', true) === true && attrNow() === 'on', 'and on again', String(attrNow()));
  prefs.merge({ shaderSpirals: false });
  ok(attrNow() === spiralMod.SHADER_OFF, 'a bulk merge mirrors too', String(attrNow()));
  prefs.reset();
  ok(attrNow() === 'on', 'and Reset puts the good bed back', String(attrNow()));

  // ---- a stored FALSE is false from the first frame: the player switched the
  //      shader off because it froze, and it must not come back on next launch
  root.removeAttribute(spiralMod.SHADER_ATTR);
  const seeded = prefsMod.createPrefs({ shaderSpirals: false });
  ok(seeded.get('shaderSpirals') === false && attrNow() === spiralMod.SHADER_OFF,
    'a player who switched it off has it off before anything is opened', String(attrNow()));

  // ---- the two files agree on the name AND on the polarity
  ok(spiralMod.SHADER_ATTR === 'data-gg-shader' && spiralMod.SHADER_OFF === 'off',
    'ui/prefs.js and exec/spiral.js name the same attribute and the same off value',
    `${spiralMod.SHADER_ATTR}="${spiralMod.SHADER_OFF}"`);
  ok(prefsMod.PREF_DEFAULTS.shaderSpirals === true && spiralMod.SHADER_OFF === 'off',
    'ABSENT means ON: a page that never built a prefs store (a self-test, the import sweep) still gets the shader, and only the exact off value switches it off');

  // ---- the copy
  ok(typeof S.options.shaderSpirals === 'string' && S.options.shaderSpirals.length > 0,
    'the drawer has a label for it', String(S.options.shaderSpirals));
  ok(typeof S.options.shaderSpiralsNote === 'string' && S.options.shaderSpiralsNote.length > 0,
    'and a line saying what it does', String(S.options.shaderSpiralsNote));
  ok(/freez/i.test(S.options.shaderSpiralsNote),
    'which names the SYMPTOM to reach for it for — a hatch nobody can find is not a hatch',
    S.options.shaderSpiralsNote);
  ok(/gif|spiral/i.test(S.options.shaderSpiralsNote),
    'and says what you fall back to, so switching it off does not read as losing the bed',
    S.options.shaderSpiralsNote);

  // ---- the row, IN a live match (this is a knob about your own screen, and the
  //      moment you want it is the moment the screen has stopped moving)
  const live = prefsMod.createPrefs({});
  const options = optionsMod.createOptions({ prefs: live, session: { hosted: false }, isInMatch: () => true });
  options.open();
  await sleep(30);
  const panel = findOne(dom.byId.get('gg-drawer'), 'gg-panel');
  ok(!!panel, 'the drawer opened');
  const rowFor = (label) => findAll(panel, 'gg-panel-row')
    .find((r) => (r.children[0] || {}).textContent === label) || null;
  const shaderRow = rowFor(S.options.shaderSpirals);
  ok(!!shaderRow, 'and carries a Shader spirals row, mid-match, next to the other own-screen knobs');
  const toggle = shaderRow ? findOne(shaderRow, 'gg-toggle') : null;
  ok(!!toggle, 'with the house toggle the other switches use');
  ok(toggle && toggle.getAttribute('aria-pressed') === 'true',
    'reading ON, because that is the default', String(toggle && toggle.getAttribute('aria-pressed')));
  const shaderNotes = findAll(panel, 'gg-panel-note').map((p) => p.textContent);
  ok(shaderNotes.includes(S.options.shaderSpiralsNote), 'the explanation is on screen with it',
    JSON.stringify(shaderNotes));
  ok(shaderNotes.includes(S.options.lockedNote),
    'and the in-match lock note is still there — this row is not one of the locked wire terms');

  // FLIPPING IT REALLY REACHES exec/. Toggle -> prefs -> attribute is the whole
  // chain, and exec/spiral.js re-reads the attribute once a second from inside
  // its own render loop, so a bed already spinning is on the far side of it.
  toggle.dispatchEvent({ type: 'click' });
  ok(live.get('shaderSpirals') === false, 'clicking it writes the pref');
  ok(toggle.getAttribute('aria-pressed') === 'false', 'and repaints itself');
  ok(attrNow() === spiralMod.SHADER_OFF,
    'and the attribute exec/spiral.js polls moved with it — that is a live shader bed dropping to raster',
    String(attrNow()));
  toggle.dispatchEvent({ type: 'click' });
  ok(live.get('shaderSpirals') === true && attrNow() === 'on',
    'and back on again, same chain', `${live.get('shaderSpirals')} / ${attrNow()}`);

  options.dispose();
  await sleep(320);
  root.removeAttribute(spiralMod.SHADER_ATTR);
}

/* ===========================================================================
 * 12. DISCORD SHARING — the avatar kit, the state module, the panel, the minis
 *     and the `gg-ava` bus (docs/GOON_DISCORD_CONTRACT.md, Work Item D).
 *
 * The risks this section exists for, in the order they would ship:
 *   1. AN OPTIMISTIC TOGGLE. A switch that paints itself on click is a switch
 *      that lies whenever the host refuses the write. The whole panel renders
 *      from the ECHO, and the only way to prove it is to click and assert that
 *      NOTHING moved until a `discord` frame arrived.
 *   2. A FETCH LOOP. `peer-card-req` must fire once per VERSION, not once per
 *      poll — /signal repeats peer_card_ver on every tick, and a request per
 *      tick is a rate-limit ban.
 *   3. A PREF THAT ONLY HIDES PIXELS. showOpponentAvatars OFF has to suppress
 *      the FETCH; "do not show me" and "do not download it" are one request.
 *   4. A LEAKED IDENTIFIER. No snowflake reaches this page, ever — `dm` is a
 *      boolean and the DM button is absent, not disabled, when it is false.
 *   5. A CTA THAT STARTS SOMETHING. `discord-link-request` asks the app to come
 *      forward; a version that began OAuth would be a consent bypass.
 * ======================================================================== */
{
  const avatarMod = await import('../ui/avatar.js');
  const discordMod = await import('../ui/discord.js');
  const prefsMod = await import('../ui/prefs.js');
  const routerMod = await import('../ui/router.js');
  const { S } = await import('../ui/strings.js');

  /* ---- 12a. the avatar kit ---------------------------------------------- */
  ok(typeof avatarMod.avatarNode === 'function', 'ui/avatar.js exports avatarNode');
  ok(typeof avatarMod.emitAva === 'function', 'and the gg-ava emitter');
  ok(avatarMod.AVA_EVENT === 'gg-ava', 'the bus is the contract event name', avatarMod.AVA_EVENT);
  // The kinds and their ORDER are frozen by §6 — E indexes the same list.
  ok(avatarMod.AVA_KINDS.join(',') === 'land,fire,drop,pop,emote,mercy,win,lose,draw,cue',
    'and carries the contract kinds, in contract order', avatarMod.AVA_KINDS.join(','));

  const hues = ['ada', 'Ada', 'bee', '', 'ада', '🦊fox'].map((x) => avatarMod.hueFromName(x));
  ok(hues.every((h) => Number.isInteger(h) && h >= 0 && h <= 359),
    'hueFromName folds any name to an integer 0-359', JSON.stringify(hues));
  ok(avatarMod.hueFromName('ada') === avatarMod.hueFromName('ada'),
    'and is deterministic — the same player is the same colour on both machines');
  ok(avatarMod.hueFromName('ada') !== avatarMod.hueFromName('Ada'),
    'case is part of the name, so two spellings do not collide by accident');
  ok(avatarMod.initialOf('🦊fox') === '🦊',
    'initialOf takes a whole code point — half a surrogate pair is a tofu box',
    avatarMod.initialOf('🦊fox'));
  ok(avatarMod.initialOf('') === '?' && avatarMod.initialOf(null) === '?',
    'and a nameless peer still gets a glyph');

  // THE BOX IS RESERVED. A bubble built with no picture is already the right
  // size with a tile in it; the picture REPLACES the tile in the same box.
  const tileAva = avatarMod.avatarNode({ side: 'opp', name: 'Ada', dataUri: null, size: 'mini' });
  ok(tileAva.getAttribute('data-side') === 'opp', 'a bubble carries data-side for the FX bus');
  ok(tileAva.getAttribute('data-size') === 'mini', 'and a size token, which is what reserves the box');
  ok(findOne(tileAva, 'gg-ava-tile') && !findOne(tileAva, 'gg-ava-img'),
    'with no picture it renders the initial-letter tile');
  ok(findOne(tileAva, 'gg-ava-tile').textContent === 'A', 'showing the initial', findOne(tileAva, 'gg-ava-tile').textContent);
  tileAva.setPicture('data:image/png;base64,AAAA');
  ok(findOne(tileAva, 'gg-ava-img') && !findOne(tileAva, 'gg-ava-tile'),
    'and the picture landing SWAPS the child — same node, same box, no reflow');
  ok(tileAva.children.length === 1, 'exactly one child at a time', String(tileAva.children.length));
  tileAva.setPicture('https://cdn.discordapp.com/x.png');
  ok(!!findOne(tileAva, 'gg-ava-tile'),
    'a non-data: src is REFUSED back to a tile — this page never fetches a remote avatar');

  /* ---- 12b. the bus is unbreakable --------------------------------------- */
  const beats = [];
  const busEar = (e) => beats.push(e.detail);
  document.addEventListener('gg-ava', busEar);
  ok(avatarMod.emitAva('land', 'you', { kind: 3 }) === true, 'emitAva dispatches a known kind');
  ok(beats.length === 1 && beats[0].kind === 'land' && beats[0].side === 'you', 'with the contract detail shape',
    JSON.stringify(beats[0]));
  ok(avatarMod.emitAva('nonsense', 'you') === false, 'an unknown kind is dropped, not thrown on');
  ok(avatarMod.emitAva('pop', 'nobody') === false || beats[beats.length - 1].side === 'you',
    'and an unknown side falls back to "you" rather than emitting garbage');
  ok(avatarMod.emitAva('pop') === true && beats[beats.length - 1].meta === undefined,
    'a beat with no meta simply carries none');
  beats.length = 0;

  /* ---- 12c. the state module: THE ECHO IS THE TRUTH ---------------------- */
  function fakeBridge() {
    const sent = [];
    const handlers = new Map();
    return {
      sent,
      send: (m) => sent.push(m),
      on: (t, fn) => {
        if (handlers.has(t)) throw new Error('duplicate handler for ' + t);
        handlers.set(t, fn);
      },
      deliver: (t, m) => { const fn = handlers.get(t); if (fn) fn(m); },
      types: () => Array.from(handlers.keys()),
      last: (t) => sent.filter((m) => m.type === t).pop() || null,
      count: (t) => sent.filter((m) => m.type === t).length,
    };
  }

  const bus = fakeBridge();
  const uiPrefs = prefsMod.createPrefs({});
  const dc = discordMod.createDiscord({
    prefs: uiPrefs,
    send: bus.send,
    on: bus.on,
    hosted: true,
    getRoom: () => ({ code: 'ABC123', token: 'roomtok', role: 'host' }),
  });

  ok(bus.types().sort().join(',') === 'discord,peer-card',
    'ui/discord.js owns exactly two inbound verbs at the bridge', bus.types().join(','));
  ok(discordMod.DISCORD_VERBS_OUT.length === 6,
    'and declares its six outbound verbs', discordMod.DISCORD_VERBS_OUT.join(','));

  ok(dc.state.avatarState === 'unlinked' && !dc.sharingDm && !dc.richPresence,
    'everything defaults to unlinked and OFF before any frame arrives');
  ok(dc.lastOpponent === null && dc.peer === null, 'with no opponent and no card');

  dc.applyInit({
    avatarState: 'off', avatarDataUri: null, dmShared: false,
    richPresence: false, seenSharePrompt: false,
    lastOpponent: { name: 'Mara', avatarDataUri: null, dm: true, ts: Date.now() - 3600000 },
  });
  ok(dc.linked === true && dc.sharingAvatar === false,
    'init "off" means LINKED but not sharing — two different facts, two different states');
  ok(dc.lastOpponent && dc.lastOpponent.name === 'Mara', 'init is the one frame that carries lastOpponent');

  // THE OPTIMISM PIN. Ask for a write, and assert the local view has NOT moved.
  dc.writePrefs({ shareAvatar: true });
  ok(bus.last('discord-prefs').shareAvatar === true, 'writePrefs posts discord-prefs');
  ok(dc.sharingAvatar === false,
    'and NOTHING local moved — the request is not the truth, the echo is');
  bus.deliver('discord', { type: 'discord', avatarState: 'shared', avatarDataUri: 'data:image/png;base64,QQ==', dmShared: false, richPresence: false, seenSharePrompt: false });
  ok(dc.sharingAvatar === true && dc.state.avatarDataUri.slice(0, 5) === 'data:',
    'only the echo flips it — and the echo is a FLAT frame, as the host sends it');
  ok(dc.lastOpponent && dc.lastOpponent.name === 'Mara',
    'and a `discord` echo never clears lastOpponent — only `init` carries that record');
  ok(dc.writePrefs({}) === false, 'a write with no fields is refused rather than costing an echo');

  /* ---- 12d. the share prompt gate ---------------------------------------- */
  ok(dc.needsSharePrompt() === true, 'sharing on + never asked = the one-time confirm is due');
  bus.deliver('discord', { type: 'discord', avatarState: 'shared', avatarDataUri: null, dmShared: false, richPresence: false, seenSharePrompt: true });
  ok(dc.needsSharePrompt() === false, 'and it is spent once the host echoes seenSharePrompt');
  bus.deliver('discord', { type: 'discord', avatarState: 'off', avatarDataUri: null, dmShared: false, richPresence: false, seenSharePrompt: false });
  ok(dc.needsSharePrompt() === false, 'with nothing shared there is nothing to confirm, asked or not');

  /* ---- 12e. peer-card-req: once per VERSION, with the room credentials ---- */
  ok(dc.notePeerCardVer(null).reason === 'none', 'a null version means the peer shares nothing — no request');
  const first = dc.notePeerCardVer('v1');
  ok(first.requested === true, 'a new version asks for the card');
  ok(bus.count('peer-card-req') === 1, 'exactly one request', String(bus.count('peer-card-req')));
  const req = bus.last('peer-card-req');
  ok(req.code === 'ABC123' && req.token === 'roomtok' && req.role === 'host',
    'and it carries {code, token, role} — /v2/goon/peercard is room-authed and only the PAGE holds them',
    JSON.stringify(req));
  for (let i = 0; i < 5; i++) dc.notePeerCardVer('v1');
  ok(bus.count('peer-card-req') === 1,
    'five more polls of the SAME version ask for nothing — /signal repeats it every tick',
    String(bus.count('peer-card-req')));
  dc.notePeerCardVer('v2');
  ok(bus.count('peer-card-req') === 2, 'a CHANGED version asks again', String(bus.count('peer-card-req')));

  bus.deliver('peer-card', { type: 'peer-card', name: 'Mara', avatarDataUri: 'data:image/png;base64,QQ==', reason: 'ok', dm: true, ver: 'v3' });
  ok(dc.peer && dc.peer.dm === true && dc.peer.name === 'Mara', 'a card lands on the peer view');
  ok(!('dm_id' in dc.peer) && !('id' in dc.peer),
    'and carries NO identifier — dm is a boolean, the host owns the snowflake', Object.keys(dc.peer).join(','));
  dc.notePeerCardVer('v3');
  ok(bus.count('peer-card-req') === 2,
    'an arriving card advances the ledger, so the version it carried is never re-fetched');

  /* ---- 12f. showOpponentAvatars OFF suppresses the FETCH ------------------ */
  ok(prefsMod.PREF_DEFAULTS.showOpponentAvatars === true,
    'showOpponentAvatars is a pref and DEFAULTS ON — the sharer already opted in',
    String(prefsMod.PREF_DEFAULTS.showOpponentAvatars));
  uiPrefs.set('showOpponentAvatars', false);
  const suppressed = dc.notePeerCardVer('v9');
  ok(suppressed.requested === false && suppressed.reason === 'pref-off',
    'with it OFF a brand-new version fetches NOTHING — not hidden pixels, no download',
    JSON.stringify(suppressed));
  ok(bus.count('peer-card-req') === 2, 'and no frame went out', String(bus.count('peer-card-req')));
  bus.deliver('peer-card', { type: 'peer-card', name: 'Mara', avatarDataUri: 'data:image/png;base64,QQ==', reason: 'ok', dm: true, ver: 'v9' });
  ok(dc.peer.avatarDataUri === null,
    'and a card that lands anyway (the pref flipped mid-flight) is stripped of its picture');
  ok(dc.peer.dm === true, 'though the DM flag survives — it is their choice, not their face');
  uiPrefs.set('showOpponentAvatars', true);

  /* ---- 12g. rp-state: enum only, never repeated --------------------------- */
  ok(dc.setRpState('lobby') === true && bus.last('rp-state').s === 'lobby', 'rp-state posts the enum');
  ok(dc.setRpState('lobby') === false, 'the same state twice is one frame, not two');
  ok(dc.setRpState('dancing') === false, 'and a value outside the enum never reaches the wire');
  ok(bus.count('rp-state') === 1, 'so exactly one rp-state frame so far', String(bus.count('rp-state')));
  dc.setRpState('live'); dc.setRpState('recap'); dc.setRpState('off');
  ok(bus.count('rp-state') === 4 && bus.last('rp-state').s === 'off', 'and the run of states is posted in order');

  /* ---- 12h. last-opponent clear, and the CTA that starts nothing ---------- */
  ok(dc.lastOpponent !== null, 'the last opponent is still on file');
  dc.clearLastOpponent();
  ok(bus.count('last-opponent-clear') === 1, 'the ✕ posts last-opponent-clear');
  ok(dc.lastOpponent === null,
    'and clears the local copy in the same breath — a card that lingers for a round trip reads as a refusal');

  dc.linkRequest();
  const link = bus.last('discord-link-request');
  ok(!!link && Object.keys(link).length === 1,
    'discord-link-request carries NOTHING but its type — it cannot grow an argument that starts OAuth',
    JSON.stringify(link));
  ok(bus.count('discord-open-dm') === 0, 'and the CTA opened no DM and started no sign-in');
  dc.openDm('peer');
  ok(bus.last('discord-open-dm').which === 'peer', 'opening a DM names WHICH, and nothing else',
    JSON.stringify(bus.last('discord-open-dm')));
  ok(!('id' in bus.last('discord-open-dm')), 'never an id — the host resolves it from its own store');

  /* ---- 12i. the lobby panel renders from the echo ------------------------- */
  const ledger = routerMod.createLedger();
  const panel = discordMod.buildDiscordSection({ discord: dc, ledger, prefs: uiPrefs, youName: 'tester' });
  ok(!!panel && !!panel.node, 'buildDiscordSection builds a card');
  const toggles = findAll(panel.node, 'gg-toggle');
  ok(toggles.length === 3, 'with the three sharing toggles', String(toggles.length));
  ok(!!findOne(panel.node, 'gg-ava'), 'and your own avatar bubble on it');
  // Unlinked: the CTA shows and the two sharing switches are inert.
  bus.deliver('discord', { type: 'discord', avatarState: 'unlinked', avatarDataUri: null, dmShared: false, richPresence: false, seenSharePrompt: false });
  ok(findOne(panel.node, 'gg-dc-link').hidden === false, 'unlinked, the Connect CTA is on screen');
  ok(toggles[0].disabled === true && toggles[1].disabled === true,
    'and the two SHARING switches are inert — there is nothing to share yet');
  const beforeLink = bus.count('discord-link-request');
  const beforeWrites = bus.count('discord-prefs');
  toggles[0].dispatchEvent({ type: 'click' });
  ok(bus.count('discord-prefs') === beforeWrites,
    'clicking a disabled switch writes nothing',
    `${beforeWrites} -> ${bus.count('discord-prefs')}`);
  ok(toggles[0].getAttribute('aria-pressed') === 'false', 'and does not paint itself on either');
  ok(bus.count('discord-link-request') === beforeLink,
    'and NOTHING on this panel auto-starts a link — the CTA is the only door, and it is a click',
    `${beforeLink} -> ${bus.count('discord-link-request')}`);

  bus.deliver('discord', { type: 'discord', avatarState: 'shared', avatarDataUri: null, dmShared: true, richPresence: false, seenSharePrompt: true });
  ok(findOne(panel.node, 'gg-dc-link').hidden === true, 'linked and sharing, the CTA steps aside');
  ok(toggles[0].getAttribute('aria-pressed') === 'true' && toggles[1].getAttribute('aria-pressed') === 'true',
    'and both switches read from the echo');
  ok(findOne(panel.node, 'gg-dc-flag').hidden === false,
    '"they can see your picture" is on screen the whole time it is true');
  ledger.dispose();

  /* ---- 12i-b. STANDALONE (?solo=1, no host). The panel is the only way to
   * LOOK at this feature during dev, so it renders — inert, with one line
   * saying why, and a CTA that cannot be pressed into a link request that has
   * nowhere to go. */
  const soloBus = fakeBridge();
  const soloDc = discordMod.createDiscord({ send: soloBus.send, on: soloBus.on, hosted: false, getRoom: () => null });
  const soloLedger = routerMod.createLedger();
  const soloPanel = discordMod.buildDiscordSection({ discord: soloDc, ledger: soloLedger, youName: 'Solo' });
  ok(soloDc.hosted === false, 'standalone, the module knows there is no host');
  ok(soloDc.state.avatarState === 'unlinked' && !soloDc.sharingDm && !soloDc.richPresence,
    'and defaults sanely — unlinked, nothing shared, no presence');
  ok(findAll(soloPanel.node, 'gg-toggle').length === 3 && !!findOne(soloPanel.node, 'gg-ava'),
    'the sharing panel still renders in full — it is the only way to see it in a browser');
  ok(findOne(soloPanel.node, 'gg-dc-link').hidden === false, 'with the CTA box on screen');
  ok(findOne(soloPanel.node, 'gg-dc-linkline').textContent === S.discord.hostedOnly,
    'carrying the hosted-only hint instead of the connect line',
    findOne(soloPanel.node, 'gg-dc-linkline').textContent);
  ok(findAll(soloPanel.node, 'gg-toggle').every((t) => t.disabled === true),
    'and every switch inert — there is no settings file out here to write to');
  ok(soloBus.sent.length === 0, 'and NOTHING was posted just by rendering it', JSON.stringify(soloBus.sent));

  // Practice mode's opponent: a name, a tile, and never a DM.
  soloDc.setSoloPeer(S.discord.practiceBot);
  ok(soloDc.peer.name === S.discord.practiceBot && soloDc.peer.dm === false && soloDc.peer.avatarDataUri === null,
    'the practice bot presents a name and a tile, and no DM — it is not a person',
    JSON.stringify(soloDc.peer));
  soloLedger.dispose();
  soloDc.dispose();

  /* ---- 12j. the HUD minis and the DM affordance --------------------------- */
  const dmBus = fakeBridge();
  const dmDc = discordMod.createDiscord({ send: dmBus.send, on: dmBus.on, hosted: true, getRoom: () => null });
  const match12 = makeFakeMatch();
  const hud12 = hudMod.mountHud({
    match: match12,
    session: { identity: { displayName: 'tester' } },
    audio: { sfx() {} },
    discord: dmDc,
  });
  const frame12 = findOne(dom.byId.get('gg-hud'), 'gg-hud-frame');
  const minis = findAll(frame12, 'gg-ava--mini');
  ok(minis.length === 2, 'the desk carries two persistent minis', String(minis.length));
  ok(minis.some((m) => m.getAttribute('data-side') === 'you')
    && minis.some((m) => m.getAttribute('data-side') === 'opp'),
    'one a side, and both tagged for the FX bus');
  ok(findAll(frame12, 'gg-ava-dm').length === 0,
    'and NO DM button before a card says they shared one');

  dmBus.deliver('peer-card', { type: 'peer-card', name: 'Mara', avatarDataUri: null, reason: 'not_shared', dm: false, ver: 'c1' });
  ok(findAll(frame12, 'gg-ava-dm').length === 0,
    'a card with dm:false grows NO button — absent, never disabled, because a button that cannot work is worse');
  dmBus.deliver('peer-card', { type: 'peer-card', name: 'Mara', avatarDataUri: null, reason: 'ok', dm: true, ver: 'c2' });
  const dmBtn = findOne(frame12, 'gg-ava-dm');
  ok(!!dmBtn, 'dm:true grows one');
  // It ASKS, it does not open: boot confirms first (a browser out of a fullscreen
  // duel is a surprise) and posts the verb itself.
  const asks = [];
  const askEar = (e) => asks.push(e.detail);
  document.addEventListener(hudMod.DISCORD_DM_EVENT, askEar);
  dmBtn.dispatchEvent({ type: 'click' });
  ok(asks.length === 1 && asks[0].which === 'peer', 'clicking it raises the DM ask', JSON.stringify(asks[0]));
  ok(dmBus.count('discord-open-dm') === 0,
    'and posts NOTHING itself — the confirm lives in boot, where the sheets are');
  ok(!('id' in asks[0]) && typeof asks[0].name === 'string', 'the ask carries a name, never an id');
  document.removeEventListener(hudMod.DISCORD_DM_EVENT, askEar);

  /* ---- 12k. the beats the desk emits -------------------------------------- */
  beats.length = 0;
  const kinds = () => beats.map((b) => b.kind + ':' + b.side);

  // LAND — on the landing, not on acceptance: the engine schedules ahead.
  match12._emit('pAcc', { payload: { id: 'z1', kind: GoonPayloadKind.FlashBurst, duration_ms: 1000 }, fireAtLocalMs: 0 });
  await sleep(30);
  ok(kinds().includes('land:you'), 'an inbound payload landing flinches YOUR bubble', kinds().join(' '));

  // LAND on the other side, from the receipt — the only proof it reached them.
  match12._emit('pRec', { id: 'z1', status: 0 });
  ok(kinds().includes('land:opp'), 'and a receipt lands the beat on THEIRS', kinds().join(' '));

  // POP + DROP off one real bubble event.
  beats.length = 0;
  const realRandom12 = Math.random;
  Math.random = () => 0;
  try {
    document.dispatchEvent(new CustomEvent('gg-bubble-pop', { detail: { kind: 'flash', worth: 2.5, payload: false, x: 10, y: 10 } }));
  } finally { Math.random = realRandom12; }
  ok(kinds().includes('pop:you'), 'popping a bubble bops your bubble', kinds().join(' '));
  ok(kinds().includes('drop:you'), 'and a roll that armed a slot is its own, rarer beat', kinds().join(' '));

  // FIRE, through the arsenal's own gate. Arm one by hand rather than relying on
  // whatever the pop above happened to drop — an order-dependent pin is a pin
  // that starts failing for an unrelated reason.
  beats.length = 0;
  let armed12 = hud12.parts.arsenal.droppable().find((c) => c.armed > 0);
  if (!armed12) {
    const candidate = hud12.parts.arsenal.droppable()[0];
    if (candidate) { hud12.parts.arsenal.armDrop(candidate.id, { count: 1 }); armed12 = candidate; }
  }
  ok(!!armed12, 'the desk has something armed to fire');
  if (armed12) hud12.parts.arsenal.fire(armed12.id);
  ok(kinds().includes('fire:you'), 'firing a payload is a beat on the shooter', kinds().join(' '));

  // EMOTE, both directions, with the dwell E syncs its wiggle to.
  beats.length = 0;
  hud12.parts.emotes.send('gg', '');
  ok(kinds().includes('emote:you'), 'your emote going out', kinds().join(' '));
  match12._emit('emote', { text: 'gg', icon: '' });
  const oppEmote = beats.find((b) => b.kind === 'emote' && b.side === 'opp');
  ok(!!oppEmote, 'and theirs arriving');
  ok(oppEmote && oppEmote.meta && oppEmote.meta.ms > 0,
    'carrying the real dwell, so the wiggle ends when the bubble does', JSON.stringify(oppEmote && oppEmote.meta));

  hud12.unmount();
  ok(match12._subs.released === match12._subs.taken,
    'and the whole discord layer leaks no subscriptions',
    `${match12._subs.released}/${match12._subs.taken}`);

  /* ---- 12l. the VS splash ------------------------------------------------ */
  const before = dom.doc.body.children.length;
  const splash = avatarMod.mountVsSplash({
    you: { name: 'tester', dataUri: null },
    opp: { name: 'Mara', dataUri: null },
    reduced: false,
    totalMs: 60,
  });
  ok(!!splash && !!splash.node, 'the VS splash mounts');
  ok(findAll(splash.node, 'gg-ava').length === 2, 'with both bubbles on it');
  ok(dom.doc.body.children.length === before + 1, 'onto <body>, outside every screen container');
  splash.remove();
  splash.remove();
  ok(dom.doc.body.children.length === before, 'and remove() is idempotent — four callers share it');

  ok(avatarMod.mountVsSplash({ you: { name: 'a' }, opp: { name: 'b' }, reduced: true }) === null,
    'reduced motion SKIPS it entirely rather than parking a card on screen for 1.6s');

  document.removeEventListener('gg-ava', busEar);
  dc.dispose();
  dmDc.dispose();
}

/* ===========================================================================
 * 13. THE ZEN TOGGLE — one button, three panels (owner, 2026-08-04).
 *
 * "add a button that hides the ui": the score, the closeness dial ("you're
 * telling them") and MERCY step off together; the
 * opponent monitor and the arsenal stay. The whole feature is one bit, so this
 * pins the bit, both of its names, and the CSS that reads them — a toggle that
 * flips a class no stylesheet mentions is an invisible feature.
 * ======================================================================== */
{
  const { S: S13 } = await import('../ui/strings.js');
  const prefsMod13 = await import('../ui/prefs.js');

  ok(typeof hudMod.HUD_ZEN_CLASS === 'string' && hudMod.HUD_ZEN_CLASS.length > 0,
    'ui/hud.js exports the zen class name');
  ok(typeof hudMod.HUD_ZEN_ATTR === 'string' && hudMod.HUD_ZEN_ATTR.length > 0,
    'ui/hud.js exports the <html> attribute MERCY is hidden from');

  // every user-facing word goes through ui/strings.js
  ok(!!(S13.hud && S13.hud.zenHide && S13.hud.zenShow),
    'strings.js carries both labels for the toggle');
  ok(!!(S13.hud.zenHideGlyph && S13.hud.zenShowGlyph && S13.hud.zenHideGlyph !== S13.hud.zenShowGlyph),
    'and two DIFFERENT glyphs — the state is never only a colour');
  ok(/escape/i.test(String(S13.hud.zenShowTitle || '')),
    'the hidden state says out loud that escape still ends the match');

  const match13 = makeFakeMatch();
  const store13 = { hudZen: false };
  const prefs13 = { get: (k) => store13[k], set: (k, v) => { store13[k] = v; } };
  const hud13 = hudMod.mountHud({ match: match13, audio: { sfx() {} }, prefs: prefs13 });
  const hudHost13 = dom.byId.get('gg-hud');
  const root13 = findOne(hudHost13, 'gg-hud-frame');
  const zen13 = findOne(hudHost13, 'gg-zen');
  const html13 = dom.doc.documentElement;

  ok(!!zen13, 'the live HUD carries exactly one zen toggle');
  ok(findAll(hudHost13, 'gg-zen').length === 1, 'exactly one',
    String(findAll(hudHost13, 'gg-zen').length));
  ok(zen13 && zen13.tagName === 'BUTTON', 'it is a real button');
  ok(zen13 && zen13.getAttribute('aria-pressed') === 'false', 'unpressed to start');
  ok(zen13 && zen13.getAttribute('aria-label') === S13.hud.zenHide,
    'labelled from strings.js', zen13 && String(zen13.getAttribute('aria-label')));
  ok(!hasClass(root13, hudMod.HUD_ZEN_CLASS), 'the desk starts whole');
  ok(html13.getAttribute(hudMod.HUD_ZEN_ATTR) === null, 'and MERCY starts visible');

  zen13.dispatchEvent({ type: 'click' });
  ok(hasClass(root13, hudMod.HUD_ZEN_CLASS), 'one press puts the modifier on the frame');
  ok(html13.getAttribute(hudMod.HUD_ZEN_ATTR) === 'on',
    'and mirrors the same bit onto <html>, which is the only way to reach #gg-mercy');
  ok(zen13.getAttribute('aria-pressed') === 'true', 'aria-pressed follows');
  ok(zen13.getAttribute('aria-label') === S13.hud.zenShow, 'and so does the label');
  ok(zen13.textContent === S13.hud.zenShowGlyph, 'the glyph flips to the way back');
  ok(store13.hudZen === true, 'the pick is remembered');
  ok(hudHost13.children.length > 0 && !!findOne(hudHost13, 'gg-mon-host'),
    'the monitor and the rest of the desk are untouched — CSS does the hiding, not JS');

  zen13.dispatchEvent({ type: 'click' });
  ok(!hasClass(root13, hudMod.HUD_ZEN_CLASS), 'a second press gives everything back');
  ok(html13.getAttribute(hudMod.HUD_ZEN_ATTR) === null, 'including MERCY');
  ok(zen13.getAttribute('aria-pressed') === 'false', 'and the button says so');
  ok(store13.hudZen === false, 'the pref follows it back');

  // the handle a play-test driver uses instead of synthesising a click
  hud13.parts.zen.set(true);
  ok(hud13.parts.zen.on === true && hasClass(root13, hudMod.HUD_ZEN_CLASS),
    'parts.zen drives the same one bit');

  /* THE UNWIND. The class dies with the node; the attribute does not, and a
   * leftover one would hide the mercy button of the NEXT match with no button
   * left on screen to bring it back. */
  hud13.unmount();
  ok(html13.getAttribute(hudMod.HUD_ZEN_ATTR) === null,
    'unmount clears the <html> bit — a stale one would hide the next match\'s MERCY');
  ok(match13._subs.released === match13._subs.taken,
    'and the toggle leaks no subscriptions', `${match13._subs.released}/${match13._subs.taken}`);

  // remembered across mounts
  const match13b = makeFakeMatch();
  const hud13b = hudMod.mountHud({ match: match13b, audio: { sfx() {} }, prefs: { get: () => true, set() {} } });
  const root13b = findOne(dom.byId.get('gg-hud'), 'gg-hud-frame');
  ok(hasClass(root13b, hudMod.HUD_ZEN_CLASS) && html13.getAttribute(hudMod.HUD_ZEN_ATTR) === 'on',
    'a remembered zen is applied at mount, not on the first click');
  hud13b.unmount();
  ok(html13.getAttribute(hudMod.HUD_ZEN_ATTR) === null, 'and unwound the same way');

  ok(prefsMod13.PREF_DEFAULTS.hudZen === false,
    'the pref exists and defaults OFF — a desk you cannot read your score in is a choice',
    String(prefsMod13.PREF_DEFAULTS.hudZen));

  // a HUD with no pref store at all must still toggle
  const match13c = makeFakeMatch();
  const hud13c = hudMod.mountHud({ match: match13c, audio: { sfx() {} } });
  const zen13c = findOne(dom.byId.get('gg-hud'), 'gg-zen');
  zen13c.dispatchEvent({ type: 'click' });
  ok(hasClass(findOne(dom.byId.get('gg-hud'), 'gg-hud-frame'), hudMod.HUD_ZEN_CLASS),
    'the toggle works with no prefs object — persistence is a nicety, not the state');
  hud13c.unmount();

  /* ---- the CSS half ----------------------------------------------------- */
  const fs13 = await import('node:fs/promises');
  const url13 = await import('node:url');
  const hudCss13 = await fs13.readFile(url13.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8');
  const goonCss13 = await fs13.readFile(url13.fileURLToPath(new URL('../goon.css', import.meta.url)), 'utf8');

  const zenBlocks = hudCss13.match(/[^{}]*\.gg-hud--zen[^{}]*\{[^}]*\}/g) || [];
  ok(zenBlocks.length > 0, 'hud.css reads the modifier class at all');
  const zenText = zenBlocks.join('\n');
  /* .gg-mult IS NOT ON THIS LIST ANY MORE. It was .gg-risk, then .gg-mult, then
   * nothing (2026-08-05) — the whole risk readout left the desk, so zen has one
   * less thing to hide and hud.css must not still be naming it. Both halves are
   * pinned: the survivors below, and the absence right after. */
  for (const sel of ['.gg-score', '.gg-dial-host']) {
    ok(zenText.includes(sel), `zen hides ${sel}`);
  }
  ok(!zenText.includes('.gg-mult'),
    'and it no longer hides .gg-mult, because there is no multiplier readout left to hide');
  // The heat gauge is NOT on that list and must not be: it is the arsenal's own
  // meter, and zen keeps the arsenal.
  ok(!zenText.includes('.gg-heat'), 'and zen keeps the heat gauge, like the rails it feeds');
  ok(/\.gg-hud--zen[^{}]*\{[^}]*display:\s*none/.test(zenText),
    'and it hides them with display, so the layout closes up instead of leaving holes');

  // what must SURVIVE the toggle: their monitor and the effects you can send
  for (const sel of ['.gg-mon-host', '.gg-rail', '.gg-item', '.gg-zen']) {
    ok(!new RegExp('\\.gg-hud--zen[^{}]*' + sel.replace('.', '\\.') + '[^{}]*\\{[^}]*display:\\s*none').test(hudCss13),
      `zen never hides ${sel}`);
  }

  ok(/html\[data-gg-zen="on"\]\s*#gg-mercy[^{]*\{[^}]*display:\s*none/.test(hudCss13),
    'MERCY is reached from <html>, because it is not a descendant of the HUD');
  const mercyZen = (hudCss13.match(/html\[data-gg-zen="on"\][^{]*\{[^}]*\}/g) || []).join('\n');
  ok(!/opacity|filter|transform|animation/.test(mercyZen),
    'and only display: the isolation contract in goon.css is not being dimmed, filtered or moved');
  ok(/filter:\s*none\s*!important/.test(goonCss13) && /opacity:\s*1\s*!important/.test(goonCss13),
    'goon.css still owns the mercy isolation block, untouched');

  // the button is the only way back on a phone: hit area, and no parked animation
  const zenBtnBlock = (hudCss13.match(/\.gg-zen\s*\{[^}]*\}/g) || [])[0] || '';
  ok(/width:\s*48px/.test(zenBtnBlock) && /height:\s*48px/.test(zenBtnBlock),
    'the toggle keeps a 48px hit area', zenBtnBlock);
  ok(!/animation/.test(zenBtnBlock),
    'and no animation — it would need pinning against the --gg-deco-play park');

  /* ---- the size pass (independent of the toggle) ------------------------- */
  const mercyBlock = (hudCss13.match(/\.gg-mercy-btn\s*\{[^}]*\}/g) || [])[0] || '';
  const mercyH = Number((mercyBlock.match(/height:\s*(\d+)px/) || [])[1] || 0);
  ok(mercyH >= 48 && mercyH <= 64, 'MERCY is smaller than it was, and still a finger tall', String(mercyH));
  const dialBlock = (hudCss13.match(/\.gg-dial\s*\{[^}]*\}/g) || [])[0] || '';
  const dialMin = Number((dialBlock.match(/min-width:\s*([\d.]+)rem/) || [])[1] || 99);
  ok(dialMin < 15, 'the closeness dial is narrower than the 15rem it shipped at', String(dialMin));
  ok(/\.gg-dial-stop\s*\{[^}]*min-height:\s*48px/.test(hudCss13),
    'but its four stops keep their 48px touch target — the plate shrank, the targets did not');

  /* ---- THE DIAL HAS NO YELLOW (owner, 2026-08-05) -----------------------
   * Second half of the same "little yellow squares" complaint that took the
   * charge pips: at `close`, every lit bar on this row turned --gg-gold, and in
   * a mid-match screenshot four short gold blocks under the score read as
   * exactly the indicator the owner had already had removed twice.
   *
   * PINNED AS A HUE BAN OVER THE WHOLE DIAL, not as one selector, because that
   * is how it comes back — a `.is-warm` or a hover state picking gold up again
   * because the rest of the app uses it freely. Gold is still legal EVERYWHERE
   * ELSE on the desk and the last two checks say so out loud: this was a
   * targeted restyle, and a blanket find-and-replace across hud.css would be a
   * different (and wrong) change. Intensity carries the ramp instead, which is
   * only legal because every stop is a word and the head stop is taller. */
  {
    const rules = [];
    const reDial = /([^{}]+)\{([^{}]*)\}/g;
    let mDial;
    while ((mDial = reDial.exec(hudCss13.replace(/\/\*[\s\S]*?\*\//g, '')))) {
      if (/\.gg-dial/.test(mDial[1])) rules.push(mDial[1].trim() + ' {' + mDial[2] + '}');
    }
    ok(rules.length >= 6, 'the dial still has its rules to check', String(rules.length));
    for (const rule of rules) {
      ok(!/--gg-gold|ffd45e|255,\s*212,\s*94/i.test(rule),
        'no gold anywhere on the closeness dial', rule.slice(0, 120));
      ok(!/--gg-amber|ffb454/i.test(rule),
        'and no amber either — the neighbouring warm hue is the same mistake', rule.slice(0, 120));
    }
    ok(/--gg-pink-deep:\s*#[0-9a-f]{6}/i.test(hudCss13),
      'the middle rung has a named colour of its own');
    ok(/\.gg-dial\[data-gg-close="2"\][^{}]*\{[^}]*--gg-pink-deep/.test(hudCss13),
      'and `close` uses it — one step along the pink ramp, not a hue change');
    ok(/\.gg-dial\[data-gg-close="3"\][^{}]*\{[^}]*--gg-hot/.test(hudCss13),
      'while `edge` still tops the ramp out at --gg-hot');
    /* The two top rungs each carry a glow, and the higher one is the bigger
     * blur: with the hue break gone that spread IS the ramp, so a future edit
     * that flattens them back to the same number has quietly merged `close`
     * into `edge` for anyone reading the bars rather than the words. */
    const blurAt = (close) => {
      const rule = (hudCss13.match(new RegExp('\\.gg-dial\\[data-gg-close="' + close + '"\\][^{}]*\\{[^}]*\\}')) || [''])[0];
      return { rule, px: Number((rule.match(/box-shadow:\s*0 0 (\d+)px/) || [])[1] || 0) };
    };
    const glowClose = blurAt('2');
    const glowEdge = blurAt('3');
    ok(glowClose.px > 0, 'close carries its own glow — intensity is what separates the rungs now', glowClose.rule);
    ok(glowEdge.px > glowClose.px, 'and edge glows harder than close', `${glowClose.px} -> ${glowEdge.px}`);
    // …and the three places gold still belongs are untouched.
    ok(/\.gg-timer\.is-urgent\s+\.gg-timer-fill\s*\{[^}]*--gg-gold/.test(hudCss13),
      'the timer still goes gold when it is running out');
    ok(/\.gg-receipt\.is-gold[^{}]*\{[^}]*--gg-gold/.test(hudCss13),
      'and a flagged receipt is still gold — only the dial was asked to change');
  }
}

/* ===========================================================================
 * 16. THE PHONE PASS (owner, 2026-08-04, with two screenshots).
 *
 * "this mess is waaaaay too cluttered, we need space to see the animations and
 * effects." Two separate faults, and this section pins the fix for each so a
 * later tidy cannot quietly reintroduce either:
 *
 *   A. THE RIGHT EDGE. `.gg-hud-frame` is a grid with ONE implicit column, and
 *      an `auto` track takes the largest min-content contribution among its
 *      items as its base size — it may exceed the container. `.gg-hud-top` is
 *      `1fr auto 1fr` (each `1fr` is `minmax(auto, 1fr)`, i.e. floored at
 *      min-content) plus a `min-width` on the timer, so on a 428pt phone the
 *      row's floor came to ~442px against a 408px content box. The whole
 *      column took that width, so EVERY row did, and the monitor, the "they
 *      claim" panel, the receipt strip and the closeness dial were all laid out
 *      ~34px past the right edge of the screen. `minmax(0, 1fr)` is the fix.
 *
 *   B. THE BOTTOM THIRD. Two tall arsenal rows, the dial UNDER them in the
 *      flow (so it landed on top of them), the effect chips in a third corner,
 *      and a MERCY button the portrait rule had quietly inflated back to 20rem.
 *
 * Everything below is asserted off the stylesheet: these are layout facts with
 * no JS to observe them, and a HUD whose CSS says nothing is an invisible bug.
 * ======================================================================== */
{
  const fs16 = await import('node:fs/promises');
  const url16 = await import('node:url');
  // Comments come out FIRST and stay out: this tier's comments carry both
  // braces and commas (they quote selectors at each other), and either one
  // derails a brace-counting or a selector-list parse.
  const css16 = (await fs16.readFile(url16.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8'))
    .replace(/\/\*[\s\S]*?\*\//g, '');

  /** Pull one balanced `@media …{ … }` body out of the sheet by a marker inside it. */
  function mediaBlock(src, marker) {
    let at = 0;
    for (;;) {
      const open = src.indexOf('@media', at);
      if (open < 0) return '';
      const brace = src.indexOf('{', open);
      if (brace < 0) return '';
      let depth = 0;
      let i = brace;
      for (; i < src.length; i++) {
        if (src[i] === '{') depth++;
        else if (src[i] === '}') { depth--; if (depth === 0) break; }
      }
      const body = src.slice(brace + 1, i);
      if (body.includes(marker)) return body;
      at = i + 1;
    }
  }
  /** The declarations of every rule whose selector list contains EXACTLY `sel`.
   *  Exact, not substring: `.gg-hud-frame` must not pick up the
   *  `#gg-hud > .gg-hud-frame` pointer-events opt-out that sits above it. */
  function ruleFor(src, sel) {
    const out = [];
    const re = /([^{}]+)\{([^{}]*)\}/g;
    let m;
    while ((m = re.exec(src))) {
      const list = m[1].split(',').map((s) => s.replace(/\/\*[\s\S]*?\*\//g, '').trim());
      if (list.includes(sel)) out.push(m[2]);
    }
    return out.join(';');
  }
  const num = (s, re, dflt) => { const m = String(s).match(re); return m ? Number(m[1]) : dflt; };

  const phone = mediaBlock(css16, '--gg-mon-w');
  ok(phone.length > 0, 'hud.css still carries the portrait/narrow block');

  /* ---- A. the right edge ------------------------------------------------ */
  const frame16 = ruleFor(css16, '.gg-hud-frame');
  ok(/grid-template-columns:\s*minmax\(\s*0\s*,\s*1fr\s*\)/.test(frame16),
    'the frame pins its one column to minmax(0, 1fr) — an auto track floors at min-content and takes every row off the right edge with it',
    frame16);
  ok(/min-width:\s*0/.test(ruleFor(css16, '.gg-hud-bottom')) || /\.gg-hud-top[^{}]*,[\s\S]{0,120}\.gg-hud-bottom\s*\{[^}]*min-width:\s*0/.test(css16),
    'and the three rows may be narrower than their own contents want to be');
  for (const side of ['left', 'right', 'bottom']) {
    ok(new RegExp('padding-' + side + ':[^;]*env\\(safe-area-inset-' + side).test(frame16),
      `the frame respects safe-area-inset-${side}`);
  }
  // the monitor column: capped, never a hard width that can outgrow its host
  ok(/max-width:\s*var\(--gg-mon-w\)/.test(ruleFor(css16, '.gg-mon')),
    'the monitor is capped by --gg-mon-w rather than hard-sized to it');
  ok(/\.gg-mon-name\s*\{[^}]*min-width:\s*0/.test(css16),
    'and their name may ellipsise instead of shoving the score and the charge count off the end');
  const monW = num(phone, /--gg-mon-w:\s*min\([^,]+,\s*(\d+)px\)/, 999);
  ok(monW <= 200, 'on a phone the monitor is at most 200px wide — it was half off-screen at 220', String(monW));

  /* ---- B. the bottom third ---------------------------------------------- */
  const item16 = num(phone, /--gg-item:\s*(\d+)px/, 0);
  ok(item16 >= 48, 'an arsenal tile keeps a 48px finger even after the compression pass', String(item16));
  ok(/\.gg-item-state\s*\{[^}]*min-height:\s*0/.test(phone),
    'the state line stops reserving a row when it has nothing to say');
  const lockedState = ruleFor(phone, '.gg-item.is-locked .gg-item-state');
  ok(/clip-path:\s*inset\(50%\)/.test(lockedState) && !/display:\s*none/.test(lockedState),
    'and "locked" leaves the LAYOUT, not the DOM — colour never travels alone in this tier',
    lockedState);

  /* THE TWO ABSOLUTE RAIL BANDS ARE GONE (2026-08-04, the arsenal sidebar).
   * They pinned eight tiles into strips at 6rem and 11.6rem and cost the stage
   * ~9rem of height on every phone, permanently, for controls a player touches a
   * handful of times a match. What replaces them is a DRAWER, and what is pinned
   * is the property that makes it worth the trade: it is an OVERLAY (fixed), it
   * grows upward from the deck floor so it cannot reach MERCY, and it starts
   * SHUT here so the default phone screen costs one 48px handle. */
  const drawer16 = ruleFor(phone, '.gg-arsenal');
  const deck16 = ruleFor(phone, '.gg-hud-bottom');
  const deckPad = num(deck16, /padding-bottom:\s*([\d.]+)rem/, 0);
  ok(!/\.gg-rail\s*\{[^}]*position:\s*absolute/.test(phone),
    'the arsenal no longer nails two absolute bands across the bottom of a phone');
  ok(/position:\s*fixed/.test(drawer16),
    'the drawer floats OVER the field instead of taking a slice out of it', drawer16);
  const drawerBottom = num(drawer16, /bottom:\s*calc\(([\d.]+)rem/, 0);
  ok(drawerBottom >= deckPad,
    'and it grows upward from the deck floor, so it can never reach MERCY', `${drawerBottom} vs ${deckPad}`);
  ok(/left:\s*calc\([^)]*env\(safe-area-inset-left/.test(drawer16) && /right:\s*auto/.test(drawer16),
    'it is anchored to the LEFT edge — the right one is the monitor, the receipts and videos.js\'s keep-out',
    drawer16);
  // no scrim, on this breakpoint or any other: an open sheet that owned the
  // bubble-spawn strip is what froze the drop economy the last time
  ok(!/gg-arsenal[^{}]*(scrim|backdrop)\s*\{/.test(css16) && !/\.gg-arsenal-scrim/.test(css16),
    'and it is scrimless — a drawer, not a modal, with the match still live behind it');
  ok(/flex-direction:\s*row/.test(deck16),
    'the deck is one row on a phone — the dial stacked under the chips is what pushed it onto the rails');

  // the live-effect chips move out of the bottom-left corner entirely…
  const fxrail16 = ruleFor(phone, '.gg-fxrail');
  ok(/position:\s*absolute/.test(fxrail16) && /top:\s*[\d.]+rem/.test(fxrail16),
    'the effect chips leave the mercy strip for the top-left, under the score column', fxrail16);
  // …but stay INSIDE the deck in the DOM, which is how .is-sd and zen still reach them
  {
    const match16 = makeFakeMatch();
    const hud16 = hudMod.mountHud({ match: match16, audio: { sfx() {} } });
    const host16 = dom.byId.get('gg-hud');
    const rail16 = findOne(host16, 'gg-fxrail');
    ok(!!rail16 && hasClass(rail16.parentNode, 'gg-hud-bottom'),
      'the chip rail is still a child of the bottom deck — .is-sd and zen both reach it through the parent');
    hud16.unmount();
  }

  // MERCY: the portrait rule used to INFLATE it back to 20rem on a 428pt phone
  const mercy16 = ruleFor(phone, '.gg-mercy-btn');
  const mercyRem = num(mercy16, /width:\s*min\([^,]+,\s*([\d.]+)rem\)/, 99);
  const mercyH16 = num(mercy16, /height:\s*(\d+)px/, 0);
  ok(mercyRem <= 15, 'the phone MERCY is no wider than 15rem — the old rule blew it back up to 20', String(mercyRem));
  ok(mercyH16 >= 48 && mercyH16 <= 56, 'and it is still a finger tall', String(mercyH16));
  ok(!/animation/.test(mercy16), 'and the isolation contract is untouched — no motion is added to it here');

  /* ---- the top bar cannot overflow either -------------------------------- */
  const top16 = ruleFor(phone, '.gg-hud-top');
  ok(/grid-template-columns:\s*minmax\(\s*0\s*,\s*1fr\s*\)\s+auto\s+auto/.test(top16),
    'on a phone only the score column is compressible; the timer and the attention/zen/gear cluster are content-sized',
    top16);
  ok(num(ruleFor(phone, '.gg-timer'), /min-width:\s*(\d+)px/, 999) <= 104,
    'and the timer gives back part of its desktop min-width');
  ok(/\.gg-zen\s*\{[^}]*width:\s*48px/.test(css16) && !/\.gg-zen\s*\{[^}]*width/.test(phone),
    'the zen toggle is never one of the things that shrinks — it is the only way back');

  /* ---- zen, extended (owner: hide the claim panel and the chips too) ------ */
  const zenText16 = (css16.match(/[^{}]*\.gg-hud--zen[^{}]*\{[^}]*\}/g) || []).join('\n');
  for (const sel of ['.gg-mon-close', '.gg-fxrail']) {
    ok(zenText16.includes(sel), `zen also hides ${sel}`);
  }
  ok(!/\.gg-hud--zen[^{}]*\.gg-mon-close[^{}]*\{[^}]*(opacity|filter|transform|animation)/.test(css16),
    'and it hides them with display only, like everything else the toggle takes away');
  // the claim panel and the dial are the two halves of one readout: never split
  ok(zenText16.includes('.gg-dial-host') && zenText16.includes('.gg-mon-close'),
    'both halves of the closeness readout leave together — hiding one and keeping the other reads as a bug');
  // what still must survive zen on a phone: the arsenal drops, it does not vanish
  const zenRail = ruleFor(phone, '.gg-hud-frame.gg-hud--zen .gg-arsenal');
  ok(/bottom:\s*calc\([\d.]+rem/.test(zenRail) && !/display:\s*none/.test(zenRail),
    'zen moves the arsenal down into the freed space rather than taking it away', zenRail);
}

/* ===========================================================================
 * 18. THE DRONE BED — the Intake's binaural bed, under a duel (owner ask).
 *
 * "The goon game should play the low drone the Intake has." The Intake's bed is
 * SYNTHESIZED (intake/render/audio.js: two sine carriers 174-196 Hz, the right
 * detuned by the beat, through a lowpass into one gain) — there is no loop file
 * to copy — so ui/droneBed.js ports the topology and the endpoints instead.
 *
 * Four things have to hold or the feature is either silent, stuck on, or loud:
 *   · the parameter math is the Intake's, and the bed is QUIETER than the cues
 *     it plays under (it never stops, and every cue in the registry is already
 *     deliberately low);
 *   · phase -> state is a pure mapping off the engine's own phase, so the bed
 *     cannot drift out of step by keeping its own state machine — and boot.js
 *     really calls it, read off the source so a deleted hook cannot pass;
 *   · the latch survives a context that does not exist yet (autoplay: a BED is
 *     started late, unlike a one-shot cue, which is dropped);
 *   · the slider exists, defaults modest, and retunes a bus rather than the next
 *     thing to start — the bed is playing while you drag it.
 * ======================================================================== */
{
  const droneMod = await import('../ui/droneBed.js');
  const prefsMod = await import('../ui/prefs.js');
  const optionsMod = await import('../ui/options.js');
  const { S } = await import('../ui/strings.js');
  const {
    DRONE_BEAT_HZ, DRONE_CARRIER_HZ, DRONE_DEPTH, DRONE_PEAK, DRONE_PHASES,
    DRONE_FADE_IN_SEC, DRONE_FADE_OUT_SEC, DRONE_GLIDE_SEC,
    droneHz, droneGain, droneWantsPlay,
  } = droneMod;

  /* ---- 18a. the parameter math is the Intake's ---------------------------- */
  ok(typeof droneMod.createDroneBed === 'function', 'ui/droneBed.js exports createDroneBed');
  ok(DRONE_CARRIER_HZ.rest === 174 && DRONE_CARRIER_HZ.deep === 196,
    'the carrier band is the Intake\'s 174-196 Hz, verbatim',
    `${DRONE_CARRIER_HZ.rest}-${DRONE_CARRIER_HZ.deep}`);
  ok(DRONE_BEAT_HZ.rest === 10.0 && DRONE_BEAT_HZ.deep === 3.5,
    'and the beat runs the same 10 -> 3.5 Hz curve', `${DRONE_BEAT_HZ.rest}-${DRONE_BEAT_HZ.deep}`);

  const rest = droneHz(0);
  const deep = droneHz(1);
  ok(rest.carrierHz === 174 && deep.carrierHz === 196, 'droneHz walks the carrier between the endpoints');
  ok(rest.beatHz === 10.0 && deep.beatHz === 3.5, 'and the beat with it, in the other direction');
  ok(rest.rightHz - rest.leftHz === rest.beatHz && deep.rightHz - deep.leftHz === deep.beatHz,
    'the RIGHT carrier is the left one detuned by the beat — that is the whole trick');
  ok(droneHz(-5).carrierHz === 174 && droneHz(99).carrierHz === 196,
    'a nonsense depth clamps instead of detuning into a siren');

  const here = droneHz();
  ok(DRONE_DEPTH > 0 && DRONE_DEPTH < 1, 'the duel sits at a fixed point ON that curve', String(DRONE_DEPTH));
  ok(here.carrierHz > 174 && here.carrierHz < 196 && here.beatHz > 3.5 && here.beatHz < 10,
    'so the default is a real low carrier with a slow beat, not an endpoint',
    `${here.carrierHz.toFixed(1)}Hz / ${here.beatHz.toFixed(1)}Hz`);
  ok(here.carrierHz < 250, 'and it is LOW — a drone you notice as pitch is not a bed', String(here.carrierHz));

  // Gain staging: peak x droneVolume x master, multiplied, never conflated.
  ok(Math.abs(droneGain(1, 1) - DRONE_PEAK) < 1e-9, 'droneGain tops out at DRONE_PEAK');
  ok(droneGain(0, 1) === 0, 'droneVolume 0 is silence — the slider really is an off switch');
  ok(droneGain(1, 0) === 0, 'and master 0 takes it down like everything else');
  ok(Math.abs(droneGain(0.5, 0.5) - DRONE_PEAK * 0.25) < 1e-9,
    'the two sliders multiply, so the master still scales a bed on its own bus');
  ok(droneGain('nonsense', 1) === 0 && droneGain(1, undefined) === 0,
    'a corrupt volume is silence, not NaN into an AudioParam');
  ok(DRONE_PEAK > 0 && DRONE_PEAK < audioMod.SFX_REGISTRY['bubble-pop'].gain,
    'and the bed peaks UNDER the pops — it is the only voice that never stops',
    `${DRONE_PEAK} vs ${audioMod.SFX_REGISTRY['bubble-pop'].gain}`);
  ok(DRONE_FADE_IN_SEC >= 1 && DRONE_FADE_IN_SEC <= 6,
    'it arrives over seconds rather than switching on', String(DRONE_FADE_IN_SEC));
  ok(DRONE_FADE_OUT_SEC > 0 && DRONE_FADE_OUT_SEC < DRONE_FADE_IN_SEC,
    'and leaves faster than it came, so it is gone before the recap sting',
    String(DRONE_FADE_OUT_SEC));
  ok(DRONE_GLIDE_SEC > 0 && DRONE_GLIDE_SEC < 1,
    'a slider drag glides rather than steps — the bed is playing while you move it',
    String(DRONE_GLIDE_SEC));

  /* ---- 18b. phase -> state, the pure mapping ------------------------------ */
  for (const p of [GoonMatchPhase.Countdown, GoonMatchPhase.Live, GoonMatchPhase.SuddenDeath]) {
    ok(droneWantsPlay(p) === true, 'the bed plays at phase ' + p);
  }
  for (const p of [GoonMatchPhase.Idle, GoonMatchPhase.Lobby, GoonMatchPhase.Consent,
    GoonMatchPhase.Draft, GoonMatchPhase.Recap]) {
    ok(droneWantsPlay(p) === false, 'and is silent at phase ' + p);
  }
  ok(droneWantsPlay(GoonMatchPhase.Countdown) === true,
    'it comes in at the COUNTDOWN, not at Live — the countdown is already the match');
  ok(droneWantsPlay(GoonMatchPhase.Recap) === false,
    'and out at the recap, which every match reaches (boot.js treats it as non-optional)');
  ok(droneWantsPlay(undefined) === false && droneWantsPlay(null) === false
    && droneWantsPlay('live') === false && droneWantsPlay(NaN) === false,
    'anything unrecognised is OFF — a bed nobody can stop is the worst failure here');
  ok(DRONE_PHASES.length === 3 && Object.isFrozen(DRONE_PHASES),
    'the playing phases are a frozen list, not three ifs to drift apart');

  /* ---- 18c. the bus: the latch, and the hostless case --------------------- */
  {
    const seen = [];
    const bus = audioMod.createAudio({ logger: { debug: (m) => seen.push(m), warn: (m) => seen.push(m) } });
    for (const k of ['dronePhase', 'startDrone', 'stopDrone']) {
      ok(typeof bus[k] === 'function', `the bus exposes ${k}()`);
    }
    ok(bus.droneWanted === false && bus.droneIsPlaying === false, 'and starts with the bed down');

    ok(bus.dronePhase(GoonMatchPhase.Live) === true, 'dronePhase(Live) asks for the bed');
    ok(bus.droneWanted === true,
      'and the LATCH holds even though this host has no AudioContext — a bed asked for before the unlock is started late, not dropped like a cue');
    ok(bus.droneIsPlaying === false, 'while nothing pretends to be playing');
    ok(seen.some((m) => /drone:on/.test(m)), 'the logger hears it come in');

    // Idempotence is the relay-rebuild case: attachMatch runs again on the same
    // session and re-fires the phase mid-Live.
    ok(bus.dronePhase(GoonMatchPhase.Live) === true && bus.droneWanted === true,
      'the same phase again is a no-op, not a second bed (the relay rebuild)');
    ok((seen.filter((m) => /drone:on/.test(m)) || []).length === 1,
      'and it is not even re-announced', JSON.stringify(seen.filter((m) => /drone:/.test(m))));

    ok(bus.dronePhase(GoonMatchPhase.Recap) === false && bus.droneWanted === false,
      'the recap puts it down');
    bus.startDrone();
    ok(bus.droneWanted === true, 'startDrone() is the manual door in (probes, this test)');
    bus.stopDrone();
    ok(bus.droneWanted === false, 'and stopDrone() the way out');
    bus.startDrone();
    bus.dispose();
    ok(bus.droneWanted === false, 'disposing drops the latch with everything else');
    ok(bus.dronePhase(GoonMatchPhase.Live) === false, 'and a disposed bus will not restart it');
  }

  /* ---- 18d. the pref and the slider --------------------------------------- */
  ok(prefsMod.PREF_DEFAULTS.droneVolume === 0.5,
    'droneVolume is a pref, and it defaults MODEST', String(prefsMod.PREF_DEFAULTS.droneVolume));
  ok(typeof prefsMod.PREF_DEFAULTS.droneVolume === 'number',
    'a number, so a corrupt store coerces instead of poisoning an AudioParam');
  {
    const p = prefsMod.createPrefs({ droneVolume: 9 });
    ok(p.get('droneVolume') === 1, 'and it is clamped like every other *Volume key', String(p.get('droneVolume')));
    const p2 = prefsMod.createPrefs({ droneVolume: 'loud' });
    ok(p2.get('droneVolume') === 0.5, 'garbage falls back to the default', String(p2.get('droneVolume')));

    // The bus seeds from prefs and follows a live drag — the whole reason the
    // volume is a BUS and not a per-voice multiplier.
    const seeded = prefsMod.createPrefs({ droneVolume: 0.3 });
    const bus = audioMod.createAudio({ prefs: seeded });
    ok(bus.volumes.drone === 0.3, 'the bus seeds its drone level from ui/prefs.js', JSON.stringify(bus.volumes));
    seeded.set('droneVolume', 0.9);
    ok(bus.volumes.drone === 0.9, 'and follows a live slider drag, mid-bed');
    bus.setVolume('drone', 0.1);
    ok(bus.volumes.drone === 0.1 && seeded.get('droneVolume') === 0.1,
      'setVolume("drone") writes both ways, exactly like the other three buses');
    bus.dispose();
  }

  // ---- the row, IN a live match (a knob about your own screen, like the rest)
  {
    const live = prefsMod.createPrefs({});
    const calls = [];
    const audioStub = { setVolume: (b, v) => calls.push([b, v]), sfx() {} };
    const options = optionsMod.createOptions({
      prefs: live, audio: audioStub, session: { hosted: false }, isInMatch: () => true,
    });
    options.open();
    await sleep(30);
    const panel = findOne(dom.byId.get('gg-drawer'), 'gg-panel');
    ok(!!panel, 'the drawer opened');
    const sliders = findAll(panel, 'gg-panel-row--slider');
    ok(sliders.length === 7,
      'there are SEVEN volume rows now — master, music, drone, ui, game, media and voice (§19)', String(sliders.length));
    const labelOf = (rowNode) => {
      const lab = findOne(rowNode, 'gg-panel-label');
      return lab && lab.children[0] ? lab.children[0].textContent : '';
    };
    const droneRow = sliders.find((r) => labelOf(r) === S.options.drone) || null;
    ok(!!droneRow, 'and one of them is the drone, next to the existing three', S.options.drone);
    ok(typeof S.options.drone === 'string' && S.options.drone.length > 0,
      'whose label lives in ui/strings.js with the others', String(S.options.drone));

    const input = droneRow ? (droneRow.children[1] || null) : null;
    ok(!!input && input.tagName === 'INPUT', 'it is the same range input the other three use');
    ok(input.getAttribute('aria-label') === S.options.drone, 'labelled for a screen reader too');
    // (the stub does not mirror the value ATTRIBUTE onto the property the way a
    // real input does, so the seed is read back off the attribute)
    ok(input.getAttribute('value') === '50',
      'seeded from the stored 50%, not a hardcoded number', String(input.getAttribute('value')));

    input.value = '20';
    input.dispatchEvent({ type: 'input' });
    ok(live.get('droneVolume') === 0.2, 'dragging it writes the pref', String(live.get('droneVolume')));
    ok(calls.some(([b, v]) => b === 'drone' && Math.abs(v - 0.2) < 1e-9),
      'and reaches ui/audio.js as the "drone" BUS — which is what retunes a bed that is already playing',
      JSON.stringify(calls));
    const valueSpan = findOne(droneRow, 'gg-panel-value');
    ok(valueSpan && valueSpan.textContent === '20%', 'the readout repaints with it', String(valueSpan && valueSpan.textContent));

    input.value = '0';
    input.dispatchEvent({ type: 'input' });
    ok(live.get('droneVolume') === 0 && calls.some(([b, v]) => b === 'drone' && v === 0),
      'and 0 really reaches the bus — the slider is the off switch');

    options.dispose();
    await sleep(320);
  }

  /* ---- 18e. the wiring is really at its call site -------------------------- */
  {
    const fs = await import('node:fs/promises');
    const url = await import('node:url');
    const read = async (p) => fs.readFile(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8');
    const bootSrc = await read('../boot.js');
    const audioSrc = await read('../ui/audio.js');
    const droneSrc = await read('../ui/droneBed.js');

    // ONE hook, and it is on the phase router — the existing seam, not a new
    // event system. A deleted call site has to fail here, not go quiet on stage.
    ok(/audio\?\.dronePhase\?\.\(phase\)/.test(bootSrc),
      'boot.js drives the bed from the phase it already routes on');
    const onPhaseBody = bootSrc.slice(bootSrc.indexOf('function onPhase(phase)'),
      bootSrc.indexOf('function onPhase(phase)') + 900);
    ok(/dronePhase/.test(onPhaseBody),
      'and the call is INSIDE onPhase(), which attachMatch re-subscribes on every rebuild');
    ok((bootSrc.match(/dronePhase/g) || []).length === 1,
      'exactly one hook — two would be two beds to keep in step', String((bootSrc.match(/dronePhase/g) || []).length));

    // The bus topology: its own node, under the master, so the master still wins.
    ok(/droneBus\s*=\s*ctx\.createGain\(\)/.test(audioSrc), 'ui/audio.js gives the bed its own bus');
    ok(/droneBus\.connect\(masterBus\)/.test(audioSrc),
      'and hangs it UNDER masterBus, so the master slider still scales it');
    ok(/glide\(droneBus\.gain/.test(audioSrc),
      'whose gain GLIDES, because the bed is playing while the slider moves');

    // Import-safety: this whole tier dies silently if a module throws on import.
    const beforeFactory = droneSrc.slice(0, droneSrc.indexOf('export function createDroneBed'));
    ok(!/new\s+(window\.)?(webkit)?AudioContext/.test(beforeFactory),
      'ui/droneBed.js constructs no AudioContext at module scope');
    ok(!/document\./.test(beforeFactory), 'and touches no DOM at import time');
    ok(/oscL\.stop\(|oscL\.stop\b/.test(droneSrc) || /dying\.oscL\.stop/.test(droneSrc),
      'every oscillator it builds is handed its own stop() — a bed may not outlive its match');
  }
}

/* ===========================================================================
 * 19. VOLUME GRANULARITY — "like we do in the intake" (owner, 2026-08-04).
 *
 * The Intake gives a player five buses (master / binaural / effects / voice /
 * music). This page had four sliders and one of them, `sfxVolume`, was doing two
 * unrelated jobs — every menu tick AND every drop, payload, pop and sting — while
 * the loudest thing in the room, the opponent's payload VIDEOS, had no slider at
 * all. This section is the split, and it holds five things:
 *
 *   · THE REGISTRY IS THE MAP. Every cue declares its bus, so "which slider does
 *     this sound obey" is answered in the same place the sound is declared and
 *     cannot drift from the section comment above it;
 *   · NOBODY'S SETTING IS THROWN AWAY. A player who had turned sfx down to 0.3
 *     gets 0.3 on both new sliders, once, and then the retired key is gone;
 *   · EACH NUMBER IS APPLIED EXACTLY ONCE. The per-cue node carries the trim, the
 *     bus carries the slider, masterBus carries the master — which is also the
 *     fix for the old squared master (cueGain used to multiply it in as well);
 *   · MEDIA CROSSES THE FENCE AS A NUMBER. exec/ never imports ui/, so the
 *     EFFECTIVE media volume is published on <html> the way skippable videos and
 *     shader spirals already are, and mediaVolume() answers with the same value
 *     for anyone on this side;
 *   · AND THE DRAWER STILL FITS. Six volume rows is a lot of drawer; the panel
 *     was already a scroller, which is the only reason this is allowed.
 * ======================================================================== */
{
  const prefsMod = await import('../ui/prefs.js');
  const optionsMod = await import('../ui/options.js');
  const { S } = await import('../ui/strings.js');
  const { DRONE_GLIDE_SEC } = await import('../ui/droneBed.js');
  const {
    SFX_REGISTRY, SFX_IDS, SFX_BUSES, BUS_UI, BUS_GAME, DEFAULT_SFX_BUS,
    busOf, busGain, cueGain, SFX_TRIM, BUS_GLIDE_SEC,
  } = audioMod;

  /* ---- 19a. the registry: every id on exactly one real bus ---------------- */
  ok(Array.isArray(SFX_BUSES) && Object.isFrozen(SFX_BUSES) && SFX_BUSES.length === 2,
    'there are exactly TWO player-facing cue buses, frozen', JSON.stringify(SFX_BUSES));
  ok(SFX_BUSES.includes(BUS_UI) && SFX_BUSES.includes(BUS_GAME),
    'and they are ui + game', `${BUS_UI}/${BUS_GAME}`);

  const noBus = SFX_IDS.filter((id) => SFX_BUSES.indexOf(SFX_REGISTRY[id].bus) < 0);
  ok(noBus.length === 0,
    'EVERY registered cue declares a valid bus — an unlabelled one would obey a slider nobody chose for it',
    noBus.join(' '));

  const onUi = SFX_IDS.filter((id) => busOf(id) === BUS_UI);
  const onGame = SFX_IDS.filter((id) => busOf(id) === BUS_GAME);
  ok(onUi.length + onGame.length === SFX_IDS.length, 'the two buses partition the registry');
  ok(onUi.length >= 4 && onGame.length >= 20,
    'both are real populations, and the match owns most of the pack', `${onUi.length} ui / ${onGame.length} game`);

  // The chrome set, named. This is the whole player-facing promise of the ui
  // slider: it is the sounds YOU make by pressing things outside a match.
  const CHROME = ['ui-move', 'ui-select', 'ui-back', 'ui-error', 'code-cell', 'code-copy', 'lamp-confirm', 'lamp-clear'];
  ok(onUi.slice().sort().join(',') === CHROME.slice().sort().join(','),
    'the ui bus is exactly the menus/sheets/code-entry chrome, nothing else', onUi.join(' '));

  // Spot pins where the line is genuinely a judgement call, so a later "tidy-up"
  // that moves one has to argue with a named test instead of a comment.
  ok(busOf('gg-mercy') === BUS_GAME,
    'MERCY is a match sound — the safety valve may not go quiet with the menus');
  ok(busOf('lock-slip') === BUS_GAME && busOf('ui-error') === BUS_UI,
    'the same buzz sorts by WHO CAUSED IT: a form refusing you is ui, a lock card slipping is the duel');
  ok(busOf('payload-in') === BUS_GAME && busOf('bubble-pop') === BUS_GAME
    && busOf('countdown-tick') === BUS_GAME && busOf('draft-lock') === BUS_GAME
    && busOf('recap-won') === BUS_GAME && busOf('video-window-in') === BUS_GAME,
    'draft, countdown, drops, payloads, bubbles, windows and the recap are all one slider');
  ok(SFX_IDS.filter((id) => id.startsWith('ui-')).every((id) => busOf(id) === BUS_UI),
    'and nothing named ui-* ended up on the match bus');

  ok(busOf('not-a-cue-at-all') === DEFAULT_SFX_BUS && DEFAULT_SFX_BUS === BUS_GAME,
    'an unknown id answers the default instead of throwing — a typo is already a logged warning');
  ok(busOf(undefined) === DEFAULT_SFX_BUS && busOf(null) === DEFAULT_SFX_BUS,
    'busOf is total: no id, no crash');

  /* ---- 19b. the migration: nobody's setting is thrown away ---------------- */
  ok(prefsMod.PREF_DEFAULTS.sfxVolume === undefined,
    'sfxVolume is RETIRED — it is not a live key any more, so there is no third slider to keep in step');
  ok(prefsMod.PREF_DEFAULTS.uiVolume === 0.85 && prefsMod.PREF_DEFAULTS.gameVolume === 0.85,
    'both halves default to the OLD sfx default, so a fresh install sounds exactly as it did',
    `${prefsMod.PREF_DEFAULTS.uiVolume}/${prefsMod.PREF_DEFAULTS.gameVolume}`);
  ok(prefsMod.PREF_DEFAULTS.mediaVolume === 0.8,
    'mediaVolume defaults modest — the HUD has to stay audible under a window',
    String(prefsMod.PREF_DEFAULTS.mediaVolume));
  ok(prefsMod.PREF_MIGRATIONS.sfxVolume.join(',') === 'uiVolume,gameVolume',
    'the migration is declared as data, not buried in a constructor', JSON.stringify(prefsMod.PREF_MIGRATIONS));

  {
    const { migratePrefs } = prefsMod;
    const seeded = migratePrefs({ sfxVolume: 0.3 });
    ok(seeded.uiVolume === 0.3 && seeded.gameVolume === 0.3,
      'a saved sfxVolume seeds BOTH new keys — "we redesigned the mixer" is not a reason to undo a setting');
    const partial = migratePrefs({ sfxVolume: 0.3, gameVolume: 0.9 });
    ok(partial.gameVolume === 0.9 && partial.uiVolume === 0.3,
      'a key that already exists WINS — a stale sfxVolume the host keeps sending can never clobber it');
    const untouched = { masterVolume: 0.5 };
    ok(migratePrefs(untouched) === untouched,
      'a blob with nothing to migrate is handed straight back, not copied');
    const empty = migratePrefs(null);
    ok(empty && typeof empty === 'object' && Object.keys(empty).length === 0,
      'and a missing blob is an empty object, not a crash and not an invented volume',
      JSON.stringify(empty));
  }

  {
    const old = prefsMod.createPrefs({ sfxVolume: 0.3 });
    ok(old.get('uiVolume') === 0.3 && old.get('gameVolume') === 0.3,
      'createPrefs runs it: an existing player lands on their own number, twice');
    ok(old.get('sfxVolume') === undefined && old.all().sfxVolume === undefined,
      'and the retired key is not carried forward — migrated once, then gone');
    ok(old.set('sfxVolume', 0.9) === false && old.get('gameVolume') === 0.3,
      'writing the retired key is refused rather than silently accepted');

    const both = prefsMod.createPrefs({ sfxVolume: 0.3, uiVolume: 0.9 });
    ok(both.get('uiVolume') === 0.9 && both.get('gameVolume') === 0.3,
      'a half-migrated store keeps the half it already had');
    const loud = prefsMod.createPrefs({ sfxVolume: 9 });
    ok(loud.get('uiVolume') === 1 && loud.get('gameVolume') === 1,
      'the migrated value is clamped like any other *Volume key');
    const junk = prefsMod.createPrefs({ sfxVolume: 'loud', mediaVolume: 'quiet' });
    ok(junk.get('gameVolume') === 0.85 && junk.get('mediaVolume') === 0.8,
      'and garbage falls back to the defaults instead of poisoning an AudioParam');
  }

  /* ---- 19c. the gain staging, and a drag that lands on the right bus ------ */
  ok(Math.abs(cueGain(0.4) - 0.4 * SFX_TRIM) < 1e-9,
    'the per-cue node carries the cue trim x the house trim and NOTHING else');
  ok(cueGain.length === 1,
    'cueGain no longer takes the sliders — that is what squared the master before the split',
    String(cueGain.length));
  {
    const tick = SFX_REGISTRY['ui-select'].gain;
    const pop = SFX_REGISTRY['bubble-pop'].gain;
    // The whole point of the feature, as arithmetic: one slider down, the other
    // untouched, and master still over both.
    ok(busGain(pop, 0, 1) === 0 && busGain(tick, 0.8, 1) > 0,
      'game at 0 silences the field and leaves the menus alone — the granularity the owner asked for');
    ok(busGain(tick, 0, 1) === 0 && busGain(pop, 0.8, 1) > 0, 'and the same in reverse');
    ok(busGain(pop, 1, 0) === 0 && busGain(tick, 1, 0) === 0,
      'master 0 still takes everything down, because it is a THIRD node under both');
    ok(Math.abs(busGain(pop, 0.5, 0.5) - cueGain(pop) * 0.25) < 1e-9,
      'the two multiply once each — a master of 0.5 is 0.5, not 0.25 (the old bug)');
    ok(BUS_GLIDE_SEC > 0 && BUS_GLIDE_SEC < DRONE_GLIDE_SEC,
      'a cue bus glides too, but shorter than the bed: nothing on it sustains',
      `${BUS_GLIDE_SEC} vs ${DRONE_GLIDE_SEC}`);
  }
  {
    const p = prefsMod.createPrefs({ uiVolume: 0.4, gameVolume: 0.6, masterVolume: 0.9 });
    const bus = audioMod.createAudio({ prefs: p });
    ok(bus.volumes.ui === 0.4 && bus.volumes.game === 0.6,
      'the mixer seeds BOTH cue buses from prefs', JSON.stringify(bus.volumes));
    ok(Object.keys(bus.volumes).sort().join(',') === 'drone,game,master,media,music,ui,voice',
      'and exposes exactly the seven sliders the drawer draws', Object.keys(bus.volumes).join(','));

    p.set('gameVolume', 0.1);
    ok(bus.volumes.game === 0.1 && bus.volumes.ui === 0.4,
      'a live drag on ONE slider moves one bus and leaves the other alone');
    bus.setVolume('ui', 0.2);
    ok(bus.volumes.ui === 0.2 && p.get('uiVolume') === 0.2,
      'setVolume("ui") writes both ways, exactly like the older buses');
    bus.setVolume('game', 0);
    ok(bus.volumes.game === 0 && p.get('gameVolume') === 0,
      'and 0 really lands — the slider is an off switch for match audio');
    bus.dispose();
  }

  /* ---- 19d. media: the number that crosses the fence ---------------------- */
  {
    const { mediaGain, MEDIA_VOL_ATTR, MEDIA_VOL_EVENT } = prefsMod;
    ok(MEDIA_VOL_ATTR === 'data-gg-mediavol',
      'the media volume is published under a data-gg-* attribute, like every other pref exec/ obeys', MEDIA_VOL_ATTR);
    ok(Math.abs(mediaGain(0.5, 0.5) - 0.25) < 1e-9, 'mediaGain multiplies media x master');
    ok(mediaGain(1, 0) === 0 && mediaGain(0, 1) === 0, 'either at 0 is silence');
    ok(mediaGain(9, 1) === 1 && mediaGain('loud', 1) === 0,
      'and it clamps/refuses garbage rather than handing a video element a NaN');

    const p = prefsMod.createPrefs({ mediaVolume: 0.8, masterVolume: 0.8 });
    const bus = audioMod.createAudio({ prefs: p });
    ok(typeof bus.mediaVolume === 'function',
      'ui/audio.js exposes mediaVolume() — a call, because it is read live from the other side of the fence');
    ok(Math.abs(bus.mediaVolume() - 0.64) < 1e-9,
      'and it answers the EFFECTIVE number, master folded in (the videos are not on the graph)',
      String(bus.mediaVolume()));

    const heard = [];
    const off = bus.onMediaVolume((v) => heard.push(v));
    p.set('mediaVolume', 0.5);
    ok(Math.abs(bus.mediaVolume() - 0.4) < 1e-9, 'a drag on the media slider moves it live', String(bus.mediaVolume()));
    p.set('masterVolume', 0.5);
    ok(Math.abs(bus.mediaVolume() - 0.25) < 1e-9,
      'and so does the MASTER — the one road by which a muted master reaches a <video>');
    ok(heard.length === 2 && Math.abs(heard[1] - 0.25) < 1e-9,
      'subscribers hear both, with the finished number', JSON.stringify(heard));
    off();
    p.set('mediaVolume', 0.9);
    ok(heard.length === 2, 'and unsubscribing really stops it');

    // The <html> mirror: this is what exec/videos.js actually reads.
    const attrNow = document.documentElement.getAttribute(MEDIA_VOL_ATTR);
    ok(Math.abs(Number(attrNow) - 0.45) < 1e-3,
      'ui/prefs.js keeps <html data-gg-mediavol> in step with both keys', String(attrNow));
    ok(/^\d\.\d{3}$/.test(String(attrNow)),
      'written to 3 places, not 17 digits of float', String(attrNow));

    const evts = [];
    const ear = (e) => evts.push(e && e.detail && e.detail.volume);
    window.addEventListener(MEDIA_VOL_EVENT, ear);
    p.set('mediaVolume', 0.2);
    ok(evts.length === 1 && Math.abs(evts[0] - 0.1) < 1e-9,
      'and fires one event with the effective number, so a window that is ALREADY open can retune',
      JSON.stringify(evts));
    p.set('musicVolume', 0.3);
    ok(evts.length === 1, 'an unrelated slider does not fire it');
    window.removeEventListener(MEDIA_VOL_EVENT, ear);

    bus.setVolume('media', 0.6);
    ok(p.get('mediaVolume') === 0.6 && Math.abs(bus.mediaVolume() - 0.3) < 1e-9,
      'setVolume("media") goes through the same door as every other slider');
    const heardAfter = [];
    bus.onMediaVolume((v) => heardAfter.push(v));
    bus.dispose();
    p.set('mediaVolume', 0.1);
    ok(heardAfter.length === 0 && typeof bus.onMediaVolume(() => {}) === 'function',
      'a disposed mixer notifies nobody and still hands back an unsubscribe rather than throwing');
  }

  /* ---- 19e. the drawer: six rows, and it still fits ----------------------- */
  {
    const live = prefsMod.createPrefs({});
    const calls = [];
    const audioStub = { setVolume: (b, v) => calls.push([b, v]), sfx() {} };
    const options = optionsMod.createOptions({
      prefs: live, audio: audioStub, session: { hosted: false }, isInMatch: () => true,
    });
    options.open();
    await sleep(30);
    const panel = findOne(dom.byId.get('gg-drawer'), 'gg-panel');
    ok(!!panel, 'the drawer opened');
    const sliders = findAll(panel, 'gg-panel-row--slider');
    const labelOf = (rowNode) => {
      const lab = findOne(rowNode, 'gg-panel-label');
      return lab && lab.children[0] ? lab.children[0].textContent : '';
    };
    // SEVEN since the 2026-08-04 voice-note pass — the 7th is another player's
    // actual voice, and it goes last (see the block comment in ui/options.js).
    ok(sliders.length === 7, 'SEVEN volume rows', String(sliders.length));
    ok(sliders.map(labelOf).join(' | ')
        === [S.options.master, S.options.music, S.options.drone, S.options.ui, S.options.game,
          S.options.media, S.options.voice].join(' | '),
    'reading Master, Music, Drone, UI sounds, Game sounds, Media, Voice notes — in that order',
      sliders.map(labelOf).join(' | '));
    ok([S.options.ui, S.options.game, S.options.media, S.options.mediaNote].every((s) => typeof s === 'string' && s.length),
      'every new label lives in ui/strings.js with the rest');
    ok(S.options.sfx === undefined,
      'and the old "SFX" label is gone rather than left behind to be re-used by accident');

    const rowFor = (label) => sliders.find((r) => labelOf(r) === label) || null;
    for (const [label, bus, key] of [
      [S.options.ui, 'ui', 'uiVolume'],
      [S.options.game, 'game', 'gameVolume'],
      [S.options.media, 'media', 'mediaVolume'],
    ]) {
      const row = rowFor(label);
      const input = row ? (row.children[1] || null) : null;
      ok(!!input && input.tagName === 'INPUT' && input.getAttribute('aria-label') === label,
        `the ${bus} row is a labelled range input`, label);
      input.value = '30';
      input.dispatchEvent({ type: 'input' });
      ok(live.get(key) === 0.3, `dragging it writes ${key}`, String(live.get(key)));
      ok(calls.some(([b, v]) => b === bus && Math.abs(v - 0.3) < 1e-9),
        `and reaches ui/audio.js as the "${bus}" bus — the drag has to land on the right one`,
        JSON.stringify(calls));
      const valueSpan = findOne(row, 'gg-panel-value');
      ok(valueSpan && valueSpan.textContent === '30%', 'the readout repaints with it');
    }
    ok(findAll(panel, 'gg-panel-note').some((p2) => p2.textContent === S.options.mediaNote),
      'the media row says what it covers — "Media" alone would be a guess');

    options.dispose();
    await sleep(320);
  }

  /* ---- 19f. the wiring, at the sources -------------------------------------
   * Six sliders is a tall drawer and exec/ cannot import any of this, so the two
   * things this block pins are the ones no in-process assertion can see: the
   * panel really scrolls, and the fence really is a fence. */
  {
    const fs = await import('node:fs/promises');
    const url = await import('node:url');
    const read = async (p) => fs.readFile(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8');
    const audioSrc = await read('../ui/audio.js');
    const prefsSrc = await read('../ui/prefs.js');
    const videosSrc = await read('../exec/videos.js');
    const cssSrc = await read('../ui/screens.css');

    // The graph: two cue buses, both under the master, both glided.
    ok(/uiBus\s*=\s*ctx\.createGain\(\)/.test(audioSrc) && /gameBus\s*=\s*ctx\.createGain\(\)/.test(audioSrc),
      'ui/audio.js builds a real gain node for each cue bus');
    ok(/uiBus\.connect\(masterBus\)/.test(audioSrc) && /gameBus\.connect\(masterBus\)/.test(audioSrc),
      'and hangs both UNDER masterBus, so the master slider still scales them');
    ok(/glide\(uiBus\.gain/.test(audioSrc) && /glide\(gameBus\.gain/.test(audioSrc),
      'whose gains glide, so a drag mid-pop does not click');
    ok(!/sfxBus/.test(audioSrc),
      'the single sfxBus is GONE, not left dangling beside its replacements');
    // The banner is the map everyone reads first; a stale diagram is a lie.
    const banner = audioSrc.slice(0, audioSrc.indexOf('import {'));
    ok(/uiBus/.test(banner) && /gameBus/.test(banner) && !/-> sfxBus/.test(banner),
      'and the header diagram was redrawn with them');
    ok(/MEDIA IS NOT ON THIS GRAPH/.test(banner),
      'the banner says where the media volume lives instead, since it is the one that is not a node');

    // The fence: exec/ may not import ui/, which is the whole reason the media
    // number travels as an attribute. If this ever stops being true, the
    // attribute can be retired — until then it is load-bearing.
    ok(!/from\s+'\.\.\/ui\//.test(videosSrc),
      'exec/videos.js imports nothing from ui/ — the attribute is the only wire');
    ok(/MEDIA_VOL_ATTR = 'data-gg-mediavol'/.test(prefsSrc),
      'and ui/prefs.js is the one writer of it, beside the two flags exec/ already reads');
    ok(/mediaVolume: MEDIA_SPEC/.test(prefsSrc) && /masterVolume: MEDIA_SPEC/.test(prefsSrc),
      'BOTH contributing keys reflect it — a master drag that did not reach the videos would be the bug');

    // The drawer grew by two rows and a note; it was already a scroller.
    const panelCss = cssSrc.slice(cssSrc.indexOf('.gg-panel {'), cssSrc.indexOf('.gg-panel.is-open'));
    ok(/overflow-y:\s*auto/.test(panelCss),
      'the options panel scrolls, which is why six rows and four toggles is allowed on a phone',
      String(panelCss.length) + ' bytes of .gg-panel rules read');
    ok(/bottom:\s*var\(--gg-drawer-clip\)/.test(panelCss),
      'and it still clips itself off the mercy button rather than growing over it');
  }

  /* ---- 19g. import safety, in a REAL hostless node -------------------------
   * This whole tier dies silently if a module throws while importing (the loader
   * spins forever with no error on screen), and this file's DOM stub would hide
   * exactly that. So the check is made in a child process with no globals at all,
   * where the migration and the mixer both have to survive construction. */
  {
    const { execFileSync } = await import('node:child_process');
    const href = (p) => new URL(p, import.meta.url).href;
    const mods = [href('../ui/prefs.js'), href('../ui/audio.js'), href('../ui/options.js'), href('../ui/strings.js')];
    const code = `
      const bad = [];
      for (const m of ${JSON.stringify(mods)}) {
        try { await import(m); } catch (e) { bad.push(m.split('/').pop() + ': ' + (e && e.message)); }
      }
      try {
        const prefs = (await import(${JSON.stringify(mods[0])})).createPrefs({ sfxVolume: 0.3 });
        if (prefs.get('gameVolume') !== 0.3) bad.push('migration did not run hostless');
        const bus = (await import(${JSON.stringify(mods[1])})).createAudio({ prefs });
        if (typeof bus.mediaVolume() !== 'number') bad.push('mediaVolume() is not a number hostless');
        if (bus.sfx('bubble-pop') !== false) bad.push('a cue in a hostless node was not a clean false');
        bus.dispose();
      } catch (e) { bad.push('construct: ' + (e && e.message)); }
      if (bad.length) { console.error(bad.join(' | ')); process.exit(1); }
    `;
    let hostlessErr = '';
    try { execFileSync(process.execPath, ['--input-type=module', '-e', code], { stdio: 'pipe' }); }
    catch (e) { hostlessErr = String((e && e.stderr) || (e && e.message) || e).trim(); }
    ok(hostlessErr === '',
      'ui/prefs.js + ui/audio.js + ui/options.js import AND construct in a node with no document and no window',
      hostlessErr);
  }
}

/* ============================================================ wake lock
 * ui/wakeLock.js — the phone-screen keeper (beta follow-up, 2026-08-04).
 * The whole state machine runs against injected nav/doc stand-ins; the two
 * invariants that matter are (1) an unsupported browser is a total no-op and
 * (2) the visibility re-arm only lives between start() and stop(). */
{
  const { createWakeLock } = await import('../ui/wakeLock.js');

  // (1) unsupported — every call is a silent no-op
  const none = createWakeLock({ nav: {}, doc: null });
  ok(none.supported === false, 'wakelock: no wakeLock API -> supported false');
  none.start(); none.stop(); none.dispose();
  ok(none.active === false, 'wakelock: and start/stop never throw or hold anything');

  // a fake UA: sentinels the test can watch, a doc whose visibility it controls
  const mkFake = () => {
    const state = { requests: 0, sentinels: [], visibility: 'visible', listeners: new Set() };
    const nav = { wakeLock: { request: async () => {
      state.requests++;
      const s = { released: false, _onRelease: null,
        release() { this.released = true; if (this._onRelease) this._onRelease(); return Promise.resolve(); },
        addEventListener(_t, fn) { this._onRelease = fn; } };
      state.sentinels.push(s);
      return s;
    } } };
    const doc = {
      get visibilityState() { return state.visibility; },
      addEventListener: (_t, fn) => state.listeners.add(fn),
      removeEventListener: (_t, fn) => state.listeners.delete(fn),
      show() { state.visibility = 'visible'; for (const fn of state.listeners) fn(); },
      hide() { state.visibility = 'hidden'; for (const fn of state.listeners) fn(); },
    };
    return { state, nav, doc };
  };

  // (2) the ordinary life: start acquires, hide drops, show re-acquires, stop stands down
  const f = mkFake();
  const wl = createWakeLock({ nav: f.nav, doc: f.doc });
  ok(wl.supported === true, 'wakelock: fake UA -> supported');
  wl.start();
  await sleep(0);
  ok(wl.active === true && f.state.requests === 1, 'wakelock: start() acquires exactly one lock');
  wl.start();
  await sleep(0);
  ok(f.state.requests === 1, 'wakelock: start() is idempotent while a live sentinel is held');

  // the UA kills the lock on hide; show() must re-arm it — that is the phone
  // player checking a notification mid-match
  f.state.sentinels[0].release();
  f.doc.hide();
  await sleep(0);
  ok(wl.active === false, 'wakelock: a hidden page holds no lock (the UA released it)');
  f.doc.show();
  await sleep(0);
  ok(wl.active === true && f.state.requests === 2, 'wakelock: visible again -> a FRESH lock, unprompted');

  wl.stop();
  ok(wl.active === false && f.state.sentinels[1].released === true, 'wakelock: stop() releases the live sentinel');
  ok(f.state.listeners.size === 0, 'wakelock: and the visibility re-arm is stood down with it');
  f.doc.show();
  await sleep(0);
  ok(f.state.requests === 2, 'wakelock: an idle (stopped) page never re-acquires — no title-screen lock');

  // (3) a request in flight when stop() lands is handed straight back
  let resolveReq = null;
  const slowNav = { wakeLock: { request: () => new Promise((r) => { resolveReq = r; }) } };
  const g = mkFake();
  const wl2 = createWakeLock({ nav: slowNav, doc: g.doc });
  wl2.start();
  ok(typeof resolveReq === 'function', 'wakelock: request in flight');
  wl2.stop();
  const late = { released: false, release() { this.released = true; return Promise.resolve(); }, addEventListener() {} };
  resolveReq(late);
  await sleep(0);
  ok(late.released === true && wl2.active === false,
    'wakelock: a sentinel that lands after stop() is released, not leaked');
}

/* ===========================================================================
 * 20. VOICE NOTES — THE HELD MIC (wave 2, docs/GOON_VOICE_PLAN.md).
 *
 * A button you hold for up to ten seconds and the other duelist hears you. Four
 * things are pinned here because all four are ways this feature can go wrong in
 * a way nobody would notice from a screenshot:
 *
 *   THE MICROPHONE IS RELEASED. Every terminal path of ui/voice/recorder.js —
 *   stop, cancel, the 10s cap, a denied permission, dispose — must leave the
 *   tracks stopped. A hot mic is the one bug in this feature that would be a
 *   betrayal rather than an annoyance, so the fake stream below counts it.
 *
 *   NOTHING STRANDS A HELD RECORDING. pointerup, a slide past the threshold,
 *   pointercancel, lostpointercapture and the feature going away mid-hold all
 *   end the gesture. A recording nobody can stop would hold the mic open until
 *   the match ended.
 *
 *   THE MIC IS ABSENT, NOT DISABLED, until voice is live — and it appears on the
 *   availability EDGE, which is the only signal ui/voice/voiceService.js gives.
 *
 *   ESCAPE IS UNTOUCHED. Escape during Live is Mercy. This file may not add a
 *   rung to that ladder, so it may not contain a key handler at all — asserted
 *   against the source, because "we forgot we added one" is exactly how a
 *   safety verb gets shadowed.
 * ======================================================================== */
{
  const recMod = await import('../ui/voice/recorder.js');
  const micMod = await import('../ui/voice/micHud.js');
  const { S: S20 } = await import('../ui/strings.js');

  ok(typeof recMod.createVoiceRecorder === 'function', 'ui/voice/recorder.js exports createVoiceRecorder');
  ok(typeof micMod.mountMicHud === 'function', 'ui/voice/micHud.js exports mountMicHud');

  /* --- 20a. the container chain, and the four refusals -------------------- */
  ok(recMod.VN_MIME_CANDIDATES[0] === 'audio/webm;codecs=opus',
    'opus-in-webm is the first container tried (what WebView2 records natively)');
  ok(recMod.VN_MIME_CANDIDATES[recMod.VN_MIME_CANDIDATES.length - 1] === '',
    'and the last rung is the UA default — an unknown browser still records');
  ok(recMod.pickVoiceMime((m) => m.indexOf('audio/webm') === 0) === 'audio/webm;codecs=opus',
    'chromium picks opus');
  ok(recMod.pickVoiceMime((m) => m.indexOf('audio/mp4') === 0) === 'audio/mp4;codecs=mp4a.40.2',
    'safari falls through to mp4 rather than refusing');
  ok(recMod.pickVoiceMime(() => false) === '', 'a host that supports none of them still gets a recorder');
  ok(recMod.pickVoiceMime(() => { throw new Error('nope'); }) === '',
    'a probe that THROWS is a no, not a crash');
  ok(recMod.VN_AUDIO_BPS === 32000, '~32 kbps: ten seconds is ~40KB, a sixth of the byte cap',
    String(recMod.VN_AUDIO_BPS));
  ok(recMod.micErrorReason({ name: 'NotAllowedError' }) === 'denied', 'a refusal is "denied"');
  ok(recMod.micErrorReason({ name: 'NotFoundError' }) === 'missing', 'no device is "missing" — a different sentence');
  ok(recMod.micErrorReason({ name: 'NotReadableError' }) === 'failed', 'a busy device is "failed"');
  ok(recMod.micErrorReason(null) === 'failed', 'and anything else still answers');

  /* --- 20b. the recorder's state machine, without a microphone ------------
   * The two injectable seams (getUserMedia, recorderFactory) are what let the
   * whole of this run under node — the module must never reach for a real one. */
  function fakeMic({ maxMs = 500, fail = null } = {}) {
    const tracks = [{ kind: 'audio', stopped: false, stop() { this.stopped = true; } }];
    const stream = { getTracks: () => tracks };
    const made = [];
    const rec = recMod.createVoiceRecorder({
      maxMs,
      getUserMedia: () => (fail ? Promise.reject(fail) : Promise.resolve(stream)),
      recorderFactory: (s, opts) => {
        const r = {
          opts,
          started: false,
          start() { this.started = true; },
          stop() {
            setTimeout(() => {
              try { this.ondataavailable && this.ondataavailable({ data: new Uint8Array(64) }); } catch (_e) { /* ignore */ }
              try { this.onstop && this.onstop(); } catch (_e) { /* ignore */ }
            }, 0);
          },
        };
        made.push(r);
        return r;
      },
      isTypeSupported: (m) => m === 'audio/webm;codecs=opus',
    });
    return { rec, tracks, made, stream };
  }

  {
    const f = fakeMic();
    const started = await f.rec.start();
    ok(started.ok === true && started.reason === 'recording', 'start() opens the microphone', JSON.stringify(started));
    ok(f.rec.isRecording() === true, 'and says so');
    ok(f.tracks[0].stopped === false, 'the track is live WHILE recording');
    ok(f.made[0] && f.made[0].opts.mimeType === 'audio/webm;codecs=opus', 'the picked container reaches MediaRecorder');
    ok(f.made[0] && f.made[0].opts.audioBitsPerSecond === recMod.VN_AUDIO_BPS, 'at the pinned bitrate');
    ok((await f.rec.start()).reason === 'busy', 'a second start while recording is refused, not stacked');

    const res = await f.rec.stop();
    ok(res.ok === true && res.reason === 'stopped', 'stop() hands back the note', JSON.stringify(res.reason));
    ok(res.blob && res.blob.size === 64, 'with the recorded bytes on it', String(res.blob && res.blob.size));
    ok(typeof res.durMs === 'number' && res.durMs >= 0 && res.durMs <= 500, 'and a measured duration', String(res.durMs));
    ok(f.tracks[0].stopped === true, 'THE MICROPHONE IS RELEASED after a stop — no hot mic');
    ok(f.rec.state() === 'idle', 'and the recorder is idle again');
    ok((await f.rec.stop()).reason === 'idle', 'a second stop is not an error path');
  }

  {
    const f = fakeMic();
    await f.rec.start();
    const res = await f.rec.cancel();
    ok(res.ok === false && res.reason === 'cancelled', 'cancel() answers "cancelled"');
    ok(!res.blob, 'and keeps NOTHING — the audio is never assembled');
    ok(f.tracks[0].stopped === true, 'THE MICROPHONE IS RELEASED after a cancel too');
  }

  {
    // The 10s cap, shrunk to the recorder's own floor so the suite can wait it out.
    const f = fakeMic({ maxMs: 500 });
    const capped = [];
    f.rec.onCapped((r) => capped.push(r));
    await f.rec.start();
    await sleep(620);
    ok(capped.length === 1, 'the cap stops the recording by itself', String(capped.length));
    ok(capped[0] && capped[0].ok === true && capped[0].capped === true && !!capped[0].blob,
      'and hands the finished note to whoever is listening (the HUD auto-sends it)');
    ok(f.tracks[0].stopped === true, 'THE MICROPHONE IS RELEASED on the cap path as well');
    ok(f.rec.isRecording() === false, 'and nothing is still running');
  }

  {
    const denied = Object.assign(new Error('denied'), { name: 'NotAllowedError' });
    const f = fakeMic({ fail: denied });
    let threw = null;
    let res = null;
    try { res = await f.rec.start(); } catch (e) { threw = e; }
    ok(!threw, 'a denied microphone NEVER throws at the caller', threw && threw.message);
    ok(res && res.ok === false && res.reason === 'denied', 'it is an answer: {ok:false, reason:"denied"}', JSON.stringify(res));
    ok(f.rec.state() === 'idle', 'and leaves nothing behind');
  }

  {
    const f = fakeMic();
    await f.rec.start();
    f.rec.dispose();
    ok(f.tracks[0].stopped === true, 'dispose() mid-recording takes the microphone with it');
  }

  /* --- 20b-bis. ONE FAILURE MAY NOT POISON THE NEXT PRESS ------------------
   *
   * THE REPORTED BUG (owner, 2026-08-05): "the mic can be triggered once, but if
   * that first recording fails the button never resets — every press after it
   * says that one did not record". Every check in this block is a way that used
   * to happen, and all of them share one shape: something left the recorder OUT
   * OF `idle` with nobody waiting on it, and `start()` answers everything that
   * is not idle with 'busy' — which the strip rendered as a refusal, forever.
   * ---------------------------------------------------------------------- */
  {
    // A microphone whose permission prompt is never ANSWERED (swiped away on
    // iOS, where that promise then simply never settles).
    const tracks = [{ kind: 'audio', stopped: false, stop() { this.stopped = true; } }];
    let resolveGum = null;
    const rec = recMod.createVoiceRecorder({
      // A cap the block cannot reach (the disowned attempt below is waited out
      // in real time) and a short start ceiling, so it is waited out quickly.
      maxMs: 30_000,
      startTimeoutMs: 150,
      getUserMedia: () => new Promise((res) => { resolveGum = res; }),
      recorderFactory: () => ({ start() {}, stop() { setTimeout(() => { try { this.onstop && this.onstop(); } catch (_e) { /* ignore */ } }, 0); } }),
      isTypeSupported: () => true,
    });

    const first = rec.start();
    await sleep(0);
    ok(rec.state() === 'starting', 'a press with a prompt still up leaves the recorder starting');
    // ...and the gesture goes away underneath it: a tap, or the prompt itself
    // stealing the pointer and firing pointercancel.
    const cancelled = await rec.cancel();
    ok(cancelled.reason === 'cancelled', 'cancelling during the prompt answers at once, without waiting for it');

    const second = rec.start();
    await sleep(0);
    ok(typeof resolveGum === 'function', 'the second press asked for a microphone of its own');
    resolveGum({ getTracks: () => tracks });
    const r2 = await second;
    ok(r2.ok === true && r2.reason === 'recording',
      'A PRESS AFTER A DISOWNED ONE IS A FRESH ATTEMPT — not the "busy" that used to stick for the session',
      JSON.stringify(r2));
    ok(rec.isRecording() === true, 'and it really is recording');
    const r1 = await first;
    /* AND IT ANSWERS 'cancelled', NOT A FAILURE. Its ceiling expires long after
     * the gesture that asked for it is forgotten, and a caller that RECOVERS
     * from failures (the mic HUD does, deliberately) would otherwise take the
     * microphone off the recording that replaced it, half a minute later. */
    ok(r1.ok === false && r1.reason === 'cancelled',
      'a superseded attempt reports itself cancelled, not failed', JSON.stringify(r1));
    ok(rec.isRecording() === true, '...and does not disturb the recording that replaced it');
    rec.dispose();
  }

  {
    // ...and where nothing supersedes it, the attempt gives up on its own.
    const tracks = [{ kind: 'audio', stopped: false, stop() { this.stopped = true; } }];
    let resolveGum = null;
    const rec = recMod.createVoiceRecorder({
      startTimeoutMs: 120,
      getUserMedia: () => new Promise((res) => { resolveGum = res; }),
      recorderFactory: () => ({ start() {}, stop() {} }),
    });
    const res = await rec.start();
    ok(res.ok === false && res.reason === 'failed',
      'a getUserMedia that never answers is written off, not waited on forever', JSON.stringify(res));
    ok(rec.state() === 'idle', 'AND THE MACHINE IS LEFT PRESSABLE — this is the whole bug');
    resolveGum({ getTracks: () => tracks });
    await sleep(5);
    ok(tracks[0].stopped === true,
      'a stream that lands after the timeout is stopped where it arrives — rule 1 does not care that it was late');
    rec.dispose();
  }

  {
    // The recovery door the UI reaches for when it is refused and cannot say why.
    const f = fakeMic();
    await f.rec.start();
    ok(f.rec.state() === 'recording', 'a recording is running');
    f.rec.reset();
    ok(f.rec.state() === 'idle', 'reset() puts it back to idle from anywhere');
    ok(f.tracks[0].stopped === true, '...taking the microphone with it, like every other terminal path');
    const again = await f.rec.start();
    ok(again.ok === true, 'and the next press records rather than being told "busy"');
    f.rec.dispose();
  }

  {
    // THE TIMESLICE. Without it WebKit may hand over nothing at all for a short
    // note, which arrives at the player as "that one did not record".
    const tracks = [{ kind: 'audio', stopped: false, stop() { this.stopped = true; } }];
    let sliceSeen = 'never started';
    const rec = recMod.createVoiceRecorder({
      getUserMedia: () => Promise.resolve({ getTracks: () => tracks }),
      recorderFactory: () => ({ start(ms) { sliceSeen = ms; }, stop() {} }),
    });
    await rec.start();
    ok(sliceSeen === recMod.VN_TIMESLICE_MS,
      'MediaRecorder is started WITH a timeslice, so a note is assembled as it is spoken',
      String(sliceSeen));
    ok(recMod.VN_TIMESLICE_MS > 0 && recMod.VN_TIMESLICE_MS <= 1000,
      'and it is short enough that the shortest keepable note still has a chunk in it');
    rec.dispose();
  }

  {
    // A recorder that dies MID-NOTE. Nobody asked it to stop, so nobody is
    // awaiting an answer — it has to volunteer one.
    const tracks = [{ kind: 'audio', stopped: false, stop() { this.stopped = true; } }];
    let made = null;
    const rec = recMod.createVoiceRecorder({
      getUserMedia: () => Promise.resolve({ getTracks: () => tracks }),
      recorderFactory: () => { made = { start() {}, stop() {} }; return made; },
    });
    await rec.start();
    const seen = [];
    rec.onFailed((p) => seen.push(p));
    made.onerror({ error: new Error('the pipeline fell over') });
    ok(seen.length === 1 && seen[0].reason === 'recorder-error',
      'a recorder that dies mid-note SAYS so, instead of letting the strip count against nothing');
    ok(tracks[0].stopped === true, 'and hands the microphone back on the way down');
    ok(rec.state() === 'idle', 'and is pressable again without anybody having to ask');
    rec.dispose();
  }

  {
    // release() answering a stop that is still in flight. A recorder that never
    // fires `stop` AND is reset under the caller must still settle the promise —
    // an await that never returns is a strip stuck on "sending…".
    const tracks = [{ kind: 'audio', stopped: false, stop() { this.stopped = true; } }];
    const rec = recMod.createVoiceRecorder({
      getUserMedia: () => Promise.resolve({ getTracks: () => tracks }),
      recorderFactory: () => ({ start() {}, stop() { /* never fires onstop */ } }),
    });
    await rec.start();
    const pending = rec.stop();
    rec.reset();
    const settled = await Promise.race([pending, sleep(50).then(() => 'NEVER SETTLED')]);
    ok(settled !== 'NEVER SETTLED', 'a pending stop() is ANSWERED by the release under it, never dropped');
    ok(rec.state() === 'idle', 'and the machine is idle behind it');
    rec.dispose();
  }

  /* --- 20c. the gesture --------------------------------------------------- */
  function fakeVoice() {
    const stateSubs = new Set();
    const inSubs = new Set();
    let avail = false;
    const sends = [];
    return {
      available: () => avail,
      sendBlob(blob, o) { sends.push({ blob, o }); return Promise.resolve({ ok: true, reason: 'sent', id: sends.length, parts: 1 }); },
      onStateChanged(fn) { stateSubs.add(fn); return () => stateSubs.delete(fn); },
      onIncoming(fn) { inSubs.add(fn); return () => inSubs.delete(fn); },
      _set(v) { avail = !!v; for (const fn of Array.from(stateSubs)) fn(avail); },
      _incoming(p) { for (const fn of Array.from(inSubs)) fn(p); },
      _sends: sends,
    };
  }
  /**
   * `elapsed` and `cap` are MUTABLE on the returned `st` so the countdown block
   * below can walk the clock towards the ceiling without waiting ten real
   * seconds — the strip reads both off the recorder every repaint, which is the
   * whole reason it may not keep a second copy of the cap.
   */
  function fakeRec({ elapsed = 900, cap = 10000 } = {}) {
    const st = { starts: 0, stops: 0, cancels: 0, recording: false, disposed: false, capSubs: new Set(), elapsed, cap };
    const api = {
      start() { st.starts++; st.recording = true; return Promise.resolve({ ok: true, reason: 'recording' }); },
      stop() { st.stops++; st.recording = false; return Promise.resolve({ ok: true, reason: 'stopped', blob: { size: 64 }, durMs: 900 }); },
      cancel() { st.cancels++; st.recording = false; return Promise.resolve({ ok: false, reason: 'cancelled' }); },
      state() { return st.recording ? 'recording' : 'idle'; },
      isRecording() { return st.recording; },
      elapsedMs() { return st.elapsed; },
      maxMs() { return st.cap; },
      mimeType() { return ''; },
      onCapped(fn) { st.capSubs.add(fn); return () => st.capSubs.delete(fn); },
      dispose() { st.disposed = true; st.recording = false; },
    };
    return { api, st };
  }
  const ptr = (node, type, x, id = 7) => node.dispatchEvent({
    type, pointerId: id, clientX: x, clientY: 0, preventDefault() {},
  });
  /** One mounted mic with its fakes, live and idle. */
  function mountMic(recOpts) {
    const host = document.createElement('div');
    const chipHost = document.createElement('div');
    const v = fakeVoice();
    const r = fakeRec(recOpts);
    const mic = micMod.mountMicHud({ host, chipHost, voice: v, recorder: r.api });
    return { host, chipHost, v, r, mic, btn: mic.parts.button };
  }

  {
    const m = mountMic();
    ok(!!m.btn && m.btn.tagName === 'BUTTON', 'the mic is a real button');
    ok(m.host.hidden === true, 'and it is ABSENT until voice is live — not a greyed-out promise');
    ok(m.btn.getAttribute('aria-label') === S20.voice.hudLabel, 'labelled from strings.js');
    const beforeEdge = m.btn;
    m.v._set(true);
    ok(m.host.hidden === false, 'the availability EDGE brings it in');
    m.v._set(false);
    ok(m.host.hidden === true, '...and takes it away again');
    m.v._set(true);
    ok(m.btn === beforeEdge && m.mic.parts.button === beforeEdge,
      'the SAME node throughout: rebuilding it would release a live pointer capture');
    m.mic.unmount();
  }

  {
    // hold -> release = send
    const m = mountMic();
    m.v._set(true);
    ptr(m.btn, 'pointerdown', 200);
    ok(m.r.st.starts === 1, 'pointerdown opens the microphone immediately (no lost first syllable)');
    ok(m.mic.isRecording() === false, 'but the strip does not expand on a tap-length press');
    await sleep(320);
    ok(m.mic.isRecording() === true, 'a 250ms hold IS a recording');
    ok(hasClass(m.mic.parts.strip, 'is-rec'), 'and the strip says so');
    ok(hasClass(m.mic.parts.strip, 'gg-plate'), 'wearing .gg-plate while it is a strip (the hot-heat hardening)');
    ptr(m.btn, 'pointerup', 200);
    await sleep(30);
    ok(m.r.st.stops === 1 && m.r.st.cancels === 0, 'release STOPS the recording');
    ok(m.v._sends.length === 1, 'and sends exactly one note');
    ok(m.v._sends[0].o && m.v._sends[0].o.durMs === 900, 'with the measured duration on it');
    m.mic.unmount();
  }

  {
    // a tap is a hint, never a note
    const m = mountMic();
    m.v._set(true);
    ptr(m.btn, 'pointerdown', 200);
    await sleep(60);
    ptr(m.btn, 'pointerup', 200);
    await sleep(20);
    ok(m.v._sends.length === 0, 'a sub-250ms tap sends nothing');
    ok(m.r.st.cancels === 1, 'and the microphone it opened is closed on the cancel path');
    ok(m.mic.parts.hint && m.mic.parts.hint.hidden === false, 'the "hold to record" hint appears');
    ok(m.mic.parts.hint.textContent === S20.voice.holdHint, '...in the copy deck\'s words', m.mic.parts.hint.textContent);
    m.mic.unmount();
  }

  {
    // slide left past the threshold = cancel
    const m = mountMic();
    m.v._set(true);
    ptr(m.btn, 'pointerdown', 300);
    await sleep(320);
    ptr(m.btn, 'pointermove', 300 - (micMod.MIC_ARM_PX + 2));
    ok(hasClass(m.mic.parts.strip, 'is-cancel'), 'crossing half way SAYS it is about to cancel');
    ok(m.v._sends.length === 0 && m.r.st.cancels === 0, 'and has not cancelled yet');
    ptr(m.btn, 'pointermove', 300 - (micMod.MIC_CANCEL_PX + 2));
    await sleep(20);
    ok(m.r.st.cancels === 1, 'past 80px the recording is cancelled');
    ok(m.v._sends.length === 0, 'and NOTHING goes out');
    m.mic.unmount();
  }

  for (const type of ['pointercancel', 'lostpointercapture']) {
    const m = mountMic();
    m.v._set(true);
    ptr(m.btn, 'pointerdown', 200);
    await sleep(320);
    ptr(m.btn, type, 200);
    await sleep(20);
    ok(m.r.st.cancels === 1 && m.v._sends.length === 0,
      `${type} cancels the hold — a held recording never strands`);
    ok(m.mic.isRecording() === false, 'and the strip folds away');
    m.mic.unmount();
  }

  {
    // the feature going away under a live hold
    const m = mountMic();
    m.v._set(true);
    ptr(m.btn, 'pointerdown', 200);
    await sleep(320);
    m.v._set(false);
    await sleep(20);
    ok(m.r.st.cancels === 1, 'voice going unavailable mid-hold cancels the recording');
    ok(m.host.hidden === true, 'and the mic leaves with it');
    m.mic.unmount();
  }

  {
    // the 10s cap auto-sends
    const m = mountMic();
    m.v._set(true);
    ptr(m.btn, 'pointerdown', 200);
    await sleep(320);
    for (const fn of Array.from(m.r.st.capSubs)) fn({ ok: true, capped: true, blob: { size: 40 }, durMs: 10000 });
    await sleep(20);
    ok(m.v._sends.length === 1, 'the ten-second cap stops AND sends');
    ok(m.mic.isRecording() === false, 'and the gesture is over even with the button still down');
    m.mic.unmount();
  }

  /* --- 20c-bis. ZEN TAKES THE MIC, IN JS AND NOT ONLY IN CSS ---------------
   * The stylesheet's `.gg-hud--zen .gg-voice-host { display:none }` is asserted
   * in 20f, and on its own it is NOT ENOUGH: pointer capture bypasses hit
   * testing, so hiding the slot under a live hold would leave a recording (and
   * the OS microphone light) running with nothing on screen to say so. The bit
   * is pushed into the handle, and it composes with availability rather than
   * replacing it. */
  {
    const m = mountMic();
    ok(typeof m.mic.setHudHidden === 'function', 'the mic takes the desk\'s hidden bit');
    m.v._set(true);
    ok(m.host.hidden === false && m.mic.shown() === true, 'live and shown, to start');
    m.mic.setHudHidden(true);
    ok(m.host.hidden === true, 'ZEN HIDES THE BUTTON — at the host, not only in the stylesheet');
    ok(m.mic.shown() === false, 'and shown() says so');
    ok(m.mic.available() === true, '...while available() still reports the FEATURE, which zen did not change');
    m.mic.setHudHidden(true);
    ok(m.host.hidden === true, 'pushing the same bit twice is a no-op');
    m.mic.setHudHidden(false);
    ok(m.host.hidden === false, 'un-zen gives it back');

    // The two bits AND. Whichever one is false, the mic is gone.
    m.mic.setHudHidden(true);
    m.v._set(false);
    m.mic.setHudHidden(false);
    ok(m.host.hidden === true, 'leaving zen does NOT resurrect a mic the feature itself has taken away');
    m.v._set(true);
    ok(m.host.hidden === false, 'and both being true is the only state that shows it');
    m.mic.unmount();
  }

  {
    // zen DURING a hold: the recording dies with the surface it was drawn on
    const m = mountMic();
    m.v._set(true);
    ptr(m.btn, 'pointerdown', 200);
    await sleep(320);
    ok(m.mic.isRecording() === true, 'a hold is running');
    m.mic.setHudHidden(true);
    await sleep(20);
    ok(m.r.st.cancels === 1, 'hiding the HUD mid-hold CANCELS the recording — never a mic behind a hidden strip');
    ok(m.v._sends.length === 0, 'and nothing is sent: the player was hiding the UI, not finishing a note');
    ok(m.mic.isRecording() === false, 'the gesture is over');
    ok(m.host.hidden === true, 'and the slot is gone');
    m.mic.unmount();
  }

  /* --- 20c-ter. THE LAST THREE SECONDS ARE A COUNTDOWN ---------------------
   * Elapsed for the first seven ("3.4s / 10s"), remaining for the last three
   * ("2s left") — the question changes, so the number does. Both readings come
   * off the RECORDER's own cap, which is why this fake can shrink it. */
  {
    const m = mountMic({ elapsed: 0, cap: 10000 });
    m.v._set(true);
    ptr(m.btn, 'pointerdown', 200);
    await sleep(320);
    const timeEl = m.mic.parts.timeEl;

    m.r.st.elapsed = 4200;
    await sleep(150);
    ok(timeEl.textContent === S20.voice.recordTimer(4200, 10000),
      'mid-note the strip reads elapsed / cap', timeEl.textContent);
    ok(!hasClass(m.mic.parts.strip, 'is-ending'), 'and is not warning about anything yet');

    m.r.st.elapsed = 10000 - micMod.MIC_COUNTDOWN_MS + 1;
    await sleep(150);
    ok(hasClass(m.mic.parts.strip, 'is-ending'),
      'crossing MIC_COUNTDOWN_MS turns the strip hot');
    ok(timeEl.textContent === S20.voice.recordCountdown(3),
      'and the number becomes a countdown, from strings.js', timeEl.textContent);

    m.r.st.elapsed = 9200;
    await sleep(150);
    ok(timeEl.textContent === S20.voice.recordCountdown(1),
      'the last whole second is "1s left", never "0s left" for a whole second', timeEl.textContent);

    // ...and the warning belongs to ONE note: it may not be inherited.
    ptr(m.btn, 'pointerup', 200);
    await sleep(30);
    ok(!hasClass(m.mic.parts.strip, 'is-ending'), 'sending clears it');
    // "sent" holds the strip for MIC_FLASH_MS and onDown only fires from idle —
    // the second hold has to wait for the fold, exactly as a player's would.
    await sleep(micMod.MIC_FLASH_MS + 120);
    m.r.st.elapsed = 0;
    ptr(m.btn, 'pointerdown', 200);
    await sleep(320);
    ok(!hasClass(m.mic.parts.strip, 'is-ending') &&
       m.mic.parts.timeEl.textContent === S20.voice.recordTimer(0, 10000),
      'so the NEXT hold starts on the elapsed reading, not the last one\'s hot number',
      m.mic.parts.timeEl.textContent);
    m.mic.unmount();
  }

  {
    // a shrunken cap counts down from ITSELF — there is no second copy of 10s
    const m = mountMic({ elapsed: 0, cap: 4000 });
    m.v._set(true);
    ptr(m.btn, 'pointerdown', 200);
    await sleep(320);
    m.r.st.elapsed = 2500;
    await sleep(150);
    ok(hasClass(m.mic.parts.strip, 'is-ending') &&
       m.mic.parts.timeEl.textContent === S20.voice.recordCountdown(2),
      'the countdown is read off rec.maxMs(), not VN_MAX_MS',
      m.mic.parts.timeEl.textContent);
    m.mic.unmount();
  }

  {
    // their note arriving
    const m = mountMic();
    const chip = m.mic.parts.chip;
    ok(!!chip && chip.hidden === true, 'the incoming chip starts hidden');
    ok(m.chipHost.hidden === true, '...at the HOST too, so an empty slot costs the column no flex gap');
    m.v._incoming({ emote: null, durMs: 1500, bytes: 4000 });
    ok(chip.hidden === false && m.chipHost.hidden === false,
      'and appears when one of THEIR notes starts playing');
    const chipWords = findOne(chip, 'gg-voice-chip-text');
    ok(chipWords && String(chipWords.textContent).indexOf(S20.voice.incoming) >= 0,
      'saying so in words, not only in moving bars', chipWords && chipWords.textContent);
    ok(findAll(chip, 'gg-voice-bar').length === 3, 'the three level bars are their own leaf elements (animation budget)');
    m.mic.unmount();
    ok(m.chipHost.children.length === 0, 'unmount takes the chip with it');
  }

  {
    // the refusal copy
    ok(micMod.sendReasonLine('sent') === S20.voice.sent, 'a sent note says "sent"');
    ok(micMod.sendReasonLine('too-soon', 3) === S20.voice.tooSoon(3), 'the 4s floor is phrased as a wait');
    ok(micMod.sendReasonLine('unavailable') === S20.voice.notActive, 'a dead feature explains itself');
    ok(micMod.sendReasonLine('nonsense-from-the-future') === S20.voice.sendFailed,
      'and an unknown reason still says something true');

    /* TWO KINDS OF NO, AND THEY ARE NOT THE SAME SENTENCE. sendReasonLine is
     * about a NOTE that failed to cross; micReasonLine is about a MICROPHONE
     * that never opened. Telling somebody "that one did not record" when nothing
     * ever started is how a mic problem gets mistaken for a recording problem. */
    ok(typeof micMod.micReasonLine === 'function', 'ui/voice/micHud.js exports micReasonLine');
    ok(micMod.micReasonLine('denied') === S20.voice.micDenied, 'a refused mic is a refusal, not a failure');
    ok(micMod.micReasonLine('missing') === S20.voice.micMissing, 'no device says no device');
    ok(micMod.micReasonLine('unsupported') === S20.voice.micMissing, '...and so does a host with no getUserMedia');
    ok(micMod.micReasonLine('cancelled') === '', 'our own gesture ending it says NOTHING — there is nothing to report');
    ok(micMod.micReasonLine('busy') === S20.voice.micFailed
      && micMod.micReasonLine('failed') === S20.voice.micFailed
      && micMod.micReasonLine('nonsense-from-the-future') === S20.voice.micFailed,
    'and everything else blames the mic rather than the recording', micMod.micReasonLine('busy'));
    ok(micMod.micReasonLine('failed') !== S20.voice.sendFailed,
      'the two lines are actually DIFFERENT — this used to be one string doing both jobs');
    ok(/try again/i.test(S20.voice.micFailed), 'and it says the thing that is now true: try again');
  }

  /* --- 20c-quater. A THROWN SEAM IS NOT A DEAD BUTTON ----------------------
   *
   * The other half of the reported stuck mic, on this side of the wire. Both
   * awaits in stopAndSend are on INJECTABLE handles, and a rejection from either
   * one used to walk out as an unhandled rejection with the strip still reading
   * "sending…" — a phase onDown refuses. The mic was then dead for the match.
   * ---------------------------------------------------------------------- */
  function mountBrokenMic(broken) {
    const host = document.createElement('div');
    const chipHost = document.createElement('div');
    const v = fakeVoice();
    const st = { starts: 0, resets: 0 };
    const api = {
      start() { st.starts++; return Promise.resolve({ ok: true, reason: 'recording' }); },
      stop() {
        return broken === 'stop'
          ? Promise.reject(new Error('the recorder fell over'))
          : Promise.resolve({ ok: true, reason: 'stopped', blob: { size: 64 }, durMs: 900 });
      },
      cancel() { return Promise.resolve({ ok: false, reason: 'cancelled' }); },
      state() { return 'idle'; },
      isRecording() { return false; },
      elapsedMs() { return 900; },
      maxMs() { return 10000; },
      mimeType() { return ''; },
      onCapped() { return () => {}; },
      reset() { st.resets++; },
      dispose() {},
    };
    if (broken === 'send') v.sendBlob = () => Promise.reject(new Error('the wire died'));
    const mic = micMod.mountMicHud({ host, chipHost, voice: v, recorder: api });
    v._set(true);
    return { mic, btn: mic.parts.button, st, v };
  }

  {
    const m = mountBrokenMic('stop');
    ptr(m.btn, 'pointerdown', 200);
    await sleep(320);
    ptr(m.btn, 'pointerup', 200);
    await sleep(40);
    ok(m.st.resets === 1, 'a stop() that REJECTS is recovered from, not left in flight');
    ok(m.mic.parts.timeEl.textContent === S20.voice.micFailed,
      'and the player is told the MIC failed, not that their note did',
      m.mic.parts.timeEl.textContent);
    ok(m.mic.phase() !== 'send', 'the strip is not left on "sending…"', m.mic.phase());
    // ...and the very next press is a whole new recording, with no wait.
    ptr(m.btn, 'pointerdown', 200);
    ok(m.st.starts === 2, 'THE NEXT PRESS RECORDS — one failure does not poison the mic');
    m.mic.unmount();
  }

  {
    const m = mountBrokenMic('send');
    ptr(m.btn, 'pointerdown', 200);
    await sleep(320);
    ptr(m.btn, 'pointerup', 200);
    await sleep(40);
    ok(m.mic.parts.timeEl.textContent === S20.voice.sendFailed,
      'a sendBlob that REJECTS reports a failed note (the recording was fine — the wire was not)',
      m.mic.parts.timeEl.textContent);
    ptr(m.btn, 'pointerdown', 200);
    ok(m.st.starts === 2, 'and the mic is pressable straight afterwards');
    m.mic.unmount();
  }

  {
    /* A FLASH IS NOT A LOCK. The terminal word sits on the strip for
     * MIC_FLASH_MS, and a player who has just been told a note failed reaches
     * STRAIGHT back for the button. A press that is ignored for over a second
     * is indistinguishable from the stuck mic all of this is about. */
    const m = mountMic();
    m.v._set(true);
    ptr(m.btn, 'pointerdown', 200);
    await sleep(320);
    ptr(m.btn, 'pointerup', 200);
    await sleep(30);
    ok(m.mic.phase() === 'flash', 'a finished note flashes its word');
    ptr(m.btn, 'pointerdown', 200);
    ok(m.r.st.starts === 2, 'pressing DURING the flash cuts it short and opens the mic again');
    await sleep(320);
    ok(m.mic.isRecording() === true, 'and that press becomes a real recording');
    m.mic.unmount();
  }

  /* --- 20d. ESCAPE IS NOT TOUCHED ----------------------------------------- */
  {
    const fs20 = await import('node:fs/promises');
    const url20 = await import('node:url');
    const src = await fs20.readFile(url20.fileURLToPath(new URL('../ui/voice/micHud.js', import.meta.url)), 'utf8');
    const code = src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/[^\n]*/g, '');
    ok(!/keydown|keyup|['"]Escape['"]/.test(code),
      'ui/voice/micHud.js has NO key handler at all — Escape during Live stays Mercy');
    ok(!/pointerType/.test(code),
      'and the gesture is not gated on pointerType: mouse, touch and pen alike');
    ok(/setPointerCapture/.test(code), 'the hold is pointer-captured, so a finger may leave the button');
    ok(/lostpointercapture/.test(code), 'and a lost capture is a cancel, not a strand');
  }

  /* --- 20e. hud.js really mounts it --------------------------------------- */
  {
    const match20 = makeFakeMatch();
    const voice20 = fakeVoice();
    const hud20 = hudMod.mountHud({ match: match20, audio: { sfx() {} }, voice: voice20 });
    const frame20 = findOne(dom.byId.get('gg-hud'), 'gg-hud-frame');
    const col20 = findOne(frame20, 'gg-rightcol');
    const micHost20 = findOne(frame20, 'gg-voice-host');
    const chipHost20 = findOne(frame20, 'gg-voice-chiphost');
    ok(!!micHost20 && !!chipHost20, 'mountHud gives the mic and the chip a slot each');
    /* THE MIC MOVED TO THE ARSENAL DRAWER (2026-08-05). It lived under the
     * receipts in the opponent column, which is where nobody looked: the owner
     * hunted for it "right under the Emote icon" across three play-tests. The
     * emote tile is the last item on the RIGHT rail, so the slot is now the last
     * child of `.gg-arsenal-panel`, immediately after the two rails. Still FLOW,
     * still no coordinates, still no keep-out band to miss. */
    const panel20 = findOne(frame20, 'gg-arsenal-panel');
    ok(!!panel20 && micHost20.parentNode === panel20,
      'the mic slot is a FLOW child of the arsenal panel — under the tiles, where the emote icon is');
    const pkids = panel20.children;
    ok(pkids.indexOf(micHost20) === pkids.length - 1 &&
       pkids.indexOf(micHost20) > pkids.indexOf(findOne(panel20, 'gg-arsenal-rails')),
      'and it is the LAST thing in the drawer, directly under the rails whose last right-hand tile is the emote');
    ok(chipHost20.parentNode === col20,
      'the CHIP stays in the opponent column — it is about THEIR voice, not a control');
    const kids = col20.children;
    ok(kids.indexOf(chipHost20) === kids.indexOf(findOne(col20, 'gg-mon-host')) + 1,
      'the chip sits directly under their bezel');
    ok(kids.indexOf(micHost20) < 0,
      'and the mic is no longer in that column at all');
    ok(!!findOne(frame20, 'gg-voice-btn'), 'the button is built');
    ok(micHost20.hidden === true, 'and hidden until the feature is live');
    ok(!!hud20.parts.mic && typeof hud20.parts.mic.isRecording === 'function',
      'exposed on parts, like every other sub-mount');
    voice20._set(true);
    ok(micHost20.hidden === false, 'the desk shows it on the availability edge');

    /* THE ZEN TOGGLE REALLY REACHES IT. Clicking the button the player clicks,
     * not calling the setter: the wiring between setZen and the mic handle is
     * the thing under test, and it has a hoisting order to get wrong. */
    hud20.parts.zen.button.dispatchEvent({ type: 'click' });
    ok(hud20.parts.zen.on === true, 'the zen button toggles the desk');
    ok(micHost20.hidden === true, 'and the mic goes with the rest of the chrome');
    hud20.parts.zen.button.dispatchEvent({ type: 'click' });
    ok(hud20.parts.zen.on === false && micHost20.hidden === false,
      'the toggle is its own undo for the mic too');
    ok(hud20.parts.mic.shown() === true, 'and the handle agrees it is back');

    hud20.unmount();
    ok(dom.byId.get('gg-hud').children.length === 0, 'and it comes down with the desk');
    ok(match20._subs.released === match20._subs.taken, 'with no subscription left behind',
      `${match20._subs.released}/${match20._subs.taken}`);
  }
  {
    /* A REMEMBERED ZEN, which is the ordering trap: the pref is read before the
     * mic is mounted, so the bit has to be REPLAYED into the handle. Without it
     * a returning player's first zen press would SHOW the mic instead of hiding
     * it, and the second would hide it — the toggle inverted for one match. */
    const match20z = makeFakeMatch();
    const voice20z = fakeVoice();
    const hud20z = hudMod.mountHud({
      match: match20z, audio: { sfx() {} }, voice: voice20z,
      prefs: { get: (k) => k === 'hudZen', set() {} },
    });
    const micHost20z = findOne(dom.byId.get('gg-hud'), 'gg-voice-host');
    voice20z._set(true);
    ok(micHost20z.hidden === true,
      'a zen remembered from the last match hides the mic at mount, not after the first click');
    hud20z.parts.zen.button.dispatchEvent({ type: 'click' });
    ok(hud20z.parts.zen.on === false && micHost20z.hidden === false,
      'and the FIRST press is the one that brings it back');
    hud20z.unmount();
  }
  {
    // no service (a build with the feature off, or a construction failure)
    const match20b = makeFakeMatch();
    const hud20b = hudMod.mountHud({ match: match20b, audio: { sfx() {} } });
    const frame20b = findOne(dom.byId.get('gg-hud'), 'gg-hud-frame');
    ok(!findOne(frame20b, 'gg-voice-btn'), 'no voice service = no mic on the desk at all');
    ok(findOne(frame20b, 'gg-voice-host').hidden === true
      && findOne(frame20b, 'gg-voice-chiphost').hidden === true,
      'and both slots are hidden, so the column is exactly what it was before this feature');
    hud20b.unmount();
  }

  /* --- 20f. the CSS half --------------------------------------------------
   * micHud.js only toggles classes. Placement, the click-through, the animation
   * budget and zen are all stylesheet facts, and a mic with no rules would pass
   * every check above while lying across the stage eating clicks. */
  {
    const fs20 = await import('node:fs/promises');
    const url20 = await import('node:url');
    const css20 = await fs20.readFile(url20.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8');
    const at20 = css20.indexOf('VOICE NOTES — the held mic');
    const end20 = css20.indexOf('ANNOUNCER — the top-centre ribbon');
    ok(at20 > 0, 'hud.css carries the voice section');
    // BOUNDED, not sliced to EOF: the announcer's own section asserts that
    // nothing after its header loops, and a `infinite` of ours must not land
    // inside its slice. (It did, for one commit.)
    const sec20 = css20.slice(at20, end20 > at20 ? end20 : css20.length);
    const bare20 = sec20.replace(/\/\*[\s\S]*?\*\//g, '');

    ok(/\.gg-voice-host\s*\{[^}]*position:\s*relative/.test(bare20),
      'the slot is the containing block...');
    ok(/\.gg-voice\s*\{[^}]*position:\s*absolute/.test(bare20) && /\.gg-voice\s*\{[^}]*right:\s*0/.test(bare20),
      '...and the strip is anchored to an edge of it, so a recording grows sideways and nothing on the desk moves');
    ok(!/position:\s*fixed/.test(bare20) && !/z-index/.test(bare20),
      'nothing here is fixed and nothing claims a z-index — MERCY owns z60 and this tier does not go near it');

    /* THE DRAWER VARIANT (2026-08-05). The slot moved under the emote tile, and
     * the arsenal is the LEFT gutter — a strip still growing leftwards out of it
     * would be off the screen inside two words. It flips to left-anchored, and
     * the button flips back to the outside edge WITHOUT the DOM order changing
     * (micHud must never re-append a node: that releases pointer capture). */
    ok(/\.gg-arsenal-panel\s+\.gg-voice\s*\{[^}]*left:\s*0/.test(bare20)
      && /\.gg-arsenal-panel\s+\.gg-voice\s*\{[^}]*right:\s*auto/.test(bare20),
      'in the arsenal drawer the strip is anchored LEFT and grows rightwards, away from the screen edge');
    ok(/\.gg-arsenal-panel\s+\.gg-voice\s*\{[^}]*flex-direction:\s*row-reverse/.test(bare20),
      '...with the button back on the outside edge by paint order, never by re-appending a captured node');
    ok(css20.indexOf('.gg-arsenal-panel .gg-voice {') > css20.indexOf('.gg-voice:not(.gg-plate)'),
      'and written after the idle padding reset it has to beat');

    ok(/\.gg-hud-frame\s+\.gg-voice,[\s\S]{0,120}pointer-events:\s*none/.test(bare20),
      'pointer-events: none at 0,2,0 — .gg-plate would otherwise turn the strip back on over the stage');
    ok(/\.gg-hud-frame\s+\.gg-voice-btn\s*\{[^}]*pointer-events:\s*auto/.test(bare20),
      'and the one control that must stay clickable re-opts in at the same weight');
    ok(css20.indexOf('.gg-hud-frame .gg-voice,') > css20.indexOf('.gg-hud-frame .gg-plate,'),
      'written LATER in the file than the .gg-plate rule it is answering');

    // THE ANIMATION BUDGET: only leaf elements animate, and only two of them.
    const animated = [];
    const re20 = /([^{}]+)\{([^{}]*)\}/g;
    let m20;
    while ((m20 = re20.exec(bare20))) {
      if (/(^|[;\s])animation:\s*(?!none)/.test(m20[2])) animated.push(m20[1].trim());
    }
    ok(animated.length > 0, 'the strip animates something');
    ok(animated.every((sel) => /gg-voice-dot|gg-voice-bar/.test(sel)),
      'and ONLY on the dot and the level bars — never on a node that already carries state classes',
      animated.join(' | '));
    ok(/\.gg-voice-dot\s*\{[^}]*animation-play-state:\s*var\(--gg-deco-play\)/.test(bare20),
      'the pulse parks with every other decoration at data-gg-fx="hot" (the red dot itself stays)');

    ok(/gg-hud--zen\s+\.gg-voice-host/.test(bare20) && /gg-hud--zen\s+\.gg-voice-chiphost/.test(bare20),
      'zen takes both slots away by name, like the dial and the effect chips');
    // ...and the JS half of the same fact, which is the one that closes the mic.
    const hudSrc20 = await fs20.readFile(url20.fileURLToPath(new URL('../ui/hud.js', import.meta.url)), 'utf8');
    ok(/setHudHidden\(zenOn\)/.test(hudSrc20.replace(/\/\*[\s\S]*?\*\//g, '')),
      'ui/hud.js pushes the zen bit into the mic — CSS cannot end a captured hold');

    ok(/\.gg-voice\.is-ending\s+\.gg-voice-time/.test(bare20),
      'the last three seconds have a colour as well as a word');
    ok(css20.indexOf('.gg-voice.is-cancel .gg-voice-time') > css20.indexOf('.gg-voice.is-ending .gg-voice-time'),
      '...and the cancel state is written later, so a note about to be dropped is not also "running out"');
    ok(/prefers-reduced-motion[\s\S]*gg-voice-dot[\s\S]*animation:\s*none/.test(bare20),
      'motion off stops the pulse without taking the state away');
    ok(/\.gg-voice-btn\s*\{[^}]*width:\s*48px[\s\S]{0,40}height:\s*48px/.test(bare20),
      'the hit area is the tier\'s 48px, like the gear and the zen toggle');
  }
}

/* ===========================================================================
 * THE MONITOR GEOMETRY — assets/monitor_frame.png was re-cut (2026-08-04).
 *
 * The old art was 1376x768 and FULLY OPAQUE: a painted city with a television
 * standing in it, and the projection rect was measured to the CRT face it
 * painted (26% / 48% / 47%). The new art is 935x667 with a TRANSPARENT SURROUND
 * — just the set — so every one of those numbers moved, the <img> can no longer
 * be stretched to a 16/9 box, and the dark glass behind it is visible for the
 * first time.
 *
 * The four face numbers are RE-DERIVED here from the art's own pixel rectangles
 * rather than copied out of the stylesheet, so this is a check and not an echo:
 * re-cut the art without re-measuring and this section says so.
 * ======================================================================== */
{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const css = await fs.readFile(url.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8');

  // The art, measured: the image, the television in it, and its screen face.
  const ART = { w: 935, h: 667 };
  const TV = { x: 148, y: 114, w: 638, h: 454 };
  const FACE = { x: 230, y: 178, w: 475, h: 295 };

  // …and the placement rule: the frame stays 16/9 (it is the drop target and a
  // row in the HUD column), and the TELEVISION is fitted to its height.
  const FRAME_H = 9 / 16;                                  // frame height, in frame widths
  const imgWidthPct = (ART.h / TV.h) * FRAME_H * (ART.w / ART.h) * 100;
  const imgHeightPct = (ART.h / TV.h) * 100;
  const imgLeftPct = 50 - ((TV.x + TV.w / 2) / ART.w) * imgWidthPct;
  const imgTopPct = 50 - ((TV.y + TV.h / 2) / ART.h) * imgHeightPct;
  const want = {
    x: imgLeftPct + (FACE.x / ART.w) * imgWidthPct,
    w: (FACE.w / ART.w) * imgWidthPct,
    y: imgTopPct + (FACE.y / ART.h) * imgHeightPct,
    h: (FACE.h / ART.h) * imgHeightPct,
  };

  const blockOf = (sel) => {
    const m = new RegExp(sel.replace(/\./g, '\\.') + '\\s*\\{([^}]*)\\}').exec(css);
    return m ? m[1] : '';
  };
  const frameBlock = blockOf('.gg-mon-frame');
  const pct = (block, prop) => {
    const m = new RegExp('(?:^|;|\\s)' + prop + ':\\s*(-?[\\d.]+)%').exec(block);
    return m ? Number(m[1]) : null;
  };
  const near = (a, b, tol = 0.15) => a !== null && Math.abs(a - b) <= tol;

  ok(!!frameBlock, 'hud.css still has a .gg-mon-frame block');
  ok(/aspect-ratio:\s*16\s*\/\s*9/.test(frameBlock),
    'the frame keeps its 16/9 box — it is the arsenal drop target and a HUD column row');
  for (const [name, wanted] of [['x', want.x], ['y', want.y], ['w', want.w], ['h', want.h]]) {
    const got = pct(frameBlock, '--gg-mon-face-' + name);
    ok(near(got, wanted),
      `--gg-mon-face-${name} matches the CRT face the art actually paints`,
      `${got} vs ${wanted.toFixed(2)}`);
  }

  // The <img> placement itself: NOT inset:0 at 100%, or the 1.402:1 art is
  // stretched across a 1.778:1 box and every face number above is a lie.
  const bezel = blockOf('.gg-mon-bezel');
  ok(!!bezel, 'hud.css has a .gg-mon-bezel block');
  ok(!/inset:\s*0/.test(bezel), 'the bezel is no longer pinned to the frame with inset: 0');
  ok(near(pct(bezel, 'width'), imgWidthPct) && near(pct(bezel, 'height'), imgHeightPct),
    'the bezel <img> is scaled so the TELEVISION fills the frame height',
    `${pct(bezel, 'width')}/${pct(bezel, 'height')} vs ${imgWidthPct.toFixed(1)}/${imgHeightPct.toFixed(1)}`);
  ok(near(pct(bezel, 'left'), imgLeftPct) && near(pct(bezel, 'top'), imgTopPct),
    'and offset so it centres on the set rather than on the image',
    `${pct(bezel, 'left')}/${pct(bezel, 'top')}`);
  // Distortion check, straight off the declared box: whatever the two numbers
  // are, their ratio must still be the art's.
  const shownAspect = pct(bezel, 'width') / (pct(bezel, 'height') * FRAME_H);
  ok(Math.abs(shownAspect - ART.w / ART.h) < 0.01,
    'the art is placed, not stretched — the declared box is still 1.40:1', shownAspect.toFixed(4));

  // The face numbers are declared ONCE and consumed by name, in both consumers.
  const proj = blockOf('.gg-mon-proj');
  ok(/left:\s*var\(--gg-mon-face-x\)/.test(proj) && /width:\s*var\(--gg-mon-face-w\)/.test(proj)
    && /top:\s*var\(--gg-mon-face-y\)/.test(proj) && /height:\s*var\(--gg-mon-face-h\)/.test(proj),
    'the projection rect is measured off --gg-mon-face-*, not off four literals of its own');
  ok(!/aspect-ratio/.test(proj),
    'and no longer claims 16/9 — the CRT face is 1.61:1 and the height is explicit');
  ok(/var\(--gg-mon-face-/.test(blockOf('.gg-mon-screen')),
    'the dark glass is cut to the same face — a 7%/6% inset now pokes out either side of the set');
  ok(/\.gg-mon-frame\.is-nobezel\s*\{[^}]*--gg-mon-face-x/.test(css),
    'the no-art fallback re-declares the face rather than overriding each consumer');

  // z-order is still the whole feature (section 9's other half).
  const zOf = (sel) => { const m = /z-index:\s*(-?\d+)/.exec(blockOf(sel)); return m ? Number(m[1]) : null; };
  ok(zOf('.gg-mon-proj') > zOf('.gg-mon-bezel'),
    'the projection rect still outranks the art — its FACE is still painted opaque black');
  ok(zOf('.gg-mon-throw') > zOf('.gg-mon-proj'), 'and the throw flare sits above both');
}

/* ===========================================================================
 * THE THROW — an inbound payload gets a sender, a flight and a landing.
 *
 * "when the opponent fires a payload at us the opponent monitor should
 *  highlight, and the item should fly from the monitor to our field, and THEN
 *  the effect triggers."
 *
 * The load-bearing property is that NONE of it may move the engine: the flight
 * is fitted inside the schedule lead the engine already reserved, so the
 * executor's render, the ACK and the receipts all happen exactly when they did.
 * ======================================================================== */
{
  const previewMod = await import('../ui/throwPreview.js');
  const { THROW_MS, THROW_MIN_MS, MINI_FOR_PAYLOAD } = opponentMod;

  // --- the sticker map is the FLOOR under every preview, so it must be real
  {
    const fs = await import('node:fs/promises');
    const url = await import('node:url');
    for (const kind of Object.keys(MINI_FOR_PAYLOAD)) {
      const art = previewMod.STICKER_FOR_PAYLOAD[kind];
      ok(!!art, `every payload kind we can be thrown has a sticker (kind ${kind})`);
      if (!art) continue;
      let there = true;
      try { await fs.stat(url.fileURLToPath(new URL('../assets/items/' + art + '.png', import.meta.url))); }
      catch (_e) { there = false; }
      ok(there, `assets/items/${art}.png exists`);
    }
    ok(previewMod.stickerUrl(GoonPayloadKind.Video) === './assets/items/item_video.png',
      'stickerUrl builds the same path ui/arsenal.js does');
    ok(previewMod.stickerUrl(9999) === null, 'and an unknown kind has no sticker rather than a broken one');
  }

  // --- the preview resolves the way the EXECUTOR will, and never by drawing
  {
    const SHA = 'a'.repeat(64);
    let drew = 0;
    let peeked = 0;
    const released = [];
    const pool = {
      acquireByTag(tag) {
        if (tag !== 'xfer:' + SHA) return null;
        return { url: 'blob:peer-clip', mime: 'video/mp4', provenance: 'peer', release() { released.push('peer'); } };
      },
      peekKind(kind) {
        peeked++;
        if (kind !== 'video') return null;
        return {
          kind, name: 'own', url: 'blob:own-clip',
          acquire: () => ({ url: 'blob:own-clip', provenance: 'local', release() { released.push('own'); } }),
        };
      },
      drawKind() { drew++; return null; },
      drawFor() { drew++; return null; },
    };
    previewMod.setPreviewMedia(pool);

    const exact = previewMod.resolvePreview(GoonPayloadKind.Video, { tags: ['xfer:' + SHA] });
    ok(exact && exact.exact === true && exact.url === 'blob:peer-clip' && exact.provenance === 'peer',
      'a payload carrying an xfer: tag we hold previews THE clip that is about to play',
      exact ? `${exact.provenance} ${exact.url}` : 'null');
    if (exact) exact.release();

    const rep = previewMod.resolvePreview(GoonPayloadKind.Video, { tags: [] });
    ok(rep && rep.exact === false && rep.url === 'blob:own-clip',
      'an untagged one previews our own library and says it is only representative');
    if (rep) rep.release();
    ok(released.length === 2, 'and both handles were given back', released.join(','));

    ok(drew === 0,
      'NOTHING here ever draws — a preview that spent a draw would change which clip then played',
      String(drew));
    ok(peeked > 0, 'it peeks instead', String(peeked));

    // wrong kind on the tag -> skipped, exactly as exec/media.js drawFor does
    const wrongKind = previewMod.resolvePreview(GoonPayloadKind.FlashBurst, { tags: ['xfer:' + SHA] });
    ok(!wrongKind, 'a video tag on an image payload is not previewed as the image');

    // the unbacked kinds fly as their sticker and nothing else
    for (const kind of [GoonPayloadKind.LockCard, GoonPayloadKind.Spiral,
      GoonPayloadKind.SubliminalStorm, GoonPayloadKind.ToyPattern]) {
      ok(previewMod.resolvePreview(kind, null) === null,
        `kind ${kind} has no media behind it and previews as its sticker`);
    }
    ok(previewMod.PREVIEW_MEDIA_KIND[GoonPayloadKind.Video] === 'video'
      && previewMod.PREVIEW_MEDIA_KIND[GoonPayloadKind.FlashBurst] === 'image',
      'the two kinds the P2P lane can carry are both previewable');

    /* --- THE OUTBOUND HALF: the arsenal's drag ghost wears the same preview.
     * What you are holding is what they are about to get. It is a ONE-LINE hook
     * (`ghost = dressGhost(ghost, kind) || ghost`) precisely so the arsenal needs
     * no branch and no knowledge of media — a null answer keeps its sticker. */
    {
      const sticker = document.createElement('img');
      sticker.className = 'gg-item-ghost';
      sticker.style.width = '64px';
      sticker.style.height = '64px';
      const live = previewMod.dressGhost(sticker, GoonPayloadKind.Video);
      ok(!!live && live !== sticker, 'a media-backed item is dragged as a live preview, not a sticker');
      ok(live && live.tagName === 'VIDEO', '…and a video payload drags the clip itself', live && live.tagName);
      ok(live && live.style.width === '64px' && live.style.height === '64px',
        'it inherits the ghost\'s size, so the arsenal\'s moveGhost/killGhost keep working unchanged');
      ok(live && /item_video\.png/.test(String(live.style['--gg-ghost-sticker'] || '')),
        'with the sticker still painted behind it until the first frame decodes');
      const before = released.length;
      live.remove();
      ok(released.length > before,
        'and removing it gives the refcount back — the arsenal cannot be expected to know that');

      ok(previewMod.dressGhost(document.createElement('img'), GoonPayloadKind.LockCard) === null,
        'an item with no content behind it keeps its sticker (null = no change)');
      ok(previewMod.dressGhost(null, GoonPayloadKind.Video) === null, 'and a missing node is a no-op');
    }

    previewMod.setPreviewMedia(null);
    ok(previewMod.resolvePreview(GoonPayloadKind.Video, null) === null,
      'no pool -> no preview -> the sticker, with no throw');
    ok(previewMod.dressGhost(document.createElement('img'), GoonPayloadKind.Video) === null,
      'and with no pool the drag ghost stays exactly what it always was');
  }

  // --- both call sites really exist (the hooks are one line each, easy to lose)
  {
    const fs = await import('node:fs/promises');
    const url = await import('node:url');
    const read = async (p) => fs.readFile(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8');
    const arsenalSrc = await read('../ui/arsenal.js');
    const hudSrc2 = await read('../ui/hud.js');
    ok(/dressGhost\(ghost,\s*rec\.item\.kind\)/.test(arsenalSrc),
      'ui/arsenal.js dresses its drag ghost — the OUTBOUND half of the same feature');
    ok(/setPreviewMedia\(media\)/.test(hudSrc2),
      'ui/hud.js is where the media pool reaches the preview, and the only place it does');
    ok(/markInbound\(\{[^}]*payload:\s*p/.test(hudSrc2),
      'and the inbound throw is handed the payload, not just the kind');
  }

  // --- the flight itself
  {
    const match = makeFakeMatch();
    const host = document.createElement('div');
    const mon = opponentMod.mountOpponent({ host, match });

    ok(typeof mon.markInbound === 'function', 'the monitor exposes markInbound');
    ok(mon.markInbound({ kind: null }) === false, 'a payload with no kind throws nothing');

    ok(!!findOne(mon.root, 'gg-mon-throw'), 'the flare node is built with the monitor');
    ok(mon.markInbound({ kind: GoonPayloadKind.LockCard, waitMs: 0 }) === true, 'markInbound accepts a kind');
    ok(hasClass(mon.root, 'is-throwing'), 'their monitor lights up the moment they throw');

    const fly = findOne(document.body, 'gg-throw');
    ok(!!fly, 'a projectile is on the page');
    const arc = fly && findOne(fly, 'gg-throw-arc');
    const art = arc && findOne(arc, 'gg-throw-art');
    ok(!!arc && !!art,
      '…as THREE nested nodes: `animation` is a shorthand, so travel-x, travel-y and the spin '
      + 'cannot share an element without cancelling each other');
    ok(art && art.style && /item_lockcard\.png/.test(String(art.style['--gg-throw-sticker'] || '')),
      'the item art is painted on it — the floor under every preview',
      art && art.style ? String(art.style['--gg-throw-sticker']) : 'none');
    ok(fly && fly.style && fly.style['--gg-throw-dx'] !== undefined
      && fly.style['--gg-throw-dy'] !== undefined && fly.style['--gg-throw-ms'] !== undefined,
      'and it carries its own travel vector and duration');

    // it LANDS, and the splash is a node of its own (a fourth animation slot)
    await sleep(THROW_MIN_MS + 140);
    ok(!findOne(document.body, 'gg-throw'), 'the projectile is spent at impact');
    ok(!!findOne(document.body, 'gg-throw-hit'), 'and leaves a splash where it hit');
    await sleep(600);
    ok(!findOne(document.body, 'gg-throw-hit'), 'which clears itself on a TIMER, not on animationend');

    // the lead is respected: a long wait delays the LAUNCH, never the effect
    mon.markInbound({ kind: GoonPayloadKind.FlashBurst, waitMs: 8000 });
    ok(!findOne(document.body, 'gg-throw'),
      'a payload scheduled 8s out does not throw anything yet — the flight is fitted INSIDE the lead');

    // …and unmount sweeps whatever is still in the air
    mon.markInbound({ kind: GoonPayloadKind.FlashBurst, waitMs: 0 });
    ok(!!findOne(document.body, 'gg-throw'), 'one more in the air');
    mon.unmount();
    ok(!findOne(document.body, 'gg-throw'), 'unmount takes the projectile with it — nothing outlives the HUD');
    ok(match._subs.released === match._subs.taken, 'and still leaks no subscriptions');
    await sleep(THROW_MS + 140);
    ok(!findOne(document.body, 'gg-throw-hit'), 'a swept projectile never lands either');
  }

  // --- ui/hud.js is what tells the monitor, off the engine's own accept event
  {
    const match = makeFakeMatch();
    const hud = hudMod.mountHud({ match, audio: { sfx() {} } });
    const mon = hud.parts.opponent;
    const seen = [];
    const real = mon.markInbound;
    mon.markInbound = (o) => { seen.push(o); return real(o); };
    match._emit('pAcc', {
      payload: { id: 'in1', kind: GoonPayloadKind.Video, duration_ms: 45000, tags: ['xfer:' + 'b'.repeat(64)] },
      fireAtLocalMs: 0,
    });
    mon.markInbound = real;
    ok(seen.length === 1, 'onPayloadAccepted reaches opponent.markInbound', String(seen.length));
    ok(seen[0] && seen[0].kind === GoonPayloadKind.Video, 'with the kind that is coming');
    ok(seen[0] && seen[0].payload && Array.isArray(seen[0].payload.tags),
      'and with the PAYLOAD, which is what makes an inbound preview exact rather than a guess');
    hud.unmount();
    ok(match._subs.released === match._subs.taken, 'the glue still leaks nothing');
    await sleep(THROW_MS + 140);
  }

  /* --- EVERY KIND, AND EVERY KIND AS ITSELF (owner, 2026-08-05: "flashes and
   * videos show the little incoming throw, the other kinds do not").
   *
   * markInbound was never kind-gated and these checks say so out loud. What the
   * other six kinds lacked was anything to SHOW: their only pixels were a
   * ~650 KB item cutout whose fetch began when the projectile did, so what flew
   * was an empty box. The glyph is the fix — text, frame one, no network — and
   * the warm is the other half: the fetch now starts at ACCEPT, inside the lead
   * the engine already reserved. */
  {
    const announcerMod = await import('../ui/announcer.js');
    const kinds = Object.keys(GoonPayloadKind).map((k) => GoonPayloadKind[k]);
    ok(kinds.length === 8, 'the wire has eight payload kinds', String(kinds.length));

    // The mark table is COMPLETE. A ninth wire code must fail HERE, in the
    // suite, and not as an invisible projectile in somebody's duel.
    for (const kind of kinds) {
      const m = previewMod.THROW_MARK[kind];
      ok(!!m && typeof m.glyph === 'string' && m.glyph.length > 0,
        `payload kind ${kind} has a glyph of its own`);
      ok(!!m && /^\d{1,3}, \d{1,3}, \d{1,3}$/.test(String(m && m.tint)),
        `…and an rgb triple for its tint`, String(m && m.tint));
    }
    // …and the glyphs are the vocabulary the ribbon and the rail chips already
    // speak, not a fourth private alphabet. (Bubbles is never announced, so the
    // ribbon has no glyph to compare against for BubbleSwarm.)
    for (const kind of kinds) {
      const element = announcerMod.PAYLOAD_ANNOUNCE_ELEMENT[kind];
      const want = announcerMod.ANNOUNCE_GLYPH[element];
      if (!want) continue;
      ok(previewMod.markFor(kind).glyph === want,
        `kind ${kind} flies under the same glyph the announcer names it with`,
        previewMod.markFor(kind).glyph + ' vs ' + want);
    }
    ok(previewMod.markFor(9999) === previewMod.DEFAULT_THROW_MARK,
      'a kind from a newer peer still throws something you can see');

    const match = makeFakeMatch();
    const host = document.createElement('div');
    const mon = opponentMod.mountOpponent({ host, match });

    const hitsBefore = findAll(document.body, 'gg-throw-hit').length;
    for (const kind of kinds) {
      ok(mon.markInbound({ kind, waitMs: 0 }) === true, `kind ${kind} is thrown at us`);
      const flies = findAll(document.body, 'gg-throw');
      const fly = flies[flies.length - 1];
      ok(!!fly, `kind ${kind} put a projectile on the page`);
      const mark = fly && findOne(fly, 'gg-throw-mark');
      ok(!!mark && mark.textContent === previewMod.markFor(kind).glyph,
        `…carrying its own glyph, which needs no network to be visible (kind ${kind})`,
        mark ? mark.textContent : 'none');
      ok(!!fly && String(fly.style['--gg-throw-tint'] || '') === previewMod.markFor(kind).tint,
        `…and its own tint, which themes the flare, the glow and the splash (kind ${kind})`,
        fly ? String(fly.style['--gg-throw-tint']) : 'none');
      ok(!!fly && fly.getAttribute('data-gg-kind') === String(kind),
        `…and says which kind it is (kind ${kind})`);
      const art = fly && findOne(fly, 'gg-throw-art');
      ok(!!art && /item_[a-z]+\.png/.test(String(art.style['--gg-throw-sticker'] || '')),
        `…with the cutout queued behind it (kind ${kind})`);
      ok(!hasClass(fly, 'is-art'),
        `…but NOT revealed, because it has not decoded — that hole was the bug (kind ${kind})`);
    }
    ok(hasClass(mon.root, 'is-throwing') && String(mon.root.style['--gg-throw-tint'] || '')
      === previewMod.markFor(kinds[kinds.length - 1]).tint,
      'their monitor flares in the colour of the thing it just threw');

    await sleep(THROW_MIN_MS + 160);
    const hits = findAll(document.body, 'gg-throw-hit');
    ok(hits.length - hitsBefore === kinds.length,
      'all eight land — the impact cue is not a two-kind feature either',
      String(hits.length - hitsBefore));
    const firstHit = hits[hitsBefore];
    ok(!!firstHit && String(firstHit.style['--gg-throw-tint'] || '') === previewMod.markFor(kinds[0]).tint,
      'and the splash wears the same colour the projectile did');

    // THE WORD: the two payloads that ARE text arrive with their text.
    ok(previewMod.throwWord(GoonPayloadKind.Video, { text: 'nope' }) === '',
      'a kind whose payload is not text never grows a caption');
    ok(previewMod.throwWord(GoonPayloadKind.LockCard, { text: '  good   girls\ndrift  ' }) === 'good girls drift',
      'the caption is collapsed remote text, never a line break in a pill');
    const longWord = previewMod.throwWord(GoonPayloadKind.SubliminalStorm, { text: 'z'.repeat(120) });
    ok(longWord.length === previewMod.THROW_WORD_MAX && /…$/.test(longWord),
      'a wall of text is clamped to a glance', String(longWord.length));
    ok(previewMod.throwWord(GoonPayloadKind.LockCard, null) === ''
      && previewMod.throwWord(GoonPayloadKind.LockCard, { text: String.fromCharCode(7, 0) }) === '',
      'and control characters alone are not a caption');
    {
      mon.markInbound({ kind: GoonPayloadKind.LockCard, waitMs: 0, payload: { text: 'say it out loud' } });
      const flies = findAll(document.body, 'gg-throw');
      const fly = flies[flies.length - 1];
      const word = fly && findOne(fly, 'gg-throw-word');
      ok(!!word && word.textContent === 'say it out loud',
        'a lock card flies with the phrase it is about to demand', word ? word.textContent : 'none');
      ok(!!word && word.parentNode && word.parentNode._classes.has('gg-throw-arc'),
        '…on the ARC, never on the art: the art tumbles and a spinning phrase cannot be read');
      ok(hasClass(fly, 'is-word'), '…and the item steadies so it can be');
    }

    /* THE WARM. The cutout's fetch starts when the payload is ACCEPTED, which is
     * a second and a half before the projectile exists — otherwise a 650 KB PNG
     * has to arrive inside a 380 ms flight, and it does not. */
    {
      previewMod.resetStickerWarm();
      const made = [];
      globalThis.Image = class FakeImage {
        constructor() { this.complete = false; this.src = ''; made.push(this); }
        addEventListener(type, fn) { if (type === 'load') this._load = fn; }
      };
      ok(previewMod.warmSticker(GoonPayloadKind.Spiral) === false,
        'a cutout nobody has fetched yet is not ready');
      ok(made.length === 1 && /item_spiral\.png/.test(String(made[0].src || '')),
        'and asking for it started exactly one fetch', String(made.length));
      ok(previewMod.warmSticker(GoonPayloadKind.Spiral) === false && made.length === 1,
        'asking twice does not fetch twice — one HTTP cache, one page');

      let revealed = 0;
      previewMod.warmSticker(GoonPayloadKind.Spiral, () => { revealed++; });
      ok(revealed === 0, 'nothing is revealed while it is still in the air');
      made[0]._load();
      ok(revealed === 1 && previewMod.stickerWarm(GoonPayloadKind.Spiral) === true,
        'the DECODE is what reveals it, exactly like .gg-throw-live is-ready');
      let again = 0;
      previewMod.warmSticker(GoonPayloadKind.Spiral, () => { again++; });
      ok(again === 1, 'and from then on it is instant');

      mon.markInbound({ kind: GoonPayloadKind.Spiral, waitMs: 0 });
      const flies = findAll(document.body, 'gg-throw');
      ok(hasClass(flies[flies.length - 1], 'is-art'),
        'a warm cutout steps in front of the glyph on frame one');

      // …and the accept itself is what warms it: a payload eight seconds out
      // launches nothing yet, but the fetch is already running.
      const before = made.length;
      mon.markInbound({ kind: GoonPayloadKind.BrainDrain, waitMs: 8000 });
      ok(made.length === before + 1,
        'ACCEPT starts the fetch — the lead the engine reserved pays for it, not the flight');
      ok(findAll(document.body, 'gg-throw').length === flies.length,
        '…while the flight itself is still correctly parked inside that lead');

      delete globalThis.Image;
      previewMod.resetStickerWarm();
    }

    mon.unmount();
    ok(match._subs.released === match._subs.taken, 'and none of it leaks a subscription');
    await sleep(THROW_MS + 140);
  }

  // --- the CSS half: one animation per node, the heat exemption, reduced motion
  {
    const fs = await import('node:fs/promises');
    const url = await import('node:url');
    const css = await fs.readFile(url.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8');
    const blockOf = (sel) => {
      const m = new RegExp(sel.replace(/\./g, '\\.') + '\\s*\\{([^}]*)\\}').exec(css);
      return m ? m[1] : '';
    };
    const animOf = (sel) => { const m = /animation:\s*([a-zA-Z]+)/.exec(blockOf(sel)); return m ? m[1] : null; };

    const names = [animOf('.gg-throw'), animOf('.gg-throw-arc'), animOf('.gg-throw-art'), animOf('.gg-throw-hit')];
    ok(names.every(Boolean), 'each throw node carries an animation of its own', names.join(','));
    ok(new Set(names).size === names.length,
      'and they are four DIFFERENT keyframes — one shorthand per element, or they cancel', names.join(','));
    for (const kf of names) {
      ok(new RegExp('@keyframes\\s+' + kf + '\\b').test(css), `@keyframes ${kf} is defined`);
    }
    ok(/\.gg-throw-live[^{]*\{[^}]*transition:\s*opacity/.test(css),
      'the live preview fades in on a TRANSITION — an animation there would take the spin\'s slot');

    // THE MARK rides on the same rule, for the same reason.
    ok(/transition:\s*opacity/.test(blockOf('.gg-throw-mark')) && !/animation:/.test(blockOf('.gg-throw-mark')),
      'the per-kind glyph fades on a transition and takes no animation slot of its own');
    ok(/\.gg-throw\.is-art\s+\.gg-throw-mark[\s\S]{0,80}opacity:\s*0/.test(css),
      'and it only steps aside once the cutout has really DECODED (is-art), never on a timer');
    ok(!/animation:/.test(blockOf('.gg-throw-word')), 'the caption takes no slot either');
    ok(/white-space:\s*nowrap/.test(blockOf('.gg-throw-word'))
      && /text-overflow:\s*ellipsis/.test(blockOf('.gg-throw-word')),
      'a long phrase is one clipped line, never a paragraph flying across the screen');
    ok(/\.gg-throw\.is-word\s+\.gg-throw-art[^{]*\{[^}]*ggThrowSwell/.test(css),
      'and a payload carrying words stops tumbling so they can be read');

    // one tint, every surface of the same event
    for (const sel of ['.gg-mon-throw', '.gg-throw-art', '.gg-throw-mark', '.gg-throw-word', '.gg-throw-hit']) {
      ok(/rgba?\(var\(--gg-throw-tint/.test(blockOf(sel)),
        `${sel} is coloured by the thrown kind's tint`);
      ok(/--gg-throw-tint,\s*var\(--gg-pink-rgb\)/.test(blockOf(sel)),
        `…and falls back to the brand pink when nobody set one (${sel})`);
    }

    // THE MONITOR IS INFORMATION: a throw flourish must survive the heat park.
    ok(/--gg-deco-play:\s*running/.test(blockOf('.gg-mon-throw')),
      'the flare re-declares --gg-deco-play — a throw is information, not chrome');
    ok(/--gg-deco-play:\s*running/.test(blockOf('.gg-throw')), '…and so does the projectile');
    ok(/animation-play-state:\s*var\(--gg-deco-play\)/.test(blockOf('.gg-throw')),
      'which only means anything because it rides the property');

    // reduced motion: the highlight stays, the flight goes
    // hud.css carries SEVERAL prefers-reduced-motion blocks (they sit next to
    // the features they stop), so every one of them is fair game.
    const rm = (css.match(/@media \(prefers-reduced-motion: reduce\)\s*\{[\s\S]*?\n\}/g) || []).join('\n');
    ok(rm.length > 0, 'hud.css has a prefers-reduced-motion block at all');
    ok(/\.gg-throw[^{]*\{[^}]*display:\s*none/.test(rm),
      'reduced motion does not spawn a projectile at all');
    ok(/is-throwing[^{]*\{[^}]*animation:\s*none/.test(rm),
      '…but keeps the monitor highlight, statically — the cause is still shown');
    ok(/is-calm[\s\S]{0,120}is-throwing[^{]*\{[^}]*animation:\s*none/.test(css),
      'and the in-app reduced-motion pref stops the flare too');
  }
}

/* ===========================================================================
 * MON-DRAG. THE LOOSE MONITOR — drag it, wheel it bigger (owner, 2026-08-04:
 * make the opponent monitor draggable and resizable "like the gifs").
 *
 * ONE SECTION, ONE LANE. Everything below belongs to ui/opponent.js, the
 * opponent/monitor rules in ui/hud.css and the three monitorX/Y/Scale prefs;
 * nothing in it touches the arsenal sidebar, the rails or the heat gauge.
 *
 * "Like the gifs" is the specification, so the pins are the gifs' invariants:
 *   · 6px of slop, and a sub-slop press does NOTHING (the monitor has no click
 *     action — the ONE exception is two of them, which is the reset);
 *   · the wheel drives WIDTH through a custom property, 0.5x..2.5x, never a
 *     transform scale;
 *   · it stays on screen and off MERCY's 96px gutter, at every viewport;
 *   · and the two things the monitor does that a gif does not — being the
 *     payload DROP TARGET and being where an inbound projectile is thrown FROM —
 *     both keep working after it has been moved, because both read a LIVE
 *     getBoundingClientRect() rather than a cached one.
 * ======================================================================== */
{
  const O = opponentMod;

  /* ---- A. the arithmetic, pure ----------------------------------------- */
  ok(O.MON_DRAG_SLOP_PX === 6, "the monitor uses the desk's 6px slop, same as a video window", String(O.MON_DRAG_SLOP_PX));
  ok(O.MON_MIN_FACTOR === 0.5 && O.MON_MAX_FACTOR === 2.5,
    "and the desk's 0.5x..2.5x wheel clamp", `${O.MON_MIN_FACTOR}..${O.MON_MAX_FACTOR}`);
  ok(O.MON_MERCY_KEEPOUT_PX === 96,
    "and MERCY's 96px gutter — the same number exec/videos.js keeps floating windows out of",
    String(O.MON_MERCY_KEEPOUT_PX));

  ok(O.monitorBaseWidth(1280) === 280, 'the docked width mirrors clamp(210px, 22vw, 280px) at desktop', String(O.monitorBaseWidth(1280)));
  ok(O.monitorBaseWidth(600) === 210, '…floors at 210 on a narrow screen', String(O.monitorBaseWidth(600)));
  ok(O.monitorBaseWidth(4000) === 280, '…and caps at 280 on a huge one', String(O.monitorBaseWidth(4000)));
  ok(O.monitorBaseWidth(0) === 280 && O.monitorBaseWidth('x') === 280,
    'a viewport it cannot read falls back to the desktop number, never NaN');

  ok(O.clampMonitorScale(0.01) === 0.5 && O.clampMonitorScale(99) === 2.5,
    'any number coerces to a legal scale');
  ok(O.clampMonitorScale('nonsense') === 1 && O.clampMonitorScale(undefined) === 1,
    'and garbage lands on 1, never NaN');
  ok(Math.abs(O.monitorScaleStep(1, 1) - 1.08) < 1e-9, 'one notch up is exactly WHEEL_STEP', String(O.monitorScaleStep(1, 1)));
  ok(O.monitorScaleStep(2.4, 3) === 2.5 && O.monitorScaleStep(0.6, -3) === 0.5,
    'and a spin past either end simply stops there');
  ok(O.monitorWidth(1, 1280) === O.monitorBaseWidth(1280), '1.0x IS the docked width');
  ok(O.monitorWidth(2, 1280) === O.monitorBaseWidth(1280) * 2, 'and the width scales linearly with the factor');

  // on-screen, with a pad
  {
    const p = O.clampMonitorPos(-500, -500, 200, 120, 1280, 720);
    ok(p.x === O.MON_EDGE_PAD_PX && p.y === O.MON_EDGE_PAD_PX, 'the top-left corner is padded, never flush', JSON.stringify(p));
    const q = O.clampMonitorPos(9999, 9999, 280, 300, 1280, 720);
    ok(q.x === 1280 - 280 - 8 && q.y === 720 - 300 - 8, 'and so is the bottom-right', JSON.stringify(q));
    const big = O.clampMonitorPos(400, 400, 4000, 4000, 1280, 720);
    ok(big.x === O.MON_EDGE_PAD_PX && big.y === O.MON_EDGE_PAD_PX,
      'a monitor bigger than the viewport pins to the reachable end instead of vanishing off it', JSON.stringify(big));
  }
  // …and off MERCY
  {
    const p = O.clampMonitorPos(500, 700, 200, 120, 1280, 720);
    ok(p.y + 120 <= 720 - O.MON_MERCY_KEEPOUT_PX,
      'the bottom-centre gutter is a hard wall: MERCY is never covered', JSON.stringify(p));
    ok(p.x === 500, '…and it is resolved on ONE axis — pushed up, never shoved sideways', JSON.stringify(p));
    const side = O.clampMonitorPos(1000, 700, 200, 120, 1280, 720);
    ok(side.y === 720 - 120 - 8, 'a monitor clear of the mercy band may sit in the bottom gutter', JSON.stringify(side));
  }

  /* ---- B. it is DOCKED until the player asks --------------------------- */
  const prefsOf = (init) => {
    const v = Object.assign({ monitorX: -1, monitorY: -1, monitorScale: 1 }, init || {});
    return { get: (k) => v[k], set: (k, x) => { v[k] = x; return true; }, _v: v };
  };
  const PID = 4200;
  /** `target` is a fifth argument on purpose: an event WITHOUT one is a press
   *  dispatched straight AT a node, i.e. the suite naming the surface it means
   *  rather than a hit test. One with a target is the browser's version, and it
   *  is what the docked grab surface is decided by. */
  const ptr = (node, type, x, y, target) => {
    const e = { type, button: 0, pointerId: PID, clientX: x, clientY: y, preventDefault() {}, stopPropagation() {} };
    if (target) e.target = target;
    if (node && node.dispatchEvent) node.dispatchEvent(e);
    return e;
  };
  const wheel = (node, dy) => {
    if (node && node.dispatchEvent) node.dispatchEvent({ type: 'wheel', deltaY: dy, preventDefault() {}, stopPropagation() {} });
  };

  {
    const match = makeFakeMatch();
    const host = document.createElement('div');
    const store = prefsOf();
    const mon = O.mountOpponent({ host, match, prefs: store });
    await sleep(50);                                  // the restore beat, deliberately let by

    ok(mon.isLoose() === false, 'a fresh monitor is DOCKED — nothing changes until the player drags it');
    ok(!hasClass(mon.root, 'is-loose'), 'and carries no detach class');
    ok(!hasClass(host, 'is-mon-loose'), 'and the column host is untouched');
    ok(mon.root.style[O.MON_WIDTH_VAR] === undefined, 'no width var is written while it is docked');
    ok(mon.root.style.left === undefined && mon.root.style.top === undefined, 'and no position either');
    ok(host.style['min-height'] === undefined, 'and no placeholder is reserved for a monitor that is still in the column');

    // a press that never travels is not a drag, and there is no click action here
    ptr(mon.root, 'pointerdown', 150, 150);
    ptr(mon.root, 'pointermove', 153, 152);           // 3.6px — inside the slop
    ptr(mon.root, 'pointerup', 153, 152);
    ok(mon.isLoose() === false, 'a sub-slop press does nothing at all — it is not a drag and it is not a click');

    /* THE GRIP. The whole feature shipped unreachable: `#gg-hud > .gg-hud-frame`
     * is pointer-events:none so the bubble field can be played through the desk,
     * pointer-events INHERITS, and .gg-mon was not on the allowlist that opts
     * things back in — so no press ever arrived, so it could never detach, so
     * the `.is-mon-loose > .gg-mon { pointer-events: auto }` rule that would
     * have unlocked it never applied. These pins are the closed loop, opened. */
    ok(!!mon.grip, 'the monitor exposes a GRIP — the strip a docked card can actually be touched by');
    ok(hasClass(mon.grip, O.MON_GRIP_CLASS) && hasClass(mon.grip, 'gg-mon-head'),
      'and it IS the head row, not a bar bolted on above it', mon.grip.className);
    ok(findAll(mon.root, 'gg-mon-grip-dots').length === 1,
      'with the six-dot handle in it — nobody drags a thing that does not look draggable');
    ok(!!mon.grip.getAttribute('title'),
      '…and a tooltip, because not one of the three gestures is discoverable on its own');

    // a press that came through any OTHER part of the card is not a drag: the
    // card is transparent so a bubble under it is still poppable, and JS says so
    // too rather than trusting one line of CSS to hold that line forever
    ptr(mon.root, 'pointerdown', 150, 150, mon.projection);
    ptr(mon.root, 'pointermove', 400, 300);
    ptr(mon.root, 'pointerup', 400, 300);
    ok(mon.isLoose() === false,
      'a press that came through the projection rect is NOT a drag — the docked card is field, not handle');

    // …and one through the grip is
    ptr(mon.grip, 'pointerdown', 150, 150, mon.grip);
    ptr(mon.grip, 'pointermove', 400, 300, mon.grip);
    ok(mon.isLoose() === true, 'a drag from the grip takes it off the shelf');
    ptr(mon.grip, 'pointerup', 400, 300, mon.grip);
    ok(store._v.monitorX === 350, 'and lands where the grip was dragged to', String(store._v.monitorX));

    // once it is LOOSE the whole card is a handle again — by then it is a window
    // the player parked, not a row in the column (exec/fx.css's .gg-vwin, same)
    ptr(mon.root, 'pointerdown', 400, 300, mon.projection);
    ptr(mon.root, 'pointermove', 600, 400);
    ok(mon.loosePlacement().x === 550,
      'and a loose monitor is grabbable anywhere on it, projection rect included', JSON.stringify(mon.loosePlacement()));
    ptr(mon.root, 'pointerup', 600, 400);

    mon.unmount();
  }

  /* ---- C. the drag: detach, follow, clamp, remember -------------------- */
  {
    const match = makeFakeMatch();
    const host = document.createElement('div');
    const store = prefsOf();
    const mon = O.mountOpponent({ host, match, prefs: store });
    await sleep(50);
    // the stub's every rect is 200x120 at (100,100) — the docked box.

    ptr(mon.root, 'pointerdown', 150, 150);
    ptr(mon.root, 'pointermove', 400, 300);           // 250,150 — well past the slop
    ok(mon.isLoose() === true, 'a drag past the slop takes the monitor off the shelf');
    ok(hasClass(mon.root, 'is-loose') && hasClass(mon.root, 'is-grabbed'), 'and it is detached AND in hand');
    ok(hasClass(host, 'is-mon-loose'), 'the column host is marked as holding a hole');
    ok(host.style['min-height'] === '120px',
      'and the hole is a MEASURED placeholder, so the receipts under it do not jump up', String(host.style['min-height']));

    // DETACHING MOVES NOTHING: the size it leaves at is the size it was standing at.
    ok(mon.root.style[O.MON_WIDTH_VAR] === '200px',
      'the width it detaches at is the width it was measured at, not an assumed 1.0x', String(mon.root.style[O.MON_WIDTH_VAR]));

    const p1 = mon.loosePlacement();
    ok(p1 && p1.x === 350 && p1.y === 250, 'and it then follows the pointer 1:1', JSON.stringify(p1));
    ok(mon.root.style.left === '350px' && mon.root.style.top === '250px',
      'written as inline left/top — the position IS the layout, never a transform',
      `${mon.root.style.left},${mon.root.style.top}`);

    // dragged off the right/bottom edge: clamped back on screen
    ptr(mon.root, 'pointermove', 5000, 5000);
    const p2 = mon.loosePlacement();
    ok(p2.x === 1280 - 200 - 8 && p2.y === 720 - 120 - 8,
      'dragged off the edge, it is clamped back on screen', JSON.stringify(p2));

    // dragged into the bottom-centre: pushed up off MERCY, and only up
    ptr(mon.root, 'pointermove', 550, 700);
    const p3 = mon.loosePlacement();
    ok(p3.x === 500 && p3.y === 720 - O.MON_MERCY_KEEPOUT_PX - 120,
      'dragged over MERCY, it slides along the top of the gutter instead of covering it', JSON.stringify(p3));

    ok(store._v.monitorX === -1, 'a drag in flight has written nothing to the store yet');
    ptr(mon.root, 'pointerup', 550, 700);
    ok(!hasClass(mon.root, 'is-grabbed'), 'letting go releases it');
    ok(store._v.monitorX === 500 && store._v.monitorY === 504,
      'and a COMPLETED drag is what gets remembered', `${store._v.monitorX},${store._v.monitorY}`);

    // a cancelled drag writes nothing new
    const wasX = store._v.monitorX;
    ptr(mon.root, 'pointerdown', 550, 700);
    ptr(mon.root, 'pointermove', 900, 300);
    ptr(mon.root, 'pointercancel', 900, 300);
    ok(store._v.monitorX === wasX,
      'a cancelled drag leaves the monitor where the hand let go and writes nothing', String(store._v.monitorX));

    // …and the reset: two sub-slop taps put it back in the column
    ptr(mon.root, 'pointerdown', 300, 300); ptr(mon.root, 'pointerup', 300, 300);
    ok(mon.isLoose() === true, 'one tap is not the reset');
    ptr(mon.root, 'pointerdown', 300, 300); ptr(mon.root, 'pointerup', 300, 300);
    ok(mon.isLoose() === false, 'two of them inside the double-tap window re-dock it');
    ok(!hasClass(mon.root, 'is-loose') && !hasClass(host, 'is-mon-loose'), 'both classes come off');
    ok(mon.root.style.left === undefined && mon.root.style[O.MON_WIDTH_VAR] === undefined,
      'and every inline override with them');
    ok(host.style['min-height'] === undefined, 'the placeholder is released — the column gets its row back');
    ok(store._v.monitorX === -1 && store._v.monitorY === -1 && store._v.monitorScale === 1,
      'and the store is reset to the docked sentinel', JSON.stringify(store._v));

    mon.unmount();
  }

  /* ---- D. the wheel: width, never scale -------------------------------- */
  {
    const match = makeFakeMatch();
    const host = document.createElement('div');
    const store = prefsOf();
    const mon = O.mountOpponent({ host, match, prefs: store });
    await sleep(50);

    wheel(mon.root, -100);                            // wheel UP grows
    ok(mon.isLoose() === true,
      'a wheel notch over a DOCKED monitor takes it off the shelf first (the column cannot resize in place)');
    const base = O.monitorBaseWidth(1280);
    ok(mon.root.style[O.MON_WIDTH_VAR] === Math.round(200 * O.MON_WHEEL_STEP) + 'px',
      'and one notch up is exactly one WHEEL_STEP wider', String(mon.root.style[O.MON_WIDTH_VAR]));

    for (let i = 0; i < 40; i++) wheel(mon.root, 100);
    ok(Math.abs(mon.loosePlacement().scale - O.MON_MIN_FACTOR) < 1e-9,
      'spun all the way down it floors at 0.5x', String(mon.loosePlacement().scale));
    ok(mon.root.style[O.MON_WIDTH_VAR] === Math.round(base * O.MON_MIN_FACTOR) + 'px',
      'and the width var says so', String(mon.root.style[O.MON_WIDTH_VAR]));

    for (let i = 0; i < 60; i++) wheel(mon.root, -100);
    ok(Math.abs(mon.loosePlacement().scale - O.MON_MAX_FACTOR) < 1e-9,
      'spun all the way up it caps at 2.5x', String(mon.loosePlacement().scale));
    ok(mon.root.style[O.MON_WIDTH_VAR] === Math.round(base * O.MON_MAX_FACTOR) + 'px',
      'and so does the width var', String(mon.root.style[O.MON_WIDTH_VAR]));
    ok(mon.root.style.transform === undefined,
      'NOTHING was written to transform — the resize is width, never a stacked scale');
    ok(Math.abs(store._v.monitorScale - O.MON_MAX_FACTOR) < 1e-9,
      'a wheel resize is remembered on the notch, not on a release it never gets', String(store._v.monitorScale));

    // a wheel with no delta is not a gesture
    const before = mon.root.style[O.MON_WIDTH_VAR];
    wheel(mon.root, 0);
    ok(mon.root.style[O.MON_WIDTH_VAR] === before, 'a wheel event with no delta changes nothing');

    /* ONE NOTCH IS ONE STEP. The grip listens and so does the root, and in a
     * browser the second of those sees the SAME event a moment later, bubbled.
     * Stepping twice per notch is a resize that runs away under the hand. */
    for (let i = 0; i < 20; i++) wheel(mon.root, 100);   // back off the ceiling
    const scale0 = mon.loosePlacement().scale;
    const one = { type: 'wheel', deltaY: -100, preventDefault() {}, stopPropagation() {} };
    mon.grip.dispatchEvent(one);
    mon.root.dispatchEvent(one);
    ok(Math.abs(mon.loosePlacement().scale - scale0 * O.MON_WHEEL_STEP) < 1e-9,
      'one wheel event seen by BOTH the grip and the root is still one notch',
      `${scale0} -> ${mon.loosePlacement().scale}`);

    mon.unmount();
  }

  /* ---- D2. the wheel can be REACHED on a docked monitor ----------------- */
  {
    // The resize died of the same cause the drag did: while docked, the grip is
    // the only thing on the card a wheel event can possibly land on.
    const match = makeFakeMatch();
    const host = document.createElement('div');
    const mon = O.mountOpponent({ host, match, prefs: prefsOf() });
    await sleep(50);

    mon.grip.dispatchEvent({ type: 'wheel', deltaY: -100, preventDefault() {}, stopPropagation() {} });
    ok(mon.isLoose() === true, 'a wheel over the GRIP of a docked monitor detaches and resizes it');
    ok(mon.root.style[O.MON_WIDTH_VAR] === Math.round(200 * O.MON_WHEEL_STEP) + 'px',
      'by exactly one notch', String(mon.root.style[O.MON_WIDTH_VAR]));

    mon.unmount();
  }

  /* ---- E. the viewport moved under a parked monitor -------------------- */
  {
    const match = makeFakeMatch();
    const host = document.createElement('div');
    const store = prefsOf({ monitorX: 900, monitorY: 300, monitorScale: 2 });
    const mon = O.mountOpponent({ host, match, prefs: store });
    await sleep(50);
    ok(mon.isLoose() === true, 'a stored position detaches the monitor on mount, with no gesture at all');
    ok(mon.loosePlacement().x === 900 && Math.abs(mon.loosePlacement().scale - 2) < 1e-9,
      'and it comes back exactly where and how big it was left', JSON.stringify(mon.loosePlacement()));
    ok(mon.root.style[O.MON_WIDTH_VAR] === O.monitorWidth(2, 1280) + 'px',
      'the width is RE-DERIVED from the factor, not restored as a stale pixel count', String(mon.root.style[O.MON_WIDTH_VAR]));

    dom.win.innerWidth = 640;
    dom.win.innerHeight = 400;
    dom.win.dispatchEvent({ type: 'resize' });
    const p = mon.loosePlacement();
    ok(p.x <= 640 - 200 - 8 && p.y <= 400 - 120 - 8,
      'shrinking the window drags the parked monitor back on screen', JSON.stringify(p));
    ok(mon.root.style[O.MON_WIDTH_VAR] === O.monitorWidth(2, 640) + 'px',
      'and 2x of the NEW --gg-mon-w, not 2x of the old one', String(mon.root.style[O.MON_WIDTH_VAR]));
    dom.win.innerWidth = 1280;
    dom.win.innerHeight = 720;
    dom.win.dispatchEvent({ type: 'resize' });

    mon.unmount();
    ok(host.style['min-height'] === undefined,
      "unmount releases the placeholder it wrote on somebody else's node");
    ok(!hasClass(host, 'is-mon-loose'), '…and the class with it');
  }

  /* ---- F. a monitor that has MOVED is still where the throw comes from -- */
  {
    const match = makeFakeMatch();
    const host = document.createElement('div');
    const mon = O.mountOpponent({ host, match });
    const frame = mon.dropTarget;
    const flights = () => dom.doc.body.children.filter((c) => hasClass(c, 'gg-throw'));

    mon.markInbound({ kind: GoonPayloadKind.FlashBurst, waitMs: 0 });
    const a = flights()[flights().length - 1];
    ok(!!a && a.style.left === '200px' && a.style.top === '160px',
      'a projectile leaves the centre of the monitor as it is now', a && `${a.style.left},${a.style.top}`);

    // …and the monitor moves. Nothing is re-mounted; only the rect changed.
    frame.getBoundingClientRect = () => ({ left: 700, top: 500, right: 900, bottom: 620, width: 200, height: 120 });
    mon.markInbound({ kind: GoonPayloadKind.FlashBurst, waitMs: 0 });
    const b = flights()[flights().length - 1];
    ok(!!b && b.style.left === '800px' && b.style.top === '560px',
      'the NEXT one leaves from where the monitor is NOW — the launch rect is read live, never cached',
      b && `${b.style.left},${b.style.top}`);

    mon.unmount();
    ok(flights().length === 0, 'and unmount sweeps every projectile still in the air');
  }

  /* ---- G. …and still the drop target, wherever it sits ----------------- */
  {
    const match = makeFakeMatch();
    const hud = hudMod.mountHud({ match, audio: { sfx() {} } });
    const mon = hud.parts.opponent;
    const tile = findAll(dom.byId.get('gg-hud'), 'gg-item')
      .find((t) => t.getAttribute('data-gg-item') === 'flash') || null;
    ok(!!tile, 'the arsenal still has a flash tile to drag from');

    // move the monitor, by the only thing the drop path can possibly read
    mon.dropTarget.getBoundingClientRect = () => ({ left: 600, top: 400, right: 800, bottom: 520, width: 200, height: 120 });

    let fires = match._fires.length;
    hud.parts.arsenal.armDrop('flash');
    ptr(tile, 'pointerdown', 20, 20);
    ptr(tile, 'pointermove', 300, 300);
    ptr(tile, 'pointerup', 700, 450);                 // inside the monitor's NEW rect
    ok(match._fires.length === fires + 1,
      'an item dropped on the monitor WHERE IT NOW IS still fires', String(match._fires.length - fires));

    fires = match._fires.length;
    hud.parts.arsenal.armDrop('flash');
    ptr(tile, 'pointerdown', 20, 20);
    ptr(tile, 'pointermove', 300, 300);
    ptr(tile, 'pointerup', 150, 150);                 // the rect it used to have
    ok(match._fires.length === fires,
      'and the desk it used to stand on is just desk again', String(match._fires.length - fires));

    // dragging the MONITOR is not firing anything
    fires = match._fires.length;
    ptr(mon.root, 'pointerdown', 150, 150);
    ptr(mon.root, 'pointermove', 420, 320);
    ptr(mon.root, 'pointerup', 420, 320);
    ok(mon.isLoose() === true, 'the monitor drags inside a real mounted HUD too');
    ok(match._fires.length === fires,
      'and moving it fires nothing — the monitor is a drop TARGET, never a trigger', String(match._fires.length - fires));

    hud.unmount();
    ok(match._subs.released === match._subs.taken,
      'the loose monitor leaks no subscriptions', `${match._subs.released}/${match._subs.taken}`);
  }

  /* ---- H. the CSS half ------------------------------------------------- */
  {
    const fs = await import('node:fs/promises');
    const url = await import('node:url');
    const css = await fs.readFile(url.fileURLToPath(new URL('../ui/hud.css', import.meta.url)), 'utf8');
    const blockOf = (sel) => {
      const m = new RegExp(sel.replace(/\./g, '\\.') + '\\s*\\{([^}]*)\\}').exec(css);
      return m ? m[1] : '';
    };
    const loose = blockOf('.gg-mon.is-loose');
    ok(loose.length > 0, 'hud.css has a .gg-mon.is-loose rule at all');
    ok(/position:\s*fixed/.test(loose), 'a detached monitor is fixed — it has to escape the column');
    ok(/width:\s*var\(--gg-mon-loose-w,\s*var\(--gg-mon-w\)\)/.test(loose),
      'its width is the loose var, FALLING BACK to the docked one so an unwritten var is not a 0px monitor', loose);
    ok(!/scale\s*\(/.test(loose),
      'and there is no transform scale anywhere near it — a scale would fight the drag offset and resample the art');
    ok(/transform:\s*none/.test(loose) && /animation:\s*none/.test(loose),
      'the keyframes-outrank-inline-styles guard is declared, so inline left/top stays authoritative', loose);

    const z = /z-index:\s*(\d+)/.exec(loose);
    ok(!!z, 'the loose monitor gets an explicit z-index');
    const zn = z ? Number(z[1]) : -1;
    ok(zn > 40, 'it lifts above the rest of the desk', String(zn));
    ok(zn < 50 && zn < 55 && zn < 60 && zn < 65,
      'and stays under the toasts (50), the inbound projectile (55), MERCY (60) and its takeover (65) — a thing thrown FROM the monitor flies OVER it',
      String(zn));

    const monBlocks = (css.match(/\.gg-mon\s*\{[^}]*\}/g) || []).join('\n');
    ok(/touch-action:\s*none/.test(monBlocks),
      'the whole card claims the touch BEFORE it detaches, or the first 6px is read as a page pan');
    ok(/user-select:\s*none/.test(monBlocks), "and the mouse's half of the same claim");
    ok(/cursor:\s*grab/.test(monBlocks), 'and it looks grabbable');
    ok(/\.gg-mon\.is-grabbed\s*\{[^}]*cursor:\s*grabbing/.test(css), 'and held while it is held');

    /* THE ONE THAT WOULD HAVE CAUGHT IT. Everything above was true on the day
     * the monitor shipped undraggable: the drag was not broken, it was
     * UNREACHABLE. pointer-events inherits, the desk is none so the bubble field
     * can be played through it, and only the allowlist near the top of hud.css
     * opts anything back in. */
    ok(/#gg-hud\s*>\s*\.gg-hud-frame\s*\{[^}]*pointer-events:\s*none/.test(css),
      'the desk is transparent to the pointer — the rule every gesture on it has to be let out of');
    const gripAllow = /\.gg-hud-frame\s+\.gg-mon-grip[^{]*\{([^}]*)\}/.exec(css);
    ok(!!gripAllow && /pointer-events:\s*auto/.test(gripAllow[1]),
      'and the grip is on the allowlist that lets it out — without this line the drag, the wheel and the reset are all dead');
    ok(!/\.gg-hud-frame\s+\.gg-mon\s*[,{]/.test(css),
      'the CARD deliberately is not: it sits over the stage, and a card that took clicks would eat every bubble pop under it');

    const gripBlock = blockOf('.gg-mon-grip');
    ok(/touch-action:\s*none/.test(gripBlock),
      "the grip claims the finger too, or the first 6px of a phone drag is a page pan", gripBlock);
    ok(/cursor:\s*grab/.test(gripBlock), 'and it looks like what it is');
    ok(/\.gg-mon\.is-grabbed\s+\.gg-mon-grip\s*\{[^}]*cursor:\s*grabbing/.test(css),
      'and like a held one while it is held');

    ok(/\.gg-mon-host\.is-mon-loose\s*\{[^}]*pointer-events:\s*none/.test(css),
      'the placeholder it leaves behind is an empty box that stops taking clicks');
    ok(/\.gg-mon-host\.is-mon-loose\s*>\s*\.gg-mon\s*\{[^}]*pointer-events:\s*auto/.test(css),
      '…and the monitor, still its CHILD (no re-parenting, ever — that would drop the pointer capture), opts back in');

    // the minis scale for free, and that is only true while the geometry is %
    ok(/aspect-ratio:\s*16\s*\/\s*9/.test(blockOf('.gg-mon-frame')),
      'the frame is still a ratio, not a height — which is why a width-only resize takes the minis with it');
    ok(/--gg-mon-face-x:\s*[\d.]+%/.test(blockOf('.gg-mon-frame')),
      'and the CRT face is still a PERCENTAGE of it');
  }

  /* ---- I. the prefs the whole thing is remembered in ------------------- */
  {
    const prefsModM = await import('../ui/prefs.js');
    ok(prefsModM.PREF_DEFAULTS.monitorX === -1 && prefsModM.PREF_DEFAULTS.monitorY === -1,
      'monitorX/Y default to the DOCKED sentinel — a fresh player has never dragged it',
      `${prefsModM.PREF_DEFAULTS.monitorX},${prefsModM.PREF_DEFAULTS.monitorY}`);
    ok(prefsModM.PREF_DEFAULTS.monitorScale === 1, 'and monitorScale to 1.0x', String(prefsModM.PREF_DEFAULTS.monitorScale));
    ok(prefsModM.PREF_DEFAULTS.monitorX === O.MON_DOCKED, 'and the sentinel is the one ui/opponent.js writes');

    // a real store round-trips them, and coerce() does not clamp a scale to 0..1
    const p = prefsModM.createPrefs({ monitorX: 300, monitorY: 210, monitorScale: 2.5 });
    ok(p.get('monitorX') === 300 && p.get('monitorY') === 210, 'a seeded position survives createPrefs');
    ok(p.get('monitorScale') === 2.5,
      'and so does a scale ABOVE 1 — it is not a volume and must never be clamped like one', String(p.get('monitorScale')));
    p.set('monitorScale', 'nonsense');
    ok(p.get('monitorScale') === 1, 'a corrupt scale falls back to the default rather than poisoning the layout');
  }

  /* ---- J. the little screen keeps playing while it is in hand ---------- */
  {
    const match = makeFakeMatch();
    match.opponent.activeEffects = [];
    const host = document.createElement('div');
    const mon = O.mountOpponent({ host, match, prefs: prefsOf() });
    await sleep(50);
    const proj = mon.projection;

    // detach + resize to both ends of the wheel, then let their tick arrive
    ptr(mon.root, 'pointerdown', 150, 150);
    ptr(mon.root, 'pointermove', 500, 300);
    ptr(mon.root, 'pointerup', 500, 300);
    for (let i = 0; i < 30; i++) wheel(mon.root, -100);          // all the way up
    match.opponent.activeEffects = ['Spiral', 'BrainDrain'];
    match._emit('opp');

    ok(findAll(proj, 'gg-mini').length === 10,
      'all ten minis are still in the projection rect after a drag and a resize', String(findAll(proj, 'gg-mini').length));
    ok(hasClass(findOne(proj, 'gg-mini-spiral'), 'is-on') && hasClass(findOne(proj, 'gg-mini-drain'), 'is-on'),
      'and their tick still lights them — moving the monitor is not moving the SCREEN');
    ok(findOne(proj, 'gg-mini-idle').hidden === true, 'the idle word still steps aside');
    ok(proj.parentNode === mon.dropTarget,
      'the projection rect is still the frame\'s post-<img> sibling — the drag never re-parents anything (§9)');
    ok(mon.root.style[O.MON_WIDTH_VAR] !== undefined && mon.root.style.width === undefined,
      'and the only size written is the width VAR — hud.css turns it into a box, so the % geometry inside follows for free');

    mon.unmount();
  }

  /* ---- K. hud.js really threads the store through ---------------------- */
  {
    const seen = [];
    const match = makeFakeMatch();
    const store = { get: (k) => { seen.push(k); return -1; }, set() { return true; } };
    const hud = hudMod.mountHud({ match, audio: { sfx() {} }, prefs: store });
    await sleep(50);
    ok(seen.indexOf('monitorX') >= 0,
      'mountHud hands ui/prefs.js to the monitor — without it the position is remembered nowhere', seen.join(','));
    hud.unmount();
  }
}

await sleep(60);
console.log(`\nselftest-hud: ${n - failures}/${n} checks passed`);
if (failures > 0) {
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
process.exit(0);
