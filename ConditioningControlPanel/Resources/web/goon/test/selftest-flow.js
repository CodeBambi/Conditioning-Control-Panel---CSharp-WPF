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

// ============================== 5. the paint-aware heartbeat (the 0804 freeze)
//
// THE INCIDENT. 2026-08-04, ~8 minutes into a session: the page froze VISUALLY
// while its JavaScript kept running. The app process was healthy, panic handling
// was clean, resource telemetry was flat, GoonHostService's heartbeat watchdog
// NEVER FIRED because the beats kept arriving, and WebView2 wrote no crash dump
// because nothing crashed — the GPU process hung. The owner had to double-panic
// out of a duel that, as far as every liveness check in the product was
// concerned, was going fine.
//
// THE HOLE IT SAT IN. A heartbeat proves the SCRIPT is alive. Nothing proved
// PIXELS were moving, and those are different questions the moment a compositor
// stalls. So the beat now carries a frame counter, and the host recovers on
// "beats arriving, frames frozen, page says it is visible".
//
// WHAT THIS BLOCK CAN AND CANNOT DO. It pins the SHAPE on both sides of the wire
// — the counter is rAF-driven, the beat is not, the visibility field exists, the
// C# reads the same two field names, the threshold and the greppable log line
// are there, and the recovery is the SAME Recover() the dead-heartbeat case
// uses. It cannot run a DispatcherTimer, so "the watchdog actually relaunches
// after 10s" is not provable from node and is not claimed here.
{
  const boot = read('boot.js');
  const code = stripComments(boot);
  const beat = (() => {
    const b = code.slice(code.indexOf('function startHeartbeat('));
    return b.slice(0, b.indexOf('\n}\n') + 1);
  })();

  ok(/function startHeartbeat\(/.test(code), 'boot.js still owns the liveness beat');
  ok(/requestAnimationFrame/.test(beat), 'and it drives a requestAnimationFrame loop');
  // The counter loop must do NOTHING but count. A probe that does work is a
  // probe that can be the thing that breaks.
  const rafLoop = /\(function frame\(\)\s*\{([^}]*)\}\)\(\)/.exec(beat);
  ok(!!rafLoop, 'the paint counter is a bare rAF loop', String(rafLoop && rafLoop[1]));
  ok(!!rafLoop && /frames\+\+/.test(rafLoop[1]) && /requestAnimationFrame\(frame\)/.test(rafLoop[1]),
    'which increments a frame count and re-arms itself, and does nothing else',
    String(rafLoop && rafLoop[1].replace(/\s+/g, ' ').trim()));

  // THE BEAT AND THE COUNTER ARE ON SEPARATE CLOCKS. This is the whole fix: a
  // beat that rides rAF cannot report a stalled rAF, because it stalls with it.
  ok(/setInterval\(\s*beat\s*,/.test(beat),
    'the beat itself is on a TIMER, not on frames — a heartbeat riding rAF cannot report that rAF stopped');
  ok(!/requestAnimationFrame\([^)]*\bbeat\b/.test(beat),
    'and nothing schedules the beat off a frame callback');

  ok(/type:\s*'heartbeat'/.test(beat), 'the beat is still a `heartbeat` message');
  ok(/msg\.paint\s*=\s*frames/.test(beat), 'stamped with the frame count');
  ok(/vis:\s*visibility\(\)/.test(beat) && /document\.visibilityState/.test(beat),
    'and with document.visibilityState — a hidden window legitimately stops painting and must never trip recovery');
  ok(/if\s*\(painting\)\s*msg\.paint\s*=\s*frames/.test(beat),
    'a host with no rAF OMITS the counter rather than sending a frozen zero: "no frame counter" is a different fact from "no frames"');

  // ---- the other half of the wire, in C#
  const hostPath = path.join(ROOT, '..', '..', '..', 'Services', 'GoonGame', 'GoonHostService.cs');
  ok(fs.existsSync(hostPath), 'the host service is where the page thinks it is', hostPath);
  const cs = fs.existsSync(hostPath) ? fs.readFileSync(hostPath, 'utf8').replace(/\r\n/g, '\n') : '';

  ok(/o\["paint"\]/.test(cs) && /o\["vis"\]/.test(cs),
    'the host reads the SAME two field names the page writes — a rename on one side is a watchdog that never fires again');
  ok(/PaintStallSeconds\s*=\s*10/.test(cs),
    'the paint-stall threshold is 10s: past any legitimate hitch, far short of how long the owner sat in front of a dead picture');
  ok(/paint stall detected/.test(cs),
    'and it logs a distinct, greppable line — this is how the diagnosis gets confirmed the next time it happens');
  ok(/Recover\("paint-stall"\)/.test(cs),
    'the new trigger runs the SAME Recover() the dead-heartbeat case does, not a second recovery path');
  ok(/Recover\("heartbeat-silent"\)/.test(cs), 'and the original trigger is untouched');
  ok(/_paintStallHandled/.test(cs),
    'guarded to fire ONCE, like the >20s rule — the tick is 5s and Recover is dispatched async, so an unguarded rule queues several relaunches');
  ok(/!string\.Equals\(vis, "visible"[\s\S]{0,220}_lastPaintMoveUtc = now;/.test(cs),
    'a page reporting it is NOT visible resets the stall clock — alt-tabbing out of a fullscreen duel is not a freeze');
  ok(/if \(paint == null\) return;/.test(cs),
    'and a beat with no counter at all switches the rule off rather than tripping it');
}

/* ============================================================================
 * 6. P2P MEDIA TRANSFER, END TO END: prepick -> transfer -> fire -> render.
 *
 * Two REAL match engines over a REAL loopback bulk pair, two real prepick queues
 * and the real protocol. The only fakes are the two ends the page cannot have
 * under node: the local artifact source (the compression cache) and the received
 * store's disk (URL.createObjectURL does not exist here). Everything between them
 * — consent, caps, the channel, the offer gate, the chunking, the tryFirePayload
 * wrapper and the receiver-side resolution — is production code.
 *
 * The second half is the case that matters MORE: the same setup on a transport
 * that cannot carry bulk degrades to today's behaviour with no special case
 * anywhere. That is the promise the whole design is built on.
 * ========================================================================= */
{
  const { createMediaQueue } = await import('../net/mediaQueue.js');
  const { createGoonMediaPool } = await import('../exec/media.js');
  const { local: localCaps } = await import('../core/caps.js');
  const { GoonPayloadKind } = await import('../core/contracts.js');

  const tick = (ms = 25) => new Promise((r) => setTimeout(r, ms));
  const SHA = (c) => String(c).repeat(64).slice(0, 64);
  const SHA_VID = SHA('a');
  const SHA_IMG = SHA('b');

  /** The compression side, as the queue sees it: two verbs and some bytes. */
  function fakeArtifacts(list) {
    const bufs = new Map();
    for (const a of list) {
      const b = new Uint8Array(a.bytes);
      for (let i = 0; i < b.length; i++) b[i] = (i * 31 + a.sha.charCodeAt(0)) & 0xff;
      bufs.set(a.sha, b);
    }
    return {
      bufs,
      listSendable: () => list.map((a) => Object.assign({}, a)),
      open(sha) {
        const b = bufs.get(sha);
        if (!b) return null;
        const meta = list.find((x) => x.sha === sha);
        return {
          bytes: b.length,
          mime: meta.mime,
          read: (offset, len) => b.buffer.slice(offset, Math.min(offset + len, b.length)),
        };
      },
    };
  }

  /** The ReceivedStore contract, in memory, with no host and no Blob. */
  function fakeStore() {
    const held = new Map();
    const parts = new Map();
    return {
      held,
      has: (sha) => held.has(sha),
      partialLength: (sha) => (parts.has(sha) ? parts.get(sha).len : 0),
      begin(sha, mime, bytes) {
        if (!parts.has(sha)) parts.set(sha, { buf: new Uint8Array(bytes), len: 0, mime });
        return true;
      },
      write(sha, offset, ab) {
        const p = parts.get(sha);
        if (!p || offset !== p.len) return false;
        const u = new Uint8Array(ab);
        p.buf.set(u, offset);
        p.len += u.length;
        return true;
      },
      async commit(sha) {
        const p = parts.get(sha);
        if (!p) return { ok: false, error: 'io-failed' };
        parts.delete(sha);
        held.set(sha, { bytes: p.buf, mime: p.mime });
        return { ok: true, url: 'https://ccp.cache/recv/' + sha + '.bin', bytes: p.len };
      },
      abort(sha) { parts.delete(sha); },
    };
  }

  /** Stand two matches up in a consented Draft, with the media-transfer opt-in on both sides. */
  async function consentedPair(pair) {
    const caps = localCaps({ transfer: true });
    const a = new GoonMatchService(pair.host, true, { logger: quiet, caps, displayName: 'A', tag: 'GG:A' });
    const b = new GoonMatchService(pair.guest, false, { logger: quiet, caps, displayName: 'B', tag: 'GG:B' });
    a.adoptLobby();
    b.adoptLobby();
    await pair.connect();
    await tick(60);
    a.proposeConsent(600, 0, 30000);
    await tick(40);
    a.setMediaTransfer(true);
    b.setMediaTransfer(true);
    await tick(40);
    a.confirmConsent();
    b.confirmConsent();
    await tick(60);
    return { a, b };
  }

  // ------------------------------------------------ 6a. the whole thing, over bulk
  {
    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
    }));

    const { a, b } = await consentedPair(pair);
    ok(pair.host.supportsBulk === true && pair.guest.supportsBulk === true,
      'the loopback pair opted IN to bulk (tests only — Practice mode never does)');
    ok(a.phase === GoonMatchPhase.Draft && b.phase === GoonMatchPhase.Draft,
      'both sides consented and entered the draft', `${a.phase}/${b.phase}`);
    ok(a.mediaTransferAgreed === true && b.mediaTransferAgreed === true,
      'and both declared the media-transfer opt-in on the consent frame');

    const artifacts = fakeArtifacts([
      { sha: SHA_VID, bytes: 90000, mime: 'video/mp4', kind: 'video', exempt: false },
      { sha: SHA_IMG, bytes: 40000, mime: 'image/png', kind: 'image', exempt: false },
    ]);
    const storeA = fakeStore();
    const storeB = fakeStore();
    const poolB = createGoonMediaPool();
    poolB.setManifest({
      images: [{ name: 'own-i', url: 'https://ccp.assets/own/i' }],
      videos: [{ name: 'own-v', url: 'https://ccp.assets/own/v' }],
    });

    const qA = createMediaQueue({
      artifacts, store: storeA, logger: quiet, canSend: () => true, idlePollMs: 40,
    });
    // B sends nothing (no artifacts, no premium) and receives everything — which is
    // the asymmetry the product is built on: only SENDING is gated.
    const qB = createMediaQueue({
      artifacts: { listSendable: () => [], open: () => null },
      store: storeB, logger: quiet, canSend: () => false, idlePollMs: 40,
    });
    const received = [];
    qB.onReceived((e) => {
      received.push(e);
      poolB.addReceived({ sha: e.sha, kind: e.kind, mime: e.mime, url: e.url, bytes: e.bytes });
    });

    qA.attach(a, pair.host);
    qB.attach(b, pair.guest);
    ok(qB.enabled() === false, 'B cannot send: canSend() is the premium gate and it said no');

    // The queue primes at Draft; the hello round trip and the idle poll are what make
    // it start, so give it a moment rather than assuming one turn.
    for (let i = 0; i < 80 && storeB.held.size < 1; i++) await tick(25);

    ok(qA.enabled() === true, "A's gate is open: caps + both consents + peer support + supportsBulk + hello");
    ok(storeB.held.size >= 1, 'at least one artifact crossed the wire and committed on B',
      String(storeB.held.size));
    ok(received.length >= 1 && /^[0-9a-f]{64}$/.test(received[0].sha),
      'and the queue announced it so boot can register it with the media pool');

    const landedSha = received[0].sha;
    const sent = artifacts.bufs.get(landedSha);
    const got = storeB.held.get(landedSha).bytes;
    ok(got.length === sent.length && got[0] === sent[0] && got[got.length - 1] === sent[sent.length - 1],
      'the bytes that landed are the bytes that were sent, start and end',
      `${got.length} vs ${sent.length}`);

    // --- the payload. Live phase + charges are forced: this section is about the
    // wrapper and the tag, not about the charge economy (selftest-core owns that).
    a._phase = GoonMatchPhase.Live;
    b._phase = GoonMatchPhase.Live;
    a._scoring._charges = 9;
    // B only knows what A's state frames told it, and we skipped Live entirely — so
    // hand it the same number by hand or the inbound gate refuses on the economy.
    b._opponentChargesKnown = 9;

    const kind = received[0].kind === 'video' ? GoonPayloadKind.Video : GoonPayloadKind.FlashBurst;
    const tags = qA.tagsFor(kind);
    ok(tags.length >= 1 && tags[0] === 'xfer:' + landedSha,
      'tagsFor hands back the landed artifact as an xfer: tag', tags.join(','));

    let inbound = null;
    b.onPayloadAccepted((e) => { inbound = e && e.payload; });
    const res = a.tryFirePayload({ kind, durationMs: 5000, intensity: 0.5 });
    ok(res && res.ok === true, 'the payload fired', res && res.error);
    await tick(80);

    ok(!!inbound, 'and it arrived on the other side');
    ok(!!inbound && Array.isArray(inbound.tags) && inbound.tags[0] === 'xfer:' + landedSha,
      "carrying the xfer tag the queue's tryFirePayload wrapper added — with zero changes to "
      + 'ui/arsenal.js or ui/soloDriver.js', JSON.stringify(inbound && inbound.tags));

    // --- and the receiver renders THEIR file.
    const drawn = poolB.drawFor(received[0].kind, inbound);
    ok(drawn && drawn.provenance === 'peer' && drawn.url.startsWith('https://ccp.cache/recv/'),
      "the receiver resolves the tag to the SENDER's artifact, flagged as theirs",
      drawn ? `${drawn.provenance} ${drawn.url}` : 'null');

    // --- consumed once, never twice.
    ok(qA.tagsFor(kind).indexOf('xfer:' + landedSha) < 0,
      'the artifact was marked consumed by the wrapper — one landing, one drop');

    qA.detach();
    qB.detach();
    ok(typeof a.tryFirePayload === 'function', 'detach left a callable tryFirePayload behind');
    a.dispose(); b.dispose(); pair.dispose();
  }

  // ------------------------------- 6b. the relay case: dormant, with no special case
  {
    // A transport that cannot carry bulk is EXACTLY the relay situation: connected,
    // healthy, and physically unable to move media. Nothing branches on it anywhere.
    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, logger: quiet,
    }));

    const { a, b } = await consentedPair(pair);
    ok(pair.host.supportsBulk === false,
      'a default loopback pair reports supportsBulk false — the relay-shaped path');
    ok(pair.host.isConnected === true,
      'while `isConnected` is TRUE, which is precisely why it is never the bulk gate (trap #1)');

    const q = createMediaQueue({
      artifacts: fakeArtifacts([{ sha: SHA_VID, bytes: 4000, mime: 'video/mp4', kind: 'video', exempt: false }]),
      store: fakeStore(), logger: quiet, canSend: () => true, idlePollMs: 40,
    });
    q.attach(a, pair.host);
    await tick(200);

    ok(q.enabled() === false, 'the queue is dormant even though everything else agreed');
    ok(q.tagsFor(GoonPayloadKind.Video).length === 0, 'so tagsFor returns []');

    a._phase = GoonMatchPhase.Live;
    b._phase = GoonMatchPhase.Live;
    a._scoring._charges = 9;
    b._opponentChargesKnown = 9;
    let inbound = null;
    b.onPayloadAccepted((e) => { inbound = e && e.payload; });
    const res = a.tryFirePayload({ kind: GoonPayloadKind.Video, durationMs: 5000, intensity: 0.5 });
    await tick(80);
    ok(res && res.ok === true, 'the payload still fires — a drop can never block on a transfer');
    ok(!!inbound && (inbound.tags === null || inbound.tags === undefined),
      'and it goes out UNTAGGED, byte-identical to the wire before this feature existed',
      JSON.stringify(inbound && inbound.tags));

    q.detach();
    a.dispose(); b.dispose(); pair.dispose();
  }
}

/* ===========================================================================
 * 6. DISCORD SHARING — the ORDERING half (docs/GOON_DISCORD_CONTRACT.md §7).
 *
 * The behaviour of the module is proved against a DOM in selftest-hud.js §12.
 * What can only be proved HERE is where the calls sit in boot's lifecycle,
 * because every risk in this block is a placement risk:
 *
 *   · a VS SPLASH that outlives the countdown. It is z55 decoration over the
 *     desk; one left behind at Recap is a full-bleed node on a screen whose
 *     entire history is "the player could not click anything";
 *   · a RICH PRESENCE that strands. Every road out of a match has to post
 *     `off`, or somebody's Discord says "In a duel" an hour after they quit;
 *   · a MERCY BEAT wired to one of its two callers. The button and the Escape
 *     ladder both concede and neither is privileged;
 *   · a PEER-CARD FETCH that gates something. It may never be awaited.
 * ======================================================================== */
{
  const boot = read('boot.js');
  const code = stripComments(boot);
  const lobby = stripComments(read('ui/screens/lobby.js'));
  const recap = stripComments(read('ui/screens/recap.js'));
  const hud = stripComments(read('ui/hud.js'));
  const discord = stripComments(read('ui/discord.js'));
  const avatar = stripComments(read('ui/avatar.js'));

  const fn = (src, name, next) => {
    const start = src.indexOf('function ' + name + '(');
    if (start < 0) return '';
    const end = next ? src.indexOf('function ' + next + '(', start) : -1;
    return end > start ? src.slice(start, end) : src.slice(start);
  };

  // ---- ONE OWNER PER VERB. bridge.on throws on a duplicate, so the two the
  //      module claims must appear nowhere else on the page.
  ok(/subscribeBridge\('discord',/.test(discord) && /subscribeBridge\('peer-card',/.test(discord),
    'ui/discord.js is the one thing that registers the discord + peer-card handlers');
  ok(!/bridge\.on\(['"]discord['"]|bridge\.on\(['"]peer-card['"]/.test(code),
    'and boot.js registers NEITHER — a second on() would throw at wiring time');
  ok((code.match(/createDiscord\(/g) || []).length === 1,
    'built exactly once, in buildApp — the lobby it renders into mounts many times a session');
  ok(/getRoom: \(\) => session\.room/.test(code),
    'the room is handed over as a THUNK — a relay fallback replaces session.room mid-match');

  // ---- THE VS SPLASH: one raise, and a drop on every road out.
  ok(/raiseVsSplash\(\)/.test(fn(code, 'onPhase')),
    'boot raises the VS splash from the phase router');
  const phases = fn(code, 'onPhase', 'ensureSession');
  const atCountdown = phases.slice(phases.indexOf('GoonMatchPhase.Countdown'), phases.indexOf('GoonMatchPhase.Live'));
  ok(/raiseVsSplash\(\)/.test(atCountdown), 'at Countdown, alongside the screen — never before it');
  const atLive = phases.slice(phases.indexOf('case GoonMatchPhase.Live'), phases.indexOf('GoonMatchPhase.SuddenDeath'));
  ok(/dropVsSplash\(\)/.test(atLive),
    'and the LIVE arm drops it — that is the hard deadline, whatever it was doing');
  ok(/dropVsSplash\(\)/.test(fn(code, 'clearForRecap', 'forceRecap')),
    'clearForRecap drops it too, on the same pass that sweeps #gg-stage');
  ok(/dropVsSplash\(\)/.test(fn(code, 'teardownEverything', 'actions')) || /dropVsSplash\(\)/.test(code.slice(code.indexOf('async function teardownEverything'), code.indexOf('const actions'))),
    'and so does teardownEverything — four callers, one idempotent remove');
  ok(/pointer-events: none/.test(read('ui/screens.css').slice(read('ui/screens.css').indexOf('.gg-vs-splash {'), read('ui/screens.css').indexOf('.gg-vs-splash.is-in'))),
    'the splash cannot take a click while it is up');
  {
    const css = read('ui/screens.css');
    const block = css.slice(css.indexOf('.gg-vs-splash {'), css.indexOf('.gg-vs-splash.is-in'));
    const z = (block.match(/z-index:\s*(\d+)/) || [])[1];
    const mercyZ = (read('goon.css').match(/--gg-mercy-z:\s*(\d+)/) || [])[1];
    ok(Number(z) === 55, 'it sits at z55', String(z));
    ok(Number(z) < Number(mercyZ),
      'which is UNDER mercy — nothing in this feature may ever be over the concede', `${z} < ${mercyZ}`);
  }
  ok(/if \(reduced\) return null;/.test(avatar),
    'and reduced motion skips it entirely rather than parking a card on screen for 1.6s');

  // ---- RICH PRESENCE: every road out posts `off`.
  const teardown = code.slice(code.indexOf('async function teardownEverything'), code.indexOf('const actions'));
  ok(/setRpState\?\.\('off'\)/.test(teardown),
    "teardownEverything posts rp-state off — leave, quit, a failed connect and a re-host all run through it");
  ok(/setRpState\?\.\('off'\)/.test(fn(code, 'finishExit')),
    'and so does finishExit, because the page may be gone before the async teardown lands');
  ok(/setRpState\?\.\('live'\)/.test(atLive), 'Live arms the live state');
  ok(/setRpState\?\.\('recap'\)/.test(phases.slice(phases.indexOf('case GoonMatchPhase.Recap'))),
    'and the Recap arm the recap one');
  ok(/setRpState\?\.\('lobby'\)/.test(lobby),
    'the lobby screen posts its own on mount — it is the screen, not a phase');
  ok(/if \(v === rp\) return false;/.test(discord),
    'and a repeat is dropped inside the module, so posting it liberally costs one frame');

  // ---- MERCY: one seam, both callers.
  const attach = code.slice(code.indexOf('function attachMatch('), code.indexOf('function stashRoom('));
  ok(/match\.declareMercy = /.test(attach) && /emitAva\('mercy', 'you'\)/.test(attach),
    'the mercy beat wraps the match INSTANCE — the button and the Escape ladder both go through it');
  ok(/origMercy\(\.\.\.args\)/.test(attach),
    'and passes the engine call straight through — nothing about a concede depends on a decoration');
  ok(/emitAva\('mercy', 'opp'\)/.test(attach),
    'their mercy is read off the result, the only place it is knowable');
  ok((code.match(/emitAva\('mercy'/g) || []).length === 2,
    'exactly two mercy emit sites, one per side', String((code.match(/emitAva\('mercy'/g) || []).length));

  // ---- THE OTHER BEATS, at the seams that already existed.
  ok(/emitAva\('land', 'you'/.test(hud) && /emitAva\('land', 'opp'/.test(hud),
    'ui/hud.js lands the beat on both sides — inbound payload, and the receipt for ours');
  ok(/emitAva\('fire', 'you'/.test(hud), 'the arsenal onFired seam fires one');
  ok(/emitAva\('pop', 'you'\)/.test(hud) && /emitAva\('drop', 'you'/.test(hud),
    'and a bubble pop raises both the pop and (rarely) the drop');
  ok(/emitAva\('cue', 'you'/.test(hud) && /emitAva\('emote', 'you'/.test(hud),
    'the announcer cue and the outbound emote ride the shared log sink');
  ok(/mountAnnouncer\(\{ host: root, match, audio, onLog \}\)/.test(hud)
    && /mountEmotes\(\{ host: root, match, audio, onLog \}\)/.test(hud),
    'which is why NEITHER mount call grew a wrapper argument');
  ok(/emitAva\(beat, 'you'\)/.test(recap) && /'lose' : 'win', 'opp'/.test(recap),
    'the recap emits win AND lose — avatarFx does not mirror those two');
  ok(/if \(beat === 'draw'\) \{ emitAva\('draw', 'you'\); return; \}/.test(recap),
    'but a draw exactly ONCE, because avatarFx applies it to both bubbles itself');
  ok(/if \(stung \|\| !tone\) return;/.test(recap),
    'and all of it is on the sting latch — paint() runs again when the countersignature lands');

  // ---- THE FETCH NEVER GATES.
  ok(!/await [^\n]*notePeerCardVer|await [^\n]*peer-card/.test(code + hud + lobby),
    'nothing on the page ever AWAITS a peer card — it may not gate lobby, countdown or Live');
  ok(/if \(!showOpponentAvatars\(\)\) return \{ requested: false, reason: 'pref-off' \};/.test(discord),
    'and the viewer pref is checked BEFORE the request, so OFF suppresses the fetch and not just the pixels');
  ok(discord.indexOf("reason: 'pref-off'") < discord.indexOf("counters.cardReqs++"),
    'the pref gate is genuinely ahead of the post, not merely present');

  // ---- THE FIRST-DUEL CONFIRM sits where it cannot be swept unanswered.
  ok(/askSharePrompt\(\{ discord, sheets \}\)/.test(lobby),
    'the one-time share confirm hangs off the lobby, not the countdown');
  ok(/if \(answer === null\) return;/.test(lobby),
    'and a swept sheet (closeChrome closes every sheet at Countdown) signs NOTHING');
  ok(/dismissible: false/.test(discord),
    'it is not scrim-dismissible either — a click-through that meant "yes" is the worst reading of a consent sheet');
  ok(/writePrefs\(\{ shareAvatar: false, shareDm: false \}\)/.test(discord),
    'declining turns the sharing off rather than remembering that it was refused');

  // ---- NO IDENTIFIER, ANYWHERE ON THE PAGE.
  const pageSrc = discord + avatar + hud + lobby + recap + code;
  ok(!/dm_id|snowflake|discordapp\.com\/users|cdn\.discordapp/.test(pageSrc),
    'no page module names a snowflake, a dm_id or a Discord URL — the host owns all three');
  ok(/which: w \}/.test(discord) || /type: 'discord-open-dm', which: w/.test(discord),
    'discord-open-dm carries WHICH and nothing else');
}

/* ============================================================================
 * 7. THE DEBUG OVERLAY (ui/debugOverlay.js) — the console a phone does not have.
 *
 * Added for the 2026-08-04 phone report: a guest was evicted from the lobby back
 * to the title and there was nowhere ON THE DEVICE for the reason to be written.
 * Hosted, warn/error tunnel to the C# log; standalone they went to a console
 * nobody can open.
 *
 * What must hold, or it is worse than not having it:
 *   · it never renders unless asked (and hosted, never implicitly);
 *   · push() cannot throw — it sits inside logger.warn/error;
 *   · the ring is bounded, so a warning storm cannot eat the page;
 *   · it is ONE node on <body> with pointer-events on itself: no gameplay.
 * ==========================================================================*/
{
  const overlay = await import('../ui/debugOverlay.js');
  const { debugRequested, debugInSearch, createDebugOverlay, captureGlobalErrors, MAX_LINES } = overlay;

  // ---- the gate
  ok(debugInSearch('?debug=1') && debugInSearch('?a=b&debug=true') && debugInSearch('?debug'),
    '?debug=1 / =true / bare all count');
  ok(!debugInSearch('?debug=0') && !debugInSearch('?solo=0') && !debugInSearch(''),
    '?debug=0 and an unrelated query do not');
  ok(debugRequested({ search: '?debug=1', hosted: true }) === true,
    'hosted CAN be asked explicitly on the querystring');
  ok(debugRequested({ search: '', prefs: { debug: true }, hosted: true }) === false,
    'but a hosted session never inherits a stored flag — a WebView2 duel keeps its chrome');
  ok(debugRequested({ search: '', prefs: { debug: true }, hosted: false }) === true,
    'standalone remembers it, so a reload (or a home-screen pin) keeps the strip');
  ok(debugRequested({ search: '', prefs: { debug: false }, hosted: false }) === false,
    'and ?debug=0 writes the flag back off');
  ok(debugRequested({}) === false, 'nothing asked for it -> nothing');

  // ---- the strip itself, over the stub DOM
  const panel = createDebugOverlay({ doc: dom.doc, max: 4, now: () => 0 });
  ok(!!panel.node && panel.node.id === 'gg-debug', 'it mounts exactly one node');
  ok(dom.doc.body.childNodes.indexOf(panel.node) >= 0, 'appended to <body>, not into a screen');
  const css = String(panel.node.getAttribute('style') || '');
  ok(/position:fixed/.test(css) && /z-index:2147483000/.test(css),
    'fixed and above every layer the page owns (#gg-modal is z70)', css.slice(0, 60));
  ok(/pointer-events:auto/.test(css), 'it takes taps on itself only — nothing else is touched');

  panel.push('warn', 'one');
  panel.push('error', new Error('two'));
  ok(panel.lines().length === 2, 'lines land', String(panel.lines().length));
  ok(/warn one/.test(panel.lines()[0]), 'with the level in the line', panel.lines()[0]);
  ok(/error two/.test(panel.lines()[1]), 'an Error is flattened to its message', panel.lines()[1]);
  for (let i = 0; i < 20; i++) panel.push('warn', 'flood ' + i);
  ok(panel.lines().length === 4, 'the ring is bounded — a storm cannot eat the page', String(panel.lines().length));
  ok(/flood 19/.test(panel.lines()[3]), 'and it keeps the NEWEST lines', panel.lines()[3]);

  // push() is inside logger.warn: it may never be the thing that throws.
  let threw = false;
  try {
    const circular = {}; circular.self = circular;
    panel.push('warn', circular);
    panel.push(null, undefined);
  } catch (_e) { threw = true; }
  ok(!threw, 'push() survives a circular object and a null level');

  // tap-to-collapse: one gesture, the whole strip is the target
  ok(panel.collapsed() === false, 'it starts open — an empty badge explains nothing');
  panel.node.dispatchEvent({ type: 'click' });
  ok(panel.collapsed() === true, 'tapping anywhere on it collapses it');
  panel.node.dispatchEvent({ type: 'click' });
  ok(panel.collapsed() === false, 'and back');

  // the two seams a logger never sees
  const fakeWin = (() => {
    const map = new Map();
    return {
      addEventListener(t, f) { if (!map.has(t)) map.set(t, new Set()); map.get(t).add(f); },
      removeEventListener(t, f) { const s = map.get(t); if (s) s.delete(f); },
      fire(t, e) { for (const f of Array.from(map.get(t) || [])) f(e); },
      count(t) { return (map.get(t) || new Set()).size; },
    };
  })();
  const off = captureGlobalErrors(panel, fakeWin);
  fakeWin.fire('error', { message: 'boom', filename: 'https://x/goon/ui/lobby.js', lineno: 12 });
  fakeWin.fire('unhandledrejection', { reason: new Error('nope') });
  ok(/boom @ lobby\.js:12/.test(panel.lines().join('\n')), 'window.onerror is captured with a location',
    panel.lines().join(' | '));
  ok(/promise: nope/.test(panel.lines().join('\n')), 'so is an unhandled rejection');
  off();
  ok(fakeWin.count('error') === 0 && fakeWin.count('unhandledrejection') === 0, 'and it unsubscribes cleanly');

  panel.dispose();
  ok(dom.doc.body.childNodes.indexOf(panel.node) < 0, 'dispose removes the node');
  panel.push('warn', 'after dispose');
  ok(panel.lines().length === 4, 'and a disposed strip stops collecting', String(panel.lines().length));

  // no usable DOM (the node import sweep) -> a handle whose every method is a no-op
  const headless = createDebugOverlay({ doc: {} });
  ok(headless.node === null && headless.lines().length === 0, 'no document -> an inert handle, never a throw');
  headless.push('warn', 'x'); headless.toggle(); headless.dispose();

  ok(MAX_LINES === 50, 'the default ring is the last 50 lines', String(MAX_LINES));

  // ---- and that boot.js actually wires it, behind the flag and nowhere else
  const boot = read('boot.js');
  ok(/teeDebug\('warn', m\)/.test(boot) && /teeDebug\('error', m\)/.test(boot),
    'boot tees warn AND error into the strip (info/debug stay out — the engine is chatty)');
  ok(!/teeDebug\('info'/.test(boot), 'and info is NOT teed');
  ok(/function wantsDebugHint\(\)/.test(boot), 'the module is not even imported unless something asked for it');
  ok(/if \(bridge\.isHosted\) return false;/.test(boot),
    'hosted never picks the flag up from stored prefs');
  ok(/import\('\.\/ui\/debugOverlay\.js'\)/.test(boot),
    'it is loaded dynamically, like the sibling waves — a missing file is not a white page');
  ok(boot.indexOf('initDebugOverlay();') < boot.indexOf("window.addEventListener('keydown'"),
    'and it is armed FIRST, so it is listening before the boot it explains can fail');
  const bridgeSrc = read('bridge.js');
  ok(/keep\.debug = /.test(bridgeSrc), 'bridge persists the flag next to server/token/uid/name');
  ok(/export function storedPrefs\(\)/.test(bridgeSrc), 'and exposes the stored blob for the pre-init decision');
}

console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
process.exit(failures === 0 ? 0 : 1);
