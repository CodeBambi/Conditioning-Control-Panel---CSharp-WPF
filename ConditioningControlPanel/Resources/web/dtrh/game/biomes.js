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
 *
 * All FOUR slots per room are live (wave 1: Toybox/Keyhole/Mirrors/Grey Ward/
 * Gallery/Casino/Velocity/Coronation; wave 2: Static/Incognito/Vertigo/
 * Searchlight/Chain Court/Undertow/Chapel/Mirror Lake). Legacy/scripted runs
 * roll NO biome and fall back to the room's classic Four Chambers dress.
 *
 * New wave-2 fields: `audio` names a master-bus color (audioBus setAudioColor:
 * 'muffled' | 'underwater') the mechanic applies on entry and lifts on exit.
 * ==========================================================================*/

import { REGION_COUNT } from './regions.js';

export const BIOMES_BY_ROOM = {
  // ---- Room I - CURIOSITY & DENIAL ("it's just something silly") ----------
  1: [
    {
      id: 'toybox', name: 'The Toybox', glyph: '🧸',
      tagline: 'it’s all just toys in here',
      // candy pastels on soft plum - bright, bouncy, harmless-looking. Wide
      // breathing rings like a mobile over a crib.
      style: {
        palette: { colLine: 0xffb3d9, colSpiral: 0x9be8ff, colFog: 0x241426,
                   colBg: 0x2b1a30, colRibbon: 0xffc7e8, colSparkle: 0xfff0fa },
        knobs: { enter: { breath: 0.85 },
                 peak:  { breath: 0.60, lineWave: 0.10 } },
        density: { rings: 100, spiralTurns: 39, arms: 3 },
        bloom: 1.0, sparkleBoost: 0.2,
        field: { residue: [255, 179, 217], sparkleHue: 200 },
      },
      // busier than the classic Long Fall but softer: lots of plain bubbles
      // ("toys"), almost no menagerie pressure.
      profile: { density: 0.95, behavioral: 0.30, echo: 0.4, chaperone: 0.5, bound: 0.3, tease: 0.6 },
      wx: { fuseMult: 1.15 },   // kind fuses - nothing here means anything, right?
      mech: 'mimics',
    },
    {
      id: 'keyhole', name: 'The Keyhole', glyph: '🗝',
      tagline: 'you only looked because the light was there',
      // near-dark brass-and-candlelight: the tube itself barely glows, and the
      // roaming keyhole beams (biomeMech) are the only bright thing.
      style: {
        palette: { colLine: 0x8a6a48, colSpiral: 0x6a5030, colFog: 0x0a0705,
                   colBg: 0x0d0a06, colRibbon: 0x92703f, colSparkle: 0xffdca0 },
        knobs: { enter: { breath: 0.30 },
                 peak:  { breath: 0.30, throb: 0.15 } },
        density: { rings: 105, spiralTurns: 39, arms: 3 },
        bloom: 0.7,
        field: { residue: [255, 214, 150], sparkleHue: 40 },
      },
      profile: { density: 0.70, behavioral: 0.35, echo: 0.4, chaperone: 0.6, bound: 0.4, tease: 0.7 },
      wx: {},
      mech: 'keyhole',
    },
    {
      id: 'static', name: 'Late Night Static', glyph: '📺',
      tagline: 'the channel you only find after midnight',
      weatherId: 'static',   // the storm sky IS the broadcast
      // 2am phosphor: CRT teal on near-black, marquee-dash rings scanning past,
      // a nervous low strobe like a set that hasn't warmed up.
      style: {
        palette: { colLine: 0x5ee8d0, colSpiral: 0x2f8f84, colFog: 0x061012,
                   colBg: 0x081416, colRibbon: 0x53d8c4, colSparkle: 0xc8fff4 },
        knobs: { enter: { ringDash: 0.60, strobe: 0.10 },
                 peak:  { ringDash: 0.85, strobe: 0.18, lineWave: 0.08 } },
        density: { rings: 110, spiralTurns: 39, arms: 3 },
        bloom: 0.9, sparkleBoost: 0.15,
        field: { bolt: [110, 240, 220], boltCore: [220, 255, 248],
                 residue: [110, 240, 220], sparkleHue: 172 },
      },
      profile: { density: 0.92, behavioral: 0.35, echo: 0.5, chaperone: 0.5, bound: 0.3, tease: 0.8 },
      wx: {},
      mech: 'flicker',
    },
    {
      id: 'incognito', name: 'Incognito', glyph: '🕶',
      tagline: 'nobody has to know. delete it after.',
      // graphite and deep navy, lights kept low - a browser window at 1am with
      // the brightness turned down and one ear on the hallway.
      style: {
        palette: { colLine: 0x5a6478, colSpiral: 0x39415a, colFog: 0x0b0d14,
                   colBg: 0x0d1018, colRibbon: 0x4a5470, colSparkle: 0x8fa0c0 },
        knobs: { enter: { breath: 0.25 },
                 peak:  { breath: 0.30, ringDash: 0.35 } },
        density: { rings: 100, spiralTurns: 39, arms: 3 },
        bloom: 0.7,
        field: { residue: [140, 160, 200], sparkleHue: 225 },
      },
      // more media pressure than the classic Long Fall: history ACCUMULATES,
      // that's the whole point of clearing it.
      profile: { density: 1.00, behavioral: 0.30, echo: 0.7, chaperone: 0.5, bound: 0.3, tease: 0.9 },
      wx: {},
      mech: 'incognito',
    },
  ],

  // ---- Room II - FEAR & CONFUSION ("what if it isn't a game?") ------------
  2: [
    {
      id: 'mirrors', name: 'Hall of Mirrors', glyph: '🪞',
      tagline: 'everywhere you look… it’s this one, isn’t it?',
      // silvered prismatic frames: crisp twin rings read as endless mirror
      // edges, a spiral that wanders like a reflection that isn't quite yours.
      style: {
        palette: { colLine: 0xcfd8e8, colSpiral: 0x9fb8ff, colFog: 0x10141f,
                   colBg: 0x131826, colRibbon: 0xbde0ff, colSparkle: 0xffffff },
        knobs: { enter: { ringDouble: 0.75, spiralDrift: 0.30 },
                 peak:  { ringDouble: 1.00, spiralDrift: 0.55 } },
        density: { rings: 125, spiralTurns: 45, arms: 6 },
        bloom: 1.15, sparkleBoost: 0.25,
        field: { bolt: [190, 210, 255], boltCore: [240, 246, 255],
                 residue: [200, 216, 255], sparkleHue: 220 },
      },
      profile: { density: 0.92, behavioral: 0.95, echo: 0.7, chaperone: 1.8, bound: 1.8, tease: 0.9 },
      wx: {},
      mech: 'mirrors',
    },
    {
      id: 'greyward', name: 'The Grey Ward', glyph: '🌫',
      tagline: 'the only color left is the one you keep looking at',
      // the world drained: charcoal tube, ash fog, dim bloom. The media - your
      // gifs and videos - are the ONLY color on screen (their textures don't
      // take the palette), and the contrast IS the lesson.
      style: {
        palette: { colLine: 0x767c85, colSpiral: 0x585e66, colFog: 0x121316,
                   colBg: 0x16171b, colRibbon: 0x6a7078, colSparkle: 0x9aa0a8 },
        knobs: { enter: { lineWave: 0.06 },
                 peak:  { lineWave: 0.10, throb: 0.12 } },
        density: { rings: 120, spiralTurns: 45, arms: 4 },
        bloom: 0.55,
        field: { bolt: [150, 156, 165], boltCore: [220, 224, 230],
                 residue: [150, 156, 165], sparkleHue: 220 },
      },
      profile: { density: 0.88, behavioral: 0.90, echo: 0.6, chaperone: 1.6, bound: 1.6, tease: 1.0 },
      wx: { heatGain: 0.5 },   // lust only warms where the color is (mech re-adds it on media touch)
      mech: 'greyward',
    },
    {
      id: 'vertigo', name: 'Vertigo', glyph: '🙃',
      tagline: 'up stopped meaning anything',
      // sick violet and wrong green, the spiral drifting hard and the lines
      // queasy - a staircase that turns out to be a ceiling.
      style: {
        palette: { colLine: 0x9a7bff, colSpiral: 0x7bffb8, colFog: 0x120a1c,
                   colBg: 0x150d22, colRibbon: 0x8a6ae8, colSparkle: 0xd8ccff },
        knobs: { enter: { spiralDrift: 0.70, lineWave: 0.10 },
                 peak:  { spiralDrift: 1.00, lineWave: 0.16, throb: 0.15 } },
        density: { rings: 120, spiralTurns: 51, arms: 4 },
        bloom: 1.0,
        field: { bolt: [154, 123, 255], boltCore: [230, 220, 255],
                 residue: [123, 255, 184], sparkleHue: 260 },
      },
      profile: { density: 0.90, behavioral: 0.90, echo: 0.6, chaperone: 1.5, bound: 1.4, tease: 0.9 },
      wx: {},
      mech: 'vertigo',
    },
    {
      id: 'searchlight', name: 'The Searchlight', glyph: '🔦',
      tagline: 'what if someone sees?',
      // near-black steel ward under a held breath: sparse, dim, a heartbeat
      // throb - and two cold beams sweeping for exactly you.
      style: {
        palette: { colLine: 0x3d4450, colSpiral: 0x2a3038, colFog: 0x05060a,
                   colBg: 0x07080d, colRibbon: 0x39404c, colSparkle: 0xcfe0ff },
        knobs: { enter: { throb: 0.35, breath: 0.15 },
                 peak:  { throb: 0.55, breath: 0.20 } },
        density: { rings: 95, spiralTurns: 39, arms: 3 },
        bloom: 0.6,
        field: { bolt: [170, 190, 255], boltCore: [235, 242, 255],
                 residue: [170, 190, 255], sparkleHue: 222 },
      },
      profile: { density: 0.80, behavioral: 1.00, echo: 0.6, chaperone: 1.7, bound: 1.6, tease: 0.8 },
      wx: {},
      mech: 'searchlight',
    },
  ],

  // ---- Room III - BARGAIN & STRUGGLE ("just this once") -------------------
  3: [
    {
      id: 'gallery', name: 'The Gallery', glyph: '🏛',
      tagline: 'look. don’t touch.',
      // ivory marble and gilt: dashed rings read as column rows, everything
      // pristine and bright - a museum you are very much being watched in.
      style: {
        palette: { colLine: 0xe8dcc0, colSpiral: 0xd4af6a, colFog: 0x211b10,
                   colBg: 0x272013, colRibbon: 0xd4af37, colSparkle: 0xfff2cc },
        knobs: { enter: { ringDash: 0.55, breath: 0.15 },
                 peak:  { ringDash: 0.75, breath: 0.15, throb: 0.20 } },
        density: { rings: 130, spiralTurns: 45, arms: 4 },
        bloom: 1.05, ribbonBoost: 0.2,
        field: { residue: [230, 205, 140], sparkleHue: 46 },
      },
      profile: { density: 1.10, behavioral: 1.00, echo: 1.6, chaperone: 0.9, bound: 0.8, tease: 1.6 },
      wx: {},
      mech: 'gallery',
    },
    {
      id: 'casino', name: 'Fool’s Casino', glyph: '🎰',
      tagline: 'just one more. you can stop whenever you want.',
      weatherId: 'foolsgold',   // the house sky: gold rains here
      // green felt under gold neon: marquee-dash rings with a lazy tick, gold
      // sparkle rain. Everything about it says the odds are in your favor.
      style: {
        palette: { colLine: 0xffd700, colSpiral: 0x3ecf6a, colFog: 0x0a170d,
                   colBg: 0x0e1f12, colRibbon: 0xffc84a, colSparkle: 0xfff0a0 },
        knobs: { enter: { ringDash: 0.60, strobe: 0.14 },
                 peak:  { ringDash: 0.80, strobe: 0.22, throb: 0.20 } },
        density: { rings: 130, spiralTurns: 51, arms: 4 },
        bloom: 1.2, sparkleBoost: 0.3,
        field: { bolt: [255, 215, 80], boltCore: [255, 245, 200],
                 residue: [255, 215, 80], sparkleHue: 50 },
      },
      profile: { density: 1.20, behavioral: 1.05, echo: 1.8, chaperone: 0.8, bound: 0.9, tease: 1.2 },
      wx: { goldenBonus: 0.02 },   // stacked deck: goldens land noticeably more often
      mech: 'casino',
    },
    {
      id: 'chaincourt', name: 'The Chain Court', glyph: '⛓',
      tagline: 'you read the terms. you signed anyway.',
      // wine-dark red and old iron: doubled rings like manacles, a slow pulse.
      // Everything here arrives bound to something else - so do you.
      style: {
        palette: { colLine: 0xc23b4e, colSpiral: 0x7a2230, colFog: 0x1a0709,
                   colBg: 0x200a0d, colRibbon: 0xa83a48, colSparkle: 0xffb0b8 },
        knobs: { enter: { ringDouble: 0.50, throb: 0.20 },
                 peak:  { ringDouble: 0.80, throb: 0.35, ringDash: 0.30 } },
        density: { rings: 125, spiralTurns: 45, arms: 4 },
        bloom: 1.0, ribbonBoost: 0.15,
        field: { bolt: [230, 90, 110], boltCore: [255, 220, 226],
                 residue: [194, 59, 78], sparkleHue: 350 },
      },
      // bound pairs everywhere: the room's whole population signs in twos
      profile: { density: 1.05, behavioral: 1.10, echo: 1.2, chaperone: 1.0, bound: 2.6, tease: 1.3 },
      wx: {},
      mech: 'contracts',
    },
    {
      id: 'undertow', name: 'The Undertow', glyph: '🌊',
      tagline: 'swim all you like. it has you.',
      // ink-water blues over black: slow heavy breath, lines that wave like
      // kelp, everything a little further away than it looks.
      style: {
        palette: { colLine: 0x2f5fc8, colSpiral: 0x1b3a80, colFog: 0x050a18,
                   colBg: 0x071020, colRibbon: 0x2a55b0, colSparkle: 0x9fc8ff },
        knobs: { enter: { breath: 0.50, lineWave: 0.12 },
                 peak:  { breath: 0.60, lineWave: 0.18, spiralDrift: 0.30 } },
        density: { rings: 115, spiralTurns: 45, arms: 3 },
        bloom: 0.85,
        field: { bolt: [90, 150, 255], boltCore: [210, 230, 255],
                 residue: [90, 150, 255], sparkleHue: 218 },
      },
      profile: { density: 1.00, behavioral: 1.00, echo: 1.4, chaperone: 0.9, bound: 0.9, tease: 1.4 },
      wx: {},
      mech: 'undertow',
    },
  ],

  // ---- Room IV - SURRENDER & ACCEPTANCE ("yes. this is you.") -------------
  4: [
    {
      id: 'velocity', name: 'Terminal Velocity', glyph: '☄',
      tagline: 'nothing here can hurt you. fall.',
      // white-hot streaks on near-black: max arms, hard strobe signage, the
      // fastest the tube ever runs. Surrender as release - fuses are so long
      // they may as well not exist; only the flow can break.
      style: {
        palette: { colLine: 0xff8ad0, colSpiral: 0xffffff, colFog: 0x14060e,
                   colBg: 0x180810, colRibbon: 0xff9ad8, colSparkle: 0xffffff },
        knobs: { enter: { strobe: 0.40, ringDouble: 0.35 },
                 peak:  { strobe: 0.60, ringDouble: 0.50, throb: 0.20 } },
        density: { rings: 150, spiralTurns: 51, arms: 6 },
        bloom: 1.25, ribbonBoost: 0.35,
        field: { bolt: [255, 138, 208], boltCore: [255, 235, 248],
                 residue: [255, 150, 215], sparkleHue: 320 },
      },
      profile: { density: 1.50, behavioral: 0.60, echo: 0.8, chaperone: 0.6, bound: 0.6, tease: 1.0 },
      wx: { fuseMult: 2.6 },   // fuses so long the field never threatens - the combo clock is the game
      mech: 'velocity',
    },
    {
      id: 'coronation', name: 'The Coronation', glyph: '👑',
      tagline: 'the run remembers everything you did',
      // royal purple and gold: crisp twin rings + a slow heartbeat throb. The
      // throne room where your own descent is read back to you as a verdict.
      style: {
        palette: { colLine: 0xb066ff, colSpiral: 0xffd700, colFog: 0x150a1f,
                   colBg: 0x190d25, colRibbon: 0xd4af37, colSparkle: 0xffe9b0 },
        knobs: { enter: { ringDouble: 0.80, throb: 0.25 },
                 peak:  { ringDouble: 1.00, throb: 0.45, strobe: 0.25 } },
        density: { rings: 140, spiralTurns: 45, arms: 4 },
        bloom: 1.3, ribbonBoost: 0.3,
        field: { bolt: [200, 140, 255], boltCore: [245, 230, 255],
                 residue: [212, 175, 55], sparkleHue: 275 },
      },
      profile: { density: 1.30, behavioral: 1.20, echo: 1.2, chaperone: 1.1, bound: 1.2, tease: 1.9 },
      wx: {},
      mech: 'coronation',
    },
    {
      id: 'chapel', name: 'The Pink Chapel', glyph: '🕊',
      tagline: 'stop fighting the rhythm and it all just works',
      // rose-gold cathedral light: twin rings like nave arches, a big soft
      // breath, bloom turned devotional. The room keeps time; you keep faith.
      style: {
        palette: { colLine: 0xffc2d4, colSpiral: 0xffe6b0, colFog: 0x1c0f14,
                   colBg: 0x221218, colRibbon: 0xffd4a0, colSparkle: 0xfff4e0 },
        knobs: { enter: { ringDouble: 0.70, breath: 0.50 },
                 peak:  { ringDouble: 1.00, breath: 0.60, throb: 0.30 } },
        density: { rings: 130, spiralTurns: 39, arms: 4 },
        bloom: 1.35, sparkleBoost: 0.2,
        field: { bolt: [255, 194, 212], boltCore: [255, 244, 230],
                 residue: [255, 214, 170], sparkleHue: 40 },
      },
      profile: { density: 1.10, behavioral: 1.00, echo: 1.0, chaperone: 1.0, bound: 1.0, tease: 1.5 },
      wx: {},
      mech: 'communion',
    },
    {
      id: 'mirrorlake', name: 'Mirror Lake', glyph: '🪷',
      tagline: 'the water is calm because you are',
      // rose-silver stillness: the answer to Room II's Hall of Mirrors. The
      // same reflections - celebrated now, not interrogated.
      style: {
        palette: { colLine: 0xe8cfe0, colSpiral: 0xc0d8e8, colFog: 0x140f18,
                   colBg: 0x181220, colRibbon: 0xd8b8d0, colSparkle: 0xffffff },
        knobs: { enter: { breath: 0.70 },
                 peak:  { breath: 0.80, ringDouble: 0.40, throb: 0.20 } },
        density: { rings: 110, spiralTurns: 39, arms: 3 },
        bloom: 1.2, sparkleBoost: 0.25,
        field: { bolt: [220, 200, 235], boltCore: [250, 245, 255],
                 residue: [220, 200, 235], sparkleHue: 300 },
      },
      profile: { density: 1.15, behavioral: 0.80, echo: 1.0, chaperone: 0.8, bound: 0.7, tease: 1.7 },
      wx: { payMult: 1.1 },   // acceptance pays - gently, on everything
      mech: 'acceptance',
    },
  ],
};

const ALL = [];
for (const room of Object.keys(BIOMES_BY_ROOM)) {
  for (const b of BIOMES_BY_ROOM[room]) { b.room = +room; ALL.push(b); }
}

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
