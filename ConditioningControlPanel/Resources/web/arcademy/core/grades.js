/* ============================================================================
 * core/grades.js - the ONE shared rubric. BUILD-CONTRACT §8, SYNTHESIS #14.
 *
 * PURE FUNCTIONS, no DOM, no bridge. A game never grades itself: it reports a
 * weighted composite (0..1), optional hard gates, and whatever assists it used;
 * this module maps that to S / A / B / C (or 'pass' for zen) and applies the
 * roster-wide caps. Every threshold and cap lives in the ONE constants block
 * below so a playtest tune is a one-place edit.
 *
 * ORDER OF OPERATIONS (do not reorder - the caps are not commutative with the
 * letter lookup, and 'pass' short-circuits everything):
 *   1. zen             -> 'pass' (DECISIONS #1: no letter, counts for attendance
 *                         AND perfect_attendance, XP at the B row - the XP table
 *                         itself lives in C#, see BUILD-CONTRACT §8)
 *   2. composite       -> base letter by threshold
 *   3. hard gates      -> any DECLARED gate that failed caps the class at A
 *                         (Impulse Control's dual speed+restraint S-gate)
 *   4. A-caps          -> peek used / below-par board size / tempo assist
 *
 * "Cap at A" means the letter may not EXCEED A. It never promotes: a C stays a C.
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * THE CONSTANTS BLOCK (playtest-tunable, one place)
 * -------------------------------------------------------------------------- */
export const GRADE_THRESHOLDS = Object.freeze({
  S: 0.92,
  A: 0.75,
  B: 0.50,
  // below B -> C
});

/* THE HONORS LETTER (economy wave, 2026-08-26). S+ is the ONE grade a player
 * can choose to make reachable: it needs the Honors lever pulled AND a composite
 * at or above SPLUS_THRESHOLD, so it is never handed out by accident and never
 * appears on a run that did not ask for it. Everything about it is ADDITIVE:
 *  - the S threshold and every letter below it are byte-for-byte what they were,
 *    so a class played on Standard grades exactly as it always did;
 *  - it is NOT in GRADE_ORDER, because that list is the CEILING ladder the caps
 *    walk (`capAt` indexes it) and no cap may ever raise a letter to S+;
 *  - `gradeRank` answers -1 for it, one better than S's 0, so every existing
 *    "lower rank is better" comparison keeps working and S+ simply wins.
 * The host re-derives the honors flag from the lever it stored at class-started
 * and degrades a claimed S+ to S when the lever was not Honors - the page may
 * propose this letter, it may never mint it. */
export const SPLUS = 'S+';
export const SPLUS_THRESHOLD = 0.97;

/** Best -> worst. Index doubles as the rank (0 = best). S+ sits ABOVE this list
 *  (rank -1) on purpose - see the note above. */
export const GRADE_ORDER = Object.freeze(['S', 'A', 'B', 'C']);
export const PASS = 'pass';

/** Every A-cap reason the shell can raise, and the ceiling each one imposes. */
export const CAP_REASONS = Object.freeze({
  peek: 'A',            // SYNTHESIS #6 - shared peek verb, flat grade cap
  below_par_board: 'A', // SYNTHESIS #7 - "playing below tier par caps at A"
  tempo_assist: 'A',    // SYNTHESIS #12 - echoPlaybackTempo < 0.85x
  hard_gate: 'A',       // BUILD-CONTRACT §8 - "fail gate = max A"
  stuck_rescue: 'A',    // Composure's grade-capped assist
  accessibility_aid: 'A', // Misdirection's numbered-shell aid
});

/** Promotion rule (shell-owned, BUILD-CONTRACT §11): S or A promotes a tier.
 *  S+ promotes for the same reason S does - it is an S with the lever pulled. */
export const PROMOTING_GRADES = Object.freeze([SPLUS, 'S', 'A']);

/* ----------------------------------------------------------------------------
 * SMALL HELPERS
 * -------------------------------------------------------------------------- */
export function clamp01(n) {
  const v = Number(n);
  if (!Number.isFinite(v)) return 0;
  return v < 0 ? 0 : v > 1 ? 1 : v;
}

/** True for the honors letter, however it was cased or spaced. */
export function isSPlus(grade) {
  return String(grade == null ? '' : grade).trim().toUpperCase() === SPLUS;
}

/** 0 = best (S), -1 for S+. 'pass' and anything unknown sort last. Every caller
 *  compares ranks rather than reading the number, so the one negative rung is
 *  free: "smaller is better" was always the contract. */
export function gradeRank(grade) {
  if (isSPlus(grade)) return -1;
  const i = GRADE_ORDER.indexOf(String(grade || '').toUpperCase());
  return i < 0 ? GRADE_ORDER.length : i;
}

/** Cap a letter so it cannot exceed `ceiling`. Never promotes. */
export function capAt(grade, ceiling) {
  if (grade === PASS) return PASS;
  const g = String(grade || '').toUpperCase();
  const gi = gradeRank(g), ci = gradeRank(ceiling);
  if (gi >= ci) return g;                 // already at or below the ceiling
  return GRADE_ORDER[ci] || g;
}

/** Base letter for a composite, before any gate or cap. */
export function letterFor(composite) {
  const c = clamp01(composite);
  if (c >= GRADE_THRESHOLDS.S) return 'S';
  if (c >= GRADE_THRESHOLDS.A) return 'A';
  if (c >= GRADE_THRESHOLDS.B) return 'B';
  return 'C';
}

/**
 * The CSS-SAFE key for a letter. Every badge on the page is styled by
 * `.grade.<key>`, and 'S+' lowercases to `s+` - which is a class attribute CSS
 * will not match without an escape, so the honours letter would have painted as
 * the unstyled default everywhere at once. One helper, so the report card, the
 * records wall and the campus chip cannot disagree about the answer.
 * @returns {string} 'splus' | 's' | 'a' | 'b' | 'c' | 'pass' | ''
 */
export function gradeKey(grade) {
  const raw = String(grade == null ? '' : grade).trim().toLowerCase();
  return raw === 's+' ? 'splus' : raw;
}

/** True when this grade should promote the player's per-game tier. */
export function isPromoting(grade) {
  return PROMOTING_GRADES.indexOf(String(grade || '').toUpperCase()) >= 0;
}

/* ----------------------------------------------------------------------------
 * THE RUBRIC
 * -------------------------------------------------------------------------- */
/**
 * @typedef {Object} GradeInput
 * @property {{composite:number}} metrics      the game's weighted composite 0..1
 * @property {Object=} hardGates               declared gates, e.g. {sGate:true}. FALSE = failed.
 * @property {boolean=} zen                    zen / untimed class
 * @property {Object=} assists                 {peek, below_par_board, tempo_assist, ...} truthy = used
 * @property {boolean=} honors                 the Honors lever was pulled for this run
 *
 * @typedef {Object} GradeResult
 * @property {'S'|'A'|'B'|'C'|'pass'} grade
 * @property {number} composite                the clamped composite actually used
 * @property {'S'|'A'|'B'|'C'} baseGrade        letter before gates/caps (informational)
 * @property {string[]} capped                 cap reasons that actually lowered the letter
 * @property {string[]} gatesFailed            names of declared gates that failed
 * @property {boolean} zen
 */

/**
 * Grade one class.
 * @param {GradeInput} input
 * @returns {GradeResult}
 */
export function gradeClass(input) {
  const src = input || {};
  const metrics = src.metrics || {};
  const composite = clamp01(metrics.composite);
  const zen = !!src.zen;

  if (zen) {
    // DECISIONS #1: pass-only, no letter. Assists are irrelevant to a pass, and
    // a zen class can neither fail a gate nor earn an S.
    return {
      grade: PASS, composite, baseGrade: letterFor(composite),
      capped: [], gatesFailed: [], zen: true,
    };
  }

  /* THE HONORS RUNG. Only ever reached with the lever pulled, and only from a
   * composite that would already have been an S with room to spare. It is put on
   * the BASE letter rather than after the caps so a hard gate or an A-cap knocks
   * it straight down to A like any other top letter - honors buys a ceiling, it
   * never buys forgiveness. */
  const baseGrade = (src.honors === true && composite >= SPLUS_THRESHOLD)
    ? SPLUS : letterFor(composite);
  let grade = baseGrade;
  const capped = [];
  const gatesFailed = [];

  // Records a reason only when it actually LOWERED the letter; a cap that bites
  // nothing (the player was at B anyway) is reported by capsRaised() instead, so
  // the report card can still be honest about it.
  const applyCap = (reason) => {
    const next = capAt(grade, CAP_REASONS[reason] || 'A');
    if (next !== grade) { grade = next; capped.push(reason); }
  };

  // 3. hard gates: only DECLARED gates count. A gate the game never declared is
  //    not a failure (a missing key must never silently cap a class at A).
  const gates = src.hardGates;
  if (gates && typeof gates === 'object') {
    for (const name of Object.keys(gates)) {
      if (gates[name] === false) gatesFailed.push(name);
    }
  }
  if (gatesFailed.length) applyCap('hard_gate');

  // 4. A-caps from assists. Shell-detected (peek, below-par board) and
  //    game-reported (tempo assist, stuck rescue) arrive in the same bag.
  const assists = src.assists;
  if (assists && typeof assists === 'object') {
    for (const reason of Object.keys(CAP_REASONS)) {
      if (reason === 'hard_gate') continue;
      if (assists[reason]) applyCap(reason);
    }
  }

  return { grade, composite, baseGrade, capped, gatesFailed, zen: false };
}

/**
 * Every cap reason the input WOULD raise, whether or not it changed the letter.
 * The report card uses this to explain "capped at A" honestly even when the
 * player's composite never reached S anyway.
 */
export function capsRaised(input) {
  const src = input || {};
  const out = [];
  const gates = src.hardGates;
  if (gates && typeof gates === 'object' && Object.keys(gates).some((k) => gates[k] === false)) out.push('hard_gate');
  const assists = src.assists;
  if (assists && typeof assists === 'object') {
    for (const reason of Object.keys(CAP_REASONS)) {
      if (reason !== 'hard_gate' && assists[reason]) out.push(reason);
    }
  }
  return out;
}

export default gradeClass;
