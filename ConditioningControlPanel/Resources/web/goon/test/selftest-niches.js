// The opponent's niches (2026-09-24): a seat that sends no files still throws pictures.
//
//   node Resources/web/goon/test/selftest-niches.js
//
// 1. caps.niches: cleaned, capped, omitted when empty, and a peer's list is re-cleaned.
// 2. exec/media.js keeps the peer niche set OUT of the deck and draws it only as the
//    drawReceived fallback, least-recently-shown first; a real received file still wins.
// 3. Source pins: boot sends peer-niches from the hello and clears it at detach, and the
//    host strings no longer say joining costs anything.

import fs from 'node:fs';
import url from 'node:url';
import { makeCaps, makeHello, cleanNiches, peerNiches, NICHE_CAP_MAX } from '../core/contracts.js';
import { local as localCaps } from '../core/caps.js';
import { createGoonMediaPool } from '../exec/media.js';
import { S } from '../ui/strings.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const read = (p) => fs.readFileSync(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8').replace(/\r\n/g, '\n');
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);

// ================================================== 1. caps.niches
{
  ok(!('niches' in makeCaps({})), 'no niches = no key (every old hello is byte-identical)');
  ok(!('niches' in makeCaps({ niches: [] })), 'an empty list is no key either');
  ok(same(makeCaps({ niches: ['bimbofication', 'Bimbos'] }).niches, ['bimbofication', 'Bimbos']), 'names pass through');
  ok(same(cleanNiches(['ok_one', 'OK_ONE', 'a', 'https://x.example/r/y', '../etc', 'has space', 7, null]), ['ok_one']),
    'only names: no urls, paths, spaces, one-letter or non-strings; deduped without case');
  const many = Array.from({ length: 20 }, (_, i) => 'niche' + i);
  ok(cleanNiches(many).length === NICHE_CAP_MAX && NICHE_CAP_MAX === 8, 'capped at 8');
  ok(same(peerNiches({ niches: ['good', 'bad name'] }), ['good']), 'a peer list is re-cleaned on receive');
  ok(same(peerNiches(null), []) && same(peerNiches({ niches: 'good' }), []), 'absent or not a list = none');
  ok(same(makeHello({ caps: localCaps({ niches: ['shiny_one'] }) }).caps.niches, ['shiny_one']), 'core/caps.js passes it to the hello');
}

// ================================================== 2. the peer niche pool
{
  const m = createGoonMediaPool();
  m.setManifest({ images: [{ name: 'own.jpg', url: 'https://ccp.assets/own.jpg' }], videos: [] });
  ok(m.drawReceived('image') === null, 'no peer niches, no received: drawReceived is null (own deck path unchanged)');
  const got = m.setPeerNicheLibrary({
    images: [{ name: 'a', url: 'https://ccp.assets/.temp/a.jpg' }, { name: 'b', url: 'https://ccp.assets/.temp/b.jpg' }],
    videos: [{ name: 'c', url: 'https://ccp.assets/.temp/c.webm' }],
  });
  ok(got === 3 && m.peerNicheCount() === 3, 'the set lands');
  ok(m.counts().images === 1, 'and stays out of the deck');
  for (let i = 0; i < 6; i++) ok(m.drawKind('image').url === 'https://ccp.assets/own.jpg', 'own draws never surface a peer niche picture');
  const p1 = m.drawReceived('image'), p2 = m.drawReceived('image'), p3 = m.drawReceived('image');
  ok(p1 && p1.provenance === 'niche', 'drawReceived falls back to their niches', p1 && p1.provenance);
  ok(p1.url !== p2.url && p3.url === p1.url, 'least-recently-shown rotation');
  const h = p1.acquire();
  ok(h && h.url === p1.url && typeof h.release === 'function', 'acquire hands a plain handle');
  const peek = m.peekReceived('image');
  ok(peek && peek.url === m.peekReceived('image').url, 'peek writes no rotation');
  const v = m.drawReceived('video');
  ok(v && v.clip === true && v.kind === 'video', 'their clips come as clips');
  m.addReceived({ sha: 'a'.repeat(64), kind: 'image', mime: 'image/jpeg', url: 'blob:real' });
  ok(m.drawReceived('image').provenance === 'peer', 'a real file of theirs still wins');
  m.setPeerNicheLibrary(null);
  ok(m.peerNicheCount() === 0, 'null takes the set back out');
}

// ================================================== 3. source pins
{
  const boot = read('../boot.js');
  ok(/peerNicheLink\.fromCaps\(match\.remoteCaps\)/.test(boot), 'boot hands the opponent hello to the host');
  ok(/type: 'peer-niches', subs: \[\]/.test(boot), 'and clears it when the match goes');
  ok(/bridge\.on\('peer-media'/.test(boot), 'and adopts peer-media frames');
  ok(/niches \}\);/.test(boot) && /function ownNiches\(\)/.test(boot), 'this seat advertises its own niches');
  ok(/session\.media\.online === false/.test(boot), 'but not with online pictures off');
  ok(!/joining (is|are) a prime|Hosting and joining/i.test(JSON.stringify(S)), 'no string says joining is paid');
  ok(typeof S.peerMedia.declined === 'string' && !/[–—!]/.test(S.peerMedia.declined), 'the declined toast, house style');
}

console.log(failures ? `selftest-niches: ${failures} FAILURE(S) of ${n}` : `selftest-niches: ${n}/${n} checks passed`);
process.exit(failures ? 1 : 0);
