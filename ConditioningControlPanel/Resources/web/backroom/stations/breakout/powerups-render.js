import {shieldY} from './words/let-go.js';
import {POWER_DURATION,WARN_AT,MUZZLE_S,swayX} from './powerups.js';
const COLORS={multiball:'#c693ff',fireball:'#ffc46d',laser:'#ff8fce',shield:'#85f5db'};
export function drawPowerIcon(g,kind,x,y,size=12,grey=false){
  g.save();g.translate(x,y);g.scale(size/12,size/12);g.lineWidth=2;g.lineCap='round';
  g.strokeStyle='#191326';g.fillStyle=grey?'#d5d5d5':COLORS[kind];
  g.beginPath();g.arc(0,0,12,0,Math.PI*2);g.fill();g.stroke();
  g.strokeStyle='#241830';g.fillStyle='#fff';
  if(kind==='multiball'){for(const x of [-5,5]){g.beginPath();g.arc(x,0,3,0,7);g.fill();g.stroke();}}
  if(kind==='fireball'){g.beginPath();g.moveTo(-5,5);g.lineTo(-6,-1);g.lineTo(-1,-7);g.lineTo(0,-2);g.lineTo(4,-5);g.lineTo(6,3);g.lineTo(2,7);g.closePath();g.fill();g.stroke();}
  if(kind==='laser'){for(const x of [-4,4]){g.beginPath();g.moveTo(x,6);g.lineTo(x,-6);g.stroke();}g.fillRect(-7,5,14,3);}
  if(kind==='shield'){g.beginPath();g.moveTo(-6,-6);g.lineTo(6,-6);g.lineTo(5,2);g.lineTo(0,7);g.lineTo(-5,2);g.closePath();g.fill();g.stroke();}
  g.restore();
}
const FLAME=['#fff1c2','#ffc46d','#ff9a4d','#ff6a3d'],TAU=Math.PI*2;
const disc=(g,x,y,r)=>{g.beginPath();g.arc(x,y,Math.max(.3,r),0,TAU);g.fill();};
/** A falling pickup: ghosts along the path it really took (swayX), a breathing glow, the icon rocking on its way down. */
function drawDrop(g,d,s,still){
  const age=d.age||0,vy=d.vy||145,c=COLORS[d.kind];
  g.save();g.globalCompositeOperation='lighter';g.fillStyle=c;
  if(!still)for(let k=4;k>=1;k--){const t=k*.05;g.globalAlpha=.34*(1-k/5);disc(g,Math.min(s.w-14,Math.max(14,swayX(d,age-t))),d.y-vy*t,11-k*1.7);}
  g.globalAlpha=still?.16:.13+.05*Math.sin(age*5);disc(g,d.x,d.y,21);g.globalAlpha=.2;disc(g,d.x,d.y,16);
  g.restore();
  g.save();g.translate(d.x,d.y);if(!still)g.rotate(Math.sin(age*3+(d.ph||0))*.3);
  drawPowerIcon(g,d.kind,0,0,still?12:12*(1+.06*Math.sin(age*7)));g.restore();
}
/** Fireball: a corona, a flame licking back along the ball's own trail, embers lifting off it. No state, no allocation. */
function drawFire(g,b,time,step){
  const tr=b.trail||[],n=tr.length>>1,r=b.r||7;
  for(let k=Math.min(n-1,39);k>=1;k-=step){
    const i=(n-1-k)*2,f=k/40,flick=1+.2*Math.sin(time*31+k*1.7);
    g.globalAlpha=.5*(1-f);g.fillStyle=FLAME[1+Math.min(2,Math.floor(f*3.4))];disc(g,tr[i]+Math.sin(time*19+k)*f*4,tr[i+1]-f*7,r*(1.5-f)*flick);
  }
  for(let j=0;j<6&&n>2;j++){                                            // embers ride a trail point up and out as they age
    const f=(time*3+j/6)%1,i=(n-1-Math.min(n-1,Math.floor(f*38)))*2;
    g.globalAlpha=.85*(1-f);g.fillStyle=FLAME[j&1];disc(g,tr[i]+Math.sin(j*12.9+f*5)*9*f,tr[i+1]-f*18,2.4*(1-f*.8));
  }
  g.globalAlpha=.3;g.fillStyle=FLAME[2];disc(g,b.x,b.y,r+6+Math.sin(time*23)*1.2);g.globalAlpha=.4;g.fillStyle=FLAME[0];disc(g,b.x,b.y,r+2.5);
}
export function drawPowerups(g,s){
  const p=s.power;if(!p||s.state!=='colour')return;
  const still=!!s.reduced,time=s.time||0,pad=s.paddle;
  for(const d of p.drops)drawDrop(g,d,s,still);
  g.save();g.lineCap='round';
  // Bolts: a wide soft tail, the coloured body, a white core. Three passes so the styles change three times, not per bolt.
  const bolt=(len,w,c,a)=>{g.strokeStyle=c;g.lineWidth=w;g.globalAlpha=a;for(const b of p.shots){g.beginPath();g.moveTo(b.x,b.y);g.lineTo(b.x,b.y+len);g.stroke();}};
  if(p.shots.length){g.globalCompositeOperation='lighter';bolt(34,6,COLORS.laser,.22);g.globalCompositeOperation='source-over';bolt(17,3.5,COLORS.laser,.95);bolt(12,1.5,'#fff',.95);}
  if(p.laser>0){
    const m=still?0:Math.min(1,(p.muzzle||0)/MUZZLE_S);                  // 1 on the volley, 0 a tenth of a second later
    for(const sign of [-1,1]){
      const x=pad.x+sign*(pad.w/2-10),y=pad.y-12+m*2.5;                  // the barrels kick back as they fire
      g.globalAlpha=1;g.fillStyle=COLORS.laser;g.fillRect(x-3,y,6,10);g.fillStyle='#fff';g.fillRect(x-1,y,2,4);
      if(m>0){g.globalCompositeOperation='lighter';g.globalAlpha=m;g.fillStyle='#ffd9ef';disc(g,x,y-2,3+8*m);g.fillRect(x-1.5,y-26*m-2,3,26*m);g.globalCompositeOperation='source-over';}
    }
  }
  if(p.fireball>0){
    let live=0;for(const b of s.balls)if(!b.lost&&!b.falling)live++;
    if(still){g.strokeStyle=COLORS.fireball;g.lineWidth=3;g.globalAlpha=1;for(const b of s.balls){if(b.lost||b.falling)continue;g.beginPath();g.arc(b.x,b.y,b.r+4,0,7);g.stroke();}}
    else{g.globalCompositeOperation='lighter';for(const b of s.balls)if(!b.lost&&!b.falling)drawFire(g,b,time,live>3?5:2);g.globalCompositeOperation='source-over';}
  }
  if(p.charges>0){g.strokeStyle=COLORS.shield;g.lineWidth=2;g.globalAlpha=p.shield<3?.4+.3*Math.sin(time*12):.8;for(let i=0;i<p.charges;i++){g.beginPath();g.moveTo(6,shieldY(pad,s.h)-i*5);g.lineTo(s.w-6,shieldY(pad,s.h)-i*5);g.stroke();}}
  g.restore();
  // The HUD under the paddle: readable bars on a dark track, and the last WARN_AT seconds pulse (white and steady when reduced).
  let count=0;for(const kind of Object.keys(COLORS))if(p[kind]>0&&(kind!=='shield'||p.charges))count++;
  const cx=Math.min(s.w-22-(count-1)*21,Math.max(22+(count-1)*21,pad.x));let slot=0;
  for(const kind of Object.keys(COLORS))if(p[kind]>0&&(kind!=='shield'||p.charges)){
    const x=cx+(slot++-(count-1)/2)*42,y=pad.y+24,warn=p[kind]<=WARN_AT,left=Math.min(1,p[kind]/POWER_DURATION[kind]);
    g.save();if(warn&&!still)g.globalAlpha=.6+.4*Math.sin(time*16);
    drawPowerIcon(g,kind,x,y,9);
    g.fillStyle='rgba(20,14,32,.7)';g.fillRect(x-17,y+12,34,5);
    g.fillStyle=warn&&still?'#fff':COLORS[kind];g.fillRect(x-16,y+13,32*left,3);g.restore();
  }
}
