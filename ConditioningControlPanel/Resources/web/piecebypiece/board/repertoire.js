// Additional silhouettes share the existing contact, clearance and landing pipeline.
export const EXTRA_ACTS = Object.freeze({
  r: Object.freeze({ name: 'heavy-squash', motion: 'p', hit: .84, end: 2.30, sound: 'stomp' }),
  n: Object.freeze({ name: 'rear-kick', motion: 'kick', hit: .92, end: 2.65, sound: 'kick' }),
  b: Object.freeze({ name: 'low-sweep', motion: 'sweep', hit: 1.04, end: 2.85, sound: 'whip' }),
});

// Two-choice bags alternate after a random first draw. Local resets do not restart them.
export function createRepertoire(signatures, random = Math.random) {
  const last = {};
  return type => {
    const extra = EXTRA_ACTS[type];
    if (!extra) return signatures[type];
    const useExtra = last[type] === undefined ? random() < .5 : !last[type];
    last[type] = useExtra;
    return useExtra ? extra : signatures[type];
  };
}

const phase = (t,a,b) => { const x=Math.max(0,Math.min(1,(t-a)/(b-a))); return x*x*x*(x*(x*6-15)+10); };
export function extraPose(a,t,age,at,flex) {
  const {d,sideways:side,spec,height:h} = a;
  if (a.motion === 'kick') {
    const turn=phase(t,.04,.30)*(1-phase(age,.20,.43));
    const coil=phase(t,.28,.65)*(1-phase(t,spec.hit-.13,spec.hit));
    const kick=phase(t,spec.hit-.13,spec.hit)*(1-phase(age,.045,.23));
    const recoil=(1-Math.exp(-age*65))*Math.exp(-age*17);
    // Turn the head away, load the front, then present the broad rear of the base.
    at.addScaledVector(d,.18*kick-.06*coil-.075*recoil);
    at.y=.11*kick;
    flex.tip.copy(d).multiplyScalar(-h*(.24*coil+.46*kick-.12*recoil));
    flex.lag.copy(d).multiplyScalar(h*(.13*coil+.18*kick));
    flex.stretch=-.13*coil+.07*kick;
    flex.twist=.12*coil;
    const angle = Math.atan2(-d.x,-d.z)-(a.piece.userData.side==='b'?Math.PI:0);
    return { tilt:-.48*kick, spin:Math.atan2(Math.sin(angle),Math.cos(angle))*turn };
  }
  const ready=phase(t,.18,.55), release=1-phase(age,.10,.38);
  const sweep=-.78+1.56*phase(t,spec.hit-.13,spec.hit+.10)-.92*phase(age,.10,.27);
  const trail=-.78+1.56*phase(t-.045,spec.hit-.13,spec.hit+.10)-.92*phase(age,.145,.315);
  const reach=a.radius+a.vRadius+.12;
  flex.tip.copy(d).multiplyScalar(reach*ready*release).addScaledVector(side,sweep*ready*release);
  flex.lag.copy(side).multiplyScalar((trail-sweep)*.38*ready*release);
  flex.stretch=.12*ready*release;
  flex.drop=(a.contactHeight-h*1.12)*ready*release;
  flex.twist=-.26*sweep*ready*release;
  at.addScaledVector(side,-.06*(1-Math.exp(-age*65))*Math.exp(-age*15));
  return { tilt:0, spin:0 };
}
