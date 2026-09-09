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
 * ==========================================================================*/

import { Chess } from '../vendor/chess.js';
import { listGames, getGame, saveGame, playerName, setPlayerName, profileStats, outcome, fmtDuration, fmtMoves, fmtWhen } from './store.js';

/** Every number the door decides with. */
export const TUNING = Object.freeze({
  endCardDelayMs: 1400,     // the board's own end beat lands first (thud, status line), then the card
  countdownStepMs: 700,     // 3 2 1, CHIME LADDER
  askTimeoutMs: 8000,       // an ignored challenge ignores itself
  replayStepMs: 900,        // auto-play cadence in a replay
  menuSway: 0.3,            // camera drift while the door is up
});
const T = TUNING;

const PIECE_WORD = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };

function esc(s) { return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

function reducedMotion(settings) {
  try {
    if (settings && settings().reducedMotion) return true;
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  } catch { return false; }
}

/** The position map the board wants, off a chess.js instance (mirrors rules.position). */
function positionOf(chess) {
  const map = {};
  for (const row of chess.board()) for (const cell of row) if (cell) map[cell.square] = { type: cell.type, side: cell.color };
  return map;
}

export function createDoor(opts = {}) {
  const { bus, game, board, params } = opts;
  const root = opts.root || (typeof document !== 'undefined' ? document.getElementById('door') : null);
  const lobby = opts.lobby || null;
  const settings = () => (opts.settings ? opts.settings() : ((typeof window !== 'undefined' && window.PBP && window.PBP.settings) || {}));
  const startGame = typeof opts.startGame === 'function' ? opts.startGame : () => {};
  if (!root) return { show() {}, hide() {}, isUp: () => false, debug: () => ({ ok: false, why: 'no #door' }), dispose() {} };

  const veil = document.createElement('div'); veil.className = 'door-veil';
  const card = document.createElement('div'); card.className = 'door-card'; card.setAttribute('role', 'dialog');
  root.append(veil, card);

  let screen = null;            // 'menu' | 'lobby' | 'found' | 'games' | 'profile' | 'replay' | 'end' | null (in game)
  let current = null;           // the game in progress: { mode, match, startedAt, captures }
  let lastEnd = null;           // the record of the last finished game, for the end card + rematch
  let looking = false;
  let ask = null;               // an incoming challenge { id, name, accept, decline, timer }
  let people = [];
  let replay = null;            // { moves, positions, marks, i, timer }
  const timers = new Set();
  const unbind = [];

  function later(fn, ms) { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); return t; }
  function hudEl() { return document.getElementById('hud'); }
  function camEl() { return document.getElementById('cam-views'); }
  function sfx(name) { try { if (board.sfx && board.sfx.play) board.sfx.play(name); } catch { /* quiet */ } }
  function still() { return reducedMotion(settings); }

  // ---------------------------------------------------------------- show / hide
  function show(name) {
    if (name === 'replay' && !replay) name = 'games';
    if (screen === 'replay' && name !== 'replay') closeReplay();
    if (screen === 'lobby' && name !== 'lobby' && name !== 'found') leaveLobby();
    screen = name;
    if (name === 'lobby' && !offList) enterLobby();
    root.hidden = false;
    root.className = 'door up screen-' + name + (still() ? ' still' : '') + (looking ? ' looking' : '');
    for (const el of [hudEl(), camEl()]) if (el) el.classList.add('parked');
    try { board.setCameraSway(name === 'replay' ? 0 : T.menuSway); } catch { /* no rig */ }
    render();
  }

  function hide() {
    if (screen === 'lobby') leaveLobby();
    if (screen === 'replay') closeReplay();
    screen = null;
    root.hidden = true;
    root.className = 'door';
    for (const el of [hudEl(), camEl()]) if (el) el.classList.remove('parked');
    try { board.setCameraSway(0); } catch { /* no rig */ }
  }

  function render() {
    card.innerHTML = '';
    const body = document.createElement('div'); body.className = 'door-body';
    body.innerHTML = ({ menu, lobby: lobbyScreen, found, games, profile, replay: replayScreen, end }[screen] || menu)();
    card.append(body);
    if (screen === 'profile') { const inp = body.querySelector('input'); if (inp) inp.addEventListener('change', () => setPlayerName(inp.value)); }
    if (screen === 'replay') { const r = body.querySelector('.door-scrub'); if (r) r.addEventListener('input', () => stepReplay(Number(r.value))); }
    const focus = body.querySelector('.door-btn.primary') || body.querySelector('button');
    if (focus && !still()) later(() => { try { focus.focus({ preventScroll: true }); } catch { /* fine */ } }, 60);
  }

  // ---------------------------------------------------------------- screens
  const me = () => playerName(settings());

  function menu() {
    return `
      <h1 class="door-title"><b>piece by piece</b></h1>
      <button type="button" class="door-btn primary" data-act="quick">quick match <span class="k">online</span></button>
      <button type="button" class="door-btn" data-act="hotseat">play here <span class="k">two players, one board</span></button>
      <div class="door-links">
        <button type="button" class="door-link" data-act="lobby">lobby</button>
        <button type="button" class="door-link" data-act="games">past games</button>
        <button type="button" class="door-link" data-act="profile">profile</button>
      </div>
      <div class="door-foot"><span>esc leaves the board</span><span class="name">at the board as <b>${esc(me())}</b></span></div>`;
  }

  function lobbyScreen() {
    const rows = people.map((p) => `
      <li class="door-item" data-id="${esc(p.id)}">
        <span class="who">${esc(p.name)}</span>
        <span class="meta">${esc(waitWord(p.waitingSince))}</span>
        <button type="button" class="door-pill" data-act="challenge" data-id="${esc(p.id)}">join</button>
      </li>`).join('');
    const askRow = ask ? `
      <div class="door-ask"><span><b>${esc(ask.name)}</b> wants a game</span>
        <button type="button" class="door-pill" data-act="accept">play</button>
        <button type="button" class="door-link" data-act="decline">ignore</button></div>` : '';
    return `
      <h1 class="door-title">lobby</h1>
      <p class="door-sub">${people.length ? `<b>${people.length}</b> at the board` : 'nobody else here yet'} - you are visible as <b>${esc(me())}</b></p>
      ${askRow}
      <button type="button" class="door-btn primary" data-act="quick" ${looking ? 'disabled' : ''}>${looking ? 'looking for a game' : 'quick match'} ${looking ? '<span class="door-dot"></span>' : '<span class="k">whoever waited longest</span>'}</button>
      ${people.length ? `<ul class="door-list">${rows}</ul>` : `<div class="door-empty"><span class="door-dot"></span>nobody at the board yet - you are first in line</div>`}
      <div class="door-foot"><span>esc - back</span>${looking ? '<button type="button" class="door-link" data-act="cancel">stop looking</button>' : ''}</div>`;
  }

  function found() {
    const m = current && current.match;
    const side = m ? m.side : 'w';
    return `
      <div class="door-found">
        <p class="line">matched with ${esc(m ? m.opponent.name : '')}</p>
        <p class="side">you play <b class="${side}">${side === 'w' ? 'white' : 'black'}</b></p>
        <div class="door-count" id="door-count"></div>
        <button type="button" class="door-link skip" data-act="go">tap to start now</button>
      </div>`;
  }

  function games() {
    const list = listGames();
    const rows = list.map((g) => {
      const o = outcome(g);
      const word = o ? o : (g.result ? (g.result.winner ? (g.result.winner === 'w' ? 'white won' : 'black won') : 'draw') : 'unfinished');
      return `
      <li class="door-item">
        <span class="who">${esc(g.opponent || 'a friend here')}<br><span class="meta">${esc(fmtWhen(g.at))}</span></span>
        <span class="meta ${o || ''}">${esc(word)} &middot; ${esc(fmtMoves(g.plies))}</span>
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
    return `
      <h1 class="door-title">profile</h1>
      <div class="door-name"><input type="text" maxlength="24" value="${esc(me())}" aria-label="your name at the board" spellcheck="false"></div>
      <div class="door-stats">
        ${stat(s.games, 'games')}${stat(s.wins, 'wins', true)}${stat(s.losses, 'losses')}
        ${stat(s.draws, 'draws')}${stat(s.streak, 'streak', s.streak > 1)}${stat(s.captures, 'taken')}
        ${stat(s.favourite || '-', 'favourite')}${stat(fmtDuration(s.ms), 'at the board')}${stat(s.rating == null ? 'unrated' : s.rating, 'rating')}
      </div>
      <div class="door-foot"><span>esc - back</span><span>rating comes with the season</span></div>`;
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
    const res = g.result || {};
    let line;
    if (o === 'win') line = 'you win'; else if (o === 'loss') line = 'you lose'; else if (o === 'draw') line = 'a draw';
    else line = res.winner ? (res.winner === 'w' ? 'white wins' : 'black wins') : 'a draw';
    const how = res.reason && res.reason !== res.result ? res.reason : res.result;
    const caps = g.captures || { w: 0, b: 0 };
    return `
      <div class="door-end">
        <p class="result ${o || ''}">${esc(line)}</p>
        <p class="tally">${esc(how || '')} &middot; ${esc(fmtMoves(g.plies))} &middot; ${(caps.w || 0) + (caps.b || 0)} taken &middot; ${esc(fmtDuration(g.durationMs))}</p>
        <div id="door-recap"></div>
      </div>
      <button type="button" class="door-btn primary" data-act="rematch">rematch</button>
      <button type="button" class="door-btn" data-act="menu">back to the door</button>`;
  }

  function waitWord(since) {
    const s = Math.max(0, Math.round((Date.now() - (since || Date.now())) / 1000));
    if (s < 60) return 'just sat down';
    return 'waiting ' + Math.floor(s / 60) + ' min';
  }

  // ---------------------------------------------------------------- lobby
  let offList = null, offAsk = null;
  async function enterLobby() {
    if (!lobby) return;
    try { await lobby.enter({ name: me() }); } catch { /* the door still opens */ }
    if (offList) offList();
    offList = lobby.onList((l) => { people = l || []; if (screen === 'lobby') render(); });
    if (offAsk) offAsk();
    offAsk = lobby.onChallenge((offer) => {
      if (screen !== 'lobby' || ask) { try { offer.decline(); } catch { /* fine */ } return; }
      ask = offer;
      sfx('pop');
      ask.timer = later(() => { if (ask === offer) { try { offer.decline(); } catch { /* fine */ } ask = null; if (screen === 'lobby') render(); } }, T.askTimeoutMs);
      render();
    });
    try { people = await lobby.list(); } catch { people = []; }
    if (screen === 'lobby') render();
  }
  function leaveLobby() {
    looking = false;
    if (ask) { try { ask.decline(); } catch { /* fine */ } ask = null; }
    if (offList) { offList(); offList = null; }
    if (offAsk) { offAsk(); offAsk = null; }
    try { if (lobby) lobby.leave(); } catch { /* fine */ }
  }

  async function look(promise) {
    looking = true;
    root.classList.add('looking');
    if (screen === 'lobby') render();
    try {
      const match = await promise;
      looking = false;
      root.classList.remove('looking');
      matched(match);
    } catch (err) {
      looking = false;
      root.classList.remove('looking');
      if (screen === 'lobby') render();
      if (err && err.message === 'left') sfx('squelch');
    }
  }

  function matched(match) {
    current = { mode: 'online', match, startedAt: 0, captures: { w: 0, b: 0 } };
    sfx('promote');
    show('found');
    countdown();
  }

  let countTimer = null;
  function countdown() {
    let n = 3;
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
    if (screen === 'found') { screen = null; }
    deal(current.mode, current.match);
  }

  // ---------------------------------------------------------------- the game itself
  function deal(mode, match = null) {
    current = { mode, match, startedAt: Date.now(), captures: { w: 0, b: 0 } };
    hide();
    startGame({ mode, match });
  }

  function onCapture(p) { if (current && p && p.by) current.captures[p.by] = (current.captures[p.by] || 0) + 1; }

  function onGameOver() {
    if (!current) return;
    let rec = {};
    try { rec = game.record ? game.record() : {}; } catch { rec = {}; }
    const m = current.match;
    lastEnd = saveGame({
      mode: current.mode, me: m ? m.side : null, opponent: m ? m.opponent.name : 'a friend here',
      moves: rec.moves || [], plies: rec.plies || 0, result: rec.result || null,
      durationMs: Date.now() - current.startedAt, captures: { ...current.captures }, clocks: rec.clocks || null,
    });
    const finished = current;
    current = null;
    // the board's own end beat first, then the card
    later(() => { if (!current && screen === null) { lastEnd.rematchOf = finished; show('end'); } }, still() ? 0 : T.endCardDelayMs);
  }

  function rematch() {
    const prev = lastEnd && lastEnd.rematchOf;
    if (prev && prev.mode === 'online' && prev.match) {
      const swapped = { ...prev.match, side: prev.match.side === 'w' ? 'b' : 'w' };
      deal('online', swapped);
    } else {
      deal('hotseat');
    }
  }

  function toMenu() {
    try { game.reset(); board.pieces.setPosition(game.rules.position()); } catch { /* the board stays as it is */ }
    try { board.setSide('w', true); } catch { /* no rig */ }
    show('menu');
  }

  // ---------------------------------------------------------------- replay
  function openReplay(id) {
    const g = getGame(id);
    if (!g) return;
    const chess = new Chess();
    const positions = [positionOf(chess)];
    const marks = [null];
    for (const san of g.moves || []) {
      let mv = null;
      try { mv = chess.move(san); } catch { mv = null; }
      if (!mv) break;
      positions.push(positionOf(chess));
      marks.push({ from: mv.from, to: mv.to });
    }
    replay = { moves: (g.moves || []).slice(0, positions.length - 1), positions, marks, i: 0, timer: null,
               title: `${g.opponent || 'a friend here'} - ${fmtWhen(g.at)}` };
    show('replay');
    stepReplay(0);
  }
  function stepReplay(i) {
    if (!replay) return;
    replay.i = Math.max(0, Math.min(replay.positions.length - 1, i));
    try { board.pieces.setPosition(replay.positions[replay.i]); } catch { /* no board */ }
    const mk = replay.marks[replay.i];
    try { const m = board.drag && board.drag.markers; if (m && m.setLastMove) m.setLastMove(mk ? mk.from : null, mk ? mk.to : null); } catch { /* no markers */ }
    try { board.setSide(replay.i % 2 === 0 ? 'w' : 'b'); } catch { /* no rig */ }
    if (replay.i === replay.positions.length - 1 && replay.timer) stopReplay();
    if (screen === 'replay') render();
  }
  function playReplay() {
    if (!replay || replay.timer) return;
    if (replay.i >= replay.positions.length - 1) stepReplay(0);
    const tickFn = () => { if (!replay || !replay.timer) return; stepReplay(replay.i + 1); if (replay && replay.timer) replay.timer = later(tickFn, T.replayStepMs); };
    replay.timer = later(tickFn, T.replayStepMs);
    render();
  }
  function stopReplay() { if (replay && replay.timer) { clearTimeout(replay.timer); timers.delete(replay.timer); replay.timer = null; } }
  function closeReplay() {
    stopReplay();
    replay = null;
    try { const m = board.drag && board.drag.markers; if (m && m.setLastMove) m.setLastMove(null, null); } catch { /* fine */ }
    try { game.reset(); board.pieces.setPosition(game.rules.position()); board.setSide('w', true); } catch { /* fine */ }
  }

  // ---------------------------------------------------------------- input
  function act(name, id) {
    switch (name) {
      case 'quick': if (screen !== 'lobby') show('lobby'); if (lobby) look(lobby.quickMatch()); break;
      case 'hotseat': deal('hotseat'); break;
      case 'lobby': show('lobby'); break;
      case 'games': show('games'); break;
      case 'profile': show('profile'); break;
      case 'challenge': if (lobby) look(lobby.challenge(id)); break;
      case 'cancel': try { if (lobby) lobby.cancel(); } catch { /* fine */ } break;
      case 'accept': if (ask) { const m = ask.accept(); const a = ask; ask = null; if (a.timer) clearTimeout(a.timer); if (m) matched(m); } break;
      case 'decline': if (ask) { try { ask.decline(); } catch { /* fine */ } if (ask.timer) clearTimeout(ask.timer); ask = null; render(); } break;
      case 'go': go(); break;
      case 'watch': openReplay(id); break;
      case 'rstart': stopReplay(); stepReplay(0); break;
      case 'rprev': stopReplay(); stepReplay(replay ? replay.i - 1 : 0); break;
      case 'rnext': stopReplay(); stepReplay(replay ? replay.i + 1 : 0); break;
      case 'rplay': if (replay && replay.timer) { stopReplay(); render(); } else playReplay(); break;
      case 'rematch': rematch(); break;
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
    e.stopImmediatePropagation();
    e.preventDefault();
    if (screen === 'found') { if (countTimer) { clearTimeout(countTimer); timers.delete(countTimer); countTimer = null; } current = null; show('lobby'); return; }
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
    unbind.push(bus.on('capture', onCapture));
    unbind.push(bus.on('gameover', onGameOver));
  }

  return {
    show,
    hide,
    isUp: () => screen !== null,
    screen: () => screen,
    /** For the harness: the door's state, and levers to pull. */
    debug: {
      state: () => ({ screen, looking, ask: ask ? ask.name : null, people: people.length, current: current ? current.mode : null, replay: replay ? replay.i : null, games: listGames().length }),
      act,
      openReplay,
      matched,
      shelf: { save: saveGame, list: listGames },
    },
    dispose() {
      card.removeEventListener('click', onClick);
      window.removeEventListener('keydown', onKey, { capture: true });
      veil.removeEventListener('pointerdown', onStageTap);
      for (const off of unbind) { try { off(); } catch { /* gone */ } }
      for (const t of timers) clearTimeout(t);
      leaveLobby();
    },
  };
}
