/* ============================================================================
 * station.js - the Daily Daze wheel station (CONTRACT.md section 7). The room
 * calls mount(ctx) once, then open()/close() per visit. Back is live at every
 * frame (Law VI): it never waits on the glb, the server or an animation.
 *
 *   wheel.js    layout, landing, countdown, result reading, Law I (pure)
 *   feel.js     THE HOUSE BOOK for the wheel: tiers, sounds, ladder, glance (pure)
 *   hypno.js    the v3 trance curves: last turn, quiet room, taffy, hub (pure)
 *   scene.js    the wheel glb close-up, one WebGL context per open()
 *   emi.js      EMI's face and poses on the perch
 *   readout.js  the one SP readout (ctx.spReadout when the room has it)
 *   bank.js / sound.js  THE BANK's tokens and the cues
 *
 * ONE FREE SPIN A DAY, plus earned slot chase credits. The server draws; the page answers the drag or the
 * button inside 100 ms (the wheel starts turning), then retargets the
 * deceleration onto result.sliceIndex whatever the drag strength. A reopen
 * after spinning shows the day's stored landing and a countdown to nextResetAt.
 *
 * HYPNO v3 (CONTRACT 10.13.F). Fullscreen effects go only through the kit's
 * moments: wheel.turn once when the long last turn starts (then its tunnel level
 * every frame), and one wheel.land.<size> on the landing frame. The Loom hub is
 * painted by the kit's one shared context; the deck deals eight small sources for the wedges and lends keys for effects.
 * Gates dress the page (spiral off: a brass star) from the first frame and live;
 * suspend and close cancel the moments and free the kit and the deck.
 *
 * THE DESKTOP RECIPE (feel.js FX_MOMENTS, owner ask 2026-09-15). On top of the
 * kit's moment the station fires the section 4 ids the host renders, one row a
 * moment: a word on a rim grab, a plum wash as the coast starts (the same for
 * every press, Law I), and on the landing frame the row for the result (small,
 * mid, big, the pot, Seeing Double, the gift, Head Empty) plus THE ALMOST when
 * the pointer rests one slice off the pot. feel.fxPlan applies Calm, the
 * cooldowns and the per-sit-down cap (gates no longer drop a step, owner
 * 2026-09-15); this file only posts what it returns. Back and suspend fire
 * nothing more (the host's station-close settles the desk).
 *
 * THE REWARD PASS (CONTRACT 10.22). The wheel used to carry half a copy of the
 * slot's kit and size its own restraint. It now asks shared/win/ for a rung and
 * OBEYS the plan it gets back: THE BANK is the shared engine (bank.js is the
 * elements alone), THE CHIME LADDER climbs over the rollup instead of ringing
 * once, THE GLOW and THE SPARKLE BURST are spent at the tiers plan.js allows,
 * ctx.revealedWin tells the room once a paid result is revealed (10.22.B), and
 * THE PRIZE MOMENT - the one reward in the room that is a thing and not a
 * number - gets THE REVEAL's curve and the burst under it. Law IX and Brakes 2,
 * 3 and 5 are NOT re-derived here; `sit` is the sit-down ledger and every party
 * that actually played is counted back into it.
 *
 * THE LANDING FLOW (shared/hypno/callout.js, owner 2026-09-15). On the frame the
 * pointer settles (Law I): the thud and the party at 0, the landed slice's rim
 * pulses to HIGHLIGHT_MS, and at FX_DELAY_MS the kit's moment, the desktop row
 * and the callout (feel.calloutFor) fire together. The tunnel level of the last
 * turn is not delayed. A Snooze gets no callout; the spin button is already
 * waiting for tomorrow, so nothing unlocks early.
 * ==========================================================================*/

import { layoutOf, landingAngle, resultIndex, readResult, restRotation, countdown, bonusSpinsOf, canSpinWheel } from './wheel.js';
import { recipe, tierOf, prizeTier, glance, landPose, pressPose, revealCount, FEEL, landMoment, nearMiss, fxPlan, freshCool, calloutFor } from './feel.js';
import { houseTier } from '../../shared/win/tier.js';
import { sitPlan, mergePlans, afterParty, freshSit } from '../../shared/win/plan.js';
import { ladderPlan } from '../../shared/win/ladder.js';
import { sparkBurst, warmGlow } from '../../../arcademy/shell/counterfx.js';
import { dressOf, edgeAlpha, captionAlpha, hubStill } from './hypno.js';
import { createLoomKit, createDeck, createMoments, wheelSize, strengthK, wheelTurnLevel, boxAround } from '../../shared/hypno/index.js';
import { createCallout, FX_DELAY_MS } from '../../shared/hypno/callout.js';
import { createScene } from './scene.js';
import { createRoomScene } from './room-scene.js';
export const roomStage = true;
import { createReadout, jackpotChip } from './readout.js';
import { createBank } from './bank.js';
import { createSound } from './sound.js';
import { rewardText, sliceText, rewardOdds, createRewardReveal } from './rewards.js';

const fmt = n => Number(n || 0).toLocaleString('en-US');
const wait = ms => new Promise(r => setTimeout(r, Math.max(0, ms)));
const mintId = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, '0')).join('');

function loadCss() {
  if (document.querySelector('link[data-wheel-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet'; link.href = new URL('./station.css', import.meta.url).href; link.dataset.wheelCss = '';
  document.head.append(link);
}

export async function mount(ctx) {
  const t = (key, fallback, vars = {}) => {
    const s = typeof ctx.lex === 'function' ? ctx.lex(key, fallback) : fallback;
    return String(s ?? fallback).replace(/\{(\w+)\}/g, (_, k) => (k in vars ? vars[k] : `{${k}}`));
  };
  const prefersReduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  // Law VI: reduced motion and Calm land at the settled state. Read at each open and on every settings frame (the
  // room's getters are live), so a player lowering MotionLevel mid-visit gets the plain dress at once.
  let reduced = false, calm = false, still = false;
  const readMotion = () => {
    reduced = !!ctx.reduced || prefersReduced; calm = String(ctx.intensity || '').toLowerCase() === 'calm'; still = reduced || calm;
  };
  readMotion();
  const hostBack = ctx.hostBack === true;
  const hypnoCtx = () => ({ intensity: String(ctx.intensity || 'normal').toLowerCase(), reduced, gates: ctx.gates });
  const hubHeld = () => hubStill({ osReduced: prefersReduced, motion: ctx.motion });   // Calm turns it at half; only OS reduced motion (or Motion Off) holds it
  loadCss();

  let el = null, scene = null, readout = null, bank = null, sound = null, session = 0, alive = false, suspended = false;
  let st = null, layout = null, busy = false, pose = 'idle0_0', unSp = null, timer = 0, refreshAt = 0, lines = [], lineAt = 0;
  let rewardReveal = null;
  let gainTimer = 0, glanceTimer = 0, feelLog = [], revealAt = -Infinity;
  let moments = null, kit = null, deck = null, unSettings = null, dress = dressOf(), hubPainted = false, turnPlayed = false, lastMoment = null, dealSeq = 0;
  let fxCool = freshCool(), wordKeys = [], lastFx = null;
  let sit = freshSit(), lastPlan = null;   // Brake 3's ledger: one per sit-down, counted only by parties that played
  let callout = null, landTimer = 0, lastCallout = null;   // the landing flow: the callout and the delayed fx frame
  const $ = sel => el.querySelector(sel);
  const note = (what, extra = {}) => { feelLog = [...feelLog.slice(-79), { what, at: Math.round(performance.now()), ...extra }]; };

  function build() {
    const root = document.createElement('div');
    root.className = 'wheel-station'; if (ctx.stage) root.dataset.seated = '';  root.dataset.phase = 'loading';
    root.innerHTML = `
      <div class="wheel-dim"></div>
      ${ctx.stage ? '' : `<canvas class="wheel-stage" aria-label="${t('br_wheel_stage', 'Daily Daze wheel. Drag the rim to spin.')}"></canvas>`}
      <div class="wheel-edges" aria-hidden="true"></div>
      <div class="wheel-slowly" aria-hidden="true">${t('br_wheel_slowly', 's l o w l y')}</div>
      <header class="wheel-top">
        <button class="wheel-back" type="button">&larr; ${t('br_wheel_back', 'Back')}</button>
        <span class="wheel-sp"></span><span class="wheel-jackpot"></span>
        <div class="wheel-status" aria-live="polite"></div>
      </header>
      <div class="wheel-emi" aria-hidden="true"><canvas class="wheel-face" width="152" height="137"></canvas><span class="wheel-zzz" hidden>z Z z</span></div>
      <span class="wheel-gain" hidden></span>
      <div class="wheel-tokens" aria-hidden="true"></div>
      <button class="wheel-spin" type="button"><span></span><small></small></button>
      <details class="wheel-odds"><summary>${t('br_wheel_odds', 'Odds')}</summary><table></table><p></p></details>
      <div class="wheel-card" role="status" hidden><p></p><button class="wheel-card-back" type="button">${t('br_wheel_back', 'Back')}</button></div>
      <div class="wheel-loading">${t('br_wheel_loading', 'Dusting off the wheel')}</div>`;
    root.querySelector('.wheel-back').onclick = back;
    root.querySelector('.wheel-card-back').onclick = back;
    if (hostBack) { root.dataset.hostBack = ''; root.querySelector('.wheel-back').hidden = true; root.querySelector('.wheel-card-back').hidden = true; }
    root.querySelector('.wheel-spin').onclick = () => press();
    return root;
  }

  function back() {
    if (el && !hostBack) $('.wheel-back').classList.add('is-ringing');   // Law VIII
    if (typeof ctx.standUp === 'function') ctx.standUp(); else close();
  }
  function card(text) { if (!el) return; $('.wheel-card p').textContent = text || ''; $('.wheel-card').hidden = !text; }
  const closedText = () => t('br_wheel_closed', 'The wheel is closed for a moment.');

  /* ------------------------------------------------------------ readouts */
  const clock = () => (st ? countdown(st.nextResetAt, Date.now()) : null);
  function resultLine(r, carry) {
    if (!r) return '';
    const special = rewardText(r, t);
    if (special) return special + (r.capped ? ' ' + t('br_wheel_capped', 'Your SP is at the cap.') : '');
    const row = layout && layout[resultIndex(layout, { sliceId: r.sliceId, sliceIndex: r.sliceIndex })];
    const name = row ? t(`br_wheel_slice_${row.id.replace(/_[a-z]$/, '')}`, row.label) : '';
    let s = r.jackpotWon ? t('br_wheel_jackpot_won', 'JACKPOT! +{n} SP.', { n: fmt(r.pay) })
      : r.snoozed ? t('br_wheel_snoozed', 'Snooze. +{n} SP tomorrow.', { n: fmt(carry) })
      : t('br_wheel_won', '{name}: +{n} SP.', { name, n: fmt(r.pay) });
    if (r.fallback && row?.id === 'dazed') s += ' ' + t('br_wheel_fallback', 'The star slipped past, so Dazed pays instead.');
    if (r.carryPaid > 0) s += ' ' + t('br_wheel_carry_paid', '+{n} SP from Snooze.', { n: fmt(r.carryPaid) });
    if (r.capped) s += ' ' + t('br_wheel_capped', 'Your SP is at the cap.');
    return s;
  }
  function sync() {
    if (!el || !st) return;
    const c = clock(), r = st.spun ? readResult(st.result) : null, j = st.jackpot || {};
    const credits = bonusSpinsOf(st), available = canSpinWheel(st);
    const bonusReady = t('br_wheel_bonus_ready', 'Bonus spins ready: {n}. No daily wait.', { n: credits });
    $('.wheel-jackpot').textContent = jackpotChip(j, t, fmt);   // MUST HIT in place of the odds (10.16.E)
    $('.wheel-jackpot').classList.toggle('is-must-hit', j.mustHit === true);
    const status = busy ? t('br_wheel_spinning', 'Round it goes...')
      : credits > 0 && st.spun ? bonusReady
      : r ? `${resultLine(r, st.snoozeCarry)}\n${c ? t('br_wheel_next', 'Next spin in {time}.', { time: c.text }) : ''}`
      : t('br_wheel_ready', 'Your free spin is ready. Drag the rim or press Spin.');
    if ($('.wheel-status').textContent !== status) $('.wheel-status').textContent = status;
    const spin = $('.wheel-spin');
    spin.disabled = el.dataset.phase !== 'play' || busy || !available;
    spin.querySelector('span').textContent = st.spun && credits > 0 ? t('br_wheel_bonus_spin', 'Bonus spin') : !available ? t('br_wheel_come_back', 'Come back') : t('br_wheel_spin', 'Spin');
    spin.querySelector('small').textContent = st.spun && credits > 0 ? t('br_wheel_bonus_count', 'Bonus spins: {n}', { n: credits }) : !available && c ? c.text : t('br_wheel_free', 'Free today');
    if (!scene) return;
    scene.screen('title_screen', t('br_wheel_title', 'DAILY DAZE'));
    const now = performance.now();
    const rq = (now - revealAt) / FEEL.REVEAL_MS, counting = r && r.jackpotWon && rq >= 0 && rq < 1;   // THE REVEAL counts the pot up
    const want = r ? [rewardText(r, t) || (r.jackpotWon ? t('br_wheel_screen_jackpot', 'JACKPOT +{n}', { n: fmt(counting ? revealCount(r.pay, rq) : r.pay) }) : r.snoozed ? t('br_wheel_screen_snooze', 'SNOOZE +{n} TOMORROW', { n: fmt(st.snoozeCarry) }) : t('br_wheel_screen_win', '+{n} SP', { n: fmt(r.total) })),
                     credits > 0 ? t('br_wheel_bonus_count', 'Bonus spins: {n}', { n: credits }) : c ? t('br_wheel_screen_next', 'NEXT {time}', { time: c.text }) : '']
      : [busy ? t('br_wheel_screen_spinning', 'ROUND IT GOES') : t('br_wheel_screen_ready', 'GIVE IT A SPIN'), t('br_wheel_screen_pot', 'JACKPOT {n}', { n: fmt(j.amount) })];
    if (want.join() !== lines.join()) { lines = want; }
    const which = Math.floor((now - lineAt) / 3200) % 2;   // two lines, one turn every 3.2 s (never a flicker)
    scene.screen('status_screen', lines[which] || lines[0], !!(r && r.jackpotWon));
  }
  function renderOdds() {
    const rows = layout.map(s => {
      const tr = document.createElement('tr');
      const name = t(`br_wheel_slice_${s.id.replace(/_[a-z]$/, '')}`, s.label);
      for (const [tag, text] of [['th', name], ['td', rewardOdds(s, t, fmt)], ['td', s.odds]]) {
        const cell = document.createElement(tag); cell.textContent = text; tr.append(cell);
      }
      return tr;
    });
    $('.wheel-odds table').replaceChildren(...rows);
    const j = st.jackpot || {}, notes = [t('br_wheel_odds_note', 'One free spin a day. The slice sizes are the picture; these are the real odds.')];
    if (st.bonusSpins != null) notes.push(t('br_wheel_bonus_rules', 'Bonus spins use the standard rewards. The growing jackpot is daily only.'));
    if (j.eligible === false) notes.push(t('br_wheel_young', 'The jackpot opens to accounts a few days old.'));
    else if (j.wonToday) notes.push(t('br_wheel_taken', "Today's jackpot is taken. It starts again tomorrow."));
    else if (j.mustHit === true) notes.push(t('br_wheel_must_hit_room', 'The pot has to fall today'));
    if (st.snoozeCarry > 0) notes.push(t('br_wheel_carry', 'Snooze carry: +{n} SP on your next spin.', { n: fmt(st.snoozeCarry) }));
    $('.wheel-odds p').textContent = notes.join(' ');
  }

  /* ---------------------------------------------------------------- feel */
  /**
   * The spine's ctx (CONTRACT 10.22.C). `reduced` and `still` are NOT the same flag and this station used to
   * conflate them: reduced motion takes the settled STATE with no travel at all, while Calm strips the
   * decoration (the shower, the sparks, the reveal) and the value STILL MOVES, because a number that just
   * changes is a Law XII break at every motion level. `lite` is Calm too, exactly as at the slot (Brake 8:
   * four tokens, no particles, every sound kept).
   *
   * `melted` is never set: the wheel has no focus state of its own - no trance, no melt, no halved spin - so
   * there is nothing here for Brake 5 to quiet. The hypno dress is the room's mood, not the player's.
   */
  const planCtx = () => ({ reduced, still: calm, lite: calm || ctx.lite === true });

  function setFace(name) { pose = name; if (scene) scene.setFace(name); }   // emi.js mirrors the face into the HUD canvas
  function glanceTo(want, rest) {
    clearTimeout(glanceTimer); setFace(glance(pose, want)); note('glance', { pose });
    if (rest) glanceTimer = setTimeout(() => { if (alive && pose !== rest) setFace(rest); }, FEEL.GLANCE_HOLD_MS);
  }
  function gain(text) {
    const g = el && $('.wheel-gain');
    if (!g) return;
    g.textContent = text; g.hidden = false;
    clearTimeout(gainTimer); gainTimer = setTimeout(() => { if (el) $('.wheel-gain').hidden = true; }, 2400);
  }
  /* --------------------------------------------------------------- hypno */
  /** The kit's Loom context paints the hub; none while suspended, and a still hub is painted once. */
  function paintHub(canvas, angle, now) {
    if (!alive || suspended) return false;
    if (hubHeld() && hubPainted) return false;
    if (!kit) { kit = createLoomKit({ still: hubHeld(), log: m => note('loom', { m }) }); hubPainted = false; }
    hubPainted = kit.paint(canvas, 'hub', { now, angle });
    return hubPainted;
  }
  /** Every frame from the scene: the stage edges, the caption, and wheel.turn's tunnel level (10.13.F). */
  function onFrame({ dim, slowing }) {
    if (!el) return;
    const edges = $('.wheel-edges'), cap = $('.wheel-slowly'), ea = edgeAlpha(dim, dress.k).toFixed(3), ca = captionAlpha(dim).toFixed(3);
    if (edges.style.opacity !== ea) edges.style.opacity = ea;
    if (cap.style.opacity !== ca) cap.style.opacity = ca;
    if (!moments || suspended) return;
    if (slowing && !turnPlayed) { turnPlayed = true; if (sound) sound.slowing(); const out = moments.play('wheel.turn'); note('moment', { id: 'wheel.turn', tokens: out.tokens.length, page: out.page }); }
    if (!slowing && dim < 0.01) turnPlayed = false;
    moments.tunnel(wheelTurnLevel(dim));
  }
  function applyDress() {
    readMotion();
    dress = dressOf(hypnoCtx());
    rewardReveal?.setStill(still);
    if (scene) { scene.setReduced(still); scene.setDress(dress); }
    if (kit) kit.setStill(hubHeld());
    if (deck) deck.setStill(still || dress.calm);   // the deck feeds a visible surface now: OS reduced motion holds it too
    hubPainted = false;
    if (el) el.dataset.hub = dress.hub;
  }
  function dealDeck(my) {
    const mine = ++dealSeq;
    // The deck keeps the picture keys; the word keys of the same deal (s0..s3) are kept here for the fx.sub_* rows.
    const media = typeof ctx.media === 'function' ? {
      media: o => Promise.resolve(ctx.media(o)).then(rep => {
        if (mine === dealSeq) wordKeys = Array.isArray(rep && rep.words) ? rep.words.map(w => w && w.key).filter(k => typeof k === 'string' && /^s\d{1,2}$/.test(k)) : [];
        return rep;
      }) } : {};
    createDeck(media, { count: 8, still: dress.calm }).then(d => {
      if (mine !== dealSeq || my !== session || suspended || !alive) { d.dispose(); return null; }
      if (deck) deck.dispose();
      deck = d; return d;
    }).catch(() => null);
  }
  /** Centre the stage edges on the wheel (on open and resize, never per frame). */
  function centreEdges() {
    const p = scene && el && scene.project('wheel_rotor'), r = el && el.getBoundingClientRect();
    if (!p || !r) return;
    el.style.setProperty('--wheel-x', `${Math.round(p.x - r.left)}px`); el.style.setProperty('--wheel-y', `${Math.round(p.y - r.top)}px`);
  }
  /** THE DESKTOP RECIPE: one section 4 id to the host, never awaited (fx-ack is advisory), nothing while suspended. */
  function fireFx(fxId, symbols, args) {
    if (suspended || !alive || typeof ctx.fx !== 'function') return false;
    try {
      const p = ctx.fx(fxId, symbols, args);
      if (p && typeof p.catch === 'function') p.catch(() => {});
    } catch (e) { console.warn('[wheel] fx failed', e); return false; }
    return true;
  }
  /** Fire feel.js's row for `moment` (gates, Calm, cooldown and the cap applied there), `seed` picking the keys. */
  function playFx(moment, seed = '') {
    if (suspended || !alive) return [];
    readMotion();
    const plan = fxPlan(moment, { still, gates: ctx.gates, cool: fxCool, now: performance.now(), gifs: deck ? deck.keys : [], words: wordKeys, seed });
    fxCool = plan.cool;
    const ids = [];
    for (const f of plan.fx) if (fireFx(f.id, f.symbols, f.args)) ids.push(f.id);
    lastFx = { moment, ids, why: plan.why, still };
    note('fx', lastFx);
    return ids;
  }
  /** Law VI: whatever the landing still owed the desk (the delayed fx frame, the callout) is dropped now. */
  function dropLanding() {
    clearTimeout(landTimer); landTimer = 0;
    if (callout) callout.cancel();
  }
  function freeHypno() {
    dropLanding();
    if (moments) moments.cancel();
    if (kit) { kit.dispose(); kit = null; }
    if (deck) { deck.dispose(); deck = null; }
    dealSeq++; hubPainted = false; turnPlayed = false;
  }
  /** The landing moment, sized to the prize, on the frame the result shows (Law I). */
  function fire(raw, r, idx) {
    if (r.reward?.kind === 'nothing' || r.reward?.kind === 'decoration' || r.reward?.kind === 'double' || suspended || !moments || !scene || idx < 0) return;   // no slice to point at: the wheel wound down, no moment
    const id = 'wheel.land.' + wheelSize(raw);
    const p = scene.project('landed');
    const from = p ? boxAround(p.x, p.y, 60, 44) : undefined;
    const gif = deck ? deck.pickKey(`${r.day}|${r.sliceId}|${idx}`) : 'g0';
    const out = moments.play(id, { color: scene.sliceColor(idx), from, gif });
    if (out.page.includes('quiet_room')) scene.quiet(strengthK(hypnoCtx()));
    lastMoment = { id, gif, from, tokens: out.tokens.length, page: out.page };
    note('moment', lastMoment);
  }
  /**
   * THE PLAN for one landing (CONTRACT 10.22.C). The station asks for a rung and obeys what comes back: Law IX's
   * sizing and Brakes 2, 3 and 5 live in shared/win/plan.js and are re-derived nowhere, least of all here.
   *
   * TWO ASKS, ONE PARTY. The pay's rung is the wheel's own `tierOf` handed in whole (houseTier rule 3: a
   * station's numeric tier is authoritative and is never raised by its pay). THE PRIZE MOMENT is asked for
   * separately, because a decoration or a Seeing Double pays nothing and `tierOf` rightly calls that a 2,
   * while the ARRIVAL of the only reward in the room that is a thing and not a number is a big beat. Brake 2
   * folds the two into the HIGHER, never the sum: one party on the frame, never two stacked.
   */
  function planFor(r) {
    const pay = sitPlan(houseTier({ station: 'wheel', tier: tierOf(r) }), sit, planCtx());
    const prize = prizeTier(r) ? sitPlan(houseTier({ station: 'wheel', tier: prizeTier(r) }), sit, planCtx()) : null;
    return mergePlans(pay, prize);
  }

  /** The landing beat (Law X): THE THUD, the party the PLAN allows, the tokens and EMI, on one frame. */
  function land(r, gained, fresh, raw, idx) {
    const plan = planFor(r), rec = recipe(r, { still, plan }), tier = tierOf(r);
    // A cloche is arriving on this frame (a prize, a Head Empty, a completed collection): Brake 2 gives the
    // beat ONE sparkle burst, and when there is a cloche the burst belongs under it, not out on the stage.
    const cloche = fresh && !!rewardText(r, t);
    lastPlan = plan;
    lineAt = performance.now();   // the result line shows first
    if (fresh && plan.reveal) { revealAt = lineAt; const step = () => { if (!alive || performance.now() - revealAt > FEEL.REVEAL_MS + 40) return; sync(); requestAnimationFrame(step); }; requestAnimationFrame(step); }
    if (r.reward?.kind !== 'nothing') sound.thud(tier === 0);
    if (fresh && r.reward?.kind !== 'nothing') {
      scene.celebrate(rec); sound.win(rec.sound);
      // THE CHIME LADDER (10.22.D): the landing note win() just played is step 0, and the steps after it climb
      // a semitone apart across THE BANK's rollup, so a big landing RISES instead of ringing once - the flat
      // cue by tier this station shipped with. Law VI: reduced motion has partyMs 0 and therefore nothing to
      // climb over, so the landing note is the whole ladder and no sound plays without a visual under it.
      sound.climb(ladderPlan(plan.spent, plan.partyMs, plan.octave < 0), plan.octave);
      // THE GLOW (10.22.D): a warm cut on the chip that just changed, in fast and out slow. Calm keeps it - a
      // cut is not travel - and a landing that paid NO SP does not get it, because the glow says LOOK AT THE
      // NUMBER and the number did not move (a gift's own arrival is its event). THE SPARKLE BURST is the
      // garnish and never the event: one a beat, from the stage the tokens fly over, or from under the cloche.
      if (plan.glow > 0 && gained > 0) warmGlow(readout.glowNode());
      if (plan.sparkle > 0 && !cloche) sparkBurst($('.wheel-tokens'), { count: plan.sparkle });
    }
    if (fresh) {
      // THE LANDING FLOW (callout.js): the slice glows from this frame; at FX_DELAY_MS the kit's moment, the desktop
      // row (THE ALMOST with it) and the callout fire together. A Snooze glows nothing and names nothing.
      const seed = `${r.day}|${r.sliceId}|${idx}`, co = calloutFor(r), my = session;
      if (co && idx >= 0) scene.hit(idx);
      clearTimeout(landTimer);
      landTimer = setTimeout(() => {
        landTimer = 0;
        if (my !== session || !alive || suspended) return;
        if (r.reward?.kind !== 'nothing') fire(raw, r, idx);
        playFx(landMoment(r), seed);
        if (nearMiss(layout, idx, r)) playFx('nearMiss', seed);
        if (co && callout) { callout.show(co.key, co.fallback, { tier: co.tier }); lastCallout = { key: co.key, tier: co.tier, at: Math.round(performance.now()) }; note('callout', lastCallout); }
      }, FX_DELAY_MS);
    }
    // THE ANNOUNCEMENT (10.22.B): the room learns what the player has just learnt, once, on the revealed
    // frame, and never about a miss - a snooze, a Head Empty and a gift that paid nothing say nothing at all.
    // The tier is the spine's `plan.shower`, not the wheel's rung, and a shower of 0 SKIPS the call: room/
    // coin-shower.js clamps 1..4, so passing a 0 would show a small win from across the room (Law IX).
    if (fresh && r.pay > 0 && plan.shower > 0) ctx.revealedWin?.(r.pay, plan.shower, t('br_wheel_screen_win', '+{n} SP', { n: fmt(r.total) }));
    // Law XIII: EMI reacts. The wheel's own landPose is richer than the plan's rung (it knows a gift from a
    // doubling from a doze), so it overrides `plan.emi` - a station may override the pose, never skip it.
    glanceTo(landPose(r));
    $('.wheel-zzz').hidden = !r.snoozed;
    if (gained > 0 && fresh) {
      // Law XII + Law X: the tokens fly from the landed face to the readout, which ticks as each one LANDS and
      // then keeps counting for the rest of the party (plan.partyMs) with the mini-thud held to the end of it.
      bank.start({ n: plan.bank, fromValue: readout.server - gained, toValue: readout.server, rollupMs: plan.partyMs,
                   from: () => scene && scene.project('landed'), to: () => readout.target() });
    } else readout.settle();
    // THE PRIZE MOMENT (10.22): the cloche arrives on THE REVEAL's curve with the beat's one burst under it.
    if (cloche) { rewardReveal.show(r, { still, plan }); rewardReveal.useModel(scene.revealReward?.(r, still)); }
    if (fresh && !cloche) gain(r.snoozed ? t('br_wheel_gain_snooze', '+{n} tomorrow', { n: fmt(st.snoozeCarry) }) : t('br_wheel_gain', '+{n} SP', { n: fmt(gained) }));
    // Brake 3's memory, and only for a party that actually played: a reopen replays nothing and costs nothing,
    // and afterParty counts a hero only when plan.reveal fired, so a jackpot under Calm keeps the sit-down's.
    if (fresh) sit = afterParty(sit, plan);
    note('land', { slice: r.sliceId, pay: r.pay, total: r.total, gained, tier, spent: plan.spent, why: plan.why,
                   party: rec.sound, shower: plan.shower, fresh, still });
  }

  /* ---------------------------------------------------------------- spin */
  async function ask(idem, my) {
    let tries = 0;
    for (;;) {
      const res = await Promise.resolve(ctx.request('spin', { idem }, idem)).catch(() => ({ ok: false, reason: 'offline' }));
      if (my !== session) return { kind: 'gone' };
      const body = res && res.body;
      if (res && res.ok && res.status === 403) return { kind: 'closed' };
      if (res && res.ok && body && body.ok) return { kind: 'result', body };
      const reason = body && body.reason ? body.reason : res && res.reason;
      if (reason === 'closed') return { kind: 'closed' };
      if (reason === 'already_spun' && body && body.result) return { kind: 'already', body };
      if (++tries > 3) return { kind: 'failed', reason };
      if (reason === 'busy') await wait(650);                                   // HTTP 200 on the wheel
      else if (reason === 'too_fast') await wait(Math.min(3000, Number(body.retryInMs) || 1000));
      else if (reason === 'timeout') await wait(250);                           // same idem: the receipt answers
      else return { kind: 'failed', reason };
      if (my !== session) return { kind: 'gone' };
    }
  }

  async function press(omega) {
    if (!alive || suspended || !scene || el.dataset.phase !== 'play' || busy) return;
    sound.arm();
    const my = session, pressedAt = performance.now();
    if (!canSpinWheel(st)) { glanceTo(pressPose(), landPose(readResult(st.result))); $('.wheel-spin').classList.add('is-ringing'); setTimeout(() => el && $('.wheel-spin').classList.remove('is-ringing'), 400); return; }
    // Law VIII: the wheel turns (or, still, the button rings) and EMI glances on this frame.
    rewardReveal.hide();
    busy = true; sound.start(); scene.coast(omega); scene.setMood('spin'); glanceTo(pressPose());
    playFx('coast');   // the same plum wash for every press: it says nothing about where the wheel stops (Law I)
    $('.wheel-spin').classList.add('is-ringing'); card(null); lineAt = performance.now(); sync();
    note('answer', { ms: Math.round(performance.now() - pressedAt), omega: omega || null });
    const before = readout.server;
    const a = await ask(mintId(), my);
    if (my !== session || !alive || a.kind === 'gone') return;
    $('.wheel-spin').classList.remove('is-ringing');
    if (a.kind === 'closed' || a.kind === 'failed') {
      await scene.windDown();
      if (my !== session) return;
      busy = false; scene.setMood('idle'); glanceTo('idle0_0');
      card(a.kind === 'closed' ? closedText() : t('br_wheel_offline', 'The house is not answering. Try again in a moment.'));
      note('refused', { reason: a.kind === 'closed' ? 'closed' : a.reason }); sync();
      return;
    }
    const b = a.body, r = readResult(b.result);
    const replyLayout = layoutOf(b.slices);
    if (replyLayout) { layout = replyLayout; scene.setLayout(layout); }
    const idx = resultIndex(layout, b.result);
    // Nothing on screen may tell the result before the pointer does: the state is adopted on the landing frame.
    const next = { ...st, spun: true, bonusSpins: bonusSpinsOf(b), result: b.result, snoozeCarry: Number(b.snoozeCarry) || 0, nextResetAt: b.nextResetAt || st.nextResetAt, jackpot: b.jackpot || st.jackpot };
    const gained = a.kind === 'result' ? Math.max(0, Number(b.sp) - before) : 0;
    readout.owe(gained); readout.setServer(b.sp);                                // Law I: held back until it lands
    if (idx < 0) { await scene.windDown(); } else await scene.land(landingAngle(layout, idx, r.day), idx);
    if (my !== session || !alive) return;
    busy = false; st = next;
    land(r, gained, a.kind === 'result', b.result, idx);
    if (a.kind === 'result') ctx.rewardLanded?.(b);
    if (a.kind === 'already') card(t('br_wheel_already', 'Already spun today. Here is where it landed.'));
    renderOdds(); sync();
  }

  function onKey(e) {
    if (!alive) return;
    if (e.key === 'Escape') { e.preventDefault(); back(); return; }
    if (e.target && e.target.closest && e.target.closest('summary, input, select')) return;
    if (e.code === 'Space' || e.key === 'Enter') { e.preventDefault(); press(); }   // the station owns Space and Enter while open
  }
  const onResize = () => { if (scene) { scene.resize(); centreEdges(); } };

  async function refresh(my) {
    const res = await Promise.resolve(ctx.request('state', {})).catch(() => null);
    if (my !== session || !res || !res.ok || !res.body || !res.body.ok || busy) return;
    const nextLayout = layoutOf(res.body.slices);
    if (!nextLayout) { card(closedText()); return; }
    st = res.body; layout = nextLayout; scene.setLayout(layout); readout.setServer(st.sp);
    if (st.spun && st.result) scene.setRotation(restRotation(layout, st.result, readResult(st.result).day) ?? 0, resultIndex(layout, st.result));
    if (!st.spun) { scene.setRotation(scene.rotation % (Math.PI * 2)); scene.setMood('idle'); setFace('idle0_0'); $('.wheel-zzz').hidden = true; }
    renderOdds(); sync();
  }
  function everySecond() {
    if (!alive || !st) return;
    const c = clock();
    if (st.spun && c && c.due && Date.now() > refreshAt) { refreshAt = Date.now() + 30000; setTimeout(() => refresh(session), 1500); }
    sync();
  }

  /* ------------------------------------------------------------ lifecycle */
  async function open() {
    if (alive) return;
    alive = true; busy = false; suspended = false; pose = 'idle0_0'; feelLog = []; lines = []; lineAt = performance.now(); lastMoment = null;
    fxCool = freshCool(); wordKeys = []; lastFx = null; lastCallout = null;   // a sit-down starts with every cooldown and cap fresh
    sit = freshSit(); lastPlan = null;   // Brake 3 and the once-a-sit-down hero start fresh with it
    const my = ++session;
    readMotion(); dress = dressOf(hypnoCtx());
    el = build(); ctx.root.append(el); rewardReveal = createRewardReveal(el, t); el.dataset.hub = dress.hub;
    moments = createMoments(ctx, { station: 'wheel' });
    callout = createCallout({ mount: el, lex: typeof ctx.lex === 'function' ? ctx.lex : undefined });
    if (typeof ctx.onSettings === 'function') unSettings = ctx.onSettings(() => { if (alive) applyDress(); });
    addEventListener('keydown', onKey); addEventListener('resize', onResize);
    readout = createReadout({ ctx, own: $('.wheel-sp'), format: n => t('br_wheel_sp', '{n} SP', { n: fmt(n) }) });
    if (readout.kind !== 'own') el.dataset.hostSp = '';
    sound = createSound();
    // Law XII: only OS reduced motion / Motion off takes the value without the flight. Calm STILL FLIES - the
    // plan has already capped it at four tokens (Brake 8) - so `reduced` here is not the station's `still`.
    bank = createBank({ layer: $('.wheel-tokens'), reduced: () => reduced,
      onTick: (v, quiet) => { readout.show(v); if (!quiet) sound.token(false); },
      onLand: () => { sound.token(true); readout.thud(still); },
      onDone: () => readout.settle() });
    if (typeof ctx.onSp === 'function') unSp = ctx.onSp(v => { if (readout) readout.setServer(v); });
    dealDeck(my);   // count 4: the wheel only lends fx.gif_from a key (10.13.C)
    const [made, res] = await Promise.all([
      (ctx.stage ? createRoomScene : createScene)({ stage: ctx.stage, canvas: $('.wheel-stage'), hud: $('.wheel-face'), reduced: still, dress, paintHub, onFrame,
        labels: s => sliceText(s, t, fmt),
        // A GETTER, never the deck itself: dealDeck replaces it after the scene exists, and suspend frees it.
        sliceMedia: () => deck,
        canSpin: () => !busy && !suspended && canSpinWheel(st),
        onGrab: ok => { sound.arm(); if (!ok) press(); else { glanceTo(pressPose()); playFx('grab'); } },
        onRelease: omega => press(omega),
        onTick: semis => sound.tick(semis) }).catch(e => ({ error: e })),
      Promise.resolve(ctx.request('state', {})).catch(() => null),
    ]);
    if (my !== session) { if (made && made.dispose) made.dispose(); return; }
    $('.wheel-loading').hidden = true;
    const fail = text => { if (made && made.dispose) made.dispose(); el.dataset.phase = 'closed'; card(text); };
    if (made.error) { console.error('[wheel] model failed to load', made.error); return fail(closedText()); }
    if (made.missing.length) return fail(t('br_wheel_model_missing', 'Model missing {n}', { n: made.missing.join(', ') }));
    if (!res || !res.ok || res.status === 403 || !res.body || !res.body.ok) return fail(closedText());   // no state, no fallback table
    st = res.body; layout = layoutOf(st.slices);
    if (!layout) return fail(closedText());
    scene = made; scene.setDress(dress); scene.setLayout(layout); readout.setServer(st.sp); readout.settle();
    renderOdds();
    const stored = st.spun ? readResult(st.result) : null;
    if (stored) {   // a reopen shows the day's landing, settled, no party and no second bank
      scene.setRotation(restRotation(layout, st.result, stored.day) ?? 0, resultIndex(layout, st.result));
      setFace(landPose(stored)); if (stored.snoozed) scene.setMood('sleepy');
      $('.wheel-zzz').hidden = !stored.snoozed;
    } else setFace('idle0_0');
    el.dataset.phase = 'rise'; sync();
    timer = setInterval(everySecond, 1000);
    await scene.rise();
    if (my !== session) return;
    el.dataset.phase = 'play'; centreEdges(); sync();
  }

  async function close() {
    if (!alive) return;
    alive = false;
    const my = ++session;
    removeEventListener('keydown', onKey); removeEventListener('resize', onResize);
    clearInterval(timer); clearTimeout(gainTimer); clearTimeout(glanceTimer);
    if (typeof unSp === 'function') unSp();
    if (typeof unSettings === 'function') unSettings();
    unSp = null; unSettings = null;
    freeHypno();
    rewardReveal?.dispose(); rewardReveal = null;
    if (moments) { moments.dispose(); moments = null; }
    if (callout) { callout.dispose(); callout = null; }
    // Law VI: Back skips every ceremony to its settled state and hands the readout the plain server number,
    // and the rest of THE CHIME LADDER is taken back rather than played faster.
    if (sound) sound.hush();
    if (bank) { bank.skip(); bank.dispose(); }
    if (readout) readout.dispose();
    if (sound) sound.dispose();
    const s = scene, root = el;
    if (s) s.skip();
    if (root) root.dataset.phase = 'leaving';
    if (s) await Promise.race([s.sink(), wait(320)]);
    if (s) s.dispose();
    if (root) root.remove();
    if (my === session) { scene = null; el = null; readout = null; bank = null; sound = null; busy = false; }
  }

  return {
    open, close,
    suspend(on) {
      const was = suspended;
      suspended = !!on;
      if (suspended && !was) freeHypno();
      else if (!suspended && was && alive) dealDeck(session);   // the kit comes back on the next hub paint
      if (sound) sound.suspend(suspended);
      if (suspended && sound) sound.hush();
      if (suspended && bank) bank.skip();
      if (suspended && scene) scene.skip();
      if (el) sync();
    },
    async destroy() { await close(); document.querySelectorAll('link[data-wheel-css]').forEach(l => l.remove()); },
    /** For dev.html and CDP checks only. */
    debug: () => ({ phase: el && el.dataset.phase, alive, busy, still, hostBack, state: st, pose,
                    readout: readout && { kind: readout.kind, value: readout.value, server: readout.server, owed: readout.owed },
                    status: el && $('.wheel-status').textContent, spin: el && $('.wheel-spin').textContent,
                    feel: { log: feelLog, cues: sound ? sound.trace.slice() : [], scene: scene && scene.debug() },
                    fx: { last: lastFx, cool: fxCool, words: wordKeys.slice(), pending: !!landTimer },
                    callout: { last: lastCallout, ...(callout ? callout.debug() : { shown: [] }) },
                    plan: lastPlan, sit,
                    hypno: { dress, lastMoment, turnPlayed, moments: moments && moments.debug(), kit: kit && kit.debug(), deck: deck && deck.debug(),
                             edges: el && Number($('.wheel-edges').style.opacity || 0), caption: el && Number($('.wheel-slowly').style.opacity || 0) } }),
  };
}
