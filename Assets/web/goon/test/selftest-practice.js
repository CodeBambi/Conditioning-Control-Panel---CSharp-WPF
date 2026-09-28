// Practice reaches the match, whatever the host fiddles with first.
//
//   node Resources/web/goon/test/selftest-practice.js
//
// THE BUG (owner, 2026-09-23): in practice the human pressed "I'm in" and got
// "Settings changed - both of you confirm again" forever. Re-proposing the SAME
// terms (the lobby's remembered-length seed, a song or a slider landing where the
// sheet already was) cleared both lamps on the host while the bot read the frame
// as a plain "not confirmed" and kept its own lamp lit. The bot only re-signs when
// its lamp is dark, so neither side ever moved. A declaration flip (send media,
// voice notes) had the same shape.
//
// Every case runs the real engines on a real loopback with the real practice
// driver, the way boot.js startSolo builds it (the bot speaks no Game Night).

import { GoonMatchPhase } from '../core/contracts.js';
import { local as localCaps } from '../core/caps.js';
import { GoonMatchService } from '../core/match.js';
import { createLoopbackPair, loopbackOptions } from '../net/loopbackTransport.js';
import { createSoloDriver } from '../ui/soloDriver.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, debug() {}, log() {} };
const tick = (ms) => new Promise((r) => setTimeout(r, ms));
const CDN = 'https://cdn.bambicloud.com/a15c22e0-d347-4d92-9f78-0fb37099e549.mp3';

async function practice(label, fiddle) {
  const pair = createLoopbackPair(loopbackOptions({ latencyMs: 20, jitterMs: 10, guestClockSkewMs: 0, logger: quiet }));
  const me = new GoonMatchService(pair.host, true, { logger: quiet, displayName: 'Me' });
  const bot = new GoonMatchService(pair.guest, false, {
    logger: quiet, displayName: 'Practice', caps: Object.assign({}, localCaps(), { night: 0 }),
  });
  const driver = createSoloDriver({ match: bot, logger: quiet });
  me.adoptLobby();
  bot.adoptLobby();
  driver.start();
  await pair.connect();
  await tick(100);
  ok(me.phase === GoonMatchPhase.Consent, label + ': the lobby opens on the terms', String(me.phase));
  // Let the bot sign the opening sheet first: every fiddle below lands AFTER that.
  for (let i = 0; i < 40 && !me.remoteConsentConfirmed; i++) await tick(100);
  ok(me.remoteConsentConfirmed, label + ': the bot signs the opening sheet');

  await fiddle(me);

  // The human signs once, the way a person presses "I'm in" once.
  await tick(2000);
  me.confirmConsent();
  for (let i = 0; i < 40 && me.phase === GoonMatchPhase.Consent; i++) await tick(100);
  ok(me.phase !== GoonMatchPhase.Consent,
    label + ': one press of "I\'m in" and practice leaves the terms', String(me.phase));
  await tick(300);
  ok(bot.phase === me.phase, label + ': the bot is on the same screen', String(bot.phase) + ' vs ' + String(me.phase));
  driver.stop();
  me.dispose();
  bot.dispose();
}

await practice('same terms again', (me) => {
  const s = me.consentSheet;
  me.proposeConsent(s.live_duration_sec, s.toy_cap, s.payload_min_gap_ms);
  ok(me.remoteConsentConfirmed, 'same terms again: the bot keeps its signature (nothing changed)');
});

await practice('slider change', (me) => {
  const s = me.consentSheet;
  me.proposeConsent(s.live_duration_sec === 600 ? 900 : 600, s.toy_cap, s.payload_min_gap_ms);
  ok(!me.remoteConsentConfirmed, 'slider change: a real change clears the bot too');
});

await practice('song picked', (me) => {
  ok(me.setSong({ url: CDN, title: 'a song', durSec: 431 }), 'song picked: the host takes the pick');
  ok(me.consentSheet.live_duration_sec === 431, 'song picked: the song is the length');
});

await practice('song, then the same length again', async (me) => {
  me.setSong({ url: CDN, title: 'a song', durSec: 431 });
  await tick(2500);
  // The lobby re-proposing what the sheet already says (a re-mount, the song row
  // repainting) must not undo the bot's second signature.
  const s = me.consentSheet;
  me.proposeConsent(s.live_duration_sec, s.toy_cap, s.payload_min_gap_ms);
});

await practice('send media flipped', (me) => {
  me.setMediaTransfer(!me.localMediaTransfer);
});

console.log(`selftest-practice: ${n - failures}/${n} passed`);
if (failures) process.exit(1);
process.exit(0);
