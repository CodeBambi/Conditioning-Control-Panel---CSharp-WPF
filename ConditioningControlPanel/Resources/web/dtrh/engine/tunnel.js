/* ============================================================================
 * tunnel.js - the endless tunnel for the Sissy Fall.
 *
 * Geometry strategy: a CLOSED-LOOP treadmill. One baked TubeGeometry wraps a
 * closed CatmullRom spine (integer sine harmonics -> the curve closes exactly);
 * the camera's endless `depth` maps onto the loop as depth % LOOP_DEPTH. Fog
 * limits sightlines to ~70u against a 1000u loop, and card content is always
 * novel from the media pool, so the geometry repetition is invisible.
 *
 * The shader is copied from js/rabbit-hole/scene.js:198-268 with the
 * branch-hole carving removed (this tunnel has no veins) and a pinker palette.
 * The ring / spiral coords derive from vUv with INTEGER uRings/uSpiralTurns/
 * uArms, so fract() is continuous across the vUv wrap - no seam at the loop.
 * ==========================================================================*/

import * as THREE from 'three';
import { Q } from '../shared/quality.js';

export const RADIUS = 5.5;
export const LOOP_DEPTH = 1000;         // world units around the loop
export const FOG_COLOR = 0x220d1e;      // deep pink-tinted haze
export const FOG_DENSITY = 0.02;

const SPINE_UP = new THREE.Vector3(0, 1, 0);

// The loop spine: a big ring warped by integer sine harmonics (integer -> the
// curve closes exactly at 2*PI). Amplitudes stay small against the ring radius
// so bend radii are ~10x the tube radius: gentle, anti-nausea. Phases are
// randomized once per session so every fall winds a little differently.
export function buildLoopLayout() {
  const R = LOOP_DEPTH / (2 * Math.PI); // ~159
  const p1 = Math.random() * Math.PI * 2, p2 = Math.random() * Math.PI * 2;
  const p3 = Math.random() * Math.PI * 2, p4 = Math.random() * Math.PI * 2;

  const N = 128;
  const pts = [];
  for (let k = 0; k < N; k++) {
    const th = (k / N) * Math.PI * 2;
    const r = R + 30 * Math.sin(3 * th + p1) + 18 * Math.sin(5 * th + p2);
    const y = 22 * Math.sin(2 * th + p3) + 12 * Math.sin(7 * th + p4);
    pts.push(new THREE.Vector3(r * Math.cos(th), y, r * Math.sin(th)));
  }
  const spine = new THREE.CatmullRomCurve3(pts, true, 'catmullrom', 0.5);

  const wrap01 = (t) => ((t % 1) + 1) % 1;
  const pointAt = (t, out) => spine.getPoint(wrap01(t), out);
  // {pos, tangent, normal, binormal} basis for placing content in the spine's
  // local frame (binormal ~ local right, normal ~ local up) - same contract as
  // the Explore engine's layout, so fog.js consumes this object as-is.
  const frameAt = (t) => {
    const u = wrap01(t);
    const pos = spine.getPoint(u);
    const tangent = spine.getTangent(u).normalize();
    const binormal = new THREE.Vector3().crossVectors(tangent, SPINE_UP);
    if (binormal.lengthSq() < 1e-5) binormal.set(1, 0, 0);
    binormal.normalize();
    const normal = new THREE.Vector3().crossVectors(binormal, tangent).normalize();
    return { pos, tangent, normal, binormal };
  };
  const frameAtDepth = (d) => frameAt(d / LOOP_DEPTH);

  return {
    RADIUS, totalDepth: LOOP_DEPTH, loopDepth: LOOP_DEPTH,
    spine, pointAt, frameAt, frameAtDepth,
  };
}

// ---- tube shader --------------------------------------------------------------
// Branch-hole carving (uHole*) is back: junctions.js hands the main-tube material
// up to MAX_HOLES branch mouths, and the frag discards wall fragments inside each
// mouth's cylinder so a diverging vein reads as a real opening cut in the wall
// (ported from the Explore rabbit-hole scene.js). uHoleCount = 0 => zero overhead.
const MAX_HOLES = 2;

const TUBE_VERT = `
  varying vec2 vUv;
  varying float vFogDepth;
  varying vec3 vWorld;
  void main() {
    vUv = uv;
    vWorld = (modelMatrix * vec4(position, 1.0)).xyz;
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    vFogDepth = -mv.z;
    gl_Position = projectionMatrix * mv;
  }`;

const TUBE_FRAG = `
  precision highp float;
  #define MAX_HOLES ${MAX_HOLES}
  varying vec2 vUv;
  varying float vFogDepth;
  varying vec3 vWorld;
  uniform float uTime;
  uniform vec3 uBg1, uBg2, uLineColor, uSpiralColor, uFogColor;
  uniform float uFogDensity, uRings, uSpiralTurns, uArms, uScroll, uFlash, uRush;
  // 0 near the top of the fall, ramping to 1 the deeper you go: speeds up the
  // intermittent line-glow flicker so the rays flash slow up top, frantic deep.
  uniform float uGlowRate;
  // chamber pattern knobs (fx.js eases these; ALL 0 = the classic look).
  // Each of the Four Chambers wears its own subset:
  //   uBreath      I   - slow ring width+brightness swell (an invitation, not a demand)
  //   uRingDash    II  - rings segmented into doorframe posts
  //   uSpiralDrift II  - spiral phase wanders/stalls/reverses + depth shear (compass-loss)
  //   uLineWave    III - rings undulate around the circumference like vines
  //   uThrob       III - heartbeat lub-dub swell on the line glow
  //   uRingDouble  IV  - crisp twin rings (authoritative)
  //   uStrobe      IV  - hard metronomic neon tick (~1.4Hz, capped - signage, not strobe)
  uniform float uBreath, uRingDash, uLineWave, uRingDouble, uSpiralDrift, uThrob, uStrobe;
  // biome pattern params (SNAPPED by fx.js at the chamber commit, never eased -
  // counts/frequencies must stay integers for the seam-free treadmill, and the
  // spiral direction must never glide through scroll space):
  //   uDashCount/uDashDuty - ring dash segmentation (classic: 10 posts, 62%)
  //   uWaveF1/uWaveF2      - lineWave circumference frequencies (classic: 3 & 7)
  //   uStrobeHz            - metronome tick rate (classic: ~1.4Hz)
  //   uSpiralDir           - +-1 spiral scroll direction
  uniform float uDashCount, uDashDuty, uWaveF1, uWaveF2, uStrobeHz, uSpiralDir;
  // biome pattern knobs (eased by fx.js exactly like the chamber knobs above;
  // ALL 0 = the classic look). Each is owned by one or two biomes:
  //   uScan     - CRT phosphor rows + rolling hold-bar     (Late Night Static)
  //   uGlitch   - row-tear bursts on the whole wall        (Late Night Static)
  //   uKaleido  - spiral folded into mirrored chevrons     (Hall of Mirrors)
  //   uChase    - marquee bulb-train around each ring      (Fool's Casino)
  //   uChain    - twin rings alternate into linked arcs    (The Chain Court)
  //   uCaustic  - shifting water-light webs                (Undertow / Mirror Lake)
  //   uArch     - stained-glass panes with lead lines      (The Pink Chapel)
  //   uMirrorY  - waterline fold: bottom reflects top      (Mirror Lake)
  //   uStreaks  - always-on speed-streak sheet             (Terminal Velocity)
  //   uRingFade / uSpiralFade - dim the linework toward nothing (Keyhole / Searchlight)
  //   uSurge / uSlip / uBeatScroll - bounded scroll motion signatures
  //   uDots     - candy polka-dot lattice                  (The Toybox)
  //   uChecker  - alpha-grid checker + redaction bars      (Incognito)
  //   uDesat    - drain the wall to grey + paper grain     (The Grey Ward)
  //   uFiligree - counter-handed second spiral             (Coronation / Vertigo)
  //   uPanel    - gilt picture-frame tiling                (The Gallery)
  uniform float uScan, uGlitch, uKaleido, uChase, uChain, uCaustic, uArch, uMirrorY;
  uniform float uStreaks, uRingFade, uSpiralFade, uSurge, uSlip, uBeatScroll;
  uniform float uDots, uChecker, uDesat, uFiligree, uPanel;
  // branch mouths carved into the wall (world space)
  uniform int uHoleCount;
  uniform vec3 uHolePos[MAX_HOLES];
  uniform vec3 uHoleAxis[MAX_HOLES];
  uniform float uHoleR[MAX_HOLES];
  uniform float uHoleBack, uHoleFwd;
  uniform vec3 uRimColor;
  // forward dead-end cut: FADE a stretch of the tube (in vUv.x arc-length space)
  // to the fog color just past a fork so the trunk visually ENDS there in haze and
  // the only ways on are the two carved branch mouths. The wall is NOT discarded -
  // it stays an opaque fog-colored enclosure, so no sightline ever leaks the page
  // background as a black void (the renderer clear is transparent).
  // uCutHide=1 switches the window to DISCARD instead: used for the fresh loop's
  // incoming tail arc during a vein ride - that arc converges on the very point
  // the vein exits at (a closed ring must return to its entry), so left solid it
  // z-fights the vein wall and slices across the ride corridor. Discarding is
  // safe THERE because the ridden vein fully encloses the camera meanwhile.
  uniform int uCutOn;
  uniform int uCutHide;
  uniform float uCutLo, uCutHi;   // arc-length window [0..1]; if hi<lo the window wraps the seam

  // 1.0 on the line (integer coord), fading to 0 within half-width w. The edge
  // is widened to at least ~1.5 screen pixels (fwidth) so a far-off or grazing
  // (tube-wall side, seen near edge-on) line is never sub-pixel-thin. That
  // sub-pixel thinness is what makes the rings appear to "skip"/step when the
  // fall is slow - at speed the per-frame motion hides it, but crawling slowly a
  // hairline line snaps pixel-to-pixel. Screen-space AA keeps slow motion fluid.
  float lineMask(float coord, float w) {
    float di = 0.5 - abs(fract(coord) - 0.5); // distance to nearest integer
    // never sharper than the designed width, never so wide the line stops fully
    // darkening at the midpoint (di maxes at 0.5) - that upper clamp keeps rings
    // crisp instead of washing to a bright fill at grazing/foreshortened angles.
    float aa = clamp(1.5 * fwidth(coord), w, 0.5);
    return 1.0 - smoothstep(0.0, aa, di);
  }
  // sharp periodic pulse (mostly dark, brief bright peaks = intermittent).
  float pulse(float p, float k) { return pow(0.5 + 0.5 * sin(p), k); }
  // cheap per-cell hash for pane tints / tear offsets.
  float hash1(float n) { return fract(sin(n * 127.1) * 43758.5453); }

  void main() {
    float len = vUv.x;
    float around = vUv.y;
    float scroll = uTime * uScroll;

    // heartbeat phase (used by the throb glow below AND the communion
    // beat-scroll): lub-dub envelope; the rate rides the same depth ramp as the
    // flicker so the pulse quickens the deeper you fall.
    float hb  = fract(uTime * (1.05 + 0.5 * uGlowRate) * 0.5);
    float thr = uThrob * (exp(-18.0 * hb) + 0.55 * exp(-18.0 * fract(hb - 0.32)));

    // biome motion signatures: BOUNDED scroll offsets only (never scale scroll
    // itself - it grows unbounded; same rule as the spiral drift).
    //   surge      - slow swell-and-drag current (Undertow; a lap on Mirror Lake)
    //   slip       - vertical-hold slip: the wall rolls most of a ring spacing,
    //                holds, then snaps back like a set losing sync
    //   beatScroll - the wall lurches ON the heartbeat and settles between
    scroll += uSurge * 2.5 * sin(uTime * 0.45);
    scroll += uSlip * 0.8 * smoothstep(0.55, 0.95, fract(uTime * 0.53));
    scroll += uBeatScroll * 0.9 * exp(-6.0 * hb);

    // row tear (Static): whole bands of the wall shear sideways in brief
    // bursts. Band ids come from an INTEGER len frequency (wrap-safe); the
    // offset is small and bounded, and per-band phase staggers the tears so
    // they ripple down the tube instead of marching in lockstep.
    if (uGlitch > 0.001) {
      float band = floor(len * 48.0);
      float burst = pulse(uTime * 2.7 + hash1(band) * 6.2831, 24.0);
      around += uGlitch * burst * (hash1(band + 17.0) - 0.5) * 0.10;
    }

    // forward dead-end: fade the trunk to fog over the cut window so it reads as
    // haze/depth (a tube vanishing into mist), not an erased wall showing void.
    float cutFade = 0.0;
    if (uCutOn == 1) {
      float span = (uCutHi >= uCutLo) ? (uCutHi - uCutLo) : (1.0 - uCutLo + uCutHi);
      float into = len - uCutLo;
      if (into < 0.0) into += 1.0;          // wrapped window
      if (into > 0.0 && into < span) {
        if (uCutHide == 1) discard;         // loop-tail arc: the vein encloses the camera
        float e = min(0.02, span * 0.35);   // ~20u ramp on the 1000u loop
        cutFade = smoothstep(0.0, e, into) * smoothstep(0.0, e, span - into);
      }
    }

    // base: subtle tint variation around the tube
    vec3 base = mix(uBg1, uBg2, 0.5 + 0.5 * sin((around + 0.25) * 6.2831));

    // ---- chamber pattern knobs (all zero -> the classic coords below) ----
    // breath (I): slow width/brightness swell, ~8.4s period. The width factor
    // only scales the designed half-width; the fwidth floor in lineMask keeps AA.
    float brW = 1.0 + uBreath * 0.5 * sin(uTime * 0.75);
    float brG = 1.0 + uBreath * 0.3 * sin(uTime * 0.75 - 0.6);
    // vine wave (III): rings undulate around the circumference. INTEGER
    // circumference frequencies (uWaveF1/F2, classic 3 & 7) -> seam-free at the
    // vUv wrap; fwidth() is taken on the perturbed coord so the wavy line
    // keeps its screen-space AA. Undertow drops to 2 & 5 = one big kelp swell.
    float wave = uLineWave * (0.35 * sin(around * 6.2831 * uWaveF1 + uTime * 0.6)
                            + 0.2  * sin(around * 6.2831 * uWaveF2 - uTime * 0.9));
    // compass-loss (II): BOUNDED spiral phase offsets (never scale scroll - it
    // grows unbounded). When d(drift)/dt beats the scroll rate the spiral
    // visibly stalls and runs backwards; shear makes depths disagree about
    // direction (integer len frequency: wrap-safe).
    float drift = uSpiralDrift * (10.0 * sin(uTime * 0.16)
                                 + 3.0 * sin(uTime * 0.041 + 1.7));
    float shear = uSpiralDrift * 1.5 * sin(len * 6.2831 * 2.0 + uTime * 0.21);

    // perpendicular rings + a helical spiral (around + length), both scrolling
    // (uSpiralDir lets a biome run the swirl against the fall)
    float ringCoord = len * uRings - scroll + wave;
    float spiralCoord = around * uArms + len * uSpiralTurns - scroll * uSpiralDir + drift + shear;
    // ring: single line, or the Court's crisp twin (offset > AA width so the
    // pair never fuses); narrower twins = the authoritative look
    float ringS = lineMask(ringCoord, 0.06 * max(brW, 0.5));
    float dblOff = 0.10 + 0.06 * uRingDouble;
    float ringD = max(lineMask(ringCoord - dblOff, 0.035), lineMask(ringCoord + dblOff, 0.035));
    float ring = mix(ringS, ringD, uRingDouble);
    // chain links (Chain Court): alternate which rail of the twin rings is
    // bright around the circumference, so the pair reads as interlocked links
    // zig-zagging between two rails instead of parallel lines. INTEGER link
    // count (wrap-safe); the soft square wave carries its own fwidth AA.
    if (uChain > 0.001) {
      float lc = around * 14.0;
      float lw = clamp(1.5 * fwidth(lc), 0.05, 0.35);
      float tri = fract(lc);
      float sq = smoothstep(0.25 - lw, 0.25 + lw, tri) * (1.0 - smoothstep(0.75 - lw, 0.75 + lw, tri));
      float railA = lineMask(ringCoord - dblOff, 0.05);
      float railB = lineMask(ringCoord + dblOff, 0.05);
      float links = max(railA * mix(1.0, 0.3, sq), railB * mix(0.3, 1.0, sq));
      ring = mix(ring, links, uChain);
    }
    // doorframe dash (II): segment rings into door-post arcs. uDashCount posts
    // (integer: wrap-safe; classic 10), uDashDuty duty, own fwidth AA.
    float segC = around * uDashCount;
    float segD = abs(fract(segC) - 0.5) * 2.0;
    float dw = clamp(1.5 * fwidth(segC), 0.03, 0.4);
    float dash = 1.0 - smoothstep(uDashDuty - dw, uDashDuty + dw, segD);
    ring *= mix(1.0, dash, uRingDash);
    float spiral = lineMask(spiralCoord, 0.05);
    // kaleidoscope (Hall of Mirrors): re-run the spiral on a FOLDED
    // circumference (integer fold count -> wrap-safe triangle wave) so the arms
    // reflect into chevrons meeting at mirror seams. Masks are mixed, never
    // coordinates - a coordinate lerp would break the vUv wrap mid-ease.
    if (uKaleido > 0.001) {
      float aFold = abs(fract(around * 3.0) - 0.5) * 2.0;
      float spiralK = lineMask(aFold * uArms + len * uSpiralTurns - scroll * uSpiralDir + drift + shear, 0.05);
      spiral = mix(spiral, spiralK, uKaleido);
    }
    // waterline mirror (Mirror Lake): fold the circumference across the tube's
    // equator (seam-continuous: 0.5-abs(a-0.5) meets itself at the wrap) so the
    // lower half reflects the upper, a slow ripple bending the reflection like
    // water barely moving; a soft glow band marks the waterline itself.
    float waterline = 0.0;
    if (uMirrorY > 0.001) {
      float aMir = 0.5 - abs(around - 0.5);
      float ripple = 0.05 * uMirrorY * sin(len * 6.2831 * 5.0 + uTime * 0.8);
      float spiralM = lineMask((aMir + ripple) * uArms + len * uSpiralTurns - scroll * uSpiralDir + drift + shear, 0.05);
      spiral = mix(spiral, spiralM, uMirrorY);
      waterline = uMirrorY * lineMask(around + 0.5, 0.09) * (0.55 + 0.45 * sin(uTime * 0.9));
    }
    // filigree (Coronation gold / Vertigo's wrong-way twin): a second, thinner
    // spiral wound the OPPOSITE hand and scrolling the OPPOSITE way, so the two
    // swirls cross and shear against each other. Integer terms - wrap-safe.
    float filigree = 0.0;
    if (uFiligree > 0.001) {
      filigree = uFiligree
        * lineMask(around * uArms - len * uSpiralTurns + scroll * uSpiralDir - drift * 0.6, 0.032);
    }
    // biome fades: a biome can dim its linework toward nothing (Terminal
    // Velocity keeps little but streaks; deep-dark biomes keep their beams).
    ring   *= 1.0 - uRingFade;
    spiral *= 1.0 - uSpiralFade;

    // intermittent glow: travelling pulses down the tube + a global shimmer.
    // the flicker RATE ramps with depth (uGlowRate): only the time term is
    // scaled, so the line spacing is unchanged - just how fast they pulse.
    float gr = 0.1 + 0.35 * uGlowRate;   // slow up top -> fast deep (flicker cut ~3x - lines flash gently)
    float globalFlash = pulse(uTime * 0.7 * gr, 6.0);
    float ringGlow   = 0.35 + 1.7 * pulse(ringCoord * 0.5 - uTime * 1.6 * gr, 3.0) + 1.1 * globalFlash;
    float spiralGlow = 0.45 + 2.0 * pulse(spiralCoord * 0.4 - uTime * 2.1 * gr, 3.0) + 0.9 * globalFlash;

    // breath (I): brightness follows the width swell, slightly lagged
    ringGlow   *= brG;
    spiralGlow *= brG;
    // heartbeat throb (III): hb/thr computed up top (the beat also drives the
    // communion beat-scroll); applied to the glows here.
    ringGlow   *= 1.0 + thr * 1.6;
    spiralGlow *= 1.0 + thr * 1.2;
    // metronome (IV): hard brief neon ticks at uStrobeHz (classic ~84bpm).
    // Amplitude is capped by the authored knob (<=0.65) and scaled CPU-side by
    // the effect-intensity dial - neon signage, never a full-screen strobe.
    float strobe = uStrobe * pulse(uTime * 6.2831 * uStrobeHz, 12.0);
    ringGlow   *= 1.0 + strobe * 2.2;
    spiralGlow *= 1.0 + strobe * 1.6;
    // marquee chase (Casino): two bulb-trains (the second dimmer, opposite
    // phase) running around every ring like casino signage. Single integer
    // circumference cycle - wrap-safe.
    if (uChase > 0.001) {
      float chase = pulse((around - uTime * 0.22) * 6.2831, 10.0)
                  + 0.45 * pulse((around + 0.5 - uTime * 0.22) * 6.2831, 10.0);
      ringGlow *= 1.0 + uChase * 2.6 * chase;
    }

    // rush: the faster the fall, the hotter the line work burns, plus streaky
    // longitudinal speed-lines that only exist at velocity
    ringGlow   *= 1.0 + uRush * 0.9;
    spiralGlow *= 1.0 + uRush * 0.7;
    float streak = lineMask(around * 24.0 + sin(len * 40.0) * 0.15, 0.03)
                 * pulse(len * 60.0 - uTime * 9.0, 2.0);
    // terminal velocity: a second, denser streak sheet that burns regardless
    // of actual fall speed - this biome is ALWAYS at speed. Integer cycles.
    float streak2 = 0.0;
    if (uStreaks > 0.001) {
      streak2 = uStreaks
        * lineMask(around * 32.0 + 0.5 + sin(len * 6.2831 * 8.0 + 2.1) * 0.12, 0.025)
        * pulse(len * 6.2831 * 10.0 - uTime * 11.0, 2.0);
    }

    vec3 col = base
      + uLineColor   * ring   * ringGlow
      + uSpiralColor * spiral * spiralGlow
      + uLineColor   * streak * uRush * 1.4
      + uLineColor   * streak2 * (1.2 + uRush * 0.6)
      + uLineColor   * uRush * 0.05
      + uSpiralColor * waterline * 0.8
      + mix(uSpiralColor, uLineColor, 0.4) * filigree * spiralGlow * 0.85
      // faint whole-wall swell on the heartbeat + the Court's whole-tube tick
      + uLineColor   * (thr * 0.05 + strobe * 0.1);

    // caustics (Undertow; a calm low dose on Mirror Lake): two interfering
    // sine fields sharpened into shifting water-light webs, a third slow field
    // breathing them in and out. Integer around/len frequencies - wrap-safe.
    if (uCaustic > 0.001) {
      float c1 = sin((around * 5.0 + len * 7.0) * 6.2831 + uTime * 0.55);
      float c2 = sin((around * 3.0 - len * 6.0) * 6.2831 - uTime * 0.4);
      float c3 = 0.6 + 0.4 * sin((around * 8.0 + len * 3.0) * 6.2831 + uTime * 0.85);
      col += uSpiralColor * uCaustic * pow(0.5 + 0.5 * c1 * c2, 4.0) * c3 * 0.6;
    }
    // stained glass (Chapel): pane cells between dark lead lines, each pane
    // leaning its own way between the two line colors with a slow devotional
    // shimmer. Integer cell counts (wrap-safe); the cells ride the wall at a
    // fraction of the ring rate so the glass belongs to the architecture.
    if (uArch > 0.001) {
      float ca = around * 9.0;
      float cl = len * 30.0 - scroll * 0.25;
      float lead = max(lineMask(ca, 0.05), lineMask(cl, 0.05));
      float h = hash1(floor(ca) * 7.0 + floor(cl) * 113.0);
      vec3 pane = mix(uLineColor, uSpiralColor, h);
      col += uArch * (pane * (0.10 + 0.08 * sin(uTime * 0.6 + h * 6.2831)) * (1.0 - lead)
                      - col * 0.45 * lead);
    }
    // CRT scan (Static): fine phosphor rows + a slow rolling hold-bar. The row
    // frequency sits far above the ring count, so its amplitude fades out
    // where the rows go sub-pixel (fwidth) instead of shimmering into moire.
    if (uScan > 0.001) {
      float sc = len * 400.0;
      float rows = 0.5 + 0.5 * sin(sc * 6.2831);
      float rowVis = clamp(1.5 - 3.0 * fwidth(sc), 0.0, 1.0);
      col *= 1.0 - uScan * 0.30 * rows * rowVis;
      col += uLineColor * uScan * 0.35 * pulse(len * 2.0 * 6.2831 - uTime * 0.5, 10.0);
    }
    // polka dots (Toybox): a candy dot-lattice riding the wall, alternate rows
    // brick-offset like wrapping paper, each dot leaning toward one of the two
    // line colors with a slow twinkle. Integer counts (wrap-safe); amplitude
    // fades where the cells go sub-pixel.
    if (uDots > 0.001) {
      float dl = len * 40.0 - scroll * 0.4;
      float rowId = floor(dl);
      float da = around * 12.0 + 0.5 * mod(rowId, 2.0);
      vec2 dc = vec2(fract(da) - 0.5, fract(dl) - 0.5);
      float dotM = 1.0 - smoothstep(0.13, 0.22, length(dc));
      float dvis = clamp(1.5 - 3.0 * fwidth(dl), 0.0, 1.0);
      float dh = hash1(floor(da) * 11.0 + rowId * 57.0);
      col += mix(uLineColor, uSpiralColor, dh) * dotM * dvis * uDots
           * (0.35 + 0.25 * sin(uTime * 1.3 + dh * 6.2831));
    }
    // private browsing (Incognito): the faint alpha-transparency checker every
    // browser draws behind "nothing", plus wide dark redaction bars crawling
    // slowly down the tube. Smooth integer-frequency sines - wrap-safe, no
    // hard edges to alias.
    if (uChecker > 0.001) {
      float ch = sin(around * 6.2831 * 8.0) * sin(len * 6.2831 * 12.0 - scroll * 0.63);
      col *= 1.0 + uChecker * 0.10 * ch;
      float bar = lineMask(len * 5.0 - uTime * 0.05, 0.30);
      col *= 1.0 - uChecker * 0.45 * bar;
    }
    // picture frames (Gallery): gilt rectangular frames tiling the wall
    // between the column posts, pane interiors dimmed a touch so the frames
    // hang on marble. Integer cell counts; frames ride the wall.
    if (uPanel > 0.001) {
      float pa2 = around * 10.0;
      float pl2 = len * 35.0 - scroll * 0.27;
      vec2 pc = vec2(abs(fract(pa2) - 0.5), abs(fract(pl2) - 0.5));
      float inner = max(pc.x, pc.y);
      float frame = smoothstep(0.34, 0.38, inner) * (1.0 - smoothstep(0.44, 0.48, inner));
      float interior = 1.0 - smoothstep(0.30, 0.36, inner);
      float pvis = clamp(1.5 - 2.0 * fwidth(pl2), 0.0, 1.0);
      col += uSpiralColor * frame * pvis * uPanel * (0.30 + 0.10 * sin(uTime * 0.5));
      col *= 1.0 - uPanel * 0.10 * interior;
    }

    // lightning strike: a brief whole-tube glare that favours the line work
    col += uFlash * (0.18 + ring * 1.4 + spiral * 0.9) * vec3(0.95, 0.8, 1.05);

    // the Grey Ward: drain the wall of color - strikes included - with a faint
    // paper grain so the grey reads as texture, not banding. Media bubbles are
    // DOM/video textures and keep theirs: that contrast IS the lesson. Applied
    // before the hole rim so the junction portal cue stays a colored signal.
    if (uDesat > 0.001) {
      float lum = dot(col, vec3(0.299, 0.587, 0.114));
      col = mix(col, vec3(lum), uDesat);
      float grain = hash1(floor(around * 220.0) + floor(len * 700.0) * 3.0 + floor(uTime * 9.0) * 7.0);
      col += vec3((grain - 0.5) * 0.05 * uDesat);
    }

    // branch mouths: discard the wall where a diverging vein pierces it, and light
    // a glowing rim on the cut boundary so the seam reads as a portal (bloom-fed).
    float holeEdge = 1e9;
    for (int i = 0; i < MAX_HOLES; i++) {
      if (i >= uHoleCount) break;
      vec3 d = vWorld - uHolePos[i];
      float al = dot(d, uHoleAxis[i]);
      vec3 pe = d - al * uHoleAxis[i];
      float dr = length(pe);
      if (al > -uHoleBack && al < uHoleFwd) {
        if (dr < uHoleR[i]) discard;
        holeEdge = min(holeEdge, dr - uHoleR[i]);
      }
    }
    float holeRim = 1.0 - smoothstep(0.0, 0.9, holeEdge);
    col += uRimColor * holeRim * (2.4 + 0.6 * sin(uTime * 2.0));

    // exp2 fog to match scene.fog; the dead-end cut fades to the same live fog
    // color (fx.js region tint), so it blends with distance haze in every region
    float f = 1.0 - exp(-uFogDensity * uFogDensity * vFogDepth * vFogDepth);
    col = mix(col, uFogColor, clamp(max(f, cutFade), 0.0, 1.0));
    gl_FragColor = vec4(col, 1.0);
  }`;

// Build the tunnel mesh around a loop layout. `uScroll` is driven by the scene
// each frame (faster fall = faster ring travel).
export function createTunnel(layout) {
  const geo = new THREE.TubeGeometry(
    layout.spine, Math.max(400, Math.round(LOOP_DEPTH * Q.tubeSegMult)), RADIUS, Q.tubeRadial, true);
  const mat = new THREE.ShaderMaterial({
    uniforms: {
      uTime: { value: 0 },
      uBg1: { value: new THREE.Color(0x2b1024) },
      uBg2: { value: new THREE.Color(0x160a18) },
      uLineColor: { value: new THREE.Color(0xff69b4) },   // pink rings
      uSpiralColor: { value: new THREE.Color(0xe56cc0) }, // pink-violet spiral
      uFogColor: { value: new THREE.Color(FOG_COLOR) },
      uFogDensity: { value: FOG_DENSITY },
      uRings: { value: Math.round(LOOP_DEPTH / 8) },        // 125 - integer: loop-seam free
      uSpiralTurns: { value: Math.round(LOOP_DEPTH / 22) }, // 45  - integer: loop-seam free
      uArms: { value: 4.0 },
      uScroll: { value: 0.8 },
      uFlash: { value: 0 }, // lightning glare, driven by fx.js
      uRush: { value: 0 },  // speed heat, driven by scene.js (0 calm - 1 flying)
      uGlowRate: { value: 0 }, // ray-flicker rate ramp, driven by fx.js (0 shallow - 1 deep)
      // chamber pattern knobs, eased by fx.js from the region styles (0 = classic)
      uBreath: { value: 0 },
      uRingDash: { value: 0 },
      uLineWave: { value: 0 },
      uRingDouble: { value: 0 },
      uSpiralDrift: { value: 0 },
      uThrob: { value: 0 },
      uStrobe: { value: 0 },
      // biome pattern params (SNAPPED by fx.js at the chamber commit - counts
      // and frequencies stay integers; direction never glides)
      uDashCount: { value: 10 },
      uDashDuty: { value: 0.62 },
      uWaveF1: { value: 3 },
      uWaveF2: { value: 7 },
      uStrobeHz: { value: 1.4 },
      uSpiralDir: { value: 1 },
      // biome pattern knobs, eased by fx.js exactly like the chamber knobs
      uScan: { value: 0 }, uGlitch: { value: 0 }, uKaleido: { value: 0 },
      uChase: { value: 0 }, uChain: { value: 0 }, uCaustic: { value: 0 },
      uArch: { value: 0 }, uMirrorY: { value: 0 }, uStreaks: { value: 0 },
      uRingFade: { value: 0 }, uSpiralFade: { value: 0 },
      uSurge: { value: 0 }, uSlip: { value: 0 }, uBeatScroll: { value: 0 },
      uDots: { value: 0 }, uChecker: { value: 0 }, uDesat: { value: 0 },
      uFiligree: { value: 0 }, uPanel: { value: 0 },
      // branch-hole carving (junctions.js sets these; 0 count => idle/no overhead)
      uHoleCount: { value: 0 },
      uHolePos: { value: Array.from({ length: MAX_HOLES }, () => new THREE.Vector3()) },
      uHoleAxis: { value: Array.from({ length: MAX_HOLES }, () => new THREE.Vector3(0, 0, 1)) },
      uHoleR: { value: new Array(MAX_HOLES).fill(0) },
      uHoleBack: { value: RADIUS * 0.35 },
      uHoleFwd: { value: RADIUS * 3.0 }, // a ~45deg vein finishes crossing the wall ~13u along its
                                         // axis (near lip ~5u, + a tube-width of oblique travel);
                                         // any shortfall leaves a wall arc slicing across the mouth
      uRimColor: { value: new THREE.Color(0xff8fd8) },
      // forward dead-end cut (junctions.js sets this; off => no overhead)
      uCutOn: { value: 0 },
      uCutHide: { value: 0 },
      uCutLo: { value: 0 },
      uCutHi: { value: 0 },
    },
    vertexShader: TUBE_VERT,
    fragmentShader: TUBE_FRAG,
    side: THREE.BackSide,
    extensions: { derivatives: true }, // fwidth() line AA (core on WebGL2; flag is a WebGL1 safety net)
  });
  let geoRef = geo;
  const mesh = new THREE.Mesh(geo, mat);

  // junctions.js hands us up to MAX_HOLES branch mouths, each {point, axis, r}
  // (world space). Writing uHoleCount = 0 restores a solid wall.
  function setHoles(holes) {
    const n = Math.min(MAX_HOLES, holes ? holes.length : 0);
    for (let i = 0; i < n; i++) {
      mat.uniforms.uHolePos.value[i].copy(holes[i].point);
      mat.uniforms.uHoleAxis.value[i].copy(holes[i].axis).normalize();
      mat.uniforms.uHoleR.value[i] = holes[i].r;
    }
    mat.uniforms.uHoleCount.value = n;
  }
  function clearHoles() { mat.uniforms.uHoleCount.value = 0; }
  // fade a forward stretch of the trunk to fog in arc-length space [lo,hi] (0..1,
  // wraps if hi<lo) so a fork reads as a hazy dead-end (the wall stays as an
  // opaque enclosure - never a black void). clearCut() restores the wall.
  // hide=true DISCARDS the window instead - only safe while something else
  // encloses the camera (the ridden vein, during a junction dive).
  function setCut(lo, hi, hide = false) {
    mat.uniforms.uCutLo.value = ((lo % 1) + 1) % 1;
    mat.uniforms.uCutHi.value = ((hi % 1) + 1) % 1;
    mat.uniforms.uCutHide.value = hide ? 1 : 0;
    mat.uniforms.uCutOn.value = 1;
  }
  function clearCut() { mat.uniforms.uCutOn.value = 0; mat.uniforms.uCutHide.value = 0; }

  return {
    mesh, material: mat,
    setHoles, clearHoles, setCut, clearCut,
    // Rebase: swap ONLY the geometry onto a fresh loop spine, keeping the same
    // material so fx.js's tunnelMat binding + all tint/warp uniforms survive.
    rebuild(newLayout) {
      const ng = new THREE.TubeGeometry(
        newLayout.spine, Math.max(400, Math.round(LOOP_DEPTH * Q.tubeSegMult)), RADIUS, Q.tubeRadial, true);
      mesh.geometry = ng;
      geoRef.dispose();
      geoRef = ng;
      clearHoles();
      clearCut();  // the cut window was in the OLD arc-length space; the fresh loop is whole
    },
    dispose() { geoRef.dispose(); mat.dispose(); },
  };
}
