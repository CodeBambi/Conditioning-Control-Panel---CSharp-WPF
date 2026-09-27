(async () => {
  const P = window.PBP, B = P.board;
  P.ramp?.setEnabled(false);
  B.setWobble(0); B.setCameraSway(0); B.setSide('w', true);
  let queue = [];
  window.requestAnimationFrame = fn => { queue.push(fn); return queue.length; };
  await new Promise(r => setTimeout(r, 300));
  let passed = 0, events = [];
  for (const name of ['land', 'sunk']) P.bus.on(name, e => events.push({ name, ...e }));
  const ok = (v, msg) => { if (!v) throw Error(msg); passed++; };
  function setup(type = 'p') {
    P.bus.emit('local', {});
    B.pieces.setPosition({ d4: {type, side:'w'}, d5: {type:'p', side:'b'} });
    events = [];
  }
  function step(seconds) { for (let t = 0; t < seconds; t += .005) { B.anim.update(.005); B.jiggle.system.update(.005); } }
  const variations = {};
  for (const type of ['p','n','b','r','q','k']) {
    variations[type] = [];
    for (let n = 0; n < 3; n++) {
      setup(type); B.pieces.move('d4','d5');
      const style = B.anim.stats().tumbles[0]?.variation;
      variations[type].push(style);
      step(.445);
      ok(events.filter(e=>e.name==='land' && e.capture).length === 1, type+' arrival emits one capture');
      const at = B.pieces.pieceAt('d5').position;
      ok(Math.hypot(at.x + .5, at.z + .5) < .001, type+' occupies its square');
      step(1.15);
      ok(events.filter(e=>e.name==='sunk').length === 1, type+' emits one victim exit');
      ok(!B.anim.busy(), type+' animation settles');
    }
    ok(new Set(variations[type]).size === 3, type+' has three distinct captures');
  }
  for (const time of [0,.10,.32,.60]) {
    setup('n'); B.pieces.move('d4','d5'); step(time); B.anim.skip(); step(1.8);
    ok(events.filter(e=>e.name==='sunk').length === 1, 'skip emits one exit at '+time);
    ok(events.filter(e=>e.name==='land' && e.capture).length === 1, 'skip preserves landing at '+time);
  }
  setup(); B.pieces.remove('d5'); B.pieces.move('d4','e5'); step(1.8);
  ok(events.some(e=>e.name==='land' && e.capture), 'en passant is a capture');
  setup('r'); B.pieces.move('d4','d5'); step(.10);
  B.pieces.setPosition({d5:{type:'r',side:'w'},d6:{type:'q',side:'b'}});
  B.pieces.move('d6','d5'); step(1.8);
  ok(events.filter(e=>e.name==='sunk').length===2, 'rapid recapture exits both victims');
  ok(!B.anim.busy(), 'rapid recapture settles');
  setup('p');
  B.pieces.pieceAt('d4').position.set(-.5,.3,-.5); // a held piece released over its target
  B.pieces.move('d4','d5'); step(.55);
  const taken = B.view.pieceGroup.children.find(p=>p.userData.side==='b');
  ok(taken.position.z < -.5, 'drag release preserves the logical impact direction'); step(1.2);
  P.game.reset();
  P.game.rules.reset('1r5k/P7/8/8/8/8/8/K7 w - - 0 1'); P.game.start(); events=[];
  ok(!!P.game.tryMove('a7','b8','q'), 'legal capture promotion'); step(.445);
  ok(B.pieces.pieceAt('b8').userData.type==='q', 'promotion replaces the model');
  ok(events.filter(e=>e.name==='land'&&e.capture).length===1, 'promotion retains capture contact');step(1.2);
  P.game.reset();
  P.game.rules.reset('7k/8/8/3p4/4P3/8/8/K7 w - - 0 1'); P.game.start(); events=[];
  P.game.tryMove('e4','d5');step(.1);P.game.takeBack();step(1.8);
  ok(B.pieces.pieceAt('e4')?.userData.side==='w' && B.pieces.pieceAt('d5')?.userData.side==='b', 'undo restores both pieces');
  ok(events.filter(e=>e.name==='sunk').length===1, 'undo settles one victim without a late second exit');
  P.bus.emit('local', {}); B.pieces.setPosition({});
  B.pieces.setPosition({d4:{type:'p',side:'w'},d5:{type:'p',side:'b'},e4:{type:'q',side:'w'},e5:{type:'r',side:'b'},f4:{type:'r',side:'w'}});events=[];
  B.pieces.move('d4','d5');B.pieces.move('e4','e5');B.pieces.move('f4','f5');
  ok(B.anim.stats().tumbles.every(t=>!!t.variation), 'batched captures each own a distinct victim');step(1.8);
  ok(events.filter(e=>e.name==='land'&&e.capture).length===2, 'batched quiet move does not inherit a capture');
  P.settings.reducedMotion = true;
  setup('n'); B.pieces.move('d4','d5');
  ok(B.anim.stats().slides[0].hop===0, 'reduced motion has no knight jump');
  step(.75); ok(!B.anim.busy(), 'reduced capture settles promptly');
  ok(B.jiggle.system.stats().reduced, 'jiggle respects the in-app setting');
  P.settings.reducedMotion = false;
  setup(); B.pieces.move('d4','d5'); step(.1); P.bus.emit('local', {}); events=[]; step(2);
  ok(!events.length, 'new match has no stale animation events');
  B.pieces.setPosition(P.game.rules.position());
  let now = performance.now();
  window.__reviewStep = ms => { for(let t=0;t<ms;t+=16.667){now+=16.667;const q=queue;queue=[];for(const fn of q)fn(now);} };
  window.__reviewStep(1700);
  return { passed, variations };
})()
