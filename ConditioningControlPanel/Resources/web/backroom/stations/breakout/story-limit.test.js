import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
const advance=(game,n)=>{for(let i=0;i<n;i++)game.step(1/120);};
test('Story limits clamp to one through eight; normal Story still has its eighth-level finale',()=>{
 for(const [value,expected] of [[0,1],[-8,1],[3.9,3],[20,8],[NaN,8],[undefined,8]])assert.equal(createGame({storyLimit:value}).snapshot().storyLimit,expected);
 const game=createGame();game.jumpToWall(8);assert.ok(game.snapshot().finale);assert.equal(game.snapshot().demoComplete,false);
});
test('level three celebrates its last word then completes the demo once without level four',()=>{
 const events=[],game=createGame({storyLimit:3,rng:()=>.7,brickStrength:[],greyMetal:false,onEvent:(name,data)=>events.push({name,...data})}),g=game.snapshot();
 game.breakoutNow();advance(game,50);game.jumpToWall(3);g.breakoutShield=null;
 g.bricks.forEach(br=>br.alive=false);const last=g.bricks[0];
 Object.assign(last,{alive:true,letter:g.spell.word[0],gif:-1,split:false,word:null,jackpot:false});g.spell.filled.fill(true);g.spell.filled[0]=false;
 game.breakBrick(0,true);assert.equal(g.demoComplete,false);assert.equal(g.stats.walls,2);
 advance(game,210);assert.equal(g.demoComplete,true);assert.equal(g.stats.walls,3);assert.equal(g.finale,null);assert.equal(g.dome,false);
 assert.equal(g.bricks.filter(b=>b.alive).length,0);assert.equal(g.balls.length,0);
 assert.deepEqual(events.filter(e=>e.name==='demoComplete'),[{name:'demoComplete',levels:3,walls:3}]);
 const before=JSON.stringify(g.stats),time=g.time;advance(game,1000);game.breakBrick(0);
 assert.equal(JSON.stringify(g.stats),before);assert.equal(g.time,time);assert.equal(events.filter(e=>e.name==='demoComplete').length,1);
 game.jumpToWall(8);assert.equal(g.stats.walls,2);assert.equal(g.demoComplete,false);assert.equal(g.finale,null);
 game.jumpToFinaleBeat('spiral');assert.equal(g.finale,null);
});
test('Endless ignores Story limits and keeps generating beyond them',()=>{
 const game=createGame({endless:true,from:3,storyLimit:1,seed:7,greyMetal:false,brickStrength:[]}),g=game.snapshot();
 for(let i=0;i<g.bricks.length;i++){Object.assign(g.bricks[i],{hp:1,strength:0});game.breakBrick(i);}
 assert.equal(g.demoComplete,false);assert.equal(g.stats.walls,4);assert.ok(g.bricks.some(b=>b.alive));
});
