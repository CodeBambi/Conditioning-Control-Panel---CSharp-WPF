/* ============================================================================
 * camera.js — rail travel + variable speed + falling-intro / exit state machine.
 *
 * The camera is locked to the tube centreline. It only ever advances an arc
 * length `s`; speed eases toward the active zone's target (slow zones crawl,
 * fast zones surge), so the SAME geometry reads very differently. States:
 *   idle    — parked at the mouth (before a run starts)
 *   intro   — the plunge: v starts high, FOV punches wide, then eases to zone
 *   run     — normal descent, speed driven by zones
 *   exiting — v ramps hard for the pull-out, then signals done
 * The black curtain / vignette DOM fades are driven from main.js off these.
 * ==========================================================================*/
import * as THREE from 'three';

const lerp = (a, b, t) => a + (b - a) * t;
const smooth = (x) => { const u = Math.min(1, Math.max(0, x)); return u * u * (3 - 2 * u); };

export class Rail {
  constructor(camera, tunnel, zones) {
    this.cam = camera;
    this.tunnel = tunnel;
    this.zones = zones;
    this.s = 0;
    this.v = 0;
    this.state = 'idle';
    this._t = 0;                 // seconds in the current transient state
    this.baseFov = 74;
    this.introFov = 104;
    this.introDur = 1.9;
    this.exitDur = 1.1;
    this._frame = { pos: new THREE.Vector3(), tan: new THREE.Vector3(), nor: new THREE.Vector3(), bin: new THREE.Vector3() };
    this._look = new THREE.Vector3();
    this.onExitDone = null;
    this.cam.fov = this.baseFov;
    this.cam.updateProjectionMatrix();
  }

  startIntro() { this.state = 'intro'; this._t = 0; this.s = 0; this.v = 62; this.cam.fov = this.introFov; this.cam.updateProjectionMatrix(); }
  startExit() { if (this.state === 'exiting') return; this.state = 'exiting'; this._t = 0; }

  /** Advance the rail; returns the current frame for downstream FX. */
  update(dt) {
    if (this.state === 'idle') {
      // gently hold at the mouth so the parked view still breathes
      this.v = lerp(this.v, 4, 1 - Math.exp(-1.2 * dt));
    } else if (this.state === 'intro') {
      this._t += dt;
      const k = smooth(this._t / this.introDur);
      const sp = this.zones.speed(this.s);
      this.v = lerp(62, sp.target, k);
      this.cam.fov = lerp(this.introFov, this.baseFov, k);
      this.cam.updateProjectionMatrix();
      if (this._t >= this.introDur) this.state = 'run';
    } else if (this.state === 'run') {
      const sp = this.zones.speed(this.s);
      this.v = lerp(this.v, sp.target, 1 - Math.exp(-sp.accel * dt));
    } else if (this.state === 'exiting') {
      this._t += dt;
      this.v = lerp(this.v, 120, 1 - Math.exp(-3.0 * dt));
      if (this._t >= this.exitDur) {
        this.state = 'done';
        if (this.onExitDone) this.onExitDone();
      }
    }

    this.s += this.v * dt;

    const f = this.tunnel.frameAt(this.s, this._frame);
    this.cam.position.copy(f.pos);
    this._look.copy(f.pos).addScaledVector(f.tan, 4.0);
    this.cam.up.copy(f.nor);
    this.cam.lookAt(this._look);
    return f;
  }

  get frame() { return this._frame; }
  get intensity01() {
    // normalized speed, handy for vignette / glow coupling
    return Math.min(1, Math.max(0, (this.v - 12) / 60));
  }
}
