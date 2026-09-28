// Five queen attacks, drawn without replacement and without boundary repeats.
export const QUEEN_ACTS = Object.freeze([
  { name: 'triple-bash', motion: 'k', hit: 1.40, end: 3.45, sound: 'headbutt' },
  { name: 'royal-double-slap', motion: 'b', hit: 1.20, end: 3.20, sound: 'whip' },
  { name: 'royal-backflip', motion: 'n', hit: .90, end: 2.36, sound: 'stomp' },
  { name: 'royal-fling', motion: 'r', hit: 1.30, end: 3.30, sound: 'launch' },
  { name: 'spring-stomp', motion: 'p', hit: .72, end: 2.18, sound: 'stomp' },
].map(Object.freeze));

export function createQueenDeck(random = Math.random) {
  let bag = [], last;
  return () => {
    if (!bag.length) {
      bag = [...QUEEN_ACTS];
      for (let i = bag.length - 1; i > 0; i--) {
        const j = Math.min(i, Math.floor(Math.max(0, random()) * (i + 1)));
        [bag[i], bag[j]] = [bag[j], bag[i]];
      }
      if (bag.at(-1) === last) [bag[0], bag[bag.length - 1]] = [bag.at(-1), bag[0]];
    }
    return last = bag.pop();
  };
}

const smooth = t => { t = Math.max(0, Math.min(1, t)); return t*t*t*(t*(t*6-15)+10); };
const phase = (t,a,b) => smooth((t-a)/(b-a));
// Two quick compressions spring back before the longer final windup.
export function queenBash(t, height, victimHeight) {
  let reach = 0, charge = 0, press = 0, wobble = 0;
  for (const [hit, start, strength] of [[.48,.12,.45],[.83,.57,.55],[1.40,.94,1]]) {
    const age = Math.max(0,t-hit), final = strength === 1;
    const recovery = 1-phase(age,final ? .26 : .035,final ? .65 : .19);
    const contact = phase(t,hit-(final ? .14 : .10),hit)*recovery;
    charge += phase(t,start,hit-.16)*(1-phase(t,hit-.14,hit));
    reach += contact;
    press += contact*(final ? .88*phase(age,0,.11) : .20);
    wobble += Math.sin(age*32)*Math.exp(-age*13)*(1-phase(age,.20,.30));
  }
  const stretch = .13*charge+.17*reach+.04*wobble;
  return { reach, charge, stretch, drop:(victimHeight*(reach-press)+.025*reach-height*(1+stretch)*reach) };
}
