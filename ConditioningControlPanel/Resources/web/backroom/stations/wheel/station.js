/* ============================================================================
 * station.js - the Daily Daze wheel station (CONTRACT.md section 7). The room
 * calls mount(ctx) once, then open()/close() per visit. Back is live at every
 * frame (Law VI): it never waits on the glb, the server or an animation.
 *
 *   wheel.js    layout, landing, countdown, result reading, Law I (pure)
 *   feel.js     THE HOUSE BOOK for the wheel: tiers, fx, ladder, glance (pure)
 *   scene.js    the wheel glb close-up, one WebGL context per open()
 *   emi.js      EMI's face and poses on the perch
 *   readout.js  the one SP readout (ctx.spReadout when the room has it)
 *   bank.js / sound.js  THE BANK's tokens and the cues
 *
 * ONE FREE SPIN A DAY. The server draws; the page answers the drag or the
 * button inside 100 ms (the wheel starts turning), then retargets the
 * deceleration onto result.sliceIndex whatever the drag strength. A reopen
 * after spinning shows the day's stored landing and a countdown to nextResetAt.
 * ==========================================================================*/

import { layoutOf, landingAngle, resultIndex, readResult, restRotation, countdown } from './wheel.js';
import { recipe, tierOf, fxFor, usesGifs, winTokens, glance, landPose, pressPose, bezier, FEEL } from './feel.js';
import { createScene } from './scene.js';
import { createReadout } from './readout.js';
import { createBank } from './bank.js';
import { createSound } from './sound.js';

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
  const reduced = !!ctx.reduced || prefersReduced;
  const calm = String(ctx.intensity || '').toLowerCase() === 'calm';
  const still = reduced || calm;                  // Law VI: reduced motion and Calm land at the settled state
  const hostBack = ctx.hostBack === true;
  loadCss();

  let el = null, scene = null, readout = null, bank = null, sound = null, session = 0, alive = false, suspended = false;
  let st = null, layout = null, busy = false, pose = 'idle0_0', gifs = [], unSp = null, timer = 0, refreshAt = 0, lines = [], lineAt = 0;
  let gainTimer = 0, glanceTimer = 0, feelLog = [], revealAt = -Infinity;
  const $ = sel => el.querySelector(sel);
  const note = (what, extra = {}) => { feelLog = [...feelLog.slice(-79), { what, at: Math.round(performance.now()), ...extra }]; };

  function build() {
    const root = document.createElement('div');
    root.className = 'wheel-station'; root.dataset.phase = 'loading';
    root.innerHTML = `
      <div class="wheel-dim"></div>
      <canvas class="wheel-stage" aria-label="${t('br_wheel_stage', 'Daily Daze wheel. Drag the rim to spin.')}"></canvas>
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
    const row = layout && layout[resultIndex(layout, { sliceId: r.sliceId, sliceIndex: r.sliceIndex })];
    const name = row ? t(`br_wheel_slice_${row.id.replace(/_[a-z]$/, '')}`, row.label) : '';
    let s = r.jackpotWon ? t('br_wheel_jackpot_won', 'JACKPOT! +{n} SP.', { n: fmt(r.pay) })
      : r.snoozed ? t('br_wheel_snoozed', 'Snooze. +{n} SP tomorrow.', { n: fmt(carry) })
      : t('br_wheel_won', '{name}: +{n} SP.', { name, n: fmt(r.pay) });
    if (r.fallback) s += ' ' + t('br_wheel_fallback', 'The star slipped past, so Dazed pays instead.');
    if (r.carryPaid > 0) s += ' ' + t('br_wheel_carry_paid', '+{n} SP from Snooze.', { n: fmt(r.carryPaid) });
    if (r.capped) s += ' ' + t('br_wheel_capped', 'Your SP is at the cap.');
    return s;
  }
  function sync() {
    if (!el || !st) return;
    const c = clock(), r = st.spun ? readResult(st.result) : null, j = st.jackpot || {};
    $('.wheel-jackpot').textContent = t('br_wheel_jackpot', 'Jackpot {n} SP, {odds}', { n: fmt(j.amount), odds: String(j.odds || '') });
    const status = busy ? t('br_wheel_spinning', 'Round it goes...')
      : r ? `${resultLine(r, st.snoozeCarry)}\n${c ? t('br_wheel_next', 'Next spin in {time}.', { time: c.text }) : ''}`
      : t('br_wheel_ready', 'Your free spin is ready. Drag the rim or press Spin.');
    if ($('.wheel-status').textContent !== status) $('.wheel-status').textContent = status;
    const spin = $('.wheel-spin');
    spin.disabled = el.dataset.phase !== 'play' || busy || !!r;
    spin.querySelector('span').textContent = r ? t('br_wheel_come_back', 'Come back') : t('br_wheel_spin', 'Spin');
    spin.querySelector('small').textContent = r && c ? c.text : t('br_wheel_free', 'Free today');
    if (!scene) return;
    scene.screen('title_screen', t('br_wheel_title', 'DAILY DAZE'));
    const now = performance.now();
    const rq = (now - revealAt) / FEEL.REVEAL_MS, counting = r && r.jackpotWon && rq >= 0 && rq < 1;   // THE REVEAL counts the pot up
    const want = r ? [r.jackpotWon ? t('br_wheel_screen_jackpot', 'JACKPOT +{n}', { n: fmt(counting ? Math.round(r.pay * bezier(FEEL.REVEAL_EASE, rq)) : r.pay) }) : r.snoozed ? t('br_wheel_screen_snooze', 'SNOOZE +{n} TOMORROW', { n: fmt(st.snoozeCarry) }) : t('br_wheel_screen_win', '+{n} SP', { n: fmt(r.total) }),
                     c ? t('br_wheel_screen_next', 'NEXT {time}', { time: c.text }) : '']
      : [busy ? t('br_wheel_screen_spinning', 'ROUND IT GOES') : t('br_wheel_screen_ready', 'GIVE IT A SPIN'), t('br_wheel_screen_pot', 'JACKPOT {n}', { n: fmt(j.amount) })];
    if (want.join() !== lines.join()) { lines = want; }
    const which = Math.floor((now - lineAt) / 3200) % 2;   // two lines, one turn every 3.2 s (never a flicker)
    scene.screen('status_screen', lines[which] || lines[0], !!(r && r.jackpotWon));
  }
  function renderOdds() {
    const rows = layout.map(s => {
      const tr = document.createElement('tr');
      const name = t(`br_wheel_slice_${s.id.replace(/_[a-z]$/, '')}`, s.label);
      for (const [tag, text] of [['th', name], ['td', s.kind === 'malus' ? t('br_wheel_odds_snooze', '+{n} next spin', { n: 2 }) : `${fmt(s.pay)} SP`], ['td', s.odds]]) {
        const cell = document.createElement(tag); cell.textContent = text; tr.append(cell);
      }
      return tr;
    });
    $('.wheel-odds table').replaceChildren(...rows);
    const j = st.jackpot || {}, notes = [t('br_wheel_odds_note', 'One free spin a day. The slice sizes are the picture; these are the real odds.')];
    if (j.eligible === false) notes.push(t('br_wheel_young', 'The jackpot opens to accounts a few days old.'));
    else if (j.wonToday) notes.push(t('br_wheel_taken', "Today's jackpot is taken. It starts again tomorrow."));
    if (st.snoozeCarry > 0) notes.push(t('br_wheel_carry', 'Snooze carry: +{n} SP on your next spin.', { n: fmt(st.snoozeCarry) }));
    $('.wheel-odds p').textContent = notes.join(' ');
  }

  /* ---------------------------------------------------------------- feel */
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
  function fire(r) {
    if (suspended || typeof ctx.fx !== 'function') return;
    for (const fxId of fxFor(r)) {
      try { const p = ctx.fx(fxId, usesGifs(fxId) && gifs.length ? gifs : undefined); if (p && p.catch) p.catch(() => {}); note('fx', { fxId }); }
      catch (e) { console.warn('[wheel] fx failed', e); }
    }
  }
  /** The landing beat (Law X): THE THUD, the party the tier allows, the tokens and EMI, on one frame. */
  function land(r, gained, fresh) {
    const rec = recipe(r, { still }), tier = tierOf(r);
    lineAt = performance.now();   // the result line shows first
    if (fresh && rec.reveal) { revealAt = lineAt; const step = () => { if (!alive || performance.now() - revealAt > FEEL.REVEAL_MS + 40) return; sync(); requestAnimationFrame(step); }; requestAnimationFrame(step); }
    sound.thud(tier === 0);
    if (fresh) { scene.celebrate(rec); sound.win(rec.sound); fire(r); }
    glanceTo(landPose(r));
    $('.wheel-zzz').hidden = !r.snoozed;
    if (gained > 0 && fresh) {
      bank.start({ n: winTokens(tier, calm), fromValue: readout.server - gained, toValue: readout.server,
                   from: () => scene && scene.project('landed'), to: () => readout.target() });
    } else readout.settle();
    if (fresh) gain(r.snoozed ? t('br_wheel_gain_snooze', '+{n} tomorrow', { n: fmt(st.snoozeCarry) }) : t('br_wheel_gain', '+{n} SP', { n: fmt(gained) }));
    note('land', { slice: r.sliceId, pay: r.pay, total: r.total, gained, tier, party: rec.sound, fresh, still });
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
    if (st.spun) { glanceTo(pressPose(), landPose(readResult(st.result))); $('.wheel-spin').classList.add('is-ringing'); setTimeout(() => el && $('.wheel-spin').classList.remove('is-ringing'), 400); return; }
    // Law VIII: the wheel turns (or, still, the button rings) and EMI glances on this frame.
    busy = true; scene.coast(omega); scene.setMood('spin'); glanceTo(pressPose());
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
    const b = a.body, r = readResult(b.result), idx = resultIndex(layout, b.result);
    // Nothing on screen may tell the result before the pointer does: the state is adopted on the landing frame.
    const next = { ...st, spun: true, result: b.result, snoozeCarry: Number(b.snoozeCarry) || 0, nextResetAt: b.nextResetAt || st.nextResetAt, jackpot: b.jackpot || st.jackpot };
    const gained = a.kind === 'result' ? Math.max(0, Number(b.sp) - before) : 0;
    readout.owe(gained); readout.setServer(b.sp);                                // Law I: held back until it lands
    if (idx < 0) { await scene.windDown(); } else await scene.land(landingAngle(layout, idx, r.day), idx);
    if (my !== session || !alive) return;
    busy = false; st = next;
    land(r, gained, a.kind === 'result');
    if (a.kind === 'already') card(t('br_wheel_already', 'Already spun today. Here is where it landed.'));
    renderOdds(); sync();
  }

  function onKey(e) {
    if (!alive) return;
    if (e.key === 'Escape') { e.preventDefault(); back(); return; }
    if (e.target && e.target.closest && e.target.closest('summary, input, select')) return;
    if (e.code === 'Space' || e.key === 'Enter') { e.preventDefault(); press(); }   // the station owns Space and Enter while open
  }
  const onResize = () => scene && scene.resize();

  async function refresh(my) {
    const res = await Promise.resolve(ctx.request('state', {})).catch(() => null);
    if (my !== session || !res || !res.ok || !res.body || !res.body.ok || busy) return;
    st = res.body; readout.setServer(st.sp);
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
    alive = true; busy = false; suspended = false; pose = 'idle0_0'; feelLog = []; lines = []; lineAt = performance.now();
    const my = ++session;
    el = build(); ctx.root.append(el);
    addEventListener('keydown', onKey); addEventListener('resize', onResize);
    readout = createReadout({ ctx, own: $('.wheel-sp'), format: n => t('br_wheel_sp', '{n} SP', { n: fmt(n) }) });
    if (readout.kind !== 'own') el.dataset.hostSp = '';
    sound = createSound();
    bank = createBank({ layer: $('.wheel-tokens'), reduced: still,
      onTick: (v, quiet) => { readout.show(v); if (!quiet) sound.token(false); },
      onLand: () => { sound.token(true); readout.thud(still); },
      onDone: () => readout.settle() });
    if (typeof ctx.onSp === 'function') unSp = ctx.onSp(v => { if (readout) readout.setServer(v); });
    Promise.resolve().then(() => (typeof ctx.media === 'function' ? ctx.media() : null))
      .then(m => { if (my === session && m && Array.isArray(m.gifs)) gifs = m.gifs.map(g => String(g.key)).filter(Boolean); }).catch(() => {});
    const [made, res] = await Promise.all([
      createScene({ canvas: $('.wheel-stage'), hud: $('.wheel-face'), reduced: still,
        labels: s => ({ big: s.kind === 'jackpot' ? `★ ${fmt(s.pay)}` : s.kind === 'malus' ? 'Zz' : fmt(s.pay),
                        small: t(`br_wheel_slice_${s.id.replace(/_[a-z]$/, '')}`, s.label) }),
        canSpin: () => !busy && !suspended && !!st && !st.spun,
        onGrab: ok => { sound.arm(); if (!ok) press(); else glanceTo(pressPose()); },
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
    scene = made; scene.setLayout(layout); readout.setServer(st.sp); readout.settle();
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
    el.dataset.phase = 'play'; sync();
  }

  async function close() {
    if (!alive) return;
    alive = false;
    const my = ++session;
    removeEventListener('keydown', onKey); removeEventListener('resize', onResize);
    clearInterval(timer); clearTimeout(gainTimer); clearTimeout(glanceTimer);
    if (typeof unSp === 'function') unSp();
    unSp = null;
    // Law VI: Back skips every ceremony to its settled state and hands the readout the plain server number.
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
      suspended = !!on;
      if (sound) sound.suspend(suspended);
      if (suspended && bank) bank.skip();
      if (suspended && scene) scene.skip();
      if (el) sync();
    },
    async destroy() { await close(); document.querySelectorAll('link[data-wheel-css]').forEach(l => l.remove()); },
    /** For dev.html and CDP checks only. */
    debug: () => ({ phase: el && el.dataset.phase, alive, busy, still, hostBack, state: st, pose,
                    readout: readout && { kind: readout.kind, value: readout.value, server: readout.server, owed: readout.owed },
                    status: el && $('.wheel-status').textContent, spin: el && $('.wheel-spin').textContent,
                    feel: { log: feelLog, cues: sound ? sound.trace.slice() : [], scene: scene && scene.debug() } }),
  };
}
