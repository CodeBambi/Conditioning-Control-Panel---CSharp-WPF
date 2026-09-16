// Authored UV .5 is the payline, with 1.4 of a thirteen-cell strip visible.
export function stripTransform(angle, count, authored=13) {
  const repeat=authored/Math.max(1,count);
  return {repeat,offset:angle/(Math.PI*2)+.5-.5*repeat};
}
