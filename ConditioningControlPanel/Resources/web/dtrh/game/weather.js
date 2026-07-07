/* ============================================================================
 * weather.js - Wave 2: the tunnel's mood zones become gameplay.
 *
 * One weather rolls per loop; the run brain forces the matching engine zone
 * (fx.forceZone) so the tube LOOKS like what it's doing, applies the modifier,
 * and shows a named chip on the HUD. The Mood Ring charm forecasts the next
 * sky / amplifies effects / rerolls once; the Storm Chaser mantra locks the
 * sky to Static for the run.
 * ==========================================================================*/

export const WEATHERS = [
  {
    id: 'stillness', zone: 'calm', glyph: '🕯', name: 'STILLNESS',
    desc: 'the deep holds its breath - fuses burn 10% slower',
    fuseMult: 1.10,
  },
  {
    id: 'perfume', zone: 'pinkfog', glyph: '🌸', name: 'HER PERFUME',
    desc: 'the fog is sweet - lust climbs 25% faster, pops pay +10%',
    heatGain: 1.25, payMult: 1.10,
  },
  {
    id: 'static', zone: 'storm', glyph: '⚡', name: 'STATIC',
    desc: 'stray current - a bolt pops a treat FOR you every few seconds (sometimes it bites a fuse instead)',
    boltEverySec: 8,
  },
  {
    id: 'foolsgold', zone: 'glimmer', glyph: '✨', name: "FOOL'S GOLD",
    desc: 'everything glitters - golden bubbles drift in more often',
    goldenBonus: 0.015,
  },
  {
    id: 'overstim', zone: 'neon', glyph: '💉', name: 'OVERSTIM',
    desc: 'too bright, too fast - the fall runs 15% hotter, bubbles come 10% faster',
    speedMult: 1.15, spawnRate: 1.10,
  },
];

export const WEATHER_BY_ID = Object.fromEntries(WEATHERS.map((w) => [w.id, w]));

/** Roll a weather different from the previous one (no double feature). */
export function rollWeather(prevId) {
  const pool = WEATHERS.filter((w) => w.id !== prevId);
  return pool[(Math.random() * pool.length) | 0];
}
