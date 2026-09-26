/* ============================================================================
 * ui/duel/duelController.js - game night duels, end to end on one side.
 *
 * A GAME CARD drops from bubble pops (ui/drops.js rolls it, ui/arsenal.js holds
 * it). Throwing it sends `t:'duel' sub:'start'` naming the game the card holds
 * (ui/duel/games.js); both screens show a short "incoming game" card, then the
 * REAL Arcademy class (ui/duel/arcademyHost.js, started through `runGame`) on
 * the same duel seed for the duel length. At the end each side sends
 * `sub:'score'` with the class's own result, waits a grace window for theirs
 * and computes the same winner from the same numbers. The winner adds DUEL_WIN_BONUS to its own match score through
 * GoonScoring.awardBonus, which rides the next state tick. A report that never
 * arrives makes the duel a TIE: nobody wins a bonus off a dropped frame.
 *
 * THE RECEIVER DECIDES WHETHER A DUEL HAPPENS. An inbound start is refused
 * unless it is the expected next idx, the receiver is past its first finished
 * match (practice always is: the bot plays duels), no duel or result hold is running, the gap after
 * the last duel (its length + 30 s) has passed and the match has had fewer than
 * DUEL_MAX_PER_MATCH duels. A refusal answers `sub:'busy'`; the thrower cancels
 * its local duel, rolls its counter back and gets the card back.
 *
 * WHAT A DUEL NEVER TOUCHES: Mercy (z60, above this overlay, and a phase change
 * out of Live closes the duel with no bonus), panic, lockdown, session state.
 * The ramp keeps running underneath; locks and throws pause while the board is open.
 *
 * THE LENGTH: the host's pick wins. The host sends `sub:'cfg'` at Live (the
 * engine keeps it as match.peerDuelLen) and every start carries len_s.
 *
 * SIMULTANEOUS THROWS: both sides number duels from the same counter, so two
 * cards thrown at once carry the same idx. The host's start wins: the host
 * ignores the guest's, the guest adopts the host's length and keeps playing.
 *
 * PER MATCH, NOT PER MOUNT: the counter, the gap and the recap tally live in a
 * WeakMap keyed on the match object, so a HUD remount mid-match changes nothing.
 *
 * THE SORT DUEL'S NOISE BOARD (2026-09-25, night revision 2). When the card holds Sort and the
 * peer's build picks noise (match.peerPicksNoise), the intro card becomes a VS reveal of both
 * players' niches and noise boards (NOISE_REVEAL_MS), then the class. The board was picked
 * BEFORE the match (owner: "right after the select the niche, not in the sort game"), on the
 * pre-match step ui/screens/noiseSetup.js, and reaches this controller as `chosenNoise()`. A
 * player who never chose gets a roll made when the duel begins. Each side sends
 * `sub:'noise' {idx, set}` at begin so the other screen names it; the wire is unchanged, and a
 * revision 2 peer from before this change (which still picks in the duel, about 6 s) lands its
 * pick late and its score inside the report grace. Each side runs the reveal on its OWN clock
 * from its own begin(), exactly as the intro card always ran. The class then deals the
 * player's niche as the right pile and their noise board as the left. A peer on revision 1
 * never sees any of it: Sort plays the old way (moving vs still).
 *
 * Node-import-safe: the DOM lives in ui/duel/duelView.js, injected.
 * ==========================================================================*/

import { GoonMatchPhase } from '../../core/contracts.js';
import { setDuelFieldActive } from '../../core/duelActivity.js';
import {
  DUEL_INTRO_MS, DUEL_MIN_FINISHED, DUEL_REPORT_GRACE_MS, bonusFor, cardEligible, duelOutcome, duelSeed, pickLength,
} from './rules.js';
import { duelGame, knownGame, normalizeGameId, pickDuelGame } from './games.js';
import { seedToString } from '../../core/rng.js';
import { getDuelLength } from '../screens/customize.js';
import { finishedMatches } from '../nightProgress.js';
import { NOISE_REVEAL_MS, clampNoiseSet, rollNoiseSet } from '../../core/noiseSets.js';
import { cleanNiches, peerNiches } from '../../core/contracts.js';
import { nicheLabel } from './noisePick.js';

const HINT_KEY = 'goon.night.cardHint.v1';
const RESULT_HOLD_MS = 1200;
/** Quiet time after a duel closes before the next may start, on top of that duel's length. */
export const DUEL_GAP_EXTRA_MS = 30000;
/** Duels one match may hold. */
export const DUEL_MAX_PER_MATCH = 5;
/** The stages before the class is up: a 'busy' answer can still take the throw back in these. */
const PRE_PLAY = new Set(['intro', 'reveal']);

/* ---- per-match state: survives HUD remounts, read by the recap after the HUD is gone ---- */
const perMatch = new WeakMap();
const orphanKey = {};
function stateOf(match) {
  const key = match && typeof match === 'object' ? match : orphanKey;
  let s = perMatch.get(key);
  if (!s) {
    s = { nextIdx: 0, started: 0, notBefore: 0, summary: { won: 0, lost: 0, tied: 0 } };
    perMatch.set(key, s);
  }
  return s;
}

/** Duel tally of one match. Read by ui/screens/recap.js. */
export function duelSummary(match) { return Object.assign({}, stateOf(match).summary); }

/** First game card ever: true exactly once per device. */
export function takeFirstCardHint() {
  try {
    if (typeof localStorage === 'undefined') return false;
    if (localStorage.getItem(HINT_KEY)) return false;
    localStorage.setItem(HINT_KEY, '1');
    return true;
  } catch (_e) { return false; }
}

/**
 * @param {object} o
 * @param {object} o.match     GoonMatchService
 * @param {object} [o.view]    ui/duel/duelView.js handle (null = headless)
 * @param {object} [o.audio]
 * @param {Function} [o.onLog]
 * @param {Function} [o.now]   ms clock (tests)
 * @param {Function} [o.later] (fn, ms) => cancel (tests)
 * @param {Function} [o.finished] finished-match count (tests)
 * @param {Function} [o.duelLength] this player's Customize pick (tests)
 * @param {Function} [o.isPractice] true in practice: the card drops from the first match (the bot plays)
 * @param {object} [o.noise]   the noise boards' pictures: {requestNoise(id), listNoise(id) -> rows}
 *   (exec/media.js). Absent = nothing to fetch (headless tests); the reveal still runs.
 * @param {Function} [o.chosenNoise] () => set id: the board picked before the match
 *   (ui/screens/noiseSetup.js, kept in prefs). '' = never chosen: the duel rolls one.
 * @param {Function} [o.rand]  () => [0,1): the roll for a player who never chose
 * @param {Function} [o.runGame] ({game, seed, len, onEnd}) => handle | Promise<handle> | null, where
 *   handle = {result(): {game, score, tile?}, destroy()}. Default: the view's startGame (the real
 *   Arcademy class). Tests pass a fake.
 */
export function createDuelController({
  match, view = null, audio = null, onLog = null, runGame = null,
  now = () => Date.now(),
  later = (fn, ms) => { const t = setTimeout(fn, ms); return () => clearTimeout(t); },
  finished = finishedMatches,
  duelLength = getDuelLength,
  isPractice = () => false,
  noise = null,
  chosenNoise = () => '',
  rand = Math.random,
} = {}) {
  const ms = stateOf(match);
  let cur = null;            // {idx, len, game, seed, run, mine, stage, by, cancels[], noise}
  let returnCard = null;     // set by the HUD once the arsenal exists
  const peerScores = new Map();
  const peerNoise = new Map();   // idx -> their set, when their frame lands before our duel does
  const unsubs = [];

  const log = (e) => { try { if (typeof onLog === 'function') onLog(e); } catch (_e) { /* never */ } };
  const sfx = (id) => { try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ } };
  const v = (name, ...args) => { try { if (view && typeof view[name] === 'function') view[name](...args); } catch (_e) { /* the view is never load-bearing */ } };
  const practice = () => { try { return !!isPractice(); } catch (_e) { return false; } };

  function isLive() { return !!match && match.phase === GoonMatchPhase.Live; }
  function busy() { return !!cur; }
  function blocksThrows() { return !!cur && (PRE_PLAY.has(cur.stage) || cur.stage === 'play'); }
  function myLen() {
    if (match && match.isHost) return pickLength(duelLength());
    return pickLength(match && match.peerDuelLen);
  }
  /**
   * Finished matches as the gates see them. PRACTICE counts as past the first match (owner,
   * 2026-09-23: "make in practice the minigame happen so i test"): the bot (ui/duel/botDuel.js)
   * speaks night over the loopback, so the card drops from the very first practice match.
   * Against a real peer the rule is unchanged: the second finished match.
   */
  function seen() { return practice() ? Math.max(finished(), DUEL_MIN_FINISHED) : finished(); }

  /** The rules both sides apply before a duel may begin (not counting "am I busy"). */
  function roomForDuel() {
    return seen() >= DUEL_MIN_FINISHED && ms.started < DUEL_MAX_PER_MATCH && now() >= ms.notBefore;
  }

  function at(delay, fn) {
    const cancel = later(() => { if (cur) fn(); }, delay);
    if (cur) cur.cancels.push(cancel);
  }

  /** The class runner: an injected one (tests), else the view's (the real Arcademy class). */
  const duck = (on) => { try { if (audio && typeof audio.duelDuck === 'function') audio.duelDuck(on); } catch (_e) { /* stub */ } };

  function startRun(spec) {
    duck(true);
    try {
      if (typeof runGame === 'function') return runGame(spec);
      if (view && typeof view.startGame === 'function') return view.startGame(spec);
    } catch (e) { log({ t: 'duel-run-failed', why: String((e && e.message) || e) }); }
    return null;
  }

  function dropRun(c) {
    if (!c || !c.run) return;
    const r = c.run;
    c.run = null;
    try { r.destroy(); } catch (_e) { /* a class that will not die must not keep the duel */ }
  }

  /** A Sort card between two builds that both pick noise. */
  function noiseDuel(id) { return id === 'sort' && !!(match && match.peerPicksNoise); }

  const wantNoise = (set) => {
    try { if (set && noise && typeof noise.requestNoise === 'function') noise.requestNoise(set); } catch (_e) { /* pictures are a nicety */ }
  };
  const noiseRows = (set) => {
    try {
      const r = set && noise && typeof noise.listNoise === 'function' ? noise.listNoise(set) : [];
      return Array.isArray(r) ? r : [];
    } catch (_e) { return []; }
  };

  function begin(idx, len, by, game) {
    const id = normalizeGameId(game);
    cur = { idx, len: pickLength(len), game: id, seed: '', run: null, by, mine: null, stage: 'intro', cancels: [], endsAt: 0, noise: null };
    setDuelFieldActive(match, true);
    ms.nextIdx = Math.max(ms.nextIdx, idx + 1);
    ms.started++;
    cur.seed = 'goon-duel|' + seedToString(duelSeed(match && match.matchSeed, idx));
    sfx('gg-fire');
    const row = duelGame(id);
    log({ t: 'duel-start', idx, len: cur.len, by, game: id });
    if (noiseDuel(id)) { beginNoise(row); return; }
    v('intro', { by, len: cur.len, game: id, name: row ? row.name : id, rule: row ? row.rule : '', hint: takeFirstCardHint() });
    at(DUEL_INTRO_MS, startPlay);
  }

  /** The board picked before the match, or '' (never chosen, or a junk pref). */
  const chosen = () => { try { return clampNoiseSet(chosenNoise()); } catch (_e) { return ''; } };

  /* ---- the Sort duel's VS reveal (see the header) ---- */
  function beginNoise(row) {
    const c = cur;
    const pre = chosen();
    const mine = pre || rollNoiseSet(rand);
    c.noise = { mine, theirs: clampNoiseSet(peerNoise.get(c.idx)), rolled: !pre };
    peerNoise.delete(c.idx);
    // Already fetching since the pre-match pick (a no-op then); a roll starts fetching now.
    wantNoise(mine);
    if (match) match.sendDuel({ sub: 'noise', idx: c.idx, set: mine });
    log({ t: 'duel-noise', idx: c.idx, set: mine, rolled: c.noise.rolled });
    c.stage = 'reveal';
    const you = nicheLabel(cleanNiches(match && match.localCaps && match.localCaps.niches));
    const them = practice() ? nicheLabel([], 'bot') : nicheLabel(peerNiches(match && match.remoteCaps));
    v('reveal', {
      by: c.by, game: c.game, name: row ? row.name : c.game, you, them,
      youNoise: mine, themNoise: c.noise.theirs, hint: takeFirstCardHint(),
    });
    sfx('gg-go');
    at(NOISE_REVEAL_MS, startPlay);
  }

  function startPlay() {
    if (!cur || cur.stage === 'play' || cur.stage === 'wait' || cur.stage === 'result') return;
    const id = cur.game;
    cur.stage = 'play';
    cur.endsAt = now() + cur.len * 1000;
    v('play', { game: id, secondsLeft: cur.len });
    const mine = cur;
    // The class may ring its own bell first (a Deep End ceiling ends the class early): its end is
    // our end. The duel clock still wins when the class runs longer.
    const spec = { game: id, seed: cur.seed, len: cur.len, onEnd: () => { if (cur === mine) endPlay(); } };
    if (mine.noise && mine.noise.mine) {
      // The board, read live: pictures that land after the bell still join the left pile.
      const set = mine.noise.mine;
      spec.noise = { set, rows: () => noiseRows(set) };
    }
    const adopt = (h) => {
      if (!h) return;
      if (cur !== mine || mine.stage !== 'play') { try { h.destroy(); } catch (_e) { /* gone */ } return; }
      mine.run = h;
    };
    const r = startRun(spec);
    if (r && typeof r.then === 'function') r.then(adopt, (e) => log({ t: 'duel-run-failed', why: String((e && e.message) || e) }));
    else adopt(r);
    tick();
  }

  function tick() {
    if (!cur || cur.stage !== 'play') return;
    const left = Math.ceil((cur.endsAt - now()) / 1000);
    if (left <= 0) { endPlay(); return; }
    v('timer', left);
    at(250, tick);
  }

  /** This side's result: the class's own, read at the bell. No class (it never loaded) = zero. */
  function readMine(c) {
    const row = duelGame(c.game);
    let r = null;
    try { r = c.run ? c.run.result() : null; } catch (_e) { r = null; }
    const out = { game: c.game, score: Math.max(0, Math.round(Number(r && r.score) || 0)) };
    if (!row || row.tiled) out.tile = Math.max(0, Math.round(Number(r && r.tile) || 0));
    return out;
  }

  function endPlay() {
    if (!cur || cur.stage !== 'play') return;
    cur.stage = 'wait';
    cur.mine = readMine(cur);
    dropRun(cur);
    duck(false);
    if (match) match.sendDuel({ sub: 'score', idx: cur.idx, game: cur.game, score: cur.mine.score, tile: cur.mine.tile | 0 });
    v('waiting');
    setDuelFieldActive(match, false);
    if (peerScores.has(cur.idx)) { resolve(); return; }
    at(DUEL_REPORT_GRACE_MS, resolve);
  }

  function resolve() {
    if (!cur || cur.stage !== 'wait') return;
    cur.stage = 'result';
    const theirs = peerScores.get(cur.idx) || null;
    // No report inside the grace window is a TIE, never a forfeit: a dropped frame (or a peer
    // that stopped talking) must not hand anybody the bonus.
    const outcome = theirs ? duelOutcome(cur.mine, theirs) : 'tie';
    // Points model (core/points.js): a pot, winner takes it, loser and each side of a tie half.
    // A legacy match (older peer) keeps the flat win bonus.
    const pot = match && typeof match.noteDuel === 'function' ? match.noteDuel(outcome) : null;
    const bonus = pot ? Math.round(pot.points) : bonusFor(outcome);
    if (!pot && bonus > 0 && match && match.scoring && typeof match.scoring.awardBonus === 'function') match.scoring.awardBonus(bonus);
    if (outcome === 'win') ms.summary.won++; else if (outcome === 'lose') ms.summary.lost++; else ms.summary.tied++;
    sfx(outcome === 'win' ? 'gg-endured' : 'gg-drop-dud');
    v('result', { outcome, bonus, pot: !!pot, game: cur.game, mine: cur.mine, theirs: theirs || { game: cur.game, tile: 0, score: 0 } });
    log({ t: 'duel-end', idx: cur.idx, outcome, bonus, reported: !!theirs });
    at(RESULT_HOLD_MS, close);
  }

  /** @param {boolean} [played] false = cancelled before it counted (a 'busy' answer): no gap. */
  function close(played = true) {
    if (!cur) return;
    for (const c of cur.cancels) { try { c(); } catch (_e) { /* gone */ } }
    dropRun(cur);
    duck(false);
    peerScores.delete(cur.idx);
    peerNoise.delete(cur.idx);
    if (played) ms.notBefore = now() + cur.len * 1000 + DUEL_GAP_EXTRA_MS;
    cur = null;
    v('close');
    setDuelFieldActive(match, false);
  }

  function refuse(idx, why) {
    log({ t: 'duel-refused', idx, why });
    if (match) match.sendDuel({ sub: 'busy', idx });
  }

  function onStart(f) {
    // An idx collision (both threw at once): the host's start wins.
    // Same idx at any stage is the same duel (a slow link can deliver it after our intro), never
    // a refusal; the length is only adopted while the intro still hides the clock.
    if (cur && cur.idx === f.idx) {
      if (match && !match.isHost && PRE_PLAY.has(cur.stage)) {
        cur.len = pickLength(f.len_s);
        log({ t: 'duel-collision', idx: f.idx, len: cur.len });
      }
      return;   // host: the guest's start yields, the guest adopts ours
    }
    if (!isLive()) return;
    if (cur) { refuse(f.idx, cur.stage === 'result' ? 'result-hold' : 'busy'); return; }
    if (f.idx !== ms.nextIdx) { refuse(f.idx, 'idx'); return; }
    // A game this build does not have: refuse, the thrower gets its card back.
    if (!knownGame(f.game)) { refuse(f.idx, 'game'); return; }
    if (!roomForDuel()) { refuse(f.idx, 'gated'); return; }
    begin(f.idx, f.len_s, 'them', f.game);
  }

  function onBusy(f) {
    // Only OUR throw, and only before anything was played, can be taken back.
    if (!cur || cur.idx !== f.idx || cur.by !== 'you' || !PRE_PLAY.has(cur.stage)) return;
    log({ t: 'duel-cancelled', idx: f.idx });
    ms.nextIdx = f.idx;
    ms.started = Math.max(0, ms.started - 1);
    close(false);
    if (typeof returnCard === 'function') { try { returnCard(); } catch (_e) { /* the card is a nicety */ } }
  }

  function onFrame(f) {
    if (!f) return;
    if (f.sub === 'cfg') return;   // kept by the engine (match.peerDuelLen)
    if (f.sub === 'score') {
      peerScores.set(f.idx, { game: normalizeGameId(f.game), tile: f.tile, score: f.score });
      if (cur && cur.idx === f.idx && cur.stage === 'wait') resolve();
      return;
    }
    if (f.sub === 'start') { onStart(f); return; }
    if (f.sub === 'noise') { onNoise(f); return; }
    if (f.sub === 'busy') onBusy(f);
  }

  /** Their board for a Sort duel. Shown only: each side deals its OWN noise. A late one (an older
   *  revision 2 build still picks inside the duel) lands on whatever is showing, or nowhere. */
  function onNoise(f) {
    const set = clampNoiseSet(f.set);
    if (!set) return;
    if (!cur || cur.idx !== f.idx || !cur.noise) {
      // Early (a slow link delivered their pick before our duel began): kept for begin().
      if (f.idx >= ms.nextIdx - 1) peerNoise.set(f.idx, set);
      return;
    }
    if (cur.noise.theirs) return;   // one board per side
    cur.noise.theirs = set;
    v('picked', { who: 'them', set, rolled: false });
  }

  function onPhase(p) {
    if (p === GoonMatchPhase.Live) {
      if (match && match.isHost) match.sendDuel({ sub: 'cfg', len_s: myLen() });
      return;
    }
    // Leaving Live (mercy, sudden death, recap, teardown) ends a running duel. No bonus.
    if (cur) { log({ t: 'duel-abort', idx: cur.idx }); close(); }
  }

  if (match && typeof match.onDuelFrame === 'function') unsubs.push(match.onDuelFrame(onFrame));
  if (match && typeof match.onPhaseChanged === 'function') unsubs.push(match.onPhaseChanged(onPhase));
  if (isLive()) onPhase(GoonMatchPhase.Live);
  // The pre-match board: ask for its pictures at the start of every match (the store forgets
  // every board when a match ends), so the Sort pile is there when a card drops.
  wantNoise(chosen());
  v('bind', { endNow: () => endPlay() });

  return {
    busy,
    /** The HUD hands the arsenal's re-arm here once it exists (a refused throw returns the card). */
    setReturnCard(fn) { returnCard = typeof fn === 'function' ? fn : null; },
    /** The arsenal's seam for the game card slot. */
    arsenalHook: {
      eligible(held) {
        return cardEligible({
          peerNight: !!(match && match.peerSupportsNight),
          finished: seen(),
          inDuel: busy(),
          held,
        }) && roomForDuel();
      },
      /** Show the slot at all: the peer speaks night, the player's second match (any practice match). */
      visible() { return !!(match && match.peerSupportsNight) && seen() >= DUEL_MIN_FINISHED; },
      busy,
      blocksThrows,
      /** @param {{game?:string}} [o] a card that already names its game (a known id) keeps it. */
      throwCard(o) {
        if (busy() || !isLive() || !roomForDuel()) return false;
        if (!match || !match.peerSupportsNight) return false;
        const idx = ms.nextIdx;
        const len = myLen();
        // Begin FIRST, then send: a 'busy' answer that comes back fast must find the duel it cancels.
        // The card's game: deterministic in (match seed, idx), named in the frame so both sides agree.
        const game = o && o.game && knownGame(o.game) ? String(o.game) : pickDuelGame(match.matchSeed, idx);
        begin(idx, len, 'you', game);
        if (!match.sendDuel({ sub: 'start', idx, len_s: len, game })) {
          if (cur && cur.idx === idx) { ms.nextIdx = idx; ms.started = Math.max(0, ms.started - 1); close(false); }
          return false;
        }
        return true;
      },
      firstHint: () => takeFirstCardHint(),
    },
    /** Test seam. */
    get state() {
      if (!cur) return null;
      const s = { idx: cur.idx, len: cur.len, stage: cur.stage, by: cur.by, game: cur.game, seed: cur.seed };
      if (cur.noise) s.noise = { mine: cur.noise.mine, theirs: cur.noise.theirs, rolled: cur.noise.rolled };
      return s;
    },
    get run() { return cur ? cur.run : null; },
    get game() { return cur ? cur.game : null; },
    get nextIdx() { return ms.nextIdx; },
    endNow() { endPlay(); },
    dispose() {
      close();
      for (const u of unsubs) { try { if (typeof u === 'function') u(); } catch (_e) { /* gone */ } }
      unsubs.length = 0;
      v('dispose');
    },
  };
}

export default createDuelController;
