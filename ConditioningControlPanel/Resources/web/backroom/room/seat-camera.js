import * as T from 'three';
import { cardLayout } from '../stations/cards/layout-3d.js';

// Fit authored play surfaces inside the space left by the station controls.
const PC_TILT=1.35, PC_MARGIN=.04;   // the roulette PC seat's lean, and the breath it keeps off the band's edges
export function seatPose(row, fixture, camera, width, height, cardHands=1, counts={d:2,0:2}) {
  fixture.updateWorldMatrix(true, true);
  const bounds = new T.Box3(), box = new T.Box3();
  function include(name) { const node = fixture.getObjectByName(name); if (node) bounds.union(box.setFromObject(node)); }
  if (row.id === 'wheel') { include('wheel_rotor'); include('pointer'); fixture.traverse(n=>{if(/^bulb_/.test(n.name))include(n.name);}); }
  if (row.id === 'cards') {
    const layout=cardLayout(fixture,cardHands,counts);
    for(const point of layout.points)for(const x of [-.5,.5])for(const z of [-.5,.5])
      bounds.expandByPoint(new T.Vector3(x*layout.width,0,z*layout.height).applyQuaternion(layout.basis).add(point));
  }
  // Roulette on a phone frames the mat while bets are open and the whole table once the ball runs
  // (stations/roulette/mat-3d.js sets userData.frame on its group); a desktop sees the whole table, seated at it.
  const phone=row.id==='roulette'&&(width<=800||height<=500), landscape=phone&&width>height;
  const matFrame=phone&&(fixture.getObjectByName('roulette_runtime_mat')?.userData.frame||'mat')==='mat';
  const pcSeat=row.id==='roulette'&&!phone;   // a mouse viewport: THE PC SEAT below solves this one's distance
  const wide=[];   // points that only bound the view vertically: the wheel above a mat-framed phone view
  let matOnly=null;
  if (row.id === 'roulette') { include('betting_mat'); include('zero_bet');
    fixture.traverse(n => { if (n.name.startsWith('bet_hit_')) {
      const w=n.userData.hit_width/2, h=n.userData.hit_depth/2;
      for(const x of [-w,w])for(const z of [-h,h])bounds.expandByPoint(n.localToWorld(new T.Vector3(x,0,z)));
    }});
    matOnly=bounds.clone();   // the board on its own, before the wheel joins it: the PC seat sits this on the frame's floor
    const track=fixture.getObjectByName('ball_track');
    if(track){box.setFromObject(track);if(matFrame){for(const x of [box.min.x,box.max.x])for(const y of [box.min.y,box.max.y])for(const z of [box.min.z,box.max.z])wide.push(new T.Vector3(x,y,z));}else bounds.union(box);}
  }
  // The slot: the reel window and the jackpot screen are the pan-in target, fitted on height alone (a phone held
  // upright would otherwise back off to fit their width); the station's own canvas frames the play once it is up.
  const travelOnly = row.id === 'slot';
  if (travelOnly) { include('reel_window'); include('screen_jackpot'); }
  if (bounds.isEmpty()) return null;
  const matCenter=bounds.getCenter(new T.Vector3()), matBounds=bounds.clone();
  const direction=new T.Vector3().fromArray(row.approach).sub(new T.Vector3().fromArray(row.look)); direction.y=0; direction.normalize();
  // A landscape phone is short: the wheel's far side may leave the top, its near rim stays in view.
  const far=landscape?.7:Infinity;
  for(const p of wide){const d=p.clone().sub(matCenter).dot(direction);if(d<-far)p.addScaledVector(direction,-far-d);bounds.expandByPoint(p);}
  const center=bounds.getCenter(new T.Vector3());
  // PC_TILT leans the roulette PC seat in from the old top-down 3. The table is nearly square and a mouse screen is
  // not, so hanging over it left the sides of the frame empty; leaning in foreshortens the table's depth, the wheel
  // costs less height, and the canopy and the dealer read as the room behind it.
  direction.y=row.id==='cards'?1.5:row.id==='roulette'?(pcSeat?PC_TILT:3):0; direction.normalize();
  const right=new T.Vector3().crossVectors(new T.Vector3(0,1,0),direction).normalize();
  const up=new T.Vector3().crossVectors(direction,right).normalize();
  if(matFrame)center.addScaledVector(right,matCenter.clone().sub(center).dot(right));   // the mat sits centred, the wheel above it
  const rightInset=landscape?200:0;
  let top=phone?(landscape?50:150):height<500?85:row.id==='roulette'?110:120, bottom=phone?(landscape?12:108):row.id==='roulette'?155:row.id==='cards'?170:180;
  if(row.id==='slot'){top=height<500?56:110;bottom=height<500?24:150;}   // no station controls share this view: the band is the chrome's only
  // THE WHEEL AND THE CARDS ARE STAGED ROWS TOO. The phone band above is gated on the roulette, and the slot took
  // its own escape on the line before, so these two solved a small screen against the DESK's band: at 844x390 that
  // is top 85 plus bottom 180, 32 per cent of the height left to fit in, and the seat backs off more than twice as
  // far as a desk does. The owner read it as "the pov is too far" at the slot; it was true at three tables, and
  // this is the other two (2026-09-16). The numbers are each station's own chrome, measured, not the roulette's.
  const small=width<=800||height<=500, shortWays=small&&width>height;
  if((row.id==='wheel'||row.id==='cards')&&small){
    top=shortWays?50:118;                                   // the room's HUD row, or the HUD plus the station chips
    bottom=shortWays?56:(row.id==='cards'?132:108);         // Odds and Spin on the sill; the cards keep their hand
  }
  const available=Math.max(.3,(height-top-bottom)/height), tan=Math.tan(camera.fov*Math.PI/360);
  const fitX=tan*(width-rightInset)/height*(row.id==='wheel'?.965:phone?.985:.92), fitY=tan*available*(phone?.96:.9);
  let distance=.4, sideways=.4;
  const corners=b=>{const list=[];for(const x of [b.min.x,b.max.x])for(const y of [b.min.y,b.max.y])for(const z of [b.min.z,b.max.z])list.push(new T.Vector3(x,y,z).sub(center));return list;};
  for(const p of corners(bounds)){const depth=p.dot(direction);distance=Math.max(distance,depth+Math.abs(p.dot(up))/fitY);if(!matFrame)sideways=Math.max(sideways,travelOnly?0:depth+Math.abs(p.dot(right))/fitX);}
  if(matFrame)for(const p of corners(matBounds)){const depth=p.dot(direction);sideways=Math.max(sideways,depth+Math.abs(p.dot(right))/fitX);}
  distance=Math.max(distance,sideways);
  let offset=(bottom-top)/(2*height);
  // THE PC SEAT (owner, 2026-09-15: "zoom closer, like way closer, so we've got on screen the board to place bets
  // exactly on the bottom of the screen and the rest is wheel and ambient"). The fit above centres the whole table in
  // the band and stops at whichever corner of its box reaches furthest - on a wide screen that corner is empty room,
  // and the board ends up a small island in the middle. This solves the distance from the two edges the owner named
  // instead: the mat's near edge on the floor of the band, the wheel's far rim just under its ceiling. The band is the
  // chrome's own, so the board stays clear of the controls and every chip stays hit-testable, and the flick hint arc
  // rides inside the ball track's radius, so the track's bound carries the hint into view with the ball and the rotor.
  if (pcSeat && matOnly) {
    const floorY=2*bottom/height-1+PC_MARGIN*available, ceilingY=1-2*top/height-PC_MARGIN*available;
    const seen=list=>list.map(p=>({depth:p.dot(direction),rise:p.dot(up)}));
    const table=seen(corners(bounds)), mat=seen(corners(matOnly));
    const riseAt=(p,d)=>p.rise/((d-p.depth)*tan);
    const sit=d=>{let low=Infinity,high=-Infinity;
      for(const p of table)high=Math.max(high,riseAt(p,d));
      for(const p of mat)low=Math.min(low,riseAt(p,d));
      const shift=(floorY-low)/2; return {fits:high+2*shift<=ceilingY,shift};};
    let near=.5,seat=distance;
    for(let i=0;i<40;i++){const mid=(near+seat)/2;if(sit(mid).fits)seat=mid;else near=mid;}
    distance=Math.max(seat,sideways);
    offset=sit(distance).shift;
  }
  const position=center.clone().addScaledVector(direction,distance);
  const view=new T.PerspectiveCamera();view.rotation.order='YXZ';view.position.copy(position);view.lookAt(center);
  return {pos:position.toArray(),yaw:view.rotation.y,pitch:view.rotation.x,offset,offsetX:rightInset/(2*width),bounds:{min:bounds.min.toArray(),max:bounds.max.toArray()}};
}
export const easeSeat=t=>t*t*(3-2*t);
export const shortAngle=(a,b)=>a+Math.atan2(Math.sin(b-a),Math.cos(b-a));
