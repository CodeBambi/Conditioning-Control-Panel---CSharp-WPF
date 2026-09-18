/* ============================================================================
 * backroom/room/memorabilia-wall.js - what the eight polaroids advertise, pure.
 *
 * The Back Room's walls carry the shelf next door: every polaroid is one card off
 * the premium page (Models/ExclusiveFeature.cs), wearing that card's own art, that
 * card's own livery tier and a note in the vault's voice. No DOM, no canvas, no
 * three, so the node tests can read the wall without a browser.
 *
 * LAW VII, every visible word is a lexicon key with an English fallback. The keys
 * are `br_photo_*`; only `br_`-prefixed rows reach the page (BackRoomHostService
 * LexPrefix), which is why the shelf's own `tab_*` and `exclusives_*` keys are
 * mirrored here under br_ names instead of being read directly.
 *
 * THREE THINGS THE WALL IS NOT ALLOWED TO DO:
 *   - advertise the Back Room inside the Back Room,
 *   - repeat the three doors the ad frame already runs (Arcademy, DtRH, Focus Gaze),
 *   - surface a hidden feature (Piece by Piece).
 * memorabilia-wall.test.mjs fails the suite if any of those creeps back in.
 * ==========================================================================*/

const photo = name => new URL('./assets/memorabilia/' + name + '.jpg', import.meta.url).href;
const sign = name => new URL('./assets/memorabilia/' + name + '.png', import.meta.url).href;

/** The picture area of a 768-wide polaroid, i.e. the width art is drawn at. */
export const PICTURE_WIDTH = 732;

/**
 * The tier sign a livery tier wears, or null for a card that is not sold by tier.
 *
 * 1 = BASIC SUBJECT, 2 = PRIME SUBJECT: the store's two names, and the words are baked
 * into the art rather than drawn here. The numbers are the LIVERY tier off
 * ExclusiveFeature.Tier ("1 = gold BASIC SUBJECT, 2 = diamond PRIME SUBJECT"), NOT the
 * entitlement bars in Services/TierGate.cs, where "premium" means tier 1 and "Lab" means
 * tier 2 and the names lie about which is which. A polaroid states a price, never a right.
 */
export function badgeFor(tier) {
  if (tier === 2) return sign('tier-badge-prime');
  if (tier === 1) return sign('tier-badge-basic');
  return null;
}

/**
 * The vault cards on the wall, keyed by their ShowTab key so a record can be checked
 * against ExclusiveFeature.All by eye. `art` is the photograph's own aspect; the paper
 * follows the picture, so a portrait card simply hangs as a taller polaroid.
 *
 * Graded Intake carries `chipKey` instead of a tier: it is tier 0 on the shelf because the
 * weekly pass opens it for free accounts, and a tier badge there would be the wall telling
 * a small lie about the one door that is not sold by tier.
 */
export const VAULT_PHOTOS = Object.freeze({
  fyp: Object.freeze({
    art: 'feature-fyp', tier: 1,
    titleKey: 'br_photo_fyp_title', title: 'For You',
    captionKey: 'br_photo_fyp_note', caption: 'it watches what you linger on.\nyou will not find the bottom.',
  }),
  remotecontrol: Object.freeze({
    art: 'feature-remote-control', tier: 1,
    titleKey: 'br_photo_remote_title', title: 'Remote Control',
    captionKey: 'br_photo_remote_note', caption: 'the dials go to someone else.\nthey pick when you get them back.',
  }),
  bambitakeover: Object.freeze({
    art: 'feature-takeover', tier: 1,
    titleKey: 'br_photo_takeover_title', title: 'Takeover',
    captionKey: 'br_photo_takeover_note', caption: 'she takes the wheel.\nyou will not notice her taking it.',
  }),
  gradedintake: Object.freeze({
    art: 'feature-graded-intake', tier: 0, chipKey: 'br_photo_chip_pass', chip: 'WEEKLY PASS READY',
    titleKey: 'br_photo_intake_title', title: 'Graded Intake',
    captionKey: 'br_photo_intake_note', caption: 'one evaluation a week.\nthe grade follows you around.',
  }),
  justdrop: Object.freeze({
    art: 'feature-justdrop', tier: 2,
    titleKey: 'br_photo_justdrop_title', title: 'Just Drop',
    captionKey: 'br_photo_justdrop_note', caption: 'you order the session.\nit is built to your weak spots.',
  }),
  haptics: Object.freeze({
    art: 'feature-haptics', tier: 1,
    titleKey: 'br_photo_haptics_title', title: 'Haptics',
    captionKey: 'br_photo_haptics_note', caption: 'every toy on one pulse.\nthe room picks the rhythm.',
  }),
  blinktrainer: Object.freeze({
    art: 'feature-blink-trainer', tier: 1,
    titleKey: 'br_photo_blink_title', title: 'Blink Trainer',
    captionKey: 'br_photo_blink_note', caption: 'the webcam counts your blinks.\nhold them open and get paid.',
  }),
  awareness: Object.freeze({
    art: 'feature-awareness', tier: 1,
    titleKey: 'br_photo_awareness_title', title: 'Awareness',
    captionKey: 'br_photo_awareness_note', caption: 'she watches where you look.\nlook away and she says so.',
  }),
});

/**
 * The five walls, and which vault card hangs in each numbered slot. The grouping is the
 * argument the wall is making, not decoration:
 *
 *   entrance   the greeter by the door, at eye level: the endless feed, which is how most
 *              people arrive and why they stay longer than they meant to.
 *   southwest  the surrender wall, next to the way out: the two cards where somebody else
 *              drives. Reading them on the way past the door is the joke.
 *   cards      over the card table, where a turn of a card marks you: the weekly evaluation.
 *   parlour    the counter, where winnings become things: the two cards you buy something
 *              with, a session to order and a toy to feel.
 *   slots      high over the machines you cannot look away from: the two cards about eyes.
 *
 * Each numbered slot stays independently replaceable, so swapping one card is one edit.
 */
export const PHOTO_GROUPS = [
  { id: 'entrance', position: [-1.75, 1.9, 7.76], yaw: Math.PI, photos: ['fyp'] },
  { id: 'southwest', position: [-3.95, 1.95, 7.76], yaw: Math.PI, photos: ['bambitakeover', 'remotecontrol'] },
  { id: 'cards', position: [6.82, 3.35, -5.4], yaw: -Math.PI / 2, photos: ['gradedintake'] },
  { id: 'parlour', position: [6.82, 1.65, -6.8], yaw: -Math.PI / 2, photos: ['haptics', 'justdrop'] },
  { id: 'slots', position: [-6.82, 3.5, 4.9], yaw: Math.PI / 2, photos: ['blinktrainer', 'awareness'] },
];

/**
 * The eight polaroids as hangable records: position, lean and the two URLs the paper needs.
 * `size` is the paper before the picture lands; the draw path re-measures the height off the
 * photograph's own aspect, so a portrait card ends up taller than this without any authoring.
 */
export function wallPhotos() {
  return PHOTO_GROUPS.flatMap((group, g) => group.photos.map((key, i) => {
    const card = VAULT_PHOTOS[key];
    if (!card) throw new Error('memorabilia-wall: no vault card named ' + key);
    const offset = (i - (group.photos.length - 1) / 2) * .7;
    return {
      id: 'photo-' + g + '-' + i, group: group.id, feature: key, kind: 'polaroid',
      titleKey: card.titleKey, title: card.title,
      captionKey: card.captionKey, caption: card.caption,
      chipKey: card.chipKey || null, chip: card.chip || null,
      tier: card.tier, src: photo(card.art), badge: badgeFor(card.tier),
      position: [group.position[0] + Math.cos(group.yaw) * offset,
        group.position[1] + (i ? -.07 : .04), group.position[2] - Math.sin(group.yaw) * offset],
      yaw: group.yaw, tilt: i ? .06 : -.065, size: [.61, .43],
    };
  }));
}
