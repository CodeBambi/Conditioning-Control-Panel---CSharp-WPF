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
  for (const id of ['gg-hud', 'gg-mercy', 'gg-stage', 'scr-sd', 'gg-toasts']) {
    const n = makeNode('div');
    n.id = id;
    byId.set(id, n);
    doc.body.appendChild(n);
  }
  doc.createElement = (tag) => makeNode(tag);
  doc.createTextNode = (t) => { const n = makeNode('#text'); n.textContent = String(t); return n; };
  doc.getElementById = (id) => byId.get(id) || null;
  doc.querySelector = () => null;

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

  const res = ars.fire('flash');
  ok(res && res.ok === true, 'arsenal fire path reaches the engine');
  ok(match._fires.length === 1, 'exactly one payload per fire');
  const req = match._fires[0];
  const kinds = Object.values(GoonPayloadKind);
  ok(kinds.includes(req.kind), 'fired kind is in GoonPayloadKind', String(req.kind));
  ok(req.durationMs >= 1000 && req.durationMs <= 180000, 'duration inside the engine clamps', String(req.durationMs));
  ok(req.intensity > 0 && req.intensity <= 1, 'intensity inside 0..1', String(req.intensity));

  // every payload item can be fired and every kind it sends is legal
  for (const item of arsenalMod.ARSENAL_ITEMS) {
    if (item.kind === null) continue;
    ars.fire(item.id);
  }
  ok(match._fires.every((f) => kinds.includes(f.kind)), 'every fired kind is a real payload kind');
  ok(match._fires.every((f) => f.durationMs >= 1000 && f.durationMs <= 180000), 'every duration is inside the clamps');

  // a heavy is one per match: the second brain drain must be refused locally
  const before = match._fires.length;
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
  const res = ars.fire('spiral');
  ok(res && res.ok === true, 'the spiral slot fires');
  ok(match._fires.length === 1 && match._fires[0].kind === GoonPayloadKind.Spiral,
    'spiral fires GoonPayloadKind.Spiral', JSON.stringify(match._fires[0] || null));
  ok(match._fires[0].durationMs >= 1000 && match._fires[0].durationMs <= 180000, 'spiral duration is inside the clamps');
  ok(fired.length === 1 && fired[0].id === res.id && fired[0].kind === GoonPayloadKind.Spiral && fired[0].durationMs > 0,
    'onFired hands hud.js the kind + duration + id of an accepted fire', JSON.stringify(fired));
  ars.unmount();
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
  ok(minis.length === 9, 'nine minis, all inside the projection rect', String(minis.length));
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

await sleep(60);
console.log(`\nselftest-hud: ${n - failures}/${n} checks passed`);
if (failures > 0) {
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
process.exit(0);
