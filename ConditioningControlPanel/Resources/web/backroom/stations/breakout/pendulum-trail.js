// Taper toward the oldest sample; never bridge a teleport or a capture snap.
export function pendulumTrailSegments(p) {
  const points = [...(p.trail || []), { x:p.x, y:p.y }];
  const segments = [];
  let length = 0;
  const maxLength = p.mode === 'orbit' ? 115 : 145;
  for (let i = points.length - 1; i > 0; i--) {
    const a = points[i - 1], b = points[i], d = Math.hypot(b.x-a.x,b.y-a.y);
    if (!Number.isFinite(d) || d > 85) break;
    if (d < .1) continue;
    const remaining = maxLength-length;
    if (remaining <= 0) break;
    const take = Math.min(remaining,d), t = take/d;
    segments.push({ a:{x:b.x+(a.x-b.x)*t,y:b.y+(a.y-b.y)*t}, b,
      strength: Math.pow(1-(length+take*.5)/maxLength,1.6) });
    length += take;
  }
  return segments.reverse();
}
export function drawPendulumTrail(ctx,p,colour) {
  ctx.save(); ctx.lineCap='round'; ctx.lineJoin='round';
  for (const segment of pendulumTrailSegments(p)) {
    const f=segment.strength;
    ctx.strokeStyle=colour(.05+.4*f); ctx.lineWidth=.5+6*f;
    ctx.beginPath();ctx.moveTo(segment.a.x,segment.a.y);ctx.lineTo(segment.b.x,segment.b.y);ctx.stroke();
  }
  ctx.restore();
}
