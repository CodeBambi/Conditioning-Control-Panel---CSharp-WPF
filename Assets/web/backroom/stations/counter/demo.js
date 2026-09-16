/* ============================================================================
 * demo.js - the Prize Parlour's "Try it" previews (2026-09-15), pure, no DOM.
 *
 * The three effect prizes (Jackpot Remix, Flashes v2, Bubbles v2) get a small
 * in-card preview of what they do, drawn by the page inside the card's art box.
 * It is NOT the prize: the real effects are the app's own overlays, and the
 * host plays them only for an account that owns the grant (FlashMotion,
 * AmbientBubbleMotion and the remix director all ask PrizeGrants, never a
 * page), so a demo must never ask the host to render them. Nothing here posts
 * an fx, nothing pays SP, nothing leaves the card.
 *
 *   demoKind(prizeId)          which preview a prize has (null: none, no button)
 *   demoFrame(kind, ms, o)     the sprites at `ms` into the preview: x, y (0..1
 *                              of the box), scale, rotation, alpha, picture index
 *   DEMO                       the lengths; `still` (Calm, reduced) shows the
 *                              settled frame and holds it (Law VI)
 * ==========================================================================*/

export const DEMO = Object.freeze({
  ms: 5200,          // one preview, then the art comes back
  stillMs: 1800,     // Calm and reduced motion: the settled picture, held, then gone
  sprites: 4,        // one per dealt picture (the counter deals 4)
  bubbleR: 0.11,     // bubble radius, a fraction of the box height
  flashW: 0.34,      // the flash card, a fraction of the box width
});

const KINDS = Object.freeze({ jackpot_remix: 'remix', flashes_v2: 'flashes', bubbles_v2: 'bubbles' });

/** Which preview a prize id has, or null (the Racing Thoughts tracks and the Discord role have none). */
export const demoKind = (prizeId) => KINDS[String(prizeId)] || null;

const clamp01 = (v) => Math.max(0, Math.min(1, Number(v) || 0));
const ease = (p) => (1 - Math.cos(Math.PI * clamp01(p))) / 2;

/** Jackpot Remix: four tiles of the dealt pictures shuffle in a 2 x 2 mosaic, swapping places every 400 ms. */
function remix(ms, still) {
  const cells = [[0.25, 0.25], [0.75, 0.25], [0.25, 0.75], [0.75, 0.75]];
  const swapEvery = 400, n = still ? 0 : Math.floor(ms / swapEvery);
  return cells.map((c, i) => {
    const at = (i + n) % 4, from = cells[(i + Math.max(0, n - 1)) % 4], p = still ? 1 : ease(((ms % swapEvery) / swapEvery) * 2);
    return { x: from[0] + (cells[at][0] - from[0]) * p, y: from[1] + (cells[at][1] - from[1]) * p, scale: 0.46, rot: 0, alpha: 1, pic: i, kind: 'tile' };
  });
}

/** Flashes v2: one picture drifts and bounces off the box's walls for 2.6 s, then hangs from the top and swings. */
function flashes(ms, still) {
  const w = DEMO.flashW, h = w * 0.75;
  if (still) return [{ x: 0.5, y: 0.5, scale: w, rot: 0, alpha: 1, pic: 0, kind: 'flash' }];
  if (ms < 2600) {
    const t = ms / 1000, vx = 0.62, vy = 0.45;
    const tri = (v, span) => { const m = ((v % (2 * span)) + 2 * span) % (2 * span); return m > span ? 2 * span - m : m; };
    const x = w / 2 + tri(0.2 + vx * t, 1 - w), y = h / 2 + tri(0.1 + vy * t, 1 - h);
    return [{ x, y, scale: w, rot: 0, alpha: 1, pic: 0, kind: 'flash' }];
  }
  const t = (ms - 2600) / 1000, decay = Math.exp(-t * 0.35);
  return [{ x: 0.5, y: 0.5, scale: w, rot: 28 * Math.cos(t * 3.2) * decay, alpha: 1, pic: 1, kind: 'flash', pivot: 'top' }];
}

/** Bubbles v2: four bubbles rain down for 2.4 s, then spiral in to the centre and fade (the Spiral In path). */
function bubbles(ms, still) {
  const n = DEMO.sprites, r = DEMO.bubbleR;
  if (still) return Array.from({ length: n }, (_, i) => ({ x: 0.2 + i * 0.2, y: 0.5, scale: r * 2, rot: 0, alpha: 1, pic: i, kind: 'bubble' }));
  if (ms < 2400) {
    return Array.from({ length: n }, (_, i) => {
      const t = ((ms + i * 380) % 1900) / 1900;
      return { x: 0.16 + i * 0.22 + 0.03 * Math.sin(ms / 260 + i), y: -r + (1 + 2 * r) * t, scale: r * 2, rot: 0, alpha: 1, pic: i, kind: 'bubble' };
    });
  }
  const p = clamp01((ms - 2400) / 2400);
  return Array.from({ length: n }, (_, i) => {
    const a0 = (i / n) * Math.PI * 2, turns = 2.5 * p, radius = 0.42 * (1 - p);
    return { x: 0.5 + radius * Math.cos(a0 + turns * Math.PI * 2), y: 0.5 + radius * 0.8 * Math.sin(a0 + turns * Math.PI * 2),
      scale: r * 2 * (1 - 0.5 * p), rot: 0, alpha: 1 - Math.max(0, (p - 0.75) / 0.25), pic: i, kind: 'bubble' };
  });
}

/**
 * The sprites of preview `kind` at `ms` into it. `still` (Calm, reduced, prefers-reduced-motion): the settled
 * frame, the same at every ms. Returns [] once the preview is over (DEMO.ms, or DEMO.stillMs when still).
 */
export function demoFrame(kind, ms, { still = false } = {}) {
  const at = Math.max(0, Number(ms) || 0);
  if (at >= (still ? DEMO.stillMs : DEMO.ms)) return [];
  if (kind === 'remix') return remix(at, still);
  if (kind === 'flashes') return flashes(at, still);
  if (kind === 'bubbles') return bubbles(at, still);
  return [];
}

/** How long a preview runs. */
export const demoLengthMs = (still = false) => (still ? DEMO.stillMs : DEMO.ms);
