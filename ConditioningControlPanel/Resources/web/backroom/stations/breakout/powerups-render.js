import {shieldY} from './words/let-go.js';
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
export function drawPowerups(g,s){
  const p=s.power;if(!p||s.state!=='colour')return;const grey=s.state==='grey';
  for(const d of p.drops){g.save();g.globalAlpha=.24;g.strokeStyle=grey?'#ddd':COLORS[d.kind];g.lineWidth=3;g.beginPath();g.moveTo(d.x,d.y-23);g.lineTo(d.x,d.y-14);g.stroke();g.restore();drawPowerIcon(g,d.kind,d.x,d.y,12,grey);}
  g.save();g.lineCap='round';g.strokeStyle=grey?'#eee':COLORS.laser;g.lineWidth=3;
  for(const b of p.shots){g.beginPath();g.moveTo(b.x,b.y);g.lineTo(b.x,b.y+13);g.stroke();}
  if(p.laser>0)for(const sign of [-1,1]){g.fillStyle=grey?'#ccc':COLORS.laser;g.fillRect(s.paddle.x+sign*(s.paddle.w/2-10)-3,s.paddle.y-12,6,10);}
  if(p.fireball>0){g.strokeStyle=grey?'#eee':COLORS.fireball;g.lineWidth=3;for(const b of s.balls){if(b.lost||b.falling)continue;g.beginPath();g.arc(b.x,b.y,b.r+4,0,7);g.stroke();}}
  if(p.charges>0){g.strokeStyle=grey?'#ddd':COLORS.shield;g.lineWidth=2;g.globalAlpha=p.shield<3?.4+.3*Math.sin(s.time*12):.8;for(let i=0;i<p.charges;i++){g.beginPath();g.moveTo(6,shieldY(s.paddle,s.h)-i*5);g.lineTo(s.w-6,shieldY(s.paddle,s.h)-i*5);g.stroke();}}
  g.restore();
  let slot=0;for(const kind of Object.keys(COLORS))if(p[kind]>0&&(kind!=='shield'||p.charges)){
    const x=s.paddle.x+(slot++-1.5)*31,y=s.paddle.y+23;drawPowerIcon(g,kind,x,y,8,grey);
    g.save();g.fillStyle=grey?'#ddd':COLORS[kind];g.fillRect(x-9,y+11,18*Math.min(1,p[kind]/({multiball:12,fireball:8,laser:8,shield:20}[kind])),2);g.restore();
  }
}
