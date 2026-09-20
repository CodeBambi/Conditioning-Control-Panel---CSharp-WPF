import {junctionProtected} from './junction-shield.js';
import {metalActive} from './grey-metal.js';
import {rotatedBrickContact} from './reform.js';
export const POWER_DURATION={multiball:12,fireball:8,laser:8,shield:20};
export const POWER_KINDS=Object.keys(POWER_DURATION);
export function ordinaryTarget(br,s) {
  return br.alive&&!br.reformSafe&&!(br.irisAlpha<.15)&&!metalActive(br,s.state)&&
    !br.finaleMetal&&!br.finaleRing&&!br.finaleDefense&&!br.finaleCenterGuard&&!br.finaleGate&&
    !br.finaleWord&&!br.irisCore&&!br.pendulumAnchor&&!br.finaleHinge&&s.finale?.phase!=='approach'&&s.finale?.phase!=='locked';
}
export function createPowerups(s,{rng,emit,newBall,damage,maxBalls=8}) {
  s.power={drops:[],shots:[],multiball:0,fireball:0,laser:0,shield:0,charges:0,shotClock:0};
  const p=s.power;
  function retire() {
    const live=s.balls.filter(b=>!b.lost&&!b.falling);
    if(live.length&&!live.some(b=>!b.temporary))live[0].temporary=false;
    s.balls=s.balls.filter(b=>{if(!b.temporary)return true;if(s.well?.captured===b){s.well.captured=null;s.well.used=false;}return false;});
  }
  function reset(){p.epoch=(p.epoch||0)+1;retire();p.drops.length=p.shots.length=0;for(const k of POWER_KINDS)p[k]=0;p.charges=0;p.shotClock=0;for(const b of s.balls)b.fireContacts?.clear();}
  function assign(br){
    if(br.split||br.gif>=0||br.word||br.spiral||br.jackpot||br.strength||!ordinaryTarget({...br,finaleRing:false},s))return;
    if(rng()<.085)br.powerup=POWER_KINDS[Math.floor(rng()*POWER_KINDS.length)];
  }
  function drop(br){
    if(s.state!=='colour')return;
    if(br.finaleWord||br.irisCore||br.pendulumAnchor||br.finaleHinge||br.finaleMetal)return;
    const kind=br.split?'multiball':br.powerup;
    if(kind&&p.drops.length<12)p.drops.push({kind,x:br.x+br.w/2,y:br.y+br.h/2,age:0});
  }
  function activate(kind){
    if(s.state!=='colour')return;
    p[kind]=POWER_DURATION[kind];
    if(kind==='shield')p.charges=Math.min(2,p.charges+1);
    if(kind==='multiball'){
      const source=s.balls.find(b=>!b.lost&&!b.falling&&!b.stuck)||s.balls.find(b=>!b.lost&&!b.falling);
      if(source)for(const sign of [-1,1]){
        if(s.balls.length>=maxBalls)break;
        const speed=Math.max(220,Math.hypot(source.vx,source.vy)),a=sign*.5;
        s.balls.push({...newBall(s.state==='grey'),x:source.x,y:source.y,vx:Math.sin(a)*speed,vy:-Math.cos(a)*speed,stuck:false,temporary:true});
      }
    }
    emit('powerCatch',{kind,x:s.paddle.x,y:s.paddle.y});
  }
  function step(dt){
    if(s.state!=='colour'){
      if(p.drops.length||p.shots.length||p.charges||POWER_KINDS.some(k=>p[k]>0)||s.balls.some(b=>b.temporary))reset();
      return;
    }
    const epoch=p.epoch;
    for(const k of POWER_KINDS)p[k]=Math.max(0,p[k]-dt);
    if(!p.multiball&&s.balls.some(b=>b.temporary))retire();
    if(!p.shield)p.charges=0;
    if(!p.fireball)for(const b of s.balls)b.fireContacts?.clear();
    const top=s.paddle.y-s.paddle.h/2;
    p.drops=p.drops.filter(d=>{
      const old=d.y;d.y+=145*dt;d.age+=dt;
      if(old-11<=top&&d.y+11>=top&&Math.abs(d.x-s.paddle.x)<=s.paddle.w/2+11){activate(d.kind);return false;}
      return d.y<s.h+24&&d.age<10;
    });
    if(p.laser>0&&s.wallAge>=1.9&&!junctionProtected({...s,finale:null})&&s.balls.some(b=>!b.stuck&&!b.lost&&!b.falling)){
      p.shotClock-=dt;
      if(p.shotClock<=0){p.shotClock=.32;for(const sign of [-1,1])p.shots.push({x:s.paddle.x+sign*(s.paddle.w/2-10),y:top-5,r:3});}
    }
    p.shots=p.shots.filter(shot=>{
      if(epoch!==p.epoch||s.wallAge<1.9)return false;
      for(let i=0;i<Math.ceil(780*dt/3);i++){
        shot.y-=780*dt/Math.ceil(780*dt/3);
        for(const br of s.bricks){
          if(!br.alive||br.reformSafe||br.irisAlpha<.15)continue;
          if(!rotatedBrickContact(shot,br))continue;
          if(ordinaryTarget(br,s))damage(br,{...shot,vx:0,vy:-780});
          return false;
        }
      }
      return epoch===p.epoch&&shot.y>-12;
    });
    if(epoch!==p.epoch)p.shots.length=0;
  }
  return {step,drop,reset,assign};
}
