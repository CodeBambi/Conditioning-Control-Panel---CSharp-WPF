// Self-contained sanity pass over the MATCH FLOW — how a run ends, and whether
// the player can get out of the end card.
//
//   node Resources/web/goon/test/selftest-flow.js
//
// It exists because of one owner report: "we cannot exit the ending card
// (clicking anywhere on the screen where I get you're broke or the winning
// message, clicking any button does nothing, esc does nothing too)".
//
// Two root causes, both pinned here:
//   1. #gg-stage (z20, full-bleed) sits ABOVE the screen stack (z10) and is only
//      click-through while it is :empty. core/match.js _endMatch() stops every
//      sustained element but knows nothing about an in-flight PAYLOAD render, so
//      a lock card / video the opponent sent kept running on the stage after the
//      match ended and turned the whole recap into a picture.
//   2. the Escape ladder had no Recap rung: Escape on the end card did nothing.
//
// What is asserted:
//   1. the layering that makes an orphan lethal is still what we think it is
//      (index.html order + goon.css z-index + the :empty opt-out);
//   2. boot.js clears the stage, the fx layers and the z70 chrome when the phase
//      turns Recap, and Escape on the recap leaves;
//   3. sudden death is DETACHED (owner's call 2026-08-03) — no runner is wired,
//      and the engine settles the Live clock on score instead of hanging;
//   4. the recap screen mounts, paints both verdicts ("You broke." / "You held."),
//      its Back button actually calls actions.leave, and it refuses to sit under
//      a dirty stage.
//
// Browser-only checks (real hit-testing, real clicks) live in the headless
// harness — this file is everything that can be proved under plain node.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { GoonMatchService } from '../core/match.js';
import { GoonEndReason, GoonMatchPhase } from '../core/contracts.js';
import { createLoopbackPair, loopbackOptions } from '../net/loopbackTransport.js';
import { S } from '../ui/strings.js';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.join(HERE, '..');
// LF-normalized: the worktree is CRLF (core.autocrlf), and every source pin
// below is written against \n.
const read = (rel) => fs.readFileSync(path.join(ROOT, rel), 'utf8').replace(/\r\n/g, '\n');

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, debug() {}, log() {} };

/** Strip // and /* *\/ comments so "is this code or a note?" is answerable. */
function stripComments(src) {
  return String(src)
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:])\/\/[^\n]*/g, '$1');
}

// ============================================================ 1. the layering
{
  const html = read('index.html');
  for (const id of ['gg-screens', 'gg-stage', 'gg-fx', 'gg-hud', 'gg-mercy', 'gg-drawer', 'gg-modal', 'scr-recap']) {
    ok(html.includes('id="' + id + '"'), 'index.html still ships #' + id);
  }
  ok(html.indexOf('id="gg-stage"') > html.indexOf('id="gg-screens"'),
    '#gg-stage is painted after (over) #gg-screens — an orphan on it shields the recap');

  // `:empty` matches only when there are NO child nodes at all, whitespace text
  // included. A prettified <div id="gg-stage">\n</div> would silently make the
  // stage opaque to clicks on every screen, forever.
  ok(/<div id="gg-stage"><\/div>/.test(html), '#gg-stage ships with no child nodes, not even whitespace');

  const goonCss = read('goon.css');
  const zOf = (sel) => {
    const m = goonCss.match(new RegExp(sel.replace(/[#]/g, '\\#') + '\\s*\\{[^}]*z-index:\\s*(\\d+)'));
    return m ? Number(m[1]) : NaN;
  };
  const zScreens = zOf('#gg-screens');
  const zStage = zOf('#gg-stage');
  ok(zScreens === 10, '#gg-screens is z10', String(zScreens));
  ok(zStage === 20, '#gg-stage is z20', String(zStage));
  ok(zStage > zScreens, 'the stage really is over the screens (this is why the fix is a teardown, not a z-index)');

  const screensCss = read('ui/screens.css');
  ok(/#gg-stage:empty\s*\{[^}]*pointer-events:\s*none/.test(screensCss),
    'ui/screens.css keeps the #gg-stage:empty click-through opt-out');
}

// ================================================== 2. boot.js recap teardown
{
  const boot = read('boot.js');
  const code = stripComments(boot);

  ok(/function clearForRecap\(/.test(code), 'boot.js defines clearForRecap()');
  const body = code.slice(code.indexOf('function clearForRecap('));
  const clearBody = body.slice(0, body.indexOf('\n}\n') + 1);
  ok(/executor\?\.stopAll\?\.\(\)/.test(clearBody), 'clearForRecap cancels in-flight payload renders (executor.stopAll)');
  ok(/layers\.stopAll\(\)/.test(clearBody), 'clearForRecap empties the layers (layers.stopAll)');
  ok(/closeChrome\(\)/.test(clearBody), 'clearForRecap closes the z70 chrome (closeChrome)');
  ok(/gg-stage/.test(clearBody) && /replaceChildren/.test(clearBody),
    'clearForRecap asserts #gg-stage is empty afterwards');

  // The shared closer itself: ONE helper, two callers (recap + the run start).
  ok(/function closeChrome\(/.test(code), 'boot.js defines the shared closeChrome()');
  const chromeBody = (() => {
    const b = code.slice(code.indexOf('function closeChrome('));
    return b.slice(0, b.indexOf('\n}\n') + 1);
  })();
  ok(/sheets\.close\(/.test(chromeBody), 'closeChrome closes an open modal sheet (#gg-modal, z70)');
  ok(/options\.close\(\)/.test(chromeBody), 'closeChrome closes the options drawer (#gg-drawer, z70)');
  ok(/sheets\s*&&\s*sheets\.isOpen/.test(chromeBody) && /options\s*&&\s*options\.isOpen/.test(chromeBody),
    'and it only closes what is actually open (no toggling a closed drawer back on)');

  // …and that it is actually WIRED, at the phase change and on every forced pass.
  const recapCase = code.slice(code.indexOf('case GoonMatchPhase.Recap:'));
  const recapArm = recapCase.slice(0, recapCase.indexOf('break;'));
  ok(/clearForRecap\(/.test(recapArm), 'the Recap phase arm calls clearForRecap');
  ok(recapArm.indexOf('clearForRecap(') < recapArm.indexOf("router.show('recap')"),
    'and it runs BEFORE the recap screen goes up');
  const force = code.slice(code.indexOf('function forceRecap('));
  ok(/clearForRecap\(/.test(force.slice(0, force.indexOf('\n}\n'))), 'forceRecap clears the way too');

  /* THE OTHER END OF THE SAME BUG — a sheet left open when the RUN starts.
   * The "how it works" explainer is one tap from the lobby, and a modal sheet is
   * a full-height z70 scrim: leave it up and it covers the bottom strip where
   * exec/bubbles.js spawns the field, so every pop lands on the scrim and the
   * drop economy silently never produces an item for the whole match. It also
   * makes Escape ambiguous at the exact moment Escape must mean MERCY. So the
   * chrome comes down on Countdown, and again on Live for the paths that never
   * show a countdown (rebuild / late join). */
  const armOf = (name) => {
    const at = code.indexOf('case GoonMatchPhase.' + name + ':');
    return at < 0 ? '' : code.slice(at, at + code.slice(at).indexOf('break;'));
  };
  const countdownArm = armOf('Countdown');
  const liveArm = armOf('Live');
  ok(/closeChrome\(\)/.test(countdownArm), 'the Countdown phase arm closes the z70 chrome');
  ok(countdownArm.indexOf('closeChrome()') < countdownArm.indexOf("router.show('countdown')"),
    'and it does it BEFORE the countdown screen goes up');
  ok(/closeChrome\(\)/.test(liveArm), 'the Live phase arm closes it too (belt and braces)');
  ok(liveArm.indexOf('closeChrome()') < liveArm.indexOf('mountMercyNow()'),
    'before mercy mounts, so nothing is ever over the button');

  /* ...and the Escape ladder is UNTOUCHED. In Live, Escape is mercy, full stop:
   * no sheet-peeling rung may creep in front of it. */
  const escLadder = code.slice(code.indexOf('function onKeyDown('), code.indexOf('function holdExit('));
  ok(!/closeChrome\(/.test(escLadder), 'the Escape ladder does NOT call closeChrome (Live Escape stays mercy)');
  ok(escLadder.indexOf('declareMercy()') < escLadder.indexOf('sheets.close(null)'),
    'mercy is still the FIRST rung of the ladder, ahead of every overlay closer');

  // The Escape ladder must have a Recap rung. Escape may never be a dead key.
  const esc = code.slice(code.indexOf('function onKeyDown('), code.indexOf('function holdExit('));
  ok(/GoonMatchPhase\.Recap/.test(esc), 'the Escape ladder knows about the Recap phase');
  ok(/actions\.leave\('escape-recap'\)/.test(esc), 'Escape on the recap leaves (never a key that does nothing)');
  ok(esc.indexOf('sheets.close(null)') < esc.indexOf("actions.leave('escape-recap')"),
    'a sheet/drawer over the recap is peeled off first — one layer per press');
}

// ============================================ 3. sudden death is DETACHED
{
  const boot = stripComments(read('boot.js'));
  const solo = stripComments(read('ui/soloDriver.js'));
  ok(!/suddenDeathRunner\s*=\s*new\s+GoonSuddenDeathRunner/.test(boot),
    'boot.js attaches NO sudden-death runner (detached 2026-08-03, pending the rounds rework)');
  ok(!/suddenDeathRunner\s*=\s*new\s+GoonSuddenDeathRunner/.test(solo),
    'ui/soloDriver.js attaches no runner either — practice mirrors a real run');

  // The modules stay in the tree, wired and importable, so this comes back as a
  // one-line change rather than an archaeology project.
  for (const rel of ['core/suddenDeath.js', 'core/rounds/model.js', 'core/rounds/quickDraw.js',
    'core/rounds/staringContest.js', 'core/rounds/reactionDuel.js', 'core/rounds/bubbleRace.js',
    'ui/sd/index.js']) {
    ok(fs.existsSync(path.join(ROOT, rel)), rel + ' is still in the tree (SD is detached, not deleted)');
  }
  ok(/GoonSuddenDeathRunner/.test(read('boot.js')), 'boot.js still imports the runner, ready to re-attach');
}

// ====================== 3b. the engine settles the clock with no runner
{
  const pair = createLoopbackPair(loopbackOptions({ latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, logger: quiet }));
  const match = new GoonMatchService(pair.host, true, { logger: quiet, displayName: 'You', tag: 'GG:test' });

  ok(match.suddenDeathRunner === null, 'a freshly built match has no runner attached');

  const phases = [];
  match.onPhaseChanged((p) => phases.push(p));

  // Stand the match up in Live and expire the clock the way _liveTick does.
  match._phase = GoonMatchPhase.Live;
  match._liveDurationMs = 1000;
  match._scoring._scoreExact = 120;
  match._opponent.score = 90;
  match._enterSuddenDeath();

  ok(match.phase === GoonMatchPhase.Recap, 'no runner -> the clock settles instead of hanging in SuddenDeath',
    String(match.phase));
  ok(phases[phases.length - 1] === GoonMatchPhase.Recap, 'and Recap is the phase it lands on');
  ok(!!match.result, 'a result was written');
  ok(match.result.endReason === GoonEndReason.SuddenDeathLoss, 'higher score wins the settled clock',
    String(match.result && match.result.endReason));
  ok(match.result.localWon === true, 'the local player (120 vs 90) is the winner');

  // …and a tie is a draw, not a coin flip.
  const pair2 = createLoopbackPair(loopbackOptions({ latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, logger: quiet }));
  const drawn = new GoonMatchService(pair2.host, true, { logger: quiet, tag: 'GG:test2' });
  drawn._phase = GoonMatchPhase.Live;
  drawn._scoring._scoreExact = 77;
  drawn._opponent.score = 77;
  drawn._enterSuddenDeath();
  ok(drawn.result && drawn.result.endReason === GoonEndReason.Draw, 'equal scores settle as a Draw',
    String(drawn.result && drawn.result.endReason));

  match.dispose(); drawn.dispose(); pair.dispose(); pair2.dispose();
}

// ================================================ 4. the recap screen itself
function installDom() {
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
      disabled: false,
      dataset: {},
      style: {},
      _text: '',
      get childElementCount() { return kids.length; },
      get textContent() { return node._text + kids.map((k) => k.textContent || '').join(''); },
      set textContent(v) { kids.length = 0; node._text = String(v); },
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
      removeChild(child) { const i = kids.indexOf(child); if (i >= 0) kids.splice(i, 1); return child; },
      remove() { if (node.parentNode) node.parentNode.removeChild(node); node.parentNode = null; },
      replaceChildren(...c) { kids.length = 0; c.forEach((x) => node.appendChild(x)); },
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
      /** every descendant whose className contains `cls` */
      findAll(cls) {
        const out = [];
        for (const k of kids) {
          if (k.classList && k.classList.contains(cls)) out.push(k);
          if (typeof k.findAll === 'function') out.push(...k.findAll(cls));
        }
        return out;
      },
      findTag(tag) {
        const out = [];
        for (const k of kids) {
          if (k.tagName === String(tag).toUpperCase()) out.push(k);
          if (typeof k.findTag === 'function') out.push(...k.findTag(tag));
        }
        return out;
      },
    };
    return node;
  }

  const byId = new Map();
  const doc = {
    documentElement: makeNode('html'),
    body: makeNode('body'),
    createElement: (t) => makeNode(t),
    createTextNode: (t) => { const nd = makeNode('#text'); nd.textContent = t; return nd; },
    getElementById: (id) => byId.get(id) || null,
    addEventListener() {}, removeEventListener() {},
  };
  for (const id of ['gg-stage', 'scr-recap', 'gg-hud', 'gg-mercy', 'gg-modal']) {
    const nd = makeNode('div');
    nd.id = id;
    byId.set(id, nd);
  }
  globalThis.document = doc;
  return { doc, byId, makeNode };
}

const dom = installDom();
const recap = await import('../ui/screens/recap.js');

// --- the guard itself
{
  const stage = dom.byId.get('gg-stage');
  stage.replaceChildren(dom.makeNode('div'), dom.makeNode('video'));
  let warned = '';
  const found = recap.assertStageClear({ warn: (m) => { warned = String(m); } });
  ok(found === 2, 'assertStageClear reports what it found', String(found));
  ok(stage.childElementCount === 0, 'assertStageClear empties the stage — the click shield is gone');
  ok(/gg-stage/.test(warned), 'and it says so at WARN (a husk means the teardown regressed)', warned);
  ok(recap.assertStageClear(null) === 0, 'a clean stage is a no-op and needs no logger');
}

/** A match stub shaped like the two fields recap.js actually reads. */
function fakeMatch(result) {
  return {
    result,
    opponent: { displayName: 'Kit' },
    scoring: { riskMultiplier: 1.15 },
    onResultFinalized() { return () => {}; },
    onMatchEnded() { return () => {}; },
  };
}
function fakeResult(o) {
  return Object.assign({
    endReason: GoonEndReason.Mercy, localWon: false, disputed: false, agreed: true,
    localScore: 210, remoteScore: 188, survivedMs: 9 * 60 * 1000,
  }, o);
}
function mountRecap(result) {
  const container = dom.makeNode('section');
  const leaves = [];
  const handle = recap.mount(container, {
    logger: quiet,
    audio: null,
    prefs: { get: () => 3, set() {} },
    matchLog: { stats: () => ({ landedOnYou: 2, enduredByYou: 1 }), payloads: () => [], sawPhase: () => false },
    getMatch: () => fakeMatch(result),
    actions: { leave: (why) => leaves.push(why) },
  });
  const buttons = container.findTag('button');
  const back = buttons.find((b) => b.textContent === S.recap.back) || null;
  const verdict = container.findAll('gg-recap-verdict')[0] || null;
  return { container, handle, leaves, buttons, back, verdict };
}

// --- the loss card: the exact one the owner was stuck on
{
  const m = mountRecap(fakeResult({ endReason: GoonEndReason.Mercy, localWon: false }));
  ok(!!m.verdict, 'the loss card renders a verdict');
  ok(m.verdict.textContent === S.recap.broke, 'the mercy-loss card is "' + S.recap.broke + '"', m.verdict.textContent);
  ok(!!m.back, 'the loss card has a Back button');
  ok(m.back.disabled !== true, 'and it is enabled');
  m.back.dispatchEvent({ type: 'click', preventDefault() {} });
  ok(m.leaves.length === 1 && m.leaves[0] === 'recap', 'clicking Back leaves the recap', JSON.stringify(m.leaves));
  m.handle.unmount();
}

// --- the win card has to work exactly as well
{
  const m = mountRecap(fakeResult({ endReason: GoonEndReason.Mercy, localWon: true }));
  ok(m.verdict.textContent === S.recap.held, 'the win card is "' + S.recap.held + '"', m.verdict.textContent);
  m.back.dispatchEvent({ type: 'click', preventDefault() {} });
  ok(m.leaves.length === 1, 'clicking Back leaves the win card too');
  m.handle.unmount();
}

// --- and so do the two the settled clock can now produce
{
  const won = mountRecap(fakeResult({ endReason: GoonEndReason.SuddenDeathLoss, localWon: true }));
  ok(won.verdict.textContent === S.recap.held, 'a settled clock in your favour is "' + S.recap.held + '"');
  won.back.dispatchEvent({ type: 'click', preventDefault() {} });
  ok(won.leaves.length === 1, 'its Back button works');
  won.handle.unmount();

  const drew = mountRecap(fakeResult({ endReason: GoonEndReason.Draw, localWon: false }));
  ok(drew.verdict.textContent === S.recap.draw, 'a tie is "' + S.recap.draw + '"');
  drew.back.dispatchEvent({ type: 'click', preventDefault() {} });
  ok(drew.leaves.length === 1, 'and so does the draw card');
  drew.handle.unmount();
}

// --- Rematch is visible-but-disabled, and must never swallow the exit
{
  const m = mountRecap(fakeResult({}));
  const rematch = m.buttons.find((b) => b.textContent.indexOf(S.recap.rematch) === 0);
  ok(!!rematch && rematch.disabled === true, 'Rematch ships visible and disabled');
  ok(m.buttons.length >= 2, 'the action row has both buttons', String(m.buttons.length));
  m.handle.unmount();
}

// --- mounting under a dirty stage cleans it up rather than rendering a picture
{
  const stage = dom.byId.get('gg-stage');
  stage.replaceChildren(dom.makeNode('div'));
  const m = mountRecap(fakeResult({}));
  ok(stage.childElementCount === 0, 'mounting the recap clears a stage husk (defence in depth)');
  m.back.dispatchEvent({ type: 'click', preventDefault() {} });
  ok(m.leaves.length === 1, 'and the card is live afterwards');
  m.handle.unmount();
}

console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
process.exit(failures === 0 ? 0 : 1);
