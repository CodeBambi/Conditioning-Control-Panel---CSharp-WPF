import {test} from 'node:test';
import assert from 'node:assert/strict';
import {transitPortal} from './portals.js';
const pair=()=>[{id:'a',pair:'p',x:200,y:200,angle:-Math.PI/2,halfLength:70},
 {id:'b',pair:'p',x:800,y:300,angle:Math.PI,halfLength:70}];
test('both ends accept entry and preserve carrier identity, lifetime and speed',()=>{
 const portals=pair(),body={x:212,y:220,vx:10,vy:100,r:8,age:4,kind:'laser',trail:[1,2]};
 const event=transitPortal(body,{x:208,y:180},portals,{kind:'powerup'});
 assert.equal(event.entryId,'a');assert.equal(event.exitId,'b');assert.equal(body.age,4);
 assert.equal(body.kind,'laser');assert.equal(body.trail.length,0);
 assert.ok(Math.abs(body.vx+100)<1e-8&&Math.abs(body.vy-10)<1e-8);
 const back={x:810,y:310,vx:100,vy:0,r:8};
 const reverse=transitPortal(back,{x:770,y:310},portals);
 assert.equal(reverse.entryId,'b');assert.equal(reverse.exitId,'a');assert.ok(back.vy<0);
});
test('backside crossings and carriers too wide for either opening pass normally',()=>{
 const portals=pair(),body={x:200,y:180,vx:0,vy:-100,r:8};
 assert.equal(transitPortal(body,{x:200,y:220},portals),null);
 body.y=220;body.vy=100;body.x=269;
 assert.equal(transitPortal(body,{x:269,y:180},portals),null);
 body.x=200;portals[1].halfLength=7;
 assert.equal(transitPortal(body,{x:200,y:180},portals),null);
});
test('sweep chooses the earliest mouth even when the endpoints are listed out of order',()=>{
 const portals=[{id:'late',pair:'l',x:200,y:200,angle:0,halfLength:60},
 {id:'late-out',pair:'l',x:900,y:400,angle:Math.PI,halfLength:60},
 {id:'first',pair:'f',x:500,y:200,angle:0,halfLength:60},
 {id:'first-out',pair:'f',x:900,y:500,angle:Math.PI,halfLength:60}];
 const body={x:100,y:200,vx:-1000,vy:0,r:8};
 assert.equal(transitPortal(body,{x:600,y:200},portals).entryId,'first');
});
test('exit lock prevents immediate recross and releases after the carrier clears the mouth',()=>{
 const portals=pair(),body={x:200,y:200,vx:0,vy:100,r:8};
 transitPortal(body,{x:200,y:180},portals);
 const exit={x:body.x,y:body.y};body.x=810;body.vx=100;body.vy=0;
 assert.equal(transitPortal(body,exit,portals),null);
 body.x=810;body.y=300;
 assert.equal(transitPortal(body,{x:770,y:300},portals).entryId,'b');
});
test('an incomplete or ambiguous pair never teleports a carrier',()=>{
 const portals=pair(),body={x:200,y:220,vx:0,vy:100,r:8};
 assert.equal(transitPortal(body,{x:200,y:180},portals.slice(0,1)),null);
 portals.push({...portals[1],id:'third'});
 assert.equal(transitPortal(body,{x:200,y:180},portals),null);
});

test('numeric endpoint zero clears its exit lock before a legitimate return',()=>{
 const portals=pair();portals[0].id=1;portals[1].id=0;
 const body={x:200,y:200,vx:0,vy:100,r:8};
 assert.equal(transitPortal(body,{x:200,y:180},portals).exitId,0);
 assert.equal(body.portalExit,0);
 body.x=810;body.y=300;body.vx=100;body.vy=0;
 const back=transitPortal(body,{x:770,y:300},portals);
 assert.equal(back.entryId,0);assert.equal(back.exitId,1);
});
