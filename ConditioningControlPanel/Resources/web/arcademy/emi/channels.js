/* ============================================================================
 * emi/channels.js - THE OFF CHANNELS: the dials, the wheel and the painters.
 *
 * EMI is a television, and a television left alone does not sit on one frame -
 * it changes the channel. When the player has been quiet long enough this
 * module supplies the thing her glass becomes for at most ten seconds, and
 * `emi/takeover.js` supplies the glass, the blip and every cancel rule.
 *
 * THIS FILE IS DATA AND PAINT. It owns no timer, no rAF, no DOM node and no
 * network call; every painter is handed a 2d context that is already sized,
 * already cleared and already owned by somebody else. That is what makes the
 * whole wave testable in node with nothing but a fake context.
 *
 * THE PAINTER CONTRACT
 *   {
 *     id, weight, cooldownMs,
 *     plan(ctx) -> spec | null,          // MAY REFUSE. Runs at ROLL time, does
 *                                        // no I/O, and a channel that cannot
 *                                        // fully play tonight is ABSENT from
 *                                        // the wheel - never a stub.
 *     prepare?(spec, ctx) -> Promise,    // the ONE place a channel may wait on
 *                                        // material; bounded by FETCH_BUDGET_MS
 *                                        // and a miss SKIPS the takeover.
 *     start(g, spec),                    // g = {c, w, h, rm}
 *     frame(g, spec, tMs),
 *     end(g, spec, reason),
 *     gag?(g, spec) -> ms,               // the alt-tab reflex, painted BEFORE the blip
 *     caught: {...}                      // what being caught on this channel is
 *   }
 *
 * THE GLASS IS 152 x 137 VIRTUAL PX, the same locked geometry face.js paints
 * (res 152, height = w x 0.903). Everything below is written at that scale.
 *
 * THE `caught` TABLES PASSED /emi-lines 2026-08-24 (the EMI COLOR wave): one
 * line changed (CH3's declined "your loss." bit where EMI only ever teases -
 * it is "fine. more for me." now), the rest stood as written. Owner reads the
 * full table at the PR; until that word these are QA-passed, not locked.
 * ==========================================================================*/

import { makeRng } from '../core/rng.js';

/* ---------------------- the dials ---------------------------------------
 * Every number the wave has, in one frozen object, the way voice.js and vox.js
 * keep theirs - so the owner can retune the cadence without reading the paint. */
export const SL_DIALS = Object.freeze({
  /* RETUNED 2026-08-25 (owner: "the screen animations are entertaining, they
   * should be frequent and used to distance the barks"). Was 90s / 20s / 180s
   * / 6 - the lab-experiment cadence. RETUNED AGAIN the same day, after the
   * heartbeat shipped and the owner played it: "we need to see the animations
   * wayy more often". The global gap halves (60s -> 30s), the sitting cap
   * more than doubles, and the per-channel rests below halve again - with
   * five channels in the wheel the OLD rests starved the frequent global
   * cadence anyway: a minute after the last takeover nothing was off
   * cooldown, so the dial that read "about once a minute" delivered a third
   * of that. */
  THEATRE_IDLE_MS: 30000,     // the floor: no channel before this much player silence
  ROLL_MS: 10000,             // wheel cadence while eligible
  GLOBAL_COOLDOWN_MS: 30000,  // no two takeovers closer than this (deep idle exempt)
  PER_SESSION_CAP: 30,        // rolled channels per sitting (deep idle exempt)
  TAKEOVER_MAX_MS: 10000,     // the hard cap (the screensaver alone is exempt)
  BLIP_MS: 180,               // collapse-to-line + line-to-dot, total
  OFFER_MS: 5000,             // the "wanna see?" window
  REVEAL_MS: 6000,            // the full-size card
  FETCH_BUDGET_MS: 2000,      // media not ready in time = takeover skipped, no black glass
  SAVER_IDLE_MS: 240000,      // deep idle threshold
  SAVER_REST_MS: 2600,        // face-breath between saver cycles (unused while the
                              // screensaver keeps its exemption - owner call 4)
  WRONG_ODDS: 1 / 40,         // per rolled-channel exit, labSeen, 1/session
  WRONG_MAX_MS: 400,
  WEIGHTS: Object.freeze({ pong: 30, browsing: 30, watching: 18, reruns: 12, shop: 7 }),
  COOLDOWN_MS: Object.freeze({           // per channel, halved 0825 and halved again (see above)
    pong: 120000, browsing: 120000, watching: 240000,
    reruns: 300000, shop: 480000,
  }),
});

/** The glass, in virtual px. face.js's own numbers (res 152, h = w x 0.903). */
export const GLASS_W = 152;
export const GLASS_H = 137;

const PINK = '#FF69B4';
const DARK = '#1A1A2E';
const CREAM = '#F5F0E1';
const RAIN_GREEN = '#00FF41';

/* THE WRONG CHANNEL's word list. Owner call 7; these are the doc's candidates.
 * /emi-lines QA 2026-08-24: passed as written (single innocent words, never
 * explained - the eeriness is the timing, not the vocabulary). */
export const WRONG_WORDS = Object.freeze(['soon', 'hi', 'again']);

/* ============================================================================
 * paint helpers - deliberately tiny, deliberately shared
 * ==========================================================================*/

function fill(g, style, x, y, w, h) {
  g.c.fillStyle = style;
  g.c.fillRect(Math.round(x), Math.round(y), Math.round(w), Math.round(h));
}

function clear(g, style) {
  fill(g, style || '#000', 0, 0, g.w, g.h);
}

/** Pixel type. Everything on the glass is drawn with one font ladder. */
function text(g, s, x, y, size, style, align) {
  try {
    g.c.font = Math.round(size) + 'px "Press Start 2P", "Noto Sans Mono", monospace';
    g.c.textAlign = align || 'left';
    g.c.textBaseline = 'top';
    g.c.fillStyle = style;
    g.c.fillText(String(s), Math.round(x), Math.round(y));
  } catch (e) { /* a channel may never throw at a mascot */ }
}

/** The scanline mask every channel wears, so they read as one appliance. */
function scanlines(g, alpha) {
  g.c.fillStyle = 'rgba(0,0,0,' + (alpha == null ? 0.22 : alpha) + ')';
  for (let y = 0; y < g.h; y += 2) g.c.fillRect(0, y, g.w, 1);
}

/** Monochrome noise, cheap: one pass of short runs rather than per-pixel. */
function static_(g, rand, density, light, dark) {
  clear(g, dark || '#0b0b12');
  const n = Math.round((density == null ? 0.4 : density) * 240);
  for (let i = 0; i < n; i++) {
    const x = Math.floor(rand() * g.w);
    const y = Math.floor(rand() * g.h);
    const w = 1 + Math.floor(rand() * 4);
    g.c.fillStyle = rand() > 0.5 ? (light || '#c9c9d6') : '#4a4a58';
    g.c.fillRect(x, y, w, 1);
  }
}

/** cover-fit an image-ish source onto the whole glass. Never throws. */
function cover(g, el, natW, natH) {
  const nw = natW || el.naturalWidth || el.videoWidth || el.width || 0;
  const nh = natH || el.naturalHeight || el.videoHeight || el.height || 0;
  if (!nw || !nh) return false;
  const s = Math.max(g.w / nw, g.h / nh);
  const w = nw * s;
  const h = nh * s;
  try { g.c.drawImage(el, (g.w - w) / 2, (g.h - h) / 2, w, h); } catch (e) { return false; }
  return true;
}

/** `0:07` - the fake timecode every playback channel wears. */
export function timecode(ms) {
  const t = Math.max(0, Math.floor(ms / 1000));
  const m = Math.floor(t / 60);
  const s = t % 60;
  return m + ':' + (s < 10 ? '0' : '') + s;
}

/** A url's last path segment, decoded and de-extensioned: the local title. */
export function titleOf(url) {
  const raw = String(url == null ? '' : url);
  let tail = raw.split('?')[0].split('#')[0];
  tail = tail.slice(tail.lastIndexOf('/') + 1);
  try { tail = decodeURIComponent(tail); } catch (e) { /* keep the raw one */ }
  tail = tail.replace(/\.[a-z0-9]{2,5}$/i, '').replace(/[_-]+/g, ' ').trim();
  if (!tail) return 'untitled';
  return tail.length > 34 ? tail.slice(0, 33) + '…' : tail;
}

/* ============================================================================
 * CH1 - CHANNEL PONG. She plays both sides, imperfectly.
 * ==========================================================================*/

const PONG_SPEED = 55;          // virtual px/s (the doc's number)
const PADDLE_H = 10;
const PADDLE_W = 2;

export const pong = {
  id: 'pong',
  weight: SL_DIALS.WEIGHTS.pong,
  cooldownMs: SL_DIALS.COOLDOWN_MS.pong,
  /* A MOVING BALL IS THE WHOLE CHANNEL, so reduced motion refuses it outright
   * rather than shipping a still of a pong board. */
  plan(ctx) {
    if (ctx.reducedMotion) return null;
    return { seed: ctx.seed + '|pong' };
  },
  start(g, spec) {
    const rand = makeRng(spec.seed);
    const dir = rand() > 0.5 ? 1 : -1;
    spec.st = {
      rand,
      x: g.w / 2, y: g.h / 2,
      vx: PONG_SPEED * dir,
      vy: PONG_SPEED * (rand() * 0.9 - 0.45),
      score: [0, 0],
      // WHICH SIDE FUMBLES, and how late. One miss per takeover is the shape
      // the ten-second window fits; the seed decides who and when.
      missSide: rand() > 0.5 ? 1 : 0,
      missAt: 3200 + rand() * 4200,
      last: 0,
      pl: g.h / 2, pr: g.h / 2,
    };
  },
  frame(g, spec, t) {
    const s = spec.st;
    const dt = Math.min(80, t - s.last) / 1000;
    s.last = t;
    s.x += s.vx * dt;
    s.y += s.vy * dt;
    if (s.y < 1) { s.y = 1; s.vy = Math.abs(s.vy); }
    if (s.y > g.h - 4) { s.y = g.h - 4; s.vy = -Math.abs(s.vy); }

    // THE TWO HANDS. Each paddle tracks the ball, and the one whose turn it is
    // to fumble tracks it slackly for the rest of the run.
    const left = 6, right = g.w - 6 - PADDLE_W;
    const fumbling = (side) => t > s.missAt && s.missSide === side;
    const track = (cur, gain) => cur + (s.y + 1.5 - cur) * gain;
    s.pl = track(s.pl, fumbling(0) ? 0.02 : 0.16);
    s.pr = track(s.pr, fumbling(1) ? 0.02 : 0.16);
    s.pl = Math.max(PADDLE_H / 2, Math.min(g.h - PADDLE_H / 2, s.pl));
    s.pr = Math.max(PADDLE_H / 2, Math.min(g.h - PADDLE_H / 2, s.pr));

    if (s.vx < 0 && s.x <= left + PADDLE_W && Math.abs(s.y - s.pl) < PADDLE_H / 2 + 2) {
      s.x = left + PADDLE_W; s.vx = Math.abs(s.vx);
      s.vy += (s.y - s.pl) * 1.6;
    }
    if (s.vx > 0 && s.x + 3 >= right && Math.abs(s.y - s.pr) < PADDLE_H / 2 + 2) {
      s.x = right - 3; s.vx = -Math.abs(s.vx);
      s.vy += (s.y - s.pr) * 1.6;
    }
    if (s.x < -4) { s.score[1] += 1; reset(s, g, 1); }
    if (s.x > g.w + 4) { s.score[0] += 1; reset(s, g, -1); }

    clear(g, '#050509');
    // the dashed centre line
    g.c.fillStyle = 'rgba(255,105,180,0.45)';
    for (let y = 2; y < g.h; y += 8) g.c.fillRect(Math.round(g.w / 2), y, 1, 4);
    fill(g, PINK, left, s.pl - PADDLE_H / 2, PADDLE_W, PADDLE_H);
    fill(g, PINK, right, s.pr - PADDLE_H / 2, PADDLE_W, PADDLE_H);
    fill(g, PINK, s.x, s.y, 3, 3);
    text(g, String(s.score[0]), g.w / 2 - 14, 5, 10, PINK, 'right');
    text(g, String(s.score[1]), g.w / 2 + 14, 5, 10, PINK, 'left');
    scanlines(g, 0.18);
  },
  end() { /* nothing to release */ },
  caught: {
    face: '^_^', hold: 900, bodyFrame: 'idle',
    lineOdds: 0.3,
    lines: ['left me was winning.', 'i play both sides.', 'best of eleven.'],
  },
};

function reset(s, g, dir) {
  s.x = g.w / 2; s.y = g.h / 2;
  s.vx = PONG_SPEED * dir;
  s.vy = PONG_SPEED * (s.rand() * 0.9 - 0.45);
}

/* ============================================================================
 * CH2 - LATE NIGHT BROWSING. In-universe app wireframes, nothing else.
 * ==========================================================================*/

/* Every page here is a place the base already knows. No real-world brands, and
 * the parody is the LAYOUT: three to six character words, chunky blocks, one
 * accent per app so each reads as a different place at a glance. */
export const BROWSE_PAGES = Object.freeze([
  Object.freeze({ id: 'mail', title: 'MAIL', accent: '#7FD4C1' }),
  Object.freeze({ id: 'board', title: 'BOARD', accent: '#F2C14E' }),
  Object.freeze({ id: 'bank', title: 'BANK', accent: '#8CC63F' }),
  Object.freeze({ id: 'office', title: 'CARDS', accent: '#C9A0DC' }),
  Object.freeze({ id: 'hole', title: 'DEEP', accent: '#6FA8DC' }),
  Object.freeze({ id: 'records', title: 'REC', accent: '#E8836F' }),
]);

const PAGE_MS = 2500;

export const browsing = {
  id: 'browsing',
  weight: SL_DIALS.WEIGHTS.browsing,
  cooldownMs: SL_DIALS.COOLDOWN_MS.browsing,
  plan(ctx) {
    const rand = makeRng(ctx.seed + '|browse');
    const order = BROWSE_PAGES.slice();
    for (let i = order.length - 1; i > 0; i--) {
      const j = Math.floor(rand() * (i + 1));
      const t = order[i]; order[i] = order[j]; order[j] = t;
    }
    // REDUCED MOTION IS ONE PAGE, no flicks: the channel survives, the cuts do not.
    const n = ctx.reducedMotion ? 1 : 2 + Math.floor(rand() * 3);
    return { pages: order.slice(0, n), seed: ctx.seed + '|browse' };
  },
  start(g, spec) { spec.st = { rand: makeRng(spec.seed + '|paint') }; },
  frame(g, spec, t) {
    const i = spec.pages.length === 1 ? 0
      : Math.min(spec.pages.length - 1, Math.floor(t / PAGE_MS));
    drawPage(g, spec.pages[i], t, spec.st.rand);
  },
  end() { /* noop */ },
  /* THE ALT-TAB REFLEX: caught browsing, she snaps to the rules file first. */
  gag(g) {
    clear(g, DARK);
    fill(g, CREAM, 8, 10, g.w - 16, g.h - 20);
    fill(g, DARK, 8, 10, g.w - 16, 12);
    text(g, 'CAMPUS', 12, 12, 8, CREAM);
    text(g, 'RULES.txt', 12, 28, 8, DARK);
    for (let y = 44; y < g.h - 16; y += 8) fill(g, 'rgba(26,26,46,0.35)', 12, y, g.w - 30, 2);
    return 300;
  },
  caught: {
    face: '(◔_◔)', hold: 1100, bodyFrame: 'smug',
    lineOdds: 1,
    lines: ['just checking my mail.', 'i had tabs open.', 'i was reading the rules.'],
  },
};

function drawPage(g, page, t, rand) {
  clear(g, '#0d0d18');
  // the title bar, one accent per app
  fill(g, page.accent, 0, 0, g.w, 13);
  text(g, page.title, 4, 3, 8, DARK);
  const blink = Math.floor(t / 500) % 2 === 0;
  if (page.id === 'mail') {
    for (let r = 0; r < 5; r++) {
      const y = 20 + r * 20;
      fill(g, r === 1 ? PINK : '#2a2a44', 6, y, g.w - 12, 15);
      if (r === 1) text(g, 're: emi', 10, y + 4, 7, DARK);
      else fill(g, '#43436a', 10, y + 5, 40 + Math.round(rand() * 50), 4);
    }
  } else if (page.id === 'board') {
    for (let r = 0; r < 4; r++) {
      const y = 20 + r * 25;
      const lit = r === 2 && blink;
      fill(g, lit ? page.accent : '#26263f', 8, y, g.w - 16, 18);
      fill(g, lit ? DARK : page.accent, 12, y + 2, 4, 4);
      fill(g, lit ? DARK : '#43436a', 20, y + 7, 60 + Math.round(rand() * 40), 4);
    }
  } else if (page.id === 'bank') {
    const n = 1200 + Math.floor(t / 40);
    text(g, String(n), g.w / 2, 40, 16, page.accent, 'center');
    text(g, 'BALANCE', g.w / 2, 24, 7, '#5a5a7a', 'center');
    if (t > 6000) text(g, '-14', g.w / 2, 74, 10, '#E8836F', 'center');
    fill(g, '#26263f', 14, 100, g.w - 28, 20);
  } else if (page.id === 'office') {
    fill(g, CREAM, 20, 26, g.w - 40, 60);
    text(g, 'NOW', g.w / 2, 34, 8, DARK, 'center');
    text(g, 'SERVING', g.w / 2, 48, 8, DARK, 'center');
    text(g, '004', g.w / 2, 64, 12, PINK, 'center');
    fill(g, page.accent, 20, 100, g.w - 40, 8);
  } else if (page.id === 'hole') {
    g.c.strokeStyle = page.accent;
    g.c.lineWidth = 2;
    g.c.beginPath();
    let y = 22;
    g.c.moveTo(4, y);
    for (let x = 4; x < g.w; x += 9) {
      y += 4 + rand() * 10;
      g.c.lineTo(x, Math.min(y, g.h + 20));
    }
    try { g.c.stroke(); } catch (e) { /* noop */ }
    text(g, 'DEPTH', 6, 16, 7, '#5a5a7a');
  } else {
    for (let r = 0; r < 3; r++) {
      for (let cI = 0; cI < 4; cI++) {
        const lit = r === 1 && cI === 2;
        fill(g, lit ? page.accent : '#26263f', 8 + cI * 34, 22 + r * 36, 28, 30);
      }
    }
  }
  scanlines(g, 0.2);
}

/* ============================================================================
 * CH3 - NOW WATCHING. The flagship, and the only channel with full colour.
 * ==========================================================================*/

export const watching = {
  id: 'watching',
  weight: SL_DIALS.WEIGHTS.watching,
  cooldownMs: SL_DIALS.COOLDOWN_MS.watching,
  /* PLAN DOES NO I/O. It asks the broker one synchronous question - is there any
   * material at all - and refuses outright when the answer is no. The fetch
   * itself is `prepare`, which is the only place in this file allowed to wait. */
  plan(ctx) {
    if (!ctx.media || !ctx.media.ready()) return null;
    return { seed: ctx.seed + '|watch', still: !!ctx.reducedMotion, item: null };
  },
  /* A SLOW FETCH SKIPS THE TAKEOVER, it never opens on a black glass. The
   * budget is the takeover runner's; this only has to answer honestly. */
  prepare(spec, ctx) {
    return ctx.media.pick({ still: spec.still }).then((item) => {
      if (!item || !item.el) return false;
      spec.item = item;
      return true;
    }).catch(() => false);
  },
  start(g, spec) { spec.st = { drawn: false }; },
  frame(g, spec, t) {
    // REDUCED MOTION IS ONE FRAME. Painting once and never again is what makes
    // "a still, not a playback" true even of an animated source.
    if (spec.still && spec.st.drawn) return;
    clear(g, '#000');
    const ok = spec.item && spec.item.el ? cover(g, spec.item.el) : false;
    if (!ok) fill(g, '#1b1b2b', 0, 0, g.w, g.h);
    spec.st.drawn = true;
    scanlines(g, 0.26);
    // THE ONLY READABLE THINGS ON THE GLASS, and they are hers.
    text(g, '▶', 5, 5, 11, PINK);
    text(g, timecode(t + 7000), g.w - 5, g.h - 14, 9, PINK, 'right');
  },
  end(g, spec) {
    // ONE MEDIA ELEMENT PER TAKEOVER, RELEASED ON END. A decoration that
    // outlives its channel is a decoder the page never gets back.
    if (spec.item && typeof spec.item.release === 'function') {
      try { spec.item.release(); } catch (e) { /* noop */ }
    }
    if (spec.item) spec.item = null;
  },
  caught: {
    /* THE EMBARRASSED BEAT: the snap first, then the shiver, then the offer. */
    snap: { face: '0_0', hold: 200, bodyFrame: 'shock' },
    face: '>_<', hold: 900, body: 'shiver', bodyFrame: 'shock',
    lineOdds: 1,
    lines: ['you saw nothing.', 'that was research.', 'i was resting my eyes.'],
    offer: {
        lines: ['wanna see?', 'i can show you.', 'want the good part?'],
      accepted: {
        face: '(¬‿¬)', bodyFrame: 'smug',
            lines: ['our secret.', 'rate it out of ten.'],
      },
      declined: {
        face: '-_-', bodyFrame: 'idle',
        /* Sulky-cute, never a bite: she keeps the good part for herself. */
        lines: ['fine. more for me.'],
      },
    },
  },
};

/* ============================================================================
 * CH4 - RERUNS. She rewatches YOU, off the page's own `days` blob.
 * ==========================================================================*/

const GRADE_OK = { s: true, a: true };

/** The most recent S-or-better day on record, or null. Pure, local, no I/O. */
export function bestRerun(days) {
  if (!days || typeof days !== 'object') return null;
  const dates = Object.keys(days).sort().reverse();
  for (const d of dates) {
    const row = days[d];
    const classes = row && row.classes;
    if (!classes || typeof classes !== 'object') continue;
    for (const key of Object.keys(classes)) {
      const g = String((classes[key] || {}).grade || '').toLowerCase();
      if (GRADE_OK[g]) return { date: d, gameKey: key, grade: g.toUpperCase() };
    }
  }
  return null;
}

/** The split-flap short name for a class key. No lexicon reaches EMI. */
export function flapName(gameKey) {
  const s = String(gameKey == null ? '' : gameKey).replace(/[_-]+/g, ' ').toUpperCase().trim();
  return s.length > 14 ? s.slice(0, 14) : (s || 'CLASS');
}

export const reruns = {
  id: 'reruns',
  weight: SL_DIALS.WEIGHTS.reruns,
  cooldownMs: SL_DIALS.COOLDOWN_MS.reruns,
  plan(ctx) {
    const hit = bestRerun(ctx.days);
    if (!hit) return null;                 // no tape, no channel. Never a stub.
    return { hit, band: !ctx.reducedMotion, seed: ctx.seed + '|rerun' };
  },
  start(g, spec) { spec.st = {}; },
  frame(g, spec, t) {
    clear(g, '#07070f');
    const h = spec.hit;
    text(g, h.grade, g.w / 2, 36, 40, PINK, 'center');
    text(g, flapName(h.gameKey), g.w / 2, 88, 7, '#8d8db0', 'center');
    text(g, h.date, g.w / 2, 102, 7, '#4d4d6a', 'center');
    // OSD: the first second is a stutter of REW, then it plays.
    const rew = t < 1000;
    text(g, rew ? '◀◀ REW' : '▶ PLAY', 5, 5, 8,
      rew && Math.floor(t / 160) % 2 === 0 ? '#5a5a7a' : PINK);
    text(g, timecode(t), g.w - 5, g.h - 14, 8, PINK, 'right');
    if (spec.band) {
      // THE TRACKING BAND: a bright wobble rolling up the frame every ~2s.
      const y = g.h - ((t % 2000) / 2000) * (g.h + 18);
      g.c.fillStyle = 'rgba(230,230,255,0.16)';
      g.c.fillRect(0, y, g.w, 9);
      g.c.fillStyle = 'rgba(255,105,180,0.10)';
      g.c.fillRect(0, y + 9, g.w, 3);
    }
    scanlines(g, 0.2);
  },
  end() { /* noop */ },
  /* SHE IS NOT EMBARRASSED HERE. This is the one caught beat she owns. */
  caught: {
    face: '(◠‿◠)', hold: 1300, bodyFrame: 'pet',
    lineOdds: 1,
    lines: ['this one is my favorite.', 'i keep the good ones.', 'watch this part.'],
  },
};

/* ============================================================================
 * CH5 - SHOP AT HOME. In-universe merchandise, price always falling.
 * ==========================================================================*/

export const SHOP_ITEMS = Object.freeze([
  Object.freeze({ id: 'card', label: 'CARD', start: 480 }),
  Object.freeze({ id: 'watch', label: 'WATCH', start: 1290 }),
  Object.freeze({ id: 'disc', label: 'DISC', start: 760 }),
]);

export const shop = {
  id: 'shop',
  weight: SL_DIALS.WEIGHTS.shop,
  cooldownMs: SL_DIALS.COOLDOWN_MS.shop,
  plan(ctx) {
    const rand = makeRng(ctx.seed + '|shop');
    return {
      item: SHOP_ITEMS[Math.floor(rand() * SHOP_ITEMS.length)],
      // Reduced motion keeps the shop but freezes the spin and the blink; the
      // joke is the layout and the falling price, and a price is a number.
      spin: !ctx.reducedMotion,
      blink: !ctx.reducedMotion,
    };
  },
  start(g, spec) { spec.st = {}; },
  frame(g, spec, t) {
    clear(g, '#12040f');
    fill(g, '#2a0a20', 0, 0, g.w, 16);
    text(g, 'SHOP AT HOME', g.w / 2, 4, 7, '#F2C14E', 'center');
    const step = spec.spin ? Math.floor(t / 220) % 4 : 0;
    drawPedestalItem(g, spec.item.id, step);
    // The price never goes up. It has never gone up.
    const price = Math.max(9, spec.item.start - Math.floor(t / 90) * 7);
    text(g, spec.item.label, g.w / 2, 92, 7, '#c9a0dc', 'center');
    text(g, String(price) + ' SP', g.w / 2, 104, 12, '#F2C14E', 'center');
    const lit = spec.blink ? Math.floor(t / 500) % 2 === 0 : true;
    fill(g, lit ? '#E8836F' : '#3a1420', 0, g.h - 14, g.w, 14);
    text(g, 'CALL NOW', g.w / 2, g.h - 11, 7, lit ? DARK : '#7a4050', 'center');
    scanlines(g, 0.18);
  },
  end() { /* noop */ },
  /* Nothing in the basket. Nothing was ever in the basket. */
  gag(g) {
    clear(g, '#12040f');
    g.c.strokeStyle = '#F2C14E';
    g.c.lineWidth = 2;
    try {
      g.c.beginPath();
      g.c.moveTo(52, 52); g.c.lineTo(60, 52); g.c.lineTo(70, 84); g.c.lineTo(100, 84);
      g.c.lineTo(106, 60); g.c.lineTo(64, 60);
      g.c.stroke();
    } catch (e) { /* noop */ }
    text(g, 'EMPTY', g.w / 2, 100, 8, '#7a4050', 'center');
    return 300;
  },
  caught: {
    face: '>_<', hold: 1000, bodyFrame: 'shock',
    lineOdds: 1,
    lines: ['window shopping.', 'not buying anything.', 'it was on sale.'],
  },
};

function drawPedestalItem(g, id, step) {
  const cx = g.w / 2;
  const cy = 52;
  fill(g, '#2a0a20', cx - 26, cy + 24, 52, 6);
  const squeeze = [1, 0.6, 0.15, 0.6][step % 4];
  if (id === 'card') {
    const w = Math.max(2, 34 * squeeze);
    fill(g, CREAM, cx - w / 2, cy - 20, w, 40);
    for (let i = 0; i < 4 && w > 8; i++) fill(g, '#2a0a20', cx - w / 2 + 4 + i * 7, cy - 12, 3, 3);
  } else if (id === 'watch') {
    const w = Math.max(2, 30 * squeeze);
    fill(g, '#F2C14E', cx - w / 2, cy - 16, w, 32);
    if (w > 10) fill(g, '#2a0a20', cx - w / 2 + 4, cy - 12, w - 8, 24);
  } else {
    const w = Math.max(2, 36 * squeeze);
    fill(g, PINK, cx - w / 2, cy - 18, w, 36);
    if (w > 12) fill(g, '#12040f', cx - w / 4, cy - 9, w / 2, 18);
  }
}

/* ============================================================================
 * CH6 - THE WRONG CHANNEL. The eerie beat, and the one that is never explained.
 *
 * It does NOT sit on the wheel: it rides the EXIT of another channel, which is
 * where signal ghosts live. No blip in, no Blipese, no line ever, no
 * acknowledgment. Once a session, `labSeen` only, never under reduced motion.
 * ==========================================================================*/

export const wrong = {
  id: 'wrong',
  weight: 0,
  cooldownMs: 0,
  plan(ctx) {
    if (ctx.reducedMotion) return null;
    if (!ctx.labSeen) return null;
    if (ctx.wrongUsed) return null;
    const rand = makeRng(ctx.seed + '|wrong|' + ctx.takeovers);
    const variant = rand() > 0.5 ? 'negative' : 'word';
    return {
      variant,
      word: WRONG_WORDS[Math.floor(rand() * WRONG_WORDS.length)],
      face: ctx.face || '0_0',
      seed: ctx.seed + '|wrong',
    };
  },
  start(g, spec) { spec.st = { rand: makeRng(spec.seed) }; },
  frame(g, spec, t) {
    if (spec.variant === 'negative') {
      // THE NEGATIVE: the glass floods pink, her face cut out in dark, and a
      // second, smaller one standing 3px behind it.
      clear(g, PINK);
      try {
        g.c.textAlign = 'center';
        g.c.textBaseline = 'middle';
        g.c.fillStyle = 'rgba(26,26,46,0.55)';
        g.c.font = '22px "Noto Sans Mono", monospace';
        g.c.fillText(spec.face, g.w / 2 + 3, g.h / 2 - 6);
        g.c.fillStyle = DARK;
        g.c.font = '30px "Noto Sans Mono", monospace';
        g.c.fillText(spec.face, g.w / 2, g.h / 2);
      } catch (e) { /* noop */ }
    } else {
      // THE WORD: two frames of static with one word barely legible in it.
      static_(g, spec.st.rand, 0.55);
      g.c.globalAlpha = 0.35;
      text(g, spec.word, g.w / 2, g.h / 2 - 6, 14, '#e8e8f2', 'center');
      g.c.globalAlpha = 1;
      static_r(g, spec.st.rand, 0.12);
    }
    void t;
  },
  end() { /* noop */ },
  caught: null,        // never acknowledged, so there is no caught beat at all
};

function static_r(g, rand, density) {
  const n = Math.round(density * 240);
  for (let i = 0; i < n; i++) {
    g.c.fillStyle = rand() > 0.5 ? 'rgba(255,255,255,0.5)' : 'rgba(0,0,0,0.5)';
    g.c.fillRect(Math.floor(rand() * g.w), Math.floor(rand() * g.h), 1 + Math.floor(rand() * 3), 1);
  }
}

/* ============================================================================
 * CH7 - SCREENSAVER. Code rain, and the one channel exempt from the cap.
 * ==========================================================================*/

const RAIN_GLYPHS = 'ABCDEFGHJKLMNPQRSTUVWXYZ0123456789アカサタナハマヤラワ';
const RAIN_STEP = 4;            // columns every 4 virtual px
const RAIN_CELL = 6;            // 4x6 glyph cells

export const saver = {
  id: 'saver',
  weight: 0,
  cooldownMs: 0,
  deep: true,
  /* THE ONE EXEMPTION (owner call 4): a screensaver that quits after ten
   * seconds reads as a malfunction, so this one runs until input. Every cancel
   * rule still applies instantly. */
  uncapped: true,
  plan(ctx) {
    if (ctx.reducedMotion) return null;    // the deep-idle slot falls to OFF AIR
    /* THE PHONE REFUSAL (perf/arcademy-mobile-dig): the one uncapped channel is
     * a canvas repainting every column of every frame until input, and on a
     * phone "deep idle" usually means the screen is burning in a pocket. The
     * global GPU-ceiling marker (core/device.js - never set on desktop) sends
     * the deep-idle slot to OFF AIR instead. pickDeepIdle consumed its seeded
     * roll before asking, so the session stays deterministic either way. */
    try {
      const h = typeof document !== 'undefined' ? document.documentElement : null;
      if (h && typeof h.getAttribute === 'function'
        && h.getAttribute('data-ae-touch-global') === '1') return null;
    } catch (e) { /* a probe must never cost the channel */ }
    return { seed: ctx.seed + '|rain' };
  },
  start(g, spec) {
    const rand = makeRng(spec.seed);
    const cols = Math.ceil(g.w / RAIN_STEP);
    const heads = [];
    for (let i = 0; i < cols; i++) {
      heads.push({
        y: -Math.floor(rand() * 40),
        speed: 12 + rand() * 26,           // cells per second
        // ABOUT ONE COLUMN IN TWENTY IS HERS. That is the tell under the costume.
        pink: rand() < 0.05,
        tail: 5 + Math.floor(rand() * 5),
        seed: Math.floor(rand() * 65536),
      });
    }
    spec.st = { heads, rand, last: 0 };
  },
  frame(g, spec, t) {
    const s = spec.st;
    const dt = Math.min(120, t - s.last) / 1000;
    s.last = t;
    clear(g, '#000');
    const rows = Math.ceil(g.h / RAIN_CELL) + 2;
    for (let i = 0; i < s.heads.length; i++) {
      const col = s.heads[i];
      col.y += col.speed * dt;
      if (col.y - col.tail > rows) { col.y = -2; col.seed = (col.seed * 31 + 7) & 65535; }
      const x = i * RAIN_STEP;
      for (let k = 0; k <= col.tail; k++) {
        const row = Math.floor(col.y) - k;
        if (row < 0 || row * RAIN_CELL > g.h) continue;
        const a = k === 0 ? 1 : Math.max(0, 1 - k / col.tail) * 0.7;
        const ch = RAIN_GLYPHS[(col.seed + row * 7 + k) % RAIN_GLYPHS.length];
        g.c.globalAlpha = a;
        text(g, ch, x, row * RAIN_CELL, RAIN_CELL, col.pink ? PINK : RAIN_GREEN);
      }
    }
    g.c.globalAlpha = 1;
    scanlines(g, 0.14);
  },
  end() { /* noop */ },
  caught: {
    face: '^_^', hold: 900, bodyFrame: 'idle',
    lineOdds: 0.25,
    lines: ['counting pixels.'],
  },
};

/* ============================================================================
 * CH8 - OFF AIR. The test card: the deep-idle look that survives reduced motion.
 * ==========================================================================*/

const BAR_COLOURS = Object.freeze(['#B36A75', '#5A6B84', '#F5F0E1', '#4E8C86', '#7A5C87', '#2F2F3D']);

export const offair = {
  id: 'offair',
  weight: 0,
  cooldownMs: 0,
  deep: true,
  uncapped: true,
  plan() { return { seed: 'offair' }; },     // it can always play, which is the point
  start(g, spec) { spec.st = {}; },
  frame(g, spec, t) {
    const bw = g.w / BAR_COLOURS.length;
    const dim = t > 4000;
    for (let i = 0; i < BAR_COLOURS.length; i++) {
      fill(g, BAR_COLOURS[i], i * bw, 0, bw + 1, g.h);
    }
    if (dim) fill(g, 'rgba(0,0,0,0.55)', 0, 0, g.w, g.h);
    // the centre plate
    const r = 30;
    g.c.fillStyle = DARK;
    try {
      g.c.beginPath();
      g.c.arc(g.w / 2, g.h / 2 - 6, r, 0, Math.PI * 2);
      g.c.fill();
    } catch (e) { fill(g, DARK, g.w / 2 - r, g.h / 2 - 6 - r, r * 2, r * 2); }
    text(g, 'brb', g.w / 2, g.h / 2 - 12, 12, CREAM, 'center');
    if (dim) {
      // NO SIGNAL drifts one step a second. That is the whole animation, and it
      // is why this is the reduced-motion-safe deep-idle look.
      const step = Math.floor((t - 4000) / 1000) % 6;
      text(g, 'NO SIGNAL', 10 + step * 4, g.h - 16, 7, CREAM);
    }
    scanlines(g, 0.16);
  },
  end() { /* noop */ },
  /* SHE FELL ASLEEP ON AIR. No blip: the bars cut to ZzZ and the wake chain
   * takes it from there. Wordless by design. */
  caught: { face: 'ZzZ', hold: 700, bodyFrame: 'idle', noBlip: true, chain: 'wake', lineOdds: 0 },
};

/* ============================================================================
 * THE WHEEL
 * ==========================================================================*/

/** The five ROLLED channels, in table order. Deep idle is not on the wheel. */
export const WHEEL = Object.freeze([pong, browsing, watching, reruns, shop]);

/** Every painter this module ships, by id - the takeover runner's lookup. */
export const CHANNELS = Object.freeze({
  pong, browsing, watching, reruns, shop, wrong, saver, offair,
});

/**
 * ROLL THE WHEEL. Every channel is asked to `plan()` first, so a channel with
 * no material tonight is simply ABSENT from the draw rather than a stub that
 * paints an apology.
 * @returns {{painter:Object, spec:Object}|null}
 */
export function rollChannel(ctx) {
  const rand = typeof ctx.rand === 'function' ? ctx.rand : Math.random;
  const live = [];
  let total = 0;
  for (const p of WHEEL) {
    if (ctx.cooldowns && ctx.cooldowns[p.id] > ctx.now) continue;
    let spec = null;
    try { spec = p.plan(ctx); } catch (e) { spec = null; }
    if (!spec) continue;
    const w = Math.max(0, p.weight | 0);
    if (!w) continue;
    total += w;
    live.push({ painter: p, spec, w });
  }
  if (!live.length) return null;
  let r = rand() * total;
  for (const row of live) {
    r -= row.w;
    if (r <= 0) return { painter: row.painter, spec: row.spec };
  }
  return { painter: live[live.length - 1].painter, spec: live[live.length - 1].spec };
}

/**
 * THE DEEP-IDLE LOOK, and it is deterministic - real screensavers are not
 * lucky. One look per session, seeded, so the two never flip-flop; reduced
 * motion always lands on OFF AIR because code rain is motion by definition.
 */
export function pickDeepIdle(ctx) {
  const order = ctx.reducedMotion ? [offair] : (makeRng(ctx.seed + '|deep')() < 0.5 ? [saver, offair] : [offair, saver]);
  for (const p of order) {
    let spec = null;
    try { spec = p.plan(ctx); } catch (e) { spec = null; }
    if (spec) return { painter: p, spec };
  }
  return null;
}

/**
 * THE INTRUSION ROLL, taken on a rolled channel's EXIT. A refusal is the normal
 * answer; the odds are 1 in 40 and it happens at most once a session.
 */
export function rollWrong(ctx) {
  const rand = typeof ctx.rand === 'function' ? ctx.rand : Math.random;
  if (rand() >= SL_DIALS.WRONG_ODDS) return null;
  let spec = null;
  try { spec = wrong.plan(ctx); } catch (e) { spec = null; }
  return spec ? { painter: wrong, spec } : null;
}

export default CHANNELS;
