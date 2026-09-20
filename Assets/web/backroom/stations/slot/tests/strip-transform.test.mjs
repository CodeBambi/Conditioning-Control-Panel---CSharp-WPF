import {test} from 'node:test';
import assert from 'node:assert/strict';
import {stripTransform} from '../strip-transform.js';

test('the authored payline centres every requested stop, including non-thirteen strips',()=>{
  for(const count of [9,13,17])for(let k=0;k<count;k++){
    const angle=((k+.5)/count-.5)*Math.PI*2, uv=stripTransform(angle,count);
    assert.ok(Math.abs(.5*uv.repeat+uv.offset-(k+.5)/count)<1e-12);
    assert.ok(Math.abs((1.4/13)*uv.repeat-1.4/count)<1e-12);
  }
});
