/* ============================================================================
 * stations/cards/feel.js - Soft Hand feel, pure (CONTRACT 10.13.F, the v3
 * mockup's timings).
 *
 *   planSteps   a reply's hand against the hand already on the felt -> the
 *               timed steps the station plays (cards out of the shoe, the
 *               split, the hole card turning, the dealer's draws, the bloom
 *               frame, the settle frame)
 *   momentOf    a settled hand -> cards.win | cards.lose | cards.push
 *   settleMoment the settle beat with its size: sweep, streak, dealer_bust, win, lose, push
 *   streakAfter, mayFire, wordKeys   the win streak, the beat cooldowns, which dealt words a whisper takes
 *   isBloom     a paid player blackjack (the bloom moment)
 *   bestCard    the highest-value card (ace highest) in the player's winning hands
 *   vortexOf    the chip vortex: direction and chip count
 *   resultLines the text readout for every result, as lexicon keys
 *   fanCard, lampBreath   the sit fan and the breathing lamp, as numbers
 *   screenHoldMs how long a fired moment keeps something fullscreen, so the
 *               next deal waits it out
 *
 * No rules and no randomness: every outcome is the server's.
 * ==========================================================================*/

import { cardValue, totalOf } from './hand.js';
import { MOMENTS } from '../../shared/hypno/moments.js';
import { FX_DELAY_MS, CALLOUT_MS } from '../../shared/hypno/callout.js';

export const TIMING = Object.freeze({
  firstMs: 100,        // press -> first card leaves the shoe
  dealGapMs: 400,      // the first round, card to card
  hitGapMs: 800,       // later player cards, dealer draws
  flyMs: 500,          // shoe -> felt
  flipMs: 420,         // a card turning over
  splitMs: 450,        // the pair sliding apart
  revealMs: 1000,      // last player card -> the hole card turns
  bjRevealMs: 1600,    // after a bloom, the hole card waits longer
  settleMs: 900,       // last dealer card -> the result shows
  sitMs: 4400,         // the sit fan, travel
  sitStillMs: 2400,    // the sit fan, settled (Calm, reduced)
  rippleMs: 3200,
  vortexMs: 2000,
  tunnelMs: 3000,
  glowMs: 1400,
  lampPeriodMs: 10000, // six breaths a minute
  bloomMs: 4000,       // fx.gif_from on the ace: Deal waits this out
  washMs: 900,         // the host wash (10.13.B): gone at 900 ms
  edgesTailMs: 150,    // after the losing breath: the last fx-tunnel 0 waits out the 100 ms post gap
  streakMs: 1700,      // fx.sub_pair at Normal: two words at 220 ms, then the 1.2 s spiral
  sweepMs: 2000,       // fx.gif_storm at Normal: the 2 s gif rain
});

/** The beats between the deal and the settle, and how often each may fire (ms): a whisper per hit at most every 1.2 s. */
export const COOLDOWN_MS = Object.freeze({ 'cards.hit': 1200 });
export const mayFire = (id, lastAt, now) => !(Number.isFinite(lastAt)) || now - lastAt >= (COOLDOWN_MS[id] || 0);
/** `n` dealt word keys from a running cursor over the four words (s0..s3), so the whispers rotate. */
export const wordKeys = (n, cursor = 0) => Array.from({ length: Math.max(0, Math.min(4, n | 0)) }, (_, i) => 's' + ((((cursor | 0) + i) % 4) + 4) % 4);
/** Wins in a row after this settled hand: a win adds one, a loss ends it, a push keeps it. */
export function streakAfter(streak, hand) {
  const s = Math.max(0, streak | 0), net = hand && hand.result ? hand.result.net : 0;
  return net > 0 ? s + 1 : net < 0 ? 0 : s;
}
export const STREAK_FROM = 3;

/* ---------------------------------------------------------------------------------------------
 * THE CALLOUT (shared/hypno/callout.js, owner 2026-09-15). A settled win names itself at FX_DELAY_MS with
 * its host fx, after the winning cards glowed (winningCards, HIGHLIGHT_GAP_MS apart in dealt order); a
 * blackjack names itself at its bloom, so its plain settle (cards.win) says nothing more. A push and a
 * loss get no callout. The next deal waits WIN_HOLD_MS from the frame the result showed.
 * ------------------------------------------------------------------------------------------ */
export const WIN_HOLD_MS = FX_DELAY_MS + CALLOUT_MS;
export const CALLOUTS = Object.freeze({
  'cards.win': { key: 'br_callout_winner', fallback: 'Winner', tier: 'small' },
  'cards.bloom': { key: 'br_callout_blackjack', fallback: 'Blackjack', tier: 'big' },
  'cards.dealer_bust': { key: 'br_callout_dealer_bust', fallback: 'Dealer Bust', tier: 'small' },
  'cards.streak': { key: 'br_callout_hot_hand', fallback: 'Hot Hand', tier: 'big' },
  'cards.sweep': { key: 'br_callout_sweep', fallback: 'Sweep', tier: 'hero' },
});
/** The callout for a moment id, or null. `bloomed`: this hand already said Blackjack, so a plain win says nothing. */
export function calloutFor(id, { bloomed = false } = {}) {
  if (id === 'cards.win' && bloomed) return null;
  return Object.prototype.hasOwnProperty.call(CALLOUTS, id) ? CALLOUTS[id] : null;
}
/** The cards of every hand the player won, in dealt order ([{ owner, slot }]); empty unless the hand settled. */
export function winningCards(hand) {
  if (!hand || !hand.done || !hand.result) return [];
  const won = hand.hands.map((_, i) => { const r = hand.result.hands[i]; return !!(r && WINS.has(r.outcome)); });
  const out = [], two = hand.hands.length === 2;
  const push = (i, j) => { if (won[i] && hand.hands[i].cards[j] !== undefined) out.push({ owner: i, slot: j }); };
  if (two) { push(0, 0); push(1, 0); hand.hands.forEach((h, i) => { for (let j = 1; j < h.cards.length; j++) push(i, j); }); }
  else if (hand.hands[0]) for (let j = 0; j < hand.hands[0].cards.length; j++) push(0, j);
  return out;
}

/**
 * How long a moment the station just played keeps something fullscreen (ms), so no new decision opens under it.
 * `fired` = host fx the moment sent (a host with no fx hook sends none), `tunnel` = the host has a tunnel hook for
 * the losing edges, `still` = Calm or reduced (the host shortens a gif_from to 60%). The bloom waits out its picture,
 * a win its wash, a loss its breath of tunnel; a push and the sit fan hold nothing fullscreen. The station adds
 * WIN_HOLD_MS from the settle frame on top for a win.
 */
export function screenHoldMs(id, { fired = 0, tunnel = false, still = false } = {}) {
  if (id === 'cards.bloom') return fired > 0 ? Math.round(TIMING.bloomMs * (still ? 0.6 : 1)) : 0;
  if (id === 'cards.win' || id === 'cards.dealer_bust') return fired > 0 ? TIMING.washMs : 0;
  if (id === 'cards.streak') return fired > 0 ? TIMING.streakMs : 0;
  if (id === 'cards.sweep') return fired > 0 ? TIMING.sweepMs : 0;
  if (id === 'cards.lose') return tunnel ? MOMENTS['cards.lose'].host.find((s) => s.tunnel === 'breath').ms + TIMING.edgesTailMs : 0;
  return 0;
}

const WINS = new Set(['win', 'blackjack', 'charlie']);
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

export function momentOf(hand) {
  if (!hand || !hand.done || !hand.result) return null;
  const net = hand.result.net;
  return net > 0 ? 'cards.win' : net < 0 ? 'cards.lose' : 'cards.push';
}

/** Every hand of a split won (two or more hands, none lost or pushed). */
export const isSweep = (hand) => !!(hand && hand.done && hand.result && hand.hands.length >= 2
  && hand.result.hands.length === hand.hands.length && hand.result.hands.every((r) => r && WINS.has(r.outcome)));
/** A win the dealer handed over by busting. */
export const isDealerBust = (hand) => !!(hand && hand.done && hand.result && hand.result.net > 0 && hand.result.dealerTotal > 21);

/**
 * The settle beat, one per hand (Brake 2): the biggest that fits. `streak` counts this hand (streakAfter).
 * sweep (a split, every hand won) > streak (the third win in a row and on) > dealer_bust > win; then lose, push.
 * Everything here reads the server's settled result: nothing is guessed before the reply (Law I).
 */
export function settleMoment(hand, streak = 0) {
  const base = momentOf(hand);
  if (base !== 'cards.win') return base;
  if (isSweep(hand)) return 'cards.sweep';
  if ((streak | 0) >= STREAK_FROM) return 'cards.streak';
  if (isDealerBust(hand)) return 'cards.dealer_bust';
  return 'cards.win';
}

export const isBloom = (hand) => !!(hand && hand.done && hand.result && hand.hands.length === 1
  && hand.result.hands[0] && hand.result.hands[0].outcome === 'blackjack');

/** The ace of a bloom: its slot in hand 0. */
export const aceSlot = (hand) => (hand && hand.hands[0] ? hand.hands[0].cards.findIndex((c) => c[0] === 'A') : -1);

/** Highest-value card, ace highest (11), first of equals, over the hands the player won. Null when none won. */
export function bestCard(hand) {
  if (!hand || !hand.done || !hand.result) return null;
  let best = null, bestV = -1;
  hand.hands.forEach((h, i) => {
    const r = hand.result.hands[i];
    if (!r || !WINS.has(r.outcome)) return;
    for (const c of h.cards) { const v = cardValue(c) === 1 ? 11 : cardValue(c); if (v > bestV) { best = c; bestV = v; } }
  });
  return best;
}

/** Winnings spiral to the player (dir 1), a lost bet to the dealer (dir -1); a push moves nothing. */
export function vortexOf(hand) {
  const net = hand && hand.result ? hand.result.net : 0;
  if (net > 0) return { dir: 1, n: clamp(net, 1, 6) };
  if (net < 0) return { dir: -1, n: clamp(-net, 1, 4) };
  return null;
}

/**
 * The text readout of a settled hand, one line per hand plus the net on a split. Keys with English fallbacks;
 * `{dealer}` is the dealer's name, filled by the station.
 */
export function resultLines(hand) {
  if (!hand || !hand.done || !hand.result) return [];
  const r = hand.result, d = r.dealerTotal, many = hand.hands.length > 1, out = [];
  r.hands.forEach((h, i) => {
    const won = Math.max(0, h.paid - h.bet), lost = h.bet, p = h.total;
    let line;
    if (h.outcome === 'blackjack') line = { key: 'br_cards_res_blackjack', fallback: 'Blackjack! +{n} SP.', vars: { n: won } };
    else if (h.outcome === 'charlie') line = { key: 'br_cards_res_charlie', fallback: 'Six cards without busting. +{n} SP.', vars: { n: won } };
    else if (h.outcome === 'win' && d > 21) line = { key: 'br_cards_res_dealer_bust', fallback: '{dealer} busts at {d}. +{n} SP.', vars: { d, n: won } };
    else if (h.outcome === 'win') line = { key: 'br_cards_res_win', fallback: '{p} beats {d}. +{n} SP.', vars: { p, d, n: won } };
    else if (h.outcome === 'push' && r.dealerBlackjack) line = { key: 'br_cards_res_push_bj', fallback: 'Blackjack each. Bet returned.', vars: {} };
    else if (h.outcome === 'push') line = { key: 'br_cards_res_push', fallback: 'Push at {p}. Bet returned.', vars: { p } };
    else if (h.outcome === 'bust') line = { key: 'br_cards_res_bust', fallback: 'Bust at {p}. {dealer} takes {n} SP.', vars: { p, n: lost } };
    else if (r.dealerBlackjack) line = { key: 'br_cards_res_dealer_bj', fallback: '{dealer} has blackjack. {dealer} takes {n} SP.', vars: { n: lost } };
    else line = { key: 'br_cards_res_lose', fallback: '{d} beats {p}. {dealer} takes {n} SP.', vars: { p, d, n: lost } };
    if (many) line = { ...line, prefix: { key: 'br_cards_hand_n', fallback: 'Hand {i}:', vars: { i: i + 1 } } };
    out.push(line);
  });
  if (many) {
    out.push(r.net === 0 ? { key: 'br_cards_res_net_even', fallback: 'Even overall.', vars: {} }
      : { key: r.net > 0 ? 'br_cards_res_net_up' : 'br_cards_res_net_down', fallback: r.net > 0 ? 'Up {n} SP overall.' : 'Down {n} SP overall.', vars: { n: Math.abs(r.net) } });
  }
  return out;
}

/**
 * The hand on the felt (`shown`, null for an empty table) -> the reply's hand (`next`), as timed steps:
 *   { at, op: 'clear' }                                   a new hand: the old cards go
 *   { at, op: 'card', owner: 0 | 1 | 'd', slot, code }   a card leaves the shoe (code null = the hole card, face down)
 *   { at, op: 'split' }                                   hand 0's second card slides to hand 1
 *   { at, op: 'bloom' }                                   the frame the ace's hand finishes turning (a paid blackjack)
 *   { at, op: 'reveal', code }                            the hole card turns
 *   { at, op: 'active', index }                           the hand the player is on
 *   { at, op: 'ready' }                                   decisions are live
 *   { at, op: 'beat', id, owner?, slot? }                 a table beat on the frame its card shows: cards.deal, cards.hit,
 *                                                         cards.double, cards.split, cards.bust, cards.reveal (`beats` only)
 *   { at, op: 'settle' }                                  the result shows (moments fire here)
 * `still` (Calm, reduced) puts every step at 0: the settled state, in order. A bloom keeps its gaps to the reveal and
 * the settle, so the settle's wash is never inside the host's 360 ms wash gap after the bloom's. `beats` is on for a
 * reply to the player's own press (Deal, a move): a hand put back on the felt (resume, refresh) has no beats.
 */
export function planSteps(shown, next, { still = false, beats = false } = {}) {
  const T = TIMING, g = still ? 0 : 1, steps = [];
  if (!next) return steps;
  let t = 0, lastCardAt = -1, bloomAt = -1;
  const push = (op, extra = {}) => steps.push({ at: Math.round(t), op, ...extra });
  const card = (owner, slot, code) => { push('card', { owner, slot, code }); lastCardAt = t; };
  const beat = (at, id, extra = {}) => { if (beats && id) { const keep = t; t = at; push('beat', { id, ...extra }); t = keep; } };
  const shownMs = (T.flyMs + T.flipMs) * g;
  const fresh = !shown || shown.id !== next.id;

  if (fresh) {
    push('clear', { sweep: !!shown });
    t += (shown ? Math.max(T.firstMs, 620) : T.firstMs) * g;
    const h = next.hands, two = h.length === 2;
    card(0, 0, h[0].cards[0]); beat(t + shownMs, 'cards.deal', { owner: 0, slot: 0 }); t += T.dealGapMs * g;
    card('d', 0, next.dealer[0]); t += T.dealGapMs * g;
    if (two) card(1, 0, h[1].cards[0]); else { card(0, 1, h[0].cards[1]); if (isBloom(next)) bloomAt = t + (T.flyMs + T.flipMs) * g; }
    t += T.dealGapMs * g;
    card('d', 1, null);
    h.forEach((x, i) => {
      for (let j = two ? 1 : i === 0 ? 2 : 1; j < x.cards.length; j++) { t += T.hitGapMs * g; card(i, j, x.cards[j]); }
    });
  } else {
    let from = shown.hands.map((x) => x.cards.length), split = false;
    if (shown.hands.length === 1 && next.hands.length === 2) { push('split'); beat(t + T.splitMs * g, 'cards.split'); t += T.splitMs * g; from = [1, 1]; split = true; }
    next.hands.forEach((x, i) => {
      const doubled = !!x.doubled && !(shown.hands[i] && shown.hands[i].doubled);
      for (let j = from[i] || 0; j < x.cards.length; j++) {
        card(i, j, x.cards[j]);
        const bust = totalOf(x.cards.slice(0, j + 1)).total > 21;
        beat(t + shownMs, bust ? 'cards.bust' : split ? null : doubled ? 'cards.double' : 'cards.hit', { owner: i, slot: j });
        t += T.hitGapMs * g;
      }
    });
    if (lastCardAt >= 0) t = lastCardAt;
  }

  if (!fresh && shown.active !== next.active) push('active', { index: next.active });
  if (!next.done) {
    if (fresh) push('active', { index: next.active });
    if (lastCardAt >= 0) t = lastCardAt + (T.flyMs + T.flipMs) * g;
    push('ready');
    return steps.sort((a, b) => a.at - b.at);
  }
  if (bloomAt >= 0) { const keep = t; t = bloomAt; push('bloom'); t = keep; }
  t = bloomAt >= 0 ? bloomAt + T.bjRevealMs : lastCardAt >= 0 ? lastCardAt + T.revealMs * g : t + T.firstMs * g;
  push('reveal', { code: next.dealer[1] });
  beat(t + T.flipMs * g, 'cards.reveal', { owner: 'd', slot: 1 });
  for (let j = 2; j < next.dealer.length; j++) { t += T.hitGapMs * g; push('card', { owner: 'd', slot: j, code: next.dealer[j] }); }
  t += T.settleMs * (bloomAt >= 0 ? 1 : g);
  push('settle');
  return steps.sort((a, b) => a.at - b.at);
}

/**
 * One card of the sit fan at `ms` into it: out of the shoe, face up in the row, back into the shoe.
 * -> { visible, p (flight out 0..1), q (flight back 0..1), flip (0 back .. 1 face), alpha }
 */
export function fanCard(i, ms, still = false) {
  if (still) {
    const a = clamp(ms / 300, 0, 1) * clamp((TIMING.sitStillMs - ms) / 300, 0, 1);
    return { visible: a > 0, p: 1, q: 0, flip: 1, alpha: a };
  }
  const tIn = 100 + i * 90, tOut = 3000 + i * 60;
  const p = clamp((ms - tIn) / 500, 0, 1), q = clamp((ms - tOut) / 450, 0, 1);
  const flip = q > 0 ? 1 - clamp(q / 0.5, 0, 1) : clamp((ms - tIn - 500) / 400, 0, 1);
  return { visible: p > 0 && q < 1, p, q, flip, alpha: 1 };
}

/** The lamp's breath 0..1 on a 10 s period; 0.5 (rest) while held for a celebration. Amplitude is the caller's (x k). */
export const lampBreath = (now, held = false) => (held ? 0.5 : 0.5 + 0.5 * Math.sin((now * Math.PI * 2) / TIMING.lampPeriodMs));
