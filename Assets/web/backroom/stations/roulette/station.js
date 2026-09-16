/* ============================================================================
 * station.js - the Velvet Vortex roulette station (CONTRACT 7 and 10.13.F). The
 * room calls mount(ctx) once, then open()/close() per visit. Back is live at
 * every frame (Law VI): it never waits on the server, a spin or a picture.
 *
 *   tape.js   bets, the client cover-all check, Law I, retries (pure)
 *   feel.js   timings, the Lighthouse clock, the ball's run planned back from
 *             the server's pocket (pure)
 *   bowl.js   the canvas bowl and its page effects
 *   mat.js    the canvas mat and the chips
 *   bowl-3d.js, mat-3d.js  the same bowl and mat on the room's own fixture when the
 *             room seats the player (ctx.stage, CONTRACT 10.18); a phone frames the
 *             mat while bets are open and the whole table once the ball runs
 *   shared/hypno  the Loom kit (turret whirl), the deal (a picture key for
 *             fx.gif_from) and the moments (the tunnel run, the haze, the wake
 *             spiral, the wash and the pocket GIF)
 *   glyphs.js the pocket glyphs (GLYPHS.md): four faded marks on the wheel's inner
 *             slope keyed by the pocket number, one section 4 id each; every
 *             landing fires the landed pocket's id on the thud frame (Law X)
 *   feel.FX_RECIPE  the host recipe on top: every beat of a spin (no more bets,
 *             the launch, the wake, the fret rattle, a near miss, the landing by
 *             outcome, a streak) fires section 4 ids through ctx.fx, cooled and
 *             stripped for Calm in feel.js (pure); gates no longer drop a step
 *             (owner 2026-09-15)
 *
 * A spin: chips on the mat (1 to 3 SP), 1 to 5 spins, then THE THROW - a flick
 * across the wheel itself (flick.js). While the throw is armed a curved arrow
 * breathes around the rim; the Spin button stays as the keyboard's fallback.
 * The flick's direction and strength set the rotor's starting speed and nothing
 * else (Law I): the pocket is the server's, planRun turns the whole ball path
 * onto it, and the same request the button sent goes out on the lift. The bowl
 * answers on the throw (Law VIII), the server settles every spin at once, and the page
 * plays them one at a time, about 8 s each. Each spin plays roulette.run (plus
 * roulette.wake on a Spiral Wake) at its launch, and exactly one
 * roulette.land.* on the frame the ball drops into the pocket. The SP chip owes
 * the tape's unplayed pays until each lands (Law I).
 *
 * THE LANDING FLOW (shared/hypno/callout.js, owner 2026-09-15). On the frame
 * the ball drops (Law I): tunnel 0, the text and the chip thud at 0; on a
 * paying spin the pocket and the paying chips glow to HIGHLIGHT_MS, and at
 * FX_DELAY_MS the landing moment, the host beat, the chips_in flight and the
 * callout (feel.calloutFor) fire together; the next launch waits WIN_HOLD_MS
 * (feel.nextLaunchAt). A miss stays quick: its moment (the run's holds
 * released), the chip vortex and a near miss's spiral all on the landing frame,
 * no callout.
 * ==========================================================================*/

import { createLoomKit, createDeck, createMoments, strengthK, rouletteRunLevel, pocketColor, viewportRect } from '../../shared/hypno/index.js';
import { createCallout, FX_DELAY_MS } from '../../shared/hypno/callout.js';
import { kit as sound } from '../../shared/sound/kit.js';
import { MAX_CHIPS, MAX_SPINS, addChip, removeChip, chipsOf, chipTotal, checkLayout, adoptTape, owed, shownSp, cursorOf, readOutcome, classify, spinBody, mintId, ROWS } from './tape.js';
import { FEEL, planRun, seedFor, landMoment, nextLaunchAt, landBeat, nearMisses, fxPlan, fxSymbols, createFxCooldowns, calloutFor } from './feel.js';
import { FLICK, flickStart, flickMove, flickRelease } from './flick.js';
import { createBowl } from './bowl.js';
import { createMat } from './mat.js';

export const roomStage = true;

const fmt = (n) => Number(n || 0).toLocaleString('en-US');
const wait = (ms) => new Promise((r) => setTimeout(r, Math.max(0, ms)));

function loadCss() {
  if (document.querySelector('link[data-roulette-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet'; link.href = new URL('./station.css', import.meta.url).href; link.dataset.rouletteCss = '';
  document.head.append(link);
}

export async function mount(ctx) {
  const t = (key, fallback, vars = {}) => {
    const s = typeof ctx.lex === 'function' ? ctx.lex(key, fallback) : fallback;
    return String(s ?? fallback).replace(/\{(\w+)\}/g, (_, k) => (k in vars ? vars[k] : `{${k}}`));
  };
  const prefersReduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  const stillNow = () => ctx.motion === 'off' || ctx.motion === 'still' || !!ctx.reduced || prefersReduced || String(ctx.intensity || '').toLowerCase() === 'calm';
  const fullNow = () => ctx.intensity === 'full' && !ctx.reduced && !prefersReduced;
  const kNow = () => (prefersReduced ? 0.5 : strengthK(ctx));
  const gates = () => { const g = ctx.gates || {}; return { flash: g.flash !== false, subliminal: g.subliminal !== false, spiral: g.spiral !== false, brainDrain: g.brainDrain !== false, tunnel: g.tunnel !== false }; };
  const stage = ctx.stage?.fixture && ctx.stage?.register ? ctx.stage : null;
  const makeBowl3D = stage ? (await import('./bowl-3d.js')).createBowl3D : null;
  const makeMat3D = stage ? (await import('./mat-3d.js')).createMat3D : null;
  let unStage = null, stageReady = stage?.ready !== false;
  const hostBack = ctx.hostBack === true;
  const hook = ctx.spReadout && typeof ctx.spReadout.owe === 'function' ? ctx.spReadout : null;
  loadCss();

  let el = null, cv = null, g = null, bowl = null, mat = null, kit = null, deck = null, moments = null;
  let alive = false, suspended = false, session = 0, phase = 'loading', raf = 0, unSp = null, unSettings = null;
  let st = null, sp = 0, tape = null, chips = {}, count = 1, why = null, hover = null, cur = null, resume = false;
  let status = '', history = [], feelLog = [], cursorSent = null, pausedAt = 0, pausedMs = 0, size = { w: 0, h: 0, dpr: 1 };
  let cool = createFxCooldowns(), streak = 0;   // the host recipe's cooldowns and the paying spins in a row
  let callout = null, landTimer = 0, lastCallout = null;   // the landing flow: the callout and the delayed fx frame of a paying spin
  let grab = null, thrown = null, lastThrow = null;   // THE THROW: the live flick, the speed it left for the next launch, the last one's log
  const $ = (sel) => el.querySelector(sel);
  const clock = () => (suspended ? pausedAt : performance.now()) - pausedMs;
  const note = (what, extra = {}) => { feelLog = [...feelLog.slice(-99), { what, at: Math.round(performance.now()), ...extra }]; };
  const owedNow = () => owed(tape);
  const reader = () => owedNow();

  /* ------------------------------------------------------------ readouts */
  const spotName = (spot) => (/^s\d+$/.test(spot) ? spot.slice(1)
    : spot === 'rose' ? t('br_roulette_spot_rose', 'Rose') : spot === 'plum' ? t('br_roulette_spot_plum', 'Plum')
    : t('br_roulette_spot_' + spot, { sip: 'Sip 1-12', sink: 'Sink 13-24', deep: 'Deep 25-36' }[spot] || spot));
  const matLabel = (spot) => (spot === 'rose' || spot === 'plum' ? spotName(spot)
    : t('br_roulette_mat_' + spot, { sip: 'SIP 1-12', sink: 'SINK 13-24', deep: 'DEEP 25-36' }[spot] || spot));
  function pocketLine(r) {
    if (r.pocket === 0) return t('br_roulette_pocket_zero', '0, the house pocket.');
    const color = r.color === 'rose' ? t('br_roulette_spot_rose', 'Rose') : t('br_roulette_spot_plum', 'Plum');
    const row = t('br_roulette_row_' + r.row, { sip: 'Sip row', sink: 'Sink row', deep: 'Deep row' }[r.row]);
    return t('br_roulette_pocket', '{n} {color}, {row}.', { n: r.pocket, color, row });
  }
  function resultLine(r) {
    let s = pocketLine(r);
    if (r.wake) s += ' ' + t('br_roulette_wake', 'Spiral Wake: winning chips pay double.');
    s += ' ' + (r.pay > 0 ? t('br_roulette_won', 'Your {spots} won {n} SP.', { spots: r.hits.map(spotName).join(', '), n: fmt(r.pay) })
      : t('br_roulette_lost', 'Nothing won this spin.'));
    return s;
  }
  function whyText(reason, extra = {}) {
    switch (reason) {
      case 'empty': return t('br_roulette_why_empty', 'Place chips on the mat to bet. Right click takes one off.');
      case 'covers_all': return t('br_roulette_why_covers_all', 'The house refuses a layout that covers every number from 1 to 36.');
      case 'stake_cap': return t('br_roulette_why_stake_cap', '{n} SP a spin at most.', { n: MAX_CHIPS });
      case 'insufficient': return t('br_roulette_why_insufficient', 'That needs {n} SP.', { n: fmt(extra.cost) });
      case 'too_fast': return t('br_roulette_why_too_fast', 'The wheel needs a moment. Try again in {s} s.', { s: Math.ceil((Number(extra.retryInMs) || 1000) / 1000) });
      case 'bad_layout': return extra.why === 'covers_all' ? whyText('covers_all')
        : t('br_roulette_why_bad_layout', 'The house refused that layout ({why}).', { why: extra.why || 'bad_layout' });
      case 'bad_request': return t('br_roulette_why_bad_request', 'That bet did not go through. Try again.');
      default: return t('br_roulette_offline', 'The house is not answering. Try again in a moment.');
    }
  }
  const check = () => checkLayout(chips, { rose: st && st.rose, count, sp: shownSp(sp, tape) });
  /** THE THROW is armed exactly when the Spin button is live: bets down (or a tape to watch) and nothing in flight. */
  const armed = () => !!(alive && !suspended && st && phase === 'bet' && stage?.ready !== false && (resume || check().ok));

  function sync() {
    if (!el) return;
    el.dataset.phase = phase;
    el.dataset.still = stillNow() ? '1' : '';
    const c = st ? check() : { ok: false, why: 'empty', stake: 0, cost: 0 };
    const left = tape ? tape.outcomes.length - tape.played : 0;
    let line = status;
    if (!line) line = resume ? t('br_roulette_resume', 'Your last spins are still on the table. {n} left to watch.', { n: left })
      : t('br_roulette_ready', 'Place your chips, pick the spins, then Spin.');
    if ($('.roul-status').textContent !== line) $('.roul-status').textContent = line;
    if (!hook) $('.roul-sp').textContent = t('br_roulette_sp', '{n} SP', { n: fmt(shownSp(sp, tape)) });
    const pips = el.querySelectorAll('.roul-pips i'), used = chipTotal(chips);
    pips.forEach((p, i) => p.classList.toggle('is-used', i < used));
    $('.roul-chips-label').textContent = stage ? t('br_roulette_place_bets', 'Place bets · {n}/{max}', { n: used, max: MAX_CHIPS }) : t('br_roulette_chips', 'Chips {n} of {max}', { n: used, max: MAX_CHIPS });
    $('.roul-chips-label').dataset.chips = String(used);
    const locked = phase !== 'bet' || resume || stage?.ready === false;
    $('.roul-clear').disabled = locked || used === 0;
    // A phone's camera frames the mat while bets are open and the whole table for the run (seat-camera.js).
    if (mat && mat.setFrame) mat.setFrame(phase === 'bet' && !resume ? 'mat' : 'table');
    el.querySelectorAll('.roul-spins button').forEach((b) => { b.setAttribute('aria-pressed', String(Number(b.dataset.n) === count)); b.disabled = locked; });
    const shownWhy = why || (phase === 'bet' && !resume && !c.ok && c.why !== 'empty' ? whyText(c.why, c) : '');
    $('.roul-why').textContent = shownWhy; $('.roul-why').hidden = !shownWhy;
    const spin = $('.roul-spin');
    spin.disabled = phase !== 'bet' || (!resume && !c.ok);
    if (bowl) bowl.setHint(armed() && !grab);   // the arrow only while the wheel is waiting for a hand
    spin.querySelector('span').textContent = phase === 'playing' || phase === 'asking' ? t('br_roulette_spinning', 'No more bets')
      : resume ? t('br_roulette_watch', 'Watch the rest') : t('br_roulette_spin', 'Spin');
    spin.querySelector('small').textContent = resume ? t('br_roulette_left', '{n} left', { n: left })
      : t('br_roulette_cost', '{stake} SP x {count} = {cost} SP', { stake: c.stake, count, cost: c.cost });
    const hist = $('.roul-history');
    if (hist.childElementCount !== history.length || (history.length && hist.lastElementChild.textContent !== history[history.length - 1])) {
      hist.replaceChildren(...history.map((h) => { const li = document.createElement('li'); li.textContent = h; return li; }));
    }
  }
  function card(text) { if (!el) return; $('.roul-card p').textContent = text || ''; $('.roul-card').hidden = !text; }
  function renderOdds() {
    const kinds = (st.table && Array.isArray(st.table.kinds)) ? st.table.kinds : [];
    const names = { straight: t('br_roulette_odds_straight', 'One number'), row: t('br_roulette_odds_row', 'A row of twelve'), color: t('br_roulette_odds_color', 'Rose or plum') };
    const rows = kinds.map((k) => {
      const tr = document.createElement('tr');
      for (const [tag, text] of [['th', names[k.kind] || k.kind], ['td', t('br_roulette_odds_pays', 'pays {n}', { n: k.pays })],
        ['td', t('br_roulette_odds_woken', 'woken {n}', { n: k.woken })], ['td', String(k.odds || '')]]) {
        const cell = document.createElement(tag); cell.textContent = text; tr.append(cell);
      }
      return tr;
    });
    $('.roul-odds table').replaceChildren(...rows);
    $('.roul-odds p').textContent = t('br_roulette_odds_note', 'Pays are the SP a chip returns, the chip included. Spiral Wake {wake}: every winning chip pays double. 1 to {max} SP a spin, up to {spins} spins. A layout covering all of 1-36 is refused.',
      { wake: String((st.table && st.table.wake) || ''), max: MAX_CHIPS, spins: MAX_SPINS });
    $('.roul-glyphs').textContent = t('br_roulette_glyphs_note', 'The faded marks on the wheel are the spell of each pocket, win or lose. Spiral: a spiral. Eye: a flash of pictures. Bubble: words. Drop: a melt. The mark lights when the ball settles there.');
  }

  /* ---------------------------------------------------------------- build */
  function build() {
    const root = document.createElement('div');
    root.className = stage ? 'roul-station br-seat' : 'roul-station';
    if (stage) root.dataset.seated = '';  root.dataset.phase = 'loading';
    const spinsBtns = Array.from({ length: MAX_SPINS }, (_, i) => `<button type="button" data-n="${i + 1}" aria-pressed="false">${i + 1}</button>`).join('');
    root.innerHTML = `
      ${stage ? '<div class="roul-stage"></div>' : '<canvas class="roul-stage"></canvas>'}
      <header class="roul-top">
        <button class="roul-back" type="button">&larr; <span></span></button>
        <span class="roul-sp"></span>
        <div class="roul-status" aria-live="polite"></div>
        <ol class="roul-history"></ol>
      </header>
      <div class="roul-controls">
        <div class="roul-row"><span class="roul-chips-label"></span><span class="roul-pips" aria-hidden="true"><i></i><i></i><i></i></span><button class="roul-clear" type="button"></button></div>
        <div class="roul-row roul-spins" role="group"><span class="roul-spins-label"></span>${spinsBtns}</div>
        <p class="roul-why" hidden></p>
        <button class="roul-spin" type="button"><span></span><small></small></button>
      </div>
      <details class="roul-odds"><summary></summary><table></table><p></p><p class="roul-glyphs"></p></details>
      <div class="roul-card" role="status" hidden><p></p><button class="roul-card-back" type="button"></button></div>
      <div class="roul-loading"></div>`;
    const set = (sel, text) => { root.querySelector(sel).textContent = text; };
    root.querySelector('.roul-stage').setAttribute('aria-label', t('br_roulette_stage', 'Velvet Vortex roulette. Click the mat to place chips.'));
    set('.roul-back span', t('br_roulette_back', 'Back')); set('.roul-card-back', t('br_roulette_back', 'Back'));
    set('.roul-clear', t('br_roulette_clear', 'Clear')); set('.roul-spins-label', t('br_roulette_spins', 'Spins'));
    set('.roul-odds summary', t('br_roulette_odds', 'Odds')); set('.roul-loading', t('br_roulette_loading', 'Brushing the velvet'));
    root.querySelector('.roul-history').setAttribute('aria-label', t('br_roulette_history', 'Last spins'));
    root.querySelector('.roul-spins').setAttribute('aria-label', t('br_roulette_spins', 'Spins'));
    root.querySelector('.roul-back').onclick = back;
    root.querySelector('.roul-card-back').onclick = back;
    if (hostBack) { root.dataset.hostBack = ''; root.querySelector('.roul-back').hidden = true; root.querySelector('.roul-card-back').hidden = true; }
    if (hook) root.dataset.hostSp = '';
    root.querySelector('.roul-spin').onclick = () => press();
    root.querySelector('.roul-clear').onclick = () => { if (phase === 'bet' && !resume) { chips = {}; why = null; sync(); } };
    root.querySelectorAll('.roul-spins button').forEach((b) => { b.onclick = () => setCount(Number(b.dataset.n)); });
    return root;
  }

  function back() {
    if (el && !hostBack) $('.roul-back').classList.add('is-ringing');   // Law VIII
    if (typeof ctx.standUp === 'function') ctx.standUp(); else close();
  }
  function setCount(n) { if (phase !== 'bet' || resume) return; count = Math.max(1, Math.min(MAX_SPINS, Math.trunc(n) || 1)); why = null; sync(); }
  function place(spot, remove = false) {
    if (phase !== 'bet' || resume || !st || stage?.ready === false) return false;
    if (remove) { chips = removeChip(chips, spot); why = null; sync(); return true; }
    const r = addChip(chips, spot, { spots: st.spots });
    chips = r.chips; why = r.ok ? null : whyText(r.why);
    if (r.ok) { sound.arm(); sound.play('chips'); }
    sync();
    return r.ok;
  }
  function ring(sel) { const b = el && $(sel); if (!b) return; b.classList.add('is-ringing'); setTimeout(() => b.classList.remove('is-ringing'), 400); }

  /** The kit's cue for a beat, on the same frame as the host recipe (Law X). The landing drops the ball first; a
   *  miss is THE SETTLE (soft, never a fail), a near miss resolves quietly, a pay is a win by tier that only rises. */
  function cue(name) {
    if (!alive || suspended) return;
    // THE THROW's voice: the wheel has no lever, the throw IS the lever. The press frame gets the spin-up
    // (Law VIII, before any reply) and the rotor's loop; every launch after it picks the loop back up; a
    // landing hands the frame straight over to the drop and the settle (Law X).
    if (name === 'near' || name.startsWith('land.')) sound.stop('wheel');
    if (name === 'nomore') { sound.arm(); sound.play('tap'); sound.play('whir'); sound.play('wheel', { speed: 1 }); }
    else if (name === 'launch') { sound.play('launch'); sound.play('wheel', { speed: 1 }); }
    else if (name === 'rattle') { sound.play('rattle'); if (cur && cur.plan) sound.play('riser', { ms: Math.min(2500, Math.max(800, cur.plan.restAt * 1000 - (clock() - cur.launchAt))) }); }
    else if (name === 'near') { sound.play('drop'); sound.play('almost'); }
    else if (name === 'land.miss') { sound.play('drop'); sound.play('settle'); }
    else if (name === 'land.win') { sound.play('drop'); sound.play('win', { tier: 'mid' }); sound.play('chips', { n: 4, at: 0.2 }); }
    else if (name === 'land.wake' || name === 'land.straight') { sound.play('drop'); sound.play('win', { tier: 'big' }); sound.play('chips', { n: 6, at: 0.3 }); }
    else if (name === 'land.full') { sound.play('drop'); sound.play('win', { tier: 'hero' }); sound.play('chips', { n: 8, at: 0.4 }); }
  }
  /** The host recipe (feel.FX_RECIPE): a beat fires its section 4 ids through ctx.fx, gated, cooled, never awaited (fx-ack is advisory). */
  function beat(name, { i = null, streak: run = 0, pocket = null } = {}) {
    const fired = [];
    cue(name);
    if (!alive || suspended || typeof ctx.fx !== 'function') return fired;
    const plan = fxPlan(name, { gates: gates(), calm: stillNow(), full: fullNow(), streak: run, pocket });
    const now = performance.now();
    for (const step of plan) {
      if (!cool.take(step, now, i)) continue;
      const symbols = fxSymbols(step, { gif: deck && tape && i != null ? deck.pickKey(tape.id + ':' + i) : null, spin: i || 0 });
      try { const p = ctx.fx(step.fx, symbols.length ? symbols : undefined); if (p && typeof p.catch === 'function') p.catch(() => {}); } catch (e) { /* noop */ }
      fired.push(step.fx);
    }
    note('fx', { beat: name, fired, planned: plan.map((s) => s.fx) });
    return fired;
  }

  /* ----------------------------------------------------------------- spin */
  /** `rotVel` is THE THROW's signed rotor speed (flick.js); the button and the keyboard send none and take the kick. */
  async function press(rotVel = null) {
    if (!alive || suspended || !st || phase !== 'bet' || stage?.ready === false) return;
    const pressedAt = performance.now();
    if (resume) { resume = false; thrown = rotVel; ring('.roul-spin'); note('answer', { ms: Math.round(performance.now() - pressedAt), resume: true }); playTape(); return; }
    const c = check();
    if (!c.ok) { ring('.roul-spin'); why = whyText(c.why, c); sync(); return; }
    // Law VIII: the rotor picks up and the button rings on this frame, before any reply.
    thrown = Number.isFinite(rotVel) ? rotVel : null;
    phase = 'asking'; why = null; status = t('br_roulette_no_more', 'No more bets...'); bowl.kick(thrown ?? FEEL.ROTOR_KICK); ring('.roul-spin'); sync();
    beat('nomore');   // the same for every press: nothing here knows the pocket (Law I)
    note('answer', { ms: Math.round(performance.now() - pressedAt) });
    const my = session, idem = mintId(), body = spinBody({ idem, count, chips, spots: st.spots, tape });
    for (let tries = 1; ; tries++) {
      const res = await Promise.resolve(ctx.request('spin', body, idem)).catch(() => ({ ok: false, reason: 'offline' }));
      if (my !== session || !alive) return;
      const a = classify(res, tries);
      note('reply', { kind: a.kind, reason: a.reason || null, tries });
      if (a.kind === 'retry') { await wait(a.waitMs); if (my !== session || !alive) return; continue; }
      if (a.kind === 'ok') {
        sp = Number(a.body.sp) || 0; tape = adoptTape(a.body.tape); cursorSent = null;
        if (!tape) { phase = 'bet'; status = ''; why = whyText('bad_request'); sync(); return; }
        chips = chipsOf(tape.bets);
        if (hook) hook.owe(reader);
        playTape();
        return;
      }
      if (a.kind === 'tape') {   // spins bought earlier and never watched: play those, nothing new was bought
        if (Number.isFinite(Number(a.body.sp))) sp = Number(a.body.sp);
        tape = a.tape; chips = chipsOf(tape.bets); cursorSent = null;
        if (hook) hook.owe(reader);
        history.push(t('br_roulette_history_resume', 'Earlier spins, still on the table'));
        playTape();
        return;
      }
      phase = a.kind === 'closed' ? 'closed' : 'bet'; status = '';
      if (a.kind === 'closed') card(t('br_roulette_closed', 'The Velvet Vortex is closed for a moment.'));
      else { why = whyText(a.reason, { ...a.body, cost: c.cost, why: a.why }); if (a.reason === 'insufficient' && Number.isFinite(Number(a.body.sp))) sp = Number(a.body.sp); }
      sync();
      return;
    }
  }

  function playTape() {
    if (!tape || tape.played >= tape.outcomes.length) { endTape(); return; }
    phase = 'playing';
    launch(tape.played, clock());
  }

  function launch(i, now) {
    const o = tape.outcomes[i], read = readOutcome(o, tape.bets, { rose: st.rose, wheel: st.wheel });
    if (read.index < 0) { note('skip', { pocket: read.pocket }); tape.played = i + 1; playTape(); return; }
    mat.clearAnims();
    // THE THROW's speed is spent on the launch it bought; the spins after it leave on the house's own kick.
    const rotVel0 = Number.isFinite(thrown) ? thrown : FEEL.ROTOR_KICK; thrown = null;
    bowl.kick(rotVel0);
    const plan = planRun({ index: read.index, seed: seedFor(tape.id, i), calm: stillNow(), rotVel0 });
    bowl.launch(plan, now, { wake: read.wake });
    const run = moments.play('roulette.run');
    const wake = read.wake ? moments.play('roulette.wake', { wake: true }) : null;
    cur = { i, read, plan, launchAt: now, landed: false, rattled: false, restAt: null, page: [...run.page, ...(wake ? wake.page : [])] };
    beat('launch', { i });                 // every spin alike (Law I)
    if (read.wake) beat('wake', { i });    // the wake is on screen as text from this frame
    status = (read.wake ? t('br_roulette_waking', 'No more bets... the bowl is waking.') : t('br_roulette_no_more', 'No more bets...'))
      + '\n' + t('br_roulette_progress', 'Spin {i} of {n}', { i: i + 1, n: tape.outcomes.length });
    note('launch', { i, pocket: read.pocket, wake: read.wake, hits: plan.hits, restAt: Math.round(plan.restAt * 1000), page: cur.page, rotVel0: Math.round(rotVel0 * 100) / 100 });
    sync();
  }

  /** Law VI: whatever a landing still owed the desk (the delayed fx frame, the callout) is dropped now. */
  function dropLanding() {
    clearTimeout(landTimer); landTimer = 0;
    if (callout) callout.cancel();
  }
  /** The moment, the host beat, the paying chips' flight and the callout of a paying spin: one frame, FX_DELAY_MS after the landing. */
  function landFx(r, id, hostBeat, near, co) {
    const box = bowl.pocketBox(r.index), now = clock();
    const hostFx = beat(hostBeat, { i: r.i, streak });   // the recipe first; the moment's wash and pocket GIF close the frame
    const m = moments.play(id, { color: pocketColor(r.pocket, st.rose), from: viewportRect(cv, box.x, box.y, box.w, box.h),
      gif: deck ? deck.pickKey(tape.id + ':' + r.i) : undefined, wake: r.wake });
    const list = [];
    for (const spot of r.hits) {
      if (m.page.includes('chips_in')) list.push({ kind: 'in', spot }, { kind: 'in', spot });
      if (m.page.includes('pulled_pair')) list.push({ kind: 'pull', spot }, { kind: 'pull', spot });
    }
    if (list.length) mat.animate(list, now);
    if (co && callout) { callout.show(co.key, co.fallback, { tier: co.tier }); lastCallout = { key: co.key, tier: co.tier, i: r.i, at: Math.round(performance.now()) }; note('callout', lastCallout); }
    note('land', { i: r.i, pocket: r.pocket, pay: r.pay, wake: r.wake, straight: r.straight, moment: id, page: m.page, fx: m.tokens.length, beat: hostBeat, near, streak, hostFx,
      callout: co ? co.key : null, delayed: true });
  }
  /** The landing frame: tunnel off, the chips, the text, and Law I lets this spin's pay land. A paying spin glows first
   *  and fires its moment, beat and callout at FX_DELAY_MS (landFx); a miss plays its moment and vortex here. */
  function land(now) {
    const r = cur.read;
    cur.landed = true; cur.landAt = now; cur.win = r.pay > 0;
    moments.tunnel(0);
    const glyphFx = beat('glyph', { i: r.i, pocket: r.pocket });   // THE POCKET GLYPH: the mark lights and its effect fires on the thud frame (Law X), win or lose
    const id = landMoment(r);
    streak = r.pay > 0 ? streak + 1 : 0;
    const near = nearMisses(r, tape.bets, st.wheel), hostBeat = landBeat(r, near);
    const co = r.pay > 0 ? calloutFor(hostBeat, { streak }) : null;
    const lose = tape.bets.filter((b) => !r.hits.includes(b.spot)).map((b) => ({ kind: 'lose', spot: b.spot }));
    mat.animate(lose, now);
    if (r.pay > 0) {
      bowl.glow(r.index, now); mat.glow(r.hits, now);   // THE GLYPH HIT: the pocket and the paying chips, 0..HIGHLIGHT_MS
      const my = session, i = r.i;
      clearTimeout(landTimer);
      landTimer = setTimeout(() => {
        landTimer = 0;
        if (my !== session || !alive || suspended || !cur || cur.i !== i) return;
        landFx(r, id, hostBeat, near, co);
      }, FX_DELAY_MS);
      note('landed', { i: r.i, pocket: r.pocket, pay: r.pay, beat: hostBeat, callout: co ? co.key : null, glyph: glyphFx });
    } else {
      const box = bowl.pocketBox(r.index);
      const hostFx = beat(hostBeat, { i: r.i, streak });   // a near miss's spiral, or nothing
      const m = moments.play(id, { color: pocketColor(r.pocket, st.rose), from: viewportRect(cv, box.x, box.y, box.w, box.h), wake: r.wake });   // releases the run's holds
      note('land', { i: r.i, pocket: r.pocket, pay: r.pay, wake: r.wake, straight: r.straight, moment: id, page: m.page, fx: m.tokens.length, beat: hostBeat, near, streak, hostFx, glyph: glyphFx, callout: null, delayed: false });
    }
    tape.played = r.i + 1;
    if (hook) { hook.owe(reader); if (r.pay > 0 && typeof hook.thud === 'function') hook.thud(); }
    const line = resultLine(r);
    history = [...history.slice(-4), `${r.pocket} ${r.pocket === 0 ? '' : spotName(r.color) + ' '}${r.pay > 0 ? '+' + fmt(r.pay) : '+0'}${r.wake ? ' ~' : ''}`.replace(/\s+/g, ' ')];
    status = line + '\n' + t('br_roulette_progress', 'Spin {i} of {n}', { i: r.i + 1, n: tape.outcomes.length });
    sync();
  }

  function endTape() {
    phase = 'bet'; cur = null;
    if (tape && cursorSent !== tape.played && tape.played > 0) flushCursor();
    note('tape-end', { played: tape ? tape.played : 0 });
    sync();
  }
  function flushCursor() {
    const c = cursorOf(tape);
    if (!c) return;
    cursorSent = c.played;
    Promise.resolve(ctx.request('cursor', c)).catch(() => {});
  }

  /* ---------------------------------------------------------------- frame */
  function layout() {
    if (stage) return;
    const w = cv.clientWidth, h = cv.clientHeight, dpr = Math.min(1.5, globalThis.devicePixelRatio || 1);
    if (w === size.w && h === size.h && dpr === size.dpr) return;
    size = { w, h, dpr };
    cv.width = Math.max(1, Math.round(w * dpr)); cv.height = Math.max(1, Math.round(h * dpr));
    const wide = w >= h * 1.1;
    if (wide) {
      bowl.layout(w * 0.27, h * 0.52, Math.min(h * 0.32, w * 0.19));
      mat.layout(w * 0.5, h * 0.16, w * 0.47, h * 0.46);
    } else {
      bowl.layout(w / 2, h * 0.27, Math.min(w * 0.3, h * 0.16));
      mat.layout(16, h * 0.47, w - 32, h * 0.24);
    }
  }

  /** THE THROW's loop follows the rotor: 1 at the hardest throw, 0 once the wheel is back to its idle drift.
   *  Throttled like the slot's drums, one update per 60 ms or per 2% of speed. */
  let wheelSent = [-1, 0];
  function followThrow(rotVel) {
    if (!alive || suspended) return;
    const span = Math.max(0.01, FLICK.VEL_MAX - FEEL.ROTOR_IDLE);
    const sp = Math.max(0, Math.min(1, (Math.abs(Number(rotVel) || 0) - FEEL.ROTOR_IDLE) / span));
    const t = clock();
    if (Math.abs(sp - wheelSent[0]) < 0.02 && t - wheelSent[1] < 60) return;
    wheelSent = [sp, t];
    sound.setWheelSpeed(sp);
  }

  function frame() {
    if (!alive) return;
    if (!stage) raf = requestAnimationFrame(frame);
    if (suspended || !bowl) return;
    if (stage && stageReady !== (stage.ready !== false)) { stageReady = stage.ready !== false; sync(); }
    const now = clock(), still = stillNow(), k = kNow();
    layout();
    if (kit) kit.setStill(still);
    const u = bowl.update(now, { still });
    followThrow(u.rotVel);
    if (cur && !cur.landed) {
      if (u.speed > 0) moments.tunnel(rouletteRunLevel(u.speed));
      if (!cur.rattled && u.phase === 'rattle') { cur.rattled = true; beat('rattle', { i: cur.i }); }   // the first fret clip, once a spin
      if (u.landed) land(now);
    }
    if (cur && cur.landed && cur.restAt == null && u.phase === 'rest') cur.restAt = now;
    if (cur && cur.restAt != null && now >= nextLaunchAt(cur.launchAt, cur.restAt, { landMs: cur.landAt, win: cur.win })) {
      if (tape && tape.played < tape.outcomes.length) launch(tape.played, now);
      else { mat.clearAnims(); endTape(); }
    }
    if (!stage) {
    g.setTransform(size.dpr, 0, 0, size.dpr, 0, 0);
    const bgr = g.createRadialGradient(bowl.geo.cx, bowl.geo.cy, bowl.geo.R * 0.5, bowl.geo.cx, bowl.geo.cy, Math.max(size.w, size.h) * 0.8);
    bgr.addColorStop(0, '#1d1233'); bgr.addColorStop(1, '#0a0614');
    g.fillStyle = bgr; g.fillRect(0, 0, size.w, size.h);
    }
    const gt = gates();
    bowl.draw(g, { dpr: size.dpr, now, k, full: fullNow(), spiral: gt.spiral, kit,
      still, slowText: t('br_roulette_slowly', 's l o w l y') });
    const landedShown = cur && cur.landed ? cur.read : null;
    mat.draw(g, { now, chips, hover, hits: landedShown ? landedShown.hits : [], landed: landedShown ? landedShown.pocket : null, k, still,
      bowl: bowl.geo, locked: phase !== 'bet' || resume });
  }

  /* ---------------------------------------------------------------- input */
  function onKey(e) {
    if (!alive) return;
    if (e.key === 'Escape') { e.preventDefault(); back(); return; }
    if (e.target && e.target.closest && e.target.closest('button, summary, input, select')) return;
    if (e.code === 'Space' || e.key === 'Enter') { e.preventDefault(); press(); return; }
    if (/^[1-5]$/.test(e.key)) { setCount(Number(e.key)); return; }
    if (e.key === 'Backspace' || e.key === 'Delete') { if (phase === 'bet' && !resume) { chips = {}; why = null; sync(); } }
  }
  const local = (e) => { const r = cv.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
  function onPointer(e) {
    if (!mat || !bowl) return;
    const p = local(e), spot = mat.pick ? mat.pick(e) : mat.hit(p.x, p.y);
    if (e.type === 'pointermove') {
      if (grab && grab.id === e.pointerId) { dragTo(e, p); return; }
      hover = spot;
      cv.style.cursor = spot && phase === 'bet' && !resume ? 'pointer' : (!spot && armed() && bowl.wheelHit(p.x, p.y) ? 'grab' : 'default');
      return;
    }
    if (e.type !== 'pointerdown') return;
    if (spot) { e.preventDefault(); place(spot, e.button === 2 || e.shiftKey); return; }
    // THE THROW: the mat has first claim on a press; what is left over, on the wheel, is a hand on the rotor.
    if (e.button !== 0 || !e.isPrimary || grab || !bowl.wheelHit(p.x, p.y)) return;
    e.preventDefault();   // never a page scroll, never the room's own look-around
    // Bets still to come: the hand may push the wheel round for the feel of it, but nothing is ever sent (Law I).
    const locked = !armed();
    if (locked && (phase !== 'bet' || resume)) { note('flick', { ok: false, why: 'shut' }); return; }
    try { cv.setPointerCapture(e.pointerId); } catch (err) { /* noop */ }
    grab = { id: e.pointerId, locked, ...flickStart(bowl.angleAt(p.x, p.y), performance.now()) };
    cv.style.cursor = 'grabbing'; sound.arm();
    sync();   // the arrow steps aside while the hand is on the wheel
  }
  /** One sample of a live grab: the wheel follows the finger, the speed is remembered, nothing is sent. */
  function dragTo(e, p) {
    e.preventDefault();
    const next = flickMove(grab, bowl.angleAt(p.x, p.y), performance.now());
    grab = { ...next, id: grab.id, locked: grab.locked };
    bowl.turn(next.d);
  }
  /** The lift. A real throw sends the SAME request the Spin button sends; anything less leaves the wheel turning. */
  function onRelease(e) {
    if (!grab || grab.id !== e.pointerId) return;
    const held = grab; grab = null;
    cv.style.cursor = 'default';
    try { if (cv.hasPointerCapture(e.pointerId)) cv.releasePointerCapture(e.pointerId); } catch (err) { /* noop */ }
    const r = e.type === 'pointerup' && !held.locked ? flickRelease(held, performance.now())
      : { ok: false, why: held.locked ? 'locked' : 'cancel', rotVel: null, sign: held.sign, travel: held.travel, omega: 0 };
    lastThrow = { ok: r.ok, why: r.why || null, sign: r.sign, travel: Math.round(held.travel * 1000) / 1000, rotVel: r.rotVel == null ? null : Math.round(r.rotVel * 100) / 100 };
    note('flick', lastThrow);
    if (!r.ok) { sync(); return; }
    press(r.rotVel);
  }
  const onContext = (e) => { if (mat && (mat.pick ? mat.pick(e) : mat.hit(local(e).x, local(e).y))) e.preventDefault(); };

  /* ------------------------------------------------------------ lifecycle */
  async function open() {
    if (alive) return;
    alive = true; suspended = false; phase = 'loading'; status = ''; why = null; history = []; feelLog = []; cur = null; resume = false; pausedMs = 0;
    grab = null; thrown = null; lastThrow = null;
    const my = ++session;
    el = build(); ctx.root.append(el);
    cv = stage ? stage.canvas : $('.roul-stage'); g = stage ? null : cv.getContext('2d'); size = { w: 0, h: 0, dpr: 1 };
    addEventListener('keydown', onKey);
    cv.addEventListener('pointermove', onPointer); cv.addEventListener('pointerdown', onPointer); cv.addEventListener('contextmenu', onContext);
    for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) cv.addEventListener(ev, onRelease);
    moments = createMoments(ctx, { station: 'roulette' });
    callout = createCallout({ mount: el, lex: typeof ctx.lex === 'function' ? ctx.lex : undefined }); lastCallout = null;
    kit = createLoomKit({ still: stillNow(), log: (m) => note('loom', { m }) });
    createDeck(ctx, { count: 4 }).then((d) => { if (my === session && alive) deck = d; else d.dispose(); }).catch(() => {});
    if (typeof ctx.onSp === 'function') unSp = ctx.onSp((v) => { if (Number.isFinite(Number(v)) && phase !== 'asking') { sp = Number(v); if (el) sync(); } });
    if (typeof ctx.onSettings === 'function') unSettings = ctx.onSettings(() => { if (el) sync(); });
    const res = await Promise.resolve(ctx.request('state', {})).catch(() => null);
    if (my !== session || !alive) return;
    $('.roul-loading').hidden = true;
    const b = res && res.ok && res.status !== 403 && res.body && res.body.ok ? res.body : null;
    if (!b || b.open === false || !Array.isArray(b.wheel) || b.wheel.length !== 37 || !Array.isArray(b.spots)) {
      phase = 'closed'; card(t('br_roulette_closed', 'The Velvet Vortex is closed for a moment.')); sync();
      return;
    }
    st = b; sp = Number(b.sp) || 0; tape = adoptTape(b.tape);
    bowl = stage ? makeBowl3D({ stage, wheel: st.wheel, rose: st.rose }) : createBowl({ wheel: st.wheel, rose: st.rose });
    mat = stage ? makeMat3D({ stage, spots: st.spots, label: matLabel }) : createMat({ spots: st.spots, rose: st.rose, label: matLabel });
    if (stage) unStage = stage.register({ update: frame, dispose() { bowl?.dispose(); mat?.dispose(); } });
    if (tape) {
      chips = chipsOf(tape.bets); resume = tape.played < tape.outcomes.length;
      if (tape.played > 0) { const last = readOutcome(tape.outcomes[tape.played - 1], tape.bets, { rose: st.rose, wheel: st.wheel }); bowl.seat(last.index); }
    }
    if (hook) hook.owe(reader);   // registered once the tape state is back (CONTRACT 7.1)
    renderOdds();
    layout();   // the mat takes clicks from the first interactive frame
    phase = 'bet'; sync();
    if (!stage) raf = requestAnimationFrame(frame);
    note('open', { resume, sp, still: stillNow(), gates: gates() });
  }

  async function close() {
    if (!alive) return;
    alive = false;
    const my = ++session;
    cancelAnimationFrame(raf); raf = 0;
    removeEventListener('keydown', onKey);
    if (cv) {
      cv.removeEventListener('pointermove', onPointer); cv.removeEventListener('pointerdown', onPointer); cv.removeEventListener('contextmenu', onContext);
      for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) cv.removeEventListener(ev, onRelease);
    }
    grab = null; thrown = null;
    if (typeof unSp === 'function') unSp();
    if (typeof unSettings === 'function') unSettings();
    unSp = null; unSettings = null;
    // Law VI: Back drops every ceremony. What has landed is flushed; a spin still running stays unplayed.
    dropLanding();
    if (callout) { callout.dispose(); callout = null; }
    if (moments) { moments.cancel(); moments.dispose(); }
    sound.stop('riser'); sound.stop('wheel');
    cool.reset(); streak = 0; note('fx', { beat: 'skip', fired: [], planned: [] });   // one-shots settle on the host; nothing new fires
    if (tape && tape.played > 0 && cursorSent !== tape.played) flushCursor();
    if (hook) { hook.owe(owedNow()); if (typeof hook.set === 'function') hook.set(null); }   // a plain number for the room
    if (unStage) { unStage(); unStage = null; }
    if (kit) kit.dispose();
    if (deck) deck.dispose();
    if (el) el.dataset.phase = 'leaving';
    const root = el;
    if (root) root.remove();
    if (my === session) { el = null; cv = null; g = null; kit = null; deck = null; moments = null; cur = null; bowl = null; mat = null; phase = 'loading'; }
  }

  return {
    open, close,
    suspend(on) {
      if (!!on === suspended) return;
      if (on) {
        suspended = true; pausedAt = performance.now();
        dropLanding();
        if (moments) moments.cancel();
        cool.reset(); note('fx', { beat: 'skip', fired: [], planned: [] });   // Law VI: the recipe stops with the moments
        sound.stop('wheel');   // THE THROW's loop never rides a suspend
        if (kit) { kit.dispose(); kit = null; }
        if (deck) deck.dispose();   // its keys still pick (pickKey needs no pictures)
      } else {
        pausedMs += performance.now() - pausedAt; suspended = false;
        if (alive) {
          kit = createLoomKit({ still: stillNow(), log: (m) => note('loom', { m }) });
          if (cur && !cur.landed && moments) { moments.play('roulette.run'); if (cur.read.wake) moments.play('roulette.wake', { wake: true }); }
        }
      }
      note('suspend', { on: !!on });
      if (el) sync();
    },
    async destroy() { await close(); document.querySelectorAll('link[data-roulette-css]').forEach((l) => l.remove()); },
    /** For dev.html and the CDP checks only. */
    debug: () => ({
      phase, alive, suspended, still: stillNow(), full: fullNow(), k: kNow(), gates: gates(), hostBack, hook: !!hook,
      sp, shown: shownSp(sp, tape), owed: owedNow(), chips: { ...chips }, count, resume,
      check: st ? check() : null, why: el ? $('.roul-why').textContent : null, status: el ? $('.roul-status').textContent : null,
      history: history.slice(), spin: el ? $('.roul-spin').textContent : null, spinDisabled: el ? $('.roul-spin').disabled : null,
      flick: { armed: armed(), grabbed: !!grab, last: lastThrow, pending: thrown },
      tape: tape && { id: tape.id, played: tape.played, n: tape.outcomes.length, bets: tape.bets, outcomes: tape.outcomes },
      cur: cur && { i: cur.i, landed: cur.landed, pocket: cur.read.pocket, wake: cur.read.wake, page: cur.page, win: !!cur.win, landAt: cur.landAt ?? null },
      callout: { last: lastCallout, pending: !!landTimer, ...(callout ? callout.debug() : { shown: [] }) },
      bowl: bowl && bowl.debug(), mat: mat && { ...mat.debug(), rects: Object.fromEntries((st ? st.spots : []).map((s) => [s, mat.rectOf(s)])) },
      kit: kit && kit.debug(), moments: moments && moments.debug(), deck: deck && { keys: deck.keys }, log: feelLog.slice(), rows: ROWS,
      fx: { ...cool.debug(), streak },
    }),
    /** Test seams for the checks: the same paths the mat and buttons take. */
    dev: { place, setCount, press, clock, armed, clearChips: () => { if (phase === 'bet' && !resume) { chips = {}; sync(); } } },
  };
}
