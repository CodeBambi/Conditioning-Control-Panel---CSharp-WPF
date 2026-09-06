/* ============================================================================
 * race/smoke/fov-check.mjs - node self-check for race/viewport.js.
 *
 *   node race/smoke/fov-check.mjs      (exits 0 on pass, 1 with a count of failures)
 *
 * viewport.js imports nothing, so this needs no three and no DOM. It checks the
 * one promise CONTRACT.md "Camera aspect rule" makes: the HORIZONTAL fov is the
 * constant, the vertical is whatever holds it. In order: 16:9 is the identity so
 * the desktop look never moves; a 390x844 phone lands on the clamp; an 844x390
 * phone comes back narrower than the base; the horizontal width is genuinely held
 * everywhere the clamp is not biting; the run and menu base fovs both survive a
 * round trip; and junk arguments do not produce NaN.
 * ==========================================================================*/

import { vFovForAspect, hFovFor, REF_ASPECT, MAX_VFOV, MIN_VFOV } from '../viewport.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const near = (a, b, eps = 1e-9) => Math.abs(a - b) <= eps;

const RUN_BASE = 72, MENU_BASE = 42;
const PORTRAIT = 390 / 844, LANDSCAPE = 844 / 390, DESK = 1280 / 720;

/* ---- 1. 16:9 is the identity -------------------------------------------- */
{
  ok(near(vFovForAspect(RUN_BASE, REF_ASPECT), RUN_BASE), '16:9 gives back exactly 72 (desktop is untouched)');
  ok(near(vFovForAspect(RUN_BASE, DESK), RUN_BASE), '1280x720 is 16:9, so it gives back exactly 72');
  ok(near(vFovForAspect(MENU_BASE, REF_ASPECT, 66), MENU_BASE), 'the menu stage gives back exactly 42 at 16:9');
}

/* ---- 2. a tall phone opens up, onto the clamp --------------------------- */
{
  const v = vFovForAspect(RUN_BASE, PORTRAIT);
  ok(near(PORTRAIT, 0.4621, 1e-3), '390x844 is an aspect of about 0.46');
  ok(v === MAX_VFOV, `0.46 clamps to MAX_VFOV (${MAX_VFOV}), got ${v.toFixed(2)}`);
  ok(vFovForAspect(RUN_BASE, PORTRAIT, 88) === 88, 'a caller with its own cap gets its own cap');
  ok(hFovFor(v, PORTRAIT) > hFovFor(RUN_BASE, PORTRAIT) + 20, 'the clamped vertical still buys 20+ degrees of width back');
}

/* ---- 3. a wide phone closes down ---------------------------------------- */
{
  const v = vFovForAspect(RUN_BASE, LANDSCAPE);
  ok(near(LANDSCAPE, 2.164, 1e-3), '844x390 is an aspect of about 2.16');
  ok(v < RUN_BASE, `2.16 comes back below 72 (no fisheye), got ${v.toFixed(2)}`);
  ok(v > 55 && v < 68, `2.16 lands in a sane band, got ${v.toFixed(2)}`);
}

/* ---- 4. the horizontal fov is the thing being held ----------------------- */
{
  const want = hFovFor(RUN_BASE, REF_ASPECT);
  let held = true, clamped = 0;
  for (const a of [0.5, 0.75, 1, 4 / 3, 16 / 10, REF_ASPECT, 2, 2.164, 21 / 9, 32 / 9]) {
    const v = vFovForAspect(RUN_BASE, a);
    if (v === MAX_VFOV || v === MIN_VFOV) { clamped++; continue; }
    if (!near(hFovFor(v, a), want, 1e-6)) held = false;
  }
  ok(held, `every unclamped aspect holds the same ${want.toFixed(2)} degrees of width`);
  ok(clamped >= 1 && clamped <= 3, 'only the extreme aspects need the clamp');
  ok(want > 100 && want < 110, `72 at 16:9 is about ${want.toFixed(1)} degrees horizontal`);
}

/* ---- 5. junk in, no NaN out --------------------------------------------- */
{
  const bad = [vFovForAspect(RUN_BASE, 0), vFovForAspect(RUN_BASE, -3), vFovForAspect(RUN_BASE, NaN), vFovForAspect(NaN, 1.5)];
  ok(bad.every((v) => Number.isFinite(v) && v >= MIN_VFOV && v <= MAX_VFOV), 'a bad aspect or base still returns a fov inside the clamp');
}

console.log(fails ? `\n${fails} failure(s)` : '\nfov-check: all good');
process.exit(fails ? 1 : 0);
