/* ============================================================================
 * ui/screens/countdown.js — five seconds, full bleed.
 *
 * The numeral is derived from the SHARED CLOCK (match.startMatchMs minus
 * clock.nowMatchMs()), never from a local setInterval that started when this
 * screen mounted. That is the whole point: both players see "3" within a frame
 * of each other even on the relay path, because both are reading the same
 * agreed instant rather than counting their own seconds.
 *
 * If the clock is missing (a transport that never synced) the screen degrades
 * to a local countdown from the phase's own budget rather than sitting blank —
 * a wrong-by-200ms countdown is survivable; a frozen one is not.
 *
 * Mercy is NOT mounted here. boot mounts mercy at Live, and pre-live Escape is
 * already a clean cancel — see the Esc ladder in boot.js.
 * ==========================================================================*/

import { createLedger, el } from '../router.js';
import { S } from '../strings.js';

const FALLBACK_MS = 5000;

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { audio, getMatch, getClock } = ctx;
  const match = getMatch();

  const numeral = el('div', { class: 'gg-count-num gg-grad', text: '' });
  const youName = el('span', { class: 'gg-count-name gg-count-name--you', text: match?.localDisplayName || S.lobby.you });
  const themName = el('span', { class: 'gg-count-name gg-count-name--them', text: match?.opponent?.displayName || S.lobby.them });

  const stage = el('div', { class: 'gg-count' }, [
    el('div', { class: 'gg-count-names' }, [youName, themName]),
    numeral,
  ]);
  container.appendChild(stage);

  const localDeadline = Date.now() + FALLBACK_MS;
  let lastShown = null;

  function remainingMs() {
    const clock = getClock ? getClock() : null;
    if (match && clock && typeof clock.nowMatchMs === 'function' && match.startMatchMs > 0) {
      try { return match.startMatchMs - clock.nowMatchMs(); } catch (_e) { /* fall through */ }
    }
    return localDeadline - Date.now();
  }

  function paint() {
    const left = remainingMs();
    const secs = Math.ceil(left / 1000);
    const label = left <= 0 ? S.countdown.go : String(Math.max(1, Math.min(9, secs)));
    if (label === lastShown) return true;
    lastShown = label;

    numeral.textContent = label;
    numeral.classList.toggle('is-go', label === S.countdown.go);
    // Restart the scale+fade for every new value.
    numeral.classList.remove('is-beat');
    void numeral.offsetWidth;
    numeral.classList.add('is-beat');
    // Names drift toward the centre as the clock runs out.
    const t = Math.max(0, Math.min(1, 1 - (left / FALLBACK_MS)));
    stage.style.setProperty('--gg-count-t', t.toFixed(3));

    try { audio?.sfx?.(label === S.countdown.go ? 'countdown-go' : 'countdown-tick'); } catch (_e) { /* stub bus */ }
    return true;
  }

  paint();
  // rAF, not an interval: the numeral has to change on the frame the shared
  // clock crosses the second, and the ledger cancels it mid-flight on unmount.
  ledger.frame(() => { paint(); return true; });

  return { unmount() { ledger.dispose(); } };
}

export default { mount };
