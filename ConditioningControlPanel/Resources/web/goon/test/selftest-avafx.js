// Self-contained sanity pass over ui/avatarFx.js + ui/avatarfx.css — the
// reaction layer on the Discord avatar bubbles (Work Item E).
//
//   node Resources/web/goon/test/selftest-avafx.js
//
// What it proves:
//   1. the module imports clean under node (no DOM at import time) and exposes
//      exactly the contract surface: avatarFx.attach(rootEl) / avatarFx.detach();
//   2. attach() is idempotent — twice is once, one document listener, one
//      observer — and no-ops with zero .gg-ava on screen or no attach at all;
//   3. every contract kind lands its class on the right bubble, and the PAIRINGS
//      E owns fire (fire -> alarm, mercy -> bow, draw/cue -> both);
//   4. one-shots SELF-CLEAN: animationend from the img/tile clears, animationend
//      bubbling up from a decoration child does NOT (the known trap), and the
//      timeout backstop clears a reaction no animationend ever answers for;
//   5. pop is throttled to <=4/s;
//   6. priority: a land interrupts a pop, a win overrides a land, and the latched
//      win refuses everything under it until reset()/node removal;
//   7. reduced motion collapses every reaction to <=1 frame with no decoration;
//   8. detach() disconnects the observer, drops the listener, kills the timers
//      and takes every class back off;
//   9. the CSS honours the one-animation-slot budget, the --gg-deco-play heat
//      governor and the z-stack contract (no z-index, no MERCY, no layout props).

// ------------------------------------------------------------------ DOM stub

function installDom() {
  const observers = new Set();

  function notify(target, added, removed) {
    if (observers.size === 0) return;
    for (const obs of Array.from(observers)) {
      if (!obs._root) continue;
      const inScope = obs._root === target
        || (typeof obs._root.contains === 'function' && obs._root.contains(target));
      if (!inScope) continue;
      const rec = { type: 'childList', target, addedNodes: added || [], removedNodes: removed || [] };
      setTimeout(() => { try { obs._cb([rec], obs); } catch (_e) { /* ignore */ } }, 0);
    }
  }

  function makeStyle() {
    const s = {};
    s.setProperty = (k, v) => { s[k] = v; };
    s.removeProperty = (k) => { delete s[k]; };
    return s;
  }

  function makeNode(tagName) {
    const kids = [];
    const map = new Map();
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
      textContent: '',
      get className() { return Array.from(classes).join(' '); },
      set className(v) { classes.clear(); String(v || '').split(/\s+/).filter(Boolean).forEach((c) => classes.add(c)); },
      classList: {
        add: (...c) => c.forEach((x) => classes.add(x)),
        remove: (...c) => c.forEach((x) => classes.delete(x)),
        toggle: (c, on) => (on ? classes.add(c) : classes.delete(c)),
        contains: (c) => classes.has(c),
      },
      appendChild(child) {
        if (child) { child.parentNode = node; child.isConnected = true; kids.push(child); notify(node, [child], []); }
        return child;
      },
      append(...c) { c.forEach((x) => node.appendChild(x)); },
      removeChild(child) {
        const i = kids.indexOf(child);
        if (i >= 0) { kids.splice(i, 1); notify(node, [], [child]); }
        return child;
      },
      remove() {
        const p = node.parentNode;
        if (p) p.removeChild(node);
        node.parentNode = null;
        node.isConnected = false;
      },
      contains(o) {
        if (o === node) return true;
        for (const k of kids) if (k && typeof k.contains === 'function' && k.contains(o)) return true;
        return false;
      },
      setAttribute(k, v) { attrs.set(k, String(v)); if (k.startsWith('data-')) node.dataset[k.slice(5).replace(/-([a-z])/g, (m, c) => c.toUpperCase())] = String(v); },
      getAttribute(k) { return attrs.has(k) ? attrs.get(k) : null; },
      removeAttribute(k) { attrs.delete(k); },
      addEventListener(type, fn) { if (!map.has(type)) map.set(type, new Set()); map.get(type).add(fn); },
      removeEventListener(type, fn) { const s = map.get(type); if (s) s.delete(fn); },
      dispatchEvent(evt) {
        const s = map.get(evt && evt.type);
        if (s) for (const fn of Array.from(s)) { try { fn(evt); } catch (_e) { /* ignore */ } }
        return true;
      },
      /** Just enough selector engine for `.class` lists and bare tag names. */
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
      _listeners: map,
      _classes: classes,
    };
    return node;
  }

  const doc = makeNode('#document');
  doc.documentElement = makeNode('html');
  doc.body = makeNode('body');
  doc.createElement = (tag) => makeNode(tag);
  doc.createTextNode = (t) => { const n = makeNode('#text'); n.textContent = String(t); return n; };
  doc.querySelectorAll = (sel) => doc.body.querySelectorAll(sel);
  doc.querySelector = (sel) => doc.body.querySelectorAll(sel)[0] || null;

  const win = makeNode('window');
  globalThis.document = doc;
  globalThis.window = win;
  globalThis.CustomEvent = class CustomEvent {
    constructor(type, init) { this.type = type; this.detail = init && init.detail; this.bubbles = !!(init && init.bubbles); }
  };
  globalThis.MutationObserver = class MutationObserver {
    constructor(cb) { this._cb = cb; this._root = null; }
    observe(root) { this._root = root; observers.add(this); }
    disconnect() { this._root = null; observers.delete(this); }
    takeRecords() { return []; }
  };
  // Reduced motion is OFF unless a block flips it — every module has to survive
  // matchMedia being absent too, which is why the module probes it in a try.
  globalThis.__calm = false;
  globalThis.matchMedia = (q) => ({ matches: /prefers-reduced-motion/.test(String(q)) && !!globalThis.__calm });

  return { doc, win, observers, makeNode };
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

const hasClass = (node, c) => !!(node && node._classes && node._classes.has(c));
const classesOf = (node) => Array.from(node._classes || []).join(' ');

/** Build the DOM D promises: `.gg-ava[data-side]` + an img or a tile child. */
function makeAva(side, flavor = 'img') {
  const box = dom.makeNode('div');
  box.className = 'gg-ava';
  box.setAttribute('data-side', side);
  const kid = dom.makeNode(flavor === 'img' ? 'img' : 'span');
  kid.className = flavor === 'img' ? 'gg-ava-img' : 'gg-ava-tile';
  box.appendChild(kid);
  return box;
}

function fire(kind, side, meta) {
  document.dispatchEvent(new CustomEvent('gg-ava', { detail: { kind, side, meta }, bubbles: true }));
}

// ------------------------------------------------------------------ imports

const mod = await import('../ui/avatarFx.js');
const {
  avatarFx, createAvatarFx, AVA_EVENT, AVA_KINDS, AVA_SIDES, REACTIONS,
  ON_CLASS, IDLE_CLASS, BUSY_CLASS, DECO_CLASS, TERMINAL_CLASS, POP_MIN_MS,
  FIRE_ECHO_MS, EMOTE_MAX_MS, fxClass,
} = mod;

ok(typeof avatarFx === 'object' && avatarFx !== null, 'ui/avatarFx.js exports an avatarFx singleton');
ok(typeof avatarFx.attach === 'function', 'and avatarFx.attach(rootEl)');
ok(typeof avatarFx.detach === 'function', 'and avatarFx.detach()');
ok(mod.default === avatarFx, 'the default export is the same instance');
ok(typeof createAvatarFx === 'function', 'plus a factory, so a test can run an isolated instance');

// ---- the frozen contract surface (docs/GOON_DISCORD_CONTRACT.md §6)
ok(AVA_EVENT === 'gg-ava', 'the event name is the contract one', String(AVA_EVENT));
ok(JSON.stringify(AVA_KINDS) === JSON.stringify(['land', 'fire', 'drop', 'pop', 'emote', 'mercy', 'win', 'lose', 'draw', 'cue']),
  'the kinds are the contract kinds, in contract order', JSON.stringify(AVA_KINDS));
ok(JSON.stringify(AVA_SIDES) === JSON.stringify(['you', 'opp']), 'and the sides are you|opp', JSON.stringify(AVA_SIDES));
for (const k of AVA_KINDS) ok(!!REACTIONS[k], 'the catalog covers the contract kind "' + k + '"');
ok(!!REACTIONS.alarm && !!REACTIONS.bow,
  'plus the two beats E invents to pair the bubbles up: alarm (after a fire) and bow (after a mercy)');

// ------------------------------------------------- 0. nothing attached at all

{
  const solo = createAvatarFx();
  let threw = null;
  try { fire('land', 'you'); fire('win', 'you'); } catch (e) { threw = e; }
  ok(!threw, 'firing gg-ava with nothing attached does not throw', threw && threw.stack);
  ok(solo.attached === false && solo.count === 0, 'and an un-attached instance stays empty');
  let dThrew = null;
  try { solo.detach(); solo.detach(); } catch (e) { dThrew = e; }
  ok(!dThrew, 'detach() on an instance that was never attached is a no-op', dThrew && dThrew.stack);
}

// -------------------------------------------- 1. attach: idempotence + no DOM

{
  const empty = dom.makeNode('div');
  document.body.appendChild(empty);
  const fx = createAvatarFx();
  let threw = null;
  try { fx.attach(empty); } catch (e) { threw = e; }
  ok(!threw, 'attach() over a root with zero .gg-ava does not throw', threw && threw.stack);
  ok(fx.count === 0, 'and adopts nothing');
  let evThrew = null;
  try { for (const k of AVA_KINDS) { fire(k, 'you'); fire(k, 'opp'); } } catch (e) { evThrew = e; }
  ok(!evThrew, 'and every kind fired at an empty page is a no-op', evThrew && evThrew.stack);
  fx.detach();
  empty.remove();
}

{
  const root = dom.makeNode('div');
  document.body.appendChild(root);
  const you = makeAva('you');
  const opp = makeAva('opp', 'tile');
  root.appendChild(you);
  root.appendChild(opp);

  const fx = createAvatarFx();
  fx.attach(root);
  ok(fx.count === 2, 'attach() adopts both bubbles already on screen', String(fx.count));
  ok(hasClass(you, ON_CLASS) && hasClass(opp, ON_CLASS), 'and marks them adopted');
  ok(hasClass(you, IDLE_CLASS) && hasClass(opp, IDLE_CLASS), 'and arms the idle loop on both');
  ok(fx.stateOf(opp).side === 'opp', 'the side comes off data-side, not off DOM order', String(fx.stateOf(opp).side));

  const listeners = document._listeners.get('gg-ava');
  ok(!!listeners && listeners.size === 1, 'one document listener', String(listeners && listeners.size));
  ok(dom.observers.size === 1, 'one MutationObserver', String(dom.observers.size));

  fx.attach(root);
  fx.attach(root);
  ok(fx.count === 2, 'attach() again changes nothing — it is idempotent', String(fx.count));
  ok(document._listeners.get('gg-ava').size === 1, 'still one document listener after three attaches',
    String(document._listeners.get('gg-ava').size));
  ok(dom.observers.size === 1, 'still one observer', String(dom.observers.size));

  // A tile bubble is the fallback D ships when there is no Discord art, and it
  // has to react exactly like an img one.
  fire('pop', 'opp');
  ok(hasClass(opp, fxClass('pop')), 'a .gg-ava-tile bubble reacts like an .gg-ava-img one', classesOf(opp));

  fx.detach();
  root.remove();
}

// --------------------------------------------- 2. every kind -> every bubble

{
  const root = dom.makeNode('div');
  document.body.appendChild(root);
  const you = makeAva('you');
  const opp = makeAva('opp');
  root.appendChild(you);
  root.appendChild(opp);
  const fx = createAvatarFx();
  fx.attach(root);

  // The solo beats: they touch the named side and nobody else.
  for (const kind of ['land', 'drop', 'pop', 'emote']) {
    fx.reset();
    for (const node of [you, opp]) node.classList.remove(fxClass(kind));
    fire(kind, 'you');
    ok(hasClass(you, fxClass(kind)), '"' + kind + '" on side=you lands ' + fxClass(kind), classesOf(you));
    ok(hasClass(you, BUSY_CLASS), 'and parks the idle loop for the length of it');
    ok(!hasClass(opp, fxClass(kind)), 'and never touches the other bubble');
    fx.reset();
    fire(kind, 'opp');
    ok(hasClass(opp, fxClass(kind)), '"' + kind + '" on side=opp lands on the far bubble', classesOf(opp));
    fx.reset();
  }

  // Heat: a hit on YOUR bubble is longer than a hit on theirs.
  ok(REACTIONS.land.ms > REACTIONS.land.msOpp,
    'land reads heavier on you than on them', REACTIONS.land.ms + ' vs ' + REACTIONS.land.msOpp);
  fire('land', 'you');
  ok(you.style['--gga-dur'] === REACTIONS.land.ms + 'ms',
    'and the JS hands CSS the duration it will actually wait for', String(you.style['--gga-dur']));
  fx.reset();
  fire('land', 'opp');
  ok(opp.style['--gga-dur'] === REACTIONS.land.msOpp + 'ms',
    'the far bubble gets the shorter one', String(opp.style['--gga-dur']));
  fx.reset();

  // emote: the wiggle is synced to the dwell and clamped to it.
  fire('emote', 'you', { emote: '😏', ms: 900 });
  ok(you.style['--gga-dur'] === '900ms', 'an emote wiggles for exactly its dwell', String(you.style['--gga-dur']));
  fx.reset();
  fire('emote', 'you', { emote: '😏', ms: 99000 });
  ok(you.style['--gga-dur'] === EMOTE_MAX_MS + 'ms',
    'a silly dwell is clamped to EMOTE_MAX_MS — nothing wiggles for a minute', String(you.style['--gga-dur']));
  fx.reset();
  fire('emote', 'you', { emote: '😏' });
  ok(you.style['--gga-dur'] === EMOTE_MAX_MS + 'ms', 'and a dwell-less emote falls back to the cap');
  fx.reset();

  // cue: BOTH bubbles lean in, and the element rides along for CSS to read.
  fire('cue', 'you', { element: 'Flashes' });
  ok(hasClass(you, fxClass('cue')) && hasClass(opp, fxClass('cue')),
    'a cue leans BOTH bubbles in — the announcer is warning the room', classesOf(you) + ' | ' + classesOf(opp));
  ok(you.getAttribute('data-gg-cue') === 'Flashes',
    'and meta.element is readable off the bubble while it holds its breath', String(you.getAttribute('data-gg-cue')));
  fx.reset();
  ok(you.getAttribute('data-gg-cue') === null, 'the cue attribute goes with the reaction', String(you.getAttribute('data-gg-cue')));

  // fire: shooter is smug NOW, target is alarmed a beat later.
  fire('fire', 'you');
  ok(hasClass(you, fxClass('fire')), 'firing a payload bounces the shooter');
  ok(!hasClass(opp, fxClass('alarm')), 'and the target has not noticed yet');
  await sleep(FIRE_ECHO_MS + 60);
  ok(hasClass(opp, fxClass('alarm')), 'the other bubble jiggles ~' + FIRE_ECHO_MS + 'ms later — E owns both bubbles',
    classesOf(opp));
  ok(REACTIONS.alarm.ms <= 260, 'and the jiggle is short', String(REACTIONS.alarm.ms));
  fx.reset();
  await sleep(REACTIONS.alarm.ms + 200);
  fx.reset();

  // mercy: the safety valve. Deflate on one side, a BOW on the other.
  fire('mercy', 'opp');
  ok(hasClass(opp, fxClass('mercy')), 'the tapping bubble deflates', classesOf(opp));
  ok(hasClass(opp, TERMINAL_CLASS.mercy), 'and latches the drained state');
  ok(hasClass(you, fxClass('bow')), 'and the other bubble BOWS', classesOf(you));
  ok(!hasClass(you, fxClass('win')) && !hasClass(you, TERMINAL_CLASS.win),
    'it does NOT get a win beat — mercy is a safety valve and is never punished');
  ok(REACTIONS.bow.prio < REACTIONS.mercy.prio,
    'the bow cannot outrank the tap-out it answers', REACTIONS.bow.prio + ' < ' + REACTIONS.mercy.prio);
  fx.reset();
  await sleep(REACTIONS.mercy.ms + 200);
  fx.reset();

  // draw: both shrug, nobody droops.
  fire('draw', 'you');
  ok(hasClass(you, fxClass('draw')) && hasClass(opp, fxClass('draw')), 'a draw shrugs both bubbles');
  ok(hasClass(you, TERMINAL_CLASS.draw) && hasClass(opp, TERMINAL_CLASS.draw), 'and latches on both');
  fx.reset();

  // win/lose stay on the side D named — inventing the other half would double
  // up the instant D emits both.
  fire('win', 'you');
  ok(hasClass(you, fxClass('win')) && hasClass(you, TERMINAL_CLASS.win), 'a win is proud');
  ok(!hasClass(opp, fxClass('lose')), 'and does not invent a loss for the other bubble');
  fx.reset();
  fire('lose', 'you');
  ok(hasClass(you, fxClass('lose')) && hasClass(you, TERMINAL_CLASS.lose), 'a loss droops and greys');
  fx.reset();

  // Junk in, nothing out.
  let junkThrew = null;
  try {
    document.dispatchEvent(new CustomEvent('gg-ava', { detail: { kind: 'explode', side: 'you' } }));
    document.dispatchEvent(new CustomEvent('gg-ava', { detail: null }));
    document.dispatchEvent(new CustomEvent('gg-ava', {}));
    fire('land', 'nonsense');
  } catch (e) { junkThrew = e; }
  ok(!junkThrew, 'an unknown kind / a missing detail / a nonsense side are all ignored, never thrown on',
    junkThrew && junkThrew.stack);
  ok(hasClass(you, fxClass('land')), 'a nonsense side falls back to "you" rather than dropping the beat');

  fx.detach();
  root.remove();
}

// ------------------------------------------------------ 3. decoration + self-clean

{
  const root = dom.makeNode('div');
  document.body.appendChild(root);
  const you = makeAva('you');
  const opp = makeAva('opp');
  root.appendChild(you);
  root.appendChild(opp);
  const fx = createAvatarFx();
  fx.attach(root);

  fire('land', 'you');
  const deco = you.querySelector('.' + DECO_CLASS);
  ok(!!deco, 'a land builds its impact decoration INSIDE the bubble', classesOf(you));
  ok(deco && deco.parentNode === you, 'and nowhere else — no new node outside the .gg-ava subtree');
  ok(you.querySelectorAll('.gg-avafx-ring').length === 1, 'one impact ring');
  ok(you.querySelectorAll('.gg-avafx-star').length === 3, 'three stars to orbit it',
    String(you.querySelectorAll('.gg-avafx-star').length));
  ok(deco && deco.getAttribute('aria-hidden') === 'true', 'the decoration is hidden from assistive tech');

  // THE TRAP: animation events BUBBLE. The ring finishing must not clear the
  // 620ms flinch on the img underneath it.
  const ring = you.querySelector('.gg-avafx-ring');
  you.dispatchEvent({ type: 'animationend', target: ring });
  ok(hasClass(you, fxClass('land')),
    'a decoration child finishing does NOT clear the parent reaction (animationend bubbles)', classesOf(you));
  you.dispatchEvent({ type: 'animationend', target: you });
  ok(hasClass(you, fxClass('land')), 'and neither does one claiming to come from the wrapper itself');

  // …and the one element that DOES own the slot clears it.
  const img = you.querySelector('.gg-ava-img');
  you.dispatchEvent({ type: 'animationend', target: img });
  ok(!hasClass(you, fxClass('land')), 'animationend from the img/tile clears the reaction', classesOf(you));
  ok(!hasClass(you, BUSY_CLASS), 'and hands the idle loop back');
  ok(you.querySelectorAll('.' + DECO_CLASS).length === 0, 'and takes the decoration with it');
  ok(you.style['--gga-dur'] === undefined, 'and the duration variable', String(you.style['--gga-dur']));
  ok(fx.stateOf(you).kind === null, 'the slot is free again');

  // THE BACKSTOP: nothing ever answers, and the reaction still ends.
  fire('land', 'opp');
  ok(hasClass(opp, fxClass('land')), 'a land on the far bubble is up');
  await sleep(REACTIONS.land.msOpp + 260);
  ok(!hasClass(opp, fxClass('land')),
    'and the timeout backstop clears it with no animationend at all — a re-parented node must not strand a class',
    classesOf(opp));
  ok(opp.querySelectorAll('.' + DECO_CLASS).length === 0, 'decoration cleaned up by the backstop too');

  // No stranded classes anywhere after the storm.
  fire('drop', 'you');
  ok(you.querySelectorAll('.gg-avafx-ring--sparkle').length === 1, 'a drop rings in violet/gold');
  fire('win', 'you');
  ok(you.querySelectorAll('.gg-avafx-ring--gold').length === 1, 'a win rings in gold');
  ok(you.querySelectorAll('.gg-avafx-bit').length === 6, 'with a confetti tick',
    String(you.querySelectorAll('.gg-avafx-bit').length));
  ok(you.querySelectorAll('.' + DECO_CLASS).length === 1,
    'and exactly ONE decoration container survives the overlap — the drop took its own with it',
    String(you.querySelectorAll('.' + DECO_CLASS).length));
  await sleep(REACTIONS.win.ms + 260);
  const stranded = Array.from(you._classes).filter((c) => c.startsWith('gg-avafx--') && c !== TERMINAL_CLASS.win);
  ok(stranded.length === 0, 'nothing transient is left on the bubble', stranded.join(' '));
  ok(hasClass(you, TERMINAL_CLASS.win), 'only the latched read-out stays: you won, and the recap plate says so');

  fx.detach();
  root.remove();
}

// ------------------------------------------------------------- 4. pop throttle

{
  const root = dom.makeNode('div');
  document.body.appendChild(root);
  const you = makeAva('you');
  root.appendChild(you);
  const fx = createAvatarFx();
  fx.attach(root);

  ok(POP_MIN_MS >= 200, 'POP_MIN_MS caps the bop at <=' + Math.floor(1000 / POP_MIN_MS) + '/s', String(POP_MIN_MS));
  ok(1000 / POP_MIN_MS <= 4, 'which is the <=4/s the design asks for', String(1000 / POP_MIN_MS));
  ok(REACTIONS.pop.ms <= 200, 'and the bop itself is <=200ms', String(REACTIONS.pop.ms));
  ok(!REACTIONS.pop.deco, 'with no decoration at all — this beat fires a lot');

  fire('pop', 'you');
  ok(hasClass(you, fxClass('pop')), 'the first pop bops');
  // Clear the slot the way a real animationend would, then pop again inside the
  // window: the throttle, not the slot, has to be what refuses it.
  you.dispatchEvent({ type: 'animationend', target: you.querySelector('.gg-ava-img') });
  ok(!hasClass(you, fxClass('pop')), 'it cleans up');
  fire('pop', 'you');
  ok(!hasClass(you, fxClass('pop')), 'a second pop inside ' + POP_MIN_MS + 'ms is DROPPED, not queued', classesOf(you));
  for (let i = 0; i < 20; i++) fire('pop', 'you');
  ok(!hasClass(you, fxClass('pop')), 'and so is a burst of twenty — a backlog of micro-bops is noise');
  await sleep(POP_MIN_MS + 60);
  fire('pop', 'you');
  ok(hasClass(you, fxClass('pop')), 'once the window passes it bops again', classesOf(you));

  fx.detach();
  root.remove();
}

// ---------------------------------------------------------------- 5. priority

{
  const root = dom.makeNode('div');
  document.body.appendChild(root);
  const you = makeAva('you');
  root.appendChild(you);
  const fx = createAvatarFx();
  fx.attach(root);

  ok(REACTIONS.win.prio >= Math.max(...Object.values(REACTIONS).map((r) => r.prio)),
    'win outranks every other beat in the catalog', String(REACTIONS.win.prio));
  ok(REACTIONS.pop.prio === Math.min(...Object.values(REACTIONS).map((r) => r.prio)),
    'and pop is bottom of the pile');

  fire('pop', 'you');
  fire('land', 'you');
  ok(hasClass(you, fxClass('land')) && !hasClass(you, fxClass('pop')),
    'a land interrupts a pop mid-bop', classesOf(you));

  fire('win', 'you');
  ok(hasClass(you, fxClass('win')), 'a win overrides a land');
  ok(!hasClass(you, fxClass('land')), 'and the land class goes with it — one slot, one reaction', classesOf(you));
  ok(hasClass(you, TERMINAL_CLASS.win), 'and the win latches');

  fire('land', 'you');
  ok(!hasClass(you, fxClass('land')), 'a land AFTER the win is refused — a latched recap is not interruptible',
    classesOf(you));
  fire('pop', 'you');
  fire('mercy', 'you');
  ok(!hasClass(you, fxClass('pop')) && !hasClass(you, fxClass('mercy')),
    'and so is everything else under it');
  ok(hasClass(you, fxClass('win')), 'the win is still the one on screen');

  fx.reset();
  ok(!hasClass(you, TERMINAL_CLASS.win) && !hasClass(you, fxClass('win')), 'reset() releases the latch');
  fire('land', 'you');
  ok(hasClass(you, fxClass('land')), 'and the bubble reacts again — a rematch on the same nodes is not stuck');

  fx.detach();
  root.remove();
}

// ------------------------------------------------------- 6. the observer

{
  const root = dom.makeNode('div');
  document.body.appendChild(root);
  const fx = createAvatarFx();
  fx.attach(root);
  ok(fx.count === 0, 'attached to an empty root');

  // D paints the VS splash / the HUD minis / the recap plates as three separate
  // sets of nodes; E has to find each of them without being told.
  const late = makeAva('opp');
  root.appendChild(late);
  await sleep(30);
  ok(fx.count === 1, 'a .gg-ava added later is adopted by the observer', String(fx.count));
  ok(hasClass(late, ON_CLASS) && hasClass(late, IDLE_CLASS), 'and armed like the rest');
  fire('land', 'opp');
  ok(hasClass(late, fxClass('land')), 'and it reacts');

  // Nested: a whole plate arrives with the bubble inside it.
  const plate = dom.makeNode('div');
  const nested = makeAva('you');
  plate.appendChild(nested);
  root.appendChild(plate);
  await sleep(30);
  ok(fx.count === 2, 'a bubble nested inside a newly-painted plate is found too', String(fx.count));

  plate.remove();
  await sleep(30);
  ok(fx.count === 1, 'and forgotten when the plate leaves', String(fx.count));
  let goneThrew = null;
  try { fire('win', 'you'); } catch (e) { goneThrew = e; }
  ok(!goneThrew, 'firing at a side whose bubble is gone is a no-op', goneThrew && goneThrew.stack);

  fx.detach();
  root.remove();
}

// -------------------------------------------------- 7. reduced motion collapse

{
  const root = dom.makeNode('div');
  document.body.appendChild(root);
  const you = makeAva('you');
  const opp = makeAva('opp');
  root.appendChild(you);
  root.appendChild(opp);

  globalThis.__calm = true;
  const fx = createAvatarFx();
  fx.attach(root);
  ok(!hasClass(you, IDLE_CLASS) && !hasClass(opp, IDLE_CLASS),
    'reduced motion: the idle bob is never even armed', classesOf(you));
  ok(hasClass(you, ON_CLASS), 'but the bubble is still adopted — the state read-outs still have to work');

  fire('land', 'you');
  ok(you.querySelectorAll('.' + DECO_CLASS).length === 0,
    'a land builds NO decoration — no ring, no stars', String(you.querySelectorAll('.' + DECO_CLASS).length));
  await sleep(30);
  ok(!hasClass(you, fxClass('land')),
    'and the class is gone inside a frame: the whole reaction is a <=1-frame state change', classesOf(you));
  ok(!hasClass(you, BUSY_CLASS), 'with nothing left parked');

  fire('win', 'you');
  await sleep(30);
  ok(!hasClass(you, fxClass('win')), 'the win beat collapses the same way');
  ok(hasClass(you, TERMINAL_CLASS.win),
    'but the LATCHED read-out stays — a colour is a state, not motion, and a calm player still needs to know',
    classesOf(you));

  fire('mercy', 'opp');
  await sleep(30);
  const strandedCalm = [];
  for (const node of [you, opp]) {
    for (const c of Array.from(node._classes)) {
      if (c.startsWith('gg-avafx--') && Object.values(TERMINAL_CLASS).indexOf(c) < 0) strandedCalm.push(c);
    }
  }
  ok(strandedCalm.length === 0, 'and nothing transient is ever left behind in calm mode', strandedCalm.join(' '));

  fx.detach();
  root.remove();
  globalThis.__calm = false;
}

// ------------------------------------------------------------------ 8. detach

{
  const root = dom.makeNode('div');
  document.body.appendChild(root);
  const you = makeAva('you');
  const opp = makeAva('opp');
  root.appendChild(you);
  root.appendChild(opp);
  const fx = createAvatarFx();
  fx.attach(root);

  fire('win', 'you');
  fire('land', 'opp');
  fire('fire', 'you');            // leaves a 200ms echo timer in flight
  ok(you.querySelectorAll('.' + DECO_CLASS).length > 0, 'mid-storm, with decoration up');

  fx.detach();
  ok(fx.attached === false && fx.count === 0, 'detach() empties the registry');
  ok(dom.observers.size === 0, 'and disconnects the MutationObserver', String(dom.observers.size));
  const after = document._listeners.get('gg-ava');
  ok(!after || after.size === 0, 'and drops the document listener', String(after && after.size));
  for (const node of [you, opp]) {
    const left = Array.from(node._classes).filter((c) => c.startsWith('gg-avafx'));
    ok(left.length === 0, 'and takes every class it added back off (' + (node === you ? 'you' : 'opp') + ')', left.join(' '));
    ok(node.querySelectorAll('.' + DECO_CLASS).length === 0, 'and every node it made');
  }

  // The in-flight timers are dead: nothing re-classes a detached bubble later.
  await sleep(FIRE_ECHO_MS + 260);
  const late = Array.from(opp._classes).filter((c) => c.startsWith('gg-avafx'));
  ok(late.length === 0, 'and the fire echo never lands after detach — every timer is tracked', late.join(' '));

  // A node added after detach is nobody's business.
  const orphan = makeAva('you');
  root.appendChild(orphan);
  await sleep(30);
  ok(!hasClass(orphan, ON_CLASS), 'a bubble painted after detach is not adopted', classesOf(orphan));

  // Re-attach works: the page can tear the duel down and open another one.
  fx.attach(root);
  ok(fx.count === 3, 're-attach picks the whole board back up', String(fx.count));
  fire('draw', 'you');
  ok(hasClass(orphan, fxClass('draw')), 'and the bubbles react again');
  fx.detach();
  root.remove();
}

// ------------------------------------------------------------------- 9. CSS

{
  const fs = await import('node:fs/promises');
  const url = await import('node:url');
  const read = async (p) => fs.readFile(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8');
  const css = await read('../ui/avatarfx.css');
  const html = await read('../index.html');

  // It is actually on the page, and in an order that does not disturb the pin
  // selftest-report keeps on screens.css < fx.css.
  ok(html.includes('ui/avatarfx.css'), 'index.html links ui/avatarfx.css');
  ok(html.indexOf('ui/screens.css') < html.indexOf('exec/fx.css'),
    'and the screens.css < fx.css order selftest-report pins is untouched');

  // Every structural claim below is made against the DECLARATIONS, not the
  // banner comments — a file that only talks about not adding a z-index is not
  // the same as a file that does not add one.
  const code = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const calmAt = code.indexOf('@media (prefers-reduced-motion: reduce)');
  const warm = calmAt > 0 ? code.slice(0, calmAt) : code;

  const ruleOf = (sel) => {
    const i = code.indexOf(sel + ' {');
    if (i < 0) return null;
    return code.slice(i, code.indexOf('}', i) + 1);
  };

  // ---- THE HEAT GOVERNOR. The loop is decoration and parks with the rest.
  const idle = ruleOf('.gg-ava.gg-avafx-idle');
  ok(!!idle, 'the idle loop has a rule', String(idle));
  ok(!!idle && /animation:\s*ggAvaBob[^;]*infinite/.test(idle), 'and it is the looping one', String(idle));
  ok(!!idle && /animation-play-state:\s*var\(--gg-deco-play/.test(idle),
    'and it rides --gg-deco-play, so html[data-gg-fx="hot"] parks it with every other decoration', String(idle));
  const bob = /@keyframes\s+ggAvaBob\s*\{([\s\S]*?)\n\}/.exec(code);
  ok(!!bob, 'ggAvaBob exists');
  const px = (bob ? bob[1] : '').match(/-?\d+(\.\d+)?px/g) || [];
  ok(px.length > 0 && px.every((v) => Math.abs(parseFloat(v)) <= 2),
    'and it moves the bubble by at most 2px — a bubble that never feels dead, not one that dances', px.join(' '));
  ok(/\.gg-ava\.gg-avafx-idle\[data-side="opp"\]\s*\{[^}]*animation-delay/.test(code),
    'the far bubble is delayed out of phase, so a pair breathes ALTERNATELY');

  // ---- THE ONE-SLOT BUDGET. The loop is on the wrapper, the one-shots are on
  //      the child, and no rule ever declares `animation` on both.
  const oneShotKinds = ['land', 'fire', 'alarm', 'drop', 'pop', 'emote', 'mercy', 'bow', 'win', 'lose', 'draw', 'cue'];
  for (const k of oneShotKinds) {
    const sel = '.gg-avafx--' + k + ' .gg-ava-img,\n.gg-avafx--' + k + ' .gg-ava-tile';
    ok(code.includes(sel), 'the "' + k + '" one-shot is declared on the CHILD (img and tile both)');
  }
  // The wrapper's own animation is declared exactly twice outside the calm
  // block: the loop, and the `none` that parks it while a reaction plays.
  const wrapperAnims = (warm.match(/\.gg-ava\.gg-avafx-(idle|busy)[^{]*\{[^}]*animation:/g) || []);
  ok(wrapperAnims.length === 2,
    'the wrapper declares `animation` exactly twice — the loop and the busy override', String(wrapperAnims.length));
  ok(/\.gg-ava\.gg-avafx-busy\s*\{[^}]*animation:\s*none/.test(warm),
    'and a live reaction parks the loop (the `.gg-vwin.is-grabbed .gg-vwin-drift` law) — a land interrupts idle');
  ok(!/\.gg-avafx--\w+\s*\{[^}]*animation:/.test(warm),
    'no reaction class ever declares `animation` on the WRAPPER — that slot belongs to the loop');
  // One-shots must NOT park: a frozen corpse is worse than a finished reaction.
  const shotRules = warm.match(/\.gg-avafx--\w+ \.gg-ava-(img|tile)[^{]*\{[^}]*\}/g) || [];
  ok(shotRules.length === oneShotKinds.length,
    'every catalog beat has exactly one child rule', shotRules.length + '/' + oneShotKinds.length);
  ok(shotRules.every((r) => !/animation-play-state/.test(r)),
    'and no one-shot rides --gg-deco-play — a parked one-shot is a frozen corpse (the .gg-vwin--out precedent)');
  ok(shotRules.every((r) => /animation:[^;]*\bvar\(--gga-dur/.test(r)),
    'each one takes its duration from --gga-dur, so the JS backstop and the keyframe can never drift apart');

  // ---- transform/opacity/filter ONLY. No layout thrash, ever.
  const kf = code.match(/@keyframes[\s\S]*?\n\}/g) || [];
  ok(kf.length >= 15, 'the catalog is actually written out in keyframes', String(kf.length));
  const banned = /(^|[;{\s])(width|height|top|left|right|bottom|margin|padding|inset)\s*:/;
  const offenders = kf.filter((b) => banned.test(b.replace(/@keyframes[^{]*\{/, '')));
  ok(offenders.length === 0,
    'and not one of them animates a layout property — transform/opacity/filter only',
    offenders.map((o) => (/@keyframes\s+(\w+)/.exec(o) || [])[1]).join(' '));

  // ---- THE Z-STACK CONTRACT. No new layers, no MERCY, no pointer grabs.
  ok(!/z-index/.test(code), 'nothing here adds a z-index — the reactions animate in place');
  ok(!/gg-mercy|--gg-mercy/.test(code), 'and MERCY is not named anywhere: z60 stays untouchable');
  ok(!/position:\s*fixed/.test(code), 'nothing escapes the bubble to a fixed box');
  const pe = code.match(/pointer-events:\s*[a-z]+/g) || [];
  ok(pe.length === 1 && pe[0] === 'pointer-events: none',
    'the only pointer-events line is the decoration opting OUT — nothing this file made can eat a click',
    pe.join(' '));
  ok(/\.gg-avafx-deco\s*\{[\s\S]*?position:\s*absolute/.test(code),
    'the decoration is absolutely positioned inside the bubble');
  ok(/\.gg-avafx-on\s*\{\s*position:\s*relative;?\s*\}/.test(code),
    'which is what the one layout declaration on the wrapper is for (no z-index, so no extra stacking context)');
  // …and it is ONE class of specificity, alone in its rule. This file loads
  // after ui/screens.css, so a tie would go to E — and D owns where a bubble
  // sits. Anything D writes with two selectors has to be able to win.
  const posRules = (code.match(/[^}]*position:\s*(relative|absolute|fixed|sticky)[^}]*\}/g) || [])
    .filter((r) => /\.gg-ava\b/.test(r));
  ok(posRules.length === 0,
    'and no rule in this file ever positions .gg-ava itself with more weight than that', posRules.join(' | '));

  // ---- REDUCED MOTION.
  ok(calmAt > 0, 'ui/avatarfx.css carries a prefers-reduced-motion block');
  const calm = code.slice(calmAt);
  ok(/\.gg-ava\.gg-avafx-idle[\s\S]{0,200}animation:\s*none\s*!important/.test(calm),
    'which stops the idle bob');
  ok(/\.gg-ava-img,[\s\S]{0,120}\.gg-ava-tile,[\s\S]{0,200}animation:\s*none\s*!important/.test(calm),
    'and every one-shot on the child');
  ok(/\.gg-avafx-deco\s*\*?[\s\S]{0,120}animation:\s*none\s*!important/.test(calm),
    'and the decoration with it');
  ok(/\.gg-avafx-deco\s*\{[^}]*display:\s*none/.test(calm),
    'and hides the decoration outright, belt-and-braces behind the JS that never builds it');
  for (const t of Object.values(TERMINAL_CLASS)) {
    ok(!new RegExp('\\.' + t + '[^{]*\\{[^}]*display:\\s*none').test(calm),
      'but the latched read-out .' + t + ' is never hidden — who won is information, not motion');
  }
}

await sleep(60);
console.log(`\nselftest-avafx: ${n - failures}/${n} checks passed`);
if (failures > 0) {
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
process.exit(0);
