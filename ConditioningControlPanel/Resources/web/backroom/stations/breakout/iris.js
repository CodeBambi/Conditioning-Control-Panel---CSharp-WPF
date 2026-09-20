// Six bounded streams. Geometry is shared by the simulation and its visible flow.
export const IRIS_ARMS = 6, IRIS_LIFE = 28, IRIS_INTERVAL = 1.4;
export function irisPose(arm, age, w=1280, h=720, rotation=0) {
  const t=Math.max(0,Math.min(1,age/IRIS_LIFE)), r=1-t;
  const a=arm*Math.PI/3 + t*2.5 + rotation;
  const rx=Math.min(w*.36,h*.52), ry=Math.min(w*.28,h*.29), cx=w/2, cy=h*.42;
  const fade=Math.min(1,r/.16), size=.55+.45*Math.min(1,r/.25);
  const bw=39*size,bh=24*size;
  const dx=rx*(-Math.cos(a)-r*2.5*Math.sin(a));
  const dy=ry*(-Math.sin(a)+r*2.5*Math.cos(a));
  return {x:cx+rx*r*Math.cos(a)-bw/2,y:cy+ry*r*Math.sin(a)-bh/2,w:bw,h:bh,angle:Math.atan2(dy,dx),irisAlpha:fade};
}
