import {test} from 'node:test';
import assert from 'node:assert/strict';
import {transitPortal} from './portals.js';

const close=(a,b,label)=>assert.ok(Math.abs(a-b)<1e-7,`${label}: ${a} != ${b}`);
const endpoints=(a,b)=>[
  {id:'a',pair:'test',x:300,y:300,angle:a,halfLength:110},
  {id:'b',pair:'test',x:900,y:350,angle:b,halfLength:110},
];

test('either face at either endpoint preserves speed and handedness at every orientation',()=>{
  for(let ai=0;ai<8;ai++)for(let bi=0;bi<8;bi++)for(const reverse of [false,true])for(const side of [-1,1]){
    const portals=endpoints(ai*Math.PI/4,bi*Math.PI/4);
    const entry=portals[reverse?1:0],exit=portals[reverse?0:1];
    const n={x:Math.cos(entry.angle),y:Math.sin(entry.angle)};
    const tangent={x:-n.y,y:n.x};
    for(const offset of [-32,0,32])for(const radius of [2,14,26,120]){
      const start=radius+48;
      const previous={x:entry.x+side*start*n.x+offset*tangent.x,y:entry.y+side*start*n.y+offset*tangent.y};
      const vx=-side*800*n.x+70*tangent.x,vy=-side*800*n.y+70*tangent.y;
      const body={x:previous.x+vx*.45,y:previous.y+vy*.45,vx,vy,r:radius};
      const event=transitPortal(body,previous,portals);
      assert.ok(event,`missing ${entry.id}->${exit.id}, angles ${ai}/${bi}`);
      assert.equal(event.entryId,entry.id);assert.equal(event.exitId,exit.id);
      const angle=exit.angle-entry.angle+Math.PI;
      close(body.vx,vx*Math.cos(angle)-vy*Math.sin(angle),'rotated vx');
      close(body.vy,vx*Math.sin(angle)+vy*Math.cos(angle),'rotated vy');
      close(Math.hypot(body.vx,body.vy),Math.hypot(vx,vy),'speed');
      const outward=(body.x-exit.x)*Math.cos(exit.angle)+(body.y-exit.y)*Math.sin(exit.angle);
      assert.ok(side*outward>=radius+18-1e-7,'whole body clears its corresponding exit face');
    }
  }
});

test('the same body can return through either face after moving clear',()=>{
  for(let ai=0;ai<8;ai++)for(let bi=0;bi<8;bi++)for(const side of [-1,1]){
    const portals=endpoints(ai*Math.PI/4,bi*Math.PI/4),a=portals[0],b=portals[1];
    const nx=Math.cos(a.angle),ny=Math.sin(a.angle);
    const previous={x:a.x+side*40*nx,y:a.y+side*40*ny};
    const body={x:a.x-side*40*nx,y:a.y-side*40*ny,vx:-side*320*nx,vy:-side*320*ny,r:12,age:3.5};
    const first=transitPortal(body,previous,portals);
    assert.equal(first?.exitId,b.id);
    const clear={x:body.x,y:body.y};body.x+=body.vx*.05;body.y+=body.vy*.05;
    assert.equal(transitPortal(body,clear,portals),null);
    body.vx=-body.vx;body.vy=-body.vy;
    const returning={x:body.x,y:body.y};body.x+=body.vx*.5;body.y+=body.vy*.5;
    const second=transitPortal(body,returning,portals);
    assert.equal(second?.entryId,b.id);assert.equal(second?.exitId,a.id);
    close(body.vx,side*320*nx,'return vx');close(body.vy,side*320*ny,'return vy');
    assert.equal(body.age,3.5,'crossing never renews payload lifetime');
  }
});
