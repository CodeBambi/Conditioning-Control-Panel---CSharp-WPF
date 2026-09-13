/* ============================================================================
 * backroom/room/screens.js - the four wall screens.
 *
 * Pictures come from the media feed (CONTRACT 5): the room asks once for a deal
 * under the station id `room` and puts the player's own GIFs on the walls,
 * playing (gif.js, ImageDecoder), or as a still first frame where the page
 * cannot decode them. Anything the feed marks
 * `fallback`, anything that will not load or is not CORS-readable, and a feed
 * that times out all mean the default house art with its captions.
 *
 * The preview's file picker is gone: nothing here reads a local file.
 * Pictures turn every 18 s with a 1.6 s cross-fade; a still room holds them.
 *
 * GIF COST CAP: only pictures on a screen inside the camera frustum advance,
 * each at most MAX_FPS (gif.js) into a texture of at most MAX_EDGE px, with at
 * most MAX_DECODES_PER_FRAME new decodes started per rendered frame. A held
 * room (a station open) renders nothing, so nothing advances. Still (reduced
 * motion, Calm, Motion still) shows the first frame.
 * ==========================================================================*/

import * as T from 'three';
import { labelTexture } from './fixtures.js';
import { animatedSource } from './gif.js';

export const TURN_S = 18;
const FADE_FROM = 16.4;
const SCREEN_ASPECT = 2.12 / 1.22;
const MAX_PICTURES = 8;
const MAX_DECODES_PER_FRAME = 1;

function material(first, caption) {
  return new T.ShaderMaterial({
    uniforms: { a: { value: first }, b: { value: first }, ratioA: { value: 1.6 }, ratioB: { value: 1.6 }, mixAmount: { value: 0 },
      titleA: { value: caption }, titleB: { value: caption }, showTitles: { value: 1 }, screen: { value: SCREEN_ASPECT } },
    vertexShader: 'varying vec2 v;void main(){v=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}',
    fragmentShader: `uniform sampler2D a,b,titleA,titleB;uniform float showTitles,ratioA,ratioB,mixAmount,screen;varying vec2 v;
vec4 pic(sampler2D tex,float ratio){vec2 uv=v-.5;if(ratio>screen)uv.y*=ratio/screen;else uv.x*=screen/ratio;uv+=.5;
if(min(uv.x,uv.y)<0.||max(uv.x,uv.y)>1.)return vec4(.022,.012,.033,1.);return texture2D(tex,uv);}
void main(){gl_FragColor=mix(pic(a,ratioA),pic(b,ratioB),mixAmount);
if(showTitles>.5&&v.y>.82){vec2 tv=vec2(v.x,(v.y-.82)/.18);gl_FragColor=mix(texture2D(titleA,tv),texture2D(titleB,tv),mixAmount);}
#include <colorspace_fragment>
}`,
  });
}

/** CONTRACT 5: media urls are ccp.assets (the player's folders) or this page's own origin, nothing else. */
function allowed(url) {
  if (typeof url !== 'string') return false;
  try { const u = new URL(url, location.href); return (u.protocol === 'https:' && u.host === 'ccp.assets') || u.origin === location.origin; } catch (e) { return false; }
}

function prep(t) { t.colorSpace = T.SRGBColorSpace; t.flipY = false; return t; }

/**
 * @param {Object} o  { meshes, ads:[{url, caption}], media: () => Promise<media frame>, log }
 */
export async function createScreens(o) {
  const loader = new T.TextureLoader();
  loader.setCrossOrigin('anonymous');
  const still = (texture) => ({ texture, tick: null, animated: false });
  const art = await Promise.all(o.ads.map((ad) => loader.loadAsync(ad.url).then(prep).catch(() => null)));
  const house = [], captions = [];
  art.forEach((t, i) => { if (t) { house.push(still(t)); captions.push(labelTexture(o.ads[i].caption)); } });
  if (!house.length) {
    const c = document.createElement('canvas'); c.width = c.height = 4;
    house.push(still(prep(new T.CanvasTexture(c)))); captions.push(labelTexture(''));
  }
  let gallery = house, custom = false, epoch = 0, decodes = 0;
  for (const m of o.meshes) m.material = material(house[0].texture, captions[0]);
  const frustum = new T.Frustum(), viewProj = new T.Matrix4();
  const due = new Set();

  /** @param t ambient seconds  @param camera the room camera  @param isStill hold first frames */
  function update(t, camera, isStill) {
    const since = Math.max(0, t - epoch), n = Math.floor(since / TURN_S);
    const blend = T.MathUtils.smoothstep(since % TURN_S, FADE_FROM, TURN_S);
    if (camera) frustum.setFromProjectionMatrix(viewProj.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse));
    due.clear();
    o.meshes.forEach((mesh, i) => {
      const u = mesh.material.uniforms;
      const a = gallery[(n + i) % gallery.length], b = gallery[(n + i + 1) % gallery.length];
      u.a.value = a.texture; u.b.value = b.texture;
      u.ratioA.value = a.texture.image.width / a.texture.image.height; u.ratioB.value = b.texture.image.width / b.texture.image.height;
      u.mixAmount.value = blend;
      u.showTitles.value = custom ? 0 : 1;
      u.titleA.value = captions[(n + i) % captions.length]; u.titleB.value = captions[(n + i + 1) % captions.length];
      if (camera && (a.tick || b.tick) && frustum.intersectsObject(mesh)) { due.add(a); if (blend > 0) due.add(b); }
    });
    let started = 0;
    for (const src of due) {
      if (started >= MAX_DECODES_PER_FRAME) break;
      if (src.tick && src.tick(performance.now(), isStill)) { started++; decodes++; }
    }
  }

  /** Ask the feed; swap in the player's pictures when at least one loads. Never throws. */
  async function deal(now) {
    let frame = null;
    try { frame = await o.media(); } catch (e) { frame = null; }
    const gifs = frame && Array.isArray(frame.gifs) ? frame.gifs : [];
    const urls = gifs.filter((g) => g && g.src !== 'fallback' && allowed(g.url)).slice(0, MAX_PICTURES).map((g) => g.url);
    const loaded = (await Promise.all(urls.map(async (u) => {
      const playing = await animatedSource(u);
      if (playing) return playing;
      const t = await loader.loadAsync(u).then(prep).catch(() => null);
      return t && t.image && t.image.width > 0 ? still(t) : null;
    }))).filter(Boolean);
    if (!loaded.length) { if (urls.length && o.log) o.log('wall pictures: none readable, house art stays'); return 0; }
    gallery = loaded; custom = true; epoch = now();
    return loaded.length;
  }

  return {
    update, deal,
    get pictures() { return custom ? gallery.length : 0; },
    /** Test seam: how many pictures can play, frames decoded so far, decodes started. */
    get animation() { return { animated: gallery.filter((g) => g.animated).length, frames: gallery.reduce((s, g) => s + (g.frames || 0), 0), decodes }; },
  };
}
