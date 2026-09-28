// Local showcase only. Produces ordinary paddle input and never changes simulation state.
const clamp = (n, lo, hi) => Math.max(lo, Math.min(hi, n));
function reflected(x, left, right) {
  const span = right - left, u = ((x - left) % (span * 2) + span * 2) % (span * 2);
  return left + (u <= span ? u : span * 2 - u);
}

/** createShowcasePilot({launchX: .44}).input(game.snapshot()) -> {x, launch}.
 * Call once before each normal simulation frame. No hidden gameplay assists.
 */
export function createShowcasePilot({launchX = .44} = {}) {
  let target = null, board = null;
  return {input(g) {
    if (board !== g.endlessBoard) { target = null; board = g.endlessBoard; }
    const plane = g.paddle.y - g.paddle.h / 2;
    const live = g.balls.filter(b => !b.lost && !b.falling);
    const candidates = live.filter(b => !b.stuck && !b.orbit && b.vy > 0)
      .map(b => ({b, t: Math.max(0, (plane - b.r - b.y) / b.vy)})).sort((a,b)=>a.t-b.t);
    if (!candidates.length) {
      const stuck = live.find(b => b.stuck);
      const x=stuck ? g.w * launchX : g.paddle.x;
      return {x, launch:!stuck || Math.abs(stuck.x-clamp(x,g.paddle.w/2,g.w-g.paddle.w/2))<2};
    }
    const {b,t} = candidates[0];
    const x = reflected(b.x + b.vx * t, b.r, g.w - b.r), y = plane - b.r;
    const remaining = g.bricks.filter(br => br.alive && !(g.state === 'grey' && br.greyMetal));
    const hinges = remaining.filter(br => br.pendulumAnchor);
    if (!target?.alive || (hinges.length && !target.pendulumAnchor)) target = null;
    if (!target) {
      const pool = hinges.length ? hinges : remaining;
      target = pool.reduce((best,br) => !best || Math.abs(br.x+br.w/2-x)<Math.abs(best.x+best.w/2-x) ? br : best, null);
    }
    const tx=target ? target.x+target.w/2 : g.w/2, ty=target ? target.y+target.h/2 : g.h*.2;
    const angle=clamp(Math.atan2(tx-x,y-ty),-.91,.91);
    // Keep the arriving paddle settled; its built-in English then follows this aim.
    const offset = angle / (Math.PI/3) * g.paddle.w/2;
    return {x:clamp(x-offset,g.paddle.w/2,g.w-g.paddle.w/2),launch:true};
  }};
}
