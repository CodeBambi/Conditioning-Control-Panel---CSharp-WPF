const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
export function finaleHelpStrength(finale,state) {
  return state==='grey' && finale?.phase==='locked' ? clamp((finale.age-90)/30,0,1) : 0;
}
export function finaleHelpSeat({finale,state,paddle,w,h,bricks}) {
  if(!finaleHelpStrength(finale,state) || finale.wordIndex%3!==0)return null;
  const width=130,height=28,y=Math.min(h*.66,paddle.y-150);
  for(const offset of [0,-90,90,-180,180]) {
    const x=clamp(paddle.x+offset-width/2,20,w-width-20);
    if(bricks.some(b=>b.alive && x<b.x+b.w+12 && x+width>b.x-12 && y<b.y+b.h+12 && y+height>b.y-12))continue;
    return {x,y,w:width,h:height};
  }
  return null;
}
// Conservative segment/box test. Expanded bounds also cover rotated ring faces.
function blocked(x,y,tx,ty,bricks,target,radius) {
  for(const b of bricks) {
    if(!b.alive || b===target || b.finaleWord)continue;
    const margin=radius+Math.max(b.w,b.h)*Math.abs(Math.sin(b.angle||0))/2;
    let lo=0,hi=1;
    for(const [origin,delta,min,max] of [[x,tx-x,b.x-margin,b.x+b.w+margin],[y,ty-y,b.y-margin,b.y+b.h+margin]]) {
      if(Math.abs(delta)<1e-9) {if(origin<min||origin>max){hi=-1;break;}}
      else {const a=(min-origin)/delta,c=(max-origin)/delta;lo=Math.max(lo,Math.min(a,c));hi=Math.min(hi,Math.max(a,c));}
    }
    if(lo<=hi && hi>=0 && lo<=1)return true;
  }
  return false;
}
export function finaleHelpAngle({finale,state,bricks,ball,angle,speed}) {
  const strength=finaleHelpStrength(finale,state);
  if(!strength || speed<=0)return angle;
  const maxTurn=(3+7*strength)*Math.PI/180;
  let best=null,difference=Infinity;
  for(const br of bricks) {
    if(!br.alive || !br.finaleWord)continue;
    const x=br.x+br.w/2,y=br.y+br.h/2;
    if(y>=ball.y || br.ttl<=Math.hypot(x-ball.x,y-ball.y)/speed+.2)continue;
    const aim=Math.atan2(x-ball.x,ball.y-y);
    if(Math.abs(aim)>Math.PI/3 || blocked(ball.x,ball.y,x,y,bricks,br,ball.r||8))continue;
    const delta=Math.abs(aim-angle);
    if(delta<difference){difference=delta;best=aim;}
  }
  return best===null?angle:clamp(angle+clamp(best-angle,-maxTurn,maxTurn),-Math.PI/3,Math.PI/3);
}