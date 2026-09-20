import test from 'node:test';
import assert from 'node:assert/strict';
import { createMusic, getMusic } from './__phone-music.js';

function rig({ patched = true, pendingResume = false, failConnect = false, native = false, factory = createMusic, storage = null } = {}) {
  const audios = [], contexts = []; let release;
  class Events {
    listeners = new Map();
    addEventListener(name, fn) { this.listeners.set(name, fn); }
    removeEventListener(name) { this.listeners.delete(name); }
    emit(name, event = {}) { this.listeners.get(name)?.(event); }
  }
  class Audio extends Events {
    dataset = {}; volume = 1; paused = true; plays = 0; loads = 0; src = '';
    constructor() { super(); audios.push(this); }
    getAttribute(name) { return this[name]; }
    removeAttribute(name) { this[name] = ''; }
    play() { this.plays++; this.paused = false; return Promise.resolve(); }
    pause() { this.paused = true; }
    load() { this.loads++; }
  }
  class Context {
    destination = {}; suspended = 0; closed = 0;
    constructor() { contexts.push(this); if (patched) this.__brMasterGain = {}; }
    createGain() { return this.gain = { gain: { value: 1 }, connect() {} }; }
    createMediaElementSource() { return { connect() { if (failConnect) throw Error('connection failed'); } }; }
    resume() { return pendingResume ? new Promise(resolve => { release = resolve; }) : Promise.resolve(); }
    suspend() { this.suspended++; return Promise.resolve(); }
    close() { this.closed++; return Promise.resolve(); }
  }
  const host = new Events(), page = new Events(); page.hidden = false;
  if (!native) host.AudioContext = Context;
  const api = factory({ AudioCtor: Audio, host, page, storage });
  return { api, host, page, audios, contexts, arm: () => host.emit('pointerdown'), release: () => release?.() };
}
const settle = async () => { for (let i = 0; i < 5; i++) await Promise.resolve(); };

test('soundtrack waits for a gesture and applies music gain exactly once under master', async () => {
  const r = rig(); assert.equal(r.audios[0].src, ''); assert.equal(r.audios[0].plays, 0);
  r.arm(); await settle();
  assert.equal(r.audios[0].volume, 1); assert.equal(r.contexts[0].gain.gain.value, .15);
  r.api.setVolume(.2, .4); assert.equal(r.contexts[0].gain.gain.value, .2);
  r.api.dispose();
});

test('unpatched WebAudio and native fallback each apply master once', async () => {
  const web = rig({ patched: false }); web.arm(); await settle();
  assert.equal(web.contexts[0].gain.gain.value, .15 * .8); assert.equal(web.audios[0].volume, 1);
  const native = rig({ native: true }); native.arm(); await settle();
  assert.equal(native.audios[0].volume, .15 * .8);
  web.api.dispose(); native.api.dispose();
});

test('play starts inside gesture and a pending context resume cannot undo hiding', async () => {
  const r = rig({ pendingResume: true }); r.arm();
  assert.equal(r.audios[0].plays, 1, 'play is called before resume resolves');
  r.page.hidden = true; r.page.emit('visibilitychange'); r.release(); await settle();
  assert.equal(r.audios[0].paused, true); assert.ok(r.contexts[0].suspended > 0);
  r.api.dispose();
});

test('back-forward cache pause resumes the same song and permanent disposal is final', async () => {
  const r = rig(); r.arm(); await settle(); const url = r.audios[0].src;
  r.host.emit('pagehide', { persisted: true }); assert.equal(r.audios[0].paused, true);
  r.host.emit('pageshow', { persisted: true }); await settle();
  assert.equal(r.audios[0].src, url); assert.equal(r.audios[0].paused, false);
  r.host.emit('pagehide', { persisted: false }); const plays = r.audios[0].plays;
  r.host.emit('pageshow', { persisted: true }); r.arm(); r.api.setVolume(.8); await settle();
  assert.equal(r.audios[0].plays, plays); assert.equal(r.audios[0].src, '');
});

test('hiding cancels a failed-track retry without starting another download', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const r = rig(); r.arm(); await settle(); const url = r.audios[0].src;
  r.audios[0].emit('error'); r.page.hidden = true; r.page.emit('visibilitychange');
  t.mock.timers.tick(1000); await settle(); assert.equal(r.audios[0].src, url);
  r.page.hidden = false; r.page.emit('visibilitychange'); await settle();
  assert.notEqual(r.audios[0].src, url); r.api.dispose();
});

test('failed WebAudio capture replaces the trapped element for native playback', async () => {
  const r = rig({ failConnect: true }); r.arm(); await settle();
  assert.equal(r.audios.length, 2); assert.equal(r.audios[0].src, '');
  assert.equal(r.audios[1].paused, false); assert.equal(r.audios[1].volume, .15 * .8);
  assert.equal(r.contexts[0].closed, 1); r.arm(); await settle(); assert.equal(r.audios.length, 2); r.api.dispose();
});


test('desktop host suspension holds music across page visibility changes', async () => {
  const r = rig(); r.arm(); await settle();
  r.api.suspend(true); assert.equal(r.audios[0].paused, true);
  r.page.emit('visibilitychange'); r.arm(); await settle(); assert.equal(r.audios[0].paused, true);
  r.api.suspend(false); await settle(); assert.equal(r.audios[0].paused, false);
  r.api.dispose(); assert.equal(r.api.disposed, true);
});

test('phone and desktop share one instance and persisted volume across room visits', () => {
  const values = new Map([['br.music.v1', '.27']]);
  const storage = { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value) };
  const r = rig({ factory: getMusic, storage });
  assert.equal(r.api.volume, .27); assert.equal(getMusic(), r.api);
  r.api.setVolume(.39); assert.equal(values.get('br.music.v1'), '0.39'); r.api.dispose();
  const reopened = rig({ factory: getMusic, storage }); assert.equal(reopened.api.volume, .39);
  assert.notEqual(reopened.api, r.api); reopened.api.dispose();
});

test('song URLs resolve beside the shared assets for desktop virtual hosts and browser copies', async () => {
  const r = rig(); r.arm(); await settle();
  assert.match(r.audios[0].src, /\/Resources\/web\/backroom\/music\/[^/]+\.mp3$/);
  assert.equal(r.audios[0].src.includes('/shared/music/'), false); r.api.dispose();
});
