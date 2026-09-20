// Compact stroke lettering. Stroke samples place fixed-size surviving bricks.
export const REFORM_WORDS = ['RELEASE', 'BREATHE', 'SOFTEN', 'RELAX', 'LET GO', 'SINK', 'MELT', 'YES', 'I'];
const top=[0,0,4,.7], mid=[0,2.65,4,.7], bot=[0,5.3,4,.7];
const left=[0,.7,.7,4.6], right=[3.3,.7,.7,4.6];
const stroke=(x1,y1,x2,y2)=>{const w=Math.hypot(x2-x1,y2-y1);return [(x1+x2)/2-w/2,(y1+y2)/2-.35,w,.7,Math.atan2(y2-y1,x2-x1)];};
const glyphs={
 R:[top,mid,[0,.7,.7,5.3],[3.3,.7,.7,1.95],[2,3.35,.7,1.3],[3.3,4.65,.7,1.35]],
 E:[top,mid,bot,[0,.7,.7,2.3],[0,3,.7,2.3]], L:[left,bot], A:[top,mid,left,right],
 S:[stroke(3,0,1,2),stroke(1,2,3,4),stroke(3,4,1,6)],
 B:[top,mid,bot,left,right], T:[top,[1.65,.7,.7,5.3]],
 H:[left,right,mid], F:[top,mid,left], O:[top,bot,left,right],
 N:[left,right,stroke(.5,.5,3.5,5.5)],
 X:[stroke(.4,.4,3.6,5.6),stroke(3.6,.4,.4,5.6)],
 M:[left,right,stroke(.4,.5,2,3),stroke(3.6,.5,2,3)],
 G:[top,bot,left,[2,2.65,2,.7],[3.3,3.35,.7,1.95]],
 I:[top,bot,[1.65,.7,.7,2.3],[1.65,3,.7,2.3]],
 K:[left,[2.6,0,1.4,1.3],[1.3,1.3,1.3,1.35],[.7,2.65,1.3,.7],[1.3,3.35,1.3,1.35],[2.6,4.7,1.4,1.3]],
 Y:[stroke(.4,.4,2,2.8),stroke(3.6,.4,2,2.8),[1.65,2.8,.7,1.6],[1.65,4.4,.7,1.6]],
};
export function reformMinimum(word) {
 return Array.from(word).reduce((n,c)=>n+(glyphs[c]?.length||0),0);
}
// Cycle only among the fullest shapes the remaining brick budget can support.
export function nextReformIndex(current,count) {
 const feasible=REFORM_WORDS.map((word,index)=>({index,min:reformMinimum(word)})).filter(x=>x.min<=count);
 if(!feasible.length)return -1;
 const largest=Math.max(...feasible.map(x=>x.min));
 const candidates=feasible.filter(x=>x.min>=largest*.75);
 return (candidates.find(x=>x.index>current)||candidates[0]).index;
}
export function reformLayout(word,count,width=1280) {
 if(count<reformMinimum(word)) return [];
 const units=word.length*5.1-1.1, scale=.8*Math.min(65,(width-110)/units), vertical=30.4, cells=[];
 const growth=1+.5*Math.max(0,Math.min(1,(100-count)/96)), bw=35.2*growth, bh=21.6*growth;
 for(let i=0;i<word.length;i++) for(const [x,y,w,h,angle=0] of glyphs[word[i]]||[]) {
  cells.push({x:(width-units*scale)/2+(i*5.1+x)*scale,y:34+y*vertical,w:w*scale,h:h*vertical,angle,letter:word[i]});
 }
 while(cells.length<count) {
  let ix=0; for(let i=1;i<cells.length;i++) if(Math.max(cells[i].w,cells[i].h)>Math.max(cells[ix].w,cells[ix].h)) ix=i;
  const a=cells[ix], b={...a};
  const cx=a.x+a.w/2, cy=a.y+a.h/2, co=Math.cos(a.angle), si=Math.sin(a.angle);
  let dx,dy;
  if(a.w>=a.h){a.w/=2;b.w=a.w;dx=co*a.w/2;dy=si*a.w/2;}
  else {a.h/=2;b.h=a.h;dx=-si*a.h/2;dy=co*a.h/2;}
  a.x=cx-dx-a.w/2;a.y=cy-dy-a.h/2;b.x=cx+dx-b.w/2;b.y=cy+dy-b.h/2;
  cells.push(b);
 }
 // Samples may overlap. Never squeeze payload faces to fit a stroke segment.
 return cells.map(c=>({...c,x:c.x+c.w/2-bw/2,y:c.y+c.h/2-bh/2,
  w:bw,h:bh,angle:c.angle+(c.h>c.w?Math.PI/2:0)}));
}
export function drawMetronome(ctx,s,reduced=false) {
 const r=s.reform;if(!r)return;
 const phase=r.beats+s.beatPhase, angle=reduced?0:Math.cos(phase*Math.PI)*.38;
 ctx.save();ctx.translate(s.w/2,s.h*.65);ctx.globalAlpha=s.state==='grey'?.1:.2;
 ctx.strokeStyle='#cb9adf';ctx.fillStyle='#e4c5ef';ctx.lineWidth=2;
 ctx.beginPath();ctx.moveTo(-65,62);ctx.lineTo(0,-100);ctx.lineTo(65,62);ctx.closePath();ctx.stroke();
 ctx.rotate(angle);ctx.beginPath();ctx.moveTo(0,55);ctx.lineTo(0,-115);ctx.stroke();
 ctx.fillRect(-9,-80,18,24);ctx.beginPath();ctx.arc(0,55,5,0,Math.PI*2);ctx.fill();
 ctx.restore();
 // Beat-stepped sundial. Each completed sweep shares the word-change clock.
 if(r.stopped)return;
 const span=Math.max(1,r.every), cycle=Math.floor(r.beats/span), step=r.beats%span;
 const progress=cycle%2?1-step/span:step/span;
 const theta=Math.PI+progress*Math.PI, radius=88;
 const pulse=reduced?0:Math.pow(1-s.beatPhase,3);
 ctx.save();ctx.translate(s.w/2,s.h*.65+157);
 ctx.globalAlpha=s.state==='grey'?.16:.38;
 ctx.lineWidth=2;ctx.strokeStyle='#b793d4';
 ctx.beginPath();ctx.arc(0,0,radius,Math.PI,Math.PI*2);ctx.stroke();
 for(let i=0;i<=span;i++){
  const a=Math.PI+i/span*Math.PI, co=Math.cos(a),si=Math.sin(a);
  ctx.beginPath();ctx.moveTo(co*(radius-5),si*(radius-5));ctx.lineTo(co*(radius+5),si*(radius+5));ctx.stroke();
 }
 ctx.strokeStyle='#edb9ee';ctx.lineWidth=4;
 ctx.beginPath();ctx.arc(0,0,radius,cycle%2?Math.PI*2:Math.PI,theta,!!(cycle%2));ctx.stroke();
 ctx.fillStyle='#eac5f4';ctx.beginPath();ctx.moveTo(0,0);
 ctx.lineTo(Math.cos(theta-.035)*radius,Math.sin(theta-.035)*radius);
 ctx.lineTo(Math.cos(theta+.035)*radius,Math.sin(theta+.035)*radius);ctx.closePath();ctx.fill();
 ctx.globalAlpha=s.state==='grey'?.2:.65;
 ctx.beginPath();ctx.arc(Math.cos(theta)*radius,Math.sin(theta)*radius,4+pulse*2,0,Math.PI*2);ctx.fill();
 ctx.beginPath();ctx.arc(0,0,4,0,Math.PI*2);ctx.fill();ctx.restore();
}

// Circle against the actual oriented face, including a stable normal when inside.
export function rotatedBrickContact(ball,brick) {
 const co=Math.cos(brick.angle||0),si=Math.sin(brick.angle||0);
 const dx=ball.x-brick.x-brick.w/2,dy=ball.y-brick.y-brick.h/2;
 const x=dx*co+dy*si,y=-dx*si+dy*co,hx=brick.w/2,hy=brick.h/2;
 const qx=Math.max(-hx,Math.min(hx,x)),qy=Math.max(-hy,Math.min(hy,y));
 let nx=x-qx,ny=y-qy,d=Math.hypot(nx,ny),depth=ball.r-d;
 if(depth<=0)return null;
 if(d>1e-8){nx/=d;ny/=d;} else if(hx-Math.abs(x)<hy-Math.abs(y)){nx=x<0?-1:1;ny=0;depth=ball.r+hx-Math.abs(x);} else {nx=0;ny=y<0?-1:1;depth=ball.r+hy-Math.abs(y);}
 return {nx:nx*co-ny*si,ny:nx*si+ny*co,depth};
}
