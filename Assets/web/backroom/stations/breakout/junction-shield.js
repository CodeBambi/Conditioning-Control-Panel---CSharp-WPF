/** Automatic protection belongs to scene transitions, independent of word effects. */
export function junctionProtected(s) {
  const f=s.finale;
  return s.wallAge<1.9 || s.transition?.kind==='breakout' || !!s.pendingBreakout ||
    !!s.reform?.moving || !!(s.spell?.celebrate>0) ||
    !!(f && (f.phase==='forming' || f.phase==='interrupt' || f.phase==='outro' ||
      (f.phase==='released' && f.stage===2)));
}
