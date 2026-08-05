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

/** A match stub shaped like the two fields recap.js actually reads.
 *  `scoring` is deliberately absent since 2026-08-05: the recap used to print
 *  `scoring.riskMultiplier` in its fine print and that readout went with the
 *  rest of the risk indicator, so a stub carrying one would be documenting a
 *  read that no longer happens. If recap.js ever touches scoring again this
 *  stub throws on undefined, which is the failure we want. */
function fakeMatch(result) {
  return {
    result,
    opponent: { displayName: 'Kit' },
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
    // B sends nothing (no artifacts, and canSend forced off) and receives everything.
    // The forced-off gate is a TEST FIXTURE, not a tier: since 2026-08-05 no real
    // seat is refused the capability (see 6a-free). It stays here because the
    // receive path must keep working while the send path is shut, whatever shut it.
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
    ok(qB.enabled() === false, 'B cannot send: canSend() is the capability gate and it said no');

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

  /* ---------------- 6a-free. THE FREE SEAT SENDS (owner call, 2026-08-05) ------
   * Everything above drives `canSend` from a hand-written arrow. This one drives
   * it from the SHAPE A FREE SEAT ACTUALLY HOLDS — bridge.js's standaloneInit
   * caps, minus every paid marker — through the same predicate boot.js installs
   * (`caps.mediaTransfer === true`). If someone re-introduces a tier default in
   * bridge.js, this fails on the object rather than on a source regex.
   *
   * The two verdicts are asserted TOGETHER on the same caps object because the
   * whole decision is that they diverge: sending free, hosting not.
   * -------------------------------------------------------------------------- */
  {
    // A guest that arrived on an invite link: no account, no tier, no host right.
    const freeSeatCaps = {
      platform: 'web', video: true, camera: false, assetCache: false,
      mediaTransfer: true,     // bridge.js standaloneInit — free for every seat
      canHost: false,          // …and the server's tier-2 bar, unmoved
    };
    // boot.js's canSend closure, verbatim in shape.
    const canSend = () => !!(freeSeatCaps && freeSeatCaps.mediaTransfer === true);
    ok(canSend() === true, 'a free seat CAN send: caps.mediaTransfer is true with no account behind it');
    ok(freeSeatCaps.canHost !== true, 'and still cannot host — the perk did not move');

    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
    }));
    const { a, b } = await consentedPair(pair);
    const artifacts = fakeArtifacts([
      { sha: SHA_IMG, bytes: 40000, mime: 'image/png', kind: 'image', exempt: false },
    ]);
    const storeB = fakeStore();
    // The FREE seat is the sender here — the exact inversion of 6a, where the
    // gated side was the one holding nothing.
    const qFree = createMediaQueue({ artifacts, store: fakeStore(), logger: quiet, canSend, idlePollMs: 40 });
    const qPeer = createMediaQueue({
      artifacts: { listSendable: () => [], open: () => null },
      store: storeB, logger: quiet, canSend: () => true, idlePollMs: 40,
    });
    qFree.attach(a, pair.host);
    qPeer.attach(b, pair.guest);

    // The hello is a round trip, so the ladder is only complete a few turns in —
    // same wait 6a uses, and the same reason.
    for (let i = 0; i < 80 && storeB.held.size < 1; i++) await tick(25);
    ok(qFree.enabled() === true,
      'and the whole sendGate ladder passes for it — capability, both consents, peer support, bulk, hello');
    ok(storeB.held.size >= 1, "the free seat's media actually crossed the wire", String(storeB.held.size));

    qFree.detach(); qPeer.detach();
    a.dispose(); b.dispose(); pair.dispose();
  }

  /* -------- 6a-consent. A FREE CAPABILITY IS NOT A FREE PASS --------------------
   * The half of 6a-free that matters most for review: give the free seat the
   * capability and take away the OTHER side's lobby tick, and the lane stays
   * shut — with the queue naming consent, not entitlement, as the reason. The
   * pair is built the 6d way (declarations set at Idle, one side silent) because
   * the setter refuses past Consent and a withdrawal mid-Draft is a no-op.
   * -------------------------------------------------------------------------- */
  {
    const caps = localCaps({ transfer: true });
    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
    }));
    const a = new GoonMatchService(pair.host, true, { logger: quiet, caps, displayName: 'Free', tag: 'GG:AFREE' });
    const b = new GoonMatchService(pair.guest, false, { logger: quiet, caps, displayName: 'Peer', tag: 'GG:BFREE' });
    ok(a.setMediaTransfer(true) === true, 'the free seat ticks its own lobby box');
    // The peer never does.
    a.adoptLobby(); b.adoptLobby();
    await pair.connect();
    await tick(60);
    a.proposeConsent(600, 0, 30000);
    await tick(40);
    a.confirmConsent(); b.confirmConsent();
    await tick(60);

    const q = createMediaQueue({
      artifacts: fakeArtifacts([{ sha: SHA_IMG, bytes: 40000, mime: 'image/png', kind: 'image', exempt: false }]),
      store: fakeStore(), logger: quiet, canSend: () => true, idlePollMs: 40,
    });
    q.attach(a, pair.host);
    await tick(60);
    ok(q.enabled() === false,
      'the capability is granted and the lane is STILL shut — one consent short');
    const why = q.tagsFor(GoonPayloadKind.FlashBurst);
    ok(Array.isArray(why) && why.length === 0,
      'so a media payload fires untagged, exactly as it does for a gated seat');
    ok(a.mediaTransferAgreed === false,
      'and the engine agrees: free to send is not the same as cleared to send');

    q.detach(); a.dispose(); b.dispose(); pair.dispose();
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

  // ------------------- 6c. BOOT'S SEEDING ORDER — the 2026-08-05 field bug
  // boot.js seeds the standing declarations inside attachMatch, and
  // net/session.js raises onCurrentMatchChanged from _beginSession — BEFORE
  // createInvite/join has moved the phase off Idle. Until 2026-08-05 the
  // setters were Lobby/Consent-only and SILENTLY refused that call on both
  // seats of every fresh P2P match: the opt-in never rode a consent frame and
  // every attack fell back to the receiver's local pool, while this suite
  // stayed green because every test seeded AFTER adoptLobby(). This section
  // seeds in boot's order and would have caught it.
  {
    const caps = localCaps({ transfer: true });
    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
    }));
    const a = new GoonMatchService(pair.host, true, { logger: quiet, caps, displayName: 'A', tag: 'GG:A6c' });
    const b = new GoonMatchService(pair.guest, false, { logger: quiet, caps, displayName: 'B', tag: 'GG:B6c' });

    // attachMatch runs NOW — the phase is still Idle on both seats.
    ok(a.phase === GoonMatchPhase.Idle && b.phase === GoonMatchPhase.Idle,
      '6c: both matches are Idle when boot would seed them', `${a.phase}/${b.phase}`);
    ok(a.setMediaTransfer(true) === true, '6c: the Idle-phase media seed is TAKEN, not silently refused');
    ok(b.setMediaTransfer(true) === true, '6c: ...on the guest seat too');
    ok(a.setLocalVoiceNotes(true) === true, '6c: and the voice-note twin takes the Idle seed the same way');

    // ...then createInvite/join flips the phase and the lobby walk happens.
    a.adoptLobby(); b.adoptLobby();
    await pair.connect();
    await tick(60);
    a.proposeConsent(600, 0, 30000);
    await tick(40);
    a.confirmConsent();
    b.confirmConsent();
    await tick(60);

    ok(a.phase === GoonMatchPhase.Draft && b.phase === GoonMatchPhase.Draft,
      '6c: the pair consented and drafted with no extra frames', `${a.phase}/${b.phase}`);
    ok(a.localMediaTransfer === true && a.remoteMediaTransfer === true,
      '6c: the host reads BOTH opt-ins true', `${a.localMediaTransfer}/${a.remoteMediaTransfer}`);
    ok(b.localMediaTransfer === true && b.remoteMediaTransfer === true,
      '6c: and so does the guest', `${b.localMediaTransfer}/${b.remoteMediaTransfer}`);
    ok(a.mediaTransferAgreed === true && b.mediaTransferAgreed === true,
      '6c: mediaTransferAgreed on both seats — the queue\'s consent legs pass');
    ok(b.remoteVoiceNotes === true, '6c: the guest heard the host\'s voice declaration too');

    a.dispose(); b.dispose(); pair.dispose();
  }

  // ---------- 6d. the asymmetric seat: one side never opts in (stale page)
  // The exact field verdict from the 08-05 hunt: the desktop's tracer said
  // "their lobby send toggle is off". A peer that never seeds (an old cached
  // build, or a player who unticked the box) must gate BOTH directions, and
  // the opted-in side's remoteMediaTransfer is the leg that says why.
  {
    const caps = localCaps({ transfer: true });
    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
    }));
    const a = new GoonMatchService(pair.host, true, { logger: quiet, caps, displayName: 'A', tag: 'GG:A6d' });
    const b = new GoonMatchService(pair.guest, false, { logger: quiet, caps, displayName: 'B', tag: 'GG:B6d' });
    ok(a.setMediaTransfer(true) === true, '6d: host opted in at Idle');
    // The guest NEVER calls setMediaTransfer — the stale-page seat.
    a.adoptLobby(); b.adoptLobby();
    await pair.connect();
    await tick(60);
    a.proposeConsent(600, 0, 30000);
    await tick(40);
    a.confirmConsent();
    b.confirmConsent();
    await tick(60);

    ok(a.localMediaTransfer === true && a.remoteMediaTransfer === false,
      '6d: the host reads local ON, remote OFF — mediaQueue names "their lobby send toggle is off"');
    ok(b.localMediaTransfer === false && b.remoteMediaTransfer === true,
      '6d: the guest reads the mirror image');
    ok(a.mediaTransferAgreed === false && b.mediaTransferAgreed === false,
      '6d: nobody transfers until BOTH declare — one stale seat gates both directions');

    a.dispose(); b.dispose(); pair.dispose();
  }

  /* ---------- 6e. THE VIDEO LANE PREFERS FOOTAGE OVER CONVERTED GIF LOOPS
   *
   * The desktop compresses an animated gif into an mp4 so it can travel (the
   * offer gate refuses any kind/mime disagreement), which makes it a perfectly
   * valid VIDEO artifact — and a video attack that plays a two-second loop
   * while a real clip sat in the same larder reads as a bug even though every
   * layer worked. `tagsFor` spends the footage first. It is a PREFERENCE: when
   * the loop is all that has landed it is still sent, because their gif beats
   * the receiver's own library.
   */
  {
    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
    }));
    const { a, b } = await consentedPair(pair);

    const GIF_VID = SHA('c');
    const REAL_VID = SHA('d');
    const PRE1 = SHA('1');
    const PRE2 = SHA('2');
    /* THE TWO TINY CLIPS ARE RULE 4's QUOTA, PAID UP FRONT (2026-08-05). This test needs a
     * `landed` array whose FIRST row is the gif — the arrangement a queue that just took
     * `landed[0]` gets wrong — and the footage preload now guarantees the first two landings are
     * footage, so the arrangement has to be built AFTER the quota rather than instead of it. Two
     * 4 KB clips satisfy it, get drained below, and leave exactly the pair rule 1 was written
     * for. Keeping them makes this test prove BOTH rules at once: the preload owns the opening,
     * and the preference owns everything after it. */
    const artifacts = fakeArtifacts([
      { sha: PRE1, bytes: 4000, mime: 'video/mp4', kind: 'video', exempt: false, codec: 'avc1' },
      { sha: PRE2, bytes: 4000, mime: 'video/mp4', kind: 'video', exempt: false, codec: 'avc1' },
      // The gif is offered before the big clip by the rule-3b score (a fresh `origin` is worth 1),
      // so a queue that just took `landed[0]` would send the loop.
      { sha: GIF_VID, bytes: 8000, mime: 'video/mp4', kind: 'video', exempt: false, origin: 'gif', codec: 'avc1' },
      { sha: REAL_VID, bytes: 40000, mime: 'video/mp4', kind: 'video', exempt: false, codec: 'avc1' },
    ]);
    const storeB = fakeStore();
    const landedOrder = [];
    const qA = createMediaQueue({
      artifacts, store: fakeStore(), logger: quiet, canSend: () => true, idlePollMs: 40,
    });
    const qB = createMediaQueue({
      artifacts: { listSendable: () => [], open: () => null },
      store: storeB, logger: quiet, canSend: () => false, idlePollMs: 40,
    });
    qB.onReceived((e) => landedOrder.push(e.sha));

    /* THE DECK IS SHUFFLED WITH Math.random (deliberately — see the header of
     * net/mediaQueue.js), so without pinning it this test would only catch a
     * regression half the time. `() => 0` makes the Fisher-Yates deterministic
     * and lands the GIF BEFORE THE BIG CLIP, which is precisely the arrangement
     * a queue that just took `landed[0]` would get wrong. */
    const realRandom = Math.random;
    try {
      Math.random = () => 0;
      qA.attach(a, pair.host);
      qB.attach(b, pair.guest);
      for (let i = 0; i < 200 && storeB.held.size < 4; i++) await tick(25);
    } finally {
      Math.random = realRandom;
    }
    ok(storeB.held.size === 4, '6e: all four clips landed', String(storeB.held.size));
    ok(landedOrder[0] === PRE1 && landedOrder[1] === PRE2,
      '6e: the two footage clips landed FIRST — rule 4 owns the opening of the match',
      landedOrder.join(',').slice(0, 40));
    ok(landedOrder[2] === GIF_VID,
      '6e: …and with the quota paid the GIF lands before the big clip, so `landed[0]` is the wrong '
      + 'answer and the test can see it', landedOrder.join(',').slice(0, 40));

    // Drain the preload pair. `tagsFor` spends footage first, so these come off in order and
    // leave the gif sitting at index 0 of `landed` — the shape the preference has to survive.
    for (const pre of [PRE1, PRE2]) {
      const t = qA.tagsFor(GoonPayloadKind.Video);
      ok(t.length === 1 && t[0] === 'xfer:' + pre,
        '6e: the preload clips are spent first, oldest footage first', t.join(','));
      qA.markConsumed(t);
    }

    const first = qA.tagsFor(GoonPayloadKind.Video);
    ok(first.length === 1 && first[0] === 'xfer:' + REAL_VID,
      '6e: the FIRST video tag is the real clip, even though the gif landed first', first.join(','));

    // Spend it, and the loop is what is left — the fallback, not an exclusion.
    qA.markConsumed(first);
    const second = qA.tagsFor(GoonPayloadKind.Video);
    ok(second.length === 1 && second[0] === 'xfer:' + GIF_VID,
      '6e: with the footage spent the gif-origin clip IS sent — a gif of theirs beats our own pool',
      second.join(','));

    qA.detach(); qB.detach();
    a.dispose(); b.dispose(); pair.dispose();
  }

  /* ---------- 6f. THE HEVC HANDSHAKE: never offer what they cannot decode
   *
   * The founding bug: ui/assetsStore.js probeVideoDecodable only ever proved
   * the SENDER could play the clip. Safari decodes its own HEVC, adopts it,
   * transfers it flawlessly, and the peer with no HEVC decoder paints a silent
   * black window for the whole slot. Here the receiver advertises `avc1` only
   * and the sender's eligibility filter drops the HEVC artifact BEFORE it is
   * offered — with a warn, because a silent skip looks exactly like "the
   * transfer doesn't work", which is what three rounds of this were.
   */
  {
    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
    }));
    const { a, b } = await consentedPair(pair);

    const HEVC = SHA('e');
    const H264 = SHA('f');
    const artifacts = fakeArtifacts([
      { sha: HEVC, bytes: 9000, mime: 'video/mp4', kind: 'video', exempt: false, codec: 'hvc1.1.6.L93.B0' },
      { sha: H264, bytes: 9000, mime: 'video/mp4', kind: 'video', exempt: false, codec: 'avc1' },
    ]);
    const warns = [];
    const loud = { info() {}, warn: (m) => warns.push(String(m)), error() {}, log() {} };

    const storeB = fakeStore();
    const qA = createMediaQueue({
      artifacts, store: fakeStore(), logger: loud, canSend: () => true, idlePollMs: 40,
    });
    // THE RECEIVER IS THE ONE WITH THE OPINION: its hello carries the list.
    const qB = createMediaQueue({
      artifacts: { listSendable: () => [], open: () => null },
      store: storeB, logger: quiet, canSend: () => false, idlePollMs: 40,
      acceptsCodecs: ['avc1'],
    });
    qA.attach(a, pair.host);
    qB.attach(b, pair.guest);
    for (let i = 0; i < 120 && storeB.held.size < 1; i++) await tick(25);
    await tick(200);                      // …and give the queue every chance to send the other one

    ok(storeB.held.has(H264), '6f: the H.264 clip transferred normally');
    ok(!storeB.held.has(HEVC),
      '6f: the HEVC clip was NEVER offered — the peer advertised no HEVC decoder');
    ok(qA.tagsFor(GoonPayloadKind.Video).indexOf('xfer:' + HEVC) < 0,
      '6f: …so it can never end up on a payload either');
    ok(warns.some((m) => /hvc1/.test(m) && /decoder/.test(m)),
      '6f: and the skip SAYS SO once — the warn is what reaches the C# log and the ?debug=1 overlay',
      warns.join(' | ').slice(0, 160));
    ok(warns.filter((m) => /does not advertise a hvc1 decoder/.test(m)).length === 1,
      '6f: exactly once per codec per match, on the saidWhy pattern — not once per poll');

    qA.detach(); qB.detach();
    a.dispose(); b.dispose(); pair.dispose();
  }

  /* ---------- 6g. THE SAME PAIR WITH AN OLD PEER: nothing is withheld.
   * A hello with no `accepts_codecs` (every build before 2026-08-05) must be
   * read as "send me anything", or the handshake would silently break the lane
   * for everybody who has not updated.
   */
  {
    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
    }));
    const { a, b } = await consentedPair(pair);

    const HEVC = SHA('e');
    const artifacts = fakeArtifacts([
      { sha: HEVC, bytes: 9000, mime: 'video/mp4', kind: 'video', exempt: false, codec: 'hvc1.1.6.L93.B0' },
    ]);
    const storeB = fakeStore();
    const qA = createMediaQueue({
      artifacts, store: fakeStore(), logger: quiet, canSend: () => true, idlePollMs: 40,
    });
    const qB = createMediaQueue({           // no acceptsCodecs — the old-peer shape
      artifacts: { listSendable: () => [], open: () => null },
      store: storeB, logger: quiet, canSend: () => false, idlePollMs: 40,
    });
    qA.attach(a, pair.host);
    qB.attach(b, pair.guest);
    for (let i = 0; i < 120 && storeB.held.size < 1; i++) await tick(25);
    ok(storeB.held.has(HEVC),
      '6g: a peer that advertises no codec list still receives the HEVC clip — fail open, end to end');

    qA.detach(); qB.detach();
    a.dispose(); b.dispose(); pair.dispose();
  }

  /* ---------- 6h. BOOT WIRES BOTH ENDS. Neither feature can work if the page
   * forgets to read the fields off the artifact source or to probe at all, and
   * both are one line in a 2,300-line file. */
  {
    const boot = read('boot.js');
    ok(/origin\s*=\s*\(it\.origin === 'gif' \|\| it\.kind === 'gif'\)/.test(boot),
      "6h: listSendable derives `origin` from the host's own flag OR the item kind (a host too "
      + 'old to send `origin` still classifies gifs as kind "gif")');
    ok(/out\.push\(\{ sha, bytes, mime, kind: wireKind, exempt, origin, codec \}\)/.test(boot),
      '6h: …and both fields ride the sendable row the queue reads');
    ok(/probeDecodeCodecs\(\)/.test(boot) && /acceptsCodecs: decodeCodecs/.test(boot),
      '6h: boot probes what this device decodes ONCE and hands it to the queue for the hello');
    ok(/origin: e\.origin \|\| ''/.test(boot),
      '6h: and a received artifact keeps its origin all the way into exec/media.js addReceived');
  }

  /* ---------- 6i. TRANSFER VARIETY, THE SENDER'S HALF (2026-08-05, play-test r9)
   *
   * "Seems like I am receiving always the same gif." Every gate passed; the pool
   * was simply too small and too lopsided for the first minute. Three rules, all
   * pinned here — see rule 3 in net/mediaQueue.js's header.
   */
  {
    const { QUEUE_DEPTH, QUEUE_DEPTH_MAX, EST_THROUGHPUT_BPS, QUEUE_AHEAD_MS } =
      await import('../net/mediaQueue.js');

    // --- 3a. DEPTH IS EARNED BY SIZE. Pure: no transport, no match — `targetDepth`
    //     only needs the eligible set and the throughput estimate, and with no
    //     channel attached that estimate is exactly EST_THROUGHPUT_BPS.
    const depthOf = (bytes, n = 4) => {
      const list = [];
      for (let i = 0; i < n; i++) {
        list.push({ sha: SHA(String(i)), bytes, mime: 'image/png', kind: 'image', exempt: false });
      }
      return createMediaQueue({
        artifacts: { listSendable: () => list, open: () => null },
        store: fakeStore(), logger: quiet, canSend: () => false,
      }).stats().depth;
    };
    const msFor = (bytes) => (bytes / EST_THROUGHPUT_BPS) * 1000;
    ok(depthOf(200000) === QUEUE_DEPTH_MAX,
      '6i: a library of small stills primes to the CEILING — the pool the phone was starved of',
      String(depthOf(200000)));
    ok(depthOf(4000000) === Math.floor(QUEUE_AHEAD_MS / msFor(4000000)),
      '6i: a mid-sized library lands in between — the unit is WIRE TIME, not a file count',
      String(depthOf(4000000)));
    ok(depthOf(8000000) === QUEUE_DEPTH,
      '6i: and a library of big clips keeps the old depth — the wire budget is never widened',
      String(depthOf(8000000)));
    ok(depthOf(0, 0) === QUEUE_DEPTH, '6i: an empty library answers the floor, never NaN');
    ok(QUEUE_DEPTH_MAX < 12,
      '6i: the ceiling stays BELOW LANDED_MAX, or the pump would spend the wire on artifacts '
      + 'the landed ring then dropped', String(QUEUE_DEPTH_MAX));

    // --- 3b. THE PUMP BALANCES KINDS. Five images and three videos: the first card
    //     is a tie and the shuffle takes it, but the SECOND is drawn against a pool
    //     that is now one-sided, so the two kinds cannot both be missing at t+1.
    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
    }));
    const { a, b } = await consentedPair(pair);
    const mixed = [];
    for (let i = 0; i < 5; i++) {
      mixed.push({ sha: SHA('a' + i), bytes: 6000, mime: 'image/png', kind: 'image', exempt: false });
    }
    for (let i = 0; i < 3; i++) {
      mixed.push({ sha: SHA('b' + i), bytes: 6000, mime: 'video/mp4', kind: 'video', exempt: false, codec: 'avc1' });
    }
    const storeB = fakeStore();
    const order = [];
    const qA = createMediaQueue({
      artifacts: fakeArtifacts(mixed), store: fakeStore(), logger: quiet, canSend: () => true, idlePollMs: 40,
    });
    const qB = createMediaQueue({
      artifacts: { listSendable: () => [], open: () => null },
      store: storeB, logger: quiet, canSend: () => false, idlePollMs: 40,
    });
    qB.onReceived((e) => order.push(e.kind));

    /* Math.random pinned, exactly as 6e does it: the deck is shuffled with it on
     * purpose, and an unpinned test would only catch this regression sometimes. */
    const realRandom = Math.random;
    try {
      Math.random = () => 0;
      qA.attach(a, pair.host);
      qB.attach(b, pair.guest);
      for (let i = 0; i < 160 && order.length < 4; i++) await tick(25);
    } finally {
      Math.random = realRandom;
    }
    ok(order.length >= 4, '6i: the queue primed several deep during the draft', order.join(','));
    /* THE PIN MOVED WHEN RULE 4 LANDED (2026-08-05), and it moved for the reason the owner asked
     * for: this library's videos are all footage-origin, so the first two picks belong to the
     * preload and the receiver gets its video lane before its image lane. What 3b still owns —
     * and what this pin now guards — is EVERYTHING AFTER the quota: the score reads the pool as
     * video-heavy the moment it regains control and spends the next picks on images, so the
     * balance the old pin wanted arrives one slot later rather than never. */
    ok(order[0] === 'video' && order[1] === 'video',
      '6i: the FIRST TWO landings are the footage preload (rule 4) — a video throw in the opening '
      + 'minute must not render a gif clip', order.join(','));
    ok(order[2] === 'image',
      '6i: …and the rule-3b score resumes UNCHANGED the moment the quota is paid, immediately '
      + 'spending the shortage it was just handed', order.join(','));
    ok(order.slice(0, 4).filter((k) => k === 'image').length >= 1
      && order.slice(0, 4).filter((k) => k === 'video').length >= 1,
      '6i: …and the balance holds through the first four', order.join(','));
    qA.detach(); qB.detach();
    a.dispose(); b.dispose(); pair.dispose();
  }

  /* ---------- 6j. THE SHOWN LEDGER (rule 3c): a burst's spare tag slots are filled
   * with artifacts the peer already has, least-recently-shown first — but ONLY as a
   * top-up. With nothing fresh at all the answer is still [], because that is what
   * keeps "one landing, one drop" true and keeps the untagged diagnostic firing.
   */
  {
    const pair = createLoopbackPair(loopbackOptions({
      latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
    }));
    const { a, b } = await consentedPair(pair);

    const four = [];
    for (let i = 0; i < 4; i++) {
      four.push({ sha: SHA(String(i)), bytes: 5000, mime: 'image/png', kind: 'image', exempt: false });
    }
    const storeB = fakeStore();
    const qA = createMediaQueue({
      artifacts: fakeArtifacts(four), store: fakeStore(), logger: quiet, canSend: () => true, idlePollMs: 40,
    });
    const qB = createMediaQueue({
      artifacts: { listSendable: () => [], open: () => null },
      store: storeB, logger: quiet, canSend: () => false, idlePollMs: 40,
    });
    qA.attach(a, pair.host);
    qB.attach(b, pair.guest);
    for (let i = 0; i < 200 && storeB.held.size < 4; i++) await tick(25);
    ok(storeB.held.size === 4, '6j: all four stills landed (depth 10 for a library this small)',
      String(storeB.held.size));

    const t1 = qA.tagsFor(GoonPayloadKind.FlashBurst);
    ok(t1.length === 3, '6j: a FlashBurst takes XFER_TAGS_MAX fresh artifacts', String(t1.length));
    qA.markConsumed(t1);
    ok(qA.stats().shown === 3, '6j: and consumption writes all three into the shown ledger',
      String(qA.stats().shown));

    const t2 = qA.tagsFor(GoonPayloadKind.FlashBurst);
    ok(t2.length === 3, '6j: the next burst still gets three slots — one fresh, two topped up',
      String(t2.length));
    ok(t1.indexOf(t2[0]) < 0,
      '6j: the FRESH artifact goes first — a never-seen file always outranks a repeat', t2[0]);
    ok(t2[1] === t1[0] && t2[2] === t1[1],
      '6j: and the repeats are the LEAST recently shown two, in that order', t2.join(','));

    qA.markConsumed(t2);
    ok(qA.tagsFor(GoonPayloadKind.FlashBurst).length === 0,
      '6j: with nothing fresh left the answer is EMPTY, not a stale pin — "one landing, one drop" '
      + 'survives, and the receiver\'s own rotation takes it from here');

    qA.detach(); qB.detach();
    a.dispose(); b.dispose(); pair.dispose();
  }

  /* ---------- 6k. THE FOOTAGE PRELOAD (rule 4, 2026-08-05, owner): "we need to
   * preload 1 or two videos, so they land immediately, this is misleading."
   *
   * A video attack renders from an artifact that has ALREADY LANDED. Small
   * gif-converted loops cross the wire in seconds and real footage does not, so
   * the first minute of every match threw Videos that rendered two-second loops
   * — every layer working, and the throw lying. The pump now owes the peer two
   * footage videos before it is allowed to optimise anything else, and takes the
   * SMALLEST it can afford because the unit is wall clock.
   *
   * Four things have to be true at once and each has cost a round of play-test
   * somewhere in this file: the preload outranks the score, the quota is a
   * CEILING (two, then never again), a library that cannot pay it degrades
   * instead of stalling, and the moment it IS paid the PR#140 scoring is back
   * with nothing changed.
   */
  {
    /** Artifacts that allocate their bytes ON OPEN — a 50 MB row must not cost 50 MB to declare. */
    const lazyArtifacts = (list) => ({
      listSendable: () => list.map((a) => Object.assign({}, a)),
      open(sha) {
        const meta = list.find((x) => x.sha === sha);
        if (!meta) return null;
        const b = new Uint8Array(meta.bytes);
        return {
          bytes: b.length,
          mime: meta.mime,
          read: (o, l) => b.buffer.slice(o, Math.min(o + l, b.length)),
        };
      },
    });

    /** One pinned run: attach both ends, drain, hand back the landing order by sha and by kind. */
    const runLibrary = async (list, wantLandings, latencyMs = 0) => {
      const pair = createLoopbackPair(loopbackOptions({
        latencyMs, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
      }));
      const { a, b } = await consentedPair(pair);
      const storeB = fakeStore();
      const shas = [];
      const kinds = [];
      const qA = createMediaQueue({
        artifacts: lazyArtifacts(list), store: fakeStore(), logger: quiet,
        canSend: () => true, idlePollMs: 40,
      });
      const qB = createMediaQueue({
        artifacts: { listSendable: () => [], open: () => null },
        store: storeB, logger: quiet, canSend: () => false, idlePollMs: 40,
      });
      qB.onReceived((e) => { shas.push(e.sha); kinds.push(e.kind); });
      /* Math.random pinned exactly as 6e and 6i pin it: the deck is shuffled with it on purpose,
       * so an unpinned run would only catch a regression in the pick order some of the time. */
      const realRandom = Math.random;
      try {
        Math.random = () => 0;
        qA.attach(a, pair.host);
        qB.attach(b, pair.guest);
        for (let i = 0; i < 240 && shas.length < wantLandings; i++) await tick(25);
      } finally {
        Math.random = realRandom;
      }
      const stats = qA.stats();
      qA.detach(); qB.detach();
      a.dispose(); b.dispose(); pair.dispose();
      return { shas, kinds, stats, held: storeB.held };
    };

    const V_BIG = SHA('7');
    const V_MID = SHA('8');
    const V_SMALL = SHA('9');
    const IMG = SHA('0');
    const vid = (sha, bytes, extra) => Object.assign(
      { sha, bytes, mime: 'video/mp4', kind: 'video', exempt: false, codec: 'avc1' }, extra || {},
    );
    const img = (sha, bytes) => ({ sha, bytes, mime: 'image/png', kind: 'image', exempt: false });

    // --- SMALLEST FIRST, AND THE QUOTA IS A CEILING. The library is declared
    //     biggest-first on purpose: source order, deck order and the rule-3b
    //     score would each have picked something else.
    {
      const r = await runLibrary([
        vid(V_BIG, 90000), vid(V_MID, 30000), vid(V_SMALL, 6000), img(IMG, 4000),
      ], 4);
      ok(r.shas[0] === V_SMALL,
        '6k: the SMALLEST footage video goes first — the point is wall-clock time-to-first-real-'
        + 'video, so a 6 KB clip beats a 90 KB one the deck happened to deal on top',
        r.shas.map((s) => s[0]).join(','));
      ok(r.shas[1] === V_MID,
        '6k: …and the second preload slot is the next smallest, not the deck\'s choice either',
        r.shas.map((s) => s[0]).join(','));
      ok(r.shas[2] === IMG,
        '6k: TWO IS A CEILING. The third footage video is NOT preloaded: the quota is paid, the '
        + 'rule-3b score is back in charge, and it spends the slot on the kind the pool is short '
        + 'of', r.shas.map((s) => s[0]).join(','));
      ok(r.shas[3] === V_BIG, '6k: the big clip lands last, on its merits',
        r.shas.map((s) => s[0]).join(','));
      ok(r.stats.preload && r.stats.preload.quota === 2 && r.stats.preload.landed >= 2,
        '6k: and stats() reports the quota, so a play-test can tell "still preloading" from '
        + '"this library has no footage in it"', JSON.stringify(r.stats.preload));
    }

    // --- POST-QUOTA IS THE PR#140 SCORE, UNCHANGED. Two footage clips pay the
    //     quota, then the pool is two images and two gif-origin videos: +2 for
    //     the hungry kind must beat +1 for a fresh origin, twice, before the
    //     variety score gets its turn. That ordering IS rule 3b's contract.
    {
      const F1 = SHA('a');
      const F2 = SHA('b');
      const I1 = SHA('c');
      const I2 = SHA('d');
      const G1 = SHA('e');
      const G2 = SHA('f');
      const r = await runLibrary([
        vid(F1, 5000), vid(F2, 5000), img(I1, 5000), img(I2, 5000),
        vid(G1, 5000, { origin: 'gif' }), vid(G2, 5000, { origin: 'gif' }),
      ], 6);
      ok(r.kinds.join(',') === 'video,video,image,image,video,video',
        '6k: preload, preload, then the PR#140 score exactly — the two images while the pool reads '
        + 'video-heavy (+2 kind shortage outranks the +1 fresh origin the gif clips were '
        + 'offering), and the gif clips once the kinds are level', r.kinds.join(','));
      ok(r.shas[0] === F1 && r.shas[1] === F2,
        '6k: the preload took the footage pair and left the gif-origin clips to the score — '
        + '"footage-origin" is kind video AND origin !== gif, nothing else',
        r.shas.map((s) => s[0]).join(','));
    }

    // --- A LIBRARY WITH NO FOOTAGE AT ALL IS UNTOUCHED. Every video is a
    //     converted loop, the rule finds nothing, and the pick order is the one
    //     this file pinned before rule 4 existed: an image lane and a video lane
    //     before the first payload fires.
    {
      const r = await runLibrary([
        img(SHA('1'), 5000), img(SHA('2'), 5000), img(SHA('3'), 5000),
        vid(SHA('4'), 5000, { origin: 'gif' }), vid(SHA('5'), 5000, { origin: 'gif' }),
      ], 4);
      ok(r.kinds[0] !== r.kinds[1],
        '6k: zero footage videos — the preload is INERT and 3b\'s alternation is byte-for-byte '
        + 'what it was', r.kinds.join(','));
      ok(r.stats.preload.landed === 0,
        '6k: …and the quota simply never fills, which is a state and not a stall',
        JSON.stringify(r.stats.preload));
    }

    // --- ONE FOOTAGE VIDEO: "as many as exist". The quota can never fill, and
    //     that is a STATE, not a wedge — the lane preloads the one it has and
    //     then goes straight back to normal service.
    {
      const ONLY = SHA('4');
      const STILL = SHA('5');
      const r = await runLibrary([vid(ONLY, 20000), img(STILL, 5000)], 2);
      ok(r.shas[0] === ONLY && r.shas[1] === STILL,
        '6k: one footage video preloads one, then the still — an unfillable quota does not hold '
        + 'the lane open waiting for a second', r.shas.map((s) => s[0]).join(','));
      ok(r.stats.preload.landed === 1,
        '6k: …and the counter honestly says one of two, forever, without ever blocking anything',
        JSON.stringify(r.stats.preload));
    }

    // --- THE UNAFFORDABLE ONE PROVES THE WIRE BUDGET STILL OUTRANKS THE RULE.
    //     50 MB cannot land inside MAX_XFER_MS at anything like a phone's uplink,
    //     so `eligible()` never lists it and the preload never sees it. The link
    //     is given 40 ms of latency for exactly that reason: `estBps()` is a
    //     MEASURED rate after the first landing and a zero-latency loopback
    //     measures hundreds of MB/s, which would (correctly!) make the clip
    //     affordable and prove nothing. On a link that behaves like a link it
    //     stays out of the pool for the whole match.
    {
      const HUGE = SHA('6');
      const S1 = SHA('7');
      const S2 = SHA('8');
      const library = [vid(HUGE, 50 * 1000 * 1000), img(S1, 5000), img(S2, 5000)];

      // The gate itself, with no transport at all: `sendableCount` is `eligible().length`,
      // and before anything lands `estBps()` is EST_THROUGHPUT_BPS by definition.
      const cold = createMediaQueue({
        artifacts: lazyArtifacts(library), store: fakeStore(), logger: quiet, canSend: () => false,
      });
      ok(cold.sendableCount() === 2,
        '6k: the 50 MB footage video is not SENDABLE in the first place — it cannot land inside '
        + 'MAX_XFER_MS at the default estimate, so the preload never even sees it as a candidate',
        String(cold.sendableCount()));

      const r = await runLibrary(library, 2, 40);
      ok((r.shas[0] === S1 || r.shas[0] === S2) && (r.shas[1] === S1 || r.shas[1] === S2),
        '6k: with no AFFORDABLE footage video the preload finds nothing and falls straight through '
        + 'to the rule-3b score — the stills land at once instead of the lane stalling',
        r.shas.map((s) => s[0]).join(','));
      ok(!r.held.has(HUGE),
        '6k: …and the 50 MB clip was never offered, because a rule that could overrule the '
        + 'MAX_XFER_MS budget would let one file own the bulk lane for the whole match');
      ok(r.stats.preload.landed === 0,
        '6k: the quota is still owed and still harmless', JSON.stringify(r.stats.preload));
    }

    // --- A CODEC THE PEER CANNOT DECODE IS NOT FOOTAGE THE PRELOAD MAY SEND.
    //     Same argument as the budget, and the same failure if it were special-
    //     cased: 6f's HEVC clip would be preloaded straight into a black window.
    {
      const pair = createLoopbackPair(loopbackOptions({
        latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
      }));
      const { a, b } = await consentedPair(pair);
      const HEVC = SHA('7');
      const STILL = SHA('8');
      const storeB = fakeStore();
      const qA = createMediaQueue({
        artifacts: lazyArtifacts([
          vid(HEVC, 9000, { codec: 'hvc1.1.6.L93.B0' }), img(STILL, 5000),
        ]),
        store: fakeStore(), logger: quiet, canSend: () => true, idlePollMs: 40,
      });
      const qB = createMediaQueue({
        artifacts: { listSendable: () => [], open: () => null },
        store: storeB, logger: quiet, canSend: () => false, idlePollMs: 40,
        acceptsCodecs: ['avc1'],
      });
      qA.attach(a, pair.host);
      qB.attach(b, pair.guest);
      for (let i = 0; i < 160 && storeB.held.size < 1; i++) await tick(25);
      await tick(200);                    // …and every chance to send the other one anyway
      ok(storeB.held.has(STILL) && !storeB.held.has(HEVC),
        '6k: the preload never offers a codec the peer cannot decode — the still landed, the HEVC '
        + 'clip did not, and the lane kept moving');
      qA.detach(); qB.detach();
      a.dispose(); b.dispose(); pair.dispose();
    }
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

  /* the PERF STATUS ROW (ui/perfProbe.js writes it twice a second). It is
   * rewritten in place and must never reach the ring — a readout that pushed a
   * line would flush the last fifty warnings out in twenty-five seconds, which
   * is the one thing this overlay exists to prevent. */
  const ringBefore = panel.lines().length;
  panel.setStatus('58fps · worst 34ms · lt 0 · fx:idle');
  ok(panel.status() === '58fps · worst 34ms · lt 0 · fx:idle', 'setStatus round-trips', panel.status());
  ok(panel.lines().length === ringBefore,
    'and it does NOT touch the ring — the readout can never flush a warning out');
  panel.setStatus('19fps');
  ok(panel.status() === '19fps', 'it is rewritten in place, not appended', panel.status());
  panel.setStatus('');
  ok(panel.status() === '', 'and an empty status clears it (the probe does this on stop)');
  let statusThrew = false;
  try { panel.setStatus(null); panel.setStatus(undefined); } catch (_e) { statusThrew = true; }
  ok(!statusThrew, 'setStatus can never throw — it sits on a timer inside a debug session');

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

/* ============================================================================
 * 7b. THE PERF PROBE (ui/perfProbe.js) — the play-test's stopwatch.
 *
 * Added for the 2026-08-05 phone report: the owner says it lags, three rounds of
 * code-reading have produced three theories and no evidence, and the device has
 * no devtools. The next session has to come back with numbers.
 *
 * What must hold, or it is worse than not having it:
 *   · ?debug=1 ONLY — the module is not even fetched otherwise, so the shipped
 *     page's behaviour is byte-identical;
 *   · it names WHICH detector fired. Safari has no `longtask` entry type, and a
 *     probe that silently observed nothing on the one device it was built for
 *     would be worse than none at all — so the rAF gap becomes the detector and
 *     the readout says so;
 *   · it never allocates per frame. A perf tool that makes garbage sixty times a
 *     second is arguing against its own numbers.
 * ==========================================================================*/
{
  const probeMod = await import('../ui/perfProbe.js');
  const {
    createPerfProbe, formatPerfLine, formatLoadContext, readLoadContext,
    PERF_LONGTASK_WARN_MS, PERF_PAINT_MS, PERF_BUCKET_MS,
  } = probeMod;

  // ---- the two pure formatters (no DOM, no window — this is why they are pure)
  ok(formatLoadContext({ heat: 'hot', flash: 14, vwin: 2, spiral: 1, drain: 1, gov: true, lite: true })
    === 'fx:hot fl:14 vw:2 sp dr gov lite',
    'the load context names the burst, the windows, the spiral, the veil, the governor and the tier',
    formatLoadContext({ heat: 'hot', flash: 14, vwin: 2, spiral: 1, drain: 1, gov: true, lite: true }));
  ok(formatLoadContext({ heat: 'idle' }) === 'fx:idle',
    'and an idle page is ONE token — zero counts are omitted or the line does not fit on a phone',
    formatLoadContext({ heat: 'idle' }));
  ok(formatLoadContext(null) === 'fx:idle', 'a missing bag reads as idle, never as a throw');

  ok(formatPerfLine({ fps: 59.6, worstMs: 18.2, hits: 0, longestMs: 0, observed: true, ctx: 'fx:idle' })
    === '60fps · worst 18ms · lt 0 · fx:idle',
    'a healthy page is one short line',
    formatPerfLine({ fps: 59.6, worstMs: 18.2, hits: 0, longestMs: 0, observed: true, ctx: 'fx:idle' }));
  const sick = formatPerfLine({ fps: 19.4, worstMs: 412.7, hits: 4, longestMs: 380.2, observed: true, ctx: 'fx:hot fl:14' });
  ok(sick === '19fps · worst 413ms · lt 4/380ms · fx:hot fl:14',
    'and a sick one carries the fps, the worst gap, the tally and the load that caused it', sick);
  const safari = formatPerfLine({ fps: 19, worstMs: 412, hits: 4, longestMs: 412, observed: false, ctx: 'fx:hot' });
  ok(/lt~ 4/.test(safari),
    'WITHOUT the longtask API the tally is written `lt~` — the tilde is the whole caveat, and it '
    + 'stops anyone comparing a Safari number to a Chromium one by accident', safari);

  ok(typeof readLoadContext() === 'object' && readLoadContext().heat === 'idle',
    'readLoadContext answers off a page with no DOM at all rather than throwing');
  ok(readLoadContext() === readLoadContext(),
    'and it refills ONE shared bag — a fresh object twice a second is the only garbage this '
    + 'module could produce');

  // ---- the loop, over a hand-cranked window
  const mkWin = () => {
    let clock = 0;
    let pending = null;
    let rafCalls = 0;
    const seen = new Set();
    return {
      now: () => clock,
      rafCalls: () => rafCalls,
      distinctCallbacks: () => seen.size,
      /** Advance the clock by `ms` and deliver one frame. */
      step(ms) { clock += ms; const fn = pending; pending = null; if (fn) fn(clock); },
      win: {
        performance: { now: () => clock },
        requestAnimationFrame(fn) { rafCalls++; seen.add(fn); pending = fn; return rafCalls; },
        cancelAnimationFrame() { pending = null; },
        // NO PerformanceObserver — this is the Safari/iOS shape on purpose.
      },
    };
  };

  {
    const h = mkWin();
    const warns = [];
    let status = '';
    const probe = createPerfProbe({
      win: h.win,
      setStatus: (t) => { status = t; },
      onWarn: (m) => warns.push(m),
      context: () => ({ heat: 'hot', flash: 12, vwin: 1, gov: true, lite: true }),
      warnMs: 100, paintMs: 0, cooldownMs: 0,
    });
    ok(probe.start() === true, 'the probe starts');
    for (let i = 0; i < 20; i++) h.step(16);          // ~320ms of healthy frames
    ok(Math.round(probe.sample().fps) === 63,
      'twenty 16ms frames read as ~63fps', String(Math.round(probe.sample().fps)));
    ok(warns.length === 0, 'and a healthy page says nothing at all', warns.join(' | '));
    ok(/63fps · worst 16ms · lt~ 0 · fx:hot fl:12 vw:1 gov lite/.test(status),
      'the readout is one line and it carries the load context', status);

    h.step(400);                                       // …and then the hitch
    ok(probe.sample().worstMs === 400, 'the worst gap is captured exactly',
      String(probe.sample().worstMs));
    ok(warns.length === 1, 'one warn, not one per frame', String(warns.length));
    ok(/^perf: frame stall 400ms/.test(warns[0]),
      'WITHOUT the longtask API a 400ms gap IS the long task, and the line says which detector '
      + 'saw it', warns[0]);
    ok(/fx:hot fl:12 vw:1 gov lite/.test(warns[0]),
      'and it names what was on screen when it happened — the whole point of the exercise',
      warns[0]);
    ok(/\|.*fps worst 400ms/.test(warns[0]), 'plus the running numbers, so one line is enough',
      warns[0]);
    ok(probe.sample().hits === 1 && probe.sample().observed === false,
      'the stall is tallied and flagged as gap-derived');

    // ONE function object, handed back to rAF every frame. An inline arrow here
    // would be sixty closures a second in the loop that measures the page.
    ok(h.distinctCallbacks() === 1,
      'the frame callback is a single hoisted function, not a per-frame closure',
      String(h.distinctCallbacks()));

    probe.stop();
    ok(probe.running() === false && status === '',
      'stop unsubscribes and clears the readout');
    const before = h.rafCalls();
    h.step(16);
    ok(h.rafCalls() === before, 'and a stopped probe never re-arms itself', String(h.rafCalls()));
  }

  // ---- the cooldown: a page genuinely on fire may not flood the C# log
  {
    const h = mkWin();
    const warns = [];
    const probe = createPerfProbe({
      win: h.win, onWarn: (m) => warns.push(m), context: () => ({ heat: 'hot' }),
      warnMs: 50, paintMs: 100000, cooldownMs: 1000,
    });
    probe.start();
    h.step(16);
    for (let i = 0; i < 4; i++) h.step(200);          // four stalls inside one cooldown
    ok(warns.length === 1, 'four stalls inside one cooldown produce ONE line, not four',
      String(warns.length));
    h.step(2000);
    ok(warns.length === 2 && /\(\+\d+ more/.test(warns[1]),
      'and the next line that gets through REPORTS the ones the cooldown ate — the rate limit '
      + 'costs a timestamp, not a fact', warns[1]);
    probe.stop();
  }

  // ---- with a longtask observer, the rAF detector stands down (or every hitch
  //      would be counted twice and every number on the line would be inflated)
  {
    const h = mkWin();
    const warns = [];
    let cb = null;
    h.win.PerformanceObserver = function PO(fn) { cb = fn; };
    h.win.PerformanceObserver.supportedEntryTypes = ['longtask', 'paint'];
    h.win.PerformanceObserver.prototype.observe = function () {};
    h.win.PerformanceObserver.prototype.disconnect = function () {};
    const probe = createPerfProbe({
      win: h.win, onWarn: (m) => warns.push(m), context: () => ({ heat: 'warm' }),
      warnMs: 100, paintMs: 100000, cooldownMs: 0,
    });
    probe.start();
    ok(probe.sample().observed === true, 'the observer took, so the readout drops the tilde');
    h.step(16);
    h.step(500);                                       // a huge gap the observer already saw
    ok(probe.sample().hits === 0,
      'the rAF detector does NOT double-count it — the observer owns the tally here',
      String(probe.sample().hits));
    cb({ getEntries: () => [{ duration: 312 }] });
    ok(probe.sample().hits === 1 && Math.round(probe.sample().longestMs) === 312,
      'and a real longtask entry is what counts', JSON.stringify(probe.sample().hits));
    ok(/^perf: long task 312ms/.test(warns[0]), 'named as a long task, not a stall', warns[0]);
    probe.stop();
  }

  // ---- no rAF at all (node, the import sweep) -> an inert handle, never a throw
  const headless = createPerfProbe({ win: null });
  ok(headless.start() === false && headless.running() === false && headless.sample() === null,
    'no requestAnimationFrame -> every method is a no-op, exactly like the overlay handle');
  headless.stop();

  ok(PERF_LONGTASK_WARN_MS === 250 && PERF_PAINT_MS === 500 && PERF_BUCKET_MS === 2500,
    'the dials: warn over 250ms, repaint at most 2x/s, two 2.5s buckets = the ~5s window');

  // ---- and boot wires it BEHIND the debug flag and nowhere else
  const bootSrc = read('boot.js');
  ok(/import\('\.\/ui\/perfProbe\.js'\)/.test(bootSrc),
    'the probe is a dynamic import, so a page that never asks for debug never fetches it');
  ok(/function initPerfProbe\(\)/.test(bootSrc)
    && bootSrc.indexOf('initPerfProbe();') > bootSrc.indexOf("import('./ui/debugOverlay.js')"),
    'and it is armed INSIDE the branch that already decided the overlay was wanted — one gate, '
    + 'not two that can disagree');
  ok(/onWarn: \(m\) => logger\.warn\(m\)/.test(bootSrc),
    'its warns go through logger.warn — the ONE sink that reaches the C# host log, the console '
    + 'and the overlay ring at once');
  ok(/setStatus: \(t\) =>/.test(bootSrc), 'and the readout goes to the overlay status row');

  // ---- the fence: ui/ may import exec/, but never the other way round
  const probeSrc = read('ui/perfProbe.js');
  ok(!/from '\.\.\/net\//.test(probeSrc),
    'ui/perfProbe.js imports nothing from net/ — it reads the render tier, not the wire');
  ok(/from '\.\.\/exec\/loadGovernor\.js'/.test(probeSrc)
    && /from '\.\.\/exec\/layers\.js'/.test(probeSrc)
    && /from '\.\.\/exec\/perfTier\.js'/.test(probeSrc),
    'it reads the governor, the layer registry and the device tier — three cheap READERS, no '
    + 'renderer, no subscription');
  const execFiles = fs.readdirSync(path.join(HERE, '..', 'exec'));
  let leaked = '';
  for (const f of execFiles) {
    if (!f.endsWith('.js')) continue;
    if (/perfProbe/.test(read('exec/' + f))) leaked += f + ' ';
  }
  ok(leaked === '', 'and nothing in exec/ knows the probe exists — the fence held', leaked);
}

/* ============================================================================
 * 8. PRACTICE SHOWS YOU YOUR OWN LIBRARY (the phone bug, round 4, 2026-08-04).
 *
 * Owner report, twice: "from mobile it's not triggering any asset even if I
 * uploaded them and went to practice", with every arsenal chip reading `locked`.
 *
 * THE SHAPE OF IT. In a duel your arsenal shot lands on THEIR screen. In practice
 * the opponent is ui/soloDriver.js, which has no exec/ renderer at all — so a
 * payload the practice player fires is a payload nobody ever sees, and the only
 * media that reaches their own screen is the element ramp plus whatever the bot
 * throws BACK. An inbound payload resolves against the RECEIVER's library
 * (exec/media.js drawFor -> drawKind), so the bot's flash burst is drawn from the
 * player's own uploads — that is the loop, and it used to wait on the bot's
 * economy (PAYLOAD_TRY_MS, a 35% skip, and charges it has not earned yet).
 *
 * Two fixes, both practice-only:
 *   · the bot opens with a scheduled salvo of the two MEDIA-CARRYING kinds;
 *   · boot seeds the practice arsenal, so the slots that draw from the library
 *     are not locked behind a bubble-drop grind.
 *
 * THE REGRESSION THIS BLOCK EXISTS FOR: the receiver validates the SENDER's
 * charges off the last state TICK, not off the payload frame, so crediting and
 * firing in the same turn is rejected with "claimed 0 < cost 1". The first cut
 * did exactly that and the salvo never landed.
 * ==========================================================================*/
{
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const { createSoloDriver, SALVO, SALVO_CREDIT_LEAD_MS } = await import('../ui/soloDriver.js');
  const { GoonConsts, GoonPayloadKind, costOf } = await import('../core/contracts.js');

  // ---- the shape of the salvo
  ok(Array.isArray(SALVO) && SALVO.length >= 2, 'the practice bot has an opening salvo', String(SALVO.length));
  const salvoKinds = SALVO.map((s) => s.kind);
  ok(salvoKinds.includes(GoonPayloadKind.FlashBurst) && salvoKinds.includes(GoonPayloadKind.Video),
    'and it is the two MEDIA-CARRYING kinds — the ones that draw off the deck', JSON.stringify(salvoKinds));
  ok(SALVO[0].atMs <= 10000, 'the first shot lands in the first ten seconds, not after a grind',
    String(SALVO[0].atMs));
  ok(SALVO_CREDIT_LEAD_MS > GoonConsts.TickIntervalMs,
    'the charges are credited more than one state tick ahead of the shot — the receiver reads the '
    + 'wallet off the tick, so credit-and-fire in one turn is rejected',
    SALVO_CREDIT_LEAD_MS + 'ms vs ' + GoonConsts.TickIntervalMs + 'ms');
  ok(SALVO.every((s) => s.atMs - SALVO_CREDIT_LEAD_MS >= 0),
    'every shot has room for its own credit lead');
  const soloSrc = stripComments(read('ui/soloDriver.js'));
  ok(/if \(salvoArmed > 0\) return;/.test(soloSrc),
    'and the ordinary cadence cannot spend a salvo shot\'s charges out from under it');
  ok(/match\.creditCharges\(cost - have, 'practice-salvo'\)/.test(soloSrc),
    'the charges go through the engine, so the wire truth stays the truth (no back door)');
  ok(/match\.tryFirePayload\(\{/.test(soloSrc),
    'and the shot goes through the same public API a human uses — every engine gate still applies');

  // ---- the real thing: two engines, a real loopback, the real driver
  const pair = createLoopbackPair(loopbackOptions({
    latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, logger: quiet,
  }));
  const me = new GoonMatchService(pair.host, true, { logger: quiet, displayName: 'Me', tag: 'GG:me' });
  const bot = new GoonMatchService(pair.guest, false, { logger: quiet, displayName: 'Practice', tag: 'GG:bot' });
  const driver = createSoloDriver({ match: bot, logger: quiet });

  const inbound = [];
  me.onPayloadAccepted((e) => inbound.push(e.payload));

  me.adoptLobby();
  bot.adoptLobby();
  driver.start();
  await pair.connect();
  await wait(80);
  me.proposeConsent(60, 0, 30000);
  await wait(60);
  me.confirmConsent();
  await wait(400);
  for (let i = 0; i < 40 && me.phase === GoonMatchPhase.Draft; i++) { me.confirmDraft(); await wait(120); }
  ok(me.phase === GoonMatchPhase.Countdown || me.phase === GoonMatchPhase.Live,
    'practice reaches the countdown with the bot signed', String(me.phase));

  // Countdown + the first salvo's own clock, plus slack for the schedule buffer.
  const deadline = Date.now() + SALVO[0].atMs + 12000;
  while (Date.now() < deadline && inbound.length === 0) await wait(200);

  ok(inbound.length > 0,
    'THE FIX: the practice player receives the bot\'s opening payload — the thing that puts their own '
    + 'library on their own screen', String(inbound.length));
  ok(inbound.length > 0 && inbound[0].kind === SALVO[0].kind,
    'and it is the flash burst, i.e. the images', inbound.length ? String(inbound[0].kind) : 'none');
  ok(inbound.length > 0 && costOf(inbound[0].kind) > 0,
    'a real, costed payload — not a special case the renderer has to know about');

  driver.stop();
  me.dispose();
  bot.dispose();
  pair.dispose();

  // ---- boot seeds the practice arsenal, and ONLY the practice arsenal
  const bootSeed = stripComments(read('boot.js'));
  ok(/function seedPracticeArsenal\(match\)/.test(bootSeed), 'boot.js has seedPracticeArsenal()');
  ok(/const PRACTICE_SEED = Object\.freeze\(\[[\s\S]{0,300}?id: 'flash'[\s\S]{0,200}?id: 'video'/.test(bootSeed),
    'and it seeds the two slots that draw from the library (flash + video)');
  ok(/arsenal\.armDrop\(seed\.id, \{ count: 1, silent: true \}\)/.test(bootSeed)
    && /match\.creditCharges\(charges, 'practice-seed'\)/.test(bootSeed),
    'through the same two public seams ui/drops.js uses — armDrop, and the charges that back it');
  const seedCalls = (bootSeed.match(/seedPracticeArsenal\(/g) || []).length;
  ok(seedCalls === 2, 'seedPracticeArsenal is defined once and called once', String(seedCalls));
  ok(/async function startSolo\(\)[\s\S]*?seedPracticeArsenal\(local\)/.test(bootSeed)
    || /seedPracticeArsenal\(local\)[\s\S]*?async function startSolo\(\)/.test(bootSeed),
    'and that one call is wired from startSolo — practice only, duel gating untouched');
  const arsenalSrc = stripComments(read('ui/arsenal.js'));
  const armDropAt = arsenalSrc.indexOf('function armDrop');
  ok(/function armDrop\(id, \{ count = DROP_STACK/.test(arsenalSrc),
    'ui/arsenal.js still owns armDrop — nothing reached past it to poke a tile');
  ok(armDropAt > 0 && !/practice|solo/i.test(arsenalSrc.slice(armDropAt, armDropAt + 600)),
    'and armDrop knows nothing about practice: the mode lives in boot, the slot logic does not');
}

console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
process.exit(failures === 0 ? 0 : 1);
