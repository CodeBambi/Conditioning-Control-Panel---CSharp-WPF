import { createSeatLook } from '../../room/seat-look.js';
/* ============================================================================
 * station.js - Soft Hand, Twenty-One (CONTRACT 7 and 10.13.F). The room calls
 * mount(ctx) once, then open()/close() per visit. Back is live at every frame
 * (Law VI): nothing here waits on the server, the deal or a moment to leave.
 *
 *   hand.js   reading the server's hand, the live controls, retries, Law I (pure)
 *   feel.js   the timed steps of a reply, moment ids, result text (pure)
 *   table.js  the 2D canvas table and its page effects
 *   shared/hypno  the Loom kit (card backs), the deck (your pictures), moments
 *
 * A SIT-DOWN is one visit: open deals 13 pictures to the 13 values and plays
 * the sit fan; "Stand up, sit back down" re-deals while no hand is open. The
 * server deals and decides every card; the page animates what the reply says
 * and fires moments on the frame the result SHOWS. While a decision is open the
 * screen is held (moments.holdScreen): nothing fullscreen covers the table.
 *
 * THE WINNING FLOW (shared/hypno/callout.js, owner 2026-09-15). On the settle
 * frame of a win (Law I): the chip thud, the text and the deal hold at 0, the
 * winning cards glow in dealt order to HIGHLIGHT_MS, and at FX_DELAY_MS the
 * settle moment, its page effects and the callout (feel.calloutFor) fire
 * together; Deal stays held WIN_HOLD_MS at least. A blackjack does the same at
 * its bloom (both cards glow, then the bloom and "Blackjack"). A loss keeps its
 * breath of tunnel on the settle frame; a push shows nothing. Gates no longer
 * drop a step (moments.js); ctx.gates only dresses the table.
 *
 * THE REWARD PASS (CONTRACT 10.22, lane BR2-rw-cards). The table used to take
 * spReadout.set and .owe and never .thud(), and a winning hand paid by having
 * the number change - Law XII broken outright. From here every paid hand:
 *
 *   asks the spine for a plan   reward.settleTier -> sitPlan (Law IX and
 *                               Brakes 2, 3, 5, 8 live in shared/win/plan.js
 *                               and NOWHERE in this file)
 *   flies THE BANK              plan.bank tokens out of the pot to the room's
 *                               SP chip, the readout ticking as each one LANDS
 *   climbs THE CHIME LADDER     the streak is the root, the rollup is the climb
 *   spends the garnish          plan.glow on the chip, plan.sparkle on the felt,
 *                               plan.shower to the room (ctx.revealedWin), and
 *                               the callout's own size capped by plan.reveal
 *
 * The bank leaves on the settle frame, beside the winning cards' glow; the
 * moment, the callout and the shower still ride FX_DELAY_MS together. A settle
 * that lands INSIDE the bloom's fullscreen picture is a focus state: it merges
 * into the bloom's party (Brake 2) and drops an octave (Brake 5), and its bank
 * still flies, because a pay is not a ceremony.
 * ==========================================================================*/

import { createLoomKit, createDeck, createMoments, strengthK, viewportRect, DECK_VALUES } from '../../shared/hypno/index.js';
import { createCallout, FX_DELAY_MS } from '../../shared/hypno/callout.js';
import { readState, readHand, legalOf, controls, classify, createIntent, mayRetry, moveBody, owedFor, shownSp, defaultStake,
  readHintPref, writeHintPref, isOpen, totalOf, MOVES } from './hand.js';
import { planSteps, settleMoment, streakAfter, mayFire, wordKeys, aceSlot, bestCard, vortexOf, resultLines, screenHoldMs, TIMING,
  calloutFor, winningCards, isBloom, WIN_HOLD_MS } from './feel.js';
import { freshSit, sitPlan, afterParty } from '../../shared/win/plan.js';
import { settleTier, bloomTier, netOf, settleCue, climbSteps, ladderRoot, joinParty, partyHoldMs, calloutTier, showsRoom, BLOOM } from './reward.js';
import { createCardsBank } from './bank.js';
import { sparkBurst, warmGlow } from '../../../arcademy/shell/counterfx.js';
import { kit as sound } from '../../shared/sound/kit.js';
import { MOMENTS } from '../../shared/hypno/moments.js';
import { createTable } from './table.js';

export const roomStage = true;

const fmt = (n) => Number(n || 0).toLocaleString('en-US');
const wait = (ms) => new Promise((r) => setTimeout(r, Math.max(0, ms)));
const mintId = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
const MOVE_LABEL = { hit: 'Hit', stand: 'Stand', double: 'Double', split: 'Split' };
const DECK_WAIT_MS = 2500;

function loadCss() {
  if (document.querySelector('link[data-cards-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet'; link.href = new URL('./station.css', import.meta.url).href; link.dataset.cardsCss = '';
  document.head.append(link);
}

export async function mount(ctx) {
  const createTable3D = ctx.stage ? (await import('./table-3d.js')).createTable3D : null;
  const t = (key, fallback, vars = {}) => {
    const s = typeof ctx.lex === 'function' ? ctx.lex(key, fallback) : fallback;
    return String(s ?? fallback).replace(/\{(\w+)\}/g, (_, k) => (k in vars ? vars[k] : `{${k}}`));
  };
  const prefersReduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  const hostBack = ctx.hostBack === true;
  const storage = (() => { try { return globalThis.localStorage || null; } catch (e) { return null; } })();
  const say = (m) => { try { if (ctx.bridge && typeof ctx.bridge.log === 'function') ctx.bridge.log('warn', '[cards] ' + m); } catch (e) { /* noop */ } };
  loadCss();

  let el = null, table = null, kit = null, deck = null, moments = null, chip = null, raf = 0, session = 0, alive = false, suspended = false;
  let st = null, shownHand = null, queue = [], busy = false, decide = false, phase = 'loading', note = '', lines = [];
  let stake = 1, stakePicked = false, hint = readHintPref(storage), dealReadyAt = 0, screenUntil = 0, sitting = 0, firstSit = true;
  let lastStill = null, unSp = null, feelLog = [], statusText = '', seat = 0, seating = false, dropped = 0;
  let streak = 0, beatAt = {}, wordCursor = 0;   // the table beats: wins in a row, each beat's last frame (cooldowns), the whisper rotation
  let callout = null, lastCallout = null, fxTimers = new Set();   // the winning flow: the callout and the delayed fx frames
  // THE REWARD PASS (10.22): THE BANK, Brake 3's sit-down ledger (one per sitting, per RUNG) and the party
  // still running, which the next beat merges into instead of stacking on (Brake 2).
  let bank = null, sit = freshSit(), party = null, lastPlan = null, bankTicks = 0;
  const $ = (sel) => el.querySelector(sel);
  const log = (what, extra = {}) => { feelLog = [...feelLog.slice(-99), { what, at: Math.round(performance.now()), ...extra }]; };
  /** `fn(now)` after `ms`, unless the visit ended or paused first (Law VI: suspend and Back fire nothing more). */
  function later(ms, fn) {
    const my = session, id = setTimeout(() => { fxTimers.delete(id); if (my !== session || !alive || suspended) return; fn(performance.now()); }, ms);
    fxTimers.add(id);
  }
  function dropTimers() { for (const id of fxTimers) clearTimeout(id); fxTimers.clear(); if (callout) callout.cancel(); }
  /** The name of the beat. `plan` sizes it: THE REVEAL (the hero callout, 14vh with a rim and a shake) is the
   *  declared hero move and plays once a sit-down, so a second sweep names itself at big instead (10.22.D). */
  function showCallout(co, plan = null) {
    if (!co || !callout) return;
    const tier = calloutTier(co, plan) || co.tier;
    callout.show(co.key, co.fallback, { tier });
    lastCallout = { key: co.key, tier, at: Math.round(performance.now()) };
    log('callout', lastCallout);
  }

  /** Calm, reduced motion and the gates, read live every frame (the loader's ctx getters follow settings frames).
   *  `still` is the table's own conflation (it draws the same either way); `reduced` and `calm` are kept APART
   *  for shared/win/plan.js, which treats them as two different things: reduced motion takes the settled state
   *  and no travel, Calm only strips the decoration and the bank still flies (10.22.C, Law XII). */
  function dress() {
    const reduced = !!ctx.reduced || prefersReduced || ['off', 'still'].includes(String(ctx.motion).toLowerCase()), intensity = String(ctx.intensity || 'normal').toLowerCase(), g = ctx.gates || {};
    return { still: reduced || intensity === 'calm', reduced, calm: intensity === 'calm', k: prefersReduced ? 0.5 : strengthK(ctx), full: intensity === 'full' && !reduced,
      gates: { flash: g.flash !== false, spiral: g.spiral !== false, brainDrain: g.brainDrain !== false, tunnel: g.tunnel !== false } };
  }

  function build() {
    const root = document.createElement('div');
    root.className = 'cards-station' + (ctx.stage ? ' br-seat cards-in-room' : ''); root.dataset.phase = 'loading';
    root.innerHTML = `
      ${ctx.stage ? '<div class="cards-stage"></div>' : '<canvas class="cards-stage"></canvas>'}
      <header class="cards-top">
        <button class="cards-back" type="button"></button>
        <span class="cards-sp"></span>
        <div class="cards-status" aria-live="polite"></div>
        <div class="cards-hint" hidden></div>
      </header>
      <div class="cards-side">
        <div class="cards-bet" role="group"><span class="cards-bet-label"></span></div>
        <label class="cards-hint-toggle"><input type="checkbox"> <span></span></label>
        <button class="cards-sit" type="button"></button>
        <span class="cards-sitting"></span>
      </div>
      <div class="cards-controls">
        ${MOVES.map((m) => `<button class="cards-move" type="button" data-move="${m}"></button>`).join('')}
        <button class="cards-deal" type="button"><span></span><small></small></button>
      </div>
      <div class="cards-total" aria-live="polite" hidden><small></small><strong></strong><span class="cards-score-sparks" aria-hidden="true"></span></div>
      <div class="cards-total cards-dealer-total" aria-live="polite" hidden><small></small><strong></strong><span class="cards-score-sparks" aria-hidden="true"></span></div>
      <div class="cards-tokens" aria-hidden="true"></div>
      <div class="cards-card" role="status" hidden><p></p><button class="cards-card-back" type="button"></button></div>
      <div class="cards-loading"></div>`;
    const q = (s) => root.querySelector(s);
    q('.cards-stage').setAttribute('aria-label', t('br_cards_stage', 'Soft Hand, a Twenty-One table.'));
    q('.cards-back').innerHTML = '&larr; '; q('.cards-back').append(t('br_cards_back', 'Back'));
    q('.cards-card-back').textContent = t('br_cards_back', 'Back');
    q('.cards-bet').setAttribute('aria-label', t('br_cards_bet', 'Bet'));
    q('.cards-bet-label').textContent = t('br_cards_bet', 'Bet');
    q('.cards-hint-toggle span').textContent = t('br_cards_hint_toggle', 'Basic-strategy hint');
    q('.cards-sit').textContent = t('br_cards_sit', 'Stand up, sit back down');
    q('.cards-loading').textContent = t('br_cards_loading', 'Shuffling the deck');
    q('.cards-deal span').textContent = t('br_cards_deal', 'Deal');
    for (const b of root.querySelectorAll('.cards-move')) { b.textContent = t('br_cards_' + b.dataset.move, MOVE_LABEL[b.dataset.move]); b.onclick = () => move(b.dataset.move); }
    q('.cards-back').onclick = back; q('.cards-card-back').onclick = back;
    if (hostBack) { root.dataset.hostBack = ''; q('.cards-back').hidden = true; q('.cards-card-back').hidden = true; }
    q('.cards-deal').onclick = () => deal();
    q('.cards-sit').onclick = () => resit();
    const box = q('.cards-hint-toggle input');
    box.checked = hint;
    box.onchange = () => { hint = box.checked; writeHintPref(storage, hint); };
    return root;
  }

  function back() { if (typeof ctx.standUp === 'function') ctx.standUp(); else close(); }
  function card(text) { if (!el) return; $('.cards-card p').textContent = text || ''; $('.cards-card').hidden = !text; }
  function ring(btn) { if (!btn) return; btn.classList.add('is-ringing'); setTimeout(() => btn.classList.remove('is-ringing'), 400); }   // Law VIII

  /* ------------------------------------------------------------ SP chip */
  function createChip() {
    const hook = ctx.spReadout && typeof ctx.spReadout.set === 'function' && typeof ctx.spReadout.owe === 'function' ? ctx.spReadout : null;
    let server = Number(typeof ctx.sp === 'function' ? ctx.sp() : 0) || 0, owed = 0, flying = null;
    const paint = () => {
      if (hook) { hook.owe(owed); hook.set(flying); return; }   // set(null) hands the rule back to the room (7.1)
      const n = el && $('.cards-sp'), text = t('br_cards_sp', '{n} SP', { n: fmt(flying != null ? flying : shownSp(server, owed)) });
      if (n && n.textContent !== text) n.textContent = text;
    };
    if (hook) { el.dataset.hostSp = ''; hook.set(null); }
    paint();
    return {
      kind: hook ? 'hook' : 'own',
      get server() { return server; }, get owed() { return owed; }, get value() { return flying != null ? flying : shownSp(server, owed); },
      setServer(v) { if (Number.isFinite(Number(v))) { server = Number(v); paint(); } },
      owe(n) { owed = Math.max(0, Math.trunc(Number(n) || 0)); paint(); },
      /** THE BANK is flying: the chip says exactly this until the last token is down (`show(null)` releases it
       *  back to the room's Law I rule). Nothing else may write the number while a run owns it. */
      show(v) { flying = Number.isFinite(Number(v)) ? Math.round(Number(v)) : null; paint(); },
      /** THE BANK's target: the room's chip, or the station's own in a standalone page. */
      target() { return hook && typeof hook.target === 'function' ? hook.target() : (el && $('.cards-sp')); },
      thud() {
        if (hook) { if (typeof hook.thud === 'function') hook.thud(); return; }
        const n = el && $('.cards-sp');
        if (n && typeof n.animate === 'function') n.animate([{ filter: 'brightness(2)' }, { filter: 'brightness(1)' }], { duration: 340 });
      },
      /** close(): the plain server number, nothing owed, no flight value. */
      handOver() { owed = 0; flying = null; if (hook) { hook.owe(0); hook.set(null); } else paint(); },
    };
  }

  /* ------------------------------------------- THE REWARD PASS (CONTRACT 10.22) */
  /** What the spine is told about this board. `reduced` and `still` are two different things and go in apart:
   *  reduced motion takes the settled STATE with no travel, Calm only strips the decoration - and Calm is also
   *  this table's lite board (Brake 8: four tokens, no particles, every sound kept). */
  function partyCtx(melted) {
    const d = dress();
    return { reduced: d.reduced, still: d.still, lite: d.calm || ctx.lite === true, melted: !!melted };
  }
  /** Brake 5 at the card table. The focus state here is a fullscreen hypno moment from an EARLIER beat still
   *  on the screen - in practice the bloom's own four seconds of picture, which a blackjack's settle lands
   *  inside. Read on the beat's own frame, BEFORE it adds a hold of its own, or every beat is melted. */
  const inTrance = (now) => screenBusy(now);
  /** THE BANK's two ends, client px, measured every frame: the camera is still moving while a seated hand
   *  settles, so neither end may be cached. `potRect` is the chip spot on the felt (2D) or the authored
   *  `bet_spot` anchors (the room view). */
  function potAt() {
    const r = table && typeof table.potRect === 'function' ? table.potRect() : null;
    if (!r) return null;
    const v = viewportRect($('.cards-stage'), r.x, r.y, r.w, r.h);
    return { x: v.x + v.w / 2, y: v.y + v.h / 2 };
  }
  function chipAt() {
    const n = chip && chip.target(), b = n && typeof n.getBoundingClientRect === 'function' ? n.getBoundingClientRect() : null;
    return b && b.width ? { x: b.left + b.width / 2, y: b.top + b.height / 2 } : null;
  }

  /** This beat owns the station for its party: the next beat merges into it instead of stacking (Brake 2),
   *  the next deal waits it out, and Brake 3's ledger counts the rung that was asked for. */
  function ownParty(plan, now, floorMs = 0) {
    const ms = partyHoldMs(plan, floorMs);
    party = { plan, until: now + ms };
    lastPlan = plan;
    if (ms > 0) screenUntil = Math.max(screenUntil, now + ms);
    sit = afterParty(sit, plan);
  }

  /**
   * What a plan buys, spent HERE and nowhere else (this file re-decides none of Law IX or Brakes 2, 3, 5, 8).
   * `ceremony` false is Brake 2: a live party is already as big as this one, so only the beats that are not a
   * celebration are left. THE BANK is not run from here - a pay must be seen to move at every rung (Law XII).
   */
  function spendParty(plan, { id, ceremony, amount = 0, text = '' }) {
    const cue = settleCue(id, plan, streak);
    // A loss's SETTLE and a push's sigh are the beat itself and always sound; THE WIN only when this beat
    // still owns the frame, so a blackjack's settle never rings a second time over its own bloom.
    if (cue && (cue[0] !== 'win' || ceremony)) sound.play(cue[0], cue[1]);
    if (!ceremony || !plan || plan.spent <= 0) return false;
    // THE CHIME LADDER climbs while THE BANK's readout counts. Step 0 is the cue above (Law X), so only what
    // follows it is scheduled; a skip takes back whatever has not sounded (Law VI).
    const climb = climbSteps(plan);
    sound.stop('ladder');
    if (climb.length) sound.play('ladder', { plan: climb, semis: ladderRoot(streak, plan.octave < 0) });
    if (plan.glow > 0) warmGlow(chip.target(), { reduced: dress().reduced });                 // THE GLOW, 480 ms
    if (plan.sparkle > 0) sparkBurst(el && $('.cards-tokens'), { count: plan.sparkle });      // THE SPARKLE BURST
    // 10.22.B: the room learns what the player has just learnt, once a result, never for a loss or a push.
    if (showsRoom(plan, amount) && typeof ctx.revealedWin === 'function') ctx.revealedWin(amount, plan.shower, text);
    log('party', { id, tier: plan.tier, spent: plan.spent, bank: plan.bank, shower: plan.shower, ladder: plan.ladder,
      sparkle: plan.sparkle, glow: plan.glow, reveal: plan.reveal, emi: plan.emi, partyMs: plan.partyMs, why: plan.why });
    return true;
  }

  /** THE BANK (Law XII): the pay leaves the pot on the settle frame, beside the winning cards' glow, and the
   *  room's chip ticks as each token LANDS (Law X). `plan.bank` of 0 is reduced motion - the settled state. */
  function flyPay(plan, from, to, now) {
    if (!bank || !(Math.round(to) > Math.round(from))) { chip.show(null); return null; }
    bankTicks = 0;
    const how = bank.pay({ n: Math.max(1, plan.bank), fromValue: from, toValue: to, from: potAt, to: chipAt,
      rollupMs: plan.partyMs, reduced: plan.bank === 0 });
    log('bank', { from: Math.round(from), to: Math.round(to), n: plan.bank, roll: plan.partyMs, how });
    return how;
  }

  /* ------------------------------------------------------------ the felt */
  function enqueue(next, { quiet = false, beats = false } = {}) {
    const steps = planSteps(shownHand, next, { still: dress().still, beats: beats && !quiet });
    const base = performance.now(), fresh = !shownHand || shownHand.id !== next.id;
    const bets = { at: 0, op: 'bets', list: next.hands.map((h) => h.bet) };
    if (!fresh && next.hands.some((h,i)=>h.doubled && !shownHand.hands[i]?.doubled) && !dress().still)
      steps.forEach(s=>{s.at+=320;});
    steps.splice(fresh ? 1 : 0, 0, bets);
    // quiet: the hand goes down settled on this frame (no flights, no timings), the steps still in their order
    queue = queue.concat(steps.map((s) => ({ ...s, at: quiet ? base : base + s.at, hand: next, quiet })));
    shownHand = next;
    decide = false;
    if (!next.done) moments.holdScreen(true);   // nothing fullscreen while a decision is open
  }

  function apply(s, now) {
    const d = dress(), h = s.hand;
    switch (s.op) {
      case 'clear': totalKey = ''; dealerTotalKey = ''; table.clear(now, s.sweep && !s.quiet && !d.still); lines = []; break;
      case 'bets': table.setBets(s.list, now, s.quiet); if (!s.quiet) sound.play('chips', { n: Math.min(6, 1 + (Array.isArray(s.list) ? s.list.length : 0)) }); break;
      case 'card': table.addCard({ ...s, settled: s.quiet }, now); if (!s.quiet) sound.play('card-slide'); break;
      case 'split': table.split(now); break;
      case 'active': table.setActive(s.index); break;
      case 'reveal': table.reveal(s.code, now, s.quiet); if (!s.quiet) sound.play('card-flip'); break;
      case 'ready': decide = true; table.setActive(h.active); break;
      case 'bloom': {
        if (s.quiet) break;
        // The blackjack's frame: both cards glow now; the bloom, its picture, its swell and "Blackjack" follow at FX_DELAY_MS.
        // The bloom is the table's big rung and it pays NOTHING yet - the hand has not settled, so no bank flies
        // here (Law I). Its plan owns the station from this frame, which is what the settle then merges into.
        const slot = aceSlot(h), r = table.cardRect(0, slot), canvas = $('.cards-stage');
        const joined = joinParty(party, sitPlan(bloomTier(), sit, partyCtx(inTrance(now))), now);
        table.hitCards(h.hands[0].cards.map((_, j) => ({ owner: 0, slot: j })), now);
        ownParty(joined.plan, now, WIN_HOLD_MS);
        later(FX_DELAY_MS, (at) => {
          spendParty(joined.plan, { id: BLOOM, ceremony: joined.ceremony });   // the pay is announced at the settle, not here
          const out = moments.play(BLOOM, { from: r ? viewportRect(canvas, r.x, r.y, r.w, r.h) : undefined, gif: deck ? deck.keyFor(h.hands[0].cards[slot]) : undefined });
          if (out.page.includes('ace_glow')) table.glowCard(0, slot, at);
          holdScreenFor(BLOOM, out, at);
          showCallout(calloutFor(BLOOM), joined.plan);
          log('moment', { id: BLOOM, tokens: out.tokens.length, page: out.page, held: out.held, from: r, delayed: FX_DELAY_MS });
        });
        break;
      }
      case 'beat': {
        // A table beat, on the frame its card shows (Law I: the card is the server's). Never quiet (a flush, a resume),
        // never inside its cooldown; a bust is a beat with nothing in it (the whisper withheld). Light host steps only
        // while a decision is open: moments.js keeps everything fullscreen out (holdScreen).
        if (s.quiet || !mayFire(s.id, beatAt[s.id], now)) { log('beat-skipped', { id: s.id, why: s.quiet ? 'quiet' : 'cooldown' }); break; }
        beatAt[s.id] = now;
        const n = (MOMENTS[s.id] ? MOMENTS[s.id].host : []).reduce((m, st) => Math.max(m, st.words | 0), 0);
        const words = n > 0 ? wordKeys(n, wordCursor) : undefined;
        if (n > 0) wordCursor += n;
        const out = moments.play(s.id, { words });
        log('moment', { id: s.id, tokens: out.tokens.length, page: out.page, held: out.held, owner: s.owner, slot: s.slot, words });
        break;
      }
      case 'settle': {
        // Brake 5, read FIRST: a fullscreen moment from an earlier beat (the bloom's picture) still owns the
        // screen, so this settle lands inside a focus state. Law I: `before` is what the chip says while the
        // pay is still owed, and the pin goes on before owe(0) so the number never jumps and then flies.
        const melted = inTrance(now), net = netOf(h), paid = !s.quiet && net > 0, before = chip.value;
        if (paid) chip.show(before);
        moments.holdScreen(false);
        decide = false;
        chip.owe(0);
        table.settleBets?.(h, now, s.quiet || d.still);
        table.setActive(-1);
        lines = resultLines(h);
        streak = streakAfter(streak, h);
        if (s.quiet) { log('settled-quiet', { hand: h.id }); break; }
        const id = settleMoment(h, streak), best = bestCard(h);
        // THE PLAN: the rung this result is worth, what the brakes leave of it, and the party already running
        // (Brake 2). Every restraint lives in shared/win/plan.js; nothing below re-decides any of it.
        const joined = joinParty(party, sitPlan(settleTier(h, streak), sit, partyCtx(melted)), now);
        const plan = joined.plan, co = calloutFor(id, { bloomed: isBloom(h) });
        spendParty(plan, { id, ceremony: joined.ceremony, amount: net, text: co ? t(co.key, co.fallback) : '' });
        const n = (MOMENTS[id] ? MOMENTS[id].host : []).reduce((m, st) => Math.max(m, st.words | 0), 0);
        const words = n > 0 ? wordKeys(n, wordCursor) : undefined;
        if (n > 0) wordCursor += n;
        const v = vortexOf(h);
        const play = (at, delayed) => {
          const out = moments.play(id, { gif: best && deck ? deck.keyFor(best) : undefined, words });
          holdScreenFor(id, out, at);
          if (out.page.includes('win_tunnel')) table.tunnel(at);
          if (!table.settleBets && out.page.includes('chip_vortex') && v) table.vortex(v.dir, v.n, at);
          log('moment', { id, tokens: out.tokens.length, page: out.page, held: out.held, net: h.result.net, best, streak, delayed });
        };
        if (paid) {
          // THE WINNING FLOW: the winning cards glow and THE BANK leaves the pot on this frame (the chip's own
          // thud now waits for the last token, Law X); the moment and the callout still follow at FX_DELAY_MS.
          table.hitCards(winningCards(h), now);
          ownParty(plan, now, WIN_HOLD_MS);
          flyPay(plan, before, chip.server, now);
          later(FX_DELAY_MS, (at) => { play(at, FX_DELAY_MS); showCallout(co, plan); });
        } else { chip.thud(); play(now, 0); }   // a loss keeps its breath of tunnel on this frame; a push shows nothing
        break;
      }
      default: break;
    }
  }
  /** A moment that put something fullscreen holds the next deal until it has ended (hand.controls 'screen'). The
   *  bloom's picture is timed by the host, which shortens it only under its own Calm (ctx.reduced or intensity calm):
   *  the OS prefers-reduced-motion alone never reaches the host, so it must not shorten the hold either. */
  function holdScreenFor(id, out, now) {
    const hostCalm = !!ctx.reduced || String(ctx.intensity || 'normal').toLowerCase() === 'calm';
    const ms = out.held ? 0 : screenHoldMs(id, { fired: out.tokens.length, tunnel: typeof ctx.fxTunnel === 'function', still: hostCalm });
    if (ms > 0) screenUntil = Math.max(screenUntil, now + ms);
  }
  /** Suspend: every step still owed goes down now, quietly (no moments). */
  function flush() { const now = performance.now(); const rest = queue; queue = []; for (const s of rest) apply({ ...s, quiet: true }, now); }

  /* ------------------------------------------------------------ requests */
  async function ask(intent, my) {
    for (;;) {
      const res = await Promise.resolve(ctx.request(intent.op, intent.body, intent.idem)).catch(() => ({ ok: false, reason: 'offline' }));
      if (my !== session) return { kind: 'gone' };
      const c = classify(res);
      if ((c.kind === 'retry' || c.kind === 'wait') && mayRetry(intent, c)) {
        if (c.kind === 'wait') note = t('br_cards_shuffling', 'Shuffling. The next deal is ready in {s} s.', { s: Math.ceil(c.waitMs / 1000) });
        log('retry', { op: intent.op, reason: c.reason, idem: intent.idem, waitMs: c.waitMs });
        await wait(c.waitMs);
        if (my !== session) return { kind: 'gone' };
        continue;
      }
      return c;
    }
  }

  /** A reply's hand goes on the felt. `quiet`: no moments (a hand stood for the player while away). `beats`: the
   *  reply answers the player's own press, so the table beats play; a hand put back (illegal, hand_open) has none. */
  function adopt(body, { quiet = false, beats = false } = {}) {
    if (Number.isFinite(Number(body.sp))) chip.setServer(body.sp);
    const next = readHand(body.hand);
    st = { ...st, sp: chip.server, hand: next, legal: legalOf(body.legal), hint: MOVES.includes(body.hint) ? body.hint : null };
    chip.owe(body.ok ? owedFor(body) : 0);   // Law I: a settled return lands on its settle frame
    if (next) enqueue(next, { quiet, beats }); else { table.clear(); shownHand = null; }
  }

  async function reply(c, my, op) {
    note = '';
    if (c.kind === 'gone') return;
    if (c.kind === 'ok') {
      if (!c.body.autoStood && ['deal','double','split'].includes(op)) ctx.spReadout?.spend?.(c.body.cost, $('.cards-bet'));
      if (!c.body.autoStood && op === 'stand' && !dress().still) standStamp();
      if (op === 'deal') dealReadyAt = performance.now() + st.floorMs;
      if (c.body.autoStood) note = t('br_cards_auto_stood', 'Your last hand was stood for you after a day away.');
      adopt(c.body, { beats: !!op && !c.body.autoStood });
    } else if (c.kind === 'adopt') {
      if (c.reason === 'auto_stood') note = t('br_cards_auto_stood', 'Your last hand was stood for you after a day away.');
      if (c.reason === 'illegal') note = t('br_cards_illegal', 'That move is not open on this hand.');
      if (c.reason === 'hand_open') note = t('br_cards_hand_open', 'Your open hand is back on the table.');
      adopt(c.body, { quiet: c.reason === 'auto_stood' });
    } else if (c.kind === 'refresh') await refresh(my);
    else if (c.kind === 'insufficient') { chip.setServer(c.body && c.body.sp); note = t('br_cards_insufficient', 'You need {n} SP for that bet.', { n: stake }); }
    else if (c.kind === 'closed') { phase = 'closed'; el.dataset.phase = 'closed'; card(t('br_cards_closed', 'The table is closed for a moment.')); }
    else note = t('br_cards_offline', 'The house is not answering. Try again in a moment.');
    log('reply', { op, kind: c.kind, reason: c.reason || null });
  }

  async function refresh(my) {
    const c = classify(await Promise.resolve(ctx.request('state', {})).catch(() => null));
    if (my !== session || c.kind !== 'ok') return;
    st = readState(c.body); chip.setServer(st.sp);
    if (st.hand) enqueue(st.hand, { quiet: st.hand.done }); else { table.clear(); shownHand = null; }
  }

  const view = (now) => controls({ phase, hand: st && st.hand, legal: st ? st.legal : [], sp: chip ? chip.server : 0, stake, busy,
    animating: queue.length > 0, dealReadyAt, screenUntil: screenBusy(now) ? Math.max(screenUntil, now + 1) : screenUntil, now });

  /** A fullscreen moment still running: its timed hold, or a tunnel breath that has outlived it. */
  const screenBusy = (now) => now < screenUntil || !!(moments && moments.breathing());

  /** Every Deal path (the button, Space and Enter, a direct call) lands here. During a fullscreen moment the press is
   *  dropped before anything else: no ring, no note, nothing kept for later (owner 2026-09-14, CONTRACT 10.14 item 10). */
  async function deal() {
    if (!alive || suspended || (ctx.stage && !ctx.stage.ready)) return;
    if (screenBusy(performance.now())) { dropped++; log('deal-dropped', { why: 'screen' }); return; }
    ring($('.cards-deal')); sound.arm();
    // Law VI: a new press takes the settled state of whatever is still running, never a faster version of it.
    if (bank && bank.busy) { bank.skip({ land: true }); sound.stop('ladder'); }
    const c = view(performance.now());
    if (!c.deal) { if (c.dealWhy === 'sp') note = t('br_cards_insufficient', 'You need {n} SP for that bet.', { n: stake }); return; }
    busy = true; lines = []; note = ''; card(null);
    const my = session;
    const r = await ask(createIntent('deal', { stake }, mintId), my);
    if (my !== session) return;
    await reply(r, my, 'deal');   // busy until the reply is on the table (a no_hand re-read included)
    if (my === session) busy = false;
  }

  function standStamp() {
    const hud=table.hud?.(); if(!hud)return;
    const stamp=document.createElement('b');stamp.className='cards-stand-stamp';
    stamp.textContent=t('br_cards_stand',MOVE_LABEL.stand).toUpperCase();
    stamp.style.left=hud.x+'px';stamp.style.top=(hud.y-62)+'px';el.append(stamp);
    stamp.animate([{transform:'translate(-50%,-50%) scale(1.7) rotate(-12deg)',opacity:0},{transform:'translate(-50%,-50%) scale(.94) rotate(-7deg)',opacity:1,offset:.22},{transform:'translate(-50%,-50%) scale(1) rotate(-7deg)',opacity:1,offset:.7},{transform:'translate(-50%,-65%) scale(1)',opacity:0}],{duration:800,easing:'ease-out'}).onfinish=()=>stamp.remove();
    sound.play('deck-square');
  }

  async function move(m) {
    if (!alive || suspended || (ctx.stage && !ctx.stage.ready)) return;
    ring(el && $(`.cards-move[data-move="${m}"]`));
    if (!view(performance.now()).moves[m]) return;
    busy = true; decide = false; note = '';
    const my = session;
    const r = await ask(createIntent(m, moveBody(st.hand), mintId), my);
    if (my !== session) return;
    await reply(r, my, m);
    if (my === session) busy = false;
  }

  /* ------------------------------------------------------------ sitting */
  /** Latched on the press: phase 'sit' refuses Deal, the moves and Sit while the pictures are dealt. One deck per sit. */
  async function sitDown(my, deckP) {
    const mine = ++seat;
    seating = true; sitting++;
    phase = 'sit'; el.dataset.phase = 'sit';
    const d = await Promise.race([deckP, wait(DECK_WAIT_MS).then(() => null)]);
    if (my !== session || mine !== seat) { deckP.then((x) => x && x.dispose()); return; }
    if (deck && deck !== d) deck.dispose();   // the last sitting's pictures stay on its cards until this deal is in
    deck = d; seating = false;
    if (!d) deckP.then((x) => { if (my === session && mine === seat && alive && !deck) deck = x; else if (x) x.dispose(); });
    table.clear(); shownHand = null; lines = [];
    // Brake 3 and Law IX count PER SIT-DOWN, and standing up and sitting back down is a new one: the worn-down
    // rungs come back and the once-a-sitting hero is owed again.
    sit = freshSit(); party = null;
    const out = moments.play('cards.sit');
    if (out.page.includes('sit_fan')) table.startFan(performance.now(), dress().still);
    log('moment', { id: 'cards.sit', sitting, page: out.page });
  }
  function afterSit() {
    phase = 'play'; el.dataset.phase = 'play';
    if (firstSit && st && st.hand) enqueue(st.hand, { quiet: st.hand.done });   // an open hand resumes with its decisions live
    firstSit = false;
  }
  function resit() {
    if (!alive || suspended) return;
    ring($('.cards-sit'));
    if (!view(performance.now()).sit) return;
    sitDown(session, createDeck(ctx, { count: 13, still: dress().still }).catch(() => null));
  }

  /* ------------------------------------------------------------ frame */
  function lineText(l) {
    const dealer = t('br_cards_dealer', 'Emi');
    return (l.prefix ? t(l.prefix.key, l.prefix.fallback, l.prefix.vars) + ' ' : '') + t(l.key, l.fallback, { ...l.vars, dealer });
  }
  function status(c) {
    if (phase === 'loading' || phase === 'closed') return '';
    const dealer = t('br_cards_dealer', 'Emi'), parts = [];
    if (phase === 'sit') {
      parts.push(!dress().gates.flash ? t('br_cards_sit_plain', 'Sitting down at the table.')
        : sitting > 1 ? t('br_cards_sit_again', 'Sitting back down. Your pictures are re-dealt to the thirteen values.')
          : t('br_cards_sit_intro', 'Sitting down. Your pictures are dealt to the thirteen values for this sitting.'));
      return parts.join('\n');
    }
    if (note) parts.push(note);
    if (queue.length && !lines.length) parts.push(t('br_cards_dealing', 'Dealing...'));
    else if (decide && shownHand && !shownHand.done) {
      const h = shownHand.hands[shownHand.active], vars = { p: totalOf(h.cards).total, d: totalOf([shownHand.dealer[0]]).total, dealer, i: shownHand.active + 1 };
      parts.push(shownHand.hands.length > 1 ? t('br_cards_decide_split', 'Hand {i}: {p} against {dealer} showing {d}. Your move.', vars)
        : t('br_cards_decide', '{p} against {dealer} showing {d}. Your move.', vars));
    } else if (lines.length) parts.push(...lines.map(lineText));
    if (phase === 'play' && !queue.length && !busy && !isOpen(st && st.hand)) parts.push(c.dealWhy === 'sp' ? t('br_cards_insufficient', 'You need {n} SP for that bet.', { n: stake }) : t('br_cards_ready', 'Pick a bet and deal.'));
    return [...new Set(parts)].join('\n');
  }

  function sync(now) {
    const c = view(now);
    const open = isOpen(st && st.hand);
    el.toggleAttribute('data-open', open);
    const cameraReady = !ctx.stage || ctx.stage.ready;
    const dealBtn = $('.cards-deal');
    dealBtn.disabled = !c.deal || !cameraReady;
    dealBtn.toggleAttribute('data-held', c.dealWhy === 'screen');
    const small = c.dealWhy === 'screen' ? t('br_cards_moment', 'One moment')
      : c.dealWhy === 'floor' ? t('br_cards_wait', '{s} s', { s: Math.ceil((dealReadyAt - now) / 1000) }) : t('br_cards_bet_line', '{n} SP', { n: stake });
    if (dealBtn.querySelector('small').textContent !== small) dealBtn.querySelector('small').textContent = small;
    for (const b of el.querySelectorAll('.cards-move')) {
      const m = b.dataset.move;
      b.disabled = !c.moves[m] || !cameraReady;
      b.classList.toggle('is-hint', !!(hint && decide && st && st.hint === m && c.moves[m]));
    }
    for (const b of el.querySelectorAll('.cards-bet button')) {
      const next = stake + Number(b.dataset.delta);
      b.disabled = !c.bet || !st.rules.stakes.includes(next) || next < 1 || next > 3 || next > chip.value;
    }
    const betValue = $('.cards-bet-value');
    // Frames run while the initial state request is pending, before renderStakes builds this output.
    if (betValue) betValue.textContent = t('br_cards_stake', '{n} SP', { n: open && shownHand ? shownHand.hands[shownHand.active].bet : stake });
    if (ctx.stage && !open && !queue.length) table.setBets([stake]);
    syncTableHud();
    $('.cards-sit').disabled = !c.sit;
    const s = status(c);
    if (s !== statusText) { statusText = s; $('.cards-status').textContent = s; }
    const hintEl = $('.cards-hint'), showHint = !!(hint && decide && st && st.hint);
    const ht = showHint ? t('br_cards_hint_is', 'Hint: {move}.', { move: t('br_cards_' + st.hint, MOVE_LABEL[st.hint]) }) : '';
    if (hintEl.textContent !== ht) hintEl.textContent = ht;
    hintEl.hidden = !showHint;
    const sit = sitting ? t('br_cards_sitting', 'Sitting {n}', { n: sitting }) : '';
    if ($('.cards-sitting').textContent !== sit) $('.cards-sitting').textContent = sit;
  }

  function frame() {
    raf = 0;
    if (!alive || suspended) return;
    const now = performance.now(), d = dress();
    if (d.still !== lastStill) { kit.setStill(d.still); if (deck) deck.setStill(d.still); lastStill = d.still; }
    while (queue.length && queue[0].at <= now) apply(queue.shift(), now);
    if (moments.breathing()) screenUntil = Math.max(screenUntil, now + TIMING.edgesTailMs);   // a late breath keeps the hold
    if (deck) deck.tick(now);
    table.draw({ now, k: d.k, still: d.still, full: d.full, gates: d.gates, deck, decide,
      print: t('br_cards_print', 'BLACKJACK PAYS 2 TO 1 · DEALER STANDS ON ALL 17s · SIX CARDS WIN'),
      dealerName: t('br_cards_dealer', 'Emi'), youName: t('br_cards_you', 'You'), handName: (i) => t('br_cards_hand_short', 'Hand {i}', { i: i + 1 }) });
    if (phase === 'sit' && !seating && table.fanDone(now)) afterSit();
    sync(now);
    raf = requestAnimationFrame(frame);
  }

  function renderStakes() {
    const box = $('.cards-bet');
    box.querySelectorAll('button,output').forEach(b => b.remove());
    for (const delta of [-1, 1]) {
      if (delta === 1) { const value = document.createElement('output'); value.className = 'cards-bet-value'; box.append(value); }
      const b = document.createElement('button'); b.type = 'button'; b.dataset.delta = delta;
      b.textContent = delta < 0 ? '-' : '+'; b.setAttribute('aria-label', t('br_cards_bet', 'Bet') + (delta < 0 ? ' -1 SP' : ' +1 SP'));
      b.onclick = () => { const next = stake + delta; if (!view(performance.now()).bet || !st.rules.stakes.includes(next) || next < 1 || next > 3) return; stake = next; stakePicked = true; };
      box.append(b);
    }
    if (ctx.stage) el.append(box);
  }

  let totalKey = '', dealerTotalKey = '';
  function paintScore(node, hud, label, key, oldKey, still) {
    node.hidden = hud.total == null || phase !== 'play';
    node.querySelector('small').textContent = label;
    node.querySelector('strong').textContent = hud.total == null ? '' : String(hud.total) + (hud.hidden ? ' + ?' : '');
    const mood = hud.total > 21 ? 'bust' : hud.total === 21 ? 'perfect' : hud.total >= 19 ? 'hot' : hud.total >= 16 ? 'good' : 'medium';
    node.dataset.mood = mood;
    if (key !== oldKey) {
      node.getAnimations().forEach(a=>a.cancel());
      const sparks=node.querySelector('.cards-score-sparks'); sparks.replaceChildren();
      if (!still && !node.hidden && !hud.quiet) {
        node.animate(mood==='bust' ? [{transform:'rotate(-5deg)'},{transform:'rotate(4deg)'},{transform:'translateY(3px)'},{transform:'none'}] : [{transform:'scale(.8)'},{transform:'scale(1.19) rotate(-3deg)'},{transform:'scale(.97) rotate(2deg)'},{transform:'none'}],{duration:480,easing:'ease-out'});
        // Existing kit owns volume and suspension. No win cue for a total alone.
        sound.play(mood==='bust' ? 'deck-square' : 'token', {i:Math.max(0,Math.min(8,hud.total-13))});
        const count=mood==='bust'?0:mood==='perfect'?16:mood==='hot'?10:mood==='good'?6:3;
        for(let i=0;i<count;i++) {
          const dot=document.createElement('i'), angle=i/count*Math.PI*2;
          dot.style.setProperty('--sx',Math.cos(angle)*(38+i%3*9)+'px');
          dot.style.setProperty('--sy',Math.sin(angle)*(34+i%4*6)+'px');
          sparks.append(dot); dot.addEventListener('animationend',()=>dot.remove(),{once:true});
        }
      }
    }
    if(still){node.getAnimations().forEach(a=>a.cancel());node.querySelector('.cards-score-sparks').replaceChildren();}
  }
  function syncTableHud() {
    if (!ctx.stage || !table.hud) return;
    const hud = table.hud(), total = $('.cards-total'), still = dress().still;
    el.toggleAttribute('data-still', still);
    for (const [key,value] of Object.entries({ 'phone-total-x':hud.x, 'phone-total-y':hud.top-18, 'hand-x': hud.x, 'hand-bottom': hud.bottom, 'total-x': hud.left - (hud.hands>1 ? 48 : 65), 'total-y': hud.y, 'bet-x': hud.betX, 'bet-y': hud.betY + 44 })) { if (!ctx.stage.lookShift || !key.startsWith('hand-')) el.style.setProperty('--' + key, value + 'px'); }
    const compact = el.clientWidth <= 800;
    const sideWidth = compact ? 84 : 142;
    const standX = Math.max(sideWidth/2+8, hud.left-sideWidth/2-14);
    const hitX = Math.min(el.clientWidth-sideWidth/2-8, hud.right+sideWidth/2+14);
    el.style.setProperty('--stand-x', standX+'px');
    el.style.setProperty('--hit-x', hitX+'px');
    el.style.setProperty('--action-y', Math.max(90, Math.min(el.clientHeight-(compact?180:158),hud.y-25))+'px');
    total.style.left = standX+'px';
    const scoreY = Math.max(110, Math.min(el.clientHeight-180, hud.y-48));
    total.style.top = scoreY+'px';
    el.style.setProperty('--secondary-y', (scoreY+(compact?30:62))+'px');
    // Stake stays below the cards, clear of the shared Hit/Deal target.
    const bet = el.querySelector(':scope > .cards-bet');
    if (bet) { bet.style.left = hud.x+'px'; bet.style.top = Math.min(el.clientHeight-40,hud.bottom+42)+'px'; bet.style.bottom = 'auto'; bet.style.right = 'auto'; bet.style.transform = 'translate(-50%,-50%)'; }
    ctx.stage?.emi?.attend?.((hud.x/el.clientWidth-.5)*2, .65);
    const key=hud.owner+':'+hud.total;
    paintScore(total,hud,hud.hands>1?t('br_cards_hand_short','Hand {i}',{i:hud.owner+1}):t('br_cards_you','You'),key,totalKey,still);totalKey=key;
    const dealer=table.hud('d'), badge=$('.cards-dealer-total'), dealerKey=dealer.total+':'+dealer.hidden;
    // Dealer subtotal is computed solely from painted face-up cards, never server hand totals.
    paintScore(badge,dealer,t('br_cards_dealer','Emi'),dealerKey,dealerTotalKey,still);dealerTotalKey=dealerKey;
    const phone=el.clientWidth<=800;
    badge.style.left=Math.max(46,Math.min(el.clientWidth-46,phone?dealer.x:dealer.left-57))+'px';
    badge.style.top=(phone?dealer.bottom+31:dealer.y)+'px';

  }

  function onKey(e) {
    if (!alive) return;
    if (e.key === 'Escape') { if (!e.defaultPrevented) { e.preventDefault(); back(); } return; }
    if (e.target && e.target.closest && e.target.closest('button, input, label, summary, select')) return;
    // Space and Enter deal (never on a held key's repeat). Moves have no letter keys: the room walks on WASD, and a
    // key still down from walking up to the table must never hit, stand or double.
    if ((e.code === 'Space' || e.key === 'Enter') && !e.repeat) { e.preventDefault(); deal(); }
  }

  /* ------------------------------------------------------------ lifecycle */
  async function open() {
    if (alive) return;
    alive = true; suspended = false; busy = false; decide = false; queue = []; shownHand = null; note = ''; lines = []; feelLog = [];
    sitting = 0; seating = false; firstSit = true; stakePicked = false; dealReadyAt = 0; screenUntil = 0; dropped = 0; phase = 'loading'; statusText = ''; lastStill = null;
    streak = 0; beatAt = {}; wordCursor = 0; lastCallout = null;
    sit = freshSit(); party = null; lastPlan = null; bankTicks = 0;   // one reward ledger per visit (Brake 3, Law IX)
    const my = ++session;
    el = build(); ctx.root.append(el);
    addEventListener('keydown', onKey);
    chip = createChip();
    // THE BANK (Law XII). Law X: the readout ticks as each token LANDS, each one a rung higher, and the
    // mini-thud waits for the END of the rollup's count - never for the end of the flight.
    bank = createCardsBank({
      layer: $('.cards-tokens'),
      onTick: (value, kind, tail) => { if (chip) chip.show(value); if (!tail) sound.play('token', { i: bankTicks++ }); },
      onLand: (kind, counting) => { if (counting) return; sound.play('token', { last: true }); if (chip) chip.thud(); },
      onDone: () => { if (chip) chip.show(null); },   // the room's Law I rule takes the chip back
    });
    moments = createMoments(ctx, { station: 'cards' });
    callout = createCallout({ ctx, mount: el, lex: typeof ctx.lex === 'function' ? ctx.lex : undefined });
    kit = createLoomKit({ still: dress().still, log: say });
    table = ctx.stage ? createTable3D(ctx.stage, { kit: () => kit, onDeal: deal, onCue: (cue) => sound.play(cue) }) : createTable($('.cards-stage'), { kit: () => kit, onCue: (cue) => sound.play(cue) });
    if (ctx.stage) createSeatLook(ctx.stage, { mount:el, enabled:()=>alive && !suspended && !dress().still,
      surface:e => {
        const hits=ctx.stage.pick(e,ctx.stage.scene.children).filter(h=>{for(let n=h.object;n;n=n.parent)if(!n.visible)return false;return true;});
        return hits[0]?.object.name === 'felt_surface';
      } });
    if (typeof ctx.onSp === 'function') unSp = ctx.onSp((v) => { if (chip) chip.setServer(v); });
    raf = requestAnimationFrame(frame);
    const deckP = createDeck(ctx, { count: 13, still: dress().still }).catch(() => null);
    const c = classify(await Promise.resolve(ctx.request('state', {})).catch(() => null));
    if (my !== session) { deckP.then((x) => x && x.dispose()); return; }
    $('.cards-loading').hidden = true;
    if (c.kind !== 'ok') {
      deckP.then((x) => x && x.dispose());
      phase = 'closed'; el.dataset.phase = 'closed';
      card(c.kind === 'closed' ? t('br_cards_closed', 'The table is closed for a moment.') : t('br_cards_offline', 'The house is not answering. Try again in a moment.'));
      return;
    }
    st = readState(c.body); chip.setServer(st.sp);
    stake = defaultStake(st.sp, st.rules.stakes);
    renderStakes();
    await sitDown(my, deckP);
  }

  function close() {
    if (!alive) return Promise.resolve();
    alive = false;
    ++session;
    removeEventListener('keydown', onKey);
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
    if (typeof unSp === 'function') unSp();
    unSp = null;
    // Law VI: every ceremony skips; the chip gets the plain server number; the kit, the deck and every hold go.
    dropTimers();
    // THE BANK leaves QUIETLY on a close (no mini-thud, no "+N"): the room's own chip is already on the
    // settled number the moment the station hands it back, and the climb is silenced with it.
    if (bank) { bank.skip(); bank.dispose(); }
    sound.stop('ladder');
    if (callout) { callout.dispose(); callout = null; }
    if (moments) { moments.cancel(); moments.dispose(); }
    if (kit) kit.dispose();
    if (deck) deck.dispose();
    if (chip) chip.handOver();
    ctx.stage?.emi?.attend?.(null,null);
    if (table) table.dispose();
    if (el) el.remove();
    el = null; table = null; kit = null; deck = null; moments = null; chip = null; bank = null; party = null; queue = []; shownHand = null; busy = false; decide = false;
    return Promise.resolve();
  }

  return {
    open, close,
    suspend(on) {
      if (!alive || suspended === !!on) return;
      suspended = !!on;
      if (suspended) {
        dropTimers();
        flush();
        if (table) table.skip(performance.now());
        moments.cancel();
        if (bank) bank.skip();   // Law VI: the readout settles at once and leaves quietly; the climb is hushed
        sound.stop('ladder');
        party = null;
        screenUntil = 0;   // the moments are cancelled, and the host's own suspend stops every overlay (gif_from too)
        if (raf) cancelAnimationFrame(raf);
        raf = 0;
        kit.dispose(); kit = createLoomKit({ still: dress().still, log: say });   // the GL context goes; a new one is made on the next draw
      } else if (!raf) raf = requestAnimationFrame(frame);
    },
    destroy() { close(); document.querySelectorAll('link[data-cards-css]').forEach((l) => l.remove()); },
    /** For dev.html and CDP checks only. */
    debug: () => ({
      phase, alive, busy, decide, suspended, hostBack, stake, stakePicked, hint, sitting, seating, queue: queue.length, dress: dress(), dropped, streak,
      screenLeftMs: Math.max(0, Math.round(screenUntil - performance.now())), dealText: el ? $('.cards-deal small').textContent : null,
      state: st && { sp: st.sp, legal: st.legal, hint: st.hint, hand: st.hand }, shown: shownHand,
      chip: chip && { kind: chip.kind, value: chip.value, server: chip.server, owed: chip.owed },
      // THE REWARD PASS (10.22): the last plan the spine handed out, what is still flying, and the sit-down ledger.
      reward: { plan: lastPlan, sit: { seen: sit.seen.slice(), heroes: sit.heroes },
        party: party && { spent: party.plan.spent, leftMs: Math.max(0, Math.round(party.until - performance.now())) },
        bank: bank && { busy: bank.busy, kind: bank.kind, settled: bank.settled } },
      status: statusText, controls: el ? view(performance.now()) : null, table: table && table.debug(), kit: kit && kit.debug(),
      deck: deck && { ...deck.debug(), keys: deck.keys, map: DECK_VALUES.map((v) => deck.keyFor(v)) }, moments: moments && moments.debug(), log: feelLog,
      callout: { last: lastCallout, pending: fxTimers.size, ...(callout ? callout.debug() : { shown: [] }) },
    }),
  };
}
