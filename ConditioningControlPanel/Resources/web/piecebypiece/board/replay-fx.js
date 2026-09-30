/* ============================================================================
 * board/replay-fx.js - the look of a capture replay panel.
 *
 * One fullscreen pass per panel (board/director.js draws a masked quad per
 * panel): the panel's own render target, with the juice done in the same
 * fragment shader so nothing adds a render pass.
 *   punch    the picture zooms about the panel centre at contact
 *   chroma   red and blue pull apart, strongest at the rim
 *   desat    a panel waiting its turn loses colour instead of only going dark
 *   burst    speed lines (a fling), a halftone ring (a squash), both (a slap)
 *   wipe     a white band crosses a panel as it lands
 *   film     an inner shadow along the ink, a letterbox vignette and grain
 * Timing for every one of these is board/replay-plan.js (panelState).
 * ==========================================================================*/

export const REPLAY_VERT = 'void main(){ gl_Position = vec4(position.xy, 0.0, 1.0); }';

export const REPLAY_FRAG = `uniform sampler2D map; uniform vec2 res; uniform vec3 planes[6]; uniform int count;
  uniform float flash; uniform float light; uniform float desat; uniform float zoom; uniform float chroma;
  uniform float burst; uniform float bstyle; uniform float wipe; uniform float time; uniform float seed;
  uniform float radius; uniform float grain; uniform vec2 center; uniform vec2 wipeDir;
  float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
  void main(){
    vec2 p = gl_FragCoord.xy;
    float edge = 1e6;
    for (int i = 0; i < 6; i++) {
      if (i >= count) break;
      float d = dot(planes[i].xy, p) + planes[i].z;
      if (d < 0.0) discard;
      edge = min(edge, d / max(length(planes[i].xy), 1e-4));
    }
    float R = max(radius, 1.0);
    vec2 q = center + (p - center) / zoom;
    vec2 off = (q - center) / R * chroma;
    vec3 c = vec3(texture2D(map, (q + off) / res).r, texture2D(map, q / res).g, texture2D(map, (q - off) / res).b);
    float l = dot(c, vec3(.2126, .7152, .0722));
    c = mix(c, vec3(l) * vec3(.93, .9, 1.06), desat) * light;
    // an inner shadow under the ink, and a letterbox-ish vignette on the stage
    c *= mix(.62, 1.0, smoothstep(0.0, R * .22, edge));
    vec2 s = p / res;
    c *= mix(.58, 1.0, smoothstep(0.0, .16, s.y) * smoothstep(1.0, .84, s.y));
    c *= mix(.8, 1.0, smoothstep(0.0, .1, s.x) * smoothstep(1.0, .9, s.x));
    if (burst >= 0.0) {
      vec2 d = p - center; float r = length(d) / R; float fade = 1.0 - burst;
      if (bstyle != 1.0) {
        float k = (atan(d.y, d.x) / 6.2831853 + .5) * 84.0;
        float h = hash(vec2(floor(k), seed));
        float inner = mix(.42, 1.1, burst);
        float line = step(.58, h) * step(fract(k), h * .45) * smoothstep(inner, inner + .16, r);
        c = mix(c, vec3(1.0, .96, .99), line * fade * .75);
      }
      if (bstyle != 0.0) {
        float cell = 11.0; vec2 g = mod(p, cell) - cell * .5;
        float ring = exp(-pow((r - burst * 1.3) / .24, 2.0));
        float dr = cell * .56 * ring;
        float dotm = 1.0 - smoothstep(dr - 1.0, dr, length(g));
        // the dots ring the blow, never cover it: clear inside the middle of the panel
        c = mix(c, vec3(1.0, .36, .66), dotm * fade * .7 * smoothstep(.3, .55, r));
      }
    }
    if (wipe >= 0.0) {
      float u = dot(p - center, wipeDir) / R;
      float band = 1.0 - smoothstep(0.0, .24, abs(u - mix(-1.4, 1.4, wipe)));
      c = mix(c, vec3(1.0), band * .92);
    }
    // The hit is an exposure punch with a pink lift, not a white wash: mixing toward
    // white before the tonemap turned the whole panel milky (Breakout paid for this too).
    c = c * (1.0 + flash * 1.8) + flash * vec3(.09, .03, .06);
    c += (hash(p + fract(time) * vec2(91.7, 37.3)) - .5) * grain;
    gl_FragColor = vec4(c, 1.0);
    #include <tonemapping_fragment>
    #include <colorspace_fragment>
  }`;

/** The burst style for an impact: 0 speed lines, 1 halftone ring, 2 both. */
export function burstStyle(impact) { return impact === 'fling' ? 0 : impact === 'squash' ? 1 : 2; }

// Comic impact words, short, by how the man was taken. No two panels say the same.
const WORDS = Object.freeze({
  fling: ['POW', 'WHAM', 'YEET'],
  slap: ['BONK', 'SMACK', 'THWAP'],
  squash: ['SPLAT', 'SQUISH', 'FLOMP'],
  other: ['POP', 'BONK', 'WHAM'],
});
export function impactWords(impact, random = Math.random) {
  const list = WORDS[impact] || WORDS.other;
  const k = Math.floor(random() * list.length);
  return list.map((_, i) => list[(k + i) % list.length]);
}

/**
 * Paint order of the panels, pictures and ink alike: the panel taking its hit on
 * top of its neighbours, the rest by index. board/director.js gives each picture
 * quad this rank and hangs each panel's ink in the same order.
 */
export const panelRank = (i, lit) => (lit ? 10 : i);

/**
 * Each panel's ink, bottom first, with the panels painted above it. A panel's
 * seam is masked by every panel above it (owner, 2026-09-29): a panel swinging
 * past its seat on the way in, pulling back on the way out or shaking on a hit
 * slides UNDER its neighbour, so its edge never draws across that neighbour's
 * picture. It used to, for about a tenth of a second on every slide.
 * polys: [{ i, lit }] -> [{ poly, over: [poly...] }]
 */
export function inkLayers(polys) {
  const order = [...polys].sort((a, b) => panelRank(a.i, a.lit) - panelRank(b.i, b.lit));
  return order.map((poly, k) => ({ poly, over: order.slice(k + 1) }));
}

/**
 * A hand-inked seam boils: each corner jitters a pixel or two, a dozen times a
 * second. Keyed on the corner's SEATED position, so a corner two panels share
 * boils the same way in both and the gutters stay closed.
 */
export function boil(kx, ky, t, amp = 1.6) {
  const f = Math.floor(t * 12);
  const h = n => { const s = Math.sin(n * 12.9898 + 78.233) * 43758.5453; return s - Math.floor(s) - .5; };
  const key = Math.round(kx * 997) * 7 + Math.round(ky * 991) * 13;
  return [h(key + f * 3.1) * 2 * amp, h(key * 1.7 + f * 5.3 + 11) * 2 * amp];
}
