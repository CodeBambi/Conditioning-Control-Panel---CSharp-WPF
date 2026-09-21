/* ============================================================================
 * stations/breakout/twists/shells-render.js - the twist's own drawing.
 * Twist: Shells (act 4). Pale ghost shells over the field, under the HUD.
 *
 * A shell is the game's own ghost ball, hollow and much bigger: the same greys
 * (#9a9a9a body, #d0d0d0 rim), the same OLD SELF lettering. It is see-through on
 * purpose, because it is not a wall: the ball goes straight through it.
 * Touches read as the rim closing up, arc by arc, so a shell about to pop is
 * obvious at a glance.
 *
 * REDUCED MOTION: the shells hold their pose, nothing shimmers and nothing
 * breathes. The rim arcs and the lettering still read.
 * ==========================================================================*/

const TAU = Math.PI * 2;
/** The ghost ball's own greys (render.js draws a ghost ball in exactly these). */
const BODY = '154,154,154', RIM = '208,208,208';

/** Field coordinates, over everything but the HUD. */
export function over(x, snap, t) {
  const st = snap && snap.shells;
  if (!st || !st.list || !st.list.length) return;
  if (snap.state === 'grey') return;                 // GREY is payload-free: the old selves are out of sight
  const still = !!snap.reduced;
  const time = Number.isFinite(t) ? t : 0;

  for (const sh of st.list) {
    if (!sh || sh.hp <= 0) continue;
    const breath = still ? 0 : Math.sin(time * 1.15 + sh.seed * TAU);
    const r = sh.r * (1 + 0.035 * breath);
    const gone = 3 - sh.hp;                          // touches taken: 0, 1, 2
    const a = 0.34 + 0.1 * gone;                     // it gets a little more solid as it gives way

    x.save();
    x.translate(sh.x, sh.y);

    /* The hollow: a faint wash, never a plate. Nothing here blocks a ball. */
    x.globalAlpha = 0.10 + 0.03 * gone;
    x.fillStyle = 'rgb(' + BODY + ')';
    x.beginPath(); x.arc(0, 0, r, 0, TAU); x.fill();

    /* The rim, drawn as three arcs: one closes for every touch it has taken. */
    x.globalAlpha = a;
    x.strokeStyle = 'rgb(' + RIM + ')';
    x.lineWidth = 2;
    for (let i = 0; i < 3; i++) {
      const from = -Math.PI / 2 + i * (TAU / 3) + (still ? 0 : 0.12 * breath);
      const span = TAU / 3 * (i < 3 - gone ? 0.74 : 0.97);
      x.beginPath(); x.arc(0, 0, r, from, from + span); x.stroke();
    }

    /* The inner ring only arrives once it has been touched: the shell tightening. */
    if (gone) {
      x.globalAlpha = 0.2 + 0.14 * gone;
      x.lineWidth = 1;
      x.beginPath(); x.arc(0, 0, r * (0.82 - 0.1 * gone), 0, TAU); x.stroke();
    }

    /* Its name, small, the way the ghost ball wears it. */
    x.globalAlpha = 0.42 + 0.12 * gone;
    x.fillStyle = 'rgb(' + RIM + ')';
    x.font = '700 9px system-ui, sans-serif';
    x.textAlign = 'center'; x.textBaseline = 'middle';
    x.fillText('OLD SELF', 0, 0);

    x.restore();
  }
}

export default { over };
