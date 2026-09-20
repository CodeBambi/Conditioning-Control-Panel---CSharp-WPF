// Authored UV .5 is the payline, with 1.4 of a thirteen-cell strip visible.
export function stripTransform(angle, count, authored=13) {
  // No cells is not one cell: authored/1 repeats a blank strip thirteen times across the reel.
  const repeat=count>0?authored/count:1;
  return {repeat,offset:angle/(Math.PI*2)+.5-.5*repeat};
}
