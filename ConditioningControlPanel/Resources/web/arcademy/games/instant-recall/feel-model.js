// Original pictures stay deliberately distinct: recognition, never a loading test.
export const PICTURES = [
  ['Moon', '#c8baff', '<path d="M67 18A32 32 0 1 0 82 72A34 34 0 0 1 67 18Z"/>'],
  ['Flower', '#ffb9d4', '<path d="M50 37C15 0 0 45 34 50C0 60 28 97 45 66C51 104 91 79 66 55C103 51 81 10 59 35C67 0 26 4 50 37Z"/><circle cx="50" cy="50" r="10" fill="#fff4d1"/>'],
  ['Mountain', '#a5e7dc', '<path d="M10 80L42 22L64 58L73 43L94 80Z"/><path d="M31 42L42 22L54 43L42 37Z" fill="#fff"/>'],
  ['Key', '#ffd781', '<circle cx="37" cy="36" r="19" fill="none" stroke="currentColor" stroke-width="12"/><path d="M46 49L77 80M65 68L76 57M76 79L87 68" fill="none" stroke="currentColor" stroke-width="10"/>'],
  ['Fish', '#93d5ff', '<path d="M15 50Q42 12 72 42L90 29V71L72 58Q42 88 15 50Z"/><circle cx="35" cy="45" r="4" fill="#252838"/>'],
  ['Leaf', '#bde49b', '<path d="M18 78Q9 19 84 14Q92 81 18 78Z"/><path d="M18 82L69 31" fill="none" stroke="#46664d" stroke-width="5"/>'],
  ['Bolt', '#ffe093', '<path d="M55 9L21 57H45L37 93L80 39H55Z"/>'],
  ['Cup', '#e7b6ff', '<path d="M19 24H68V58Q65 82 43 82Q21 79 19 58Z"/><path d="M68 32Q95 28 87 51Q83 61 68 60" fill="none" stroke="currentColor" stroke-width="9"/>'],
  ['Eye', '#99e8d5', '<path d="M8 50Q49 5 92 50Q51 95 8 50Z"/><circle cx="50" cy="50" r="18" fill="#2b3548"/><circle cx="55" cy="44" r="6" fill="#fff"/>'],
  ['Planet', '#f5bc92', '<circle cx="50" cy="50" r="25"/><ellipse cx="50" cy="50" rx="43" ry="12" transform="rotate(-25 50 50)" fill="none" stroke="currentColor" stroke-width="7"/>'],
  ['Heart', '#ffa7b7', '<path d="M50 84L17 51C-4 21 32 4 50 29C68 4 104 21 83 51Z"/>'],
  ['Crown', '#f2de96', '<path d="M16 28L35 44L50 17L66 44L86 28L77 79H24Z"/><path d="M26 67H75" stroke="#b48b46" stroke-width="5"/>'],
];

export function makeRound(random = Math.random, number = 0) {
  const ids = PICTURES.map((_, i) => i);
  for (let i = ids.length - 1; i > 0; i--) {
    const j = Math.min(i, Math.max(0, Math.floor(random() * (i + 1))));
    [ids[i], ids[j]] = [ids[j], ids[i]];
  }
  const original = ids.slice(0, 3);
  const changed = Math.min(2, Math.max(0, Math.floor(random() * 3)));
  const replay = [...original];
  replay[changed] = ids[3];
  return { original, replay, changed, number };
}

export function picture(id, label = true) {
  const [name, color, paths] = PICTURES[id];
  return `<svg viewBox="0 0 100 100" role="img" aria-label="${name}" style="color:${color};fill:currentColor">${paths}</svg>${label ? `<span>${name}</span>` : ''}`;
}

export function report(correct, total = 6) {
  return { metrics: { composite: Math.max(0, Math.min(1, correct / total)), correct, rounds: total }, hardGates: {}, flavorXp: 0, feelVersion: 2 };
}
