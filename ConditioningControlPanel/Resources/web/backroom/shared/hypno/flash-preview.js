/** Every Back Room flash drifts slowly; variants change speed and direction. */
export function flashPreviewFrames({width,height,portrait=false,opacity=1,motion=true,variant=0}) {
  const mode=Math.abs(Math.floor(variant))%6,base='translate(-50%,-50%)';
  const pace=.6+mode*.22,sign=mode%2?-1:1;
  const dx=width*.16*pace*sign,dy=height*(portrait?.08:.12)*pace*(mode%3?-1:1);
  return [0,.08,.32,.58,.84,1].map((t,i)=>({
    transform:motion?`${base} translate(${dx*(t-.5)}px,${dy*(t-.5)}px) rotate(${sign*(t-.5)*4}deg)`:base,
    opacity:i===0||i===5?0:opacity,offset:t
  }));
}
