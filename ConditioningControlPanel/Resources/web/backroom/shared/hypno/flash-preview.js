/** Occasional Back Room samples of V2 motion. Never changes global ownership or settings. */
export function flashPreviewFrames({ width, height, portrait = false, opacity = 1, motion = true, variant = 0 }) {
  const mode = motion ? Math.abs(Math.floor(variant)) % 3 : 0;
  const base = 'translate(-50%,-50%)';
  const dx = portrait ? width * .12 : width * .08, dy = portrait ? height * .06 : height * .16;
  const pose = (x, y, angle = 0) => `${base} translate(${x}px,${y}px) rotate(${angle}deg)`;
  const points = mode === 1
    ? [pose(-dx,-dy), pose(dx,dy), pose(-dx,dy*.5), pose(dx*.5,-dy), base]
    : mode === 2
      ? [pose(-dx,0,-8), pose(dx,dy,8), pose(-dx,0,-6), pose(dx,dy,5), base]
      : [base,base,base,base,base];
  return [
    { transform: points[0], opacity: 0, offset: 0 },
    { transform: points[0], opacity, offset: .08 },
    { transform: points[1], opacity, offset: .32 },
    { transform: points[2], opacity, offset: .58 },
    { transform: points[3], opacity, offset: .84 },
    { transform: points[4], opacity: 0, offset: 1 },
  ];
}
