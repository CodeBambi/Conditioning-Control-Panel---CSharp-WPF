// Self-contained pass over OPEN TABLES (2026-09-23): the lobby of open games and the Prime gate.
//
//   node Resources/web/goon/test/selftest-opentables.js
//
// What is asserted:
//   1. net/openTables.js makes a /open row safe: no free text, https or the server's own avatar
//      path only, a bad code drops the row.
//   2. Friends first, newest first, one row per code, capped at 20.
//   3. The gate: the server's `you` wins, then the host's flags (`=== false` locks, absent does
//      not), and standalone with no answer locks nothing on a guess. Practice is never asked.
//   4. A failed join maps to Taken, the Prime sheet, sign in, or a plain sheet.
//   5. The ticker (the /open poll and the /list renew) runs on start, on its interval, skips while
//      inactive, bumps at once, and stops for good.
//   6. The signaling client and the fake server agree on /list, /open and the join gate.
//   7. The copy: no em-dashes, no exclamation marks, the Prime sheet says practice is free.
//   8. The home screen, mounted against a fake DOM: rows render, free accounts see locks, Practice
//      is never locked, and a Taken join greys its row.

import {
  normalizeTable, normalizeOpen, sortTables, groupTables, gateFor, joinOutcome, listingBody,
  createTicker, clock, agoText, safeAvatar, resolveAvatar, TABLES_MAX, OPEN_POLL_MS, LIST_RENEW_MS,
} from '../net/openTables.js';
import { GoonSignalingClient, GoonSignalError, GOON_PATHS } from '../net/signaling.js';
import { GoonFakeSignalingServer } from '../net/fakeSignaling.js';
import { S } from '../ui/strings.js';

const fs = await import('node:fs');
const path = await import('node:path');
const urlMod = await import('node:url');
const HERE = path.dirname(urlMod.fileURLToPath(import.meta.url));
const read = (rel) => fs.readFileSync(path.join(HERE, '..', rel), 'utf8');

let n = 0;
let failures = 0;
function ok(cond, what, detail) {
  n++;
  if (!cond) { failures++; console.error('FAIL: ' + what + (detail !== undefined ? ' (' + detail + ')' : '')); }
}
const quiet = { info() {}, warn() {}, error() {}, debug() {} };

// ------------------------------------------------------------ 1. the row
{
  const r = normalizeTable({
    code: 'k7q-p2m', name: '<b>pink‮static</b>', level: 41.7, avatar: 'javascript:alert(1)',
    friend: true, song: true, cardSec: 60, pictures: 1, waitingSec: 42, title: 'my secret song', note: 'hi',
  });
  ok(!!r, 'a good row normalizes');
  ok(r.code === 'K7QP2M', 'code upper-cased, dash stripped', r.code);
  ok(!/[<>‮]/.test(r.name), 'markup and bidi characters never reach the name', r.name);
  ok(r.level === 41, 'level floors to an int');
  ok(r.avatar === '', 'a javascript: avatar is dropped');
  ok(r.pictures === false, 'flags are strict booleans (1 is not true)');
  ok(!('title' in r) && !('note' in r), 'no field outside the contract survives (no host text)');
  ok(Object.keys(r).sort().join(',') === 'avatar,cardSec,code,friend,level,name,pictures,song,waitingSec',
    'exactly the contract fields', Object.keys(r).join(','));
  ok(normalizeTable({ code: 'a"b' }) === null, 'a code with junk drops the row');
  ok(normalizeTable(null) === null && normalizeTable('x') === null, 'non-objects drop');
  ok(normalizeTable({ code: 'ABC123', name: '' }).name === 'someone', 'an empty name reads as someone');
  ok(safeAvatar('https://cdn.discordapp.com/a/b.png') === 'https://cdn.discordapp.com/a/b.png', 'https avatar kept');
  ok(safeAvatar('http://x/y.png') === '', 'plain http avatar dropped');
  ok(safeAvatar('/v2/friends/avatar/u_abc123') === '/v2/friends/avatar/u_abc123', 'the server friend-avatar path is kept');
  ok(safeAvatar('/v2/friends/avatar/../../x') === '', 'a path that walks is not');
  ok(resolveAvatar('/v2/friends/avatar/u_abc123', 'https://proxy.example/') === 'https://proxy.example/v2/friends/avatar/u_abc123',
    'a relative avatar rides the server base');
  ok(resolveAvatar('/v2/friends/avatar/u_abc123', '') === '', 'and there is none without a base');
  ok(resolveAvatar('https://x.example/a.png', '') === 'https://x.example/a.png', 'an absolute one needs no base');
}

// ------------------------------------------------------------ 2. sorting
{
  const rows = [
    { code: 'AAAAA1', friend: false, waitingSec: 5 },
    { code: 'AAAAA2', friend: true, waitingSec: 100 },
    { code: 'AAAAA3', friend: true, waitingSec: 10 },
    { code: 'AAAAA1', friend: false, waitingSec: 1 },
    { code: 'AAAAA4', friend: false, waitingSec: 50 },
  ].map(normalizeTable);
  const s = sortTables(rows);
  ok(s.map((r) => r.code).join(',') === 'AAAAA3,AAAAA2,AAAAA1,AAAAA4', 'friends first, newest first, deduped', s.map((r) => r.code).join(','));
  const g = groupTables(rows);
  ok(g.friends.length === 2 && g.anyone.length === 2, 'grouped into friends and anyone');
  const many = Array.from({ length: 30 }, (_, i) => normalizeTable({ code: 'CODE' + String(i).padStart(2, '0'), waitingSec: i }));
  ok(sortTables(many).length === TABLES_MAX, 'capped at ' + TABLES_MAX);
  const o = normalizeOpen({ you: { canHost: true, canJoin: false }, tables: [{ code: 'ZZZZZ1' }, { code: '!' }], lastOpenedAgoSec: 720 });
  ok(o.you.canHost === true && o.you.canJoin === false, 'you block read');
  ok(o.tables.length === 1, 'a bad row is dropped, the rest kept');
  ok(o.lastOpenedAgoSec === 720, 'lastOpenedAgoSec kept');
  ok(normalizeOpen({}).you === null && normalizeOpen({}).lastOpenedAgoSec === null, 'a silent server says nothing, not "no"');
}

// ------------------------------------------------------------ 3. the gate
{
  ok(gateFor({ you: { canHost: false, canJoin: false }, caps: { canHost: true }, hosted: true }).canHost === false,
    "the server's you block beats the host flags");
  ok(gateFor({ you: { canHost: true, canJoin: true } }).source === 'server', 'and says so');
  const h = gateFor({ caps: { canHost: false, canJoin: false }, hosted: true });
  ok(!h.canHost && !h.canJoin && h.source === 'host', 'hosted canHost/canJoin false lock both');
  const h2 = gateFor({ caps: { video: true }, hosted: true });
  ok(h2.canHost && h2.canJoin, 'absent flags do not lock (`=== false` on purpose)');
  ok(gateFor({ caps: null, hosted: true }).canJoin, 'a caps-less init frame locks nothing');
  const sa = gateFor({ caps: { canHost: false }, hosted: false });
  ok(sa.canHost && sa.canJoin && sa.source === 'unknown', 'standalone with no answer locks nothing on a guess');
  ok(!('canPractice' in sa), 'practice is not a gated thing at all');
}

// ------------------------------------------------------------ 4. join outcomes
{
  ok(joinOutcome('already_joined') === 'taken', 'already_joined -> Taken');
  ok(joinOutcome('unknown_code') === 'taken' && joinOutcome('expired') === 'taken', 'a vanished table -> Taken');
  ok(joinOutcome('no_join_access') === 'prime', 'no_join_access -> the Prime sheet');
  ok(joinOutcome('signin') === 'signin', 'signin -> sign in');
  ok(joinOutcome('network') === 'sheet' && joinOutcome('') === 'sheet', 'anything else -> the plain sheet');
}

// ------------------------------------------------------------ 5. the ticker
{
  let now = 0;
  const timers = [];
  const setTimer = (fn, ms) => { const t = { fn, at: now + ms, dead: false }; timers.push(t); return t; };
  const clearTimer = (t) => { if (t) t.dead = true; };
  const advance = (ms) => {
    const end = now + ms;
    for (;;) {
      const due = timers.filter((t) => !t.dead && t.at <= end).sort((a, b) => a.at - b.at)[0];
      if (!due) break;
      now = due.at; due.dead = true; due.fn();
    }
    now = end;
  };
  let runs = 0;
  let active = true;
  const tk = createTicker({ run: () => { runs++; }, intervalMs: LIST_RENEW_MS, isActive: () => active, setTimer, clearTimer });
  tk.start();
  ok(runs === 1, 'start runs once, now');
  tk.start();
  ok(runs === 1, 'a second start does not double the loop');
  advance(LIST_RENEW_MS);
  ok(runs === 2, 'and again after one interval');
  advance(LIST_RENEW_MS * 3);
  ok(runs === 5, 'and on every interval', runs);
  active = false;
  advance(LIST_RENEW_MS * 2);
  ok(runs === 5, 'an inactive page (hidden list) does not call', runs);
  active = true;
  tk.bump();
  ok(runs === 6, 'bump runs at once (a switch change)');
  advance(LIST_RENEW_MS - 1);
  ok(runs === 6, 'and restarts the wait');
  tk.stop();
  advance(LIST_RENEW_MS * 5);
  ok(runs === 7 || runs === 6, 'stop ends it', runs);
  const after = runs;
  advance(LIST_RENEW_MS * 5);
  ok(runs === after && tk.stopped, 'for good');
  tk.bump();
  ok(runs === after, 'a bump after stop does nothing');
  const throwing = createTicker({ run: () => { throw new Error('boom'); }, intervalMs: 10, setTimer, clearTimer });
  throwing.start();
  ok(throwing.runs === 1, 'a throwing run never breaks the loop');
  throwing.stop();
  ok(OPEN_POLL_MS === 15000 && LIST_RENEW_MS === 60000, 'the brief cadences: poll 15 s, renew 60 s');
}

// ------------------------------------------------------------ 6. client + fake server
{
  let t = 1000000;
  const server = new GoonFakeSignalingServer({ now: () => t, hostGate: true, joinGate: true, labAccess: ['u_host', 'u_friend', 'u_stranger'] });
  server.setProfile('u_host', { name: 'pinkstatic', level: 41 });
  server.setFriend('u_friend', 'u_host');
  const client = (uid) => new GoonSignalingClient({ post: server.post, unifiedId: uid, logger: quiet });
  const host = client('u_host');
  const friend = client('u_friend');
  const stranger = client('u_stranger');
  const free = client('u_free');

  const inv = await host.createInvite('pinkstatic');
  ok(!!inv, 'a Prime host mints a room');
  const listed = await host.list(inv.code, { visibility: 'friends', song: true, cardSec: 60, pictures: true, token: inv.token });
  ok(listed && listed.visibility === 'friends' && listed.expiresInSec === 150, 'the host lists it for friends', JSON.stringify(listed));
  const listReq = server.requests.filter((r) => r.path === GOON_PATHS.list).pop();
  ok(Object.keys(listReq.body).sort().join(',') === 'cardSec,code,pictures,song,token,unified_id,visibility',
    'the /list body carries flags and numbers only, never a title', Object.keys(listReq.body).join(','));

  let o = await friend.open();
  ok(o && o.tables.length === 1 && o.tables[0].friend === true, 'a friend sees it, marked friend');
  ok(o.tables[0].name === 'pinkstatic' && o.tables[0].level === 41 && o.tables[0].song && o.tables[0].cardSec === 60,
    'with the host name, level and flags');
  ok(o.you.canHost && o.you.canJoin, 'and a Prime caller may host and join');
  o = await stranger.open();
  ok(o && o.tables.length === 0, 'a stranger does not see a Friends table');
  o = await host.open();
  ok(o && o.tables.length === 0, 'the host never sees their own table');

  await host.list(inv.code, { visibility: 'anyone' });
  o = await stranger.open();
  ok(o && o.tables.length === 1 && o.tables[0].friend === false, 'Anyone shows it to a stranger, unmarked');
  o = await free.open();
  ok(o && o.tables.length === 1, 'a FREE account still sees the rows');
  ok(o.you.canJoin === false && o.you.canHost === false, 'but may not join or host');

  server.setBlocked('u_stranger', 'u_host');
  o = await stranger.open();
  ok(o && o.tables.length === 0, 'a blocked pair never sees each other');
  server.setBlocked('u_stranger', 'u_host', false);

  const off = await host.list(inv.code, { visibility: 'off' });
  ok(off && off.visibility === 'off' && !server.listing(inv.code), 'Off unlists');
  o = await stranger.open();
  ok(o && o.tables.length === 0, 'and the list is empty again');
  ok(server.room(inv.code), 'but the room (and its code) still stands');
  await host.list(inv.code, { visibility: 'anyone' });

  const notHost = await stranger.list(inv.code, { visibility: 'anyone' });
  ok(notHost === null && stranger.lastError === GoonSignalError.NotHost, 'only the host may list a room', stranger.lastError);

  const noJoin = await free.join(inv.code, 'freeloader');
  ok(noJoin === null && free.lastError === GoonSignalError.NoJoinAccess, 'a free account is refused no_join_access', free.lastError);
  ok(!server.room(inv.code).joined, 'before the room is touched');
  const anon = new GoonSignalingClient({ post: server.post, anonymous: true, logger: quiet });
  ok((await anon.join(inv.code)) === null && anon.lastError === GoonSignalError.SignIn, 'no account is refused signin', anon.lastError);
  const anonOpen = await anon.open();
  ok(anonOpen === null && anon.lastError === GoonSignalError.SignIn, '/open needs an account too');

  const seat = await stranger.join(inv.code, 'stranger');
  ok(!!seat, 'a Prime stranger sits down');
  ok(!server.listing(inv.code), 'a seated table leaves the list');
  const renewLate = await host.list(inv.code, { visibility: 'anyone' });
  ok(renewLate === null && host.lastError === GoonSignalError.NotLobby, 'and renewing it now says not_lobby', host.lastError);
  const taken = await friend.join(inv.code, 'friend');
  ok(taken === null && friend.lastError === GoonSignalError.AlreadyJoined, 'the next one in hears already_joined (Taken)');
  ok(joinOutcome(friend.lastError) === 'taken', 'which the row shows as Taken');

  // Listing expiry: a table nobody renews drops off after 150 s, and lastOpenedAgoSec counts up.
  const inv2 = await host.createInvite('pinkstatic');
  await host.list(inv2.code, { visibility: 'anyone' });
  t += 60000;
  o = await stranger.open();
  ok(o.tables.length === 1 && o.tables[0].waitingSec === 60, 'waitingSec counts from the first listing', o.tables[0] && o.tables[0].waitingSec);
  t += 100000;
  o = await stranger.open();
  ok(o.tables.length === 0, 'an unrenewed listing expires');
  ok(o.lastOpenedAgoSec === 160, 'and the empty state knows when the last one opened', o.lastOpenedAgoSec);

  // The {ok:false, reason} shape at 200 is also a refusal.
  const shaped = new GoonSignalingClient({ post: async () => ({ status: 200, body: JSON.stringify({ ok: false, reason: 'not_lobby' }) }), unifiedId: 'u_x', logger: quiet });
  ok((await shaped.list('ABC123', {})) === null && shaped.lastError === 'not_lobby', 'a 200 {ok:false, reason} is a refusal');
  const r403 = new GoonSignalingClient({ post: async () => ({ status: 403, body: JSON.stringify({ ok: false, reason: 'no_join_access' }) }), unifiedId: 'u_x', logger: quiet });
  ok((await r403.join('ABC123')) === null && r403.lastError === 'no_join_access', 'a 403 carrying only `reason` still names it');
  const nd = new GoonSignalingClient({ post: async () => ({ status: 404, body: '' }), unifiedId: 'u_x', logger: quiet });
  ok((await nd.open()) === null && nd.lastError === GoonSignalError.NotDeployed, 'an undeployed /open reads not_deployed');

  // Gates default off, so the older suites keep their free, anonymous joins.
  const old = new GoonFakeSignalingServer();
  const oh = new GoonSignalingClient({ post: old.post, unifiedId: 'u_a', logger: quiet });
  const og = new GoonSignalingClient({ post: old.post, anonymous: true, logger: quiet });
  const oi = await oh.createInvite('a');
  ok(!!(await og.join(oi.code)), 'joinGate defaults off');
}

// ------------------------------------------------------------ 7. copy + wiring
{
  const dash = /—|–/;
  const flat = (o) => Object.values(o).map((v) => (typeof v === 'function' ? v(3, 2) : String(v))).join(' | ');
  for (const k of ['tables', 'table', 'prime']) {
    ok(!dash.test(flat(S[k])), 'S.' + k + ' has no em or en dashes');
    ok(!/!/.test(flat(S[k])), 'S.' + k + ' has no exclamation marks');
  }
  ok(/free/i.test(S.prime.line) && /practice/i.test(S.prime.line), 'the Prime sheet says practice is free');
  ok(S.prime.headline === '1v1 is a Prime game' && S.prime.badge === 'PRIME SUBJECT', 'the approved headline and badge');
  ok(!dash.test(S.sheets.noHostAccess.line) && /practice/i.test(S.sheets.noHostAccess.line),
    'the old host refusal no longer promises a free join');
  ok(!/joining a room is always free/.test(S.title.hostNoLab), 'nor does the menu note');
  ok(clock(60) === '1:00' && clock(42) === '0:42' && clock(0) === '0:00', 'clock');
  ok(agoText(30) === 'just now' && agoText(720) === '12 minutes ago' && agoText(3600) === 'an hour ago' && agoText(null) === '', 'agoText');

  const sheets = read('ui/sheets.js');
  ok(/case 'no_join_access':\s*\n\s*return showPrime\(\);/.test(sheets), 'showSignalError sends no_join_access to the Prime sheet');
  ok(/case 'no_host_access':\s*\n\s*case 'no_pass':\s*\n\s*if \(primeActions\) return showPrime\(\);/.test(sheets), 'and no_host_access too, once boot wired it');
  const boot = read('boot.js');
  ok(/sheets\.setPrimeActions\(/.test(boot), 'boot wires the Prime sheet actions');
  ok(/practice: \(\) => \{ void actions\.goPractice\(\); \}/.test(boot), 'practice is always one tap from the sheet');
  ok(/m\.joinCode/.test(boot) && /bridge\.on\('join-code'/.test(boot), 'boot reads init.joinCode and the join-code frame (desk seam)');
  ok(/stampSeated\(\);/.test(boot), 'a landed join stamps SEATED');
  const host = read('ui/screens/host.js');
  ok(/if \(answer === 'practice'\) return;/.test(host), 'host.js does not route over a practice pick');
  ok(/createTicker\(\{ run: sendListing, intervalMs: LIST_RENEW_MS \}\)/.test(host), 'host.js renews on the 60 s clock');
  ok(/renew\?\.stop\(\); ledger\.dispose\(\);/.test(host), 'and stops when the screen goes (seated or closed)');
  const title = read('ui/screens/title.js');
  ok(/createTicker\(\{ run: refresh, intervalMs: OPEN_POLL_MS, isActive: isVisible \}\)/.test(title), 'title polls /open only while visible');
  const rv = await import('../ui/rivalry.js');
  const mem = new Map();
  const store = { getItem: (k) => (mem.has(k) ? mem.get(k) : null), setItem: (k, v) => mem.set(k, String(v)) };
  let clockMs = 1;
  const riv = rv.createRivalry({ store, now: () => clockMs++ });
  riv.note('Mia', 'w'); riv.note('lacequeen', 'l'); riv.note('Mia', 'l');
  const rec = riv.recent(3);
  ok(rec.length === 2 && rec[0].name === 'Mia' && rec[0].w === 1 && rec[0].l === 1, 'rivalry.recent lists the newest first', JSON.stringify(rec));
}

// ------------------------------------------------------------ 8. the home screen, mounted
function makeNode(tagName) {
  const kids = [];
  const map = new Map();
  const classes = new Set();
  const attrs = new Map();
  const node = {
    tagName: String(tagName || 'div').toUpperCase(), nodeType: 1, children: kids, childNodes: kids, parentNode: null,
    hidden: false, style: {}, dataset: {}, value: '', disabled: false, title: '', innerHTML: '',
    _text: '',
    get textContent() { return node._text + kids.map((k) => k.textContent || '').join(''); },
    set textContent(v) { kids.length = 0; node._text = String(v); },
    get lastChild() { return kids[kids.length - 1] || null; },
    get firstChild() { return kids[0] || null; },
    get className() { return Array.from(classes).join(' '); },
    set className(v) { classes.clear(); String(v || '').split(/\s+/).filter(Boolean).forEach((c) => classes.add(c)); },
    classList: {
      add: (...c) => c.forEach((x) => classes.add(x)),
      remove: (...c) => c.forEach((x) => classes.delete(x)),
      toggle: (c, on) => { const v = on === undefined ? !classes.has(c) : !!on; if (v) classes.add(c); else classes.delete(c); return v; },
      contains: (c) => classes.has(c),
    },
    appendChild(child) { if (child) { if (child.parentNode) child.parentNode.removeChild(child); child.parentNode = node; kids.push(child); } return child; },
    append(...c) { c.forEach((x) => node.appendChild(typeof x === 'string' ? doc.createTextNode(x) : x)); },
    prepend(child) { if (child) { child.parentNode = node; kids.unshift(child); } return child; },
    removeChild(child) { const i = kids.indexOf(child); if (i >= 0) kids.splice(i, 1); child.parentNode = null; return child; },
    remove() { if (node.parentNode) node.parentNode.removeChild(node); },
    replaceChildren(...c) { for (const k of kids) k.parentNode = null; kids.length = 0; node._text = ''; c.forEach((x) => x && node.appendChild(x)); },
    setAttribute(k, v) { attrs.set(k, String(v)); if (k === 'class') node.className = String(v); },
    getAttribute(k) { return attrs.has(k) ? attrs.get(k) : null; },
    removeAttribute(k) { attrs.delete(k); },
    addEventListener(type, fn) { if (!map.has(type)) map.set(type, new Set()); map.get(type).add(fn); },
    removeEventListener(type, fn) { const s = map.get(type); if (s) s.delete(fn); },
    click() { const s = map.get('click'); if (s) for (const fn of Array.from(s)) fn({ type: 'click', preventDefault() {}, target: node }); },
    focus() {}, blur() {},
    _classes: classes,
  };
  return node;
}
const doc = makeNode('#document');
doc.body = makeNode('body');
doc.documentElement = makeNode('html');
doc.visibilityState = 'visible';
doc.createElement = (tag) => makeNode(tag);
doc.createTextNode = (t) => { const x = makeNode('#text'); x._text = String(t); return x; };
doc.getElementById = () => null;
globalThis.document = doc;
globalThis.window = makeNode('window');

const title = await import('../ui/screens/title.js');
function findAll(root, cls) {
  const out = [];
  (function walk(node) {
    if (!node) return;
    if (node._classes && node._classes.has(cls)) out.push(node);
    for (const k of node.children || []) walk(k);
  })(root);
  return out;
}
const tick = () => new Promise((r) => setTimeout(r, 0));

async function mountHome({ you, tables = [], hosted = true, caps = { canHost: true, canJoin: true }, join = null }) {
  const container = makeNode('div');
  const calls = [];
  const primes = [];
  const toasts = [];
  const handle = title.mount(container, {
    session: { hosted, caps, identity: { displayName: 'me' }, net: { serverBase: 'https://proxy.example' } },
    actions: {
      goHost: () => calls.push('host'), goJoin: () => calls.push('join'), goPractice: () => calls.push('practice'),
      goJoinCode: (c) => calls.push('code:' + c), goAssets() {}, goVoice() {}, quit() {},
      openTables: async () => ({ ok: true, data: normalizeOpen({ you, tables, lastOpenedAgoSec: 720 }) }),
      joinStart: async (code) => { calls.push('joinStart:' + code); return join ? join(code) : { ok: true }; },
    },
    sheets: { showPrime: async () => { primes.push(1); return null; }, showSignalError: async () => null, openNode() {} },
    toasts: { warn: (t) => toasts.push(t), good() {} },
    rivalry: { recordFor: (name) => (name === 'pinkstatic' ? { w: 3, l: 2, d: 0, known: true } : { known: false }), recent: () => [] },
    logger: quiet,
  });
  await tick(); await tick();
  return { container, calls, primes, toasts, handle };
}

{
  const rows = [
    { code: 'FRND01', name: 'pinkstatic', level: 41, friend: true, song: true, cardSec: 60, pictures: true, waitingSec: 42, avatar: '/v2/friends/avatar/u_pink' },
    { code: 'ANYO01', name: 'velvetvoid', level: 57, friend: false, song: false, cardSec: 0, pictures: false, waitingSec: 15 },
  ];
  const prime = await mountHome({ you: { canHost: true, canJoin: true }, tables: rows });
  const got = findAll(prime.container, 'gg-ot-row');
  ok(got.length === 2, 'two rows render', got.length);
  ok(got[0]._classes.has('is-friend'), 'the friend row comes first and is marked');
  ok(/you 3 - 2/.test(got[0].textContent), 'with the LOCAL rivalry record', got[0].textContent);
  ok(/new/.test(got[1].textContent), 'a stranger reads "new"');
  const imgs = [];
  (function walk(x) { if (!x) return; if (x.tagName === 'IMG' && x.getAttribute('src')) imgs.push(x.getAttribute('src')); for (const k of x.children || []) walk(k); })(got[0]);
  ok(imgs.includes('https://proxy.example/v2/friends/avatar/u_pink'), 'the friend avatar resolves against the server base', imgs.join(','));
  const joins = findAll(prime.container, 'gg-ot-join');
  ok(joins.length === 2 && joins.every((b) => !b._classes.has('is-locked')), 'a Prime account gets live Sit down buttons');
  joins[0].click();
  await tick(); await tick();
  ok(prime.calls.includes('joinStart:FRND01'), 'Sit down joins that code');
  const hostItem = findAll(prime.container, 'gg-menu-item')[0];
  hostItem.click();
  ok(prime.calls.includes('host'), 'Host routes for a Prime account');
  prime.handle.unmount();

  const free = await mountHome({ you: { canHost: false, canJoin: false }, tables: rows });
  const fj = findAll(free.container, 'gg-ot-join');
  ok(fj.every((b) => b._classes.has('is-locked')), 'a free account sees the rows with locked buttons');
  fj[0].click();
  await tick();
  ok(free.primes.length === 1 && !free.calls.some((c) => c.startsWith('joinStart')), 'a locked Join opens the Prime sheet and joins nothing');
  const fh = findAll(free.container, 'gg-menu-item');
  ok(fh[0]._classes.has('is-locked') && fh[0].getAttribute('aria-disabled') === 'true', 'Host wears the lock');
  fh[0].click();
  ok(free.primes.length === 2 && !free.calls.includes('host'), 'and opens the Prime sheet');
  fh[1].click();
  ok(free.calls.includes('practice'), 'Practice is never locked, on any account');
  ok(!fh[1]._classes.has('is-locked'), 'and never wears a lock');
  free.handle.unmount();

  const race = await mountHome({
    you: { canHost: true, canJoin: true }, tables: rows,
    join: () => ({ ok: false, error: { kind: 'already_joined' } }),
  });
  findAll(race.container, 'gg-ot-join')[1].click();
  await tick(); await tick(); await tick();
  const after = findAll(race.container, 'gg-ot-row');
  const takenRow = after.find((r) => r.dataset.code === 'ANYO01');
  ok(takenRow && takenRow._classes.has('is-gone'), 'a race lost greys the row');
  ok(/Taken/.test(takenRow.textContent), 'and its button reads Taken');
  ok(race.toasts[0] === S.tables.takenToast, 'with the toast');
  race.handle.unmount();

  const quietNight = await mountHome({ you: { canHost: true, canJoin: true }, tables: [] });
  const empty = findAll(quietNight.container, 'gg-ot-empty')[0];
  ok(empty && /12 minutes ago/.test(empty.textContent), 'the quiet night says when the last table opened', empty && empty.textContent);
  quietNight.handle.unmount();
}

if (failures) {
  console.error(`\nselftest-opentables: ${n - failures}/${n} checks passed`);
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
console.log(`selftest-opentables: ${n}/${n} checks passed`);
