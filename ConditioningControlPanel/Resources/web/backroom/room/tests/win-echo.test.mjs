import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createWinEcho, envelope, ECHO} from '../win-echo.js';
import {PARTY} from '../../shared/win/plan.js';

const win=(over={})=>({key:'wheel',amount:40,tier:3,text:'WIN +40',name:'Daily Daze',...over});

test('nothing was won, so the floor is told nothing',()=>{
 const e=createWinEcho();
 for(const amount of [0,-1,NaN,Infinity,'x',null,undefined])assert.equal(e.celebrate(win({amount}),{},0),null);
 assert.equal(e.celebrate(null,{},0),null);
 assert.equal(e.debug().raised,0);
});

test('Law IX: a small win never crosses the room, a good one does',()=>{
 const e=createWinEcho();
 assert.equal(e.celebrate(win({tier:1,amount:2}),{},0),null);      // a chime at the cabinet and no more
 assert.equal(e.celebrate(win({tier:0,amount:2}),{},0),null);
 assert.equal(e.line('wheel',10),null);
 const good=e.celebrate(win({tier:2}),{},0);
 assert.equal(good.shower,PARTY.SHOWER[2]);
 assert.ok(good.aura>0);
 assert.equal(good.marquee,null);                                   // only a hero leaves the fixture
 assert.equal(e.debug().hushed,2);
});

test('the walk-back: the aura rises, holds and fades, and the line outlives the coins',()=>{
 const e=createWinEcho();
 const fired=e.celebrate(win({tier:3}),{},1000);
 assert.equal(fired.ms,PARTY.MS[3]+ECHO.TAIL_MS);
 assert.equal(e.gain('wheel',1000,false),0);                        // in fast, from nothing
 assert.ok(e.gain('wheel',1000+ECHO.RISE_MS,false)>e.gain('wheel',1050,false));
 assert.equal(e.gain('wheel',2000,false),ECHO.GAIN[3]);             // full through the hold
 assert.ok(e.gain('wheel',1000+fired.ms-200,false)<ECHO.GAIN[3]);   // out slow
 assert.equal(e.gain('wheel',1000+fired.ms,false),0);
 assert.equal(e.line('wheel',1000+fired.ms-1),'WIN +40');
 assert.equal(e.line('wheel',1000+fired.ms),null);
 assert.equal(e.gain('roulette',2000,false),0);                     // one fixture won, one fixture shows
 assert.deepEqual([...e.gains(2000,false)],[['wheel',ECHO.GAIN[3]]]);
});

test('Brake 9: Calm and reduced motion keep the news and drop the decoration',()=>{
 for(const quiet of [{still:true},{reduced:true}]) {
  const e=createWinEcho();
  const fired=e.celebrate(win({tier:4,amount:400}),quiet,0);
  assert.equal(fired.shower,0);                                     // no coins
  assert.equal(fired.aura,0);                                       // no aura
  assert.equal(fired.marquee,null);                                 // no reveal, so no board
  assert.equal(fired.line,'WIN +40');                               // the text always
  assert.equal(e.line('wheel',100),'WIN +40');
  assert.equal(e.gain('wheel',100,false),0);
 }
 // Calm arriving mid-echo takes the aura on the next frame and leaves the line alone.
 const e=createWinEcho();
 e.celebrate(win({tier:3}),{},0);
 assert.ok(e.gain('wheel',900,false)>0);
 assert.equal(e.gain('wheel',900,true),0);
 assert.equal(e.gains(900,true).size,0);
 assert.equal(e.line('wheel',900),'WIN +40');
});

test('the hero takes the board, once a visit, and signs it',()=>{
 const e=createWinEcho();
 const hero=e.celebrate(win({key:'slot:rose',tier:4,amount:400,text:'WIN +400',name:'Candy Rose'}),{},0);
 assert.equal(hero.plan.reveal,true);
 assert.equal(hero.marquee,'Candy Rose'+ECHO.JOIN+'WIN +400');
 assert.equal(e.marquee(100),hero.marquee);
 assert.equal(e.marquee(hero.ms),null);                             // and then the Parlour has its name back
 // Law IX: the second jackpot of a visit is a very good tier 3. It still showers; it does not take the board.
 const second=e.celebrate(win({key:'slot:mint',tier:4,amount:400,text:'WIN +400',name:'Candy Mint'}),{},20000);
 assert.equal(second.plan.spent,3);
 assert.equal(second.marquee,null);
 assert.equal(e.marquee(20100),null);
 assert.ok(second.shower>0);
});

test('Brake 3: the floor wears out, over the whole visit and not one sit-down',()=>{
 const e=createWinEcho();
 const spent=[];
 for(let i=0;i<5;i++)spent.push(e.celebrate(win({tier:3}),{},i*20000).plan.spent);
 assert.deepEqual(spent,[3,3,3,2,2]);
 for(let i=5;i<40;i++)e.celebrate(win({tier:3}),{},i*20000);
 const worn=e.celebrate(win({tier:3}),{},900000);
 assert.equal(worn.plan.spent,1);                                   // a thud and the tokens
 assert.equal(worn.shower,0);                                       // and the floor stops turning its head
 assert.equal(worn.aura,0);
 assert.equal(worn.line,'WIN +40');                                 // the news is never worn away (Brake 9)
});

test('Brake 2: two pays on one fixture in one beat are one echo, at the higher rung',()=>{
 const e=createWinEcho();
 const first=e.celebrate(win({tier:2}),{},0);
 const second=e.celebrate(win({tier:3}),{},120);
 assert.equal(second.plan.spent,3);
 assert.equal(e.gains(2000,false).get('wheel'),ECHO.GAIN[3]);
 // ...and the smaller one does not shrink the bigger one that is still running.
 const third=e.celebrate(win({tier:2}),{},240);
 assert.equal(third.plan.spent,3);
 // A pay arriving after the echo is over is its own echo again.
 const later=e.celebrate(win({tier:2}),{},60000);
 assert.equal(later.plan.spent,2);
});

test('the line is a label, and clearing settles the floor at once (Law VI)',()=>{
 const e=createWinEcho();
 e.celebrate(win({tier:3,text:'  WIN\n+40   '+'x'.repeat(80)}),{},0);
 const line=e.line('wheel',10);
 assert.equal(line.length,ECHO.LINE_MAX);
 assert.equal(line.slice(0,9),'WIN +40 x');   // the newline is a space, the ends are trimmed
 e.clear('wheel');
 assert.equal(e.line('wheel',10),null);
 assert.equal(e.gain('wheel',10,false),0);
 e.celebrate(win({key:'slot:rose',tier:4,amount:400}),{},0);
 e.clear();
 assert.equal(e.marquee(10),null);
 assert.equal(e.gains(10,false).size,0);
});

test('the envelope never strobes and never leaves the rails',()=>{
 assert.equal(envelope(-1,1000),0);
 assert.equal(envelope(0,1000),0);
 assert.equal(envelope(1000,1000),0);
 assert.equal(envelope(500,0),0);
 assert.equal(envelope(NaN,1000),0);
 for(let t=0;t<=3800;t+=17)assert.ok(envelope(t,3800)>=0&&envelope(t,3800)<=1);
 // One rise and one fall: it crosses half exactly twice, which is what "no strobe" means here.
 let crossings=0,was=0;
 for(let t=0;t<=3800;t+=1){const v=envelope(t,3800);if((was<.5)!==(v<.5))crossings++;was=v;}
 assert.equal(crossings,2);
});
