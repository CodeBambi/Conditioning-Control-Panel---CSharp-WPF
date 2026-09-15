import * as T from 'three';

// Large tabletop cards: preserve each public owner/slot, arrange only presentation.
export function cardLayout(fixture, hands=1, counts={d:2,0:2}) {
  const first=fixture.getObjectByName('card_slot_dealer_0'), last=fixture.getObjectByName('card_slot_dealer_5');
  fixture.updateWorldMatrix(true,true);
  const anchor=fixture.getObjectByName('card_slot_p0_0'), scale=anchor.getWorldScale(new T.Vector3());
  const basis=anchor.getWorldQuaternion(new T.Quaternion());
  const width=anchor.userData.card_width*scale.x*2.25, height=anchor.userData.card_height*scale.z*2.25;
  const origin=first.getWorldPosition(new T.Vector3()).lerp(last.getWorldPosition(new T.Vector3()),.5);
  const right=new T.Vector3(1,0,0).applyQuaternion(basis), depth=new T.Vector3(0,0,1).applyQuaternion(basis), up=new T.Vector3(0,1,0).applyQuaternion(basis);
  const points=[];
  function point(owner,slot){
    const count=Math.max(2,Math.min(owner==='d'?12:6,counts[owner]||2));
    const row=Math.floor(slot/6), columns=Math.min(6,count-row*6), column=slot%6;
    const spread=width*(columns===2?.88:hands===2?.30:.48);
    const x=(column-(columns-1)/2)*spread+(owner==='d'||hands===1?0:owner===0?width*1.42:-width*1.42);
    const z=(owner==='d'?row*height*.62:height*1.7)-height*.4;
    return origin.clone().addScaledVector(right,x).addScaledVector(depth,z).addScaledVector(up,.004+slot*.0008);
  }
  for(const owner of ['d',0,...(hands===2?[1]:[])])for(let slot=0;slot<Math.max(2,counts[owner]||2);slot++)points.push(point(owner,slot));
  return {width,height,basis,points,point};
}
export function cardCounts(cards=[]) {const counts={d:2,0:2};for(const c of cards)counts[c.owner]=Math.max(counts[c.owner]||2,c.slot+1);return counts;}
