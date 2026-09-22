import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createBeat,hitSemis,hitCutoff} from './audio.js';
import {pentatonic,ROOT_HZ} from '../../shared/sound/kit.js';
import POWER,{CHORDS,laserSemis} from './cues/power.js';
import {CUES} from './cues.js';
import REACT,{KIND_RGB} from './reactions/power.js';
import {drawPowerups} from './powerups-render.js';
import {POWER_KINDS} from './powerups.js';

const KEY=[0,2,4,7,9];
/** Distance in semitones from the nearest note of the key (any octave). */
function offKey(hz){const st=12*Math.log2(hz/ROOT_HZ),pc=((st%12)+12)%12;return Math.min(...KEY.concat(12).map(k=>Math.abs(pc-k)));}
function fakeSynth(now=10){
  const played=[],beat=createBeat(96,0),bus={bed:{},sfx:{name:'sfx'},sub:{},word:{}};
  const synth={ctx:{},now,played,beat,bus,room:{},saturation:.6,state:'colour',SEMI:s=>2**(s/12),ROOT_HZ,pentatonic,hitSemis,hitCutoff,
    tone:(hz,dur,level,o={})=>({k:'tone',hz,dur,level,...o}),noise:(hz,dur,level,o={})=>({k:'noise',hz,dur,level,...o}),
    play:(notes,t,dest)=>played.push({notes,t,dest}),glide(){},duck(){},quantise:lead=>beat.quantise(now,lead)};
  return synth;
}
const CASES=[['powerDrop',{kind:'laser',x:300,xN:.2}],['powerMiss',{kind:'shield',xN:.9}],['powerWarn',{kind:'laser'}],['powerExpire',{kind:'fireball'}],
  ['laserShot',{x:640,xN:.5}],['laserHit',{x:100,xN:.1}],...POWER_KINDS.map(kind=>['powerCatch',{kind,xN:.5}])];

test('every power cue is registered, runs against a fake synth, stays quiet and short, and is pitched in key',()=>{
  assert.deepEqual(Object.keys(POWER).sort(),['laserHit','laserShot','powerCatch','powerDrop','powerExpire','powerMiss','powerWarn']);
  for(const [name,data] of CASES){
    assert.equal(CUES[name],POWER[name]);
    const synth=fakeSynth();assert.notEqual(POWER[name](synth,data),false,name);
    assert.equal(synth.played.length,1,name);const {notes,t,dest}=synth.played[0];
    assert.equal(dest,synth.bus.sfx);assert.ok(notes.length>=1&&notes.length<=6,name);
    assert.equal(t,name==='laserHit'?synth.now:synth.quantise(),name+' clock');       // a body is now, a note is on the grid
    for(const n of notes){
      assert.ok(n.level>0&&n.level<=.09&&n.dur>0&&n.dur<=.45&&(n.at||0)<=.4,name+' level/dur');
      if(n.pan!=null)assert.ok(n.pan>=0&&n.pan<=1);
      if(n.k==='tone')for(const hz of [n.hz,n.hzTo].filter(Boolean))assert.ok(offKey(hz)<.1,`${name} ${hz.toFixed(1)} Hz is off key`);
    }
  }
  assert.equal(POWER.powerCatch(fakeSynth(),{kind:'nope'}),false);                     // unknown kind keeps the old fallback
  assert.doesNotThrow(()=>POWER.laserShot(fakeSynth(),{}));
});
test('each catch has its own motif',()=>{
  const sig=POWER_KINDS.map(kind=>{const s=fakeSynth();POWER.powerCatch(s,{kind});return JSON.stringify(s.played[0].notes.map(n=>[n.k,Math.round(n.hz),n.at||0]));});
  assert.equal(new Set(sig).size,4);
});
test('the laser plucks a tone of the chord under the bar it lands in, beside the arp, and follows the bed round the loop',()=>{
  const src=readFileSync(new URL('./audio.js',import.meta.url),'utf8').match(/const CHORDS = (\[\[[^;]*\]\]);/);
  assert.deepEqual(JSON.parse(src[1]),CHORDS,'cues/power.js CHORDS drifted from audio.js');
  const heard=new Set();
  for(let bar=-4;bar<8;bar++)for(let eighth=0;eighth<8;eighth++){
    const synth=fakeSynth(bar*2.5+eighth*.3125-.04);POWER.laserShot(synth,{xN:.5});   // fired a hair early, as powerups.js does
    const {notes,t}=synth.played[0],chord=CHORDS[((bar%4)+4)%4];
    assert.ok(Math.abs(t-(bar*2.5+eighth*.3125))<1e-6,'lands on the eighth');
    const semis=Math.round(12*Math.log2(notes[0].hz/ROOT_HZ));assert.ok(chord.includes(semis-12),`bar ${bar}: ${semis}`);
    assert.equal(semis,laserSemis(synth,t));assert.notEqual(semis-12,chord[((((bar%4)+4)%4)*8+eighth)%3],'never doubles the arp');heard.add(semis);
  }
  assert.ok(heard.size>=5);
});
test('reactions honour grey and reduced motion, and wear the pickup colours',()=>{
  function fx(over){const log=[];const rec=n=>(...a)=>log.push([n,...a]);
    return {log,P:{burst:rec('burst'),spray:rec('spray'),rects:rec('rects')},shockwaves:{push:rec('shock')},stamps:{push:rec('stamp')},cam:{kick:rec('kick')},reduced:false,colour:true,rungs:()=>true,
      colours:{WHITE:[255,255,255]},flash:rec('flash'),aberr:rec('aberr'),...over};}
  const snap={paddle:{x:640,y:680,w:160}};
  for(const name of Object.keys(REACT))for(const kind of POWER_KINDS){
    const grey=fx({colour:false});REACT[name](grey,{kind,x:5,y:6},snap);assert.equal(grey.log.length,0,name+' in grey');
    const calm=fx({reduced:true});REACT[name](calm,{kind,x:5,y:6},snap);assert.ok(calm.log.every(e=>e[0]==='stamp'),name+' reduced');
    assert.doesNotThrow(()=>REACT[name](fx(),{kind:'nope'},null));
  }
  const f=fx();REACT.powerCatch(f,{kind:'shield',x:5,y:6},snap);
  assert.deepEqual(f.log.find(e=>e[0]==='burst')[3],KIND_RGB.shield);assert.ok(f.log.some(e=>e[0]==='kick')&&f.log.some(e=>e[0]==='flash'));
  assert.ok(f.log.find(e=>e[0]==='kick')[1]<=4&&f.log.find(e=>e[0]==='flash')[1]<=.15);
});
test('the power layer draws every state without throwing, draws nothing in grey, and its fire has a ceiling',()=>{
  let calls=0;const g=new Proxy({},{get:(_,k)=>k==='calls'?calls:()=>{calls++;return {addColorStop(){}};},set:()=>true});
  const trail=[];for(let i=0;i<40;i++)trail.push(100+i,300-i);
  const s={w:1280,h:720,state:'colour',time:3.2,paddle:{x:10,y:680,w:160,h:12},balls:Array.from({length:8},(_,i)=>({x:100+i,y:300,r:7,trail})).concat({x:1,y:1,r:7,lost:true}),
    power:{drops:[{kind:'laser',x:50,y:200,age:1,vy:140,x0:50,ph:1},{kind:'shield',x:60,y:20}],shots:[{x:5,y:400},{x:9,y:300}],multiball:1.5,fireball:8,laser:.5,shield:20,charges:2,muzzle:.1}};
  drawPowerups(g,s);const full=calls;assert.ok(full>50&&full<2600,String(full));
  calls=0;drawPowerups(g,{...s,reduced:true});assert.ok(calls>20&&calls<full);
  calls=0;drawPowerups(g,{...s,state:'grey'});drawPowerups(g,{...s,power:null});assert.equal(calls,0);
  calls=0;drawPowerups(g,{...s,balls:[{x:1,y:1}],power:{...s.power,drops:[],shots:[]}});assert.ok(calls>0);
});
test('the split, the shield save and a burning brick each get their own reaction, in their own colour',()=>{
  function fx(over){const log=[];const rec=n=>(...a)=>log.push([n,...a]);
    return {log,P:{burst:rec('burst'),spray:rec('spray'),rects:rec('rects')},shockwaves:{push:rec('shock')},stamps:{push:rec('stamp')},cam:{kick:rec('kick')},reduced:false,colour:true,rungs:()=>true,
      colours:{WHITE:[255,255,255]},flash:rec('flash'),aberr:rec('aberr'),...over};}
  const split=fx();REACT.multiSplit(split,{x:300,y:200,n:2});
  assert.ok(!split.log.some(e=>e[0]==='shock')&&split.log.some(e=>e[0]==='kick'),'a little pop, not the breakout shockwave');
  assert.ok(split.log.filter(e=>e[0]==='burst').length>=3,'the pop carries the ball colour and both copy colours');
  assert.ok(split.log.find(e=>e[0]==='kick')[1]<=5);
  const save=fx();REACT.powerSave(save,{x:300,y:700});assert.deepEqual(save.log.find(e=>e[0]==='spray')[5],KIND_RGB.shield);
  const cold=fx();REACT.brick(cold,{x:1,y:1},{power:{fireball:0}});assert.equal(cold.log.length,0,'no embers without the fireball');
  const hot=fx();REACT.brick(hot,{x:1,y:1},{power:{fireball:3}});assert.deepEqual(hot.log[0][3],KIND_RGB.fireball);
});
