// Self-contained sanity pass over the HOST GATE and the FREE ANONYMOUS JOIN (2026-08-04).
//
//   node Resources/web/goon/test/selftest-hostgate.js
//
// One server change, two halves of one product decision: minting a room became a TIER-2 perk
// (403 `no_host_access`), and in exchange joining one became free for absolutely everyone —
// including a person with no account at all, who plays on a server-minted `g_` seat identity.
// The weekly free pass that used to sit between the two is gone.
//
// What is asserted, and why each one is here:
//
//   1. THE ABSENCE OF A FIELD IS A REQUEST. An anonymous /join must omit `unified_id` entirely,
//      not send it empty: the server reads the missing key as "mint me one" and reads an empty
//      string as a 400. This is the single easiest thing to break with a well-meaning `|| ''`,
//      and the symptom — every invite link dying at the door — has no other cause that looks
//      like it.
//   2. THE SEAT ID IS ADOPTED, NOT MERELY RECEIVED. `guest_id` comes back once, on the /join
//      response, and every later room-scoped call must present it as `unified_id`. A client that
//      parsed it and kept sending its old id would connect, sit in the room, and be told the seat
//      is somebody else's the moment anything re-checked.
//   3. THE SEAT SURVIVES A RELOAD. A guest id is also the reclaim key (there is no account token
//      behind it), so it is persisted and re-presented for ITS room — and only for its room,
//      because a guest id carried between rooms would be a durable handle on somebody who never
//      signed up for one.
//   4. THE REFUSAL HAS A SENTENCE. 403 `no_host_access` is a product message, not a fault. It
//      must not fold into `unauthorized` ("reconnect your account" to somebody whose account is
//      connected fine) and must not fall through to the network sheet ("check your connection"
//      about a server that answered instantly).
//   5. THE MENU TELLS THE TRUTH EARLY, BUT ONLY WHERE IT CAN. Hosted, the C# host already knows
//      the tier, so Host dims with the reason under it. Standalone, entitlement is unknowable
//      before a round-trip, so Host stays live and the 403 sheet catches it — a page that dimmed
//      on a guess would lock a paying supporter out of what they paid for.
//   6. BOTH CLIENTS AGREE. The C# reference client and its fake are the written contract; if they
//      and the JS binding disagree about the new rules, one of them is a bug.

import { GoonSignalingClient, GoonSignalError, isGuestId, normalizeCode } from '../net/signaling.js';
import { GoonFakeSignalingServer, isGuestUid } from '../net/fakeSignaling.js';
import { GoonSession } from '../net/session.js';
import { standaloneInit } from '../bridge.js';
import { S } from '../ui/strings.js';

const fs = await import('node:fs');
const path = await import('node:path');
const urlMod = await import('node:url');
const HERE = path.dirname(urlMod.fileURLToPath(import.meta.url));
const ROOT = path.join(HERE, '..');
// LF-normalized: the worktree is CRLF (core.autocrlf) and every pin below is written against \n.
const read = (rel) => fs.readFileSync(path.join(ROOT, rel), 'utf8').replace(/\r\n/g, '\n');
const readApp = (...rel) => fs.readFileSync(path.join(ROOT, '..', '..', '..', ...rel), 'utf8').replace(/\r\n/g, '\n');

// ------------------------------------------------------------------ harness

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, debug() {}, log() {} };

/** Wraps a fake server's post so a test can see EXACTLY what went on the wire, opts and all. */
function recorder(server) {
  const calls = [];
  return {
    calls,
    post: (p, body, opts) => { calls.push({ path: p, body, opts }); return server.post(p, body); },
    last: (suffix) => [...calls].reverse().find((c) => String(c.path).endsWith(suffix)) || null,
  };
}

// ========================================================== 1. the error vocabulary

{
  ok(GoonSignalError.NoHostAccess === 'no_host_access', 'GoonSignalError carries no_host_access');
  ok(GoonSignalError.NoPass === 'no_pass',
    'and KEEPS no_pass — retired server-side, still parsed so an old server gets a sentence');

  const stub = (status, body) => new GoonSignalingClient({
    post: () => Promise.resolve({ status, body }),
    unifiedId: 'u_someone', logger: quiet,
  });

  let c = stub(403, '{"error":"no_host_access"}');
  ok((await c.createInvite('A')) === null, '403 fails the invite');
  ok(c.lastError === GoonSignalError.NoHostAccess, '403 no_host_access maps to itself, NOT unauthorized');
  ok(c.lastErrorInfo.kind === 'no_host_access', 'and reaches the UI through lastErrorInfo');

  // A bare 403 is still "signed out": only the body can say it is an entitlement answer.
  c = stub(403, '');
  await c.createInvite('A');
  ok(c.lastError === GoonSignalError.Unauthorized, 'a BODYLESS 403 still reads as unauthorized');

  c = stub(402, '{"error":"no_pass","next_pass_utc":"2026-08-10T00:00:00Z"}');
  await c.createInvite('A');
  ok(c.lastError === GoonSignalError.NoPass && c.lastErrorDetail === '2026-08-10T00:00:00Z',
    'the retired 402 still parses whole — old-server tolerance is not optional');
}

// ================================================ 2. the fake server models the gate

{
  const server = new GoonFakeSignalingServer({ hostGate: true });
  server.setLabAccess('u_tier2');

  const poor = new GoonSignalingClient({ post: server.post, unifiedId: 'u_tier1', logger: quiet });
  ok((await poor.createInvite('Tier1')) === null, 'the fake refuses a room to a non-tier-2 uid');
  ok(poor.lastError === GoonSignalError.NoHostAccess, 'with the real server\'s 403 no_host_access');
  ok(server.roomCount === 0, 'and mints nothing');

  const rich = new GoonSignalingClient({ post: server.post, unifiedId: 'u_tier2', logger: quiet });
  const inv = await rich.createInvite('Tier2');
  ok(!!inv && !!inv.code, 'tier 2 mints a room');
  ok(inv.pass === 'premium', 'and /invite keeps the legacy `pass` shape (nothing is charged)');

  // Whitelist folds to permanent tier 2, exactly as computeEffectiveTier does it server-side.
  const wl = new GoonFakeSignalingServer({ hostGate: true, whitelist: ['u_owner'] });
  const owner = new GoonSignalingClient({ post: wl.post, unifiedId: 'u_owner', logger: quiet });
  ok(!!(await owner.createInvite('Owner')), 'a whitelisted uid hosts without being named tier 2');

  // The default is OFF so the room-lifecycle suites are not all about entitlement.
  const open = new GoonFakeSignalingServer();
  const anyone = new GoonSignalingClient({ post: open.post, unifiedId: 'u_nobody', logger: quiet });
  ok(!!(await anyone.createInvite('Nobody')), 'hostGate defaults off — the gate is opt-in for tests');
}

// ============================================ 3. JOINING IS FREE, AND MAY BE ANONYMOUS

{
  const server = new GoonFakeSignalingServer({ hostGate: true, labAccess: ['u_host'] });
  const host = new GoonSignalingClient({ post: server.post, unifiedId: 'u_host', logger: quiet });
  const inv = await host.createInvite('Host');

  const rec = recorder(server);
  const seats = [];
  const guest = new GoonSignalingClient({
    post: rec.post, logger: quiet,
    anonymous: true,
    onGuest: (id, code) => seats.push({ id, code }),
  });
  ok(guest.unifiedId === '', 'an anonymous client starts with no identity at all');

  const j = await guest.join(inv.code, 'Nobody');
  ok(!!j, 'a uid-less join is accepted — joining is free, account or not');

  // --- 1. the absence of a field IS the request -------------------------------
  const joinCall = rec.last('/join');
  ok(!!joinCall && !('unified_id' in joinCall.body),
    'the /join body OMITS unified_id entirely — an empty string is a 400, not a request');
  ok(joinCall.opts && joinCall.opts.noAuth === true,
    'and goes out with no X-Auth-Token: a guest has no account token to send');

  // --- 2. the seat id is adopted, not merely received -------------------------
  ok(isGuestId(j.guestId) && isGuestUid(j.guestId), 'the response carries a well-formed g_ seat id');
  ok(/^g_[a-f0-9]{16}$/.test(j.guestId), 'g_ + 16 hex, byte for byte the documented shape');
  ok(guest.unifiedId === j.guestId, 'the client ADOPTS it as its unified_id');
  ok(guest.guestId === j.guestId && guest.guestRoom === normalizeCode(inv.code),
    'and records which room the seat belongs to');
  ok(j.pass === 'free', 'the advisory pass label is "free" — nothing was charged');
  ok(seats.length === 1 && seats[0].id === j.guestId && seats[0].code === normalizeCode(inv.code),
    'the persistence sink is handed the id AND the room');

  // Every later room-scoped call must present the seat id, or the room stops believing us.
  await guest.signal(inv.code, j.token, 'guest', 0, [{ kind: 'offer', data: '{}' }]);
  ok(rec.last('/signal').body.unified_id === j.guestId, '/signal presents the g_ id');
  ok(rec.last('/signal').opts.noAuth === true, '/signal still sends no auth header');
  await guest.relay(inv.code, j.token, 'guest', 0, ['{}'], 100);
  ok(rec.last('/relay').body.unified_id === j.guestId, '/relay presents the g_ id');
  await guest.leave(inv.code, j.token, 'guest');
  ok(rec.last('/leave').body.unified_id === j.guestId, '/leave presents the g_ id');

  // --- an ACCOUNT join is untouched by any of it ------------------------------
  const rec2 = recorder(server);
  const acct = new GoonSignalingClient({ post: rec2.post, unifiedId: 'u_member', logger: quiet });
  const inv2 = await host.createInvite('Host');
  const j2 = await acct.join(inv2.code, 'Member');
  ok(!!j2 && rec2.last('/join').body.unified_id === 'u_member', 'an account join still sends its uid');
  ok(rec2.last('/join').opts === undefined, 'and still sends its auth header');
  ok(j2.guestId === '' && acct.unifiedId === 'u_member', 'and is handed no guest_id to adopt');
}

// =================================================== 4. THE SEAT SURVIVES A RELOAD

{
  const server = new GoonFakeSignalingServer();
  const host = new GoonSignalingClient({ post: server.post, unifiedId: 'u_host', logger: quiet });
  const inv = await host.createInvite('Host');

  const first = new GoonSignalingClient({ post: server.post, logger: quiet, anonymous: true });
  const j1 = await first.join(inv.code);
  ok(!!j1 && !!j1.guestId, 'a guest takes the seat');

  // THE RELOAD. A brand new client, constructed from what was persisted.
  const rec = recorder(server);
  const reloaded = new GoonSignalingClient({
    post: rec.post, logger: quiet, anonymous: true,
    guestId: j1.guestId, guestRoom: inv.code,
  });
  const j2 = await reloaded.join(inv.code);
  ok(!!j2, 'the reload gets back in — not "that room already has two players", by its own ghost');
  ok(rec.last('/join').body.unified_id === j1.guestId, 'by RE-PRESENTING the stored g_ id');
  ok(j2.rejoin === true && j2.pass === 'rejoin', 'the server reports a reclaim, not a fresh claim');
  ok(j2.guestId === j1.guestId, 'the seat id is confirmed back, so the client can trust what it kept');
  ok(j2.token !== j1.token, 'with a fresh room token');

  // …but ONLY for its own room. A guest id carried between rooms would be a durable handle on
  // somebody who never signed up for one.
  const other = await host.createInvite('Host2');
  const rec2 = recorder(server);
  const elsewhere = new GoonSignalingClient({
    post: rec2.post, logger: quiet, anonymous: true,
    guestId: j1.guestId, guestRoom: inv.code,
  });
  const j3 = await elsewhere.join(other.code);
  ok(!!j3 && !('unified_id' in rec2.last('/join').body),
    'a DIFFERENT room omits the stored id and takes a fresh identity');
  ok(j3.guestId && j3.guestId !== j1.guestId, 'which the server duly mints');

  // A malformed stored id is not an identity — it must never reach the wire.
  const rec3 = recorder(server);
  const junk = new GoonSignalingClient({
    post: rec3.post, logger: quiet, anonymous: true,
    guestId: 'u_pretending', guestRoom: inv.code,
  });
  ok(junk.guestId === '', 'a non-g_ stored id is discarded at construction');
  await junk.join(await host.createInvite('H3').then((i) => i.code));
  ok(!('unified_id' in rec3.last('/join').body), 'and never gets presented');
}

// ================================================= 5. the session wires the two together

{
  const noop = () => ({ dispose() {}, cancelMatch() {} });
  const saved = [];
  const seat = { id: '', code: '', save(id, code) { this.id = id; this.code = code; saved.push([id, code]); } };
  const s = new GoonSession({
    createMatch: noop,
    identity: { unifiedId: 'local-Solo', displayName: 'Solo', appVersion: 'dev', anonymous: true },
    guest: seat,
    logger: quiet,
  });
  const sig = s._createSignaling();
  ok(sig.anonymous === true, 'GoonSession passes identity.anonymous down to the signaling client');
  ok(typeof sig._onGuest === 'function', 'and wires the persistence sink');
  sig._adoptGuestId({ guest_id: 'g_0123456789abcdef' }, 'ABC123');
  ok(seat.id === 'g_0123456789abcdef' && seat.code === 'ABC123',
    'a minted seat is written back into the live store, so the NEXT client reclaims it');

  // An account page must not accidentally go anonymous.
  const s2 = new GoonSession({
    createMatch: noop,
    identity: { unifiedId: 'u_member', displayName: 'M', appVersion: 'dev' },
    logger: quiet,
  });
  ok(s2._createSignaling().anonymous === false, 'no `anonymous` on the identity = an account page');
}

// ============================================ 6. bridge: who counts as having an account

{
  const before = standaloneInit();
  ok(before.identity.anonymous === true,
    'a bare standalone launch is ANONYMOUS — a `local-…` fallback id is not an account');
  ok(/^local-/.test(before.identity.unifiedId), 'and still carries the throwaway local id for display');

  globalThis.location = { search: '?uid=u_realaccount&name=Phone' };
  try {
    const withUid = standaloneInit();
    ok(withUid.identity.anonymous === false, '?uid= is a real account — not anonymous');
    ok(withUid.identity.unifiedId === 'u_realaccount', 'and is used verbatim');
  } finally { delete globalThis.location; }

  const src = read('bridge.js');
  ok(/const account = String\(q\.get\('uid'\) \|\| prefs\.unifiedId \|\| ''\);/.test(src),
    'a uid adopted into prefs by an earlier launch counts as an account too');
  ok(/if \(netCfg\.authToken && !noAuth\) headers\['X-Auth-Token'\]/.test(src),
    'postNet honours noAuth on the direct-fetch path');
}

// ===================================================== 7. the sheet, and the retired copy

{
  ok(!!S.sheets.noHostAccess && typeof S.sheets.noHostAccess.line === 'string',
    'there is a no_host_access sheet, and its line is a plain string (no pass date to interpolate)');
  ok(/supporter perk/i.test(S.sheets.noHostAccess.headline + ' ' + S.sheets.noHostAccess.line),
    'it names the perk');
  ok(/free/i.test(S.sheets.noHostAccess.line),
    'and says what is still free, rather than stopping at "no"');
  ok(S.sheets.noPass === undefined, 'the weekly-pass copy is retired — the server never sends it');
  ok(typeof S.title.hostNoLab === 'string' && S.title.hostNoLab === S.title.hostNoLab.toLowerCase(),
    'the menu note is lowercase, same voice as lobby.transferNoPremium');

  const sheets = read('ui/sheets.js');
  ok(/case 'no_host_access':\s*\n\s*case 'no_pass':/.test(sheets),
    'showSignalError maps BOTH the new refusal and the retired one to that sheet');
}

// ============================================== 8. the title menu, actually mounted

function installDom() {
  function makeNode(tagName) {
    const kids = [];
    const map = new Map();
    const classes = new Set();
    const attrs = new Map();
    const node = {
      tagName: String(tagName || 'div').toUpperCase(),
      nodeType: 1,
      children: kids,
      childNodes: kids,
      parentNode: null,
      isConnected: true,
      hidden: false,
      style: { setProperty() {}, removeProperty() {} },
      dataset: {},
      value: '',
      disabled: false,
      textContent: '',
      title: '',
      get className() { return Array.from(classes).join(' '); },
      set className(v) { classes.clear(); String(v || '').split(/\s+/).filter(Boolean).forEach((c) => classes.add(c)); },
      classList: {
        add: (...c) => c.forEach((x) => classes.add(x)),
        remove: (...c) => c.forEach((x) => classes.delete(x)),
        toggle: (c, on) => (on ? classes.add(c) : classes.delete(c)),
        contains: (c) => classes.has(c),
      },
      appendChild(child) { if (child) { child.parentNode = node; kids.push(child); } return child; },
      append(...c) { c.forEach((x) => node.appendChild(x)); },
      prepend(child) { if (child) { child.parentNode = node; kids.unshift(child); } return child; },
      removeChild(child) { const i = kids.indexOf(child); if (i >= 0) kids.splice(i, 1); return child; },
      remove() { if (node.parentNode) node.parentNode.removeChild(node); node.parentNode = null; node.isConnected = false; },
      replaceChildren(...c) { kids.length = 0; c.forEach((x) => node.appendChild(x)); },
      contains(other) {
        if (other === node) return true;
        for (const k of kids) if (k && typeof k.contains === 'function' && k.contains(other)) return true;
        return false;
      },
      setAttribute(k, v) { attrs.set(k, String(v)); if (k === 'class') node.className = String(v); },
      getAttribute(k) { return attrs.has(k) ? attrs.get(k) : null; },
      removeAttribute(k) { attrs.delete(k); },
      hasAttribute(k) { return attrs.has(k); },
      addEventListener(type, fn) { if (!map.has(type)) map.set(type, new Set()); map.get(type).add(fn); },
      removeEventListener(type, fn) { const s = map.get(type); if (s) s.delete(fn); },
      dispatchEvent(evt) {
        const s = map.get(evt && evt.type);
        if (s) for (const fn of Array.from(s)) { try { fn(evt); } catch (_e) { /* ignore */ } }
        return true;
      },
      click() { node.dispatchEvent({ type: 'click', preventDefault() {}, target: node }); },
      focus() {}, blur() {},
      _classes: classes,
    };
    return node;
  }
  const doc = makeNode('#document');
  doc.documentElement = makeNode('html');
  doc.body = makeNode('body');
  doc.activeElement = null;
  doc.createElement = (tag) => makeNode(tag);
  doc.createElementNS = (_ns, tag) => makeNode(tag);
  doc.createTextNode = (t) => { const x = makeNode('#text'); x.textContent = String(t); return x; };
  doc.getElementById = () => null;
  globalThis.document = doc;
  globalThis.window = makeNode('window');
  return { doc, makeNode };
}
const dom = installDom();

// Imported AFTER the DOM exists — a screen module touches document at mount, not at import,
// but the router it pulls in is the one thing that must find a document to build nodes with.
const title = await import('../ui/screens/title.js');

function findAll(root, className) {
  const out = [];
  (function walk(node) {
    if (!node) return;
    if (node._classes && node._classes.has(className)) out.push(node);
    for (const kid of node.children || []) walk(kid);
  })(root);
  return out;
}

/** Mount the title screen against a session shape and hand back the Host menu item. */
function mountTitle(sessionShape) {
  const container = dom.makeNode('div');
  const clicks = [];
  const handle = title.mount(container, {
    session: sessionShape,
    actions: {
      goHost: () => clicks.push('host'), goJoin: () => clicks.push('join'),
      goPractice: () => clicks.push('practice'), goAssets: () => {}, goVoice: () => {},
      quit: () => {},
    },
    logger: quiet,
  });
  const items = findAll(container, 'gg-menu-item');
  return { handle, container, clicks, items, host: items[0] || null };
}

{
  // HOSTED + refused: visible, dimmed, and carrying the reason.
  const m = mountTitle({ hosted: true, caps: { canHost: false } });
  ok(!!m.host, 'the Host item is still RENDERED when hosting is refused — a missing row reads as broken');
  ok(m.host.disabled === true, 'but disabled');
  ok(m.host._classes.has('is-disabled'), 'and dimmed with the lobby-row class');
  ok(m.host.getAttribute('aria-disabled') === 'true', 'and announced as disabled');
  const note = findAll(m.host, 'gg-menu-note')[0];
  ok(!!note && note.textContent === S.title.hostNoLab, 'with the supporter-perk note under it');
  ok(!m.host._classes.has('gg-btn--primary'), 'and it is not still the primary call to action');
  m.host.click();
  ok(m.clicks.length === 0, 'clicking it routes nowhere — the guard is in the handler, not just the CSS');
  m.handle.unmount();
}

{
  // HOSTED + allowed: untouched.
  const m = mountTitle({ hosted: true, caps: { canHost: true } });
  ok(m.host.disabled !== true && !m.host._classes.has('is-disabled'), 'canHost true leaves Host alone');
  m.host.click();
  ok(m.clicks[0] === 'host', 'and it routes');
  ok(findAll(m.host, 'gg-menu-note').length === 0, 'with no note to explain a gate that is not there');
  m.handle.unmount();
}

{
  // HOSTED by a host that predates the flag: absent is not false.
  const m = mountTitle({ hosted: true, caps: { video: true } });
  ok(m.host.disabled !== true, 'a missing canHost does NOT lock Host — `=== false` on purpose');
  m.handle.unmount();
  const m2 = mountTitle({ hosted: true, caps: null });
  ok(m2.host.disabled !== true, 'and neither does a caps-less init frame');
  m2.handle.unmount();
}

{
  // STANDALONE: entitlement is unknowable before a round-trip, so never guess.
  const m = mountTitle({ hosted: false, caps: { canHost: false } });
  ok(m.host.disabled !== true,
    'standalone leaves Host live even against canHost:false — the 403 sheet is the fallback');
  m.host.click();
  ok(m.clicks[0] === 'host', 'and it still routes, so a supporter is never locked out on a guess');
  m.handle.unmount();
}

// ==================================================== 9. the C# half says the same thing

{
  const patreon = readApp('Services', 'Account', 'PatreonService.cs');
  ok(/public bool HasLabAccess =>/.test(patreon), 'PatreonService exposes HasLabAccess');
  ok(/HasLabAccess =>[\s\S]{0,200}CurrentTier >= PatreonTier\.Level2/.test(patreon),
    'and it is TIER 2, not the tier-1 bar HasPremiumAccess uses');
  ok(/HasLabAccess =>[\s\S]{0,300}IsWhitelisted/.test(patreon), 'with the whitelist folded in');
  ok(/HasLabAccess =>[\s\S]{0,300}SubscribeStar/.test(patreon),
    'and SubscribeStar OR\'d in, the way the server folds substar_tier');
  ok(/computeEffectiveTier\(user\) &gt;= 2/.test(patreon),
    'documented against the server comparison it mirrors');
  ok(!/HasLabAccess =>[\s\S]{0,300}HasCachedPremiumAccess/.test(patreon),
    'and NOT off the 2-week grace cache, which caches a tier-1 entitlement');

  const hostSvc = readApp('Services', 'GoonGame', 'GoonHostService.cs');
  ok(/canHost = HostingAllowed\(\),/.test(hostSvc), 'the init frame carries caps.canHost');
  ok(/mediaTransfer = TransferAllowed\(\),\s*\n\s*canHost = HostingAllowed\(\),/.test(hostSvc),
    'right beside the transfer verdict it is a rung above');
  ok(/HostingAllowed\(\)\s*\n?\s*\{[\s\S]{0,160}App\.Patreon\?\.HasLabAccess == true;[\s\S]{0,80}catch \{ return false; \}/.test(hostSvc),
    'computed from HasLabAccess, defaulting false in the same try/catch style as TransferAllowed');

  const cs = readApp('Services', 'GoonGame', 'GoonSignalingClient.cs');
  ok(/ErrorNoHostAccess = "no_host_access"/.test(cs), 'the C# client knows no_host_access');
  ok(/case 403:\s*\n(\s*\/\/.*\n)*\s*LastError = serverError \?\? ErrorUnauthorized;/.test(cs),
    '403 triage is its own arm, so the body can name the entitlement answer');
  ok(/no_host_access/.test(cs.slice(0, cs.indexOf('POST /v2/goon/signal'))),
    'and the header contract documents the tier-2 invite rule');
  ok(/JOINING IS FREE/.test(cs), 'and that joining is free for everyone');
  ok(/guest_id/.test(cs), 'and the anonymous-guest flow, even though C# never takes it');
  ok(/no_pass is retired|Retired 2026-08-04|Retired: the server no longer charges/.test(cs),
    'with the weekly pass marked retired rather than silently deleted');

  // The C# fake is the executable half of that contract.
  ok(/public bool HostGate \{ get; set; \}/.test(cs), 'GoonFakeSignalingServer models the host gate');
  ok(/if \(HostGate && !_labAccess\.Contains[\s\S]{0,140}ErrorNoHostAccess\)/.test(cs),
    'refusing /invite below tier 2 with 403 no_host_access');
  ok(/\["pass"\] = "free"/.test(cs), 'and never charging for a join');
  ok(/DOCUMENTED GAP/.test(cs), 'with the un-ported seat-identity model called out rather than implied');
}

// ============================================================ 10. the protocol doc

{
  const doc = readApp('docs', 'GOON_GAME_PROTOCOL.md');
  ok(/no_host_access/.test(doc), 'GOON_GAME_PROTOCOL.md documents the 403');
  ok(/guest_id/.test(doc), 'and the anonymous guest identity');
  ok(/`g_`|g_<|`g_\+/.test(doc), 'and names the g_ shape');
  ok(/"free"\s*\|\s*"rejoin"\s*\|\s*"self_duel"|free.*rejoin.*self_duel/.test(doc),
    'and the new pass vocabulary');
  ok(/canHost/.test(doc), 'and the caps.canHost the host answers with');
}

console.log(failures === 0
  ? `selftest-hostgate: ${n}/${n} checks passed`
  : `selftest-hostgate: ${n - failures}/${n} checks passed\n${failures} FAILURE(S)`);
process.exitCode = failures === 0 ? 0 : 1;
