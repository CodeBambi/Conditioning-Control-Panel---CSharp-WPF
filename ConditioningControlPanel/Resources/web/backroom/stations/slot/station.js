/* ============================================================================
 * station.js - the slot station (CONTRACT.md section 7). The room calls
 * mount(ctx) once, then open()/close() per sit-down. Back is live at every
 * frame (Law VI): it never waits on the glb, the server or an animation.
 *
 *   tape.js   what plays next, and the readouts (server-settled outcomes)
 *   scene.js  the cabinet, one WebGL context per open(), freed in close()
 *   media.js  the dealt GIFs and words painted on the reel cells
 *   word.js   the lone word and the dead-spin settle on the page (shared/hypno/callout.js): fx.sub_single is
 *             shown here and not sent; fx.sub_pair / fx.sub_cascade go to the host with args { wordsShown: true }
 *             (the host skips its own words and plays the pair's spiral / the cascade's fullscreen GIF)
 *   pace.js   THE PACE: about 4 s an outcome
 *   feel.js   THE HOUSE BOOK: which move plays, how big, and the Brake (lane F1)
 *   bank.js / sound.js  THE BANK's tokens and the cues
 *
 * THE FLOW on the frame the result SHOWS (shared/hypno/callout.js, Law I: after the server reply, the tape holds
 * outcomes ahead): the landing thud at 0 ms; the winning glyphs glow 0..400 ms in reel order 80 ms apart
 * (scene.highlight: rim pulse + 6% cell pop; the root carries class br-glyph-hit for that window); at 400 ms the
 * callout (.show) and the row's host fx fire TOGETHER (feel.flowPlan decides, flow() below schedules); the next
 * press unlocks no earlier than landing + 2000 ms on a paid line (the jackpot holds 3400 ms, losses stay as quick
 * as today). The tunnel level and the jar/free-spin sequencing keep their own timing.
 * THE GIF TEASE (owner: a couple of points on the GIF visuals, the economy untouched): a row reading `none` that
 * shows exactly two GIF symbols fires ONE host GIF flash, fx.gif_burst with args { count: 1 }, at 400 ms. No
 * callout, no SP, the payout untouched (mock-server.js says the same).
 * A1's hold (reels 1+2 a live pair, reel 3 still travelling) pulls ctx.fxTunnel to 0.4 with the riser and releases
 * on the landing: page dressing of a known tape row, no host fx. Eight seconds idle breathes the spiral haze behind
 * the cabinet (scene.haze) until the next press; off while a result plays, off on suspend.
 * ==========================================================================*/

import { createTape, stopsFor } from './tape.js';
import { createScene, FACES } from './scene.js';
import { createMedia, fxSymbols } from './media.js';
import { COMBOS, PAY_MS, comboSize, paintCombo } from './paytable.js';
import { createSlotWords, subWords } from './word.js';   // the lone word, the sub pair / trio, the dead-spin settle (callout.js)
import { PACE } from './pace.js';
import { recipe, tierOf, meltedBy, ladderSemis, ladderPlan, spendTokens, glance, landPose, restPose, pressPose, glanceHoldMs } from './feel.js';
import { anticipation, almost } from './feel.js';   // the playbook's Tier A (CONTRACT 10.15)
import { ATTRACT, attractOk, emiLandings } from './feel.js';
// The playbook's Tier B and C (CONTRACT 10.16): B1 the spiral jar, B3 the comp, C1 the EMI pair re-spin.
import { jarPlan, jarParty, JAR_TIER, playsWithoutPress, respinKeep, respinHold } from './feel.js';
import { flowPlan, FLOW, CALLOUTS } from './feel.js';                     // THE FLOW on the landing frame
import { createCallout, GLYPH_HIT, WORD_MS, WORD_GAP_MS } from '../../shared/hypno/callout.js';
import { createBank } from './bank.js';
import { createSound } from './sound.js';
// THE SPINE (CONTRACT 10.22.C). One rung, one plan, one sit-down ledger, for all four stations. The slot
// keeps deciding its own OUTCOMES and its own cabinet recipe; it stopped deciding its own restraint.
import { houseTier } from '../../shared/win/tier.js';
import { freshSit, sitPlan, afterParty } from '../../shared/win/plan.js';
// THE SPARKLE BURST and THE GLOW (10.22.D): two moves counterfx has exported since the Arcademy shipped and
// the Back Room has never fired. The plan says WHEN and HOW MUCH; these two calls are the whole of the spend.
import { sparkBurst, warmGlow } from '../../../arcademy/shell/counterfx.js';

const STATION = 'slot';
/** CONTRACT 7 (room/loader.js): the root stays see-through, so the room's pan-in is the load screen and the cabinet
 *  rises over the room's held frame. There is no loading card: Back is live the whole way (Law VI). */
export const roomBehind = true;
export const roomStage = true;
const ROTATE_SEEN = 'br_slot_rotate_seen';   // the sideways nudge, dismissed once a session
const LINE_LABELS = {
  emi3: '3 EMI', emi2: '2 EMI', gif3same: '3 of the same GIF', sub3: '3 subliminals', spiral3: '3 spirals',
  gif3: '3 GIFs', sub2: '2 subliminals', spiral2: '2 spirals', melt: 'Melt',
};
const FREE_KINDS = new Set(['free', 'respin', 'jar', 'emi_respin']);   // every kind that costs no SP
const fmt = n => Number(n || 0).toLocaleString('en-US');

function loadCss() {
  if (document.querySelector('link[data-slot-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet'; link.href = new URL('./station.css', import.meta.url).href; link.dataset.slotCss = '';
  document.head.append(link);
}

export async function mount(ctx) {
  const t = (key, fallback, vars = {}) => {
    const s = typeof ctx.lex === 'function' ? ctx.lex(key, fallback) : fallback;
    return String(s ?? fallback).replace(/\{(\w+)\}/g, (_, k) => (k in vars ? vars[k] : `{${k}}`));
  };
  const reduced = !!ctx.reduced || (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  // CONTRACT 7: inside the room, the room's Back is the only Back (ctx.hostBack). Standalone (dev.html) keeps ours.
  const hostBack = ctx.hostBack === true;
  // CONTRACT 7: one station, three cabinets in the room. variant = { id, name, palette } or null.
  const variant = ctx.variant && typeof ctx.variant === 'object' ? ctx.variant : null;
  // CONTRACT 7.1: the room's SP chip, { set(value), owe(n), thud(), target() }. Standalone keeps .slot-sp.
  const hostSp = ctx.spReadout && typeof ctx.spReadout.set === 'function' && typeof ctx.spReadout.owe === 'function' ? ctx.spReadout : null;
  // Brake 8: a Calm bank, or a lite board, flies 4 tokens at most. ctx.lite is the device (room/loader.js) and
  // cannot change under a sit-down; the Calm half is read at open, as it has been.
  const lite = ctx.lite === true || String(ctx.intensity || '').toLowerCase() === 'calm';
  // Law VI: the coin shower is travel, so Calm and Motion off settle it exactly as reduced motion
  // does. Read live, because a settings frame changes ctx.intensity and ctx.motion under a sit-down.
  const stillFx = () => reduced || String(ctx.intensity || '').toLowerCase() === 'calm' || String(ctx.motion || '').toLowerCase() === 'off';
  loadCss();

  let el = null, scene = null, tape = null, media = null, session = 0, alive = false;
  let busy = false, suspended = false, unSp = null, lastMelt = null;
  let pace = 'idle', queued = false, breathEnds = 0, marks = [];   // pace phase: idle | breath | spin | reveal
  // Feel state for one sit-down (lane F1): the readout override while THE BANK flies, the win streak for
  // THE CHIME LADDER, how often each tier has partied (Brake 3), and EMI's current pose (THE MASCOT GLANCE).
  let sound = null, bank = null, shown = null, owing = false, streak = 0, words = null;
  // Brake 3's memory is plan.js's `freshSit` ledger now, not a pair of counters here: `seen` per RUNG and the
  // once-a-sit-down hero, one object, replaced (never mutated) by `afterParty` for every party that played.
  let sit = freshSit(), landPlan = null;
  let pose = 'idle0_0', glanceTimer = 0, gainTimer = 0, playing = null, feelLog = [], bankFrom = 0, bankTo = 0, bankHold = 1600, bankGlow = 0;
  // A4 attract: one idle timeout re-armed on input (no polling) and one self-re-arming wink, both cleared on
  // any input, on suspend and on close, so a shut cabinet leaves nothing running.
  let idleTimer = 0, winkTimer = 0, attracting = false;
  // B1: the jar count the tube is currently showing, while the ticks run ahead of the tape's own count
  // (the same override `shown` is for the SP readout). null follows the tape (Law I).
  let jarShown = null;
  // THE FLOW: the callout (one per open), the timers it and the fx ride on (cleared on suspend and close, Law VI),
  // when the next press may start after a landing, the host tunnel level A1 pulls, and the haze's idle timer.
  let callout = null, flowTimers = new Set(), flowLast = null, unlockAt = 0, tunnelLevel = 0, hazeTimer = 0;
  const $ = sel => el.querySelector(sel), wait = ms => new Promise(r => setTimeout(r, Math.max(0, ms)));
  function mark(phase) { pace = phase; marks = [...marks.slice(-79), { phase, at: Math.round(performance.now()) }]; }
  function note(what, extra = {}) { feelLog = [...feelLog.slice(-79), { what, at: Math.round(performance.now()), ...extra }]; }
  /** THE MARQUEE BOARD (scene.marquee): the line the cabinet's own sign shows while a spin plays. The board
   *  MIRRORS what the centre already says, it never replaces it: the announcer zoom and the word beat are
   *  untouched. null lets the cabinet name come back now; otherwise it holds for scene's MARQUEE_HOLD_MS. */
  function board(text) { if (scene) scene.marquee(text == null ? null : String(text).toUpperCase()); }

  function sendMelt(left) {
    if (left === lastMelt) return;
    lastMelt = left;
    const frame = { type: 'melt', station: STATION, left };
    if (typeof ctx.melt === 'function') ctx.melt(left);
    else if (ctx.bridge && typeof ctx.bridge.send === 'function') ctx.bridge.send(frame);
  }

  function build() {
    const root = document.createElement('div');
    root.className = 'slot-station'; root.dataset.phase = 'loading';
    if (ctx.stage) root.dataset.sharedRoom = 'true';
    if (reduced) root.dataset.reduced = '';   // Law VI: the jar takes the settled fill, never a slower one
    root.innerHTML = `
      <div class="slot-dim"></div>
      <canvas class="slot-stage" aria-label="${t('br_slot_stage', 'Slot cabinet')}"></canvas>
      <div class="slot-media" aria-hidden="true"></div>
      <div class="slot-hint" hidden>${t('br_slot_pull', 'Pull down')} &darr;</div>
      <header class="slot-top">
        <button class="slot-back" type="button">&larr; ${t('br_slot_back', 'Back')}</button>
        <span class="slot-sp"></span><span class="slot-comp" hidden></span><span class="slot-jackpot"></span><span class="slot-spirals" hidden></span>
        <div class="slot-status" aria-live="polite"></div>
      </header>
      <canvas class="slot-face" width="152" height="137" aria-hidden="true"></canvas>
      <span class="slot-gain" hidden></span><span class="slot-state-note" hidden></span>
      <div class="slot-payline" aria-hidden="true" hidden></div>
      <div class="slot-tokens" aria-hidden="true"></div>
      <div class="slot-callout" aria-live="polite"></div>
      <div class="slot-controls">
        <div class="slot-freeze">${[0, 1, 2].map(i => `<button type="button" data-col="${i}" aria-pressed="false"></button>`).join('')}</div>
      </div>
      <button class="slot-spin" type="button"><span></span><small></small></button>
      <details class="slot-odds"><summary>${t('br_slot_paytable', 'Prizes')}</summary><table></table><p></p></details>
      <div class="slot-card" role="status" hidden><p></p><button class="slot-card-back" type="button">${t('br_slot_back', 'Back')}</button></div>
      <button class="slot-rotate" type="button" hidden><i aria-hidden="true">&#x21bb;</i>${t('br_slot_rotate', 'Turn your phone sideways for a bigger view')}</button>`;
    root.querySelector('.slot-rotate').onclick = () => { rotateSeen = true; try { sessionStorage.setItem(ROTATE_SEEN, '1'); } catch (e) { /* private mode */ } paintRotate(); };
    root.querySelector('.slot-odds').addEventListener('toggle', payLoop);
    root.querySelector('.slot-back').onclick = back;
    root.querySelector('.slot-card-back').onclick = back;
    if (hostSp) root.dataset.hostSp = '';
    if (hostBack) {
      root.dataset.hostBack = '';
      root.querySelector('.slot-back').hidden = !ctx.stage;
      root.querySelector('.slot-card-back').hidden = true;
    }
    root.addEventListener('pointerdown', onPoke, true);   // A4: any pointer press ends attract (the lever included)
    root.querySelector('.slot-spin').onclick = () => press();
    root.querySelectorAll('[data-col]').forEach(b => { b.onclick = () => toggleFreeze(Number(b.dataset.col)); });
    return root;
  }

  /* THE SIDEWAYS NUDGE (phone desk run): in portrait the reels are small, so a phone (a coarse pointer) at the cabinet
   * gets one dismissable hint to turn sideways. Remembered for the session once dismissed; it hides itself in landscape
   * and shows again in portrait until then. A desk (fine pointer, no touch) never sees it. */
  let rotateSeen = false;
  try { rotateSeen = sessionStorage.getItem(ROTATE_SEEN) === '1'; } catch (e) { /* private mode */ }
  const coarse = () => (typeof matchMedia === 'function' && matchMedia('(any-pointer: coarse)').matches) || (navigator.maxTouchPoints || 0) > 0;
  function paintRotate() {
    const n = el && $('.slot-rotate');
    if (!n) return;
    n.hidden = rotateSeen || !coarse() || !(innerHeight > innerWidth) || el.dataset.phase !== 'play';
  }

  function back() {
    endAttract(true);
    // Law VIII: Back rings on the frame it is asked (standalone; in the room the room's chip answers its own press).
    if (el && !hostBack) $('.slot-back').classList.add('is-ringing');
    if (typeof ctx.standUp === 'function') ctx.standUp();
    else close();
  }

  function card(text) {
    if (!el) return;
    $('.slot-card p').textContent = text || '';
    $('.slot-card').hidden = !text;
  }

  function refusalText(reason) {
    if (reason === 'insufficient') return t('br_slot_insufficient', 'Not enough SP for this spin.');
    if (reason === 'closed') return t('br_slot_closed', 'The cabinet is closed for a moment.');
    if (reason === 'empty' || reason === 'tape_unplayed') return t('br_slot_stuck', 'The reels stuck. Pull again.');
    return t('br_slot_offline', 'The house is not answering. Try again in a moment.');
  }

  /* ONE SP READOUT (in-room tidy, lane F1). In the room, ctx.spReadout is the only SP on screen and THE
   * BANK's target (CONTRACT 7.1): the room keeps its chip on Law I shownSp from what the tape owes (owe), and
   * the station only hands it a number while THE BANK flies (set). Standalone, .slot-sp is both. */
  const readout = () => (hostSp ? hostSp.target() : el && $('.slot-sp'));
  const shownSp = () => (shown ?? (tape ? tape.snapshot().shownSp : 0));
  function paintSp() {
    if (hostSp) { hostSp.set(shown); return; }
    const node = el && $('.slot-sp'), text = t('br_slot_sp', '{n} SP', { n: fmt(shownSp()) });
    if (node && node.textContent !== text) node.textContent = text;
  }

  /** B1 THE SPIRAL JAR (10.16.A), as a READING and no longer as a tube.
   *
   *  The upright glass stood outboard of the cabinet, which on a phone put it directly beside EMI - owner,
   *  2026-09-16: "that bar near emi is horrible, remove it". The jar itself is untouched: the tape still
   *  counts the spirals, still spills, still pays its free spins and still throws its tier 2 party. What is
   *  gone is the fill. The COUNT stays, in the status line with the rest of the tape's state, which is what
   *  Brake 9 asked for in the first place (every value is also text) and what Law XII needs to show it move.
   *  `jarShown` is the tick override while the reels are still stopping; null follows the tape (Law I). */
  function jarPart() {
    if (!tape) return null;
    const s = tape.snapshot(), size = s.jarSize;
    if (!(size > 0)) return null;
    const v = Math.max(0, Math.min(size, jarShown ?? s.jar));
    return t('br_slot_jar_count', 'Spirals {n} / {m}', { n: fmt(v), m: fmt(size) });
  }

  function sync() {
    if (!el || !tape) return;
    const s = tape.snapshot();
    paintSp();
    $('.slot-jackpot').textContent = t('br_slot_jackpot', 'Jackpot {n}', { n: fmt(s.jackpot) });
    // 10.16.C: EMI hands the comp over, there is no ceremony. A chip beside the SP readout until it is spent.
    const chip = $('.slot-comp');
    chip.hidden = !s.comp;
    if (s.comp) chip.textContent = t('br_slot_comp_chip', 'On the house');
    const parts = [];
    if (s.comp) parts.push(t('br_slot_comp', 'On the house: {n} spins', { n: s.comp.spins }));
    parts.push(t('br_slot_last_win', 'Last win {n}', { n: fmt(s.lastWin) }), t('br_slot_free_left', 'Free spins {n}', { n: s.free }));
    parts.push(s.melt ? t('br_slot_melt_left', 'Melt: {n} spins at half', { n: s.melt }) : t('br_slot_ready', 'Ready'));
    // The jar reads as its own short chip, not as another clause in an already long sentence: on a phone on
    // its side the status chip is a narrow column, and every clause there costs it two wrapped lines.
    const jarChip = $('.slot-spirals'), jar = jarPart();
    jarChip.hidden = !jar;
    if (jar && jarChip.textContent !== jar) jarChip.textContent = jar;
    const status=$('.slot-status');
    status.textContent=ctx.stage?t('br_slot_last_win','Last win {n}',{n:fmt(s.lastWin)}):parts.join('  ·  ');
    status.setAttribute('aria-label',parts.join('  ·  '));
    const extraNote=$('.slot-state-note');
    extraNote.hidden=!ctx.stage||!(s.comp||s.free||s.melt);
    extraNote.textContent=[s.comp?t('br_slot_comp','On the house: {n} spins',{n:s.comp.spins}):'',s.free?t('br_slot_free_left','Free spins {n}',{n:s.free}):'',s.melt?t('br_slot_melt_left','Melt: {n} spins at half',{n:s.melt}):''].filter(Boolean).join(' · ');
    const playable = el.dataset.phase === 'play';
    el.dataset.pace = pace;
    el.querySelectorAll('[data-col]').forEach((b, i) => {
      const on = s.hold === i, roman = ['I', 'II', 'III'][i];
      b.setAttribute('aria-pressed', String(on));
      b.textContent = on ? t('br_slot_frozen', 'Frozen {n}', { n: roman }) : t('br_slot_freeze', 'Freeze {n}', { n: roman });
      b.disabled = !playable || busy || !s.canFreeze;
    });
    const spin = $('.slot-spin');
    spin.disabled = !playable || (busy && pace !== 'reveal');   // a press in the reveal waits for the breath
    spin.querySelector('span').textContent = t('br_slot_spin', 'Spin');
    spin.querySelector('small').textContent =
      s.hold !== null ? t('br_slot_cost', '{n} SP', { n: s.freezeCost })
      : FREE_KINDS.has(s.nextKind) ? t('br_slot_free_spin', 'Free spin')
      : s.onTape ? t('br_slot_on_tape', '{n} left on tape', { n: s.onTape })
      // 10.16.C: the next buy is the comp, at 0 SP. Every press after that is a normal paid tape.
      : s.comp ? t('br_slot_comp_cost', 'Free')
      : s.tapeCount >= 1 ? t('br_slot_tape_cost', '{n} SP for {n} spins', { n: s.tapeCount })
      : t('br_slot_cost', '{n} SP', { n: s.stake });
    if (scene) {
      scene.setHold(s.hold);
      scene.screen('marquee', variant && variant.name ? String(variant.name).toUpperCase() : t('br_slot_marquee', 'CANDY'));
      scene.screen('screen_jackpot', t('br_slot_screen_jackpot', 'JACKPOT {n}', { n: fmt(s.jackpot) }));
      scene.screen('screen_status', s.melt ? t('br_slot_screen_melt', 'MELT · {n} SPINS AT HALF', { n: s.melt })
        : t('br_slot_screen_status', 'WIN {n} · FREE {m}', { n: fmt(s.lastWin), m: s.free }));
    }
  }

  function drawHud(name) {
    const c = el && $('.slot-face'), img = scene && scene.faceImage;
    if (!c || !img) return;
    const g = c.getContext('2d');
    g.clearRect(0, 0, 152, 137);
    g.drawImage(img, (FACES[name] ?? 3) * 152, 0, 152, 137, 0, 0, 152, 137);
  }
  function setFace(name) { pose = name; if (scene) scene.setFace(name); drawHud(name); }
  /** THE MASCOT GLANCE: react now (inside Law VIII's 100 ms), hold, then settle on the rest pose. Never the
   *  same pose twice in a row; the rest is skipped when she is already there. */
  function glanceTo(want, holdMs, rest) {
    clearTimeout(glanceTimer);
    setFace(glance(pose, want));
    note('glance', { pose });
    if (rest) glanceTimer = setTimeout(() => { if (alive && pose !== rest) { setFace(rest); note('rest', { pose }); } }, holdMs);
  }

  /* A4 ATTRACT (House Book deck II, playbook Tier A). After ATTRACT.IDLE_MS seated with nothing running the
   * cabinet attracts itself: the reels drift, a chase sweeps every ~8 s, EMI winks. No SP moves and nothing is
   * read from the tape, so it can end at any frame. Off on Calm, under reduced motion and while melted
   * (Brake 5). Any input ends it on the frame it arrives (Law VIII) and the reels ease home (Brake 1). */
  const attractState = () => ({
    seated: !!el && el.dataset.phase === 'play', phase: pace, busy, banking: !!(bank && bank.busy),
    meltLeft: tape ? tape.snapshot().melt : 0, calm: lite, reduced, suspended,
  });
  function armIdle() {
    clearTimeout(idleTimer); idleTimer = 0;
    clearTimeout(hazeTimer); hazeTimer = 0;
    if (!alive || suspended || lite || reduced) return;   // Calm and reduced motion never arm it at all
    idleTimer = setTimeout(() => { idleTimer = 0; enterAttract(); }, ATTRACT.IDLE_MS);
    // THE ATTRACT HAZE: 8 s idle, the same gate as the drift (seated, quiet, not melted), off on the next press.
    hazeTimer = setTimeout(() => { hazeTimer = 0; if (scene && attractOk(attractState()) && scene.haze(true)) note('haze', { on: true }); }, FLOW.HAZE_IDLE_MS);
  }
  function enterAttract() {
    if (attracting || !scene || !attractOk(attractState()) || !scene.attract(true)) return;
    attracting = true;
    winkTimer = setTimeout(wink, ATTRACT.WINK_FIRST_MS);
    note('attract', { on: true });
  }
  /** THE MASCOT GLANCE, unprompted: a short chain that returns to rest. Never the same pose twice (glance()). */
  function wink() {
    winkTimer = 0;
    if (!attracting || !alive) return;
    glanceTo('hearts', glanceHoldMs(false), restPose(0));
    winkTimer = setTimeout(wink, ATTRACT.WINK_MS);
  }
  /** Ends it and drops both timers. `now` snaps the reels home instead of easing (Back, suspend: Law VI). */
  function endAttract(now = false) {
    clearTimeout(idleTimer); idleTimer = 0;
    clearTimeout(winkTimer); winkTimer = 0;
    clearTimeout(hazeTimer); hazeTimer = 0;
    if (scene && scene.hazing) { scene.haze(false); note('haze', { on: false }); }
    if (!attracting) return;
    attracting = false;
    if (scene) scene.attract(false, now);
    note('attract', { on: false });
  }
  const onPoke = () => { if (!alive) return; endAttract(); armIdle(); };

  /** THE THUD on the SP readout: the bank's last token (a mini-thud). Reduced motion: a lit state, no scale. */
  function thudReadout() {
    if (hostSp) { hostSp.thud(); return; }
    const box = readout();
    if (!box || typeof box.animate !== 'function') return;
    if (reduced) { box.animate([{ boxShadow: '0 0 0 2px #ffcf6b' }, { boxShadow: '0 0 0 2px #ffcf6b' }], { duration: 520 }); return; }
    box.animate([{ transform: 'scale(1.3)', filter: 'brightness(2.2)' }, { transform: 'scale(.94)', offset: 0.55 }, { transform: 'scale(1)', filter: 'brightness(1)' }],
      { duration: 340, easing: 'cubic-bezier(.2,1.5,.4,1)' });
  }
  /** Brake 9: every value is also text. `holdMs` keeps the +N up for a long rollup, so it is still there when
   *  the count settles (playbook A3), not gone 1.6 s into a 6 s climb. */
  function gain(n, holdMs = 1600) {
    const g = el && $('.slot-gain');
    if (!g) return;
    g.textContent = t(n >= 0 ? 'br_slot_gain' : 'br_slot_spent', n >= 0 ? '+{n} SP' : '-{n} SP', { n: fmt(Math.abs(n)) });
    g.hidden = false; g.dataset.sign = n >= 0 ? 'up' : 'down';
    clearTimeout(gainTimer); gainTimer = setTimeout(() => { if (el) $('.slot-gain').hidden = true; }, Math.max(0, holdMs));
  }
  const readoutAt = () => { const b = readout() && readout().getBoundingClientRect(); return b && b.width ? { x: b.left + b.width / 2, y: b.top + b.height / 2 } : null; };
  /** THE BANK forwards (a win) or reversed (a tape or freeze debit). `roll` (playbook A3) is how long the
   *  readout keeps counting: the tokens are the same either way, the count-up is what scales to the win.
   *  `glowMs` is plan.glow - THE GLOW the chip takes on the mini-thud, at the END of the count and not at the
   *  start of the flight (Law X: it shares the frame the value settles on). A spend never glows: Law IX sizes
   *  a PARTY, and money leaving is not one. */
  function flyBank(kind, fromValue, toValue, n, roll = 0, glowMs = 0) {
    shown = fromValue;
    const from = kind === 'pay' ? () => scene && (scene.project('payout_spawn') || scene.project('payout_tray')) : readoutAt;
    const to = kind === 'pay' ? readoutAt : () => scene && (scene.project('payout_tray') || scene.project('payout_spawn'));
    if (kind === 'pay' && scene) scene.trayThud();
    if (!(bank.busy && bank.kind === 'pay' && kind === 'pay')) bankFrom = fromValue;   // a merged pay keeps its first value
    bankTo = toValue;
    bankHold = Math.max(1600, roll + 600);
    bankGlow = kind === 'pay' ? Math.max(0, Number(glowMs) || 0) : 0;
    const how = bank.start({ kind, n, fromValue, toValue, from, to, rollupMs: roll });
    note('bank', { kind, fromValue, toValue, n, roll, how });
    paintSp();
  }

  /* THE LEGEND (owner, 2026-09-16: "show the prize payout with simple mockups ... and what combinations they
   * can get and what it pays"). Each row now carries a PICTURE of the row that pays, painted by the reels'
   * own painter, so a flash cell is the player's own dealt GIF and a spiral cell is the same Loom field the
   * glass is showing. The words stay (Brake 9: every value is also text) - the picture is added to the label,
   * never instead of it, and the canvas is aria-hidden so a screen reader reads the row once. */
  let combos = [];
  function renderOdds() {
    const s = tape.snapshot();
    const table = $('.slot-odds table');
    combos = [];
    table.replaceChildren(...s.lines.map(l => {
      const tr = document.createElement('tr');
      const ids = COMBOS[l.id];
      const art = document.createElement('td');
      art.className = 'slot-combo';
      if (ids) {
        const size = comboSize(ids.length), ratio = Math.min(3, Math.max(1, devicePixelRatio || 1));
        const canvas = document.createElement('canvas');
        canvas.width = Math.round(size.w * ratio); canvas.height = Math.round(size.h * ratio);
        canvas.style.width = `${size.w}px`; canvas.style.height = `${size.h}px`;
        canvas.setAttribute('aria-hidden', 'true');
        art.append(canvas);
        combos.push({ canvas, ids, ratio });
      }
      tr.append(art);
      // 10.16.D: the jackpot's published odds are the TOTAL (the direct draw plus the re-spin's share), so the
      // direct-draw row never understates it; the emi2 row says what it actually buys.
      const odds = l.id === 'emi3' && s.jackpotOdds ? s.jackpotOdds
        : l.id === 'emi2' ? `${l.odds || ''} · ${t('br_slot_respin', 'One more look')}` : String(l.odds || '');
      // Brake 9 twice over: the label names the row and the odds sit under it, both as text, both beside the
      // picture. Four columns did not fit a 330 px sheet - the odds were simply cut off the right edge.
      const th = document.createElement('th');
      th.textContent = t(`br_slot_line_${l.id}`, LINE_LABELS[l.id] || String(l.id));
      if (odds) { const small = document.createElement('small'); small.textContent = odds; th.append(small); }
      const pay = document.createElement('td');
      pay.className = 'slot-pay';
      pay.textContent = l.pays > 0 ? t('br_slot_pay_sp', '{n} SP', { n: fmt(l.pays) }) : '-';
      tr.append(th, pay);
      return tr;
    }));
    paintCombos(performance.now());
    const stake = t('br_slot_stake', 'Each spin costs {n} SP. A freeze costs {m} SP.', { n: s.stake, m: s.freezeCost });
    $('.slot-odds p').textContent = s.jarSize > 0
      ? `${stake} ${t('br_slot_jar_odds', 'The jar pays {n} free spins every {m} spirals.', { n: s.jarFree, m: fmt(s.jarSize) })}`
      : stake;
  }

  /* The legend's own clock. It runs ONLY while the panel is open, at the decoder's 12 Hz and not the frame
   * rate, and it stops on close, on suspend and under reduced motion (Law VI takes the settled picture, which
   * for a still deck is one paint). A shut panel costs nothing: nine rows of three cells is real work and
   * nobody is looking at it. */
  let payRaf = 0, payAt = -Infinity;
  const lookNow = () => ({ gif: i => (media ? media.gif(i) : null), word: i => (media ? media.word(i) : null),
                           reduced, face: scene ? scene.faceImage : null });
  function paintCombos(now) {
    if (!el || !alive || !combos.length) return;
    const look = lookNow();
    for (const c of combos) paintCombo(c.canvas, c.ids, now, look, c.ratio);
    payAt = now;
  }
  function payFrame(now) {
    payRaf = 0;
    if (!el || !alive || suspended || !$('.slot-odds') || !$('.slot-odds').open) return;
    if (now - payAt >= PAY_MS) paintCombos(now);
    payRaf = requestAnimationFrame(payFrame);
  }
  function payLoop() {
    if (payRaf) { cancelAnimationFrame(payRaf); payRaf = 0; }
    const open = !!el && !!$('.slot-odds') && $('.slot-odds').open && !suspended && alive;
    if (!open) return;
    paintCombos(performance.now());
    if (!reduced) payRaf = requestAnimationFrame(payFrame);
  }

  function toggleFreeze(col) {
    endAttract(); armIdle();
    if (!alive || busy || !tape || el.dataset.phase !== 'play') return;
    const previous = tape.snapshot().hold;
    tape.toggleHold(col);
    const held = tape.snapshot().hold;
    if (held !== previous) { sound.arm(); sound.freeze(held === null); }
    sync();   // scene.setHold dips the button this frame (Law VIII)
  }

  function fireFx(fxId, keys, args) {
    if (suspended || !alive || typeof ctx.fx !== 'function') return;
    try {
      const p = ctx.fx(fxId, keys && keys.length ? keys : undefined, args);   // never awaited: fx-ack is advisory
      if (p && typeof p.catch === 'function') p.catch(() => {});
    } catch (e) { console.warn('[slot] fx failed', e); }
  }
  /** The outcome's fx, on the frame the result shows (Law I). word.js shows the dealt subliminal word(s) on the page
   *  itself (fx.sub_single stays here; sub_pair / sub_cascade go to the host with { wordsShown: true }) and settles
   *  a dead spin (line none, no fx) with EMI's bark; every other fx id goes to the host as before. */
  function fire(o, tease) {
    // EMI's wink lands a tick after land()'s own glance on this frame, so the settle's face is hers and not the rest's.
    // The bark stands over wherever she is: her shelf beside the reels while seated, her topper before that
    // (scene.js). Without the node the callout keeps its own centred place.
    if (!words) words = createSlotWords({ ctx, mount: el, media, lex: ctx.lex,
      emi: { react: () => setTimeout(() => { if (alive) glanceTo('hearts', glanceHoldMs(false), restPose(tape ? tape.snapshot().melt : 0)); }, 0),
             anchor: () => (scene ? scene.project('emi_topper', true) : null) } });
    return words.outcome(o, fireFx, { tease: !!tease });
  }
  /** The host tunnel (CONTRACT 10.13.B): A1's hold pulls it to FLOW.TEASE_TUNNEL, the landing releases it. Sent
   *  on change only; the room's `tunnel` gate (a missing key reads true) drops it to 0. */
  function tunnel(level) {
    const want = ctx.gates && ctx.gates.tunnel === false ? 0 : Math.max(0, Math.min(1, Number(level) || 0));
    if (want === tunnelLevel) return;
    tunnelLevel = want;
    if (typeof ctx.fxTunnel === 'function') { try { ctx.fxTunnel(want); } catch (e) { console.warn('[slot] tunnel failed', e); } }
    note('tunnel', { level: want });
  }
  /** One flow timer: this open's, this session's; suspend and close clear them all (Law VI). */
  function later(ms, fn) {
    const my = session;
    const id = setTimeout(() => { flowTimers.delete(id); if (alive && !suspended && my === session) fn(); }, Math.max(0, ms));
    flowTimers.add(id);
  }
  function clearFlow() {
    for (const id of flowTimers) clearTimeout(id);
    flowTimers.clear();
    if (callout) callout.cancel();
  }
  /** THE FLOW from the landing frame (feel.flowPlan): the glyph hit now, then at each `at` the callout(s) and the
   *  row's own host fx together (the GIF tease's flash rides the same frame). `respinRow`: the row the spiral2
   *  respin granted takes its "Respin" word first; `jarWord`: the jar's Overflow already holds the frame, so a
   *  small word yields to it. Law I: everything here reads the row the tape landed. */
  function flow(o, { respinRow = false, jarWord = false } = {}) {
    const plan = flowPlan(o, { respinRow, jarWord }), at = performance.now();
    unlockAt = at + plan.unlockMs;
    flowLast = { line: o.line, kind: o.kind, landedAt: Math.round(at), hits: plan.hits, callouts: plan.callouts.map(c => c.key),
                 fx: plan.fx.map(f => f.id), unlockMs: plan.unlockMs, calloutAt: null, fxAt: null };
    scene.highlight(plan.hits);
    if (el && plan.hits.length) { el.classList.add(GLYPH_HIT); later(FLOW.HIGHLIGHT_MS, () => { if (el) el.classList.remove(GLYPH_HIT); }); }
    const chain = plan.fx.some(f => /^fx.sub_/.test(f.id)) ? subWords(o, media).length : 0;   // a sub chain owns the centre first
    const wordsMs = chain ? WORD_MS + WORD_GAP_MS * (chain - 1) : 0;
    for (const [i, c] of plan.callouts.entries()) later(c.at + wordsMs, () => {
      if (callout) callout.show(c.key, c.fallback, { tier: c.tier });
      board(t(c.key, c.fallback));   // THE MARQUEE BOARD mirrors the announcer
      // 10.22.D THE SPARKLE BURST: 7 sparks at tier 3, 9 at the jackpot, from the middle of the callout
      // layer as the word arrives. ONE burst a moment (Brake 2), so only the FIRST callout of the frame takes
      // it, and `plan.sparkle` is the whole of the rest of the gate - it is already 0 on a small win, on lite,
      // on Calm, while melted and under reduced motion. land() runs on the same frame flow() was called on and
      // this fires FX_DELAY_MS later, so the plan standing here is always this landing's own.
      if (i === 0 && landPlan && landPlan.sparkle > 0) {
        const n = sparkBurst(el && $('.slot-callout'), { count: landPlan.sparkle });
        note('sparkle', { count: n, tier: landPlan.spent });
      }
      if (flowLast) flowLast.calloutAt = Math.round(performance.now());
      note('callout', { key: c.key, tier: c.tier, at: c.at });
    });
    const tease = plan.fx.find(f => f.args && f.args.count === 1) || null;   // the GIF tease rides its own entry
    later(FLOW.FX_DELAY_MS, () => {
      const shown = fire(o, tease);   // word.js: the words on the page, the rest to the host, a dead spin settles
      if (tease) fireFx(tease.id, fxSymbols(tease.id, o, media), tease.args);
      if (flowLast) { flowLast.fxAt = Math.round(performance.now()); flowLast.words = shown ? shown.words : []; }
      note('fx', { ids: plan.fx.map(f => f.id), at: FLOW.FX_DELAY_MS, words: shown ? shown.words.length : 0, settled: !!(shown && shown.settled) });
    });
    return plan;
  }

  /** The landing beat (Law X): the party the Brake allows, the ladder, the tokens and EMI, all on one frame.
   *  Melt reads from the tape cursor, never a freeze outcome's own meltLeft (the stored tape's end melt). */
  function land(landed, before) {
    const melt = tape.snapshot().melt, o = (landed.meltLeft || 0) === melt ? landed : { ...landed, meltLeft: melt };
    const tier = tierOf(o), melted = meltedBy(o), r = recipe(o, { seen: sit.seen[tier], jackpots: sit.heroes });
    // THE PLAN (CONTRACT 10.22.C). The slot's own tierOf still says what the line is WORTH; houseTier only
    // normalises it onto the room's rungs, and sitPlan reads Brake 3 off the one ledger. Everything this
    // beat is allowed to spend - tokens, rollup, ladder, shower, sparks, glow - comes back frozen in `plan`,
    // and the station may always spend LESS than it, never more. `reduced` and `still` are two flags, not
    // one: reduced motion is the settled state (no travel at all), Calm strips the decoration and the value
    // still flies, because a number that just changes is a Law XII break at every motion level.
    const plan = sitPlan(houseTier({ station: STATION, tier }), sit, { reduced, lite, still: stillFx(), melted });
    const semis = ladderSemis(streak, melted), roll = plan.partyMs;   // playbook A3: the count-up scales to the win
    landPlan = plan;   // flow()'s callout frame reads it FX_DELAY_MS from now (THE SPARKLE BURST)
    // Law IX, once a sit-down: the hero the ledger counts is the REVEAL the cabinet actually played, which is
    // feel.recipe's (the same pop and turn at every motion level), not plan.reveal - that is the decoration
    // budget on top of it. A party that spent nothing is not a party and does not wear the rung down.
    sit = afterParty(sit, r.reveal ? { ...plan, reveal: true } : plan);
    if (tier > 0) { sound.win(r.sound, semis); streak++; } else streak = 0;   // the no-pay cue was the last reel's muted thud
    scene.setMelted(melted); if (melted) sound.melt();
    if (o.line === 'melt') { scene.malus(); sound.malus(); }
    scene.celebrate(r, o.pay, t('br_slot_screen_win', 'WIN +{n}', { n: fmt(o.pay) }));
    // 10.22.B: the room is told ONCE, on the frame the player learns it, and it is told the PLAN's shower
    // tier, never the slot's own rung. 0 means no shower at all (Law IX: a small win is a close-up event and
    // does not show from across the room), and room/coin-shower.js clamps 1..4, so the call is SKIPPED
    // rather than made with a 0 - that guard is the station's, the spine cannot make it from here.
    if (plan.shower > 0) ctx.revealedWin?.(o.pay, plan.shower, t('br_slot_screen_win', 'WIN +{n}', { n: fmt(o.pay) }));
    if (r.tokens) {
      flyBank('pay', before, tape.snapshot().shownSp, plan.bank, roll, plan.glow);
      // THE CHIME LADDER climbs while the readout counts. Law VI: reduced motion has no rollup to climb over
      // and THE BANK has already settled, so plan.partyMs is 0 and the ladder is the landing note alone.
      sound.climb(ladderPlan(plan.spent, roll, melted), semis);
      scene.payline(roll, r);                                // A6: the winning row frames for the same window
    }
    glanceTo(landPose(o), glanceHoldMs(melted), restPose(o.meltLeft));
    // THE MARQUEE BOARD: the same result the pills carried. A win first, then the free spins it granted, then
    // the melt it left, else Ready. The announcer's own name lands on the sign FX_DELAY_MS later (flow()).
    const after = tape.snapshot();
    board(o.pay > 0 ? t('br_slot_screen_win', 'WIN +{n}', { n: fmt(o.pay) })
      : after.free > 0 ? t('br_slot_free_left', 'Free spins {n}', { n: after.free })
      : after.melt ? t('br_slot_screen_melt', 'MELT · {n} SPINS AT HALF', { n: after.melt })
      : t('br_slot_ready', 'Ready'));
    note('land', { line: o.line, pay: o.pay, tier, spent: plan.spent, why: plan.why, party: r.party,
                   sound: r.sound, melted, streak, roll, shower: plan.shower, sparkle: plan.sparkle });
  }

  /**
   * B1: the jar spills. A tier 2 party (feel.jarParty) plus fx.spiral_full, and THEN the free spins play as
   * any free spins do. Brake 2: an outcome that also won tier 2 or better keeps its own party and the jar's
   * note is dropped. Calm: the fill only, no party, though fx.spiral_full still fires at its Calm recipe.
   */
  function jarSpill(p) {
    const s = tape.snapshot();
    fireFx('fx.spiral_full');
    const r = jarParty({ melted: meltedBy(p.o), calm: lite, winTier: tierOf(p.o), seen: sit.seen[JAR_TIER] });
    if (r) {
      // Brake 3's ledger is the spine's and there is one of it: the jar's tier 2 party wears down the SAME
      // rung a tier 2 line does, because the jar borrows spiral3's shape since it IS that event (10.16.A).
      sit = afterParty(sit, sitPlan(JAR_TIER, sit, { reduced, lite, still: stillFx(), melted: r.melted }));
      // Brake 2: the outcome won a line as well, so the jar's own note is dropped and the win's note, a beat
      // later on its own landing, is the one that sounds. The party is still the jar's, the bigger of the two.
      if (r.sound) sound.win(r.sound, ladderSemis(streak, r.melted));
      scene.celebrate(r, 0, t('br_slot_jar_full', 'The jar spills: {n} free spins', { n: s.jarFree }));
      glanceTo('spirals', glanceHoldMs(r.melted), restPose(s.melt));
      // THE FLOW: the jar's own word, on the tick that fills it (a big callout; a small landing word yields to it).
      if (callout) callout.show(CALLOUTS.jar.key, CALLOUTS.jar.fallback, { tier: CALLOUTS.jar.tier });
      board(t(CALLOUTS.jar.key, CALLOUTS.jar.fallback));
      p.jarWord = true;
      note('callout', { key: CALLOUTS.jar.key, tier: CALLOUTS.jar.tier, at: 'jar' });
      // The reading's own flash rides the same frame (Law X). Reduced motion takes the settled text, no flash.
      const box = el && $('.slot-spirals');
      if (box && !reduced && typeof box.animate === 'function') {
        box.animate([{ filter: 'brightness(2.4)' }, { filter: 'brightness(1)' }], { duration: 480, easing: 'ease-out' });
      }
    }
    note('jar-full', { free: s.jarFree, party: r ? r.party : null, winTier: tierOf(p.o) });
  }

  /** Tier A and B on a reel's thud frame (Law X, one gesture one beat): reel 2's thud opens A1's rising tone,
   *  the last reel's thud carries A2's ghost under the same muted thud, and every spiral ticks the jar on the
   *  thud of the reel it landed on, never before (10.16.A). */
  function stopFeel(p, i) {
    if (i === 1 && !p.respin && p.ant && p.ant.holdMs > 0) { sound.rise(PACE.STAGGER_MS + p.ant.holdMs, lite); tunnel(FLOW.TEASE_TUNNEL); note('tease', { kind: p.ant.kind, holdMs: p.ant.holdMs }); }
    if (i === p.lastReel) tunnel(0);   // A1's pull releases on the landing frame, whatever held it
    if (i === p.lastReel && p.near) { scene.almost(p.near); sound.almost(lite); note('almost', p.near); }
    const k = p.jar ? p.jar.reels.indexOf(i) : -1;
    if (k >= 0) {
      jarShown = p.jar.values[k];
      sync();                             // the reading ticks with the reel that filled it (Law XII)
      note('jar-tick', { reel: i, value: jarShown });
      if (p.jar.full && k === p.jar.reels.length - 1) jarSpill(p);
    }
  }

  /**
   * ONE BEAT: the reels travel, the outcome lands, the reveal holds. `before` is the snapshot from before the
   * tape was asked, so the spend, the melt, the strips and the jar are all read at the right moment.
   * Answers false when the sit-down moved on under it.
   */
  async function beat(r, before, my) {
    const o = r.outcome, after = tape.snapshot();
    if (after.shownSp < before.shownSp) flyBank('spend', before.shownSp, after.shownSp, spendTokens(before.shownSp - after.shownSp, lite));
    scene.setMelted(before.melt > 0);
    // C1 (10.16.D): the re-spin holds reels 1 and 2 as EMI and brings reel 3 back for the FULL 1,400 ms gold
    // hold, never halved, not even while melted, because this beat IS the event. It gets no ALMOST tell.
    const respin = playsWithoutPress(o.kind), keep = respinKeep(o.kind);
    // The playbook's Tier A. Both read the outcome the tape already carries: A1 only delays the third
    // reel, A2 only reads the strip cell the server's own stop landed beside. No stop is ever weighted.
    const ant = respin ? respinHold() : anticipation(o, before.strips, { melted: before.melt > 0, held: r.held });
    playing = { o, respin, keep, lastReel: respin ? 2 : [2, 1, 0].find(i => i !== r.held), ant,
                near: respin ? null : almost(o, before.strips, { held: r.held }),
                emi: emiLandings(o),                                  // A5: which reels wiggle
                jar: jarPlan(o, before.jar, before.jarSize) };        // B1: which reels tick the tube
    mark('spin'); sync();
    // Reel 3 is alone here, so the rising tone runs from the start of its travel: there is no reel 2 thud to
    // open it (A1's own tone still opens on that thud for every other spin, in stopFeel).
    if (respin) { sound.rise(PACE.SPIN_MS + ant.holdMs, lite); tunnel(FLOW.TEASE_TUNNEL); note('tease', { kind: ant.kind, holdMs: ant.holdMs, respin: true }); }
    await scene.spin(Array.isArray(o.stops) ? o.stops : stopsFor(before.strips, o.symbols), r.held,
                     { holdMs: ant.holdMs, gold: ant.gold && !lite, dim: !lite, keep });
    if (my !== session || !alive) return false;
    const shownBefore = shownSp();
    const p = playing; playing = null;
    tape.land(o);
    flow(o, { respinRow: o.kind === 'respin', jarWord: !!(p && p.jarWord) });   // THE FLOW: hit now, word + fx at 400 ms
    land(o, shownBefore);
    jarShown = null;                      // Law I: whatever the ticks showed, sync() below settles on the tape's count
    scene.reveal(o.pay > 0); mark('reveal'); sync();
    await wait(PACE.REVEAL_MS);
    return my === session && alive;
  }

  async function press() {
    if (!alive || suspended || !scene || el.dataset.phase !== 'play') return;
    // On a phone the open legend is a sheet over the glass, and a pull means "I am playing, not reading".
    // A desk keeps it: there it sits in its own corner and covers nothing.
    const panel = $('.slot-odds');
    if (panel && panel.open && (innerWidth <= 800 || innerHeight <= 500)) panel.open = false;
    endAttract();
    sound.arm();
    // Law VI, Brake 7: one press settles a rollup that is still counting, straight to the tape's value, with
    // the mini-thud and the +N it would have ended on. The rest of the climb and the frame's pulse go quiet.
    if (bank && bank.kind === 'pay') { bank.skip({ land: true }); sound.hush(); scene.paylineOut(); note('skip', { rollup: true }); }
    if (busy) { if (pace === 'reveal') { queued = true; scene.answer(); sound.lever(); note('answer', { queued: true }); } return; }
    const my = session, before = tape.snapshot();
    // Law VIII: the lever leans and EMI glances on this frame, before the tape or the server answers.
    scene.answer(); sound.lever(); clearTimeout(glanceTimer); setFace(glance(pose, pressPose()));
    note('answer', { pose });
    busy = true; queued = false; card(null); mark('breath'); sync();
    board(t('br_slot_marquee_spin', 'Spinning'));   // THE MARQUEE BOARD: the pull is on the sign before the tape answers
    // THE BREATH (PACE): the next spin starts no sooner than BREATH_MS after the last reveal; the buy runs meanwhile.
    const [r] = await Promise.all([tape.press(), wait(breathEnds - performance.now())]);
    if (my !== session || !alive) return;
    if (r.kind !== 'play') {
      busy = false; mark('idle'); scene.letGo(); setFace(glance(pose, restPose(before.melt)));
      if (r.kind === 'refused') card(refusalText(r.reason));
      board(null);
      sync(); armIdle();
      return;
    }
    let step = r, from = before;
    for (;;) {
      if (!(await beat(step, from, my))) return;
      // THE FLOW: a paid line holds the next spin to landing + UNLOCK_MS (the jackpot longer); a loss keeps the pace.
      breathEnds = Math.max(performance.now() + PACE.BREATH_MS, unlockAt);
      // C1 (10.16.D): the re-spin is the SECOND BEAT of this same press. The lever is never asked twice and
      // nothing is bought: the outcome is already on the tape, queued by the emi2 that just landed.
      if (!playsWithoutPress(tape.snapshot().nextKind)) break;
      mark('breath'); sync();
      await wait(PACE.BREATH_MS);
      if (my !== session || !alive) return;
      from = tape.snapshot();
      step = await tape.press();
      if (step.kind !== 'play') break;
      note('respin', { kind: step.outcome.kind });
    }
    busy = false; mark('idle'); sync(); armIdle();
    if (queued) press();
  }

  function onKey(e) {
    if (!alive) return;
    endAttract(); armIdle();   // A4: any key ends it, then the idle timer starts over
    if (e.key === 'Escape') { e.preventDefault(); back(); return; }
    if (e.target && e.target.closest && e.target.closest('button, summary, input, select')) return;
    if (e.code === 'Space') { e.preventDefault(); press(); }
    if (['1', '2', '3'].includes(e.key) && tape && tape.snapshot().canFreeze) toggleFreeze(Number(e.key) - 1);
  }
  const onResize = () => { if (scene) scene.resize(); paintRotate(); };

  async function open() {
    if (alive) return;
    alive = true; busy = false; suspended = false; lastMelt = null; pace = 'idle'; queued = false; breathEnds = 0;
    shown = null; streak = 0; sit = freshSit(); landPlan = null; pose = 'idle0_0'; playing = null; jarShown = null;
    flowLast = null; unlockAt = 0; tunnelLevel = 0; clearTimeout(hazeTimer); hazeTimer = 0;
    attracting = false; clearTimeout(idleTimer); clearTimeout(winkTimer); idleTimer = winkTimer = 0;
    const my = ++session;
    el = build();
    ctx.root.append(el);
    addEventListener('keydown', onKey); addEventListener('resize', onResize);
    tape = createTape({ request: (op, body, idem) => ctx.request(op, body, idem), onMelt: sendMelt });
    media = createMedia($('.slot-media'), ctx.lex);
    sound = createSound();
    callout = createCallout({ mount: $('.slot-callout'), lex: t });   // one per open; the layer sits above the reels
    bank = createBank({
      layer: $('.slot-tokens'), reduced,
      onTick: (value, kind, quiet) => { shown = value; paintSp(); if (!quiet) sound.token(false); note('tick', { kind, value }); },
      // `rolling` (playbook A3): the tokens are down but the readout is still counting, so the mini-thud
      // waits for the end of the rollup (Law X). The +N goes up on the landing and stays out the count.
      // 10.22.D THE GLOW: a warm cut on the SP chip as the last token lands, 480 ms, in fast and out slow.
      // It rides the mini-thud's own frame (Law X) and never a small win's - plan.glow is 0 while melted
      // (Brake 5) and under reduced motion (Law VI), and Calm keeps it, because a warm cut is not travel.
      onLand: (kind, rolling) => { if (kind === 'spend' && scene) scene.trayThud(); else if (!rolling) { sound.token(true); thudReadout();
                                     if (bankGlow > 0) { warmGlow(readout()); note('glow', { ms: bankGlow }); } }
                                   gain(bankTo - bankFrom, rolling ? bankHold : 1600); },
      onDone: () => { shown = null; paintSp(); },
    });
    if (typeof ctx.onSp === 'function') unSp = ctx.onSp(v => { if (tape) { tape.setServerSp(v); sync(); } });
    const dealt = Promise.resolve().then(() => (typeof ctx.media === 'function' ? ctx.media() : null))
      .then(m => (my === session ? media.deal(m) : null)).catch(() => null);
    const [made, state] = await Promise.all([
      createScene({ stage:ctx.stage, canvas: $('.slot-stage'), reduced, stillFx, variant: variant && variant.id, palette: variant && variant.palette, hint: $('.slot-hint'),
                    payline: $('.slot-payline'), topRow:$('.slot-top'), spinControl:$('.slot-spin'), freezeLabels:[...el.querySelectorAll('.slot-freeze button')], spinLabel:t('br_slot_spin','Spin'),
                    canPull: () => (!busy || pace === 'reveal') && !suspended,
                    onLever: () => press(), onFreeze: col => toggleFreeze(col),
                    onReelSpeed: (i, speed) => sound.roll(i, speed),
                    onReelStop: i => { const p = playing; sound.thud(i, !!p && i === p.lastReel && !(p.o.pay > 0));
                      // A5: EMI landed on this reel, so the cell wiggles after this thud and she glances. The next
                      // reel's thud is untouched (Law X); reduced motion takes the settled state, so no wiggle.
                      if (p && p.emi.includes(i) && !reduced) { scene.wiggle(i, PACE.THUD_MS); glanceTo('hearts', glanceHoldMs(meltedBy(p.o)), null); note('emi-wiggle', { reel: i }); }
                      note('thud', { reel: i }); if (p) stopFeel(p, i); } }).catch(e => ({ error: e })),
      tape.open(),
    ]);
    if (my !== session) { if (made && made.dispose) made.dispose(); return; }
    if (made.error) { console.error('[slot] cabinet failed to load', made.error); el.dataset.phase = 'closed'; card(refusalText('closed')); return; }
    if (made.missing.length) {
      made.dispose();
      el.dataset.phase = 'closed';
      card(t('br_slot_model_missing', 'Model missing {n}', { n: made.missing.join(', ') }));
      return;
    }
    if (!state.ok) { made.dispose(); el.dataset.phase = 'closed'; card(refusalText('closed')); return; }   // 3.4: no state, no fallback table
    scene = made;
    // The tape is back: from here the room reads what it owes live (until close hands it a number).
    if (hostSp) { owing = true; hostSp.owe(() => (tape ? tape.snapshot().owed : 0)); }
    const s = tape.snapshot();
    scene.setStrips(s.strips);
    scene.setStops(s.last && Array.isArray(s.last.stops) ? s.last.stops : stopsFor(s.strips, s.shown));
    scene.setLook({ gif: i => media.gif(i), word: i => media.word(i) });
    dealt.then(() => { if (my === session && scene) scene.setLook({ gif: i => media.gif(i), word: i => media.word(i) }); });
    renderOdds();
    setFace(!s.last ? restPose(s.melt) : landPose(s.last));
    el.dataset.phase = 'rise';
    sync();
    await scene.rise();
    if (my !== session) return;
    el.dataset.phase = 'play';
    sync(); paintRotate();
    // 10.16.C: EMI hands the comp over. No party, no REVEAL: a glance and one chime (Brake 1, arriving is not
    // an earned moment). The five spins themselves celebrate on their own merits.
    const offered = tape.snapshot().comp;
    if (offered) {
      sound.arm();
      sound.win('chime', 0);
      glanceTo('hearts', glanceHoldMs(s.melt > 0), restPose(s.melt));
      note('comp', { id: offered.id, spins: offered.spins });
    }
    armIdle();
  }

  async function close() {
    if (!alive) return;
    alive = false;
    const my = ++session;
    if (tape) {
      tape.abort();
      const cur = tape.cursor();
      if (cur) Promise.resolve(ctx.request('cursor', cur)).catch(() => {});
    }
    removeEventListener('keydown', onKey); removeEventListener('resize', onResize);
    if (payRaf) cancelAnimationFrame(payRaf);
    payRaf = 0; combos = [];
    if (typeof unSp === 'function') unSp();
    unSp = null;
    // Law VI: Back skips every ceremony to its settled state, then hands the room its chip back.
    endAttract(true);
    clearFlow(); tunnel(0);
    if (callout) callout.dispose();
    clearTimeout(glanceTimer); clearTimeout(gainTimer);
    if (bank) { bank.skip(); bank.dispose(); }
    // What a reopen will show: the stored tape's unplayed pays (a freeze's own outcomes are not stored).
    // Before the tape came back the room keeps what it had, so a quick Back never dips the chip either.
    if (hostSp && owing && tape) hostSp.owe(tape.snapshot().tapeOwed);
    if (hostSp) hostSp.set(null);
    owing = false;
    if (sound) sound.dispose();
    if (words) { words.dispose(); words = null; }   // Law VI: the word, the tunnel and the speech go with the cabinet
    const s = scene, root = el, m = media;
    if (s) s.skip();
    if (root) root.dataset.phase = 'leaving';
    if (s) await Promise.race([s.sink(), new Promise(r => setTimeout(r, 340))]);
    if (s) s.dispose();
    if (m) m.dispose();
    if (root) root.remove();
    if (my === session) { scene = null; el = null; tape = null; media = null; busy = false; bank = null; sound = null; shown = null; callout = null; }
  }

  return {
    open,
    close,
    suspend(on) {
      suspended = !!on;
      if (suspended) { endAttract(true); clearFlow(); tunnel(0); } else armIdle();   // Law VI: the word and the timers drop at once
      if (sound) sound.suspend(suspended);   // the climb is hushed with it: a resumed context would replay the rest
      if (suspended && bank) bank.skip();
      if (suspended && words) words.cancel();   // Law VI: the word, the tunnel and the speech drop at once
      if (suspended && scene) { scene.cancelPull(); scene.skip(); }
      payLoop();   // the legend's 12 Hz stops with everything else, and comes back with it
      if (el) sync();
    },
    async destroy() {
      await close();
      document.querySelectorAll('link[data-slot-css]').forEach(l => l.remove());
    },
    /** For dev.html and CDP checks only. */
    debug: () => ({ phase: el && el.dataset.phase, busy, alive, pace, marks, snapshot: tape && tape.snapshot(),
                  jar: { shown: jarShown, plan: playing ? playing.jar : null,
                         text: el && $('.slot-jar span') ? $('.slot-jar span').textContent : null,
                         fill: el && $('.slot-jar i') ? $('.slot-jar i').style.height : null,
                         on: !!(el && $('.slot-jar') && $('.slot-jar').dataset.on != null),
                         hidden: !!(el && $('.slot-jar') && $('.slot-jar').hidden) },
                  respin: playing ? !!playing.respin : false,
                  comp: { chip: !!(el && $('.slot-comp') && !$('.slot-comp').hidden),
                          label: el && $('.slot-spin small') ? $('.slot-spin small').textContent : null },
                    hostBack, variant: variant && variant.id, palette: !!(scene && scene.recoloured),
                    spinning: !!(scene && scene.spinning), sceneAlive: !!scene,
                    callout: callout ? callout.debug() : null, flow: flowLast, tunnel: tunnelLevel, haze: !!(scene && scene.hazing),
                    feel: { log: feelLog, pose, streak, seen: sit.seen, heroes: sit.heroes, plan: landPlan,
                            shown, readout: String(shownSp()), hostSp: !!hostSp,
                            attracting, idleArmed: !!idleTimer, emi: playing ? playing.emi : [],
                            cues: sound ? sound.trace.slice() : [], scene: scene && scene.debug() } }),
  };
}
