/* ============================================================
 * Bureau bridge — the page's single seam to the outside world.
 *
 * App mode (inside the Control Panel's WebView2 host):
 *   window.chrome.webview exists. Every request goes to the C#
 *   BureauHostService, which owns auth, the /v2/bureau/* server
 *   calls, the local content-hash index and frame decoding.
 *   Image pixels never leave the machine; the server only ever
 *   sees hash-keyed labels.
 *
 * Standalone mode (public page on cclabs.app):
 *   No host. A deterministic demo backend serves practice
 *   mannequins so the page is a playable teaser + a dev harness.
 *
 * Protocol (host -> page):
 *   init            { protocol, admin, hasAuth, indexing:{done,total,ready} }
 *   index-progress  { done, total, ready }
 *   packs           { lockers:[{id,name,installed,images}], catalogueUrl }
 *   pack-install-progress { msg } · pack-installed { id, name } · pack-install-failed { error }
 *   batch           { items:[{target, src, dims:[w,h]}], profile, exhausted, auth? }
 *   submit-result   { target, ok, code?, error?, gold?, grade?, score?, xp?,
 *                     filed?, amended?, trust_tier?, shift_used?, shift_cap? }
 *   inbox-result    { ok, entries:[{t,labels,clean,xp}], xp }
 *   stats-result    { ok, ratified, total, subs, contested }
 *   gold-result     { target, ok, removed?, error? }
 *   end-run         {}                      (host asks us to wind down)
 *
 * Protocol (page -> host):
 *   ready · heartbeat · log {msg}
 *   next {count, pack?} · submit {target, dims, boxes, amend?} · inbox · stats
 *   packs · open-catalogue · pack-drop {name} (+File AdditionalObjects)
 *   gold-set {target, boxes, remove}         (admin desks only)
 *   exit · exit-done
 * ============================================================ */
'use strict';

const Bridge = (() => {
  const isApp = !!(window.chrome && window.chrome.webview);
  const handlers = new Map();      // type -> Set<fn>

  function on(type, fn) {
    if (!handlers.has(type)) handlers.set(type, new Set());
    handlers.get(type).add(fn);
  }
  function emit(type, msg) {
    const set = handlers.get(type);
    if (set) for (const fn of set) { try { fn(msg); } catch (e) { console.error(e); } }
  }
  function send(type, payload) {
    const msg = Object.assign({ type }, payload || {});
    // Post the OBJECT, not a JSON string: the host parses e.WebMessageAsJson.
    if (isApp) window.chrome.webview.postMessage(msg);
    else Demo.handle(msg);
  }

  /* Send a message carrying dropped File objects: the WebView2 host receives them as
   * CoreWebView2File AdditionalObjects (with real filesystem paths) — the only way a
   * dropped file's path can reach C#. Falls back to the demo backend standalone. */
  function sendWithFiles(type, payload, files) {
    const msg = Object.assign({ type }, payload || {});
    if (isApp && window.chrome.webview.postMessageWithAdditionalObjects) {
      window.chrome.webview.postMessageWithAdditionalObjects(msg, files);
    } else {
      Demo.handle(Object.assign({ _files: files }, msg));
    }
  }
  function log(msg) { send('log', { msg: String(msg) }); if (!isApp) console.log('[bureau]', msg); }

  if (isApp) {
    window.chrome.webview.addEventListener('message', (e) => {
      let msg = e.data;
      if (typeof msg === 'string') { try { msg = JSON.parse(msg); } catch { return; } }
      if (msg && msg.type) emit(msg.type, msg);
    });
  }

  /* ---------------- standalone demo backend ---------------- */
  /* The demo's user-visible strings are DATA the page renders as if it came
   * off the wire (chest names, cosigner names, install steps), so they live
   * here rather than in UI_COPY — but they follow the same fiction and the
   * same guardrails as the copy tables. */
  const Demo = (() => {
    // Quests object mirrors the wire contract; the demo advances it so the public
    // page shows the whole feature. day.n starts near target so completion is quick.
    const quests = {
      day:   { n: 10, target: 12, goldN: 0, goldTarget: 1, done: false, goldDone: false },
      week:  { n: 37, target: 60, days: 4, minDays: 3, done: false, tickets: 2 },
      month: { n: 61, target: 100, quality: 34, qualityMin: 50, entitled: false },
      streak: { n: 8, bonusXp: 40 },
      frozen: false,
      dayEndsInSec: 6 * 3600 + 12 * 60,
    };
    // profile.mail = unclaimed slips waiting in the tray (optional additive field; the
    // clock-in briefing + POST WAITING nudge read it). Real server may add it later.
    let profile = { trust_tier: 1, xp: 120, sparks: 12, shift_used: 3, shift_cap: 150, recert: false, mail: 2, quests };
    let caseSeq = 0, ratified = 802, subs = 3080;
    let pendingMail = 2;
    const CATALOGUE = 'https://discord.com/channels/1456573221489999934/1511409848699584653';
    // Keepsake chests. Ids are wire values and never change; only the names are copy.
    const demoLockers = [
      { id: 'calibration-a', name: 'Practice Chest A', installed: true, images: 64 },
      { id: 'calibration-b', name: 'Practice Chest B', installed: true, images: 41 },
      { id: 'mannequin-arch', name: 'The Mannequin Room', installed: false, images: 217 },
      { id: 'sealed-annex', name: 'The Sealed Annex', installed: false, images: 180 },
    ];

    // Deterministic practice mannequin as an SVG data URI. Pose varies by seed
    // so consecutive demo cases don't look identical.
    function mannequin(seed) {
      const tilt = ((seed * 37) % 11) - 5;                 // -5..5 deg head tilt
      const armL = 120 + ((seed * 53) % 40);               // silhouette width wobble
      const hue = [74, 68, 80][seed % 3];
      const svg =
        `<svg xmlns="http://www.w3.org/2000/svg" width="400" height="300" viewBox="0 0 400 300">` +
        `<rect width="400" height="300" fill="#0e0d1a"/>` +
        `<g stroke="#232041" stroke-width="1">` +
        Array.from({ length: 19 }, (_, i) => `<line x1="${(i + 1) * 20}" y1="0" x2="${(i + 1) * 20}" y2="300"/>`).join('') +
        Array.from({ length: 14 }, (_, i) => `<line x1="0" y1="${(i + 1) * 20}" x2="400" y2="${(i + 1) * 20}"/>`).join('') +
        `</g>` +
        `<line x1="200" y1="0" x2="200" y2="300" stroke="#3c3866" stroke-dasharray="4 4"/>` +
        `<line x1="0" y1="150" x2="400" y2="150" stroke="#3c3866" stroke-dasharray="4 4"/>` +
        `<g fill="#4a44${hue}" opacity=".9" transform="rotate(${tilt} 200 150)">` +
        `<circle cx="200" cy="74" r="26"/>` +
        `<path d="M200 104 C ${200 - armL / 3} 108 ${200 - armL / 2.8} 150 ${200 - armL / 3.5} 196 ` +
        `C ${200 - armL / 4} 228 178 252 200 252 C 222 252 ${200 + armL / 4} 228 ${200 + armL / 3.5} 196 ` +
        `C ${200 + armL / 2.8} 150 ${200 + armL / 3} 108 200 104 Z"/></g>` +
        `<text x="12" y="290" fill="#3c3866" font-size="10" font-family="monospace">PRACTICE VALENTINE ${String(seed).padStart(4, '0')} · MANNEQUIN</text>` +
        `</svg>`;
      // NOT btoa: the label carries a "·" (U+00B7). btoa doesn't throw on it —
      // it emits the raw Latin-1 byte, which is invalid UTF-8, so the XML parser
      // rejects the whole document and the light-table silently never renders.
      // Percent-encoding is unicode-safe and needs no width/height guesswork.
      return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
    }

    function fakeHash(n) {
      let h = '';
      for (let i = 0; i < 64; i++) h += ((n * 2654435761 + i * 40503) % 16).toString(16);
      return h;
    }

    function handle(msg) {
      switch (msg.type) {
        case 'ready':
          setTimeout(() => emit('init', {
            type: 'init', protocol: 1, admin: false, hasAuth: true, demo: true,
            indexing: { done: 1, total: 1, ready: true },
          }), 350);
          break;
        case 'next': {
          const items = [];
          for (let i = 0; i < Math.min(msg.count || 8, 12); i++) {
            caseSeq++;
            items.push({ target: fakeHash(caseSeq) + ':0', src: mannequin(caseSeq), dims: [400, 300] });
          }
          setTimeout(() => emit('batch', { type: 'batch', items, profile, exhausted: false }), 300);
          break;
        }
        case 'submit': {
          if (msg.amend) {
            setTimeout(() => emit('submit-result', {
              type: 'submit-result', target: msg.target, ok: true, amended: true,
            }), 350);
            break;
          }
          subs++;
          profile.shift_used = Math.min(profile.shift_used + (caseSeq % 4 === 0 ? 0 : 10), profile.shift_cap);
          const gold = caseSeq % 4 === 0;
          // Advance the quest counters so the clipboard ticks live on the demo page.
          quests.day.n = Math.min(quests.day.target, quests.day.n + 1);
          quests.day.done = quests.day.n >= quests.day.target;
          if (gold) { quests.day.goldN = Math.min(quests.day.goldTarget, quests.day.goldN + 1); quests.day.goldDone = quests.day.goldN >= quests.day.goldTarget; }
          quests.week.n = Math.min(quests.week.target, quests.week.n + 1);
          quests.month.n = Math.min(quests.month.target, quests.month.n + 1);
          const res = gold
            ? { type: 'submit-result', target: msg.target, ok: true, gold: true, grade: ['S', 'A', 'B'][caseSeq % 3], score: 0.87, xp: [40, 25, 10][caseSeq % 3], trust_tier: profile.trust_tier, profile }
            : { type: 'submit-result', target: msg.target, ok: true, filed: true, xp: 10, shift_used: profile.shift_used, shift_cap: profile.shift_cap, profile };
          if (!gold) { profile.xp += 10; ratified += (subs % 3 === 0) ? 1 : 0; }
          else profile.xp += res.xp;
          setTimeout(() => emit('submit-result', res), 350);
          break;
        }
        case 'inbox': {
          // Quest payout slips arrive alongside ordinary ratification slips.
          const entries = pendingMail > 0 ? [
            { type: 'quest', quest: 'daily', xp: 60 },
            { type: 'quest', quest: 'weekly', xp: 200 },
            { type: 'quest', quest: 'streak', xp: 40 },
            // cosigners is optional on the wire — the digest renders gracefully without it.
            { t: fakeHash(11) + ':0', labels: 2, clean: false, xp: 60, cosigners: ['KEEPER 4471', 'KEEPER 0090'] },
            { t: fakeHash(12) + ':2', labels: 0, clean: true, xp: 15 },
          ] : [];
          const xp = entries.reduce((a, e) => a + e.xp, 0);
          pendingMail = 0; profile.mail = 0; profile.xp += xp;
          setTimeout(() => emit('inbox-result', { type: 'inbox-result', ok: true, entries, xp }), 250);
          break;
        }
        case 'stats':
          setTimeout(() => emit('stats-result', {
            type: 'stats-result', ok: true, ratified, total: 6306, subs, contested: 9,
            raid: { closed: ratified, total: 6306 },
          }), 200);
          break;
        case 'packs':
          setTimeout(() => emit('packs', {
            type: 'packs', lockers: demoLockers.map((l) => Object.assign({}, l)), catalogueUrl: CATALOGUE,
          }), 250);
          break;
        case 'pack-drop': {
          // Simulated intake: any dropped zip "installs" the first missing demo locker.
          const ghost = demoLockers.find((l) => !l.installed);
          let step = 0;
          const steps = ['Unwrapping...', 'Encrypting 12/64...', 'Encrypting 51/64...', 'Counting the new keepsakes...'];
          const tick = setInterval(() => {
            if (step < steps.length) {
              emit('pack-install-progress', { type: 'pack-install-progress', msg: steps[step++] });
            } else {
              clearInterval(tick);
              if (ghost) {
                ghost.installed = true;
                emit('pack-installed', { type: 'pack-installed', id: ghost.id, name: ghost.name });
                emit('packs', { type: 'packs', lockers: demoLockers.map((l) => Object.assign({}, l)), catalogueUrl: CATALOGUE });
              } else {
                emit('pack-install-failed', { type: 'pack-install-failed', error: 'Every practice chest is already home.' });
              }
            }
          }, 550);
          break;
        }
        case 'open-catalogue':
          window.open(CATALOGUE, '_blank');
          break;
        case 'exit':
          emit('end-run', { type: 'end-run' });
          break;
      }
    }
    return { handle };
  })();

  return { isApp, on, send, sendWithFiles, log };
})();
