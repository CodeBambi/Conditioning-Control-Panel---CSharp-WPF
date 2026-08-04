// Sanity pass over net/ — signaling, the fake server, the transport base, loopback, relay
// mechanics, and session.js's relay-fallback dance.
//
//   node Resources/web/goon/test/selftest-net.js
//
// WebRTC itself is NOT testable under node (no RTCPeerConnection), which is why webrtcTransport is
// kept thin over the shared base: everything asserted below lives in transportBase/signaling/
// session, and the browser leg is a play-test item.

import { GoonSignalingClient, GoonSignalError, normalizeCode } from '../net/signaling.js';
import { GoonFakeSignalingServer } from '../net/fakeSignaling.js';
import { createLoopbackPair, loopbackOptions, loopbackPresets } from '../net/loopbackTransport.js';
import { GoonRelayTransport } from '../net/relayTransport.js';
import { GoonSession } from '../net/session.js';
import { GoonWebRtcTransport } from '../net/webrtcTransport.js';
import { GoonTransportState, makeEmote, makeTick } from '../core/contracts.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, log() {} };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

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

  const dup = await guest.join(invite.code, 'Circe');
  ok(dup === null && guest.lastError === GoonSignalError.AlreadyJoined, 'second join -> already_joined', String(guest.lastError));

  // --- post-and-drain round trip ------------------------------------------------
  const h1 = await host.signal(invite.code, invite.token, 'host', 0, [{ kind: 'offer', data: 'OFFER' }]);
  ok(h1 && h1.messages.length === 0, 'no self-echo: the poster never gets its own message back');
  ok(h1 && h1.cursor === 1, 'cursor advances PAST our own message', String(h1 && h1.cursor));
  ok(h1 && h1.peerJoined === true, 'peer_joined true after the guest redeemed');

  const g1 = await guest.signal(invite.code, joined.token, 'guest', 0, [{ kind: 'answer', data: 'ANSWER' }]);
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
  const r2 = await guest.relay(invite.code, joined.token, 'guest', 0, [], 10);
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

// ============================================================ run
const main = async () => {
  await testSignaling();
  await testLoopback();
  await testRelayTransport();
  await testSessionFallback();
  await testBulkSurface();
  await testVwinTick();

  console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
  process.exit(failures === 0 ? 0 : 1);
};

main().catch((e) => { console.error(e); process.exit(1); });
