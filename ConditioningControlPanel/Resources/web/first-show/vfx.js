const TAU = Math.PI * 2;
const colors = ['#fff4fc', '#ff8ccd', '#dc8aff', '#91e7ff', '#ffd7a0'];
export function createShowVfx(canvas, getMotion) {
 const ctx = canvas.getContext('2d');
 let w = 0, h = 0, particles = [], rings = [], trails = [], tick = 0;
 const stars = Array.from({length:70}, (_,i) => ({a:i*2.399963,r:.12+((i*37)%89)/90,s:1+(i%4),p:i*.73}));
 function resize(width,height) { w=width; h=height; canvas.width=w; canvas.height=h; }
 function reset() { particles=[]; rings=[]; trails=[]; ctx.clearRect(0,0,w,h); tick=0; }
 function burst(x,y,count=80,power=1) {
  const m=getMotion(); if(!m) return;
  for(let i=0;i<count*(m<1?.55:1)&&particles.length<950;i++) {
   const a=Math.random()*TAU, speed=(70+Math.random()*280)*power*(m<1?.6:1);
   particles.push({x,y,px:x,py:y,vx:Math.cos(a)*speed,vy:Math.sin(a)*speed,age:0,life:.55+Math.random()*1.1,size:1.7+Math.random()*4.4,color:colors[i%colors.length],kind:i%5,spin:Math.random()*TAU});
  }
 }
 function ring(x,y,power=1,color='#ffb3e5') { if(getMotion()) rings.push({x,y,age:0,life:.72,r:20,max:125*power,color}); }
 function trail(x,y,color='#ffb3e5',size=3) {
  if(!getMotion()||particles.length>850) return;
  particles.push({x,y,px:x,py:y,vx:(Math.random()-.5)*30,vy:15+Math.random()*20,age:0,life:.35+Math.random()*.45,size,color,kind:0,spin:0});
 }
 function comet(fromX,fromY,toX,toY) { if(getMotion()) trails.push({x:fromX,y:fromY,tx:toX,ty:toY,age:0,life:.42}); }
 function paint(dt,time,phase) {
  tick+=dt; ctx.clearRect(0,0,w,h); if(!getMotion()) return;
  const active=phase==='playing', m=getMotion(), centerX=w/2, centerY=h*.44;
  ctx.globalCompositeOperation='lighter';
  if(active) {
   const gathering=time>=28&&time<32;
   for(const star of stars) {
    const a=star.a+time*.06*m,r=((star.r+(gathering?-(time-28)*.38*m:time*.014))%1+1)%1;
    const x=centerX+Math.cos(a)*r*w*.67,y=centerY+Math.sin(a)*r*h*.7;
    const glow=.15+.45*(.5+.5*Math.sin(time*1.9+star.p));
    ctx.globalAlpha=glow;ctx.fillStyle=colors[star.s%5];
    if(gathering) {ctx.strokeStyle=colors[star.s%5];ctx.lineWidth=star.s*.7;ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(centerX+(x-centerX)*(1.09+r*.08),centerY+(y-centerY)*(1.09+r*.08));ctx.stroke();}
    else {ctx.fillRect(x,y,star.s,star.s);if(star.s===4){ctx.fillRect(x-3,y+1,10,1);ctx.fillRect(x+1,y-3,1,10);}}
   }
   if(time>=7&&time<32) {
    const grow=Math.min(1,(time-7)*2),gather=time>=28,rad=Math.min(w,h)*(gather?.21:.3);
    for(let i=0;i<3;i++) {
     ctx.strokeStyle=colors[(i+1)%5];ctx.lineWidth=i===1?2:1;ctx.globalAlpha=grow*(gather?.7:.2);
     ctx.beginPath();ctx.ellipse(centerX,centerY,rad*(1+i*.25),rad*(.42+i*.12),time*.2*m+i*.6,-time*.5+i*2,-time*.5+i*2+Math.PI*1.35);ctx.stroke();
    }
   }
  }
  for(const t of trails) {
   t.age+=dt;const p=Math.min(1,t.age/t.life),q=1-(1-p)**3,prev=Math.max(0,q-.25);
   ctx.globalAlpha=(1-p)*.9;ctx.strokeStyle='#ffe3fa';ctx.lineWidth=3*(1-p)+1;
   ctx.beginPath();ctx.moveTo(t.x+(t.tx-t.x)*prev,t.y+(t.ty-t.y)*prev);ctx.lineTo(t.x+(t.tx-t.x)*q,t.y+(t.ty-t.y)*q);ctx.stroke();
  }
  trails=trails.filter(t=>t.age<t.life);
  for(const ring of rings) {
   ring.age+=dt;const p=ring.age/ring.life;ctx.globalAlpha=Math.max(0,(1-p)*.85);ctx.strokeStyle=ring.color;ctx.lineWidth=(1-p)*4+1;
   ctx.beginPath();ctx.arc(ring.x,ring.y,ring.r+(ring.max-ring.r)*(1-(1-p)**3),0,TAU);ctx.stroke();
  }
  rings=rings.filter(r=>r.age<r.life);
  for(const p of particles) {
   p.age+=dt;p.px=p.x;p.py=p.y;p.x+=p.vx*dt;p.y+=p.vy*dt;p.vx*=Math.pow(.32,dt);p.vy+=24*dt;
   const fade=Math.max(0,1-p.age/p.life);ctx.globalAlpha=fade;ctx.fillStyle=p.color;ctx.strokeStyle=p.color;
   if(p.kind===1) {ctx.lineWidth=p.size*.6;ctx.beginPath();ctx.moveTo(p.x,p.y);ctx.lineTo(p.x-p.vx*.045,p.y-p.vy*.045);ctx.stroke();}
   else if(p.kind===2) {const s=p.size*fade;ctx.beginPath();ctx.moveTo(p.x-s*2,p.y);ctx.lineTo(p.x,p.y-s*2);ctx.lineTo(p.x+s*2,p.y);ctx.lineTo(p.x,p.y+s*2);ctx.closePath();ctx.fill();}
   else if(p.kind===3) {ctx.save();ctx.translate(p.x,p.y);ctx.rotate(p.spin+p.age*4);ctx.fillRect(-p.size/2,-p.size/2,p.size,p.size*2);ctx.restore();}
   else {ctx.beginPath();ctx.arc(p.x,p.y,p.size*fade,0,TAU);ctx.fill();}
  }
  particles=particles.filter(p=>p.age<p.life);ctx.globalAlpha=1;ctx.globalCompositeOperation='source-over';
 }
 return {resize,reset,burst,ring,trail,comet,paint,get count(){return particles.length}};
}
