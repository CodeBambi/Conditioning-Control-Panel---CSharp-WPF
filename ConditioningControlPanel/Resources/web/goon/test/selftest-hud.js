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
const { riskTierOf } = await import('../core/draft.js');
const { ELEMENTS } = await import('../ui/strings.js');

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
  ok(left.children.length === 5, 'left rail renders 5 items', String(left.children.length));
  ok(right.children.length === 4, 'right rail renders 4 items', String(right.children.length));
  ok(left.children.length + right.children.length === arsenalMod.ARSENAL_ITEMS.length,
    'every arsenal item got a slot (9 with spiral)', String(left.children.length + right.children.length));
  ok(arsenalMod.ARSENAL_ITEMS.some((i) => i.id === 'spiral' && i.kind === GoonPayloadKind.Spiral),
    'the spiral slot is wired to GoonPayloadKind.Spiral');
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

/* --- 2b-iii. ui/drops.js: the roller ---------------------------------------
 * Pure functions first (chance curve + rarity weighting), then the roller with
 * a scripted RNG so the whole pop -> credit -> arm chain is deterministic. */
{
  const { DROP_TUNING, dropChanceFor, weightOf, pickDrop, createDropRoller } = await import('../ui/drops.js');
  const bub = await import('../exec/bubbles.js');

  ok(typeof DROP_TUNING.CHANCE_PER_WORTH === 'number' && typeof DROP_TUNING.COST_BIAS === 'number',
    'ui/drops.js exports one tuning block');
  ok(Math.abs(dropChanceFor(bub.POP_WORTH_NORMAL) - 0.12) < 1e-9,
    'a normal bubble drops ~12% of the time', String(dropChanceFor(bub.POP_WORTH_NORMAL)));
  ok(Math.abs(dropChanceFor(bub.POP_WORTH_EFFECT) - 0.30) < 1e-9,
    'an effect bubble drops ~30% of the time', String(dropChanceFor(bub.POP_WORTH_EFFECT)));
  ok(dropChanceFor(bub.POP_WORTH_EFFECT) > dropChanceFor(bub.POP_WORTH_NORMAL),
    'effect bubbles are worth strictly more than plain ones');
  ok(dropChanceFor(bub.POP_WORTH_PAYLOAD) === 0, 'a payload-minted bubble never rolls');
  ok(dropChanceFor(undefined) === 0 && dropChanceFor(null) === 0, 'a malformed worth never rolls');
  ok(dropChanceFor(999) <= DROP_TUNING.CHANCE_MAX, 'the chance is ceilinged', String(dropChanceFor(999)));
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
  const credits = [];
  const match = {
    phase: GoonMatchPhase.Live,
    creditCharges(n, reason) { credits.push({ n, reason }); return true; },
  };
  const rolls = [0, 0];   // always a hit, always the first candidate
  const roller = createDropRoller({ match, arsenal: fakeArsenal, random: () => rolls.shift() ?? 0 });

  const clutter = roller.onPop({ kind: 'normal', worth: bub.POP_WORTH_PAYLOAD, payload: true });
  ok(clutter.dropped === false && clutter.reason === 'clutter', 'a payload-minted pop is skipped entirely');
  ok(credits.length === 0, 'and never credits a charge');

  const hit = roller.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT, x: 10, y: 10 });
  ok(hit.dropped === true && hit.id === 'flash', 'a winning roll arms an item', JSON.stringify(hit));
  ok(credits.length === 1 && credits[0].n === 1 && credits[0].reason === 'bubble-drop',
    'and credits the charge the item will cost, on the engine seam', JSON.stringify(credits));
  ok(armedIds.length === 1, 'exactly one arm per drop');

  // the engine refusing (wrong phase, capped, whatever) must block the arm
  const noCredit = createDropRoller({
    match: { phase: GoonMatchPhase.Live, creditCharges: () => false },
    arsenal: fakeArsenal,
    random: () => 0,
  });
  const blocked = noCredit.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT });
  ok(blocked.dropped === false && blocked.reason === 'no-charge',
    'creditCharges() saying no blocks the arm', JSON.stringify(blocked));
  ok(armedIds.length === 1, 'and nothing was armed behind its back', String(armedIds.length));

  // outside Live nothing rolls at all
  const idle = createDropRoller({ match: { phase: GoonMatchPhase.Draft }, arsenal: fakeArsenal, random: () => 0 });
  ok(idle.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT }).reason === 'phase', 'no drops outside a live match');

  // a missing seam (older engine) must not break the loop
  const noSeam = createDropRoller({ match: { phase: GoonMatchPhase.Live }, arsenal: fakeArsenal, random: () => 0 });
  ok(noSeam.onPop({ kind: 'flash', worth: bub.POP_WORTH_EFFECT }).dropped === true,
    'the roller still works against an engine without creditCharges');

  // a miss is a miss
  const missed = createDropRoller({ match, arsenal: fakeArsenal, random: () => 0.99 });
  ok(missed.onPop({ kind: 'normal', worth: bub.POP_WORTH_NORMAL }).reason === 'miss', 'a losing roll drops nothing');
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

// -------------------------------- 2c. the risk table in ui/strings.js agrees
// Two tables describe the same thing (core/draft.js is authority, strings.js
// carries a copy for the tile). Nothing else cross-checks them.
{
  for (const meta of ELEMENTS) {
    ok(meta.risk === riskTierOf(meta.id), `ELEMENTS risk matches core/draft.js for ${meta.name}`,
      `${meta.risk} vs ${riskTierOf(meta.id)}`);
  }
  const spiral = ELEMENTS.find((e) => e.id === GoonElement.Spiral);
  ok(!!spiral && spiral.name === 'spiral' && typeof spiral.blurb === 'string' && spiral.blurb.length > 10,
    'the draft pool carries a spiral tile with copy');
  ok(!!spiral && spiral.blurb === spiral.blurb.toLowerCase(), 'element blurbs stay lowercase');
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
 * assets/monitor_frame.png is OPAQUE art with a painted pure-black CRT face, and
 * the projection rect was a child of .gg-mon-screen (z-index:1 -> its own
 * stacking context) while the <img> sat at z-index:2. Every mini rendered under
 * the art. Structure and z-order are BOTH asserted, because either one alone
 * puts it back under the paint.
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
    const v = { masterVolume: 0.8, musicVolume: 0.5, sfxVolume: 0.7, reduceMotion: false };
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
 * mini still never lit. */
{
  const match = makeFakeMatch();
  match.opponent.activeEffects = [];
  const hud = hudMod.mountHud({ match, audio: { sfx() {} } });
  const proj = findOne(dom.byId.get('gg-hud'), 'gg-mon-proj');
  const bub = findOne(proj, 'gg-mini-bubbles');

  hud.parts.arsenal.armDrop('bubbles');    // items are EARNED now (drop economy)
  const res = hud.parts.arsenal.fire('bubbles');
  ok(res && res.ok === true, 'the real desk fires a bubble swarm');
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
  const { cueGain, pickVariant, SFX_TRIM, POOL_MAX, MIN_GAP_MS, UNLOCK_EVENTS } = audioMod;

  ok(SFX_TRIM > 0 && SFX_TRIM <= 1, 'there is ONE exported master trim over every cue', String(SFX_TRIM));
  ok(POOL_MAX >= 4 && POOL_MAX <= 16,
    'the pool cap is a real ceiling — 26 bubbles may not become 26 sources', String(POOL_MAX));
  ok(MIN_GAP_MS > 0, 'and same-frame repeats of one id are swallowed', String(MIN_GAP_MS));
  ok(UNLOCK_EVENTS.includes('pointerdown') && UNLOCK_EVENTS.includes('keydown'),
    'the gesture-unlock list covers pointer AND keyboard');

  ok(Math.abs(cueGain(0.5, 1, 1) - 0.5 * SFX_TRIM) < 1e-9, 'cueGain folds the house trim in');
  ok(cueGain(0.5, 0, 1) === 0, 'master 0 is silence');
  ok(cueGain(0.5, 1, 0) === 0, 'sfx 0 is silence');
  ok(Math.abs(cueGain(0.5, 0.5, 0.5) - 0.5 * 0.5 * 0.5 * SFX_TRIM) < 1e-9,
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
  bus.setVolume('sfx', 0.5);
  ok(bus.volumes.sfx === 0.5, 'setVolume writes the bus it names');
  bus.setVolume('nonsense', 0.1);
  ok(bus.volumes.sfx === 0.5, 'and ignores a bus it does not have');
  ok(bus.unlock() === 'none', 'unlock() on a hostless bus reports none rather than throwing');
  bus.dispose();
  ok(bus.sfx('bubble-pop') === false, 'a disposed bus is inert');

  // It must read the player's existing sliders, not invent a second set.
  const prefsMod = await import('../ui/prefs.js');
  const prefs = prefsMod.createPrefs({ masterVolume: 0.4, sfxVolume: 0.6 });
  const bus2 = audioMod.createAudio({ prefs });
  ok(bus2.volumes.master === 0.4 && bus2.volumes.sfx === 0.6,
    'the bus seeds from ui/prefs.js — no settings UI of its own', JSON.stringify(bus2.volumes));
  prefs.set('sfxVolume', 0.2);
  ok(bus2.volumes.sfx === 0.2, 'and follows a live slider drag');
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
  ok((dropsSrc.match(/sfx\('gg-drop-dud'\)/g) || []).length === 2,
    'and BOTH fizzle paths (no charge / arsenal full) cue the dud');
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
 * 13. THE ZEN TOGGLE — one button, four panels (owner, 2026-08-04).
 *
 * "add a button that hides the ui": the score, the risk multiplier, the
 * closeness dial ("you're telling them") and MERCY step off together; the
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
  for (const sel of ['.gg-score', '.gg-risk', '.gg-dial-host']) {
    ok(zenText.includes(sel), `zen hides ${sel}`);
  }
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
}

await sleep(60);
console.log(`\nselftest-hud: ${n - failures}/${n} checks passed`);
if (failures > 0) {
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
process.exit(0);
