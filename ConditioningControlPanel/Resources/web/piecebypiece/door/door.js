/* ============================================================================
 * door/door.js - the front door: menu, lobby, past games, profile, the end card.
 *
 * One glass card over the live board. The board underneath is the scenery and
 * keeps drifting (Law III); the card is the subject, and every screen is the
 * same card with different contents so the eye learns one place. Rules the
 * door keeps for you:
 *   - two clicks from the Play wall to a game: the strip, then one button
 *   - nothing is hostage: Esc always goes back, then out; a countdown is a
 *     tap away from "now"; a challenge you ignore ignores itself
 *   - ONE foreground BREATH per screen (the primary button's glow, or the
 *     waiting dot while you look for a game), everything else lands once
 *   - reduced motion takes the state and not the travel (.still)
 *
 *   createDoor({ bus, game, board, lobby, root, params, startGame, settings })
 *     -> { show(screen), hide(), isUp, debug, dispose }
 *
 * startGame({ mode, match }) is boot's: it deals the game and hides the door.
 * The door never touches the referee mid-game; it reads the record at the end.
 *
 * THE LOBBY IS LEFT WHEN THE GAME DEALS, not when the lobby screen goes. The
 * found screen keeps the lobby entered on purpose (the pairing has to land
 * through its poll), so the one place the door can stand up is go(): a lobby
 * still entered during a game keeps re-advertising a player who is mid-game,
 * and the server would pair a third player against a ghost.
 *
 * THE HOST NAMES THE PLAYER. Hosted, the account's display name is what the
 * server lists and what "you are visible as" has to say (boot.js copies it
 * into settings.playerName off pbp:identity), so the profile's name box is
 * read-only there. And a hosted page whose account is signed out cannot play
 * anybody: the lobby says so in one line, with quick match off, rather than
 * showing an empty room and a quick match that fails - and never the mock,
 * whose invented names a desktop player would try to join.
 *
 * OPEN TABLES. The lobby screen is a list you browse, not a room you stand in:
 * watching it (lobby.onTables) never lists the player. One tap on a row's
 * Join sits at that table; Host a table is the only thing that puts the
 * player on the list, and the list's own poll hears somebody sit down. A
 * poll repaints the list slot alone, keeping its scroll and focus, so a
 * three-second tick never throws the reader back to the top.
 *
 * PICTURES FIRST, ONCE. Every start (solo, two here, continue, quick match,
 * a table, a join, a challenge, a friend's game) goes through gate(): with
 * Distraction on and no picture choice saved, the shared picker asks first
 * (ui/pictures.js), and the pick starts the game. Saved, it is never asked
 * again; the menu's Pictures link opens the manager.
 * ==========================================================================*/

import { buildReplay, showReplayStep, resultLine } from './replay.js';
import { readSolo, soloOptions } from '../game/save.js';
import { LEVELS, LEVEL_ORDER } from '../game/search.js';
import { requestRematch } from '../net/rematch.js';
import { isHosted, identity, whenIdentity, postToHost, onHostMessage } from '../bridge.js';
import { createStake, pills as stakePills, stakeLabel, refusalText, sameStake, isNone } from '../net/stake.js';
import { listGames, getGame, saveGame, playerName, setPlayerName, profileStats, outcome, fmtDuration, fmtMoves, fmtWhen, finalIq } from './store.js';
import { recapHtml, iqFromApi, plainIq } from './fall.js';
import { pictureChoice } from '../ui/pictures.js';

/** Every number the door decides with. */
export const TUNING = Object.freeze({
  endCardDelayMs: 1400,     // the board's own end beat lands first (thud, status line), then the card
  countdownStepMs: 700,     // 3 2 1, CHIME LADDER
  askTimeoutMs: 8000,       // an ignored challenge ignores itself
  replayStepMs: 900,        // auto-play cadence in a replay
  iqSettleMs: 2500,         // the last move's IQ grade may still be in the worker at the end; wait this long for it
  menuSway: 0.3,            // camera drift while the door is up
});
const T = TUNING;

const PIECE_WORD = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };

/** The clocks a hosted table can carry. */
const HOST_TCS = Object.freeze([
  { label: '5+3', initial_ms: 300000, increment_ms: 3000 },
  { label: '10+0', initial_ms: 600000, increment_ms: 0 },
  { label: '15+10', initial_ms: 900000, increment_ms: 10000 },
]);

/** What a refused seat says, in one short line under the buttons. */
const SEAT_NOTES = Object.freeze({
  gone: 'that table just closed',
  blocked: 'that table is not open to you',
  self: 'that is your own table',
  left: 'nobody answered',
});

/** 600000 + 5000 -> "10+5". Minutes that are not whole read as m:ss. */
export function tcWord(tc) {
  if (!tc || !Number.isFinite(Number(tc.initial_ms))) return '';
  const sec = Math.round(Number(tc.initial_ms) / 1000);
  const min = sec % 60 ? `${Math.floor(sec / 60)}:${String(sec % 60).padStart(2, '0')}` : String(sec / 60);
  return `${min}+${Math.round((Number(tc.increment_ms) || 0) / 1000)}`;
}

function esc(s) { return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

function reducedMotion(settings) {
  try {
    if (settings && settings().reducedMotion) return true;
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  } catch { return false; }
}

export function createDoor(opts = {}) {
  const { bus, game, board, params } = opts;
  const root = opts.root || (typeof document !== 'undefined' ? document.getElementById('door') : null);
  // A hosted page never gets the mock (see the header). No lobby at all reads
  // as "not available", which is at least true.
  const lobby = (opts.lobby && !(isHosted && opts.lobby.debug && opts.lobby.debug.isMock)) ? opts.lobby : null;
  const settings = () => (opts.settings ? opts.settings() : ((typeof window !== 'undefined' && window.PBP && window.PBP.settings) || {}));
  const startGame = typeof opts.startGame === 'function' ? opts.startGame : () => {};
  if (!root) return { show() {}, hide() {}, isUp: () => false, debug: () => ({ ok: false, why: 'no #door' }), dispose() {} };

  const veil = document.createElement('div'); veil.className = 'door-veil';
  const card = document.createElement('div'); card.className = 'door-card'; card.setAttribute('role', 'dialog');
  root.append(veil, card);

  let screen = null;            // 'menu' | 'lobby' | 'found' | 'games' | 'profile' | 'replay' | 'end' | null (in game)
  let setup = soloOptions();
  let current = null;           // the game in progress: { mode, match, startedAt, captures }
  let lastEnd = null;           // the record of the last finished game, for the end card + rematch
  let looking = false;
  let ask = null;               // an incoming challenge { id, name, accept, decline, timer }
  let people = [];
  let playingNow = [];          // [{ white, black, timeControl, moves }], names only
  let lookKind = null;          // 'quick' | 'join' | 'host' | 'challenge' while looking
  let joiningId = null;         // the table a Join is sitting down at
  let note = '';                // one line after a refused seat
  let tableCount = null;        // for the menu's button, once asked
  let hostTc = HOST_TCS[1];
  const seenTables = new Set(); // rows already drawn once land without their entrance
  let replay = null;            // { moves, positions, marks, i, timer }
  const timers = new Set();
  const unbind = [];
  // PvP stakes (net/stake.js): online games only. Disabled, and drawn as nothing, until the
  // host says stakes are on for this account.
  const stake = opts.stake || createStake({ post: postToHost, onMessage: onHostMessage });
  let stakeChip = null;
  // Where Distraction's pictures come from (ui/pictures.js). Every start asks first while no
  // choice is saved, once (owner, 2026-09-30); after that the manager is the only way in.
  const pictures = opts.pictures || pictureChoice;
  /** Run a start, after the one-time picture ask if it is still owed. Backing out starts nothing. */
  function gate(start, backedOut = () => {}) {
    let owed = false;
    try { owed = pictures.needsChoice(); } catch { owed = false; }
    if (!owed) { start(); return; }
    Promise.resolve(pictures.ask({ still: still() })).then((go) => {
      if (go) start(); else backedOut();
    }).catch(() => start());
  }

  /**
   * Who the host says we are, behind one object so a harness can stand in for
   * the desktop (debug.host). Unhosted, whenIdentity resolves at once with the
   * empty identity and none of the account states below can happen.
   */
  let host = { isHosted, identity, whenIdentity };
  let signedOut = false;        // hosted, and there is no signed-in account behind the page

  /** Wait for the host's word, then remember whether there is anyone to play as. */
  async function askHost() {
    let id = null;
    try { id = await host.whenIdentity(); } catch { id = null; }
    if (!id) { try { id = host.identity(); } catch { id = null; } }
    signedOut = !!host.isHosted && !(id && id.online !== false && id.unifiedId);
    return id;
  }

  /** Nothing is asked of the lobby before the host has spoken, and nothing at all for nobody. */
  async function afterHost(fn) {
    await askHost();
    if (signedOut) throw new Error('cancelled');    // the quiet rejection: look() re-renders, no squelch
    return fn();
  }

  function later(fn, ms) { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); return t; }
  function hudEl() { return document.getElementById('hud'); }
  function camEl() { return document.getElementById('cam-views'); }
  function sfx(name) { try { if (board.sfx && board.sfx.play) board.sfx.play(name); } catch { /* quiet */ } }
  function still() { return reducedMotion(settings); }

  // ---------------------------------------------------------------- show / hide
  function show(name) {
    if (current && !game.isOver() && current.startedAt) {
      if (current.mode === 'online') return;
      toMenu();
      if (name === 'menu') return;
    }
    if (name === 'replay' && !replay) name = 'games';
    if (screen === 'replay' && name !== 'replay') closeReplay();
    if (['lobby', 'end'].includes(screen) && !['lobby', 'found', 'end'].includes(name)) leaveLobby();
    screen = name;
    if ((name === 'lobby' || (name === 'end' && lastEnd?.mode === 'online')) && !offList) enterLobby();
    board.drag?.suspend?.(true);
    root.hidden = false;
    root.className = 'door up screen-' + name + (still() ? ' still' : '') + (looking ? ' looking' : '');
    for (const el of [hudEl(), camEl()]) if (el) el.classList.add('parked');
    try { board.setCameraSway(name === 'replay' ? 0 : T.menuSway); } catch { /* no rig */ }
    render();
    paintStake();
  }

  function hide() {
    if (screen === 'lobby' || screen === 'end') leaveLobby();
    if (screen === 'replay') closeReplay();
    screen = null;
    root.hidden = true;
    board.drag?.suspend?.(false);
    root.className = 'door';
    for (const el of [hudEl(), camEl()]) if (el) el.classList.remove('parked');
    try { board.setCameraSway(0); } catch { /* no rig */ }
    paintStake();
  }

  function render() {
    const kept = card.querySelector('.tables-scroll');
    const top = kept ? kept.scrollTop : 0;
    card.innerHTML = '';
    const body = document.createElement('div'); body.className = 'door-body';
    body.innerHTML = ({ menu, lobby: lobbyScreen, found, games, profile, replay: replayScreen, end }[screen] || menu)();
    card.append(body);
    const sc = body.querySelector('.tables-scroll'); if (sc && top) sc.scrollTop = top;
    if (screen === 'menu') countTables();
    if (screen === 'profile') { const inp = body.querySelector('input'); if (inp && !inp.readOnly) inp.addEventListener('change', () => setPlayerName(inp.value)); }
    if (screen === 'replay') { const r = body.querySelector('.door-scrub'); if (r) r.addEventListener('input', () => stepReplay(Number(r.value))); }
    for (const input of body.querySelectorAll('[data-setup]')) input.addEventListener('change', () => {
      setup = soloOptions({ ...setup, [input.dataset.setup]: input.value });
      paintLevel();
    });
    const focus = body.querySelector('.door-btn.primary') || body.querySelector('button');
    if (focus && !still()) later(() => { try { focus.focus({ preventScroll: true }); } catch { /* fine */ } }, 60);
  }

  /** The solo button and the level row say the same level; a pick repaints both in place. */
  const levelWord = () => LEVELS[setup.level].label.toLowerCase() + ' computer';
  function levelHtml() {
    return `<div class="level-row" role="group" aria-label="Computer strength">${LEVEL_ORDER.map(id => `<button type="button" class="level-pill${id === setup.level ? ' on' : ''}" data-act="level" data-id="${id}" aria-pressed="${id === setup.level}" title="${esc(LEVELS[id].blurb)}">${esc(LEVELS[id].label)}</button>`).join('')}</div>`
      + `<p class="level-blurb">${esc(LEVELS[setup.level].blurb)}</p>`;
  }
  function paintLevel() {
    const label = card.querySelector('[data-act=solo] .k'); if (label) label.textContent = levelWord();
    const slot = card.querySelector('.level-slot'); if (slot) slot.innerHTML = levelHtml();
  }

  // ---------------------------------------------------------------- stakes
  /** The pill row: Off, 15 min, 30 min (only while the lock could take it), 5, 10, 25 sparkles. */
  function stakeHtml() {
    const s = stake.state;
    if (!s.enabled) return '';
    const L = s.labels;
    const lock = !!s.locked;
    const row = stakePills(s.options, s.timeOk).map((p) => `<button type="button" class="stake-pill${sameStake(p, s.pick) ? ' on' : ''}" data-act="stake" data-id="${esc(p.kind + ':' + p.amount)}"${(lock || s.busy) ? ' disabled' : ''}>${esc(stakeLabel(p, L))}</button>`).join('');
    const them = (s.match && s.them && !isNone(s.them)) ? `<span class="stake-chip">${esc(L.them)} <b>${esc(stakeLabel(s.them, L))}</b></span>` : '';
    const note = s.refusal ? esc(refusalText(s.refusal, L)) : (lock ? esc(L.locked) : '');
    return `<div class="stake-row" role="group" aria-label="${esc(L.title)}"><span class="stake-k">${esc(L.title)}</span>${row}</div>`
      + ((them || note) ? `<div class="stake-meta">${them}${note ? `<span class="stake-note">${note}</span>` : ''}</div>` : '');
  }

  /** Stakes changed: repaint only the stake bits (a full render would restart the count). */
  function paintStake() {
    const slot = card.querySelector('.stake-slot');
    if (slot) slot.innerHTML = stakeHtml();
    const res = card.querySelector('.stake-result');
    if (res) res.textContent = stake.resultLine() || '';
    // over the board during the game: you and them, until it is over
    const s = stake.state;
    const show = screen === null && s.enabled && s.match && !s.ended && (!isNone(s.you) || !isNone(s.them));
    if (!show) { if (stakeChip) stakeChip.hidden = true; return; }
    if (!stakeChip) { stakeChip = document.createElement('div'); stakeChip.className = 'stake-live'; document.body.append(stakeChip); }
    const L = s.labels;
    const part = (who, st) => isNone(st) ? '' : `<span>${esc(who)} <b>${esc(stakeLabel(st, L))}</b></span>`;
    stakeChip.innerHTML = part(L.you, s.you) + part(L.them, s.them) + (s.locked ? `<span class="lock">${esc(L.locked)}</span>` : '');
    stakeChip.hidden = false;
  }
  unbind.push(stake.subscribe(paintStake));

  // ---------------------------------------------------------------- screens
  const me = () => playerName(settings());

  function menu() {
    const saved = readSolo();
    const option = (value, text, selected) => `<option value="${value}" ${value === selected ? 'selected' : ''}>${text}</option>`;
    return `
      <h1 class="door-title"><b>piece by piece</b></h1>
      <p class="door-h">Your next move.</p>
      ${saved ? `<button type="button" class="door-btn primary" data-act="continue">Continue game <span class="k">${esc(fmtMoves(saved.moves.length))}</span></button>` : ''}
      <button type="button" class="door-btn ${saved ? '' : 'primary'}" data-act="solo">Play solo <span class="k">${esc(levelWord())}</span></button>
      <div class="level-slot">${levelHtml()}</div>
      <button type="button" class="door-btn" data-act="lobby">Open tables <span class="k tables-count">${esc(countWord())}</span></button>
      <button type="button" class="door-btn" data-act="hotseat">Two players here <span class="k">one board</span></button>
      <details class="door-setup"><summary>Solo setup</summary><div class="door-settings">
        <label>Your pieces<select data-setup="side">${option('w', 'White', setup.side)}${option('b', 'Black', setup.side)}${option('random', 'Surprise me', setup.side)}</select></label>
        <label>Clock<select data-setup="clockMs">${option('0', 'Untimed', String(setup.clockMs))}${option('300000', '5 minutes each', String(setup.clockMs))}${option('900000', '15 minutes each', String(setup.clockMs))}</select></label>
        ${saved ? '<p class="door-sub">A new solo game replaces your unfinished one.</p>' : ''}
      </div></details>
      <div class="door-links">
        <button type="button" class="door-link" data-act="games">Past games</button>
        <button type="button" class="door-link" data-act="profile">Profile</button>
        ${pictures.available() ? '<button type="button" class="door-link" data-act="pictures">Pictures</button>' : ''}
      </div>
      <div class="door-foot"><span>esc leaves the board</span><span class="name">at the board as <b>${esc(me())}</b></span></div>`;
  }

  const countWord = () => (tableCount ? `${tableCount} open` : 'online');

  /** The menu's button says how many tables are open: one read, never a listing. */
  function countTables() {
    if (!lobby || typeof lobby.tables !== 'function') return;
    askHost().then(() => (signedOut ? null : lobby.tables())).then((t) => {
      if (!t) return;
      tableCount = (t.open || []).length;
      const el = screen === 'menu' && card.querySelector('.tables-count');
      if (el) el.textContent = countWord();
    }).catch(() => {});
  }

  function lobbyScreen() {
    // Three rooms: the one with tables in it, the one with nobody to play as
    // (hosted, signed out), and the one with no lobby behind it at all. The
    // last two are one line each and every button is off, never a dead one.
    const canPlay = !!lobby && !signedOut;
    const hosting = looking && lookKind === 'host';
    const askRow = ask ? `
      <div class="door-ask"><span><b>${esc(ask.name)}</b> wants a game</span>
        <button type="button" class="door-pill" data-act="accept">play</button>
        <button type="button" class="door-link" data-act="decline">ignore</button></div>` : '';
    const dot = '<span class="door-dot"></span>';
    const quickLabel = lookKind === 'quick' ? `looking for a game ${dot}`
      : lookKind === 'challenge' ? `waiting for an answer ${dot}` : 'quick match';
    const hostLabel = hosting ? `at your table ${dot}` : `host a table <span class="k">${esc(hostTc.label)}</span>`;
    const clocks = (canPlay && !looking) ? `<div class="level-row tc-row" role="group" aria-label="Your table's clock">${HOST_TCS.map((t) => `<button type="button" class="level-pill${t === hostTc ? ' on' : ''}" data-act="hosttc" data-id="${esc(t.label)}" aria-pressed="${t === hostTc}">${esc(t.label)}</button>`).join('')}</div>` : '';
    const stop = hosting ? 'leave the table' : lookKind === 'join' ? 'stand up' : 'stop looking';
    return `
      <div class="tables-panel">
        <div class="tables-side">
          <h1 class="door-title">open tables</h1>
          <p class="door-sub tables-sub">${subHtml()}</p>
          ${canPlay ? '<div class="stake-slot">' + stakeHtml() + '</div>' : ''}
          ${askRow}
          <div class="door-row tables-actions">
            <button type="button" class="door-btn primary" data-act="quick" title="whoever waited longest" ${((looking && !hosting) || !canPlay) ? 'disabled' : ''}>${quickLabel}</button>
            <button type="button" class="door-btn host-btn${hosting ? ' on' : ''}" data-act="host" ${(looking || !canPlay || typeof lobby.host !== 'function') ? 'disabled' : ''}>${hostLabel}</button>
          </div>
          ${clocks}
          <p class="tables-note" role="status">${esc(note)}</p>
        </div>
        <div class="tables-slot">${tablesHtml()}</div>
      </div>
      <div class="door-foot"><span>esc - back</span>${looking ? `<button type="button" class="door-link" data-act="cancel">${stop}</button>` : ''}</div>`;
  }

  function subHtml() {
    if (signedOut) return 'not signed in';
    if (!lobby) return 'online play is not available right now';
    if (looking && lookKind === 'host') return `your table is open as <b>${esc(me())}</b>`;
    return `<b>${people.length}</b> open &middot; <b>${playingNow.length}</b> playing now`;
  }

  /** The scroll region: the open tables, then the games on right now. */
  function tablesHtml() {
    if (signedOut) return '<div class="door-empty">sign in to see open tables</div>';
    if (!lobby) return '<div class="door-empty">the board here still works - play here from the menu</div>';
    const hosting = looking && lookKind === 'host';
    const mine = hosting ? `
      <li class="table-row mine"><span class="table-name">${esc(me())}</span>
        <span class="table-meta"><b>${esc(hostTc.label)}</b><span>your table</span></span><span class="door-dot"></span></li>` : '';
    const rows = people.map((p, i) => {
      const fresh = !seenTables.has(p.id);
      const sitting = lookKind === 'join' && joiningId === p.id;
      const meta = [tcWord(p.timeControl) ? `<b>${esc(tcWord(p.timeControl))}</b>` : '', `<span>${esc(waitWord(p.waitingSince))}</span>`,
        (p.rating !== null && p.rating !== undefined && Number.isFinite(Number(p.rating))) ? `<span class="rating">${esc(Math.round(Number(p.rating)))}</span>` : ''].join('');
      return `
      <li class="table-row${fresh ? ' fresh' : ''}${sitting ? ' sitting' : ''}" style="--i:${Math.min(i, 8)}" data-id="${esc(p.id)}">
        <span class="table-name">${esc(p.name)}</span>
        <span class="table-meta">${meta}</span>
        <button type="button" class="table-join" data-act="join" data-id="${esc(p.id)}" aria-label="join ${esc(p.name)}" ${(looking && !hosting) ? 'disabled' : ''}>${sitting ? 'sitting <span class="door-dot"></span>' : 'join'}</button>
      </li>`;
    }).join('');
    for (const p of people) seenTables.add(p.id);
    const open = (mine || rows) ? `<ul class="tables-list" aria-label="Open tables">${mine}${rows}</ul>`
      : '<div class="door-empty tables-empty">no open tables. host one</div>';
    const games = playingNow.map((g) => `
      <li class="playing-row"><span class="who">${esc(g.white)} <i>vs</i> ${esc(g.black)}</span>
        <span class="meta">${esc([tcWord(g.timeControl), fmtMoves(g.moves)].filter(Boolean).join(' - '))}</span></li>`).join('');
    return `<div class="tables-scroll" tabindex="0" aria-label="Open tables and games on now">${open}
      <h2 class="tables-h">playing now</h2>
      ${games ? `<ul class="playing-list">${games}</ul>` : '<p class="tables-none">no games on right now</p>'}</div>`;
  }

  /**
   * A poll landed: repaint the list slot and the count line only, keeping the
   * scroll and the focused Join where they were.
   */
  function paintTables() {
    if (screen !== 'lobby') return;
    const slot = card.querySelector('.tables-slot');
    if (!slot) { render(); return; }
    const sc = slot.querySelector('.tables-scroll');
    const top = sc ? sc.scrollTop : 0;
    const a = document.activeElement;
    const inSlot = !!(a && slot.contains(a));
    const focusId = inSlot && a.dataset ? a.dataset.id : null;
    slot.innerHTML = tablesHtml();
    const sc2 = slot.querySelector('.tables-scroll');
    if (sc2) sc2.scrollTop = top;
    if (inSlot) {
      const back = (focusId && slot.querySelector(`[data-act=join][data-id="${CSS.escape(focusId)}"]`)) || sc2;
      try { back && back.focus({ preventScroll: true }); } catch { /* fine */ }
    }
    const sub = card.querySelector('.tables-sub');
    if (sub) sub.innerHTML = subHtml();
  }

  function found() {
    const m = current && current.match;
    const side = m ? m.side : 'w';
    return `
      <div class="door-found">
        <p class="line">matched with ${esc(m ? m.opponent.name : '')}</p>
        <p class="side">you play <b class="${side}">${side === 'w' ? 'white' : 'black'}</b></p>
        <div class="stake-slot">${stakeHtml()}</div>
        <div class="door-count" id="door-count"></div>
        <button type="button" class="door-link skip" data-act="go">tap to start now</button>
      </div>`;
  }

  function games() {
    const list = listGames();
    const rows = list.map((g) => {
      const o = outcome(g);
      const word = o ? o : (g.result ? (g.result.winner ? (g.result.winner === 'w' ? 'white won' : 'black won') : 'draw') : 'unfinished');
      const iq = finalIq(g);
      return `
      <li class="door-item">
        <span class="who">${esc(g.opponent || 'a friend here')}<br><span class="meta">${esc(fmtWhen(g.at))}</span></span>
        <span class="meta ${o || ''}">${esc(word)} &middot; ${esc(fmtMoves(g.plies))}${iq !== null ? ` &middot; iq ${iq}` : ''}</span>
        <button type="button" class="door-pill" data-act="watch" data-id="${esc(g.id)}">watch</button>
      </li>`;
    }).join('');
    return `
      <h1 class="door-title">past games</h1>
      ${list.length ? `<ul class="door-list">${rows}</ul>` : '<div class="door-empty">no games on the shelf yet - the first one you finish lands here</div>'}
      <div class="door-foot"><span>esc - back</span><span>${list.length} kept</span></div>`;
  }

  function profile() {
    const s = profileStats();
    const stat = (n, l, pink) => `<div class="door-stat"><span class="n${pink ? ' pink' : ''}">${n}</span><span class="l">${l}</span></div>`;
    // Hosted, the name is the account's: the server lists it, so it is shown
    // here and not typed. Unhosted (a plain browser), typed and kept locally.
    const hosted = !!host.isHosted;
    return `
      <h1 class="door-title">profile</h1>
      <div class="door-name"><input type="text" maxlength="24" value="${esc(me())}" aria-label="your name at the board" spellcheck="false"${hosted ? ' readonly' : ''}></div>
      ${hosted ? '<p class="door-sub">your name comes from your account</p>' : ''}
      <div class="door-stats">
        ${stat(s.games, 'games')}${stat(s.wins, 'wins', true)}${stat(s.losses, 'losses')}
        ${stat(s.draws, 'draws')}${stat(s.streak, 'streak', s.streak > 1)}${stat(s.captures, 'taken')}
        ${stat(s.favourite || '-', 'favourite')}${stat(fmtDuration(s.ms), 'at the board')}${stat(fmtMoves(s.plies), 'played')}
        ${s.lowIq !== null && s.lowIq !== undefined ? `<div class="door-stat wide"><span class="n pink">${s.lowIq}</span><span class="l">lowest iq</span></div>` : ''}
      </div>
      <div class="door-foot"><span>esc - back</span><span>history on this device</span></div>`;
  }

  function replayScreen() {
    const r = replay;
    const n = r.i;
    const san = n === 0 ? 'start' : `<i>${Math.ceil(n / 2)}${n % 2 ? '.' : '...'}</i>${esc(r.moves[n - 1])}`;
    return `
      <div class="door-strip">
        <button type="button" class="door-btn door-ico" data-act="rstart" title="to the start" aria-label="to the start">&#9198;</button>
        <button type="button" class="door-btn door-ico" data-act="rprev" title="back one" aria-label="back one">&#9664;</button>
        <span class="san">${san}</span>
        <button type="button" class="door-btn door-ico" data-act="rnext" title="on one" aria-label="on one">&#9654;</button>
        <button type="button" class="door-btn door-ico ${r.timer ? 'primary' : ''}" data-act="rplay" title="play" aria-label="play">${r.timer ? '&#10074;&#10074;' : '&#9654;&#9654;'}</button>
      </div>
      <input class="door-scrub" type="range" min="0" max="${r.moves.length}" value="${n}" aria-label="move">
      <div class="door-foot"><span>esc - back to the shelf</span><span>${esc(r.title)}</span></div>`;
  }

  function end() {
    const g = lastEnd || {};
    const o = outcome(g);
    const line = resultLine(g);
    return `
      <div class="door-end">
        <p class="result ${o || ''}">${esc(line)}</p>
        <p class="tally">${esc(fmtMoves(g.plies))} &middot; ${esc(fmtDuration(g.durationMs))}</p>
        ${g.mode === 'online' ? '<p class="stake-result">' + esc(stake.resultLine() || '') + '</p>' : ''}
        <div id="door-recap">${recap()}</div>
      </div>
      ${ask ? `<div class="door-ask"><span><b>${esc(ask.name)}</b> wants a rematch</span><button type="button" class="door-pill" data-act="accept">Play</button><button type="button" class="door-link" data-act="decline">Decline</button></div>` : ''}
      <button type="button" class="door-btn primary" data-act="rematch">Rematch</button>
      <button type="button" class="door-btn" data-act="watch" data-id="${esc(g.id)}">Review game</button>
      <button type="button" class="door-link" data-act="menu">Menu</button>`;
  }

  function waitWord(since) {
    const s = Math.max(0, Math.round((Date.now() - (since || Date.now())) / 1000));
    if (s < 60) return 'just sat down';
    return 'waiting ' + Math.floor(s / 60) + ' min';
  }

  // ---------------------------------------------------------------- lobby
  let offList = null, offAsk = null;
  async function enterLobby() {
    askHost().then(() => { if (host.isHosted && !signedOut) stake.limits(); }).catch(() => {});
    // The host first: a signed-out account has nothing to enter with, and
    // the screen has to say so rather than sit on an empty list.
    await askHost();
    if (!['lobby', 'end'].includes(screen)) return;
    if (signedOut || !lobby) { if (screen === 'lobby') render(); return; }
    if (offList) offList();
    const tables = (t) => { people = (t && t.open) || []; playingNow = (t && t.playing) || []; tableCount = people.length; paintTables(); };
    if (typeof lobby.onTables === 'function') {
      // browse: the list is read, the player is not put on it
      offList = lobby.onTables(tables);
    } else {
      // an older lobby with no tables: stand in it, as before
      try { await lobby.enter({ name: me() }); } catch { /* the door still opens */ }
      if (!['lobby', 'end'].includes(screen)) { lobby.leave(); return; }
      offList = lobby.onList((l) => { people = l || []; paintTables(); });
    }
    if (offAsk) offAsk();
    offAsk = lobby.onChallenge((offer) => {
      if (!['lobby', 'end'].includes(screen) || ask) { try { offer.decline(); } catch { /* fine */ } return; }
      ask = offer;
      sfx('pop');
      ask.timer = later(() => { if (ask === offer) { try { offer.decline(); } catch { /* fine */ } ask = null; if (['lobby', 'end'].includes(screen)) render(); } }, T.askTimeoutMs);
      render();
    });
    try {
      if (typeof lobby.tables === 'function') tables(await lobby.tables());
      else { people = await lobby.list(); paintTables(); }
    } catch { /* the poll fills it in */ }
  }
  function leaveLobby() {
    looking = false;
    lookKind = null;
    joiningId = null;
    note = '';
    if (ask) { try { ask.decline(); } catch { /* fine */ } ask = null; }
    if (offList) { offList(); offList = null; }
    if (offAsk) { offAsk(); offAsk = null; }
    try { if (lobby) lobby.leave(); } catch { /* fine */ }
  }

  let lookSeq = 0;
  async function look(promise, kind = 'challenge', id = null) {
    // A new look may start while one is out (Join from your own table): the old
    // one rejects 'cancelled' later, and must not clear the new one's state.
    const mine = ++lookSeq;
    looking = true;
    lookKind = kind;
    joiningId = id;
    note = '';
    root.classList.add('looking');
    if (screen === 'lobby') render();
    const done = () => { looking = false; lookKind = null; joiningId = null; root.classList.remove('looking'); };
    try {
      const match = await promise;
      if (mine !== lookSeq) return;
      done();
      matched(match);
    } catch (err) {
      if (mine !== lookSeq) return;
      done();
      const why = err && err.message;
      note = SEAT_NOTES[why] && why !== 'left' ? SEAT_NOTES[why] : '';
      if (screen === 'lobby') render();
      if (SEAT_NOTES[why]) sfx('squelch');
    }
  }

  function matched(match) {
    current = { mode: 'online', match, startedAt: 0, captures: { w: 0, b: 0 } };
    if (match && match.id) stake.begin(match.id);
    sfx('promote');
    show('found');
    countdown();
  }

  let countTimer = null;
  function countdown() {
    // with stakes on, two more beats: long enough to read what the other side put down
    let n = stake.state.enabled ? 5 : 3;
    const el = () => card.querySelector('#door-count');
    const tick = () => {
      const c = el();
      if (!c || screen !== 'found') return;
      c.textContent = String(n);
      c.classList.remove('tick'); void c.offsetWidth; c.classList.add('tick');
      sfx('tick');
      if (n === 0) { go(); return; }
      n--;
      countTimer = later(tick, T.countdownStepMs);
    };
    // "go" lands on 0: three ticks and the board
    tick();
  }
  function go() {
    if (!current) return;
    if (countTimer) { timers.delete(countTimer); clearTimeout(countTimer); countTimer = null; }
    const m = current.match;
    // An accepted challenge is a round trip on a server, and the lobby handed
    // the door its Match before the answer landed (net/lobbyServer.js,
    // offer()): the id is filled in behind it, on `ready`. Dealing a match
    // with no id throws in startOnlineMatch, so the count holds at zero until
    // the id is there - or until it is not, which is the lobby's way of
    // saying he is not playing after all.
    if (current.mode === 'online' && m && !m.id && m.ready && typeof m.ready.then === 'function') {
      const mine = current;
      m.ready.then((r) => {
        if (current !== mine) return;
        if (r && r.id) { go(); return; }
        current = null;
        sfx('squelch');
        show('lobby');
      }).catch(() => { if (current === mine) { current = null; show('lobby'); } });
      return;
    }
    // The lobby was kept through the found screen so the pairing could land;
    // the game has, so stand up (see the header). The next visit to the lobby
    // screen enters again, because leaveLobby drops the subscription show()
    // keys the re-entry on.
    leaveLobby();
    if (screen === 'found') { screen = null; }
    deal(current.mode, m);
  }

  // ---------------------------------------------------------------- the game itself
  function deal(mode, match = null, restore = null) {
    current = { mode, match, options: restore?.options || setup, startedAt: Date.now() - (restore?.durationMs || 0), captures: { w: 0, b: 0 } };
    if (restore) for (const [i, san] of restore.moves.entries()) if (san.includes('x')) current.captures[i % 2 ? 'b' : 'w']++;
    if (mode === 'online' && match && match.id) { stake.begin(match.id); stake.play?.(); }
    hide();
    startGame({ mode, match, options: setup, restore });
  }

  function onCapture(p) { if (current && p && p.by) current.captures[p.by] = (current.captures[p.by] || 0) + 1; }

  function onGameOver() {
    if (!current) return;
    let rec = {};
    try { rec = game.record ? game.record() : {}; } catch { rec = {}; }
    const m = current.match;
    const history = game.rules.chess.history({ verbose: true });
    const captures = { w: 0, b: 0 };
    for (const move of history) if (move.captured) captures[move.color]++;
    // the seat off the record when the driver knows it (an online seat may have
    // been corrected by the server after the deal), else the lobby's word
    const seat = (rec.me === 'w' || rec.me === 'b') ? rec.me : (m ? m.side : null);
    lastEnd = saveGame({
      mode: current.mode, me: seat, opponent: rec.opponent || (m ? m.opponent.name : 'a friend here'),
      moves: rec.moves || [], plies: rec.plies || 0, result: rec.result || null, fen: rec.fen || history[0]?.before,
      durationMs: rec.durationMs ?? (Date.now() - current.startedAt), captures, clocks: rec.clocks || null,
      iq: plainIq(rec.iq),   // the fall so far; settleIq() brings in the last grade
    });
    settleIq(lastEnd, rec.iq);
    if (current.mode === 'online' && m && m.id) stake.end(m.id);
    const finished = current;
    current = null;
    // the board's own end beat first, then the card
    later(() => { if (!current && screen === null) { lastEnd.rematchOf = finished; show('end'); } }, still() ? 0 : T.endCardDelayMs);
  }

  // ---------------------------------------------------------------- the fall (IQ recap)
  /** The end card's IQ recap (door/fall.js): your own seat only, nothing for a game with no grades. */
  function recap() { return lastEnd ? recapHtml(lastEnd) : ''; }
  function paintRecap() {
    const slot = screen === 'end' && card.querySelector('#door-recap');
    if (slot) slot.innerHTML = recap();
  }
  /**
   * The grader (window.PBP.iq) works in a worker, so the last move's grade can
   * land after the game does. Wait for it (bounded), then keep the fall on the
   * saved game and paint it into the card in place. A game dealt meanwhile owns
   * the grader by then, so only what the record already carried is kept.
   */
  function settleIq(saved, fromRecord) {
    const api = window.PBP && window.PBP.iq;
    let settled = null;
    try { settled = api && typeof api.settled === 'function' ? api.settled(T.iqSettleMs) : null; } catch { settled = null; }
    Promise.race([Promise.resolve(settled), new Promise((r) => later(r, T.iqSettleMs + 500))]).catch(() => {}).then(() => {
      const fresh = lastEnd === saved && !current;
      const iq = (fresh && iqFromApi(window.PBP && window.PBP.iq)) || plainIq(fromRecord);
      if (!iq) return;
      saved.iq = iq;
      const { rematchOf, ...plain } = saved;   // the finished match object is not shelf material
      try { saveGame(plain); } catch { /* the shelf is optional */ }
      if (lastEnd === saved) paintRecap();
    });
  }

  function rematch() {
    const prev = lastEnd && lastEnd.rematchOf;
    if (prev && prev.mode === 'online' && prev.match) {
      show('lobby');
      if (lobby) look(afterHost(() => requestRematch(lobby, prev.match)));
    } else if (prev?.mode === 'solo') {
      setup = prev.options || setup;
      deal('solo');
    } else {
      deal('hotseat');
    }
  }

  function toMenu() {
    if (current?.mode === 'online' && !game.isOver()) return;
    current = null;
    stake.clear();
    board.anim?.skip?.();
    game.clock?.stop?.();
    // A finished online seat stays in the chair for the end card, so the
    // board under the card is the finished position. The menu wants the
    // hotseat's fresh one, and that only exists once the switch has gone
    // back; reset() on the online seat is a no-op and would leave the mate.
    try { if (typeof game.switchBack === 'function') game.switchBack(); } catch { /* one driver only */ }
    try { game.reset(); board.pieces.setPosition(game.rules.position()); } catch { /* the board stays as it is */ }
    try { board.setSide('w', true); } catch { /* no rig */ }
    show('menu');
  }

  // ---------------------------------------------------------------- replay
  function openReplay(id) {
    if (current?.mode === 'online' && !game.isOver()) return;
    const g = getGame(id);
    if (!g) return;
    if (current) toMenu();
    leaveLobby();
    try { replay = buildReplay(g); } catch { return; }
    replay.title = `${g.opponent || 'a friend here'} - ${fmtWhen(g.at)}`;
    board.anim?.setClock?.(null);
    show('replay');
    stepReplay(0);
  }
  function stepReplay(i, animate = false) {
    if (!replay) return;
    showReplayStep(board, replay, i, animate && !still());
    if (replay.i === replay.moves.length && replay.timer) stopReplay();
    if (screen === 'replay') render();
  }
  function playReplay() {
    if (!replay || replay.timer) return;
    if (replay.i >= replay.moves.length) stepReplay(0);
    const tickFn = () => {
      if (!replay || !replay.timer) return;
      if (board.anim?.busy?.()) { replay.timer = later(tickFn, 150); return; }
      stepReplay(replay.i + 1, true);
      if (replay?.timer) replay.timer = later(tickFn, T.replayStepMs);
    };
    replay.timer = later(tickFn, 200);
    render();
  }
  function stopReplay() { if (replay && replay.timer) { clearTimeout(replay.timer); timers.delete(replay.timer); replay.timer = null; } }
  function closeReplay() {
    stopReplay();
    replay = null;
    board.anim?.setClock?.(() => game.clock);
    board.anim?.skip?.();
    game.switchBack?.();
    try { const m = board.drag && board.drag.markers; if (m && m.setLastMove) m.setLastMove(null, null); } catch { /* fine */ }
    try { game.reset(); board.pieces.setPosition(game.rules.position()); board.setSide('w', true); } catch { /* fine */ }
  }

  // ---------------------------------------------------------------- input
  function act(name, id) {
    switch (name) {
      // the lobby is asked only once the host has said who we are, and not at
      // all for nobody (afterHost); look() reads the quiet rejection as a re-render
      // every start below goes through gate(): the picture ask, once, while nothing is saved
      case 'quick': gate(() => { if (screen !== 'lobby') show('lobby'); if (lobby) look(afterHost(() => lobby.quickMatch()), 'quick'); }); break;
      case 'join': if (lobby && id) gate(() => look(afterHost(() => (typeof lobby.join === 'function' ? lobby.join(id) : lobby.challenge(id))), 'join', id)); break;
      case 'host': if (lobby && typeof lobby.host === 'function') gate(() => look(afterHost(() => lobby.host({ timeControl: { initial_ms: hostTc.initial_ms, increment_ms: hostTc.increment_ms } })), 'host')); break;
      case 'hosttc': { const t = HOST_TCS.find((c) => c.label === id); if (t) { hostTc = t; render(); } break; }
      case 'solo': gate(() => deal('solo')); break;
      case 'level': if (LEVELS[id]) { setup = soloOptions({ ...setup, level: id }); paintLevel(); } break;
      case 'continue': gate(() => { const saved = readSolo(); if (saved) deal('solo', null, saved); else render(); }); break;
      case 'hotseat': gate(() => deal('hotseat')); break;
      case 'lobby': show('lobby'); break;
      case 'games': show('games'); break;
      case 'profile': show('profile'); break;
      case 'pictures': Promise.resolve(pictures.manage({ still: still() })).then(() => { if (screen === 'menu') render(); }).catch(() => {}); break;
      case 'challenge': if (lobby) gate(() => look(afterHost(() => lobby.challenge(id)))); break;
      case 'cancel': try { if (lobby) lobby.cancel(); } catch { /* fine */ } break;
      // accepting is a round trip on a server; the mock answers at once and
      // Promise.resolve makes both read the same. The ask comes first; backing
      // out of it declines, as ignoring the challenge would.
      case 'accept': if (ask) {
        const a = ask; ask = null; if (a.timer) clearTimeout(a.timer);
        gate(() => Promise.resolve(a.accept()).then((m) => { if (m) matched(m); }).catch(() => { sfx('squelch'); if (screen) render(); }),
          () => { try { a.decline(); } catch { /* fine */ } if (screen) render(); });
      } break;
      case 'decline': if (ask) { try { ask.decline(); } catch { /* fine */ } if (ask.timer) clearTimeout(ask.timer); ask = null; render(); } break;
      case 'go': go(); break;
      case 'stake': { const [k, a] = String(id || '').split(':'); stake.choose(k, Number(a)); break; }
      case 'watch': openReplay(id); break;
      case 'rstart': stopReplay(); stepReplay(0); break;
      case 'rprev': stopReplay(); stepReplay(replay ? replay.i - 1 : 0); break;
      case 'rnext': stopReplay(); stepReplay(replay ? replay.i + 1 : 0, true); break;
      case 'rplay': if (replay && replay.timer) { stopReplay(); render(); } else playReplay(); break;
      case 'rematch': gate(rematch); break;
      case 'menu': toMenu(); break;
      default: break;
    }
  }

  function onClick(e) {
    const b = e.target.closest('[data-act]');
    if (!b || !card.contains(b)) return;
    e.preventDefault();
    act(b.dataset.act, b.dataset.id);
  }
  // Esc: back, then out. Capture phase so boot's own "Esc leaves the board"
  // only sees it from the menu; a countdown is not a place Esc can trap you either.
  function onKey(e) {
    if (e.key !== 'Escape' || screen === null) return;
    if (screen === 'menu') return;                  // boot posts pbp:exit
    if (pictures.isOpen && pictures.isOpen()) return;   // the picture card's own Escape closes it
    e.stopImmediatePropagation();
    e.preventDefault();
    if (screen === 'found') { if (countTimer) { clearTimeout(countTimer); timers.delete(countTimer); countTimer = null; } current = null; stake.withdraw(); stake.clear(); show('lobby'); return; }
    if (screen === 'end') { toMenu(); return; }
    if (screen === 'replay') { show('games'); return; }
    show('menu');
  }
  // a tap on the board during the countdown means "now"
  function onStageTap() { if (screen === 'found') go(); }

  card.addEventListener('click', onClick);
  window.addEventListener('keydown', onKey, { capture: true });
  veil.addEventListener('pointerdown', onStageTap);
  if (bus) {
    unbind.push(bus.on('menu-request', toMenu));
    unbind.push(bus.on('capture', onCapture));
    unbind.push(bus.on('gameover', onGameOver));
  }

  // ---------------------------------------------------------------- friends drawer
  /**
   * An intent from the desktop's friends drawer (host frame `pbp:friend`):
   *   { mode: 'challenge', friendId }   challenge that friend; the challenge id goes back to
   *                                     the host (`pbp:friend-challenge`) so the drawer can send
   *                                     it as the invite, and the lobby waits for the yes
   *   { mode: 'accept', challengeId }   the friend's side: take it up, straight to the board
   * A live online game is never interrupted; the host hears null and says so.
   */
  function friend(intent) {
    const m = intent || {};
    const tell = (challengeId) => { try { postToHost({ type: 'pbp:friend-challenge', friendId: m.friendId || null, challengeId: challengeId || null }); } catch { /* no host */ } };
    const busy = current && current.mode === 'online' && !game.isOver();
    if (busy || !lobby) { if (m.mode === 'challenge') tell(null); return; }
    // a friend's game is a start too: the picture ask comes first while nothing is saved
    if (m.mode === 'challenge' && m.friendId) {
      gate(() => {
        if (screen !== 'lobby') show('lobby');
        let told = false;
        const said = (id) => { if (!told) { told = true; tell(id); } };
        look(afterHost(() => lobby.challenge(String(m.friendId), { onChallengeId: said }))
          .catch((err) => { said(null); throw err; }));
      }, () => tell(null));
      return;
    }
    if (m.mode === 'accept' && m.challengeId && typeof lobby.acceptChallenge === 'function') {
      gate(() => {
        if (screen !== 'lobby') show('lobby');
        look(afterHost(() => lobby.acceptChallenge(String(m.challengeId))));
      });
    }
  }

  return {
    show,
    hide,
    friend,
    isUp: () => screen !== null,
    screen: () => screen,
    /** For the harness: the door's state, and levers to pull. */
    debug: {
      state: () => ({ screen, looking, lookKind, note, ask: ask ? ask.name : null, people: people.length, playing: playingNow.length, current: current ? current.mode : null, replay: replay ? replay.i : null, games: listGames().length, signedOut, hosted: !!host.isHosted, inLobby: !!offList }),
      act,
      openReplay,
      matched,
      stake,
      shelf: { save: saveGame, list: listGames },
      /**
       * Stand in for the desktop host: { isHosted, identity(), whenIdentity() },
       * any subset. What lets a plain browser photograph the hosted states
       * (a read-only name, a signed-out lobby) that only a host can put it in.
       */
      host(fake) {
        host = Object.assign({}, host, fake || {});
        signedOut = false;
        if (offList) leaveLobby();
        if (screen) render();
      },
    },
    dispose() {
      card.removeEventListener('click', onClick);
      window.removeEventListener('keydown', onKey, { capture: true });
      veil.removeEventListener('pointerdown', onStageTap);
      for (const off of unbind) { try { off(); } catch { /* gone */ } }
      for (const t of timers) clearTimeout(t);
      leaveLobby();
      try { stake.dispose(); } catch { /* gone */ }
      if (stakeChip) stakeChip.remove();
    },
  };
}
