/* readout.js - the wheel's ONE SP readout, through a tiny adapter so it can ride the room's hook.
 *
 *   ctx.spReadout { set(value), owe(n), thud(), target() }   the room's chip owns the Law I rule (fix pass,
 *                                                            CONTRACT 7.1 amendment 2026-09-14): used when present
 *   ctx.hostBack without the hook                            the room's #br-sp-value chip, written directly and
 *                                                            put back by an observer if the room repaints it
 *   standalone (dev.html)                                    the station's own chip
 *
 * LAW I. What it shows is wheel.shownSp(server, owed, flying): the server balance minus the pay the page has
 * not landed yet, or a BANK tick, and never more than the server holds. `owe(n)` holds a pay back from the
 * moment the spin reply arrives until THE BANK lands it; `settle()` (Back, suspend, close) hands over the
 * plain server number. */

import { shownSp } from './wheel.js';

export function createReadout({ ctx, own, doc = globalThis.document, format = n => String(n) }) {
  const hook = ctx && ctx.spReadout && typeof ctx.spReadout.set === 'function' && typeof ctx.spReadout.owe === 'function' ? ctx.spReadout : null;
  const hostChip = !hook && ctx && ctx.hostBack === true && doc ? doc.getElementById('br-sp-value') : null;
  let server = Number.isFinite(Number(ctx && typeof ctx.sp === 'function' ? ctx.sp() : NaN)) ? Number(ctx.sp()) : 0;
  let owed = 0, flying = null, obs = null;

  const value = () => shownSp(server, owed, flying);
  const node = () => (hook ? null : hostChip || own || null);
  function paint() {
    if (hook) { hook.owe(owed); hook.set(flying == null ? null : value()); return; }
    const n = node(), text = hostChip ? String(value()) : format(value());
    if (n && n.textContent !== text) n.textContent = text;
  }
  if (hostChip && typeof MutationObserver === 'function') {
    obs = new MutationObserver(paint);
    obs.observe(hostChip, { childList: true, characterData: true, subtree: true });
  }

  return {
    kind: hook ? 'hook' : hostChip ? 'room-chip' : 'own',
    get value() { return value(); },
    get server() { return server; },
    get owed() { return owed; },
    /** The authoritative balance changed (a reply, a balance frame). */
    setServer(sp) { if (Number.isFinite(Number(sp))) { server = Number(sp); paint(); } },
    /** Pay the server has settled but the page has not landed. */
    owe(n) { owed = Math.max(0, Math.trunc(Number(n) || 0)); paint(); },
    /** A BANK tick (the number the readout says while tokens fly), or null to go back to the rule. */
    show(n) { flying = n == null ? null : Number(n); paint(); },
    /** Everything landed: no debt, no flight, the plain server number. */
    settle() { owed = 0; flying = null; paint(); },
    /** THE THUD on the readout when the last token lands. Reduced motion lights it instead of scaling it. */
    thud(reduced) {
      if (hook && typeof hook.thud === 'function') { hook.thud(); return; }
      const n = node(), box = n && ((n.closest && n.closest('.br-sp')) || n);
      if (!box || typeof box.animate !== 'function') return;
      if (reduced) { box.animate([{ boxShadow: '0 0 0 2px #ffcf6b' }, { boxShadow: '0 0 0 2px #ffcf6b' }], { duration: 520 }); return; }
      box.animate([{ transform: 'scale(1.3)', filter: 'brightness(2.2)' }, { transform: 'scale(.94)', offset: 0.55 }, { transform: 'scale(1)', filter: 'brightness(1)' }],
        { duration: 340, easing: 'cubic-bezier(.2,1.5,.4,1)' });
    },
    /** Where THE BANK's tokens fly to, in client px. */
    target() {
      const el = hook && typeof hook.target === 'function' ? hook.target() : node();
      const b = el && el.getBoundingClientRect ? el.getBoundingClientRect() : null;
      return b && b.width ? { x: b.left + b.width / 2, y: b.top + b.height / 2 } : null;
    },
    dispose() {
      if (obs) obs.disconnect();
      obs = null;
      owed = 0; flying = null;
      paint();
    },
  };
}
