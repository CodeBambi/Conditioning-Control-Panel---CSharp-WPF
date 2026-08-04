// Sanity pass over net/ — signaling, the fake server, the transport base, loopback, relay
// mechanics, and session.js's relay-fallback dance.
//
//   node Resources/web/goon/test/selftest-net.js
//
// WebRTC itself is NOT testable under node (no RTCPeerConnection), which is why webrtcTransport is
// kept thin over the shared base: everything asserted below lives in transportBase/signaling/
// session, and the browser leg is a play-test item.

import fs from 'node:fs';

import { GoonSignalingClient, GoonSignalError, normalizeCode } from '../net/signaling.js';
import { GoonFakeSignalingServer, shadowGuestUid, isShadowUid } from '../net/fakeSignaling.js';
import { createLoopbackPair, loopbackOptions, loopbackPresets } from '../net/loopbackTransport.js';
import { GoonRelayTransport } from '../net/relayTransport.js';
import { GoonSession } from '../net/session.js';
import { GoonWebRtcTransport } from '../net/webrtcTransport.js';
import { GoonTransportState, GoonConsts, makeEmote, makeTick } from '../core/contracts.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, log() {} };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
// LF-normalized: the worktree is CRLF (core.autocrlf) and every source pin below
// is written against \n.
const readSource = (rel) => fs.readFileSync(new URL(rel, import.meta.url), 'utf8').replace(/\r\n/g, '\n');

async function until(fn, ms = 4000, step = 25) {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    if (fn()) return true;
    await sleep(step);
  }
  return false;
}

// ============================================================ 1. fake signaling + error map
async function testSignaling() {
  const fake = new GoonFakeSignalingServer();
  const host = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_host', appVersion: 'test', logger: quiet });
  const guest = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_guest', appVersion: 'test', logger: quiet });

  const invite = await host.createInvite('Bambi', false);
  ok(!!invite && !!invite.code && !!invite.token, 'invite mints a code + token');
  ok(invite.pass === 'premium' && invite.relayAllowed === true, 'invite reports pass + relay_allowed');

  // Before the join the host sees peer_joined=false.
  const pre = await host.signal(invite.code, invite.token, 'host', 0, []);
  ok(pre && pre.peerJoined === false, 'peer_joined false before the guest redeems');

  const joined = await guest.join(invite.code.toLowerCase(), 'Circe');
  ok(!!joined && !!joined.token, 'join redeems the code (case-insensitive)');
  ok(joined.token !== invite.token, 'guest gets its own room token');
  ok(joined.peerDisplayName === 'host', 'join reports the peer display name');

  /* --- THE SEAT BELONGS TO A UID (ghost-slot fix, 2026-08-04) ------------------
   * The owner's phone was told "that room already has two players" by its OWN
   * earlier attempt. Three outcomes have to stay separable here, because the
   * lobby renders three different sentences from them. */
  const stranger = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_third', appVersion: 'test', logger: quiet });
  const taken = await stranger.join(invite.code, 'Stranger');
  ok(taken === null && stranger.lastError === GoonSignalError.AlreadyJoined,
    'a DIFFERENT uid on an occupied room -> already_joined', String(stranger.lastError));

  const selfJoin = await host.join(invite.code, 'Bambi');
  ok(selfJoin === null && host.lastError === GoonSignalError.SelfJoin,
    'the host redeeming its own code -> self_join, not "full"', String(host.lastError));

  const again = await guest.join(invite.code, 'Circe');
  ok(!!again && again.rejoin === true, 'the SAME uid reclaims its seat instead of being refused');
  ok(!!again && again.token && again.token !== joined.token, 'a reclaimed seat gets a fresh room token');
  const reclaimedToken = again.token;

  // --- post-and-drain round trip ------------------------------------------------
  const h1 = await host.signal(invite.code, invite.token, 'host', 0, [{ kind: 'offer', data: 'OFFER' }]);
  ok(h1 && h1.messages.length === 0, 'no self-echo: the poster never gets its own message back');
  ok(h1 && h1.cursor === 1, 'cursor advances PAST our own message', String(h1 && h1.cursor));
  ok(h1 && h1.peerJoined === true, 'peer_joined true after the guest redeemed');

  const g1 = await guest.signal(invite.code, reclaimedToken, 'guest', 0, [{ kind: 'answer', data: 'ANSWER' }]);
  ok(g1 && g1.messages.length === 1 && g1.messages[0].kind === 'offer', 'guest drains the offer');
  ok(g1 && g1.messages[0].from === 'host' && g1.messages[0].seq === 1, 'drained message carries seq + from');
  ok(g1 && g1.cursor === 2, 'guest cursor covers its own answer too', String(g1 && g1.cursor));

  const h2 = await host.signal(invite.code, invite.token, 'host', h1.cursor, []);
  ok(h2 && h2.messages.length === 1 && h2.messages[0].data === 'ANSWER', 'host drains the answer');
  const h3 = await host.signal(invite.code, invite.token, 'host', h2.cursor, []);
  ok(h3 && h3.messages.length === 0, 'exclusive cursor: nothing is redelivered');

  // --- relay mailbox is a separate ring -----------------------------------------
  const r1 = await host.relay(invite.code, invite.token, 'host', 0, ['{"t":"tick"}'], 10);
  ok(r1 && r1.frames.length === 0 && r1.cursor === 1, 'relay: no self-echo, cursor advances');
  const r2 = await guest.relay(invite.code, reclaimedToken, 'guest', 0, [], 10);
  ok(r2 && r2.frames.length === 1 && r2.frames[0] === '{"t":"tick"}', 'relay: peer drains the frame');
  ok(r2 && r2.peerOnline === true, 'relay reports peer_online');

  // --- failure paths -------------------------------------------------------------
  const bad = await guest.join('ZZZZZZ', 'Circe');
  ok(bad === null && guest.lastError === GoonSignalError.UnknownCode, 'unknown code -> unknown_code', String(guest.lastError));

  fake.expire(invite.code);
  const dead = await host.signal(invite.code, invite.token, 'host', 0, []);
  ok(dead === null && host.lastError === GoonSignalError.Expired, 'expired room -> expired', String(host.lastError));
  const deadJoin = await guest.join(invite.code, 'Circe');
  ok(deadJoin === null && guest.lastError === GoonSignalError.UnknownCode, 'expired code reads as unknown_code to a joiner');

  const nope = await fake.post('/v2/goon/nope', {});
  ok(nope.status === 404 && JSON.parse(nope.body).error === GoonSignalError.NotDeployed, 'unknown route -> not_deployed');

  // --- the {kind, detail} error map the lobby renders -----------------------------
  const stub = (status, body) => new GoonSignalingClient({ post: () => Promise.resolve({ status, body }), logger: quiet });

  let c = stub(404, '');
  ok(await c.createInvite('x') === null && c.lastError === GoonSignalError.NotDeployed, 'bare 404 -> not_deployed');

  c = stub(404, '{"error":"expired"}');
  ok(await c.createInvite('x') === null && c.lastError === GoonSignalError.Expired, '404 WITH a body keeps the server error');

  c = stub(402, '{"error":"no_pass","next_pass_utc":"2026-08-10T00:00:00Z"}');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.NoPass && c.lastErrorDetail === '2026-08-10T00:00:00Z', '402 -> no_pass + next_pass_utc');
  ok(c.lastErrorInfo.kind === 'no_pass' && c.lastErrorInfo.detail === '2026-08-10T00:00:00Z', 'lastErrorInfo shape');

  c = stub(429, '{"error":"rate_limited","cap":"user","count":41,"retry_after_seconds":17}');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.RateLimited && c.retryAfterSeconds === 17, '429 -> rate_limited + retry_after_seconds');

  c = stub(409, '{"error":"already_joined"}');
  ok(await c.join('AAA') === null && c.lastError === GoonSignalError.AlreadyJoined, '409 -> already_joined');

  c = stub(401, '');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.Unauthorized, '401 -> unauthorized');

  c = stub(0, '');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.Network, 'status 0 (bridge transport failure) -> network');

  c = stub(200, 'not json');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.Malformed, '2xx with a junk body -> malformed_response');

  c = stub(200, '{"ok":true}');
  await c.createInvite('x');
  ok(c.lastError === GoonSignalError.Malformed, '2xx with no code/token -> malformed_response');

  c = stub(500, '');
  await c.createInvite('x');
  ok(c.lastError === 'http_500', 'unmapped status -> http_<status>');

  // --- code normalization ---------------------------------------------------------
  ok(normalizeCode(' 7qk4-rm ') === '7QK4RM', 'normalizeCode trims, uppers, strips dashes/spaces');
  ok(normalizeCode(null) === '' && normalizeCode(undefined) === '', 'normalizeCode tolerates null');
  ok(normalizeCode('IL0O') === 'IL0O', 'normalizeCode does NOT fold crockford I/L/O (server owns the alphabet)');
}

// ============================================================ 2. loopback + clocks + base
async function testLoopback() {
  const logged = { info: [], warn: [], error: [] };
  const spy = {
    info(m) { logged.info.push(String(m)); },
    warn(m) { logged.warn.push(String(m)); },
    error(m) { logged.error.push(String(m)); },
  };

  const pair = createLoopbackPair(loopbackOptions(Object.assign(loopbackPresets.p2p(), { logger: spy })));
  const { host, guest } = pair;

  ok(host.isHost === true && guest.isHost === false, 'pair roles');
  ok(host.state === GoonTransportState.Disconnected, 'starts Disconnected');
  ok(host.clock.isClockMaster === true && guest.clock.isClockMaster === false, 'host owns the match clock');

  const hostStates = [];
  const guestMsgs = [];
  const hostMsgs = [];
  const unsubState = host.onStateChanged((s) => hostStates.push(s));
  guest.onMessageReceived((m) => guestMsgs.push(m));
  host.onMessageReceived((m) => hostMsgs.push(m));

  const synced = await pair.connect();
  ok(synced, 'both loopback clocks synced');
  ok(host.state === GoonTransportState.ConnectedP2P, 'host connected');
  ok(host.clock.offsetMs === 0, 'host offset pinned at 0', String(host.clock.offsetMs));
  ok(Math.abs(guest.clock.offsetMs + 3517) < 50, 'guest offset cancels the 3517ms skew', String(guest.clock.offsetMs));
  ok(Math.abs(guest.clock.nowMatchMs() - host.clock.nowMatchMs()) < 50, 'match clocks agree (<50ms)',
    String(guest.clock.nowMatchMs() - host.clock.nowMatchMs()));

  // Frames round-trip through the base: serialize -> deliver -> parse.
  await host.send(makeEmote({ text: 'hi', icon: '*' }));
  ok(await until(() => guestMsgs.length > 0, 2000), 'frame delivered to the peer');
  ok(guestMsgs[0] && guestMsgs[0].t === 'emote' && guestMsgs[0].text === 'hi', 'frame parsed back into a message');
  ok(guestMsgs[0] && guestMsgs[0].v === 1, 'protocol version rides along');
  ok(hostMsgs.length === 0, 'no self-echo through the loopback');

  // Clock ping/pong is private to MatchClock and must never surface as a message.
  ok(!guestMsgs.some((m) => m.t === 'clock_ping' || m.t === 'clock_pong'), 'clock traffic never surfaces on messageReceived');

  // Oversize frame: dropped on the SEND side with an error, never handed to the channel.
  const before = guestMsgs.length;
  logged.error.length = 0;
  await host.send(makeTick({ active_effects: new Array(4000).fill('effectname') }));
  await sleep(150);
  ok(guestMsgs.length === before, 'oversize frame never reaches the peer');
  ok(logged.error.some((m) => m.includes('oversize')), 'oversize frame logged as an error', logged.error.join('|'));

  // A message with an unknown discriminator is dropped by parse, not by the peer's engine.
  const beforeUnknown = guestMsgs.length;
  await host._sendRaw('{"t":"from_the_future","v":9}');
  await sleep(150);
  ok(guestMsgs.length === beforeUnknown, 'unknown message type dropped at the wire');

  await host.close();
  ok(host.state === GoonTransportState.Closed, 'closed');
  ok(hostStates.length === 2 && hostStates[0] === GoonTransportState.ConnectedP2P
    && hostStates[1] === GoonTransportState.Closed, 'state events fire in order', hostStates.join(','));
  unsubState();

  // Unsubscribe actually unsubscribes.
  const wasLen = hostStates.length;
  guest.markConnected();
  await guest.close();
  ok(hostStates.length === wasLen, 'unsubscribed listener stops firing');

  pair.dispose();
  ok(host.isDisposed && guest.isDisposed, 'pair disposed');

  // Instant + relay presets exist and carry the documented knobs.
  const inst = loopbackPresets.instant();
  ok(inst.latencyMs === 0 && inst.jitterMs === 0 && inst.guestClockSkewMs === 0, 'instant preset');
  const rel = loopbackPresets.relay();
  ok(rel.latencyMs === 900 && rel.jitterMs === 600 && rel.guestClockSkewMs === 3517, 'relay preset');
}

// ============================================================ 3. relay transport mechanics
async function testRelayTransport() {
  const fake = new GoonFakeSignalingServer();
  const hostSig = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_host', logger: quiet });
  const guestSig = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_guest', logger: quiet });

  // The fake answers instantly, so the cadence floor is what paces the loop — shortened here so
  // the test asserts CURSOR/ECHO correctness rather than wall-clock timing.
  const mk = (isHost, signaling) => new GoonRelayTransport({
    isHost, signaling, logger: quiet, waitMs: 50, minGapMs: 50,
  });
  const host = mk(true, hostSig);
  const guest = mk(false, guestSig);

  const hostMsgs = [];
  const guestMsgs = [];
  host.onMessageReceived((m) => hostMsgs.push(m));
  guest.onMessageReceived((m) => guestMsgs.push(m));

  const code = await host.createInvite();
  ok(!!code, 'relay host minted a room');
  ok(host.code === code && !!host.token, 'relay keeps code + token');

  const joined = await guest.join(code);
  ok(joined === true, 'relay guest joined');

  ok(await until(() => host.state === GoonTransportState.ConnectedRelay
    && guest.state === GoonTransportState.ConnectedRelay, 3000), 'both sides report ConnectedRelay',
  `${host.state}/${guest.state}`);

  await host.send(makeEmote({ text: 'relayed', icon: '~' }));
  ok(await until(() => guestMsgs.some((m) => m.t === 'emote'), 3000), 'frame crossed the relay mailbox');
  ok(guestMsgs.find((m) => m.t === 'emote').text === 'relayed', 'relayed frame intact');
  ok(!hostMsgs.some((m) => m.t === 'emote'), 'relay never echoes the sender its own frame');

  const room = fake.room(code);
  ok(room.relay.length > 0, 'server ring holds the posted frames');
  // The emote is the host's OWN frame: its cursor must have moved past it even though the server
  // never echoed it back. (Exact equality with the ring head would race the clock's ping traffic.)
  const emoteSeq = room.relay.find((f) => f.from === 'host' && f.data.includes('"emote"')).seq;
  ok(host._cursor >= emoteSeq, 'poster cursor advanced past its OWN frame', `${host._cursor} vs ${emoteSeq}`);
  ok(guest._cursor >= emoteSeq, 'peer cursor covers everything drained', `${guest._cursor} vs ${emoteSeq}`);

  // Clock sync rides the same mailbox — proof the relay path is a full transport, not a side door.
  ok(await until(() => host.clock.isSynced && guest.clock.isSynced, 4000), 'clocks sync over the relay');

  await host.close();
  await guest.close();
  ok(host.state === GoonTransportState.Closed, 'relay closed');
  host.dispose();
  guest.dispose();

  // A room that dies under the loop surfaces the terminal server verdict.
  const dead = new GoonRelayTransport({ isHost: true, signaling: hostSig, logger: quiet, waitMs: 20, minGapMs: 20 });
  const deadCode = await dead.createInvite();
  ok(await until(() => dead.state === GoonTransportState.ConnectedRelay, 2000), 'second room live');
  fake.expire(deadCode);
  ok(await until(() => dead.state === GoonTransportState.Disconnected, 3000), 'expired room disconnects the relay');
  ok(dead.lastError === GoonSignalError.Expired, 'relay surfaces expired', String(dead.lastError));
  dead.dispose();
}

// ============================================================ 4. session fallback dance (stubs)
async function testSessionFallback() {
  // --- stubs -------------------------------------------------------------------
  class StubWebrtc {
    constructor(isHost, signaling, { failIce = true } = {}) {
      this.isHost = isHost; this.signaling = signaling; this._failIce = failIce;
      this.code = null; this.token = null; this.iceFailed = false; this.lastError = null;
      this.closed = false; this.disposed = false;
    }
    async createInvite() { this.code = 'ROOM01'; this.token = 'tok-host'; return this.code; }
    async join(c) { this.code = c; this.token = 'tok-guest'; return true; }
    async waitForChannel() {
      await sleep(10);
      this.iceFailed = this._failIce;
      this.lastError = this._failIce ? 'ice_timeout' : 'unauthorized';
      return false;
    }
    async close() { this.closed = true; }
    dispose() { this.disposed = true; }
  }

  class StubRelay {
    constructor(isHost, signaling) {
      this.isHost = isHost; this.signaling = signaling;
      this.adopted = null; this.lastError = null; this.closed = false; this.disposed = false;
    }
    adoptRoom(code, token) { this.adopted = { code, token }; }
    async waitForChannel() { return true; }
    async close() { this.closed = true; }
    dispose() { this.disposed = true; }
  }

  class StubMatch {
    constructor(transport, isHost) {
      this.transport = transport; this.isHost = isHost;
      this.adoptedLobby = false; this.cancelled = null; this.disposed = false;
    }
    createInvite() { return this.transport.createInvite(); }
    join(c) { return this.transport.join(c); }
    adoptLobby() { this.adoptedLobby = true; return true; }
    cancelMatch(reason) { this.cancelled = reason; }
    dispose() { this.disposed = true; }
  }

  // --- the fallback path --------------------------------------------------------
  const matches = [];
  const signalings = [];
  const webrtcs = [];
  const relays = [];
  const changed = [];
  const failed = [];

  const session = new GoonSession({
    logger: quiet,
    createMatch: (t, isHost) => { const m = new StubMatch(t, isHost); matches.push(m); return m; },
    createSignaling: () => { const s = { id: signalings.length, dispose() { this.disposed = true; } }; signalings.push(s); return s; },
    createWebrtc: (isHost, sig) => { const w = new StubWebrtc(isHost, sig); webrtcs.push(w); return w; },
    createRelay: (isHost, sig) => { const r = new StubRelay(isHost, sig); relays.push(r); return r; },
  });
  session.onCurrentMatchChanged((m) => changed.push(m));
  session.onConnectFailed((r) => failed.push(r));

  const code = await session.host();
  ok(code === 'ROOM01', 'host() returns the invite code');
  ok(matches.length === 1 && session.currentMatch === matches[0], 'match built for the p2p transport');
  ok(changed.length === 1, 'onCurrentMatchChanged fired on session start');

  ok(await until(() => relays.length === 1 && !!relays[0].adopted, 3000), 'fallback opened a relay transport');
  ok(matches.length === 2, 'fallback REBUILT the match via the factory', String(matches.length));
  ok(session.currentMatch === matches[1], 'currentMatch points at the new match');
  ok(changed.length === 2 && changed[1] === matches[1], 'onCurrentMatchChanged fired for the rebuild');

  ok(matches[0].disposed === true, 'old match disposed');
  ok(matches[0].cancelled === null, 'old match was NOT cancelled (dispose is silent — never a forfeit)');
  ok(matches[1].adoptedLobby === true, 'new match adoptLobby()d instead of re-inviting');
  ok(relays[0].adopted.code === 'ROOM01' && relays[0].adopted.token === 'tok-host', 'code + token carried to adoptRoom');
  ok(webrtcs[0].closed && webrtcs[0].disposed, 'dead p2p transport closed and disposed');

  ok(signalings.length === 1, 'exactly ONE signaling client per session', String(signalings.length));
  ok(relays[0].signaling === webrtcs[0].signaling, 'relay reuses the SAME signaling instance (room + burned pass)');
  ok(failed.length === 0, 'a successful fallback raises no connectFailed');

  // leave() is the user path: it cancels, then folds.
  await session.leave();
  ok(matches[1].cancelled === 'left', 'leave() cancels the live match');
  ok(matches[1].disposed && relays[0].closed && relays[0].disposed, 'leave() tears the plumbing down');
  ok(signalings[0].disposed === true, 'leave() disposes the signaling client');
  ok(session.currentMatch === null && session.isBusy === false, 'session back to idle');
  ok(changed.length === 3 && changed[2] === null, 'teardown raises onCurrentMatchChanged(null)');

  // --- a signaling-level failure must NOT fall back to relay ---------------------
  const m2 = []; const r2 = []; const f2 = [];
  const s2 = new GoonSession({
    logger: quiet,
    createMatch: (t, isHost) => { const m = new StubMatch(t, isHost); m2.push(m); return m; },
    createSignaling: () => ({ dispose() {} }),
    createWebrtc: (isHost, sig) => new StubWebrtc(isHost, sig, { failIce: false }),
    createRelay: (isHost, sig) => { const r = new StubRelay(isHost, sig); r2.push(r); return r; },
  });
  s2.onConnectFailed((r) => f2.push(r));
  await s2.host();
  ok(await until(() => f2.length > 0, 3000), 'signaling failure raises connectFailed');
  ok(f2[0] === 'unauthorized', 'connectFailed carries the machine reason', String(f2[0]));
  ok(r2.length === 0, 'no relay is opened when ICE did not fail');
  ok(m2[0].disposed && m2[0].cancelled === null, 'failed lobby is disposed, not cancelled');
  ok(s2.currentMatch === null, 'failed session is idle');

  // A session already running refuses a second host().
  const busy = new GoonSession({
    logger: quiet,
    createMatch: (t, isHost) => new StubMatch(t, isHost),
    createSignaling: () => ({ dispose() {} }),
    createWebrtc: (isHost, sig) => new StubWebrtc(isHost, sig),
    createRelay: (isHost, sig) => new StubRelay(isHost, sig),
  });
  await busy.host();
  ok(await busy.host() === null, 'a busy session refuses a second host()');
  await busy.leave();
}

// =========================== 4b. the bulk surface (P2P media transfer, 2026-08-04)
//
// The transfer wire rides a SECOND, out-of-band channel. The gate on it is `supportsBulk`, and the
// whole reason that member exists is section 4 above: a relay fallback REBUILDS the match over a
// transport that reports `isConnected === true` while being a 16 KB/frame HTTP long-poll ring that
// media must never touch. `isConnected` would say yes. `supportsBulk` says no, and because it says
// no the prepick queue goes dormant, no `xfer:` tags are emitted, and the receiver renders from its
// own library — the silent degradation is the ABSENCE of a special case, which is what is asserted
// here.
async function testBulkSurface() {
  const fake = new GoonFakeSignalingServer();
  const sig = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_host', logger: quiet });
  const relay = new GoonRelayTransport({ isHost: true, signaling: sig, logger: quiet, waitMs: 50, minGapMs: 50 });

  ok(relay.supportsBulk === false, 'a fresh relay transport reports supportsBulk false');
  await relay.createInvite();
  ok(await until(() => relay.state === GoonTransportState.ConnectedRelay, 3000), 'relay connected');
  ok(relay.isConnected === true, 'and it IS connected as far as CONNECTED_STATES is concerned');
  ok(relay.supportsBulk === false, '…but still supportsBulk false — the gate is not isConnected');
  ok(relay.sendBulk(new ArrayBuffer(16)) === false, 'sendBulk returns FALSE so a caller can branch');
  ok(relay.bulkBufferedAmount === 0 && relay.bulkLowThreshold === 0, 'and its water marks are zero');
  ok(typeof relay.onBulkMessage(() => {}) === 'function', 'onBulkMessage still hands back an unsubscribe');
  ok(typeof relay.onBulkStateChanged(() => {}) === 'function', 'so does onBulkStateChanged');
  await relay.close();
  relay.dispose();

  // The webrtc transport is the ONLY one that overrides all six. RTCPeerConnection does not exist
  // under node, so this asserts the SURFACE (which is what a caller binds to) rather than a live
  // channel — the browser leg stays a play-test item, exactly as the header of this file says.
  const p2p = new GoonWebRtcTransport({ isHost: true, signaling: sig, logger: quiet });
  const proto = Object.getPrototypeOf(p2p);
  for (const member of ['supportsBulk', 'bulkBufferedAmount', 'bulkLowThreshold']) {
    const d = Object.getOwnPropertyDescriptor(proto, member);
    ok(!!d && typeof d.get === 'function', `webrtc overrides the ${member} getter`);
  }
  ok(typeof proto.sendBulk === 'function', 'webrtc overrides sendBulk');
  ok(typeof proto.onBulkMessage === 'function' && typeof proto.onBulkStateChanged === 'function',
    'webrtc overrides both bulk subscriptions');
  ok(p2p.supportsBulk === false, 'with no peer connection there is no bulk channel');
  ok(p2p.sendBulk(new ArrayBuffer(8)) === false, 'and sendBulk says so rather than pretending');
  ok(p2p.bulkLowThreshold === 262144, 'its low-water mark is BULK_LOW_WATER', String(p2p.bulkLowThreshold));
  p2p.dispose();
  sig.dispose();

  // Loopback: OFF by default, because Practice mode runs on this pair and must keep behaving
  // exactly as it does today.
  const plain = createLoopbackPair(loopbackOptions(Object.assign(loopbackPresets.instant(), { logger: quiet })));
  await plain.connect();
  ok(plain.host.supportsBulk === false, 'a default loopback pair carries no bulk channel (Practice)');
  ok(plain.host.sendBulk(new ArrayBuffer(8)) === false, 'and refuses bulk with a false');
  plain.dispose();

  // …and opt-in for the transfer tests.
  const bulk = createLoopbackPair(loopbackOptions({
    latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, bulk: true, logger: quiet,
  }));
  const states = [];
  bulk.host.onBulkStateChanged((s) => states.push(s));
  const gotBulk = [];
  const gotGame = [];
  bulk.guest.onBulkMessage((d) => gotBulk.push(d));
  bulk.guest.onMessageReceived((m) => gotGame.push(m));
  await bulk.connect();

  ok(states.length === 1 && states[0] === 'open', 'opting in reports the channel open on connect', states.join(','));
  ok(bulk.host.supportsBulk === true, 'and supportsBulk flips true');
  ok(bulk.host.sendBulk(new ArrayBuffer(16384)) === true, 'sendBulk takes a full-size binary frame');
  ok(await until(() => gotBulk.length === 1, 2000), 'which reaches the peer');
  ok(gotBulk[0] instanceof ArrayBuffer && gotBulk[0].byteLength === 16384, 'intact and un-parsed',
    String(gotBulk[0] && gotBulk[0].byteLength));
  ok(gotGame.length === 0, 'and NEVER surfaces on the game channel — bulk bypasses wire.js entirely');

  bulk.host.sendBulk('{"t":"xfer_hello","v":1}');
  ok(await until(() => gotBulk.length === 2, 2000), 'a control string rides the same channel');
  ok(gotBulk[1] === '{"t":"xfer_hello","v":1}', 'raw, exactly as it was written');

  bulk.host.dropBulkChannel();
  ok(states[states.length - 1] === 'closed', 'a dropped bulk channel is reported');
  ok(bulk.host.supportsBulk === false, 'and closes the gate');
  ok(bulk.host.sendBulk(new ArrayBuffer(8)) === false, 'so nothing more is accepted');
  bulk.dispose();

  // The wiring-mistake detector: an xfer_ control frame on the GAME channel would otherwise vanish
  // into parse()'s "unknown t" INFO log and the transfer would hang with no evidence anywhere.
  const warned = [];
  const spy = { info() {}, warn(m) { warned.push(String(m)); }, error() {} };
  const probe = createLoopbackPair(loopbackOptions({ latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, logger: spy }));
  probe.host.markConnected();
  probe.guest.markConnected();
  await probe.host._sendRaw('{"t":"xfer_offer","v":1,"tid":1}');
  await sleep(60);
  ok(warned.some((m) => m.includes('xfer_') && m.includes('GAME channel')),
    'an xfer_ frame on the game channel is logged at WARN', warned.join('|'));
  warned.length = 0;
  await probe.host._sendRaw('{"t":"from_the_future","v":1}');
  await sleep(60);
  ok(warned.length === 0, 'while an ordinary unknown type stays a quiet INFO drop');
  probe.dispose();
}

// ====================================== 5. the tick's `vwin` on a real channel (2026-08-04)
//
// APPEND-ONLY WIRE GROWTH, the DraftMsg allowed/confirmed precedent: one optional integer on the
// state tick saying how many FLOATING VIDEO WINDOWS the sender has up. Everything below is about
// the two directions of forgiveness that make an append-only field safe:
//
//   ABSENT = 0. An older peer, or the C# reference client, never mentions it. That must parse as
//   a tick with no windows, not as undefined leaking into a monitor.
//
//   NEVER TRUST THE NUMBER. It is clamped to the wire cap and then to the display cap on the way
//   IN and on the way OUT, so nothing downstream ever has to ask whether 9, -3, "2" or NaN is
//   possible. Frames are pushed through _sendRaw, i.e. straight into the peer's parse(), because
//   a hostile or mismatched peer is exactly what does not go through our own factories.
async function testVwinTick() {
  const pair = createLoopbackPair(loopbackOptions(Object.assign(loopbackPresets.instant(), { logger: quiet })));
  const { host, guest } = pair;
  const got = [];
  guest.onMessageReceived((m) => { if (m && m.t === 'tick') got.push(m); });
  await pair.connect();

  const lastTick = () => got[got.length - 1] || null;
  const raw = async (json) => {
    const before = got.length;
    await host._sendRaw(json);
    await until(() => got.length > before, 2000);
    return lastTick();
  };

  await host.send(makeTick({ score: 7, vwin: 3 }));
  ok(await until(() => got.length > 0, 2000), 'a tick carrying vwin crosses the channel');
  ok(lastTick() && lastTick().vwin === 3, 'and arrives with the count intact', JSON.stringify(lastTick()));
  ok(lastTick() && lastTick().score === 7, 'the fields it rides with are untouched', String(lastTick() && lastTick().score));

  await host.send(makeTick({ score: 1 }));
  await until(() => got.length > 1, 2000);
  ok(lastTick().vwin === 0, 'a tick built without one reports zero windows', String(lastTick().vwin));

  // The old-peer frame: byte for byte what a client that predates the field emits.
  const old = await raw('{"t":"tick","v":1,"at_match_ms":900,"score":12,"attention_pct":80,'
    + '"attention_mode":0,"active_effects":["Flashes"],"toy":false,"charges":2}');
  ok(!!old, 'a tick with NO vwin member still routes');
  ok(old.vwin === 0, 'ABSENT reads as zero, never undefined', String(old.vwin));
  ok(old.score === 12 && old.active_effects.length === 1, '…and the rest of that old frame parses normally');

  const cases = [
    ['{"t":"tick","v":1,"vwin":9}', 4, 'over the wire cap clamps to the display cap'],
    ['{"t":"tick","v":1,"vwin":5}', 4, 'five windows read as a full pool of four'],
    ['{"t":"tick","v":1,"vwin":4}', 4, 'four is four'],
    ['{"t":"tick","v":1,"vwin":-3}', 0, 'a negative count is zero'],
    ['{"t":"tick","v":1,"vwin":2.7}', 2, 'a fraction truncates rather than poisoning the DOM'],
    ['{"t":"tick","v":1,"vwin":"2"}', 2, 'a numeric string is read forgivingly'],
    ['{"t":"tick","v":1,"vwin":"lots"}', 0, 'a word is zero'],
    ['{"t":"tick","v":1,"vwin":null}', 0, 'an explicit null is zero'],
    ['{"t":"tick","v":1,"vwin":1e9}', 4, 'an absurd number is still just a full pool'],
    ['{"t":"tick","v":1,"vwin":[3]}', 0, 'an array where an int belongs is zero (Number([3]) is 3 — not here)'],
    ['{"t":"tick","v":1,"vwin":true}', 0, 'a boolean is zero too'],
  ];
  for (const [json, want, label] of cases) {
    const msg = await raw(json);
    ok(!!msg && msg.vwin === want, `vwin: ${label}`, `${json} -> ${msg && msg.vwin}`);
  }

  // Outbound is clamped too: our own bug must not be what puts 12 on their screen.
  const beforeOut = got.length;
  await host.send(makeTick({ vwin: 12 }));
  await until(() => got.length > beforeOut, 2000);
  ok(lastTick().vwin === 4, 'a local over-count is clamped on the way OUT as well', String(lastTick().vwin));

  // And it costs the 16 KB guard nothing: the field is four bytes of name.
  const fat = got.length;
  await host.send(makeTick({ vwin: 4, active_effects: new Array(4000).fill('effectname') }));
  await sleep(120);
  ok(got.length === fat, 'the oversize guard still drops a fat tick, vwin or not', String(got.length - fat));

  pair.dispose();
}

// ============================================================ 7. the hash blocklist
//
// net/blocklist.js is the safety client for the P2P media transfer, and it has two jobs that
// pull in opposite directions: answer the OFFER GATE in one synchronous turn (a round trip
// there would be a wedge and a timing oracle), and never let a proxy outage silently kill a
// consented feature. So it batches, caches both verdicts, and FAILS OPEN.
async function testBlocklist() {
  const { createBlocklist, BLOCKLIST_MAX_PER_POST } = await import('../net/blocklist.js');
  const SHA = (c) => String(c).repeat(64).slice(0, 64);
  const A = SHA('a'), B = SHA('b'), C = SHA('c');

  // --- batching: one POST for a burst of checks, and the hits come back only.
  {
    const posts = [];
    const bl = createBlocklist({
      post: (path, body) => {
        posts.push({ path, body });
        return Promise.resolve({ status: 200, body: JSON.stringify({ ok: true, blocked: [B] }) });
      },
      unifiedId: () => 'u_test',
      logger: quiet,
      batchMs: 5,
    });

    ok(bl.knows(A) === false, 'an unknown hash is UNKNOWN — which the offer gate reads as "not blocked"');
    ok(bl.isBlocked(A) === false, 'and never as blocked, or an outage would look like a ban');

    const blocked = [];
    bl.onBlocked((sha) => blocked.push(sha));
    bl.check([A, B, C, 'nope', A]);
    await sleep(60);

    ok(posts.length === 1, 'one debounced POST for the whole burst, duplicates and junk dropped',
      String(posts.length));
    ok(posts[0].path === '/v2/goon/blocked', 'to the route the server whitelist already allows', posts[0].path);
    ok(posts[0].body.unified_id === 'u_test', 'carrying the identity the rate limiter keys on');
    ok(posts[0].body.hashes.length === 3, 'three real hashes, and the junk string was never sent',
      String(posts[0].body.hashes.length));
    ok(bl.knows(A) && bl.knows(B) && bl.knows(C), 'every hash in the batch now has a verdict');
    ok(bl.isBlocked(B) === true && bl.isBlocked(A) === false && bl.isBlocked(C) === false,
      'and only the hits are blocked — the response carries hits ONLY, which is the shape the client wants');
    ok(blocked.length === 1 && blocked[0] === B,
      'onBlocked fires once per newly-blocked hash (this is what drops the file)', blocked.join(','));

    bl.check([A, B, C]);
    await sleep(40);
    ok(posts.length === 1, 'a re-check of known hashes costs nothing at all', String(posts.length));
    bl.dispose();
  }

  // --- the flush threshold: a big prefetch does not wait out the debounce.
  {
    let posted = 0;
    const bl = createBlocklist({
      post: () => { posted++; return Promise.resolve({ status: 200, body: '{"ok":true,"blocked":[]}' }); },
      logger: quiet,
      batchMs: 100000,          // the debounce would never fire — only the threshold can
      flushAt: 4,
    });
    bl.prime([SHA('1'), SHA('2'), SHA('3'), SHA('4'), SHA('5')]);
    await sleep(40);
    ok(posted === 1, 'hitting the flush threshold posts immediately instead of waiting', String(posted));
    ok(BLOCKLIST_MAX_PER_POST === 64, 'and the per-call cap matches the server BLOCKED_QUERY_MAX',
      String(BLOCKLIST_MAX_PER_POST));
    bl.dispose();
  }

  // --- FAIL OPEN. A dead proxy must not ban everyone's media.
  {
    let calls = 0;
    const bl = createBlocklist({
      post: () => { calls++; return Promise.resolve({ status: 503, body: '' }); },
      logger: quiet,
      batchMs: 5,
      retryMs: 80,
    });
    bl.check([A]);
    await sleep(50);
    ok(calls === 1, 'the lookup was attempted');
    ok(bl.isBlocked(A) === false, 'a 5xx does NOT block the hash — the sender is someone we consented to');
    ok(bl.knows(A) === true, 'and the verdict ANSWERS the offer gate immediately rather than stalling it');

    bl.check([A]);
    await sleep(20);
    ok(calls === 1, 'inside the retry window it is not asked again', String(calls));
    await sleep(120);
    bl.check([A]);
    await sleep(50);
    ok(calls === 2, 'past the retry stamp it IS asked again — fail-open is temporary, not permanent',
      String(calls));
    ok(bl.stats().failures === 2, 'and the failures are counted for the debug surface',
      JSON.stringify(bl.stats()));
    bl.dispose();
  }

  // --- status 0 (no host at all) is the same story.
  {
    const bl = createBlocklist({ post: () => Promise.resolve({ status: 0, body: '' }), logger: quiet, batchMs: 5 });
    bl.check([A]);
    await sleep(50);
    ok(bl.knows(A) === true && bl.isBlocked(A) === false, 'a dead bridge fails open too');
    bl.dispose();
  }
}

/* ====================================== 8. peer_card_ver (Discord §3)
 *
 * The ONE piece of the Discord sharing feature that lives in net/: the opaque
 * version string the server hands each side about the OTHER. This layer only
 * has to CARRY it and announce it — it never fetches an avatar, and it must
 * never decide whether one is wanted (that is a viewer preference, one tier up
 * in ui/discord.js).
 *
 * The three ways it would go wrong:
 *   · a version that arrives on /join (guest) or /signal (host) and is silently
 *     dropped, leaving the opponent permanently faceless with nothing in a log;
 *   · a listener that throws taking the signaling poll down with it — the poll
 *     IS the match's connection and an avatar is a decoration;
 *   · null being reported as a change. Nobody sharing anything is the DEFAULT
 *     case (every flag ships false), and it has to be silent.
 * ======================================================================== */
async function testPeerCardVer() {
  const fake = new GoonFakeSignalingServer();
  const hostSig = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_host', logger: quiet });
  const guestSig = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_guest', logger: quiet });

  const seenHost = [];
  const seenGuest = [];
  hostSig.onPeerCard((v) => seenHost.push(v));
  guestSig.onPeerCard((v) => seenGuest.push(v));
  // A decoration listener that blows up must not be able to break the poll.
  hostSig.onPeerCard(() => { throw new Error('listener blew up'); });

  const invite = await hostSig.createInvite('host');
  ok(!!invite, 'peer-card: a room is minted');

  // NOBODY SHARES ANYTHING — the default, and it must be silent.
  const quietJoin = await guestSig.join(invite.code, 'guest');
  ok(!!quietJoin && quietJoin.peerCardVer === null,
    'with no sharing, /join reports a null card version', String(quietJoin && quietJoin.peerCardVer));
  ok(seenGuest.length === 0, 'and NOTHING is announced — null is not a change', JSON.stringify(seenGuest));

  // The HOST learns the guest's version off /signal, next to peer_joined.
  fake.setPeerCardVer('guest', 'g-v1');
  const poll = await hostSig.signal(invite.code, invite.token, 'host', 0, []);
  ok(!!poll && poll.peerJoined === true, 'the host polls and sees the peer joined');
  ok(poll.peerCardVer === 'g-v1', 'and the guest card version rides alongside it', String(poll.peerCardVer));
  ok(seenHost.length === 1 && seenHost[0] === 'g-v1',
    'announced once — and the throwing listener stopped neither the poll nor the good listener',
    JSON.stringify(seenHost));
  ok(hostSig.peerCardVer === 'g-v1', 'and latched on the client, for a subscriber that arrives late');

  // /signal repeats it on EVERY tick. The client announces every time; the PAGE
  // de-duplicates. Two jobs, two files — a client that filtered here would hide
  // a re-share that happened to produce the same version.
  await hostSig.signal(invite.code, invite.token, 'host', poll.cursor, []);
  await hostSig.signal(invite.code, invite.token, 'host', poll.cursor, []);
  ok(seenHost.length === 3,
    "a repeated version is announced every poll — de-duplication is the page's job, not the wire's",
    String(seenHost.length));

  fake.setPeerCardVer('guest', 'g-v2');
  const poll2 = await hostSig.signal(invite.code, invite.token, 'host', poll.cursor, []);
  ok(poll2.peerCardVer === 'g-v2' && seenHost[seenHost.length - 1] === 'g-v2',
    'and a changed version comes straight through', String(poll2.peerCardVer));

  // The GUEST's half of the same contract, on a fresh room: /join is the only
  // response it ever gets before the channel is up.
  const fake2 = new GoonFakeSignalingServer();
  const hostSig2 = new GoonSignalingClient({ post: fake2.post, unifiedId: 'u_host2', logger: quiet });
  const guestSig2 = new GoonSignalingClient({ post: fake2.post, unifiedId: 'u_guest2', logger: quiet });
  const seenGuest2 = [];
  guestSig2.onPeerCard((v) => seenGuest2.push(v));
  fake2.setPeerCardVer('host', 'h-v1');
  const inv2 = await hostSig2.createInvite('host');
  const joined2 = await guestSig2.join(inv2.code, 'guest');
  ok(joined2 && joined2.peerCardVer === 'h-v1',
    'the guest learns the HOST card version off the /join response', String(joined2 && joined2.peerCardVer));
  ok(seenGuest2.length === 1 && seenGuest2[0] === 'h-v1',
    'announced exactly once', JSON.stringify(seenGuest2));

  const after = seenHost.length;
  hostSig.dispose();
  await hostSig.signal(invite.code, invite.token, 'host', poll2.cursor, []);
  ok(seenHost.length === after, 'a disposed client notifies nobody', `${after} -> ${seenHost.length}`);

  guestSig.dispose(); hostSig2.dispose(); guestSig2.dispose();
}

/* ==================================================================================
 * 9. THE GUEST BOUNCE — "briefly shows the page of the setup before game then
 *    bounces me back to homepage" (owner's phone test, 2026-08-04).
 *
 * The phone runs this page STANDALONE and joins a match the PC app hosts. The
 * join lands, the lobby paints, and a moment later the player is on the title
 * with nothing said. There is exactly one mechanism in the page that can do that
 * SILENTLY: GoonSession tears the match down internally, boot.js hears
 * onCurrentMatchChanged(null) and jumps to the title — while the EXPLANATION
 * (onConnectFailed) is still one turn behind it.
 *
 * Three things are pinned here:
 *   a) a guest that connects raises neither of the two events that can evict it;
 *   b) every internal fold raises connectFailed in the SAME macrotask turn as the
 *      teardown, which is what makes boot's deferred jump correct rather than
 *      lucky (a setTimeout(0) armed when the match goes null still runs after);
 *   c) a P2P attempt that died in a way the BROWSER owns — an SDP the phone's
 *      stack refuses — is flagged for the RELAY, not folded. The room is live and
 *      the weekly pass is burned; folding it is the eviction above.
 * ================================================================================*/
async function testGuestFold() {
  /** A guest transport shaped like GoonWebRtcTransport, with the outcome dialled in. */
  class GuestLeg {
    constructor(outcome) {
      this.isHost = false;
      this.code = null; this.token = null;
      this.iceFailed = false; this.lastError = null;
      this.closed = false; this.disposed = false;
      this._outcome = outcome;
    }
    async join(code) { this.code = code; this.token = 'tok-guest'; return true; }
    async waitForChannel() {
      await sleep(10);
      if (this._outcome === 'connected') return true;
      // 'sdp_rejected' is the fixed shape: a browser-level refusal that still
      // leaves a live room behind, so it flags for fallback like ice_timeout.
      this.iceFailed = this._outcome !== 'signaling';
      this.lastError = this._outcome === 'signaling' ? 'signaling_failed' : 'sdp_rejected';
      return false;
    }
    async close() { this.closed = true; }
    dispose() { this.disposed = true; }
  }
  class RelayLeg {
    constructor() { this.adopted = null; this.lastError = null; }
    adoptRoom(code, token) { this.adopted = { code, token }; }
    async waitForChannel() { return true; }
    async close() { /* nothing to close */ }
    dispose() { /* nothing to drop */ }
  }
  class FoldMatch {
    constructor(transport, isHost) {
      this.transport = transport; this.isHost = isHost;
      this.adoptedLobby = false; this.disposed = false;
    }
    join(c) { return this.transport.join(c); }
    adoptLobby() { this.adoptedLobby = true; return true; }
    cancelMatch() { /* the user path, not exercised here */ }
    dispose() { this.disposed = true; }
  }
  const build = (outcome) => {
    const legs = []; const relays = []; const events = [];
    const s = new GoonSession({
      logger: quiet,
      createMatch: (t, isHost) => new FoldMatch(t, isHost),
      createSignaling: () => ({ dispose() {} }),
      createWebrtc: () => { const l = new GuestLeg(outcome); legs.push(l); return l; },
      createRelay: () => { const r = new RelayLeg(); relays.push(r); return r; },
    });
    s.onCurrentMatchChanged((m) => events.push(m ? 'match' : 'null'));
    s.onConnectFailed((r) => events.push('failed:' + r));
    return { s, legs, relays, events };
  };

  // --- a) the healthy guest: nothing that can evict it ever fires ----------------
  {
    const g = build('connected');
    ok(await g.s.join('ABC123') === true, 'a guest that connects reports a successful join');
    await sleep(120);
    ok(g.events.join(',') === 'match', 'no teardown and no connectFailed on a healthy join', g.events.join(','));
    ok(g.s.currentMatch !== null, 'the match is still live — the lobby has something to render');
    await g.s.leave();
  }

  // --- b) the ordering boot.js's deferred fold depends on -----------------------
  {
    const g = build('signaling');
    let foldRanAt = -1;
    g.s.onCurrentMatchChanged((m) => {
      if (m) return;
      setTimeout(() => { foldRanAt = g.events.length; }, 0);
    });
    await g.s.join('ABC123');
    ok(await until(() => foldRanAt >= 0, 3000), 'the deferred fold ran');
    ok(g.events.join(',') === 'match,null,failed:signaling_failed',
      'an internal fold raises teardown THEN connectFailed', g.events.join(','));
    ok(foldRanAt === 3, 'and both land before a setTimeout(0) armed at teardown', String(foldRanAt));
    ok(g.relays.length === 0, 'a signaling-level failure still refuses to try a relay');
  }

  // --- c) an SDP the phone refused is a relay case, not an eviction -------------
  {
    const g = build('sdp_rejected');
    await g.s.join('ABC123');
    ok(await until(() => g.relays.length === 1 && !!g.relays[0].adopted, 3000),
      'a transport that flags iceFailed adopts the SAME room over the relay');
    ok(g.relays[0].adopted.code === 'ABC123', 'the code the player already typed is reused',
      String(g.relays[0].adopted && g.relays[0].adopted.code));
    ok(g.events.indexOf('null') < 0, 'and the guest is never torn down — no bounce to the title',
      g.events.join(','));
    ok(g.s.currentMatch !== null && g.s.currentMatch.adoptedLobby === true,
      'the rebuilt match adopts the lobby instead of re-joining');
    await g.s.leave();
  }

  // --- and the transport half of (c), which needs a browser to run for real -----
  {
    const src = readSource('../net/webrtcTransport.js');
    const i = src.indexOf('setRemoteDescription(offer) rejected');
    ok(i > 0, 'webrtcTransport still handles a rejected inbound offer');
    const arm = src.slice(i, i + 400);
    ok(/_iceFailed\s*=\s*true/.test(arm),
      'a guest whose stack refuses the offer flags iceFailed — session.js relays it instead of folding');
    ok(/_setState\(GoonTransportState\.Disconnected, 'sdp_rejected'\)/.test(arm),
      'and it still surfaces promptly rather than burning the ICE budget');
  }

  // --- the boot.js half: the jump is deferred and cancellable -------------------
  {
    const boot = readSource('../boot.js');
    ok(/function foldToTitle\(\)/.test(boot) && /function cancelFoldToTitle\(\)/.test(boot),
      'boot.js routes the silent teardown through foldToTitle/cancelFoldToTitle');
    ok(/if \(!soloPair\) foldToTitle\(\);/.test(boot),
      'onCurrentMatchChanged(null) no longer calls router.show(\'title\') directly');
    const cf = boot.indexOf('onConnectFailed((reason)');
    ok(cf > 0 && /cancelFoldToTitle\(\);/.test(boot.slice(cf, cf + 900)),
      'the connect-failure sheet cancels the fold so it opens over the screen it is about');
    ok(/setTimeout\(go, 0\)/.test(boot), 'and the fold really is deferred by a macrotask');
  }
}

/* ================================================================================
 * 10. THE GHOST SEAT (owner play-test, 2026-08-04)
 *
 * The phone was refused with "that room already has two players" by a room whose
 * second player WAS the phone: an earlier attempt had registered the seat and then
 * folded without telling anyone, and nothing but the 30-minute room TTL could
 * clear it. Two independent halves fix it, and both are pinned here because either
 * one alone still leaves a hole:
 *
 *   a) the SERVER reclaims a seat for the uid that already holds it, so the retry
 *      works even when the goodbye never arrives (closed tab, dead network, a
 *      server that predates /leave);
 *   b) the CLIENT hands the seat back on any teardown of a room that never
 *      connected, so the ghost does not exist in the first place — and does NOT
 *      hand it back once a channel came up, because past that point the room token
 *      is what the ledger writes with.
 * ==============================================================================*/
async function testSeatRelease() {
  // --- a) the fake server's seat semantics, end to end -------------------------
  {
    const fake = new GoonFakeSignalingServer();
    const host = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_h', logger: quiet });
    const phone = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_p', logger: quiet });
    const other = new GoonSignalingClient({ post: fake.post, unifiedId: 'u_o', logger: quiet });

    const inv = await host.createInvite('Host');
    const first = await phone.join(inv.code, 'Phone');
    ok(!!first && first.rejoin === false, 'a first join is not a rejoin');

    // The bounce: the phone hands the seat back on its way out.
    ok(await phone.leave(inv.code, first.token, 'guest') === true, 'guest /leave is acknowledged');
    ok(fake.room(inv.code) && fake.room(inv.code).joined === false, 'the seat is free again');

    const back = await phone.join(inv.code, 'Phone');
    ok(!!back && back.rejoin === false, 'a released seat is a clean join, not a reclaim');
    ok(fake.room(inv.code).guestUid === 'u_p', 'and the room knows whose seat it is');

    // …and the belt-and-braces half: even WITHOUT the goodbye, the same uid gets in.
    const reclaim = await phone.join(inv.code, 'Phone');
    ok(!!reclaim && reclaim.rejoin === true, 'a ghost seat is reclaimed by its own uid');
    ok(fake.room(inv.code).guestEpoch === 3, 'each claim is a new seat epoch', String(fake.room(inv.code).guestEpoch));

    ok(await other.join(inv.code, 'Other') === null && other.lastError === GoonSignalError.AlreadyJoined,
      'a genuinely occupied room still refuses a stranger — the message was only a lie about the OWNER');

    // The host walking away takes the code with it.
    ok(await host.leave(inv.code, inv.token, 'host') === true, 'host /leave is acknowledged');
    ok(fake.room(inv.code) === null, 'a host fold deletes the room');
    ok(await phone.join(inv.code, 'Phone') === null && phone.lastError === GoonSignalError.UnknownCode,
      'and the folded code is gone for everyone');
  }

  // --- b) leave() never disturbs the error the lobby is about to render ---------
  {
    const c = new GoonSignalingClient({ post: () => Promise.resolve({ status: 500, body: '' }), logger: quiet });
    c.lastError = GoonSignalError.AlreadyJoined;
    c.lastErrorDetail = 'keep-me';
    await c.leave('ABC123', 'tok', 'guest');
    ok(c.lastError === GoonSignalError.AlreadyJoined && c.lastErrorDetail === 'keep-me',
      'a failed goodbye does not overwrite lastError');
  }

  // --- c) which teardowns send it ----------------------------------------------
  const leaves = [];
  const sig = () => ({
    dispose() {},
    leave: (code, token, role) => { leaves.push({ code, token, role }); return Promise.resolve(true); },
  });
  class Leg {
    constructor(isHost, outcome) {
      this.isHost = isHost; this._outcome = outcome;
      this.code = null; this.token = null; this.iceFailed = false; this.lastError = null;
    }
    async createInvite() { this.code = 'ROOM99'; this.token = 'tok-host'; return this.code; }
    async join(c) { this.code = c; this.token = 'tok-guest'; return true; }
    async waitForChannel() {
      await sleep(5);
      if (this._outcome === 'connected') return true;
      this.iceFailed = false;                    // signaling-level: no relay, straight fold
      this.lastError = 'signaling_failed';
      return false;
    }
    async close() {}
    dispose() {}
  }
  const mk = (outcome) => new GoonSession({
    logger: quiet,
    createMatch: (t, isHost) => ({
      transport: t, isHost,
      createInvite: () => t.createInvite(), join: (c) => t.join(c),
      adoptLobby: () => true, cancelMatch: () => {}, dispose: () => {},
    }),
    createSignaling: sig,
    createWebrtc: (isHost) => new Leg(isHost, outcome),
    createRelay: (isHost) => new Leg(isHost, outcome),
  });

  {
    const s = mk('fold');
    await s.join('ABC123');
    ok(await until(() => leaves.length === 1, 3000), 'a guest fold hands the seat back');
    ok(leaves[0] && leaves[0].role === 'guest' && leaves[0].code === 'ABC123' && leaves[0].token === 'tok-guest',
      'with the room it actually held', JSON.stringify(leaves[0]));
  }
  {
    leaves.length = 0;
    const s = mk('fold');
    await s.host();
    await s.leave();
    ok(leaves.length === 1 && leaves[0].role === 'host', 'a host that never connected folds its lobby',
      JSON.stringify(leaves));
  }
  {
    leaves.length = 0;
    const s = mk('connected');
    await s.join('ABC123');
    await sleep(40);
    await s.leave();
    ok(leaves.length === 0,
      'a room that CONNECTED is never released — the ledger still needs that token', JSON.stringify(leaves));
  }
  {
    leaves.length = 0;
    const s = mk('fold');
    await s.dispose();
    ok(leaves.length === 0, 'a session that never got a room has nothing to hand back');
  }
  {
    // …and the flag is per ATTEMPT. A session object outlives one match here, so a
    // sticky "we connected once" would silently disable the goodbye for every room
    // after the first — the exact ghost, one match later.
    leaves.length = 0;
    let leg = 0;
    const s = new GoonSession({
      logger: quiet,
      createMatch: (t, isHost) => ({
        transport: t, isHost, join: (c) => t.join(c),
        adoptLobby: () => true, cancelMatch: () => {}, dispose: () => {},
      }),
      createSignaling: sig,
      createWebrtc: () => new Leg(false, ++leg === 1 ? 'connected' : 'fold'),
      createRelay: () => new Leg(false, 'fold'),
    });
    await s.join('AAA111');
    await sleep(40);
    await s.leave();
    ok(leaves.length === 0, 'the connected attempt still sends nothing');
    await s.join('BBB222');
    ok(await until(() => leaves.length === 1 && leaves[0].code === 'BBB222', 3000),
      'a LATER attempt on the same session still hands its seat back', JSON.stringify(leaves));
  }

  // --- d) the server half, pinned by source (it lives in another repo) ----------
  {
    const src = readSource('../net/session.js');
    ok(/_releaseRoom\(transport, signaling\);/.test(src) && src.indexOf('_releaseRoom(transport, signaling);') < src.indexOf('signaling.dispose()'),
      'the goodbye is issued BEFORE the signaling client is disposed');
    const sess = src.slice(src.indexOf('_releaseRoom(transport, signaling) {'));
    ok(/if \(this\._everConnected\) return;/.test(sess.slice(0, 400)),
      'and it is gated on never having connected');
  }
}

/* ================================================================================
 * 11. SELF-DUEL — one whitelisted account in both seats
 *
 * The owner play-tests GG with ONE account on two devices: the PC app hosts, the
 * phone opens the standalone web build through a link that carries the same
 * `?uid=`, so §10's `self_join` guard — correct for everyone else — refused the
 * entire e2e run. Whitelisted accounts may therefore hold both seats, with the
 * guest seat under the SHADOW id `<uid>#self`.
 *
 * The shadow is the whole safety argument, and it is a SERVER-side one (single
 * ledger row, a self-report that names nobody, an intact room-by-uid lookup).
 * What is pinned HERE is the half the client can be wrong about: the fake server
 * models the same rule, and nothing in the client stack cares that the two seats
 * are one account — no uid comparison, no ledger de-dup, no peercard assumption.
 * ==============================================================================*/
async function testSelfDuel() {
  const OWNER = 'u_owner', OUTSIDER = 'u_outsider';
  const SHADOW = shadowGuestUid(OWNER);
  ok(isShadowUid(SHADOW) && !isShadowUid(OWNER) && SHADOW === `${OWNER}#self`,
    'the shadow id is the uid plus a suffix a real unified_id can never contain');

  // --- a non-whitelisted account is refused exactly as before -------------------
  {
    const fake = new GoonFakeSignalingServer();
    const plain = new GoonSignalingClient({ post: fake.post, unifiedId: OWNER, logger: quiet });
    const inv = await plain.createInvite('Owner');
    const self = await plain.join(inv.code, 'Owner');
    ok(self === null && plain.lastError === GoonSignalError.SelfJoin,
      'without the whitelist the host still gets self_join', String(plain.lastError));
    ok(fake.room(inv.code).joined === false, 'and the refusal did not take the seat');
  }

  // --- the affordance ----------------------------------------------------------
  const fake = new GoonFakeSignalingServer();
  fake.setWhitelisted(OWNER);
  const pc = new GoonSignalingClient({ post: fake.post, unifiedId: OWNER, logger: quiet });
  const phone = new GoonSignalingClient({ post: fake.post, unifiedId: OWNER, logger: quiet });
  const other = new GoonSignalingClient({ post: fake.post, unifiedId: OUTSIDER, logger: quiet });

  const inv = await pc.createInvite('Owner PC');
  const seat = await phone.join(inv.code, 'Owner Phone');
  ok(!!seat && !!seat.token, 'a whitelisted account may redeem its own code');
  ok(seat.selfDuel === true && seat.rejoin === false, 'and the response says so', JSON.stringify(seat));
  ok(fake.room(inv.code).guestUid === SHADOW,
    'the guest seat is the SHADOW id, never the real uid', String(fake.room(inv.code).guestUid));
  ok(fake.room(inv.code).hostUid === OWNER, 'the host seat is untouched');

  // The two seats are still two peers as far as every mailbox is concerned.
  const h = await pc.signal(inv.code, inv.token, 'host', 0, [{ kind: 'offer', data: 'OFFER' }]);
  ok(h && h.messages.length === 0 && h.peerJoined === true, 'the host sees a joined peer and no self-echo');
  const g = await phone.signal(inv.code, seat.token, 'guest', 0, [{ kind: 'answer', data: 'ANSWER' }]);
  ok(g && g.messages.length === 1 && g.messages[0].data === 'OFFER', 'the shadow seat drains the offer');
  const h2 = await pc.signal(inv.code, inv.token, 'host', h.cursor, []);
  ok(h2 && h2.messages.length === 1 && h2.messages[0].data === 'ANSWER', 'and the host drains the answer back');
  const r = await pc.relay(inv.code, inv.token, 'host', 0, ['{"t":"tick"}'], 10);
  const r2 = await phone.relay(inv.code, seat.token, 'guest', 0, [], 10);
  ok(r && r2 && r2.frames.length === 1 && r2.peerOnline === true, 'the relay ring works across the two seats');

  // --- reclaim lands on the shadow, never on a second shadow -------------------
  const back = await phone.join(inv.code, 'Owner Phone');
  ok(!!back && back.rejoin === true && back.selfDuel === true, 'the self-duel guest reclaims its own seat');
  ok(fake.room(inv.code).guestUid === SHADOW, 'still ONE shadow deep (never <uid>#self#self)',
    String(fake.room(inv.code).guestUid));
  ok(fake.room(inv.code).guestEpoch === 2, 'the reclaim is a new seat epoch', String(fake.room(inv.code).guestEpoch));
  ok(back.token !== seat.token, 'and it gets a fresh room token');

  // --- /leave frees the shadow seat -------------------------------------------
  ok(await phone.leave(inv.code, back.token, 'guest') === true, 'the shadow seat can be handed back');
  ok(fake.room(inv.code).joined === false && fake.room(inv.code).guestUid === null, 'the seat is free again');
  const again = await phone.join(inv.code, 'Owner Phone');
  ok(!!again && again.selfDuel === true && again.rejoin === false, 'and the owner walks straight back in');
  ok(fake.room(inv.code).guestUid === SHADOW, 'on the same shadow id');

  // --- the guard it is an exception to still holds -----------------------------
  ok(await other.join(inv.code, 'Outsider') === null && other.lastError === GoonSignalError.AlreadyJoined,
    'a stranger is still refused an occupied room');
  const inv2 = await pc.createInvite('Owner PC');
  ok((await other.join(inv2.code, 'Outsider')) !== null, 'a real second player joins a whitelisted host normally');
  ok(fake.room(inv2.code).guestUid === OUTSIDER, 'and takes the seat under its OWN uid');
  const late = await phone.join(inv2.code, 'Owner Phone');
  ok(late === null && phone.lastError === GoonSignalError.AlreadyJoined,
    'a whitelisted host cannot self-duel into an occupied room', String(phone.lastError));

  // --- nothing in the client stack compares the two identities -----------------
  // If any of these ever grow a uid comparison, a self-duel becomes a silent
  // "you are your own opponent" bug rather than a working test rig.
  for (const rel of ['../net/session.js', '../net/webrtcTransport.js', '../net/relayTransport.js',
    '../net/transportBase.js', '../core/match.js']) {
    ok(!/unified_?[Ii]d\s*[=!]==?/.test(readSource(rel)), `${rel} never compares unified ids`);
  }
  ok(/self_duel/.test(readSource('../net/signaling.js')), 'the client reads the self_duel flag off /join');
}

/* ================================================================================
 * 12. BETA GATES (pre-ship follow-ups, 2026-08-04)
 *
 *   a) THE PREMIUM SEND VERDICT. The standalone page used to grant itself
 *      caps.mediaTransfer=true unconditionally (the dev affordance). Now the
 *      server answers /invite and /join with `media_send` (tier>=1 — the same
 *      bar the C# host applies), the signaling client records it, boot.js folds
 *      it into session.caps for the standalone page, and bridge.js only keeps
 *      the always-on default when there is NO server in play at all.
 *   b) THE OWED OFFER. A guest that redeemed a code is owed the host's offer
 *      within a poll round trip; a dead host used to leave it on "joining…"
 *      forever because the ICE budget never started. NoOfferTimeoutMs now feeds
 *      the same relay-fallback ladder as an ICE timeout — guest only, the
 *      host's untimed wait-for-a-human stays untimed.
 * ==============================================================================*/
async function testBetaGates() {
  // --- a) the verdict, recorded off both endpoints ------------------------------
  {
    const old = new GoonFakeSignalingServer();                    // models a server WITHOUT the field
    const c0 = new GoonSignalingClient({ post: old.post, unifiedId: 'u_old', logger: quiet });
    const inv0 = await c0.createInvite('Old');
    ok(!!inv0 && inv0.mediaSend === null && c0.mediaSend === null,
      'a server that predates media_send leaves the verdict null — nothing changes');

    const gated = new GoonFakeSignalingServer({ mediaSend: false });
    const freeHost = new GoonSignalingClient({ post: gated.post, unifiedId: 'u_free', logger: quiet });
    const inv = await freeHost.createInvite('Free');
    ok(!!inv && inv.mediaSend === false && freeHost.mediaSend === false,
      '/invite carries the server verdict and the client records false (free tier)');

    gated.setMediaSend(true);
    const premGuest = new GoonSignalingClient({ post: gated.post, unifiedId: 'u_prem', logger: quiet });
    const gj = await premGuest.join(inv.code, 'Prem');
    ok(!!gj && gj.mediaSend === true && premGuest.mediaSend === true,
      '/join carries the verdict and the client records true (premium)');
  }

  // --- a) the page halves, pinned at source level -------------------------------
  {
    const bridge = readSource('../bridge.js');
    ok(/mediaTransfer:\s*!\(q\.get\('server'\)\s*\|\|\s*prefs\.serverBase\)/.test(bridge),
      'standaloneInit only self-grants sending when NO server is in play (the pure-local dev path)');
    const boot = readSource('../boot.js');
    ok(/typeof goonSession\.signaling\.mediaSend === 'boolean'/.test(boot)
      && /!session\.hosted/.test(boot.slice(boot.indexOf('mediaSend') - 600, boot.indexOf('mediaSend') + 600)),
      'boot.js folds the server verdict into session.caps — standalone only, hosted init stays authoritative');
  }

  // --- b) the owed offer, transport half (needs a browser to run for real) ------
  {
    const src = readSource('../net/webrtcTransport.js');
    const i = src.indexOf('noOfferDeadline');
    ok(i > 0, 'waitForChannel budgets the wait for the FIRST offer');
    ok(/this\.isHost \? Infinity : Date\.now\(\) \+ GoonConsts\.NoOfferTimeoutMs/.test(src),
      'guest only — the host\'s untimed wait for a human stays untimed');
    const arm = src.slice(i, src.indexOf('negotiationSeen && Date.now() > deadline'));
    ok(/_iceFailed = true/.test(arm) && /'no_offer'/.test(arm),
      'a missing offer flags iceFailed so the SAME relay-fallback ladder runs');
    ok(Number.isFinite(GoonConsts.NoOfferTimeoutMs) && GoonConsts.NoOfferTimeoutMs > GoonConsts.IceTimeoutMs,
      'the no-offer budget is more generous than the ICE budget — a slow poll cycle must not eat a live host');
  }
}

// ============================================================ run
const main = async () => {
  await testSignaling();
  await testLoopback();
  await testRelayTransport();
  await testSessionFallback();
  await testBulkSurface();
  await testVwinTick();
  await testBlocklist();
  await testPeerCardVer();
  await testGuestFold();
  await testSeatRelease();
  await testSelfDuel();
  await testBetaGates();

  console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
  process.exit(failures === 0 ? 0 : 1);
};

main().catch((e) => { console.error(e); process.exit(1); });
