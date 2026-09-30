(async () => {
  const P = window.PBP, B = P.board;
  const { captureVolumes } = await import('./board/choreography.js');
  P.ramp?.setEnabled(false); P.game.clock.stop(); B.setWobble(0); B.setCameraSway(0);
  window.requestAnimationFrame = () => 0;
  await new Promise(r => setTimeout(r, 200));
  let passed = 0, cases = 0, lands = [];
  const ok = (v, message) => { if (!v) throw Error(message); passed++; };
  P.bus.on('land', e => lands.push(e));
  const overlaps = (a, b) => a.max.x > b.min.x + .001 && a.min.x < b.max.x - .001
    && a.max.y > b.min.y + .001 && a.min.y < b.max.y - .001
    && a.max.z > b.min.z + .001 && a.min.z < b.max.z - .001;
  const step = () => { B.anim.update(1 / 120); B.jiggle.system.update(1 / 120); };
  function setup(side) {
    P.bus.emit('local', {}); B.pieces.setPosition({}); P.game.reset();
    P.game.rules.reset(`r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R ${side} KQkq - 0 1`);
    P.game.start(); P.game.clock.stop(); lands = [];
  }
  for (const side of ['w', 'b']) for (const wing of ['k', 'q']) for (const dragged of [false, true]) {
    P.settings.reducedMotion = false; setup(side);
    const rank = side === 'w' ? '1' : '8', to = (wing === 'k' ? 'g' : 'c') + rank;
    const rookFrom = (wing === 'k' ? 'h' : 'a') + rank, rookTo = (wing === 'k' ? 'f' : 'd') + rank;
    const king = B.pieces.pieceAt('e' + rank), rook = B.pieces.pieceAt(rookFrom);
    if (dragged) king.position.x = wing === 'k' ? 2.5 : -1.5;
    ok(P.game.tryMove('e' + rank, to), `${side} ${wing} legal castle`);
    for (let f = 0; f < 150; f++) {
      step(); const rv = captureVolumes(rook);
      for (const other of B.view.pieceGroup.children) {
        if (other === rook || !other.userData.type) continue;
        const ov = captureVolumes(other);
        ok(!rv.some(a => ov.some(b => overlaps(a, b))), `${side} ${wing} dragged=${dragged} overlap at ${f}/120 with ${other.userData.square}`);
      }
    }
    ok(!B.anim.busy(), 'both pieces settle within 1.25 seconds');
    ok(B.pieces.pieceAt(to) === king && B.pieces.pieceAt(rookTo) === rook, 'final square ownership');
    ok(lands.filter(e => e.piece === 'k').length === 1 && lands.filter(e => e.piece === 'r').length === 1, 'one landing each');
    ok(Math.abs(rook.position.z) === 3.5 && rook.position.y === 0, 'rook planted on back rank');
    cases++;
  }
  for (const action of ['reduced', 'skip', 'undo', 'reset', 'reduce-midflight']) {
    P.settings.reducedMotion = action === 'reduced'; setup('w');
    ok(P.game.tryMove('e1', 'g1'), action + ' castle');
    for (let f = 0; f < 24; f++) step();
    if (action === 'skip') B.anim.skip();
    if (action === 'undo') P.game.takeBack();
    if (action === 'reset') { P.bus.emit('local', {}); P.game.reset(); P.game.start(); lands = []; }
    if (action === 'reduce-midflight') P.settings.reducedMotion = true;
    for (let f = 0; f < 150; f++) step();
    ok(!B.anim.busy(), action + ' releases animation ownership');
    const reversed = action === 'undo' || action === 'reset';
    ok(B.pieces.pieceAt(reversed ? 'e1' : 'g1')?.userData.type === 'k', action + ' king square');
    ok(B.pieces.pieceAt(reversed ? 'h1' : 'f1')?.userData.type === 'r', action + ' rook square');
    if (action === 'reset') ok(!lands.length, 'reset has no late landing');
    cases++;
  }
  P.settings.reducedMotion = false; setup('w'); P.game.tryMove('e1', 'g1');
  for (let f = 0; f < 65; f++) step();
  B.view.renderer.render(B.view.scene, B.view.camera);
  return { passed, cases, screenshot: 'rook passes outside the back rank' };
})()
