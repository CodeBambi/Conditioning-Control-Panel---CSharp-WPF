/* ============================================================================
 * biomes.js - THE BIOMES: place-flavor variants of the four Journey Rooms.
 *
 * Every room (regions.js) owns up to four biomes; a fresh descent rolls ONE per
 * room, so a run reads as e.g. Toybox -> Grey Ward -> Gallery -> Coronation and
 * no two descents dress (or PLAY) alike. A biome is data-only:
 *
 *   id / name / glyph / tagline - identity (arrival card, recap, diary)
 *   style     - a COMPLETE regions.js-shaped style (palette / knobs / density /
 *               bloom / boosts / field). It REPLACES the room's classic style
 *               for the chamber - the room keeps its band/numeral/title, the
 *               biome owns the look. Same rules as regions.js: density counts
 *               must be integers (seam-free treadmill), strobe is capped by the
 *               player's effect-intensity dial downstream.
 *   weatherId - optional sky override. undefined = the room's default sky;
 *               null = explicitly open sky.
 *   profile   - optional spawn-profile override (density/behavioral/echo/...).
 *               undefined = the room's classic profile.
 *   wx        - biome scalar hooks folded into the SAME math as weather
 *               modifiers (payMult / heatGain / fuseMult / goldenBonus). They
 *               multiply on top of whatever sky is up - a biome is the land,
 *               the weather is the sky.
 *   mech      - id of this biome's mechanic in game/biomeMech.js (the part
 *               that makes a biome a place you PLAY, not just a re-tint).
 *   mechHint  - one plain-language line on how the mechanic plays; the quiet
 *               grey note under the flavour line (roulette settle card,
 *               arrival card, HUD biome chip).
 *   veinFx    - the junction/draft-room corridor dress ({accent, <knob>:
 *               weight}): the doorway tubes wear the biome's signature
 *               pattern in its accent color, on top of each door's identity
 *               color. Knob names = junctions.js VEIN_FX_KEYS (dots, scan,
 *               chase, chain, caustic, arch, mirrorY, streaks, beam, helix/
 *               helix2, kaleido, drift, desat, ringFade, breath/throb/strobe/
 *               flicker, surge/slip/beat). Forks dress as the biome you're
 *               IN; a draft room dresses as the one its doors lead into,
 *               only after the roulette lands (no early hint).
 *
 * All FOUR slots per room are live (wave 1: Toybox/Keyhole/Mirrors/Grey Ward/
 * Gallery/Casino/Velocity/Coronation; wave 2: Static/Incognito/Vertigo/
 * Searchlight/Chain Court/Undertow/Chapel/Mirror Lake). Legacy/scripted runs
 * roll NO biome and fall back to the room's classic Four Chambers dress.
 *
 * New wave-2 fields: `audio` names a master-bus color (audioBus setAudioColor:
 * 'muffled' | 'underwater') the mechanic applies on entry and lifts on exit.
 *
 * BIOME IDENTITY PASS: beyond the seven chamber knobs, styles may now use the
 * biome pattern knobs (eased like the others; all 0 = classic): scan, glitch,
 * kaleido, chase, chain, caustic, arch, mirrorY, streaks, ringFade,
 * spiralFade, surge, slip, beatScroll, dots, checker, desat, filigree, panel
 * - each owned by one or two biomes so no two places share a silhouette.
 * `style.params` re-parameterizes the base geometry (dashCount / dashDuty /
 * waveF1 / waveF2 / strobeHz / spiralDir); params SNAP at the chamber commit
 * and counts/frequencies must be INTEGERS (seam-free treadmill). glitch +
 * strobe are scaled by the player's effect-intensity dial downstream
 * (photosensitivity). `style.ambient` names a fieldFx ambient particle field
 * ({kind, color | colors, count?} - confetti/motes/specks/ash/glints/bubbles/
 * coins/embers/goldleaf/petals); omit it for biomes whose field identity is
 * already carried by a mechanic (Chain Court tethers, Vertigo motion).
 * ==========================================================================*/

import { REGION_COUNT } from './regions.js';

export const BIOMES_BY_ROOM = {
  // ---- Room I - CURIOSITY & DENIAL ("it's just something silly") ----------
  1: [
    {
      id: 'toybox', name: 'The Toybox', glyph: '🧸',
      tagline: 'it’s all just toys in here',
      // candy pastels on soft plum - bright, bouncy, harmless-looking. Wide
      // breathing rings like a mobile over a crib, a polka-dot lattice like
      // wrapping paper, and confetti drifting down through the field.
      style: {
        palette: { colLine: 0xffb3d9, colSpiral: 0x9be8ff, colFog: 0x241426,
                   colBg: 0x2b1a30, colRibbon: 0xffc7e8, colSparkle: 0xfff0fa },
        knobs: { enter: { breath: 0.85, dots: 0.50 },
                 peak:  { breath: 0.60, lineWave: 0.10, dots: 0.80 } },
        density: { rings: 100, spiralTurns: 39, arms: 3 },
        bloom: 1.0, sparkleBoost: 0.2,
        field: { residue: [255, 179, 217], sparkleHue: 200 },
        ambient: { kind: 'confetti', colors: [[255, 179, 217], [155, 232, 255], [255, 240, 250]] },
      },
      // busier than the classic Long Fall but softer: lots of plain bubbles
      // ("toys"), almost no menagerie pressure.
      profile: { density: 0.95, behavioral: 0.30, echo: 0.4, chaperone: 0.5, bound: 0.3, tease: 0.6 },
      wx: { fuseMult: 1.15 },   // kind fuses - nothing here means anything, right?
      mech: 'mimics',
      mechHint: 'about 1 in 7 toys flips when popped: a flash slips out and that pop pays ×3',
      // wrapping-paper corridor: candy polka dots + a crib-mobile breath
      veinFx: { accent: 0x9be8ff, dots: 1.0, breath: 0.7 },
    },
    {
      id: 'keyhole', name: 'The Keyhole', glyph: '🗝',
      tagline: 'you only looked because the light was there',
      // near-dark brass-and-candlelight: the linework fades toward nothing so
      // the roaming keyhole beams (biomeMech) are the only bright thing, with
      // warm dust motes hanging in the candle air.
      style: {
        palette: { colLine: 0x8a6a48, colSpiral: 0x6a5030, colFog: 0x0a0705,
                   colBg: 0x0d0a06, colRibbon: 0x92703f, colSparkle: 0xffdca0 },
        knobs: { enter: { breath: 0.30, ringFade: 0.25, spiralFade: 0.35 },
                 peak:  { breath: 0.30, throb: 0.15, ringFade: 0.40, spiralFade: 0.55 } },
        density: { rings: 105, spiralTurns: 39, arms: 3 },
        bloom: 0.7,
        field: { residue: [255, 214, 150], sparkleHue: 40 },
        ambient: { kind: 'motes', color: [255, 214, 150] },
      },
      profile: { density: 0.70, behavioral: 0.35, echo: 0.4, chaperone: 0.6, bound: 0.4, tease: 0.7 },
      wx: {},
      mech: 'keyhole',
      mechHint: 'two candle beams roam the dark: pop inside the light for ×1.6 pay, outside it only ×0.8',
      // candlelit throat: the linework sinks toward dark and what's left wavers
      veinFx: { accent: 0xffdca0, flicker: 1.0, ringFade: 0.5 },
    },
    {
      id: 'static', name: 'Late Night Static', glyph: '📺',
      tagline: 'the channel you only find after midnight',
      weatherId: 'static',   // the storm sky IS the broadcast
      // 2am phosphor: CRT teal on near-black - scanline rows with a rolling
      // hold-bar, whole rows tearing sideways when the signal drops, the wall
      // slipping vertical hold, and a nervous fast tick like a set that hasn't
      // warmed up. Fine 16-post dash reads as broadcast bars.
      style: {
        palette: { colLine: 0x5ee8d0, colSpiral: 0x2f8f84, colFog: 0x061012,
                   colBg: 0x081416, colRibbon: 0x53d8c4, colSparkle: 0xc8fff4 },
        knobs: { enter: { ringDash: 0.60, strobe: 0.10, scan: 0.55, glitch: 0.20, slip: 0.45 },
                 peak:  { ringDash: 0.85, strobe: 0.18, lineWave: 0.08, scan: 0.85, glitch: 0.45, slip: 0.65 } },
        params: { dashCount: 16, strobeHz: 2.3 },
        density: { rings: 110, spiralTurns: 39, arms: 3 },
        bloom: 0.9, sparkleBoost: 0.15,
        field: { bolt: [110, 240, 220], boltCore: [220, 255, 248],
                 residue: [110, 240, 220], sparkleHue: 172 },
        ambient: { kind: 'specks', color: [200, 255, 244] },
      },
      profile: { density: 0.92, behavioral: 0.35, echo: 0.5, chaperone: 0.5, bound: 0.3, tease: 0.8 },
      wx: {},
      mech: 'flicker',
      mechHint: 'the signal rolls in and out (screen tints teal when it is IN): on-signal pops pay ×2, snow pops ×0.5',
      // a dying broadcast: phosphor rows, row-tear bursts, vertical-hold slip
      veinFx: { accent: 0x5ee8d0, scan: 0.9, glitch: 0.5, slip: 0.6 },
    },
    {
      id: 'incognito', name: 'Incognito', glyph: '🕶',
      tagline: 'nobody has to know. delete it after.',
      // graphite and deep navy, lights kept low - a browser window at 1am with
      // the brightness turned down and one ear on the hallway. The wall wears
      // the faint alpha-transparency checker of a page with nothing on it,
      // and wide redaction bars crawl past like history being blacked out.
      style: {
        palette: { colLine: 0x5a6478, colSpiral: 0x39415a, colFog: 0x0b0d14,
                   colBg: 0x0d1018, colRibbon: 0x4a5470, colSparkle: 0x8fa0c0 },
        knobs: { enter: { breath: 0.25, checker: 0.40 },
                 peak:  { breath: 0.30, ringDash: 0.35, checker: 0.70 } },
        density: { rings: 100, spiralTurns: 39, arms: 3 },
        bloom: 0.7,
        field: { residue: [140, 160, 200], sparkleHue: 225 },
        ambient: { kind: 'motes', color: [140, 160, 200], count: 10 },
      },
      // more media pressure than the classic Long Fall: history ACCUMULATES,
      // that's the whole point of clearing it.
      profile: { density: 1.00, behavioral: 0.30, echo: 0.7, chaperone: 0.5, bound: 0.3, tease: 0.9 },
      wx: {},
      mech: 'incognito',
      // two things happen here: every bubble spawns ANONYMOUS (soft treats fade to
      // a plain blue soap bubble, live effects blur to a generic one that still
      // carries its fuse) so you can't read the field — and the room clears its own
      // history on a shrinking timer, melting whatever is still on screen.
      mechHint: 'every bubble spawns ANONYMOUS — soft treats fade plain, live ones blur, so you can’t tell them apart: pop blind. HISTORY CLEARED wipes hit every ~40s (5s warning): everything still on screen melts for +2 🪙 each',
      // an empty page at 1am: alpha-checker wall, redaction bars crawling past
      veinFx: { accent: 0x8fa0c0, checker: 0.85, ringFade: 0.25 },
    },
  ],

  // ---- Room II - FEAR & CONFUSION ("what if it isn't a game?") ------------
  2: [
    {
      id: 'mirrors', name: 'Hall of Mirrors', glyph: '🪞',
      tagline: 'everywhere you look… it’s this one, isn’t it?',
      // silvered prismatic frames: crisp twin rings read as endless mirror
      // edges, and the spiral folds kaleidoscopic - six arms reflecting into
      // chevrons that meet at mirror seams, wandering like a reflection that
      // isn't quite yours.
      style: {
        palette: { colLine: 0xcfd8e8, colSpiral: 0x9fb8ff, colFog: 0x10141f,
                   colBg: 0x131826, colRibbon: 0xbde0ff, colSparkle: 0xffffff },
        knobs: { enter: { ringDouble: 0.75, spiralDrift: 0.30, kaleido: 0.45 },
                 peak:  { ringDouble: 1.00, spiralDrift: 0.55, kaleido: 1.00 } },
        density: { rings: 125, spiralTurns: 45, arms: 6 },
        bloom: 1.15, sparkleBoost: 0.25,
        field: { bolt: [190, 210, 255], boltCore: [240, 246, 255],
                 residue: [200, 216, 255], sparkleHue: 220 },
        ambient: { kind: 'glints', color: [220, 230, 255] },
      },
      profile: { density: 0.92, behavioral: 0.95, echo: 0.7, chaperone: 1.8, bound: 1.8, tease: 0.9 },
      wx: {},
      mech: 'mirrors',
      mechHint: 'every ~40s a 9-second mirror moment turns everything into your favorite: those pops pay ×1.5',
      // silvered corridor: a helix folded kaleidoscopic into mirror chevrons
      veinFx: { accent: 0x9fb8ff, helix: 0.9, kaleido: 1.0 },
    },
    {
      id: 'greyward', name: 'The Grey Ward', glyph: '🌫',
      tagline: 'the only color left is the one you keep looking at',
      // the world drained: charcoal tube, ash fog, dim bloom - and an in-shader
      // desaturation that drains even strikes and tint bleed to grey grain, ash
      // sifting down through the field. The media - your gifs and videos - are
      // the ONLY color on screen, and the contrast IS the lesson.
      style: {
        palette: { colLine: 0x767c85, colSpiral: 0x585e66, colFog: 0x121316,
                   colBg: 0x16171b, colRibbon: 0x6a7078, colSparkle: 0x9aa0a8 },
        knobs: { enter: { lineWave: 0.06, desat: 0.55 },
                 peak:  { lineWave: 0.10, throb: 0.12, desat: 0.85 } },
        density: { rings: 120, spiralTurns: 45, arms: 4 },
        bloom: 0.55,
        field: { bolt: [150, 156, 165], boltCore: [220, 224, 230],
                 residue: [150, 156, 165], sparkleHue: 220 },
        ambient: { kind: 'ash', color: [150, 156, 165] },
      },
      profile: { density: 0.88, behavioral: 0.90, echo: 0.6, chaperone: 1.6, bound: 1.6, tease: 1.0 },
      wx: { heatGain: 0.5 },   // lust only warms where the color is (mech re-adds it on media touch)
      mech: 'greyward',
      mechHint: 'only the media holds color: go 7s without popping or grabbing any and your streak greys away fast',
      // the drain starts at the door: the corridor greys out, grain like ash
      veinFx: { accent: 0x9aa0a8, desat: 0.8, breath: 0.35 },
    },
    {
      id: 'vertigo', name: 'Vertigo', glyph: '🙃',
      tagline: 'up stopped meaning anything',
      // sick violet and wrong green: the main spiral scrolls BACKWARDS
      // (spiralDir -1) while a counter-handed twin runs the other way, the
      // drift stalling and shearing both - a staircase that turns out to be a
      // ceiling.
      style: {
        palette: { colLine: 0x9a7bff, colSpiral: 0x7bffb8, colFog: 0x120a1c,
                   colBg: 0x150d22, colRibbon: 0x8a6ae8, colSparkle: 0xd8ccff },
        knobs: { enter: { spiralDrift: 0.70, lineWave: 0.10, filigree: 0.45 },
                 peak:  { spiralDrift: 1.00, lineWave: 0.16, throb: 0.15, filigree: 0.75 } },
        params: { spiralDir: -1 },
        density: { rings: 120, spiralTurns: 51, arms: 4 },
        bloom: 1.0,
        field: { bolt: [154, 123, 255], boltCore: [230, 220, 255],
                 residue: [123, 255, 184], sparkleHue: 260 },
      },
      profile: { density: 0.90, behavioral: 0.90, echo: 0.6, chaperone: 1.5, bound: 1.4, tease: 0.9 },
      wx: {},
      mech: 'vertigo',
      mechHint: 'a 3·2·1 count flips gravity for 7s: fuses burn ×1.4 hotter and snaps finished mid-flip pay ×2',
      // two helixes fighting each other + a stalling drift: up stops meaning anything
      veinFx: { accent: 0x7bffb8, helix: 0.7, helix2: 0.85, drift: 1.0 },
    },
    {
      id: 'searchlight', name: 'The Searchlight', glyph: '🔦',
      tagline: 'what if someone sees?',
      // near-black steel ward under a held breath: the linework fades almost
      // to nothing - the sweeping beams (biomeMech) are what shows you the
      // walls - sparse cold dust drifting, a heartbeat throb in the dark.
      style: {
        palette: { colLine: 0x3d4450, colSpiral: 0x2a3038, colFog: 0x05060a,
                   colBg: 0x07080d, colRibbon: 0x39404c, colSparkle: 0xcfe0ff },
        knobs: { enter: { throb: 0.35, breath: 0.15, ringFade: 0.50, spiralFade: 0.60 },
                 peak:  { throb: 0.55, breath: 0.20, ringFade: 0.70, spiralFade: 0.80 } },
        density: { rings: 95, spiralTurns: 39, arms: 3 },
        bloom: 0.6,
        field: { bolt: [170, 190, 255], boltCore: [235, 242, 255],
                 residue: [170, 190, 255], sparkleHue: 222 },
        ambient: { kind: 'motes', color: [170, 190, 255], count: 10 },
      },
      profile: { density: 0.80, behavioral: 1.00, echo: 0.6, chaperone: 1.7, bound: 1.6, tease: 0.8 },
      wx: {},
      mech: 'searchlight',
      mechHint: 'pops in the dark pay ×1.35, inside a beam only ×0.6 — linger in the light and you are SPOTTED: nearby fuses enrage',
      // near-black steel throat: sweeping beams are what shows you the walls
      veinFx: { accent: 0xcfe0ff, beam: 1.0, ringFade: 0.6, throb: 0.45 },
    },
  ],

  // ---- Room III - BARGAIN & STRUGGLE ("just this once") -------------------
  3: [
    {
      id: 'gallery', name: 'The Gallery', glyph: '🏛',
      tagline: 'look. don’t touch.',
      // ivory marble and gilt: chunky column posts (12 posts, wide duty), gilt
      // picture frames tiling the wall between them, dust hanging in museum
      // light - everything pristine and bright, and you are very much being
      // watched in it.
      style: {
        palette: { colLine: 0xe8dcc0, colSpiral: 0xd4af6a, colFog: 0x211b10,
                   colBg: 0x272013, colRibbon: 0xd4af37, colSparkle: 0xfff2cc },
        knobs: { enter: { ringDash: 0.55, breath: 0.15, panel: 0.50 },
                 peak:  { ringDash: 0.75, breath: 0.15, throb: 0.20, panel: 0.75 } },
        params: { dashCount: 12, dashDuty: 0.55 },
        density: { rings: 130, spiralTurns: 45, arms: 4 },
        bloom: 1.05, ribbonBoost: 0.2,
        field: { residue: [230, 205, 140], sparkleHue: 46 },
        ambient: { kind: 'motes', color: [255, 242, 204] },
      },
      profile: { density: 1.10, behavioral: 1.00, echo: 1.6, chaperone: 0.9, bound: 0.8, tease: 1.6 },
      wx: {},
      mech: 'gallery',
      mechHint: 'grabs melt in your hands (−3 streak): keeping hands off builds pay up to ×2, and 10s TOUCH PERMITTED windows let you binge',
      // museum corridor: gilt picture frames tiling the marble, dust-still
      veinFx: { accent: 0xd4af6a, panel: 0.9, breath: 0.2 },
    },
    {
      id: 'casino', name: 'Fool’s Casino', glyph: '🎰',
      tagline: 'just one more. you can stop whenever you want.',
      weatherId: 'foolsgold',   // the house sky: gold rains here
      // green felt under gold neon: marquee-dash rings with bulb-trains chasing
      // around them like casino signage, a slot-reel tick, gold sparkle rain.
      // Everything about it says the odds are in your favor.
      style: {
        palette: { colLine: 0xffd700, colSpiral: 0x3ecf6a, colFog: 0x0a170d,
                   colBg: 0x0e1f12, colRibbon: 0xffc84a, colSparkle: 0xfff0a0 },
        knobs: { enter: { ringDash: 0.60, strobe: 0.14, chase: 0.55 },
                 peak:  { ringDash: 0.80, strobe: 0.22, throb: 0.20, chase: 0.90 } },
        params: { dashCount: 12, strobeHz: 1.9 },
        density: { rings: 130, spiralTurns: 51, arms: 4 },
        bloom: 1.2, sparkleBoost: 0.3,
        field: { bolt: [255, 215, 80], boltCore: [255, 245, 200],
                 residue: [255, 215, 80], sparkleHue: 50 },
        ambient: { kind: 'coins', color: [255, 215, 80] },
      },
      profile: { density: 1.20, behavioral: 1.05, echo: 1.8, chaperone: 0.8, bound: 0.9, tease: 1.2 },
      wx: { goldenBonus: 0.02 },   // stacked deck: goldens land noticeably more often
      mech: 'casino',
      mechHint: 'a golden pop puts its gold in a pot: golden again doubles it (up to 3 times), a detonation burns it, 20s quiet cashes out',
      // the house's hallway: gold bulb-trains chasing every ring, a slot tick
      veinFx: { accent: 0xffd700, chase: 1.0, strobe: 0.5 },
    },
    {
      id: 'chaincourt', name: 'The Chain Court', glyph: '⛓',
      tagline: 'you read the terms. you signed anyway.',
      // wine-dark red and old iron: the twin rings interlock into chain links
      // zig-zagging between two rails, the whole tube swaying on a slow
      // pendulum pulse. Everything here arrives bound to something else - so
      // do you.
      style: {
        palette: { colLine: 0xc23b4e, colSpiral: 0x7a2230, colFog: 0x1a0709,
                   colBg: 0x200a0d, colRibbon: 0xa83a48, colSparkle: 0xffb0b8 },
        knobs: { enter: { ringDouble: 0.50, throb: 0.20, chain: 0.55, surge: 0.20 },
                 peak:  { ringDouble: 0.80, throb: 0.35, ringDash: 0.30, chain: 0.90, surge: 0.30 } },
        density: { rings: 125, spiralTurns: 45, arms: 4 },
        bloom: 1.0, ribbonBoost: 0.15,
        field: { bolt: [230, 90, 110], boltCore: [255, 220, 226],
                 residue: [194, 59, 78], sparkleHue: 350 },
      },
      // bound pairs everywhere: the room's whole population signs in twos
      profile: { density: 1.05, behavioral: 1.10, echo: 1.2, chaperone: 1.0, bound: 2.6, tease: 1.3 },
      wx: {},
      mech: 'contracts',
      mechHint: 'every clean snap banks +2 🪙; signing a drifting contract pays 20 🪙 now — the balance comes due 45s later',
      // old iron gullet: the rings interlock into chain links on a slow pulse
      veinFx: { accent: 0xe65a6e, chain: 0.95, throb: 0.5 },
    },
    {
      id: 'undertow', name: 'The Undertow', glyph: '🌊',
      tagline: 'swim all you like. it has you.',
      // ink-water blues over black: slow heavy breath, one big kelp wave
      // rolling the rings (waveF 2 & 5 instead of the classic 3 & 7), caustic
      // light webs shifting on the wall, and the whole tube surging forward
      // and dragging back like a current that owns you.
      style: {
        palette: { colLine: 0x2f5fc8, colSpiral: 0x1b3a80, colFog: 0x050a18,
                   colBg: 0x071020, colRibbon: 0x2a55b0, colSparkle: 0x9fc8ff },
        knobs: { enter: { breath: 0.50, lineWave: 0.15, caustic: 0.50, surge: 0.55 },
                 peak:  { breath: 0.60, lineWave: 0.24, spiralDrift: 0.30, caustic: 0.80, surge: 0.85 } },
        params: { waveF1: 2, waveF2: 5 },
        density: { rings: 115, spiralTurns: 45, arms: 3 },
        bloom: 0.85,
        field: { bolt: [90, 150, 255], boltCore: [210, 230, 255],
                 residue: [90, 150, 255], sparkleHue: 218 },
        ambient: { kind: 'bubbles', color: [159, 200, 255] },
      },
      profile: { density: 1.00, behavioral: 1.00, echo: 1.4, chaperone: 0.9, bound: 0.9, tease: 1.4 },
      wx: {},
      mech: 'undertow',
      mechHint: 'currents drag everything to their eye and slow your snaps: win one inside for ×3 pay and 12s of still water',
      // a flooded pipe: caustic light webs, the flow surging forward and dragging back
      veinFx: { accent: 0x9fc8ff, caustic: 0.9, surge: 0.85 },
    },
  ],

  // ---- Room IV - SURRENDER & ACCEPTANCE ("yes. this is you.") -------------
  4: [
    {
      id: 'velocity', name: 'Terminal Velocity', glyph: '☄',
      tagline: 'nothing here can hurt you. fall.',
      // white-hot streaks on near-black: the linework recedes and a permanent
      // speed-streak sheet takes over - a starfield at warp regardless of the
      // actual fall speed. Max arms, hard strobe signage. Surrender as release
      // - fuses are so long they may as well not exist; only the flow can break.
      style: {
        palette: { colLine: 0xff8ad0, colSpiral: 0xffffff, colFog: 0x14060e,
                   colBg: 0x180810, colRibbon: 0xff9ad8, colSparkle: 0xffffff },
        knobs: { enter: { strobe: 0.40, ringDouble: 0.35, streaks: 0.65, ringFade: 0.35, spiralFade: 0.25 },
                 peak:  { strobe: 0.60, ringDouble: 0.50, throb: 0.20, streaks: 1.00, ringFade: 0.55, spiralFade: 0.40 } },
        density: { rings: 150, spiralTurns: 51, arms: 6 },
        bloom: 1.25, ribbonBoost: 0.35,
        field: { bolt: [255, 138, 208], boltCore: [255, 235, 248],
                 residue: [255, 150, 215], sparkleHue: 320 },
        ambient: { kind: 'embers', color: [255, 150, 215] },
      },
      profile: { density: 1.50, behavioral: 0.60, echo: 0.8, chaperone: 0.6, bound: 0.6, tease: 1.0 },
      wx: { fuseMult: 2.6 },   // fuses so long the field never threatens - the combo clock is the game
      mech: 'velocity',
      mechHint: 'full speed and fuses barely burn: but 4s without a pop and your streak starts bleeding away',
      // already at terminal velocity: white-hot streaks, the rings receding
      veinFx: { accent: 0xffffff, streaks: 1.0, ringFade: 0.45 },
    },
    {
      id: 'coronation', name: 'The Coronation', glyph: '👑',
      tagline: 'the run remembers everything you did',
      // royal purple and gold: crisp twin rings, a gold filigree spiral wound
      // the other way (the crown braid), gold leaf rising like the room is
      // exhaling honors. The throne room where your own descent is read back
      // to you as a verdict.
      style: {
        palette: { colLine: 0xb066ff, colSpiral: 0xffd700, colFog: 0x150a1f,
                   colBg: 0x190d25, colRibbon: 0xd4af37, colSparkle: 0xffe9b0 },
        knobs: { enter: { ringDouble: 0.80, throb: 0.25, filigree: 0.55 },
                 peak:  { ringDouble: 1.00, throb: 0.45, strobe: 0.25, filigree: 0.90 } },
        density: { rings: 140, spiralTurns: 45, arms: 4 },
        bloom: 1.3, ribbonBoost: 0.3,
        field: { bolt: [200, 140, 255], boltCore: [245, 230, 255],
                 residue: [212, 175, 55], sparkleHue: 275 },
        ambient: { kind: 'goldleaf', color: [255, 215, 80] },
      },
      profile: { density: 1.30, behavioral: 1.20, echo: 1.2, chaperone: 1.1, bound: 1.2, tease: 1.9 },
      wx: {},
      mech: 'coronation',
      mechHint: 'gold verdicts drift down reading your run back: click to accept one for +0.12 multiplier, kept all run',
      // the processional: a gold filigree braid wound against the fall, regal pulse
      veinFx: { accent: 0xffd700, helix2: 0.95, throb: 0.5, breath: 0.2 },
    },
    {
      id: 'chapel', name: 'The Pink Chapel', glyph: '🕊',
      tagline: 'stop fighting the rhythm and it all just works',
      // rose-gold cathedral light: stained-glass panes glowing between dark
      // lead lines, twin rings like nave arches, a big soft breath - and the
      // wall itself advances ON the heartbeat, settling between. The room
      // keeps time; you keep faith.
      style: {
        palette: { colLine: 0xffc2d4, colSpiral: 0xffe6b0, colFog: 0x1c0f14,
                   colBg: 0x221218, colRibbon: 0xffd4a0, colSparkle: 0xfff4e0 },
        knobs: { enter: { ringDouble: 0.70, breath: 0.50, arch: 0.50, beatScroll: 0.30 },
                 peak:  { ringDouble: 1.00, breath: 0.60, throb: 0.30, arch: 0.80, beatScroll: 0.55 } },
        density: { rings: 130, spiralTurns: 39, arms: 4 },
        bloom: 1.35, sparkleBoost: 0.2,
        field: { bolt: [255, 194, 212], boltCore: [255, 244, 230],
                 residue: [255, 214, 170], sparkleHue: 40 },
        ambient: { kind: 'motes', color: [255, 220, 180], count: 14 },
      },
      profile: { density: 1.10, behavioral: 1.00, echo: 1.0, chaperone: 1.0, bound: 1.0, tease: 1.5 },
      wx: {},
      mech: 'communion',
      mechHint: 'a soft bell rings every ~2s: pop ON it to chain up to ×2.2 (every 4th gilds nearby) — off-beat pays ×0.8',
      // the nave: stained-glass panes between lead lines, the wall keeping time
      veinFx: { accent: 0xffe6b0, arch: 0.9, beat: 0.55 },
    },
    {
      id: 'mirrorlake', name: 'Mirror Lake', glyph: '🪷',
      tagline: 'the water is calm because you are',
      // rose-silver stillness: the answer to Room II's Hall of Mirrors. The
      // tube folds across a glowing waterline - the lower half a rippled
      // reflection of the upper - with calm caustic light and the gentlest
      // lapping surge. The same reflections, celebrated now, not interrogated.
      style: {
        palette: { colLine: 0xe8cfe0, colSpiral: 0xc0d8e8, colFog: 0x140f18,
                   colBg: 0x181220, colRibbon: 0xd8b8d0, colSparkle: 0xffffff },
        knobs: { enter: { breath: 0.70, mirrorY: 0.60, caustic: 0.18, surge: 0.15 },
                 peak:  { breath: 0.80, ringDouble: 0.40, throb: 0.20, mirrorY: 0.90, caustic: 0.30, surge: 0.20 } },
        density: { rings: 110, spiralTurns: 39, arms: 3 },
        bloom: 1.2, sparkleBoost: 0.25,
        field: { bolt: [220, 200, 235], boltCore: [250, 245, 255],
                 residue: [220, 200, 235], sparkleHue: 300 },
        ambient: { kind: 'petals', color: [232, 207, 224] },
      },
      profile: { density: 1.15, behavioral: 0.80, echo: 1.0, chaperone: 0.8, bound: 0.7, tease: 1.7 },
      wx: { payMult: 1.1 },   // acceptance pays - gently, on everything
      mech: 'acceptance',
      mechHint: 'your shields melt into +0.15 multiplier each on entry; pops while the water shows a reflection pay ×1.5',
      // the still shore: the corridor folds across a glowing waterline, calm caustics
      veinFx: { accent: 0xc0d8e8, helix: 0.6, mirrorY: 0.95, caustic: 0.35, surge: 0.2 },
    },
  ],
};

const ALL = [];
for (const room of Object.keys(BIOMES_BY_ROOM)) {
  for (const b of BIOMES_BY_ROOM[room]) { b.room = +room; ALL.push(b); }
}

/** All 16 biomes flat, rooms 1..4 in order - the stable left-to-right order the
 * draft room's roulette row displays them in. */
export const BIOMES_ALL = ALL;

export function biomeById(id) {
  return ALL.find((b) => b.id === id) || null;
}

/** Roll one biome id per room (1..REGION_COUNT). Every descent re-rolls, so the
 * mix stays unknown until each chamber's arrival card names it. */
export function rollBiomeIds() {
  const out = [];
  for (let room = 1; room <= REGION_COUNT; room++) {
    const pool = BIOMES_BY_ROOM[room] || [];
    out.push(pool.length ? pool[(Math.random() * pool.length) | 0].id : null);
  }
  return out;
}

/** Resolve the rolled biome for a 1-based waveIndex (relapse loops past the
 * last region reuse its roll, mirroring regionForWave). rolled = st.biomes. */
export function biomeForWave(waveIndex, rolled) {
  if (!rolled) return null;
  const i = Math.min(Math.max(1, waveIndex | 0), REGION_COUNT) - 1;
  return rolled[i] ? biomeById(rolled[i]) : null;
}
