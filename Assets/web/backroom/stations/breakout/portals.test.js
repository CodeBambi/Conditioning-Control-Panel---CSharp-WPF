import {test} from 'node:test';
import assert from 'node:assert/strict';
import {transitPortal,PORTAL_DEPTH} from './portals.js';
const pair=()=>[{id:1,pair:'p',x:200,y:200,angle:-Math.PI/2,halfLength:65},
 {id:0,pair:'p',x:800,y:300,angle:Math.PI,halfLength:65}];
test('both faces transport on touch before the centre crosses the plane',()=>{
 for(const side of [-1,1]) {
  const portals=pair(),body={x:210,y:200-side*20,vx:10,vy:side*100,r:8,age:4,trail:[1,2]};
  const event=transitPortal(body,{x:208,y:200-side*60},portals,{kind:'powerup'});
  assert.equal(event.entryId,1);assert.equal(event.exitId,0);assert.equal(event.entrySide,side);
  assert.equal(body.age,4);assert.equal(body.trail.length,0);
  assert.ok(Math.abs(body.vx+side*100)<1e-8&&Math.abs(body.vy-10)<1e-8);
  assert.ok((body.x-800)*side<0,'matching exit face');
 }
});
test('rounded rim catches a swept edge hit that never crosses the portal centre plane',()=>{
 const portals=pair(),body={x:270,y:210,vx:0,vy:100,r:8};
 const event=transitPortal(body,{x:270,y:180},portals);
 assert.ok(event);assert.equal(event.entryId,1);
 assert.ok(event.fromY<200);assert.ok(Math.abs(body.vx+100)<1e-8);
});
test('grown bubbles larger than the mouth still transit with clamped offset and full clearance',()=>{
 const portals=pair(),body={x:330,y:160,vx:0,vy:100,r:120};
 const event=transitPortal(body,{x:330,y:0},portals,{kind:'bubble'});
 assert.ok(event);assert.ok(body.x<800-120-PORTAL_DEPTH);
 assert.ok(Math.abs(body.y-300)<=65+1e-7);
});
test('fast carriers choose the earliest mouth even when endpoint order is reversed',()=>{
 const portals=[{id:'late',pair:'l',x:200,y:200,angle:0,halfLength:60},
 {id:'late-out',pair:'l',x:900,y:400,angle:Math.PI,halfLength:60},
 {id:'first',pair:'f',x:500,y:200,angle:0,halfLength:60},
 {id:'first-out',pair:'f',x:900,y:500,angle:Math.PI,halfLength:60}];
 const body={x:100,y:200,vx:-60000,vy:0,r:8};
 assert.equal(transitPortal(body,{x:600,y:200},portals).entryId,'first');
});
test('exit overlap cannot ping-pong and numeric id zero unlocks after either-side clearance',()=>{
 for(const side of [-1,1]) {
  const portals=pair(),body={x:200,y:200-side*24,vx:0,vy:side*100,r:8};
  assert.equal(transitPortal(body,{x:200,y:200-side*60},portals).exitId,0);
  const exit={x:body.x,y:body.y};body.x=800;body.vx=side*100;body.vy=0;
  assert.equal(transitPortal(body,exit,portals),null);
  body.x=800-side*20;body.y=300;
  const back=transitPortal(body,{x:800-side*80,y:300},portals);
  assert.equal(back.entryId,0);assert.equal(back.exitId,1);assert.equal(back.entrySide,side);
 }
});
test('clear misses, stationary overlaps and incomplete pairs do not teleport',()=>{
 const portals=pair(),body={x:290,y:200,vx:0,vy:100,r:8};
 assert.equal(transitPortal(body,{x:290,y:100},portals),null);
 body.x=200;assert.equal(transitPortal(body,{x:200,y:200},portals),null);
 assert.equal(transitPortal(body,{x:200,y:100},portals.slice(0,1)),null);
 portals.push({...portals[1],id:'third'});
 assert.equal(transitPortal(body,{x:200,y:100},portals),null);
});
