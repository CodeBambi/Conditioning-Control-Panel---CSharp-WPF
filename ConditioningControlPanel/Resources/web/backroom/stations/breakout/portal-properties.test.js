import {test} from 'node:test';
import assert from 'node:assert/strict';
import {transitPortal} from './portals.js';

const close=(a,b,label)=>assert.ok(Math.abs(a-b)<1e-7,`${label}: ${a} != ${b}`);
const endpoints=(a,b)=>[
  {id:'a',pair:'test',x:300,y:300,angle:a,halfLength:110},
  {id:'b',pair:'test',x:900,y:350,angle:b,halfLength:110},
];

test('portal orientation matrix preserves speed and handedness in both directions',()=>{
  for(let ai=0;ai<8;ai++)for(let bi=0;bi<8;bi++)for(const reverse of [false,true]){
    const portals=endpoints(ai*Math.PI/4,bi*Math.PI/4);
    const entry=portals[reverse?1:0],exit=portals[reverse?0:1];
    const n={x:Math.cos(entry.angle),y:Math.sin(entry.angle)};
    const tangent={x:-n.y,y:n.x};
    for(const offset of [-32,0,32])for(const radius of [2,14,26]){
      const previous={x:entry.x+36*n.x+offset*tangent.x,y:entry.y+36*n.y+offset*tangent.y};
      const vx=-240*n.x+70*tangent.x,vy=-240*n.y+70*tangent.y;
      const body={x:previous.x+vx*.3,y:previous.y+vy*.3,vx,vy,r:radius};
      const event=transitPortal(body,previous,portals);
      assert.ok(event,`missing ${entry.id}->${exit.id}, angles ${ai}/${bi}`);
      assert.equal(event.entryId,entry.id);assert.equal(event.exitId,exit.id);
      const angle=exit.angle-entry.angle+Math.PI;
      close(body.vx,vx*Math.cos(angle)-vy*Math.sin(angle),'rotated vx');
      close(body.vy,vx*Math.sin(angle)+vy*Math.cos(angle),'rotated vy');
      close(Math.hypot(body.vx,body.vy),Math.hypot(vx,vy),'speed');
      const outward=(body.x-exit.x)*Math.cos(exit.angle)+(body.y-exit.y)*Math.sin(exit.angle);
      assert.ok(outward>=radius+2-1e-7,'whole body emerges in front');
    }
  }
});

test('the same body can return through its exit after moving clear',()=>{
  for(let ai=0;ai<8;ai++)for(let bi=0;bi<8;bi++){
    const portals=endpoints(ai*Math.PI/4,bi*Math.PI/4),a=portals[0],b=portals[1];
    const nx=Math.cos(a.angle),ny=Math.sin(a.angle);
    const previous={x:a.x+40*nx,y:a.y+40*ny};
    const body={x:a.x-40*nx,y:a.y-40*ny,vx:-320*nx,vy:-320*ny,r:12,age:3.5};
    const first=transitPortal(body,previous,portals);
    assert.equal(first?.exitId,b.id);
    const clear={x:body.x,y:body.y};body.x+=body.vx*.05;body.y+=body.vy*.05;
    assert.equal(transitPortal(body,clear,portals),null);
    body.vx=-body.vx;body.vy=-body.vy;
    const returning={x:body.x,y:body.y};body.x+=body.vx*.5;body.y+=body.vy*.5;
    const second=transitPortal(body,returning,portals);
    assert.equal(second?.entryId,b.id);assert.equal(second?.exitId,a.id);
    close(body.vx,320*nx,'return vx');close(body.vy,320*ny,'return vy');
    assert.equal(body.age,3.5,'crossing never renews payload lifetime');
  }
});
