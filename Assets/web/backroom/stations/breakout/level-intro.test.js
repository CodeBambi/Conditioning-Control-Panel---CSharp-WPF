import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createLevelIntro, LEVEL_TITLES } from './level-intro.js';
test('each title is cached, expires before launch, and reduced motion avoids transforms', () => {
  const old = globalThis.document, calls = []; let created = 0;
  const g = new Proxy({}, { get: (_, k) => (...args) => calls.push([k, ...args]) });
  globalThis.document = { createElement() { created++; return { getContext: () => g }; } };
  try {
    const intro = createLevelIntro();
    for (let i = 0; i < LEVEL_TITLES.length; i++) {
      intro.draw(g,i,.4,1280,720,true); intro.draw(g,i,.5,1280,720,true);
    }
    assert.equal(created,8);
    assert.equal(calls.filter(c=>c[0]==='rotate'||c[0]==='scale').length,0);
    const count=calls.length; intro.draw(g,0,1.15,1280,720,false); assert.equal(calls.length,count);
  } finally { globalThis.document=old; }
});
