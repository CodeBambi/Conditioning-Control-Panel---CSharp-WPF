import test from 'node:test';
import assert from 'node:assert/strict';
import {openPayloadSpot,brickOverlap} from './placement.js';
test('payload landing prefers clear space over its blocked origin',()=>{
 const bricks=[{alive:true,x:450,y:200,w:350,h:280,angle:0}];
 const p=openPayloadSpot(600,340,95,bricks,1280,720);
 assert.equal(brickOverlap(p.x,p.y,95,bricks),0);
 assert.ok(p.y+95<720-70);
});
test('rotated faces block placement and dense fields return finite fallback',()=>{
 const bricks=[{alive:true,x:400,y:280,w:200,h:20,angle:Math.PI/2}];
 assert.ok(brickOverlap(500,370,20,bricks)>0);
 assert.equal(brickOverlap(590,290,20,bricks),0);
 const p=openPayloadSpot(500,300,100,[{alive:true,x:0,y:0,w:1280,h:720}],1280,720);
 assert.ok(Number.isFinite(p.x)&&Number.isFinite(p.y));
});
