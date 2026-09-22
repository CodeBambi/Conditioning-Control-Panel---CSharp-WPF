import { test } from 'node:test';
import assert from 'node:assert/strict';
import { durabilityColour, cometSegments, paddleMood, squashScale, pushInZoom, PUSH_IN_S, perfectLabel, perfectSize, paddleLean, relativeDrag, cometLength, COMET_BONUS, COMET_MAX, wordTrailLength, wordTrailPoints, WORD_TRAIL, jellyScale, bubbleIdle, BALL_TINTS } from './feedback.js';
import { createParticles } from './particles.js';

const luminance = rgb => rgb.map(v => {v /= 255; return v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4;}).reduce((n,v,i)=>n+v*[.2126,.7152,.0722][i],0);
test('damage gets brighter across theme hues and dull mode', () => {
  for (const grey of [false,true]) for (let hue=0; hue<360; hue+=15) {
    const colours=[3,2,1].map(hp=>durabilityColour(hp,grey,hue));
    assert(luminance(colours[0])<luminance(colours[1]));
    assert(luminance(colours[1])<luminance(colours[2]));
    if(grey) colours.forEach(c=>assert.equal(new Set(c).size,1));
  }
});
test('comet follows bounce history, grows with speed, and stops at teleports',()=>{
  const trail=[];for(let x=0;x<=300;x+=5)trail.push(x,0);
  const ball={x:300,y:0,vx:200,vy:0,trail};
  const length=b=>cometSegments(b).reduce((n,s)=>n+Math.hypot(s.nx-s.x,s.ny-s.y),0);
  assert(length({...ball,vx:260})>length(ball));
  assert.equal(length({...ball,stuck:true}),0);
  assert.equal(length({...ball,x:900}),0);
  const bounce=cometSegments({...ball,x:295,y:5,trail:[280,0,290,0,300,0]});
  assert.equal(bounce[0].nx,300);assert.equal(bounce[1].nx,290);
});
test('recent return smiles; only all distant balls make the paddle sad',()=>{
  const p={x:0,y:600},far={x:0,y:0},near={x:0,y:550};
  assert.equal(paddleMood([far],p,.5),'happy');
  assert.equal(paddleMood([far],p,0),'sad');
  assert.equal(paddleMood([far,near],p,0),'neutral');
  assert.equal(paddleMood([{...far,stuck:true}],p,0),'neutral');
});
test('smoke is bounded and expires',()=>{
  const p=createParticles({max:12,rng:()=>.5});
  p.smoke(0,0,[180,180,180],40);assert.equal(p.count(),12);
  p.step({},1,0,()=>{});assert.equal(p.count(),0);
});

test('squash flattens at the bounce, overshoots once and settles round', () => {
  const hit = squashScale(1), spring = squashScale(.3), rest = squashScale(0);
  assert.ok(hit.along < .65 && hit.across > 1.2, 'flat against the surface');
  assert.ok(spring.along > 1 && spring.across < 1, 'one springy overshoot');
  assert.ok(Math.abs(rest.along - 1) < 1e-9 && Math.abs(rest.across - 1) < 1e-9);
});
test('the push-in peaks near 1.06 and is back to 1 when it ends', () => {
  assert.equal(pushInZoom(0), 1); assert.ok(Math.abs(pushInZoom(.2) - 1.06) < 1e-9);
  assert.ok(pushInZoom(.7) > 1 && pushInZoom(.7) < 1.02); assert.equal(pushInZoom(PUSH_IN_S), 1); assert.equal(pushInZoom(NaN), 1);
});
test('perfect text counts the streak and its size is capped; the lean is bounded', () => {
  assert.deepEqual([1, 2, 3].map(perfectLabel), ['PERFECT', 'PERFECT x2', 'PERFECT x3']);
  assert.equal(perfectLabel(undefined), 'PERFECT'); assert.equal(perfectSize(1), 20); assert.equal(perfectSize(40), 35);
  assert.equal(paddleLean(0), 0); assert.equal(paddleLean(9e9), .2); assert.equal(paddleLean(-9e9), -.2);
});
test('touch drag is relative, a little faster than the finger, and re-anchors at a wall', () => {
  const drag = { sx: 900, px: 300 };
  assert.equal(relativeDrag(drag, 900, 85, 1280), 300, 'touching down moves nothing');
  assert.ok(Math.abs(relativeDrag(drag, 1000, 85, 1280) - 415) < 1e-9, '100 px of finger is 115 px of paddle');
  assert.equal(relativeDrag(drag, 100, 85, 1280), 85, 'clamped at the wall');
  assert.ok(relativeDrag(drag, 110, 85, 1280) > 85, 'and the reversal answers at once');
});
test('a fast ball earns up to a quarter more comet, and no more',()=>{
  assert.equal(wordTrailLength(300),300*.22);
  assert.ok(Math.abs(wordTrailLength(700)-700*.22*(1+COMET_BONUS))<1e-9);
  assert.ok(Math.abs(wordTrailLength(500)-500*.22*1.125)<1e-9,'the bonus ramps between');
  assert.equal(wordTrailLength(5000),225);assert.equal(wordTrailLength(-3),0);
  for(let v=0;v<1200;v+=20)assert.ok(wordTrailLength(v+20)>=wordTrailLength(v),'never shrinks with speed');
  // The streak itself is short: about what the slowest ball (220 px/s) used to draw, whatever the speed.
  assert.equal(cometLength(220),220*.22);assert.equal(cometLength(700),COMET_MAX);assert.ok(COMET_MAX<=64);
  for(let v=0;v<1200;v+=20)assert.ok(cometLength(v)<=wordTrailLength(v));
});
test('bubble jelly rests at round, squashes along the hit and rings out; the idle stays slight',()=>{
  assert.deepEqual(jellyScale(0),{along:1,across:1});
  const hit=jellyScale(1);assert.ok(hit.along<.85&&hit.across>1.1);
  let swings=0,prev=hit.along-1;for(let t=1;t>=0;t-=.01){const k=jellyScale(t).along-1;if(k*prev<0)swings++;if(k)prev=k;assert.ok(Math.abs(k)<=.2001);}
  assert.ok(swings>=3,'it wobbles, not just relaxes');
  for(let a=0;a<30;a+=.37){const i=bubbleIdle(a,1.3);assert.ok(Math.abs(i.breath-1)<=.03&&Math.abs(i.sx-1)<=.035&&Math.abs(i.sx+i.sy-2)<1e-9);}
  assert.equal(BALL_TINTS[0],null);assert.notDeepEqual(BALL_TINTS[1],BALL_TINTS[2]);
});

test('the words trail further than the streak, evenly spaced along the path, fading out',()=>{
  const trail=[];for(let x=0;x<=600;x+=5)trail.push(x,0);
  const ball={x:600,y:0,vx:600,vy:0,trail},pts=wordTrailPoints(ball);
  assert.ok(pts.length>=4,'several words on a fast ball');
  assert.ok(600-pts[pts.length-1].x>COMET_MAX,'they outrun the streak');
  assert.equal(600-pts[0].x,WORD_TRAIL.first);
  for(let i=1;i<pts.length;i++){assert.ok(Math.abs((pts[i-1].x-pts[i].x)-WORD_TRAIL.gap)<1e-9);assert.ok(pts[i].alpha<pts[i-1].alpha);}
  assert.deepEqual(wordTrailPoints({...ball,stuck:true}),[]);
  assert.ok(WORD_TRAIL.size>7&&WORD_TRAIL.alpha>.5,'bigger and clearer than the old 7px at half alpha');
  assert.ok(WORD_TRAIL.gap>=20&&WORD_TRAIL.gap<=28,'dense, but a 10px word still has room');
});
