(async () => {
  const P = window.PBP, B = P.board;
  const { ACTS, captureVolumes, captureBounds } = await import('./board/choreography.js');
  P.ramp?.setEnabled(false); P.game.clock.stop(); B.setWobble(0); B.setCameraSway(0);
  window.requestAnimationFrame = () => 0;
  await new Promise(r => setTimeout(r, 200));
  let passed = 0, events = [], cases = 0;
  const {hopPlan,hopAt}=await import('./board/hops.js');
  for (const name of ['land', 'sunk', 'hit', 'dissolve', 'contact']) P.bus.on(name, e => events.push({ name, ...e }));
  const ok = (v, msg) => { if (!v) throw Error(msg); passed++; };
  for (const distance of [1, 3, 5]) {
    const plan=hopPlan({x:0,z:0},{x:distance,z:0});
    ok(plan.count===(distance===1?2:distance),'hop count follows board squares');
    for(let i=0;i<plan.count;i++) {
      const land=i*plan.beat+plan.flightEnd;
      const first=hopAt(plan,land+.01),last=hopAt(plan,land+.08);
      ok(first.height<.0001 && last.height<.0001,'landing stays on floor');
      ok(Math.abs(first.travel-last.travel)<.0001,'no sliding during landing rebound');
      ok(Math.abs(first.ring)>.1,'wobble starts at touchdown');
    }
  }
  const {impactTime,impactDelay,impactPulse}=await import('./board/impact.js');
  for(const motion of ['p','n','b','r','k']) {
    const delay=impactDelay(motion); let previous=0;
    for(let t=0;t<2;t+=1/240) {const mapped=impactTime(t,1,delay);ok(mapped>=previous-1e-10,'impact clock is monotone');previous=mapped;}
    ok(Math.abs(impactTime(.99,1,delay)-.99)<1e-10,'no pause before contact');
    ok(Math.abs(impactTime(2,1,delay)-(2-delay))<1e-10,'bounded contact delay');
  }
  const {siliconePoint}=await import('./board/silicone.js');
  const dent={x:0,z:0,dx:.10,dy:0,dz:0,dentAt:.55};
  const near=siliconePoint({x:-.15,y:.55,z:0},1,dent),far=siliconePoint({x:.15,y:.55,z:0},1,dent);
  ok(near.x>-.15&&Math.abs(far.x-.15)<1e-10,'dent affects the struck side');
  ok(siliconePoint({x:-.15,y:0,z:0},1,dent).x===-.15,'dent leaves the planted base fixed');
  ok(impactPulse(.015,1).dent>0&&impactPulse(.015,1).wave===0,'local dent precedes travelling wobble');
  ok(impactPulse(.08,1.4).wave>impactPulse(.08,.6).wave,'tall victims flex more');
  function setup(type = 'p', side = 'w', victim = 'r', crowded = false) {
    P.bus.emit('local', {}); B.pieces.setPosition({});
    const map = {d4:{type, side},d5:{type:victim,side:side==='w'?'b':'w'}};
    if(crowded) for(const sq of ['c3','d3','e3','c4','e4','c5','e5','c6','d6','e6']) map[sq]={type:'k',side:'b'};
    B.pieces.setPosition(map); events=[];
    return { attacker:B.pieces.pieceAt('d4'), victim:B.pieces.pieceAt('d5'), others:B.view.pieceGroup.children.filter(p=>!['d4','d5'].includes(p.userData.square)) };
  }
  const step = seconds => { for(let t=0;t<seconds;t+=1/60){B.anim.update(1/60);B.jiggle.system.update(1/60);B.dust.update(1/60,B.view.camera,B.view.renderer);} };
  const overlap = (a,b) => a.max.x > b.min.x+.0001 && a.min.x < b.max.x-.0001 && a.max.y > b.min.y+.0001 && a.min.y < b.max.y-.0001 && a.max.z > b.min.z+.0001 && a.min.z < b.max.z-.0001;
  for(const type of ['p','n','b','r','q','k']) for(const side of ['w','b']) for(const crowded of [false,true]) for(let variant=0;variant<(type==='q'?5:1);variant++) {
    const pieces=setup(type,side,crowded?'k':'p',crowded); B.pieces.move('d4','d5');
    const style=B.anim.stats().acts[0]; ok(!!style,type+' signature starts');
    for(let frame=0;frame<Math.ceil((style.end + .1) * 60);frame++) {
      step(1/60);
      const p=pieces.attacker,v=pieces.victim;
      if(!B.anim.stats().acts.length) continue;
      const av=captureVolumes(p);
      const blockers=pieces.others.filter(x=>x.parent===B.view.pieceGroup);
      if(v.parent===B.view.pieceGroup)blockers.push(v);
      for(const other of blockers) {
        const bv=captureVolumes(other);
        if(av.some(a=>bv.some(b=>overlap(a,b))))throw Error(type+' '+side+' crowded='+crowded+' attacker overlaps '+other.userData.square+' frame='+frame);
      }
      if(v.parent===B.view.pieceGroup) {
        const vv=captureVolumes(v);
        for(const other of pieces.others) if(vv.some(a=>captureVolumes(other).some(b=>overlap(a,b)))) throw Error(type+' victim hits neighbour frame='+frame);
        ok(captureBounds(v).min.y>=-.0001,type+' victim above board');
      }
      ok(captureBounds(p).min.y>=-.0001,type+' attacker above board');
    }
    ok(events.filter(e=>e.name==='hit').length===1,type+' one hit');
    ok(events.filter(e=>e.name==='land'&&e.capture).length===1,type+' one landing');
    ok(events.filter(e=>e.name==='sunk').length===1,type+' one exit');
    ok(events.filter(e=>e.name==='dissolve').length===(type==='r'||style.variation==='royal-fling'?0:1),type+' dissolve except launch');
    ok(!B.anim.busy(),type+' settles');
    if(style.variation==='triple-bash') ok(events.filter(e=>e.name==='contact').length===2,'queen has two light contacts before the squash');
    cases++;
  }
  const {createQueenDeck}=await import('./board/queen.js');
  for(const seed of [0,.25,.99]) {
    const deck=createQueenDeck(()=>seed);let last;
    for(let cycle=0;cycle<4;cycle++) {
      const seen=new Set();
      for(let i=0;i<5;i++) {const act=deck();ok(act!==last,'queen does not repeat adjacent attacks');seen.add(act.name);last=act;}
      ok(seen.size===5,'each shuffle plays every queen attack');
    }
  }
  const planted=setup('b','w','r'), base=planted.victim.position.clone();
  B.pieces.move('d4','d5');step(B.anim.stats().acts[0].approach + .80);
  ok(Math.hypot(planted.victim.position.x-base.x,planted.victim.position.z-base.z)<.0001,'first slap keeps victim base planted');
  const firstBend=Math.hypot(planted.victim.userData.capturePose.x,planted.victim.userData.capturePose.z);
  ok(firstBend>.08,'first slap bends victim');step(.34);
  ok(Math.hypot(planted.victim.userData.capturePose.x,planted.victim.userData.capturePose.z)<firstBend*.15,'victim snaps back before second slap');
  step(.22);ok(Math.abs(planted.victim.quaternion.w)<.95,'second slap tips victim');
  for(const type of ['p','n','b','r','q','k']) {
    P.bus.emit('local',{});B.pieces.setPosition({});B.pieces.setPosition({d4:{type,side:'w'}});events=[];
    const piece=B.pieces.pieceAt('d4');B.pieces.move('d4','d5');step(.16);
    ok(piece.position.y>.08,type+' travels off the floor');step(.5);
    ok(events.filter(e=>e.name==='land').length===1,type+' quiet hop lands once');
    ok(B.sfx.log().some(e=>e.name==='hop'),type+' hop cue plays');
  }
  P.bus.emit('local',{});B.pieces.setPosition({});B.pieces.setPosition({d2:{type:'q',side:'w'}});events=[];
  B.pieces.move('d2','d7');step(2.2);ok(events.filter(e=>e.name==='land').length===1,'long hop sequence has one final landing');
  ok(B.sfx.log().some(e=>e.name==='hopland'),'intermediate hop has landing sound');
  for(const time of [0,.1,.8,1.2]) {setup('n');B.pieces.move('d4','d5');step(time);B.anim.skip();step(5.5);ok(events.filter(e=>e.name==='sunk').length===1,'skip one exit');ok(events.filter(e=>e.name==='land'&&e.capture).length===1,'skip one landing');}
  setup('p');B.pieces.remove('d5');B.pieces.move('d4','e5');step(5.5);ok(events.some(e=>e.name==='land'&&e.capture),'en passant');
  setup('r');B.pieces.move('d4','d5');step(.1);B.pieces.setPosition({d5:{type:'r',side:'w'},d6:{type:'q',side:'b'}});B.pieces.move('d6','d5');step(5.5);ok(events.filter(e=>e.name==='sunk').length===2,'rapid recapture');
  setup('p');B.pieces.pieceAt('d4').position.set(-.5,0,-.5);B.pieces.move('d4','d5');ok(B.anim.stats().acts.length===1,'drag released exactly at target still captures');step(5.5);
  P.game.reset();P.game.rules.reset('1r5k/P7/8/8/8/8/8/K7 w - - 0 1');P.game.start();events=[];
  ok(!!P.game.tryMove('a7','b8','q'),'capture promotion legal');step(5.5);ok(B.pieces.pieceAt('b8').userData.type==='q','promotion model');ok(events.filter(e=>e.name==='land'&&e.capture).length===1,'promotion capture');
  P.game.reset();P.game.rules.reset('7k/8/8/3p4/4P3/8/8/K7 w - - 0 1');P.game.start();events=[];P.game.tryMove('e4','d5');step(.1);P.game.takeBack();step(5.5);
  ok(B.pieces.pieceAt('e4')?.userData.side==='w'&&B.pieces.pieceAt('d5')?.userData.side==='b','undo restores board');
  setup('p');B.pieces.move('d4','d5');step(5.5);events=[];B.pieces.move('d5','d6');step(.8);ok(events.filter(e=>e.name==='land'&&!e.capture).length===1,'next quiet move is not another capture');
  P.settings.reducedMotion=true;setup('n');B.pieces.move('d4','d5');step(.6);ok(!B.anim.busy(),'reduced motion settles quickly');ok(!events.some(e=>e.name==='hit'),'reduced skips slapstick');P.settings.reducedMotion=false;
  setup();B.pieces.move('d4','d5');step(.1);P.bus.emit('local',{});events=[];step(5.5);ok(!events.length,'reset has no late events');
  return {passed,cases};
})()
