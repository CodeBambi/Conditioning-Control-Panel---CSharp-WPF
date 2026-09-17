/* ============================================================================
 * backroom/room/screens.js - the room's four wall screens, plus whatever Room
 * Service has switched on (two more packs of wall screens, four and six, and
 * the ceiling projection); scene.js hands them all in as one mesh list.
 *
 * Pictures come from the media feed (CONTRACT 5): the room asks once for a deal
 * under the station id `room` and puts the player's own GIFs on the walls,
 * playing (gif.js, ImageDecoder), or as a still first frame where the page
 * cannot decode them. Anything the feed marks
 * `fallback`, anything that will not load or is not CORS-readable, and a feed
 * that times out all mean the default house art with its captions.
 *
 * The preview's file picker is gone: nothing here reads a local file.
 * Pictures turn every 9 s through a brief silent static handoff; a still room holds them.
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
import { screenTransition } from './screen-transition.js';

export const TURN_S = 9;
const SCREEN_ASPECT = 2.12 / 1.22;
const MAX_PICTURES = 8;
/* Owner, 2026-09-16: "the screens do not change the gif/vid only the ceiling does". They did change - every
 * 18 s, which is past the length of a look - and they barely played: ONE decode could start per 83 ms across
 * every screen on the wall, so four pictures on screen at once advanced at three frames a second each. The
 * per-source clock in gif-decode.js is the real cap (one decode in flight, never above MAX_FPS, only inside
 * the frustum, nothing at all while a station holds the room), so this budget is a per-FRAME batch now and
 * not a second global throttle on top of it. The ceiling projector keeps its own faster turn (4.5 s). */
const MAX_DECODES_PER_FRAME = 4;

function material(first, caption) {
  return new T.ShaderMaterial({
    uniforms: { glitch: {value:0}, glitchFrame: {value:0}, grid: {value:0}, a: { value: first }, b: { value: first }, ratioA: { value: 1.6 }, ratioB: { value: 1.6 }, mixAmount: { value: 0 },
      ax: {value:first}, ay: {value:first}, bx: {value:first}, by: {value:first},
      ratioAx: {value:1}, ratioAy: {value:1}, ratioBx: {value:1}, ratioBy: {value:1}, panelsA: {value:1}, panelsB: {value:1},
      titleA: { value: caption }, titleB: { value: caption }, showTitles: { value: 1 }, screen: { value: SCREEN_ASPECT }, cover: { value: 0 } },
    vertexShader: 'varying vec2 v;void main(){v=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}',
    fragmentShader: `uniform sampler2D a,b,ax,ay,bx,by,titleA,titleB;
uniform float glitch,glitchFrame,grid,showTitles,ratioA,ratioB,ratioAx,ratioAy,ratioBx,ratioBy,panelsA,panelsB,mixAmount,screen,cover;varying vec2 v;
float noise(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
vec4 pic(sampler2D tex,float ratio,vec2 point,float aspect,float fill){vec2 uv=point-.5;uv.x+=(noise(vec2(floor(point.y*28.),glitchFrame))-.5)*.09*glitch;if(fill>.5){if(ratio>aspect)uv.x*=aspect/ratio;else uv.y*=ratio/aspect;}else{if(ratio>aspect)uv.y*=ratio/aspect;else uv.x*=aspect/ratio;}uv+=.5;
if(min(uv.x,uv.y)<0.||max(uv.x,uv.y)>1.)return vec4(.022,.012,.033,1.);return texture2D(tex,uv);}
vec4 mosaic(sampler2D mainTex,sampler2D leftTex,sampler2D rightTex,float r,float rl,float rr,float panels){
if(panels<1.5)return pic(mainTex,r,v,screen,cover);
float col=min(panels-1.,floor(v.x*panels));vec2 uv=vec2(fract(v.x*panels),v.y);
if(uv.x<.006||uv.x>.994)return vec4(.035,.012,.05,1.);
if(col<.5)return pic(leftTex,rl,uv,screen/panels,1.);
if(col<1.5)return pic(mainTex,r,uv,screen/panels,1.);
return pic(rightTex,rr,uv,screen/panels,1.);}
vec4 tiles(){
vec2 uv=fract(v*vec2(3.,2.));float tile=floor(v.x*3.)+3.*floor(v.y*2.);float aspect=screen*2./3.;
if(uv.x<.004||uv.y<.004)return vec4(.035,.012,.05,1.);
if(tile<.5)return pic(a,ratioA,uv,aspect,1.);
if(tile<1.5)return pic(ax,ratioAx,uv,aspect,1.);
if(tile<2.5)return pic(ay,ratioAy,uv,aspect,1.);
if(tile<3.5)return pic(b,ratioB,uv,aspect,1.);
if(tile<4.5)return pic(bx,ratioBx,uv,aspect,1.);
return pic(by,ratioBy,uv,aspect,1.);}
void main(){gl_FragColor=grid>.5?tiles():mix(mosaic(a,ax,ay,ratioA,ratioAx,ratioAy,panelsA),mosaic(b,bx,by,ratioB,ratioBx,ratioBy,panelsB),mixAmount);
if(showTitles>.5&&v.y>.82){vec2 tv=vec2(v.x,(v.y-.82)/.18);gl_FragColor=mix(texture2D(titleA,tv),texture2D(titleB,tv),mixAmount);}
// A low-contrast interference band, never a full-screen white flash.
float grain=noise(floor(v*vec2(280.,160.))+vec2(glitchFrame,glitchFrame*3.));
float scan=.5+.5*sin(v.y*460.+glitchFrame);
vec3 snow=mix(vec3(.055,.035,.08),vec3(.34,.27,.39),grain)*(.8+.2*scan);
gl_FragColor.rgb=mix(gl_FragColor.rgb,snow,glitch*.76);
#include <colorspace_fragment>
}`,
  });
}

/** CONTRACT 5: media urls are ccp.assets (the player's folders) or this page's own origin, nothing else. */
function allowed(url) {
  if (typeof url !== 'string') return false;
  try { const u = new URL(url, location.href); return (u.protocol === 'https:' && u.host === 'ccp.assets') || u.origin === location.origin; } catch (e) { return false; }
}

function prep(t) {
  const image=t.image, edge=Math.max(image?.width||0,image?.height||0);
  if(edge>512){
    const canvas=document.createElement('canvas');canvas.width=Math.max(1,Math.round(image.width*512/edge));canvas.height=Math.max(1,Math.round(image.height*512/edge));
    canvas.getContext('2d').drawImage(image,0,0,canvas.width,canvas.height);
    t.dispose();t=new T.CanvasTexture(canvas);
  }
  t.colorSpace = T.SRGBColorSpace; t.flipY = false; return t;
}

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
  let gallery = house, custom = false, epoch = 0, decodes = 0, disposed = false;
  for (const m of o.meshes) { m.material = material(house[0].texture, captions[0]); m.material.uniforms.screen.value=m.userData.screenAspect||SCREEN_ASPECT;m.material.uniforms.cover.value=m.userData.screenCover?1:0; }
  const frustum = new T.Frustum(), viewProj = new T.Matrix4();
  const due = new Set();
  let nextDecode = 0, cursor = 0, dealing = false, refreshAt = Infinity, sourceVersion = 0;
  const sourceChanged = () => { sourceVersion++; refreshAt = -Infinity; };
  window.addEventListener('br-media-changed', sourceChanged);
  const free = src => src.dispose ? src.dispose() : src.texture.dispose();

  /** @param t ambient seconds  @param camera the room camera  @param isStill hold first frames */
  function update(t, camera, isStill) {
    if ((!isStill || refreshAt === -Infinity) && !dealing && t >= refreshAt) { refreshAt = t + 72; void deal(() => t); }
    const since = Math.max(0, t - epoch);
    if (camera) frustum.setFromProjectionMatrix(viewProj.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse));
    due.clear();
    o.meshes.forEach((mesh, i) => {
      const u = mesh.material.uniforms;
      const turn=mesh.userData.screenTurn||TURN_S;
      const state=screenTransition(since,turn,isStill,gallery.length,i);
      const n=state.index,blend=state.mix;
      u.glitch.value=state.glitch;u.glitchFrame.value=state.frame;
      const grid=!!mesh.userData.screenGrid;u.grid.value=grid?1:0;
      const a = gallery[(n + i) % gallery.length], b = gallery[(n + i + (grid?3:1)) % gallery.length];
      u.a.value = a.texture; u.b.value = b.texture;
      u.ratioA.value = a.texture.image.width / a.texture.image.height; u.ratioB.value = b.texture.image.width / b.texture.image.height;
      const ratio = src => src.texture.image.width / src.texture.image.height;
      const panels = src => custom && !mesh.userData.screenCover && ratio(src) < u.screen.value * .8
        ? Math.min(gallery.length, 3, Math.max(2, Math.round(u.screen.value / ratio(src)))) : 1;
      const extrasA = [gallery[(n+i+1)%gallery.length],gallery[(n+i+2)%gallery.length]];
      const extrasB = [gallery[(n+i+(grid?4:2))%gallery.length],gallery[(n+i+(grid?5:3))%gallery.length]];
      u.panelsA.value=panels(a);u.panelsB.value=panels(b);
      for(const [name,src] of [['ax',extrasA[0]],['ay',extrasA[1]],['bx',extrasB[0]],['by',extrasB[1]]]) {
        u[name].value=src.texture;u['ratio'+name[0].toUpperCase()+name[1]].value=ratio(src);
      }
      u.mixAmount.value = blend;
      u.showTitles.value = custom || mesh.userData.screenNoTitles ? 0 : 1;
      u.titleA.value = captions[(n + i) % captions.length]; u.titleB.value = captions[(n + i + 1) % captions.length];
      let visible=true;for(let p=mesh;p;p=p.parent)if(!p.visible)visible=false;
      if (visible && camera && (a.tick || b.tick) && frustum.intersectsObject(mesh)) { due.add(a);
        if(grid){[...extrasA,b,...extrasB].forEach(src=>due.add(src));}
        if(u.panelsA.value>1)due.add(extrasA[0]);if(u.panelsA.value>2)due.add(extrasA[1]);
        if (blend > 0) { due.add(b);if(u.panelsB.value>1)due.add(extrasB[0]);if(u.panelsB.value>2)due.add(extrasB[1]); } }
    });
    const now=performance.now();
    if(now<nextDecode)return;   // one batch per rendered frame at most; each source still paces itself
    let started = 0;
    const ready=[...due];
    for (let i=0;i<ready.length;i++) {
      const src=ready[(cursor+i)%ready.length];
      if (started >= MAX_DECODES_PER_FRAME) break;
      if (src.tick && src.tick(now, isStill)) { started++; decodes++; cursor=(cursor+i+1)%ready.length; nextDecode=now+1000/60; }
    }
  }

  /** Ask the feed; swap in the player's pictures when at least one loads. Never throws. */
  async function deal(now) {
    if (disposed || dealing) return 0;
    dealing = true; const version = sourceVersion;
    try {
    let frame = null;
    try { frame = await o.media(); } catch (e) { frame = null; }
    const gifs = frame && Array.isArray(frame.gifs) ? frame.gifs : [];
    const urls = [...new Set(gifs.filter((g) => g && g.src !== 'fallback' && allowed(g.url)).map((g) => g.url))].slice(0, MAX_PICTURES);
    const results=new Array(urls.length);let next=0;
    await Promise.all([0,1].map(async()=>{while(next<urls.length&&!disposed){const index=next++,u=urls[index];results[index]=await (async()=>{
      const playing = await animatedSource(u);
      if (playing) return playing;
      const t = await loader.loadAsync(u).then(prep).catch(() => null);
      return t && t.image && t.image.width > 0 ? still(t) : null;
    })();}}));
    const loaded=results.filter(Boolean);
    if (disposed || version !== sourceVersion) { loaded.forEach(src => src.dispose ? src.dispose() : src.texture.dispose()); return 0; }
    if (!loaded.length) { if (urls.length && o.log) o.log('wall pictures: none readable, house art stays'); return 0; }
    const previous = custom ? gallery : [];
    gallery = loaded; custom = true; epoch = now();
    previous.forEach(free);
    return loaded.length;
    } finally { dealing = false; refreshAt = version !== sourceVersion ? -Infinity : now() + 72; }
  }

  return {
    update, deal,
    dispose() {
      disposed = true; window.removeEventListener('br-media-changed', sourceChanged);
      for (const src of new Set([...house, ...gallery])) src.dispose ? src.dispose() : src.texture.dispose();
      captions.forEach(t => t.dispose());
      // One transition ShaderMaterial per screen, made here, so it is freed here too.
      for (const mesh of o.meshes) if (mesh.material) mesh.material.dispose();
    },
    get pictures() { return custom ? gallery.length : 0; },
    /** Test seam: how many pictures can play, frames decoded so far, decodes started. */
    get animation() { return { animated: gallery.filter((g) => g.animated).length, frames: gallery.reduce((s, g) => s + (g.frames || 0), 0), decodes }; },
  };
}
