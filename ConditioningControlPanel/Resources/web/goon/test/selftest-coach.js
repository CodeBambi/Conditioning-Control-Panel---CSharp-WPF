// Self-contained sanity pass over the COACHING TIER — ui/coach.js, the S.coach
// copy deck, the two prefs behind them, and the call sites that fire them.
//
//   node Resources/web/goon/test/selftest-coach.js
//
// The feature is one sentence: the first time each mechanic touches a player,
// say one true thing about it, once, ever. Every check below defends one word of
// that sentence.
//
//   1. IMPORT SWEEP. There are no devtools inside WebView2 — a module that
//      throws at import does not error, the loader simply never settles and the
//      player watches a spinner forever.
//   2. THE POLICY IS PURE. shouldFire/hasSeen/withSeen decide everything and
//      touch no store, no clock and no DOM, so the rules are testable without
//      building a match.
//   3. ONCE MEANS ONCE, AND THE MARK GOES DOWN AT QUEUE TIME. A hint dropped by
//      the pacer, cleared by a teardown, or fired into a page with no toast
//      layer is still spent. A hint refused because the feature is OFF is NOT —
//      switching hints back on must not have silently burned the deck.
//   4. THE PACER SPACES THEM. Firsts arrive in clumps; four toasts at once is
//      noise. One per COACH_GAP_MS, and a queue past COACH_QUEUE_MAX drops
//      rather than grows.
//   5. NOTHING IS BLOCKING. The module reaches for `toasts.show` and nothing
//      else — no sheet, no modal, no scrim. A sheet opened during Live is what
//      froze the drop economy the last time one was, and coaching is the least
//      important thing on the desk.
//   6. THE COPY IS TRUE TO THE CODE. Every number in S.coach is cross-checked
//      against the constant that produces it. This is the check that matters
//      most: a wrong explainer is worse than no explainer, because a player
//      plans around it. It is also the one that will fail first when somebody
//      retunes a constant, which is the entire point.
//   7. EVERY HINT HAS A CALL SITE AND EVERY CALL SITE HAS A HINT. An id nothing
//      fires is dead weight; a fire() with an id the module does not own is a
//      hint that can never be marked and would therefore repeat forever.
//   8. THE PARALLEL-PR FENCE. S.voice and boot's reportMicGate are being edited
//      elsewhere; this suite pins that the coaching pass did not touch either.

// ------------------------------------------------------------------ harness

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, debug() {}, log() {} };
const tick = (ms) => new Promise((r) => setTimeout(r, ms));

const fs = await import('node:fs');
const path = await import('node:path');
const urlMod = await import('node:url');
const HERE = path.dirname(urlMod.fileURLToPath(import.meta.url));
const ROOT = path.join(HERE, '..');
// LF-normalized: the worktree is CRLF (core.autocrlf) and every pin below is
// written against \n.
const read = (rel) => fs.readFileSync(path.join(ROOT, rel), 'utf8').replace(/\r\n/g, '\n');

/** Strip // and block comments so "is this code or a note?" is answerable. */
function stripComments(src) {
  return String(src)
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:])\/\/[^\n]*/g, '$1');
}

// =========================================================== 1. import sweep

let coachMod = null;
let stringsMod = null;
let prefsMod = null;
let contractsMod = null;
let arsenalMod = null;
{
  let threw = '';
  try {
    coachMod = await import('../ui/coach.js');
    stringsMod = await import('../ui/strings.js');
    prefsMod = await import('../ui/prefs.js');
    contractsMod = await import('../core/contracts.js');
    arsenalMod = await import('../ui/arsenal.js');
  } catch (e) { threw = (e && e.message) || String(e); }
  ok(!threw, 'the coaching tier imports clean under node (no DOM at import)', threw);
}

const { createCoach, COACH, COACH_IDS, COACH_GAP_MS, COACH_QUEUE_MAX, COACH_TOAST_MS,
  COACH_PREF_ENABLED, COACH_PREF_SEEN, isCoachId, hasSeen, withSeen, shouldFire } = coachMod;
const S = stringsMod.S;
const { GoonConsts } = contractsMod;
const { ARSENAL_ITEMS } = arsenalMod;

// ======================================================== 2. the pure policy
{
  ok(COACH_IDS.length >= 8, 'there are at least eight hint ids', String(COACH_IDS.length));
  ok(new Set(COACH_IDS).size === COACH_IDS.length, 'and no two of them collide');
  ok(COACH_IDS.every((id) => typeof id === 'string' && id.length > 0), 'every id is a non-empty string');

  ok(isCoachId(COACH.DROP) === true, 'isCoachId knows an id it owns');
  ok(isCoachId('nope') === false && isCoachId(null) === false && isCoachId(7) === false,
    'and refuses anything else, without throwing');

  ok(hasSeen(null, COACH.POP) === false, 'hasSeen tolerates a missing map');
  ok(hasSeen({}, COACH.POP) === false, 'and an empty one');
  ok(hasSeen({ pop: true }, COACH.POP) === true, 'and reads a mark');
  // A store round-trips through JSON and through prefs.coerce; an older build's
  // 1 must not resurrect a hint somebody already read.
  ok(hasSeen({ pop: 1 }, COACH.POP) === true, 'ANY truthy member counts as spent, not just `true`');

  const base = Object.freeze({ pop: true });
  const next = withSeen(base, COACH.DROP);
  ok(next !== base && next.pop === true && next.drop === true,
    'withSeen returns a NEW map carrying both marks — the caller\'s copy is never mutated');
  ok(withSeen(base, COACH.POP) === base, 'and is a no-op (same reference) when the mark is already there');
  ok(withSeen(base, 'not-a-hint') === base, 'a typo at a call site cannot grow the stored blob');
  ok(Object.keys(withSeen(null, COACH.POP)).length === 1, 'withSeen tolerates a missing map too');

  ok(shouldFire(COACH.POP, { enabled: true, seen: {} }) === true, 'shouldFire: fresh + on = yes');
  ok(shouldFire(COACH.POP, { enabled: false, seen: {} }) === false, 'hints off = no');
  ok(shouldFire(COACH.POP, { enabled: true, seen: { pop: true } }) === false, 'already spent = no');
  ok(shouldFire('nope', { enabled: true, seen: {} }) === false, 'an unknown id = no');
  ok(shouldFire(COACH.POP) === true, 'and with no state at all it coaches (a dev harness is not a reason to go silent)');
}

// ============================================ 3. the prefs behind the feature
{
  const { PREF_DEFAULTS, createPrefs } = prefsMod;
  ok(PREF_DEFAULTS[COACH_PREF_ENABLED] === true,
    'coachHints defaults ON — a duel with nothing explained is the bug this exists for');
  ok(PREF_DEFAULTS[COACH_PREF_SEEN] && typeof PREF_DEFAULTS[COACH_PREF_SEEN] === 'object',
    'coachSeen defaults to an object, so prefs.coerce takes its object branch');
  ok(Object.keys(PREF_DEFAULTS[COACH_PREF_SEEN]).length === 0, 'and it starts empty');

  const p = createPrefs();
  p.set(COACH_PREF_SEEN, { pop: true, drop: true });
  const back = p.get(COACH_PREF_SEEN);
  ok(back && back.pop === true && back.drop === true, 'a seen-map round-trips through the store');
  ok(back !== p.get(COACH_PREF_SEEN), 'and comes out as a COPY each time — an edit has to come back through set()');
  // The failure mode the object branch exists for: String({}) is "[object Object]".
  ok(typeof p.get(COACH_PREF_SEEN) === 'object', 'it never degrades to a nine-character string');
  p.set(COACH_PREF_SEEN, 'garbage');
  ok(typeof p.get(COACH_PREF_SEEN) === 'object' && Object.keys(p.get(COACH_PREF_SEEN)).length === 0,
    'and a corrupt value lands as a fresh empty map rather than as itself');
}

/** A toast sink that records instead of painting. */
function fakeToasts() {
  const shown = [];
  return {
    shown,
    show(text, opts) { shown.push({ text, opts: opts || {} }); return {}; },
    good(t, o) { return this.show(t, o); },
    warn(t, o) { return this.show(t, o); },
    bad(t, o) { return this.show(t, o); },
  };
}

/** The smallest prefs-shaped store the coach needs. */
function fakeStore(seed) {
  const values = Object.assign({ [COACH_PREF_ENABLED]: true, [COACH_PREF_SEEN]: {} }, seed || {});
  return {
    values,
    get(k) { const v = values[k]; return (v && typeof v === 'object') ? Object.assign({}, v) : v; },
    set(k, v) { values[k] = v; return true; },
  };
}

// ================================================== 4. once ever, and the mark
{
  const toasts = fakeToasts();
  const prefs = fakeStore();
  const c = createCoach({ prefs, toasts, logger: quiet });

  ok(c.fire(COACH.POP, 'pop line') === true, 'the first fire is accepted');
  ok(c.seen(COACH.POP) === true, 'and the hint is immediately marked spent');
  ok(prefs.values[COACH_PREF_SEEN].pop === true, 'in the STORE, not only on the page');
  ok(c.fire(COACH.POP, 'pop line') === false, 'a second fire of the same hint is refused');
  ok(toasts.shown.length === 1, 'and nothing is shown twice', String(toasts.shown.length));
  ok(toasts.shown[0].text === 'pop line', 'the caller\'s finished sentence is what goes out');
  ok(toasts.shown[0].opts.ms === COACH_TOAST_MS,
    'coached toasts dwell longer than a status toast — they are a sentence, not a state');

  ok(c.fire('not-a-hint', 'x') === false, 'an unknown id is refused rather than shown');
  ok(c.fire(COACH.DIAL, '') === false, 'an empty sentence is refused rather than shown as a blank toast');
  c.dispose();
}

// A hint refused because the feature is OFF must NOT be spent.
{
  const toasts = fakeToasts();
  const prefs = fakeStore({ [COACH_PREF_ENABLED]: false });
  const c = createCoach({ prefs, toasts, logger: quiet });
  ok(c.enabled === false, 'the coach reads the switch off the store');
  ok(c.fire(COACH.CHECK, 'check line') === false, 'with hints off, nothing fires');
  ok(toasts.shown.length === 0, 'and nothing is shown');
  ok(c.seen(COACH.CHECK) === false,
    'and the hint is NOT burned — turning hints back on must not hand the player an empty deck');

  prefs.values[COACH_PREF_ENABLED] = true;
  ok(c.enabled === true, 'the switch is read LIVE, so the drawer reaches a match already running');
  ok(c.fire(COACH.CHECK, 'check line') === true, 'and the hint is still there to spend');
  c.dispose();
}

// No store at all (private mode, a dev harness): still once per page.
{
  const toasts = fakeToasts();
  const c = createCoach({ toasts, logger: quiet });
  ok(c.fire(COACH.EMOTE, 'a') === true && c.fire(COACH.EMOTE, 'a') === false,
    'with no prefs store the page-local ledger still keeps "once"');
  c.dispose();
}

// No toast layer at all: the hint is spent, not banked.
{
  const prefs = fakeStore();
  const c = createCoach({ prefs, logger: quiet });
  ok(c.fire(COACH.DIAL, 'a') === true, 'a page with no toast tier still accepts a fire');
  ok(c.seen(COACH.DIAL) === true, 'and spends it — hints must never bank up for a later match');
  c.dispose();
}

// ============================================================= 5. the pacer
{
  const toasts = fakeToasts();
  const c = createCoach({ prefs: fakeStore(), toasts, logger: quiet });
  c.fire(COACH.POP, 'one');
  c.fire(COACH.DROP, 'two');
  c.fire(COACH.FIRED, 'three');
  ok(toasts.shown.length === 1, 'a clump of firsts shows ONE line immediately', String(toasts.shown.length));
  ok(c.pending === 2, 'and queues the rest', String(c.pending));
  ok(c.seen(COACH.FIRED) === true, 'every one of them is already marked, queued or not');
  await tick(COACH_GAP_MS + 120);
  ok(toasts.shown.length === 2, 'the second lands a gap later', String(toasts.shown.length));
  await tick(COACH_GAP_MS + 120);
  ok(toasts.shown.length === 3, 'and the third after that', String(toasts.shown.length));
  ok(c.pending === 0, 'the queue drains to empty');
  c.dispose();
}

// The queue has a ceiling, and hitting it still spends the hint.
{
  const toasts = fakeToasts();
  const c = createCoach({ prefs: fakeStore(), toasts, logger: quiet });
  const ids = COACH_IDS.slice(0, COACH_QUEUE_MAX + 3);
  for (const id of ids) c.fire(id, 'line ' + id);
  ok(c.pending <= COACH_QUEUE_MAX,
    'the queue never grows past COACH_QUEUE_MAX — a line a minute late is worse than no line', String(c.pending));
  ok(ids.every((id) => c.seen(id)), 'and every id offered is spent, shown or dropped');
  c.dispose();
}

// A teardown clears what is queued and keeps every mark.
{
  const toasts = fakeToasts();
  const prefs = fakeStore();
  const c = createCoach({ prefs, toasts, logger: quiet });
  c.fire(COACH.POP, 'one');
  c.fire(COACH.DROP, 'two');
  c.clearPending();
  ok(c.pending === 0, 'clearPending empties the queue');
  await tick(COACH_GAP_MS + 120);
  ok(toasts.shown.length === 1, 'so nothing arrives over the recap of a match that already ended');
  ok(c.seen(COACH.DROP) === true, 'and the cleared hint stays spent — it was offered');
  c.dispose();
}

// Switching OFF drops what has not been said yet.
{
  const toasts = fakeToasts();
  const prefs = fakeStore();
  const c = createCoach({ prefs, toasts, logger: quiet });
  c.fire(COACH.POP, 'one');
  c.fire(COACH.DROP, 'two');
  c.setEnabled(false);
  ok(prefs.values[COACH_PREF_ENABLED] === false, 'setEnabled writes the pref');
  await tick(COACH_GAP_MS + 120);
  ok(toasts.shown.length === 1, 'and a queued line does not arrive after the switch was thrown');
  c.dispose();
}

// dispose() is idempotent and stops the pump.
{
  const toasts = fakeToasts();
  const c = createCoach({ prefs: fakeStore(), toasts, logger: quiet });
  c.fire(COACH.POP, 'one');
  c.fire(COACH.DROP, 'two');
  c.dispose();
  c.dispose();
  ok(c.fire(COACH.CHECK, 'x') === false, 'a disposed coach accepts nothing');
  await tick(COACH_GAP_MS + 120);
  ok(toasts.shown.length === 1, 'and never drains after teardown', String(toasts.shown.length));
}

// ===================================================== 6. the copy IS the code
{
  const c = S.coach;
  ok(c && typeof c === 'object', 'S.coach exists');
  for (const key of ['goal', 'pop', 'fired', 'check', 'dial', 'emote', 'practice', 'hints', 'hintsNote', 'howGoal']) {
    ok(typeof c[key] === 'string' && c[key].length > 0, 'S.coach.' + key + ' is a non-empty string');
  }
  ok(typeof c.drop === 'function' && typeof c.incoming === 'function',
    'the two interpolated lines are FUNCTIONS, so strings.js stays import-safe');
  ok(c.drop('flash', 1).indexOf('flash') >= 0 && c.drop('flash', 1).indexOf('1') >= 0,
    'S.coach.drop names the item and its key');
  ok(c.drop().length > 0 && c.incoming().length > 0, 'and both still read as sentences with nothing to name');
  ok(c.incoming('flash burst').indexOf('flash burst') >= 0, 'S.coach.incoming names the family that just landed');

  // --- the attention check: twelve seconds, then x0.6 for a minute ----------
  const attSrc = read('ui/attention.js');
  const grace = /const GRACE_MS = (\d+);/.exec(attSrc);
  ok(grace && Number(grace[1]) === 12000,
    'ui/attention.js GRACE_MS is still 12 s — S.coach.check says "twelve seconds"', grace ? grace[1] : 'missing');
  ok(/twelve seconds/.test(c.check), 'and the copy says so');
  ok(GoonConsts.NoCamFailedCheckMult === 0.6, 'the failed-check multiplier is still 0.6');
  ok(/x0\.6/.test(c.check), 'and the copy says x0.6');
  ok(GoonConsts.NoCamFailedCheckPenaltyMs === 60000, 'the penalty is still one minute');
  ok(/minute/.test(c.check), 'and the copy says a minute');

  // --- the cooldown: two back to back, then one every thirty seconds --------
  ok(GoonConsts.PayloadMinGapMs === 30000, 'PayloadMinGapMs is still 30 s');
  ok(/thirty seconds/.test(c.fired), 'and S.coach.fired says thirty seconds');
  ok(GoonConsts.PayloadBurst === 2, 'PayloadBurst is still 2');
  ok(/^two /.test(c.fired), 'and S.coach.fired opens with the burst');

  // --- the practice seed: flash on key 1, video on key 3 -------------------
  const bootSrc = read('boot.js');
  const seedBlock = /const PRACTICE_SEED = Object\.freeze\(\[([\s\S]*?)\]\);/.exec(bootSrc);
  ok(!!seedBlock, 'boot.js still has a PRACTICE_SEED table');
  const seedIds = seedBlock ? Array.from(seedBlock[1].matchAll(/id: '([a-z]+)'/g)).map((m) => m[1]) : [];
  ok(seedIds.join(',') === 'flash,video',
    'practice still seeds flash + video — S.coach.practice names exactly those two', seedIds.join(','));
  const slots = ARSENAL_ITEMS.filter((i) => i.kind !== null).map((i) => i.id);
  ok(slots.indexOf('flash') === 0, 'flash is still key 1', String(slots.indexOf('flash') + 1));
  ok(slots.indexOf('video') === 2, 'video is still key 3', String(slots.indexOf('video') + 1));
  ok(/flash/.test(c.practice) && /video/.test(c.practice), 'and the copy names both');
  ok(/keys 1 and 3/.test(c.practice), 'and quotes the two keys that actually fire them');

  // --- the three things the copy may NEVER say -----------------------------
  const allCopy = Object.keys(c)
    .map((k) => (typeof c[k] === 'function' ? c[k]('x', 1) : c[k]))
    .join(' | ')
    .toLowerCase();
  ok(!/charge/.test(allCopy),
    'NOTHING in S.coach mentions charges — the meter still exists and still buys nothing (2026-08-05)');
  ok(!/camera|webcam|gaze/.test(allCopy),
    'and nothing mentions a camera — localAttentionMode is hard-wired NoCam, the Cam branch is unreachable');
  ok(!/sudden death/.test(allCopy),
    'and nothing promises sudden death — the runner is detached (boot.js), the phase resolves on score in one tick');
  ok(!/damage|hurt(s)? you|costs you .*point/.test(allCopy),
    'and no payload is described as taking points: nothing inbound touches score, multiplier, heat or cooldown');
  ok(!/[!]/.test(allCopy), 'no exclamation marks — the announcer is the only raised voice on this page');
}

// ============================== 7. every hint has a call site, and vice versa
{
  const sources = {
    'ui/hud.js': stripComments(read('ui/hud.js')),
    'ui/attention.js': stripComments(read('ui/attention.js')),
    'ui/closeness.js': stripComments(read('ui/closeness.js')),
    'boot.js': stripComments(read('boot.js')),
  };
  const all = Object.keys(sources).map((k) => sources[k]).join('\n');

  const expect = {
    POP: 'ui/hud.js',
    DROP: 'ui/hud.js',
    FIRED: 'ui/hud.js',
    INCOMING: 'ui/hud.js',
    EMOTE: 'ui/hud.js',
    CHECK: 'ui/attention.js',
    DIAL: 'ui/closeness.js',
    PRACTICE: 'boot.js',
  };
  for (const name of Object.keys(expect)) {
    const file = expect[name];
    ok(new RegExp('COACH\\.' + name + '\\b').test(sources[file]),
      'COACH.' + name + ' is fired from ' + file);
  }
  // The other direction: an id nothing fires can never be spent and is dead weight.
  for (const key of Object.keys(COACH)) {
    ok(new RegExp('COACH\\.' + key + '\\b').test(all), 'every id in COACH has a caller — ' + key);
  }

  // Nothing coached may block. The module reaches for one method on one tier.
  const coachSrc = stripComments(read('ui/coach.js'));
  ok(/toasts\s*&&\s*typeof toasts\.show === 'function'/.test(coachSrc),
    'ui/coach.js paints through ui/toasts.js and guards the handle');
  ok(!/sheets|openNode|showSignalError|gg-modal|gg-scrim/.test(coachSrc),
    'and never opens a sheet, a modal or a scrim — a coached line can never block a live match');
  ok(!/document|window|createElement/.test(coachSrc),
    'and touches no DOM at all: the toast tier owns every node');

  // Every fire in the tree goes through a guarded optional call, so a null coach
  // (a HUD built without one) is an uncoached desk and not a crash.
  const fires = Array.from(all.matchAll(/coach[?.]*\.fire\?*\./g));
  ok(fires.length >= 8, 'there are at least eight coached beats wired up', String(fires.length));
  ok(!/[^?]\bcoach\.fire\(/.test(all),
    'and every one of them is an OPTIONAL call — a desk with no coach is uncoached, never broken');
}

// ===================================================== 8. the switch, and the fence
{
  const optSrc = stripComments(read('ui/options.js'));
  ok(/S\.coach\.hints\b/.test(optSrc) && /'coachHints'/.test(optSrc),
    'the options drawer offers the hints toggle');
  ok(/S\.coach\.hintsNote/.test(optSrc), 'with a note saying what OFF means');

  const hudSrc = read('ui/hud.js');
  ok(/S\.coach\.goal/.test(hudSrc), 'the desk carries the objective caption');
  const cssSrc = read('ui/hud.css');
  ok(/gg-hud--zen \.gg-coach-goal/.test(cssSrc),
    'and zen hides it with the dial it captions — a caption for an absent readout is a bug');
  ok(/\.gg-coach-goal[\s\S]{0,400}pointer-events: none/.test(cssSrc),
    'the caption is pointer-transparent — it may never park a dead box over the bubble field');

  const titleSrc = read('ui/screens/title.js');
  ok(/S\.coach\.howGoal/.test(titleSrc), 'the how-it-works card leads with the goal');
  ok(S.how.bullets.length === 6, 'and the six bullets under it are still six', String(S.how.bullets.length));

  // --- the parallel-PR fence (see the header) ------------------------------
  const strSrc = read('ui/strings.js');
  ok(/S\.coach|coach: \{/.test(strSrc), 'S.coach lives in ui/strings.js with the rest of the deck');
  ok(/lobbyRelay:/.test(strSrc) && /holdKeyHint:/.test(strSrc),
    'and the S.voice block is intact — a parallel PR owns it');
  ok(/function reportMicGate\(\)/.test(read('boot.js')), 'boot.js reportMicGate is untouched — same reason');
}

// -------------------------------------------------------------------- report
if (failures) {
  console.error(`\nselftest-coach: ${n - failures}/${n} checks passed`);
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
console.log(`selftest-coach: ${n}/${n} checks passed`);
