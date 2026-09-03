// Self-contained sanity pass over the SHAREABLE INVITE — the `?join=` deep link,
// the first-run media step it drops a new player into, and the `media_prep`
// frame that tells the host somebody is in there picking files.
//
//   node Resources/web/goon/test/selftest-invite.js
//
// The three pieces are one feature and they fail as one: a link that joins a
// room and then leaves the joiner staring at a lobby with an empty deck is not a
// better invite than typing six characters, and a host who cannot tell "nobody
// came" from "they are busy" gives up on the duel either way.
//
// What is asserted, and why each one is here:
//
//   1. IMPORT SWEEP. There are no devtools inside WebView2 — a module that
//      throws at import does not error, the loader simply never settles and the
//      player watches a spinner forever. Every new module is imported first.
//   2. THE LINK IS TOLERANT AND THE STRIP IS NOT OPTIONAL. `?join=` survives a
//      chat client lowercasing it, a helpful hyphen, %20 padding and a line
//      wrap; and it is REMOVED from the address bar the moment it is read,
//      because a link left in place is re-joined by every refresh, every back
//      button and every home-screen pin, forever, against a room whose TTL is
//      five minutes.
//   3. THE HOSTED LINK POINTS SOMEWHERE REAL. In WebView2 the page is served
//      from the virtual host https://ccp.game/, which resolves on exactly one
//      machine; copying that URL would hand the other player a dead link, so the
//      hosted case MUST fall back to the public deployment constant.
//   4. THE ONBOARDING BRANCH IS A BRANCH. Empty deck -> the screen; any deck at
//      all -> straight past it; and the confirm unlocks on the FIRST file, not
//      on the twentieth (the twenty is copy, not a gate).
//   5. `media_prep` IS FORWARD-COMPATIBLE BY CONSTRUCTION. It is an append-only
//      message on an unchanged protocol version: a peer that never heard of it
//      drops the frame as an unknown `t` (wire.js parse -> null + log) and reads
//      as "not preparing", which is the state that changes nothing.
//   6. EVERY STRING THE SCREENS READ EXISTS. An undefined string renders the
//      word "undefined" at the player.

// ------------------------------------------------------------------ DOM stub
//
// The same shape selftest-assets.js carries, trimmed to what one card needs.
// Anything absent is absent ON PURPOSE: the screen must already degrade without
// it, and a throw here means a guard is missing.

function installDom() {
  function makeStyle() {
    const s = {};
    s.setProperty = (k, v) => { s[k] = v; };
    s.removeProperty = (k) => { delete s[k]; };
    return s;
  }

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
      style: makeStyle(),
      dataset: {},
      value: '',
      disabled: false,
      textContent: '',
      title: '',
      files: null,
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
      _listeners: map,
      _classes: classes,
      _attrs: attrs,
    };
    return node;
  }

  const doc = makeNode('#document');
  doc.documentElement = makeNode('html');
  doc.body = makeNode('body');
  doc.activeElement = null;
  const byId = new Map();
  for (const id of ['gg-modal', 'gg-drawer', 'scr-media-setup', 'scr-title', 'scr-join']) {
    const n = makeNode('div');
    n.id = id;
    n.hidden = true;
    byId.set(id, n);
    doc.body.appendChild(n);
  }
  doc.createElement = (tag) => makeNode(tag);
  doc.createElementNS = (_ns, tag) => makeNode(tag);
  doc.createTextNode = (t) => { const n = makeNode('#text'); n.textContent = String(t); return n; };
  doc.getElementById = (id) => byId.get(id) || null;

  const win = makeNode('window');
  win.innerWidth = 1280;
  win.innerHeight = 720;

  globalThis.document = doc;
  globalThis.window = win;
  return { doc, win, byId, makeNode };
}

const dom = installDom();

// ------------------------------------------------------------------ harness

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, debug() {}, log() {} };
const tick = (ms) => new Promise((r) => setTimeout(r, ms));

function findAll(root, className) {
  const out = [];
  (function walk(node) {
    if (!node) return;
    if (node._classes && node._classes.has(className)) out.push(node);
    for (const kid of node.children || []) walk(kid);
  })(root);
  return out;
}
const findOne = (root, className) => findAll(root, className)[0] || null;
function findByText(root, text) {
  const out = [];
  (function walk(node) {
    if (!node) return;
    if (node.textContent === text) out.push(node);
    for (const kid of node.children || []) walk(kid);
  })(root);
  return out;
}
const findTag = (root, tag) => {
  const out = [];
  (function walk(node) {
    if (!node) return;
    if (node.tagName === String(tag).toUpperCase()) out.push(node);
    for (const kid of node.children || []) walk(kid);
  })(root);
  return out;
};

const fs = await import('node:fs');
const path = await import('node:path');
const urlMod = await import('node:url');
const HERE = path.dirname(urlMod.fileURLToPath(import.meta.url));
const ROOT = path.join(HERE, '..');
// LF-normalized: the worktree is CRLF (core.autocrlf) and every pin below is
// written against \n.
const read = (rel) => fs.readFileSync(path.join(ROOT, rel), 'utf8').replace(/\r\n/g, '\n');

/** Strip // and block comments so "is this code or a note?" is answerable. */
function stripComments(src) {
  return String(src)
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:])\/\/[^\n]*/g, '$1');
}

// =========================================================== 1. import sweep
let inviteLink = null;
let mediaSetup = null;
let contracts = null;
let wire = null;
let mediaMod = null;
let strings = null;
let routerMod = null;
{
  const modules = [
    ['ui/inviteLink.js', (m) => { inviteLink = m; }],
    ['ui/screens/mediaSetup.js', (m) => { mediaSetup = m; }],
    ['ui/screens/host.js', () => {}],
    ['ui/screens/join.js', () => {}],
    ['ui/screens/lobby.js', () => {}],
    ['ui/router.js', (m) => { routerMod = m; }],
    ['ui/strings.js', (m) => { strings = m; }],
    ['core/contracts.js', (m) => { contracts = m; }],
    ['core/wire.js', (m) => { wire = m; }],
    ['exec/media.js', (m) => { mediaMod = m; }],
  ];
  for (const [rel, assign] of modules) {
    let mod = null;
    let threw = '';
    try { mod = await import('../' + rel); } catch (e) { threw = (e && e.message) || String(e); }
    ok(!!mod, 'imports clean under node: ' + rel, threw);
    if (mod) assign(mod);
  }
}

// ====================================================== 2. the link, as a value
{
  const { buildInviteUrl, readJoinCode, isCompleteCode, JOIN_PARAM, GOON_PUBLIC_URL, CODE_LEN } = inviteLink;

  ok(JOIN_PARAM === 'join', 'the querystring key is `join`', JOIN_PARAM);
  ok(CODE_LEN === 6, 'the code length matches the join screen', String(CODE_LEN));

  // --- reading it. Every one of these is a real thing that happens to a URL on
  // its way through a chat client, a QR scan or a paste into a notes app.
  const cases = [
    ['?join=ABC123', 'ABC123', 'the plain case'],
    ['?join=abc123', 'ABC123', 'lowercased by a chat client'],
    ['?join=AbC123', 'ABC123', 'mixed case'],
    ['?join=ABC-123', 'ABC123', 'a helpful hyphen'],
    ['?join=abc-123', 'ABC123', 'lowercased AND hyphenated'],
    ['?join=%20ABC123%20', 'ABC123', 'padded with %20 by a line wrap'],
    ['?join=AB C123', 'ABC123', 'a space in the middle'],
    ['?join=A-B-C-1-2-3', 'ABC123', 'hyphens throughout'],
    ['join=ABC123', 'ABC123', 'no leading question mark'],
    ['?server=https://x&join=abc123&token=t', 'ABC123', 'alongside the phone client credentials'],
    ['?join=ABC123EXTRA', 'ABC123', 'clamped to six characters, never sent longer'],
    ['?join=', '', 'an empty value is no link'],
    ['?join=   ', '', 'whitespace only is no link'],
    ['?solo=1', '', 'no join param at all'],
    ['', '', 'no querystring at all'],
  ];
  for (const [search, want, why] of cases) {
    const got = readJoinCode(search);
    ok(got === want, 'readJoinCode — ' + why, JSON.stringify(search) + ' -> ' + JSON.stringify(got));
  }

  // Deliberately NOT folded: Crockford's I/L->1 and O->0. The server mints the
  // alphabet, and a client that silently rewrote a character would turn "you
  // typed it wrong" into "that room does not exist" (ui/screens/join.js).
  ok(readJoinCode('?join=IL0O12') === 'IL0O12', 'readJoinCode does NOT fold I/L/O — the server owns the alphabet');

  // Garbage in, nothing out — and above all, nothing thrown.
  for (const junk of [null, undefined, 0, {}, '?join']) {
    let threw = false;
    let got = 'x';
    try { got = readJoinCode(junk); } catch (_e) { threw = true; }
    ok(!threw && got === '', 'readJoinCode survives ' + String(junk), got);
  }

  ok(isCompleteCode('abc123') === true, 'isCompleteCode: six characters is submittable');
  ok(isCompleteCode('abc12') === false, 'isCompleteCode: five is not');
  ok(isCompleteCode('') === false, 'isCompleteCode: nothing is not');
  ok(isCompleteCode('a-b-c-1-2-3') === true, 'isCompleteCode normalizes before counting');

  // --- building it, standalone: whatever host the player actually reached us on,
  // including a LAN address during a play-test.
  const local = buildInviteUrl('abc123', {
    hosted: false, origin: 'https://cclabs.app', pathname: '/goon-beta/',
  });
  ok(local === 'https://cclabs.app/goon-beta/?join=ABC123', 'standalone link uses origin + pathname', local);

  const lan = buildInviteUrl('ABC123', { hosted: false, origin: 'http://192.168.1.40:8080', pathname: '/goon/index.html' });
  ok(lan === 'http://192.168.1.40:8080/goon/index.html?join=ABC123',
    'a LAN play-test links to the LAN address, filename and all', lan);

  // --- building it, HOSTED. This is the one that matters: inside WebView2 the
  // page is https://ccp.game/goon/index.html, a virtual host that resolves on
  // exactly one machine. Copying it would hand the other player a dead link.
  const hosted = buildInviteUrl('abc123', {
    hosted: true, origin: 'https://ccp.game', pathname: '/goon/index.html',
  });
  ok(hosted.indexOf('ccp.game') < 0, 'the HOSTED link never leaks the WebView2 virtual host', hosted);
  ok(hosted === GOON_PUBLIC_URL + '?join=ABC123', 'the hosted link points at the public deployment', hosted);
  ok(/^https:\/\//.test(GOON_PUBLIC_URL), 'the public constant is an absolute https URL', GOON_PUBLIC_URL);

  // A file: page (or a stub with no location) has no usable origin — fall back
  // rather than build `null?join=…`.
  const filed = buildInviteUrl('abc123', { hosted: false, origin: 'null', pathname: '/x.html' });
  ok(filed === GOON_PUBLIC_URL + '?join=ABC123', 'an unusable origin falls back to the public constant', filed);
  ok(buildInviteUrl('', { hosted: false, origin: 'https://x' }) === '', 'no code, no link');
  ok(buildInviteUrl('abc123') === GOON_PUBLIC_URL + '?join=ABC123', 'no options at all still builds something loadable');

  // A base that already carries a query keeps it.
  const withQuery = buildInviteUrl('abc123', { hosted: false, origin: 'https://x.dev', pathname: '/g/' });
  ok(withQuery.indexOf('?join=') > 0, 'the param is appended with ? when there is no query', withQuery);

  // Round trip: everything we build is something we can read back.
  ok(readJoinCode(new URL(local).search) === 'ABC123', 'a built link reads back as the same code');
  ok(readJoinCode(new URL(hosted).search) === 'ABC123', 'and so does the hosted one');
}

// ================================================ 3. the strip (history.replaceState)
{
  const { stripJoinParam, consumeJoinCode, readJoinCode } = inviteLink;

  /** A window just real enough to hold a URL and remember a replaceState. */
  function fakeWin(href) {
    const w = {
      location: { href, get search() { return new URL(w.location.href).search; } },
      history: {
        calls: [],
        replaceState(_s, _t, url) {
          this.calls.push(url);
          w.location.href = new URL(url, href).href;
        },
      },
    };
    return w;
  }

  {
    const w = fakeWin('https://cclabs.app/goon-beta/?join=ABC123');
    ok(stripJoinParam(w) === true, 'stripJoinParam reports that it removed one');
    ok(w.history.calls.length === 1, 'exactly one replaceState — never a pushState duplicate', String(w.history.calls.length));
    ok(readJoinCode(w.location.search) === '', 'and the code is gone from the address bar', w.location.href);
  }

  {
    // THIS FUNCTION TOUCHES ONE PARAM AND NO OTHER — that is the whole check.
    // The credentials ARE erased, but by bridge.js, which strips `?token=` (and a
    // `?server=` its allowlist refused) on EVERY standalone launch, link or no
    // link. Moving that job in here would only protect players who arrived by
    // invite, which is the wrong half of them.
    const w = fakeWin('https://cclabs.app/goon-beta/?server=https%3A%2F%2Fs.dev&join=ABC123&token=tk&uid=u_abc12345#x');
    stripJoinParam(w);
    const u = new URL(w.location.href);
    ok(u.searchParams.get('join') === null, 'join is removed');
    ok(u.searchParams.get('server') === 'https://s.dev', 'server is left to bridge.js', String(u.searchParams.get('server')));
    ok(u.searchParams.get('token') === 'tk', 'so is the token — one param per owner');
    ok(u.searchParams.get('uid') === 'u_abc12345', 'uid survives');
    ok(u.hash === '#x', 'the fragment survives', u.hash);
  }

  {
    const w = fakeWin('https://cclabs.app/goon-beta/?solo=1');
    ok(stripJoinParam(w) === false, 'nothing to strip is not an error');
    ok(w.history.calls.length === 0, 'and nothing is written to history');
  }

  // A sandboxed frame, a browser that refuses replaceState, no window at all:
  // the link still worked, and re-joining a dead room is survivable. A THROW is
  // not — this runs during boot, before any error seam can render anything.
  for (const bad of [null, undefined, {}, { location: {} }, { location: { href: 'x' }, history: {} }]) {
    let threw = false;
    try { stripJoinParam(bad); } catch (_e) { threw = true; }
    ok(!threw, 'stripJoinParam never throws on ' + JSON.stringify(bad));
  }
  {
    const w = fakeWin('https://x.dev/?join=ABC123');
    w.history.replaceState = () => { throw new Error('denied'); };
    let threw = false;
    let res = true;
    try { res = stripJoinParam(w); } catch (_e) { threw = true; }
    ok(!threw && res === false, 'a browser that refuses replaceState degrades quietly');
  }

  // consumeJoinCode is read + strip in ONE verb, on purpose: two calls would
  // leave a window in which an early failure returns the player to a page still
  // holding the code, and the next reload joins the dead room all over again.
  {
    const w = fakeWin('https://cclabs.app/goon-beta/?join=abc-123');
    const code = consumeJoinCode(w);
    ok(code === 'ABC123', 'consumeJoinCode returns the normalized code', code);
    ok(w.history.calls.length === 1, 'and strips it in the same call');
    ok(consumeJoinCode(w) === '', 'a second read finds nothing — one link, one join');
  }
  {
    const w = fakeWin('https://cclabs.app/goon-beta/');
    ok(consumeJoinCode(w) === '', 'no link, no code');
    ok(w.history.calls.length === 0, 'and no history write');
  }
}

// ================================================== 4. the wiring (source pins)
{
  const boot = stripComments(read('boot.js'));
  const html = read('index.html');
  const host = stripComments(read('ui/screens/host.js'));
  const join = stripComments(read('ui/screens/join.js'));
  const lobby = stripComments(read('ui/screens/lobby.js'));

  // --- the deep link reaches the join screen
  ok(/from '\.\/ui\/inviteLink\.js'/.test(boot), 'boot.js imports ui/inviteLink.js');
  ok(/consumeJoinCode\(/.test(boot), 'boot.js consumes (reads AND strips) the join param');
  ok(/router\.show\('join',\s*\{\s*autoCode/.test(boot), 'boot.js routes a linked code straight to the join screen');
  ok(/function openFirstScreen\(/.test(boot), 'boot.js has ONE first-screen decision, not a scattered one');
  {
    const b = boot.slice(boot.indexOf('function openFirstScreen('));
    const body = b.slice(0, b.indexOf('\n}\n') + 1);
    ok(/router\.show\('title'\)/.test(body), 'and it still lands on the title when there is no link');
  }
  ok(!/router\.show\('title'\);\n      bridge\.log\('boot ok'\)/.test(boot),
    'settle() no longer hard-codes the title — it asks openFirstScreen');

  // --- the join screen actually submits it
  ok(/ctx\?\.autoCode/.test(join), 'join.js reads ctx.autoCode');
  ok(/autoCode\.length === CODE_LEN/.test(join), 'join.js only auto-submits a COMPLETE code');
  ok(/const remembered = autoCode \|\| prefs\?\.get\?\.\('lastCode'\)/.test(join),
    "a link's code beats the remembered one — the player asked for THIS room");
  // The failure path is the whole reason this screen still exists for a linked
  // joiner: the code stays in the field and the reason goes on screen.
  ok(/case 'unknown_code': return S\.join\.errUnknown/.test(join), 'unknown_code still renders inline, code intact');
  ok(/case 'expired': return S\.join\.errExpired/.test(join), 'expired still renders inline, code intact');

  // --- the host screen offers the link
  ok(/from '\.\.\/inviteLink\.js'/.test(host), 'host.js imports the link builder');
  ok(/buildInviteUrl\(/.test(host), 'host.js builds a link from the minted code');
  ok(/S\.host\.copyLink/.test(host), 'host.js ships the "copy link" button');
  ok(/S\.host\.copy\b/.test(host), 'and KEEPS the plain-code copy beside it (the link is additive)');
  ok(/hosted: !!\(session && session\.hosted\)/.test(host), 'host.js tells the builder whether it is hosted');
  ok(/linkBtn\.disabled = true/.test(host), 'the link button is dead until a code exists');

  // --- the media screen is reachable
  ok(html.includes('id="scr-media-setup"'), 'index.html ships #scr-media-setup');
  ok(html.indexOf('id="scr-media-setup"') < html.indexOf('id="scr-lobby"'),
    'and it sits before the lobby in the stack, where it sits in the flow');
  ok(routerMod.SCREEN_IDS.mediaSetup === 'scr-media-setup', 'the router maps mediaSetup -> #scr-media-setup');
  ok(/mediaSetup: mediaSetupScreen/.test(boot), 'boot.js registers the screen module');
  ok(/needsMediaSetup\(media\)/.test(boot), 'boot.js decides the branch off the DECK, not off the picker');
  ok(/mediaPrepPending = needsMediaSetup/.test(boot), 'and only on the join that landed');
  {
    // The decision is made in joinStart and NOWHERE else — one join, one answer.
    const js = boot.slice(boot.indexOf('async joinStart('));
    const body = js.slice(0, js.indexOf('\n  },'));
    ok(/syncLocalDeck\(true\)/.test(body), 'joinStart re-feeds the deck before asking whether it is empty');
    ok(/mediaPrepPending = needsMediaSetup\(media\)/.test(body), 'joinStart is where the branch is decided');
  }
  ok(!/hostStart[\s\S]{0,400}needsMediaSetup/.test(boot), 'hosting never triggers the media step');

  // --- the hold, and the way out of it
  ok(/function showMediaSetup\(/.test(boot), 'boot.js holds the screen in front of the lobby');
  {
    const b = boot.slice(boot.indexOf('function showMediaSetup('));
    const body = b.slice(0, b.indexOf('\n}\n') + 1);
    ok(/setMediaPrep\?\.\(true\)/.test(body), 'entering the step tells the peer');
    ok(/mediaPrepTold/.test(body), 'and tells them ONCE — onPhase fires many times');
  }
  ok(/mediaPrepDone\(\)/.test(boot), 'boot.js exposes the "I am set" action');
  {
    const b = boot.slice(boot.indexOf('mediaPrepDone()'));
    const body = b.slice(0, b.indexOf('\n  },'));
    ok(/clearMediaPrep\(true\)/.test(body), 'locking in tells the peer we are done');
    ok(/onPhase\(currentMatch\.phase\)/.test(body), 'and hands the player to whatever phase the match reached');
  }
  ok(/clearMediaPrep\(false\)/.test(boot.slice(boot.indexOf('function detachMatch('))),
    'a detached match clears the hold with nobody to tell');

  // The ENGINE is untouched: the media step is a screen, not a phase.
  const lobbyArm = boot.slice(boot.indexOf('case GoonMatchPhase.Lobby:'));
  ok(/if \(showMediaSetup\(\)\) break;/.test(lobbyArm.slice(0, lobbyArm.indexOf('case GoonMatchPhase.Draft'))),
    'both pre-lobby phase arms consult the hold');
  ok(!/GoonMatchPhase\.MediaSetup/.test(boot), 'no phase was invented for it');

  // --- the host sees it
  ok(/S\.lobby\.prepPicking/.test(lobby), 'the lobby renders the "picking their media" line');
  ok(/match\.remoteMediaPrep/.test(lobby), 'off the engine flag, not off a local guess');
  ok(/typeof match\.onMediaPrepChanged === 'function'/.test(lobby),
    'and subscribes OPTIONALLY, so an older match object still mounts');
}

// ===================================== 5. the branch: does this player need it?
{
  const { needsMediaSetup } = mediaSetup;
  const pool = mediaMod.createGoonMediaPool();

  ok(needsMediaSetup(pool) === true, 'a fresh page (no manifest, no picks) needs the step');

  // The HOST's preset is media too: a desktop player with a library behind them
  // is "has media" without owning a single local pick.
  pool.setManifest({
    images: [{ name: 'a.png', url: 'https://ccp.assets/a.png' }],
    videos: [], skipped: 0, truncated: false,
  });
  ok(needsMediaSetup(pool) === false, 'a host manifest alone skips the step');

  const pool2 = mediaMod.createGoonMediaPool();
  pool2.setLocalLibrary([{ kind: 'image', name: 'a.png', url: 'blob:a' }]);
  ok(needsMediaSetup(pool2) === false, 'a single local pick alone skips the step');
  pool2.setLocalLibrary([]);
  ok(needsMediaSetup(pool2) === true, 'and removing the last one puts it back');

  // A pool we cannot read at all answers false: showing this screen by accident
  // to somebody with a full library is worse than quietly skipping it.
  ok(needsMediaSetup(null) === false, 'no pool -> no screen');
  ok(needsMediaSetup({}) === false, 'a pool with no hasMedia -> no screen');
  ok(needsMediaSetup({ hasMedia() { throw new Error('boom'); } }) === false, 'a pool that throws -> no screen, no throw');
}

// ========================================= 6. the media screen, actually mounted
{
  const L = strings.S.mediaSetup;

  /** A store just real enough: the two verbs the screen uses, and a change signal. */
  function fakeStore(initial) {
    const listeners = new Set();
    let locals = (initial || []).slice();
    const api = {
      added: [],
      get items() { return locals.slice(); },
      get localItems() { return locals.slice(); },
      get localCount() { return locals.length; },
      onItems(fn) { listeners.add(fn); return () => listeners.delete(fn); },
      onLocalProgress(fn) { listeners.add(() => {}); return () => { void fn; }; },
      addLocalFiles(files) {
        api.added.push(files);
        for (let i = 0; i < files.length; i++) {
          locals.push({ id: 'local:' + (locals.length + 1), name: files[i].name, kind: 'image', bytes: 100, srcBytes: 100, srcUrl: 'blob:x' });
        }
        api.emit();
        return Promise.resolve({ added: files.length, compressed: 0, dupes: 0, tooBig: 0, badType: 0, failed: 0 });
      },
      removeLocal(id) { locals = locals.filter((x) => x.id !== id); api.emit(); },
      emit() { for (const fn of Array.from(listeners)) { try { fn(locals.slice()); } catch (_e) { /* ignore */ } } },
    };
    return api;
  }

  // --- empty: the confirm is dead, and it SAYS why
  {
    const store = fakeStore([]);
    const container = dom.byId.get('scr-media-setup');
    container.replaceChildren();
    const doneCalls = [];
    const handle = mediaSetup.mount(container, {
      assets: store, logger: quiet, audio: null,
      actions: { mediaPrepDone: () => doneCalls.push(1), leave: () => doneCalls.push('leave') },
    });

    const card = findOne(container, 'gg-mediasetup');
    ok(!!card, 'the media screen mounts a card');

    const lock = findByText(card, L.lock)[0];
    ok(!!lock, 'the confirm button is on screen', L.lock);
    ok(lock.disabled === true, 'with NOTHING added it is disabled');
    ok(lock.title === L.lockNeed, 'and it says why', lock.title);

    const tally = findOne(card, 'gg-mediasetup-tally');
    ok(tally && tally.textContent === L.countNone, 'the tally says nothing has been added', tally && tally.textContent);

    // A disabled confirm must be inert even if something clicks it.
    lock.click();
    ok(doneCalls.length === 0, 'clicking the disabled confirm does nothing');

    // --- one file is enough. The twenty is copy, not a gate.
    store.addLocalFiles([{ name: 'one.png' }]);
    ok(lock.disabled === false, 'ONE item unlocks the confirm — the 20 is a suggestion');
    ok(findOne(card, 'gg-mediasetup-tally').textContent === L.count(1), 'and the tally counts it');
    const note = findAll(card, 'gg-assets-note').find((x) => x.textContent === L.suggest(20));
    ok(!!note, 'while still nudging toward the suggested twenty');

    lock.click();
    ok(doneCalls.length === 1, 'the enabled confirm calls actions.mediaPrepDone', String(doneCalls.length));

    // --- and it goes back if they change their mind
    const rows = findAll(card, 'gg-local-row');
    ok(rows.length === 1, 'the pick is listed', String(rows.length));
    const rm = findByText(rows[0], L.remove)[0];
    ok(!!rm, 'with a remove button');
    rm.dispatchEvent({ type: 'click', preventDefault() {} });
    ok(lock.disabled === true, 'removing the last item re-locks the confirm');

    // --- the picker is the SAME one the library screen uses
    const file = findTag(card, 'input').find((x) => x.getAttribute('type') === 'file');
    ok(!!file, 'the screen ships a real file input');
    ok(String(file.getAttribute('accept')).indexOf('.zip') >= 0, 'and it accepts a .zip pack');
    ok(String(file.getAttribute('accept')).indexOf('video/mp4') >= 0, 'and clips');
    ok(file.getAttribute('multiple') !== null, 'and more than one at a time');

    handle.unmount();
  }

  // --- twenty is praised, not merely tolerated
  {
    const many = [];
    for (let i = 0; i < 20; i++) many.push({ id: 'local:' + i, name: i + '.png', kind: 'image', bytes: 10, srcBytes: 10, srcUrl: 'blob:x' });
    const store = fakeStore(many);
    const container = dom.byId.get('scr-media-setup');
    container.replaceChildren();
    const handle = mediaSetup.mount(container, { assets: store, logger: quiet, actions: {} });
    const card = findOne(container, 'gg-mediasetup');
    ok(findOne(card, 'gg-mediasetup-tally').textContent === L.count(20), 'twenty items counted');
    const praised = findAll(card, 'gg-assets-note').some((x) => x.textContent === L.enough);
    ok(praised, 'and the nudge turns into praise at the suggested count');
    handle.unmount();
  }

  // --- the Discord pointer is a real, openable link
  {
    const store = fakeStore([]);
    const container = dom.byId.get('scr-media-setup');
    container.replaceChildren();
    const handle = mediaSetup.mount(container, { assets: store, logger: quiet, actions: {} });
    const card = findOne(container, 'gg-mediasetup');
    const a = findTag(card, 'a')[0];
    ok(!!a, 'the discord mention is an anchor, not just words');
    ok(a.getAttribute('href') === inviteLink.DISCORD_INVITE_URL, 'pointing at the invite constant', String(a.getAttribute('href')));
    ok(/^https:\/\/discord\.gg\//.test(inviteLink.DISCORD_INVITE_URL), 'which is a discord.gg invite', inviteLink.DISCORD_INVITE_URL);
    ok(a.getAttribute('rel') === 'noopener noreferrer', 'opened safely');
    handle.unmount();
  }

  // --- a store that is missing entirely must not take the screen down: this is
  // the screen standing between a new player and their first duel.
  {
    const container = dom.byId.get('scr-media-setup');
    container.replaceChildren();
    let threw = '';
    try {
      const h = mediaSetup.mount(container, { logger: quiet, actions: {} });
      h.unmount();
    } catch (e) { threw = (e && e.message) || String(e); }
    ok(threw === '', 'mount() survives a missing assets store', threw);
  }
}

// ============================================= 7. the `media_prep` wire message
{
  const { makeMediaPrep, MessageFactories, GoonMatchPhase } = contracts;
  const { serialize, parse } = wire;

  ok(typeof makeMediaPrep === 'function', 'contracts.js exports the factory');
  ok(MessageFactories.media_prep === makeMediaPrep, 'and the catalog routes `media_prep` to it');

  // --- the shape
  const on = makeMediaPrep({ preparing: true });
  ok(on.t === 'media_prep' && on.preparing === true, 'the frame carries `preparing`');
  ok(makeMediaPrep().preparing === false, 'ABSENT MEANS NOT PREPARING — the state that changes nothing');
  ok(makeMediaPrep().v === contracts.PROTOCOL_VERSION, 'it rides the CURRENT protocol version, not a new one', String(makeMediaPrep().v));

  // --- the round trip
  const json = serialize(on);
  ok(json.indexOf('"t":"media_prep"') >= 0, 'serializes with its discriminator', json);
  const back = parse(json, { logger: quiet });
  ok(!!back && back.preparing === true, 'and parses back true');
  const off = parse(serialize(makeMediaPrep({ preparing: false })), { logger: quiet });
  ok(!!off && off.preparing === false, 'and false round-trips too');

  // A frame from a peer that omits the field entirely (or fills it with junk)
  // reads as the factory default. The wire is untrusted.
  const bare = parse('{"t":"media_prep","v":1}', { logger: quiet });
  ok(!!bare && bare.preparing === false, 'an omitted field reads as false');
  const junk = parse('{"t":"media_prep","v":1,"preparing":"yes"}', { logger: quiet });
  ok(!!junk, 'a junk value still parses (the frame is not the place to reject it)');

  // --- FORWARD COMPATIBILITY, the whole point. An older build's catalog has no
  // `media_prep`, and wire.js already answers unknown types by ignoring them and
  // staying up. This is that behaviour, proved on a type nobody will ever add.
  let logged = '';
  const spy = { info: (m) => { logged += m; }, warn() {}, error() {} };
  ok(parse('{"t":"media_prep_from_the_future","v":1}', { logger: spy }) === null,
    'an unknown `t` parses to null rather than throwing — how an old peer sees this message');
  ok(/Ignoring unknown message type/.test(logged), 'and it is logged, not swallowed silently', logged);

  // ...and the same frame handed straight to a match must not disturb it either.
  const { GoonMatchService } = await import('../core/match.js');
  const { createLoopbackPair, loopbackOptions } = await import('../net/loopbackTransport.js');

  {
    const pair = createLoopbackPair(loopbackOptions({ latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, logger: quiet }));
    const solo = new GoonMatchService(pair.host, true, { logger: quiet, displayName: 'A', tag: 'GG:A' });
    let threw = '';
    try { solo._onMessageReceived({ t: 'something_new', v: 1 }); } catch (e) { threw = (e && e.message) || String(e); }
    ok(threw === '', 'the match pump ignores an unknown message type', threw);
    ok(solo.remoteMediaPrep === false, 'and a match that never heard the frame reads "ready"');
    solo.dispose();
    pair.dispose();
  }

  // --- end to end, over a real (in-process) transport
  {
    const pair = createLoopbackPair(loopbackOptions({ latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, logger: quiet }));
    const host = new GoonMatchService(pair.host, true, { logger: quiet, displayName: 'Host', tag: 'GG:host' });
    const guest = new GoonMatchService(pair.guest, false, { logger: quiet, displayName: 'Bambi', tag: 'GG:guest' });
    const seen = [];
    host.onMediaPrepChanged((v) => seen.push(v));

    host.adoptLobby();
    guest.adoptLobby();
    await pair.connect();
    await tick(60);

    ok(host.remoteMediaPrep === false, 'before anything is said, the host reads "ready"');
    ok(guest.localMediaPrep === false, 'and the guest has declared nothing');

    ok(guest.setMediaPrep(true) === true, 'the guest declares it is picking media');
    await tick(40);
    ok(host.remoteMediaPrep === true, 'the host sees it');
    ok(seen.length === 1 && seen[0] === true, 'exactly one change event', JSON.stringify(seen));

    // EDGE-TRIGGERED: a screen that repaints ten times a second must not turn a
    // status hint into wire traffic.
    ok(guest.setMediaPrep(true) === false, 're-declaring the same value sends nothing');
    await tick(30);
    ok(seen.length === 1, 'and raises no second event', String(seen.length));

    ok(guest.setMediaPrep(false) === true, 'locking in clears it');
    await tick(40);
    ok(host.remoteMediaPrep === false, 'the host sees them come ready');
    ok(seen.length === 2 && seen[1] === false, 'two events, in order', JSON.stringify(seen));

    // IT IS NOT A TERM. Nothing about the phase, the sheet or the confirmations
    // may have moved because somebody said they were still picking files.
    ok(host.localConsentConfirmed === false && host.remoteConsentConfirmed === false,
      'no confirmation was touched');
    ok(host.phase === GoonMatchPhase.Consent || host.phase === GoonMatchPhase.Lobby,
      'and the phase went exactly where it always goes', String(host.phase));

    host.dispose();
    guest.dispose();
    pair.dispose();
  }

  // --- it refuses to speak before there is anybody to speak to
  {
    const pair = createLoopbackPair(loopbackOptions({ latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, logger: quiet }));
    const idle = new GoonMatchService(pair.guest, false, { logger: quiet, displayName: 'X', tag: 'GG:x' });
    ok(idle.phase === GoonMatchPhase.Idle, 'a fresh match is Idle');
    ok(idle.setMediaPrep(true) === false, 'and setMediaPrep is a no-op there');
    idle.dispose();
    pair.dispose();
  }
}

// ================================================ 8. every string actually exists
{
  const S = strings.S;

  const mediaKeys = ['eyebrow', 'headline', 'lead', 'discordLine', 'discordCta', 'add',
    'countNone', 'remove', 'lock', 'lockNeed', 'waiting', 'note', 'leave', 'enough'];
  for (const k of mediaKeys) {
    ok(typeof S.mediaSetup[k] === 'string' && S.mediaSetup[k].length > 0, 'S.mediaSetup.' + k + ' exists');
  }
  ok(Array.isArray(S.mediaSetup.tips) && S.mediaSetup.tips.length >= 3, 'S.mediaSetup.tips is a real list');
  ok(typeof S.mediaSetup.count(2) === 'string' && S.mediaSetup.count(2).indexOf('2') >= 0, 'S.mediaSetup.count interpolates');
  ok(S.mediaSetup.count(1) !== S.mediaSetup.count(2), 'and it is singular at one');
  ok(S.mediaSetup.suggest(20).indexOf('20') >= 0, 'S.mediaSetup.suggest names the number');
  // The suggestion is a suggestion. Nothing in the copy may read as a gate.
  ok(!/must|required|need at least \d/i.test(S.mediaSetup.suggest(20) + ' ' + S.mediaSetup.tips.join(' ')),
    'and none of the guidance reads as a hard requirement');
  // The zip route and the discord pointer are BOTH named — they are the two
  // answers to "I do not have twenty files sitting in a folder".
  ok(/\.zip/.test(S.mediaSetup.tips.join(' ')), 'the tips mention a .zip pack');
  ok(/discord/i.test(S.mediaSetup.discordLine), 'and the discord line mentions discord');

  for (const k of ['copyLink', 'copiedLink', 'linkNote']) {
    ok(typeof S.host[k] === 'string' && S.host[k].length > 0, 'S.host.' + k + ' exists');
  }
  ok(S.host.inviteLinkLine('https://x/?join=A').indexOf('https://x/?join=A') >= 0,
    'S.host.inviteLinkLine carries the URL');
  ok(typeof S.toasts.linkCopied === 'string', 'S.toasts.linkCopied exists');
  ok(typeof S.join.leadLinked === 'string', 'S.join.leadLinked exists');
  ok(typeof S.lobby.eyebrowPicking === 'string', 'S.lobby.eyebrowPicking exists');
  ok(S.lobby.prepPicking('Bambi').indexOf('Bambi') >= 0, 'S.lobby.prepPicking names them');
  ok(typeof S.lobby.prepPicking('') === 'string' && S.lobby.prepPicking('').length > 0,
    'and still reads as a sentence with no name to use');
}

// ======================================================== 9. the protocol doc
{
  const doc = fs.readFileSync(path.join(ROOT, '..', '..', '..', 'docs', 'GOON_GAME_PROTOCOL.md'), 'utf8')
    .replace(/\r\n/g, '\n');
  ok(/`media_prep`/.test(doc), 'GOON_GAME_PROTOCOL.md documents the media_prep message');
  ok(/\?join=/.test(doc), 'and the invite link format');
  ok(/absent = (not preparing|false)/i.test(doc), 'and says what an absent frame means');
}

// -------------------------------------------------------------------- report
if (failures) {
  console.error(`\nselftest-invite: ${n - failures}/${n} checks passed`);
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
console.log(`selftest-invite: ${n}/${n} checks passed`);
