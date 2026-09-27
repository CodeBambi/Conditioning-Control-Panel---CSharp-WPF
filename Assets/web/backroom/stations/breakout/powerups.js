import {junctionProtected} from './junction-shield.js';
import {metalActive} from './grey-metal.js';
import {rotatedBrickContact} from './reform.js';
/** Seconds. The shooting paddle lasts 4 (was 8: owner, 2026-09-21, half the time). */
export const POWER_DURATION={multiball:12,fireball:8,laser:4,shield:20};
/** An ordinary brick's chance to hold a power-up. Was .085; 15 percent fewer OVERALL (owner, 2026-09-21): split bricks (game.js SPLIT_CHANCE, 1 percent) still always drop, so this one carries the whole cut. */
export const POWER_CHANCE=.07;
export const POWER_KINDS=Object.keys(POWER_DURATION);
/** The random drop's mix. Multiball also falls from every split brick, so it takes the small share here (owner, 2026-09-21: way too many). */
export const POWER_WEIGHT={multiball:1,fireball:3,laser:3,shield:3};
const WEIGHT_SUM=POWER_KINDS.reduce((n,k)=>n+POWER_WEIGHT[k],0);
/** One roll 0..1 to a kind, by weight. One rng call, as before, so the wall's dice stay where they were. */
export function pickPower(roll){let r=Math.max(0,Math.min(.999999,roll))*WEIGHT_SUM;for(const k of POWER_KINDS){r-=POWER_WEIGHT[k];if(r<0)return k;}return POWER_KINDS[POWER_KINDS.length-1];}
/** Seconds left when a timed power warns, the drop's fall (px/s, px/s2, cap) and its catch reach past the paddle edge. */
export const WARN_AT=2, DROP_V0=118, DROP_ACCEL=20, DROP_VMAX=190, DROP_REACH=14, MUZZLE_S=.11;
/** Eighth notes fire the laser; the shot leaves this much of an eighth early so its quantised pluck lands ON the eighth. */
export const LASER_LEAD=.16, FALLBACK_SPB=60/96;
/** Sim seconds a handed beat clock may stand still before the laser's grid runs on the sim clock instead (one beat at 96 bpm). */
export const BEAT_STALL_S=FALLBACK_SPB;
/** A drop's sway around the column it fell from. Eases in so it leaves the brick's centre; the renderer reuses it for the trail. */
export const swayX=(d,age=d.age)=>(d.x0??d.x)+Math.sin(age*2.4+(d.ph||0))*6*Math.min(1,Math.max(0,age)*2);
export function ordinaryTarget(br,s) {
  return br.alive&&!br.reformSafe&&!(br.irisAlpha<.15)&&!metalActive(br,s.state)&&
    !br.finaleMetal&&!br.finaleRing&&!br.finaleDefense&&!br.finaleCenterGuard&&!br.finaleGate&&
    !br.finaleWord&&!br.irisCore&&!br.pendulumAnchor&&!br.finaleHinge&&s.finale?.phase!=='approach'&&s.finale?.phase!=='locked';
}
/** The finale bricks a laser shot may damage: exactly the ones the BALL damages through breakBrick's own phase rules. In
 * 'released' every ring, defense row, centre guard and gate takes normal damage; a defense row also takes it during
 * 'approach' and 'locked'. Any OTHER brick touched during 'approach' would trigger the interrupt, which a laser must never
 * cause, so those stay out. Words, hinges, metal, iris cores and pendulum anchors stay excluded. Not for assign(). */
export function laserFinaleTarget(br,s) {
  const f=s.finale;if(!f||!br.alive||br.reformSafe)return false;
  if(f.phase==='released')return !!(br.finaleRing||br.finaleDefense||br.finaleCenterGuard||br.finaleGate);
  return !!br.finaleDefense&&(f.phase==='approach'||f.phase==='locked');
}
export function createPowerups(s,{rng,emit,newBall,damage,maxBalls=8,beatTime=null}) {
  s.power={drops:[],shots:[],multiball:0,fireball:0,laser:0,shield:0,charges:0,clock:0,eighth:null,muzzle:0,beatSeen:null,beatSeenAt:0};
  const p=s.power;
  /** The laser's grid position in eighths: the bed's beat clock when the game hands one over, else the sim clock at 96 bpm.
   * A handed clock that stops moving (an AudioContext left suspended or interrupted keeps its currentTime still while the
   * game plays on) must not stop the laser: after BEAT_STALL_S of sim time with no movement the sim clock carries the grid on
   * from where it stalled, and the handed clock takes over again the moment it moves. */
  function eighth(){
    let beats=NaN;try{if(typeof beatTime==='function')beats=Number(beatTime());}catch(e){/* sim clock */}
    if(Number.isFinite(beats)&&beats!==p.beatSeen){p.beatSeen=beats;p.beatSeenAt=p.clock;}
    if(p.beatSeen==null)beats=p.clock/FALLBACK_SPB;
    else if(p.clock-p.beatSeenAt>BEAT_STALL_S)beats=p.beatSeen+(p.clock-p.beatSeenAt)/FALLBACK_SPB;
    else beats=p.beatSeen;
    return Math.floor(beats*2+LASER_LEAD);
  }
  function retire() {
    const live=s.balls.filter(b=>!b.lost&&!b.falling);
    if(live.length&&!live.some(b=>!b.temporary)){live[0].temporary=false;live[0].tint=0;}   // the promoted copy IS the player's ball now: violet again
    s.balls=s.balls.filter(b=>{if(!b.temporary)return true;if(s.well?.captured===b){s.well.captured=null;s.well.used=false;}return false;});
  }
  function reset(){p.epoch=(p.epoch||0)+1;retire();p.drops.length=p.shots.length=0;for(const k of POWER_KINDS)p[k]=0;p.charges=0;p.muzzle=0;p.eighth=null;for(const b of s.balls)b.fireContacts?.clear();}
  function assign(br){
    if(br.split||br.gif>=0||br.word||br.spiral||br.jackpot||br.strength||!ordinaryTarget({...br,finaleRing:false},s))return;
    if(rng()<POWER_CHANCE)br.powerup=pickPower(rng());
  }
  function drop(br){
    if(s.state!=='colour')return;
    if(br.finaleWord||br.irisCore||br.pendulumAnchor||br.finaleHinge||br.finaleMetal)return;
    const kind=br.split?'multiball':br.powerup;
    if(!kind||p.drops.length>=12)return;
    const x=br.x+br.w/2,y=br.y+br.h/2;
    // The sway's phase comes from where the brick stood, never from rng: the wall's dice stay where they were.
    p.drops.push({kind,x,y,x0:x,age:0,vy:DROP_V0,ph:(x*.013+y*.029)%6.283});
    emit('powerDrop',{kind,x,y});
  }
  function activate(kind){
    if(s.state!=='colour')return;
    p[kind]=POWER_DURATION[kind];
    if(kind==='shield')p.charges=Math.min(2,p.charges+1);
    let split=null;
    if(kind==='multiball'){
      const source=s.balls.find(b=>!b.lost&&!b.falling&&!b.stuck)||s.balls.find(b=>!b.lost&&!b.falling);
      let made=0;split=source?{x:source.x,y:source.y}:null;
      if(source)for(const sign of [-1,1]){
        if(s.balls.length>=maxBalls)break;
        const speed=Math.max(220,Math.hypot(source.vx,source.vy)),a=sign*.5;
        // tint: each copy wears its own colour (feedback.js BALL_TINTS), so three balls read as three.
        s.balls.push({...newBall(s.state==='grey'),x:source.x,y:source.y,vx:Math.sin(a)*speed,vy:-Math.cos(a)*speed,stuck:false,temporary:true,tint:sign<0?1:2});made++;
      }
      if(!made)split=null;
    }
    emit('powerCatch',{kind,x:s.paddle.x,y:s.paddle.y});
    if(split)emit('multiSplit',split);                                   // after the catch, so the catch still speaks first
  }
  function step(dt){
    if(s.state!=='colour'){
      if(p.drops.length||p.shots.length||p.charges||POWER_KINDS.some(k=>p[k]>0)||s.balls.some(b=>b.temporary))reset();
      return;
    }
    const epoch=p.epoch;
    p.clock+=dt;p.muzzle=Math.max(0,p.muzzle-dt);
    // A power with nothing left to give ends quietly: the save already spoke, the extra balls are already gone.
    if(p.shield>0&&!p.charges)p.shield=0;
    if(p.multiball>0&&!s.balls.some(b=>b.temporary))p.multiball=0;
    for(const k of POWER_KINDS){
      const was=p[k];if(!(was>0))continue;
      p[k]=Math.max(0,was-dt);
      if(!p[k])emit('powerExpire',{kind:k});
      else if(was>WARN_AT&&p[k]<=WARN_AT)emit('powerWarn',{kind:k});   // once per catch: only the crossing speaks
    }
    if(!p.multiball&&s.balls.some(b=>b.temporary))retire();
    if(!p.shield)p.charges=0;
    if(!p.fireball)for(const b of s.balls)b.fireContacts?.clear();
    const top=s.paddle.y-s.paddle.h/2;
    p.drops=p.drops.filter(d=>{
      const old=d.y;d.vy=Math.min(DROP_VMAX,(d.vy??DROP_V0)+DROP_ACCEL*dt);d.y+=d.vy*dt;d.age+=dt;
      d.x=Math.min(s.w-14,Math.max(14,swayX(d)));
      // The reach grew with the sway (11 -> 14), so a drop that would have been caught on a straight fall still is.
      if(old-11<=top&&d.y+11>=top&&Math.abs(d.x-s.paddle.x)<=s.paddle.w/2+DROP_REACH){activate(d.kind);return false;}
      if(!d.missed&&d.y-11>top){d.missed=true;emit('powerMiss',{kind:d.kind,x:d.x});}
      return d.y<s.h+24&&d.age<10;
    });
    if(p.laser>0&&s.wallAge>=1.9&&!junctionProtected({...s,finale:null})&&s.balls.some(b=>!b.stuck&&!b.lost&&!b.falling)){
      // BEAT-LOCKED: one volley per eighth note of the bed. The first one waits for the next eighth after the catch.
      const e=eighth();
      if(p.eighth!=null&&e!==p.eighth){
        for(const sign of [-1,1])p.shots.push({x:s.paddle.x+sign*(s.paddle.w/2-10),y:top-5,r:3});
        p.muzzle=MUZZLE_S;emit('laserShot',{x:s.paddle.x,y:top-5});
      }
      p.eighth=e;
    } else p.eighth=null;
    p.shots=p.shots.filter(shot=>{
      if(epoch!==p.epoch||s.wallAge<1.9)return false;
      for(let i=0;i<Math.ceil(780*dt/3);i++){
        shot.y-=780*dt/Math.ceil(780*dt/3);
        for(const br of s.bricks){
          if(!br.alive||br.reformSafe||br.irisAlpha<.15)continue;
          if(!rotatedBrickContact(shot,br))continue;
          emit('laserHit',{x:shot.x,y:shot.y});
          if(ordinaryTarget(br,s)||laserFinaleTarget(br,s))damage(br,{...shot,vx:0,vy:-780});
          return false;
        }
      }
      return epoch===p.epoch&&shot.y>-12;
    });
    if(epoch!==p.epoch)p.shots.length=0;
  }
  return {step,drop,reset,assign};
}
