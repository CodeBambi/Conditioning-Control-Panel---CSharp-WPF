const GAMES = [
  { id: 'instant-recall', name: 'Instant Recall', pitch: 'Catch the room lying.', instruction: 'Watch three pictures. One changes. Catch it.', color: '#dba4cb' },
  { id: 'composure', name: 'Composure', pitch: 'That should not stand. Make it stand.', instruction: 'Six pieces. One small platform. Make something stand.', color: '#f0ca8c' },
  { id: 'lost-and-found', name: 'Lost & Found', pitch: 'Find it. Peel it. See what is underneath.', instruction: 'Find the three pictures. Peel the room open.', color: '#e5acb1' },
  { id: 'echo', name: 'Echo', pitch: 'The room starts the tune. You finish it.', instruction: 'Four pads. Follow the light. Make the tune grow.', color: '#a9d8cc' },
];
const $ = id => document.getElementById(id);
let selected, selectedModule, instance, context, paused = false, finished = false, generation = 0, take = 0;
$('motion').checked = matchMedia('(prefers-reduced-motion: reduce)').matches;
for (const [index, game] of GAMES.entries()) {
  const button = document.createElement('button'); button.className = 'game-card'; button.type = 'button';
  button.style.setProperty('--accent', game.color);
  button.innerHTML = `<span class="number">0${index + 1}</span><i class="shape" aria-hidden="true"></i><h2>${game.name}</h2><p>${game.pitch}</p>`;
  button.addEventListener('click', () => select(game)); $('games').append(button);
}
function dispose() {
  generation++;
  instance?.destroy(); instance = null; context = null;
  paused = false; finished = false; $('pause').textContent = 'Pause'; $('pause').disabled = true;
  $('motion').disabled = false; $('stage').replaceChildren(); $('error').hidden = true;
}
async function select(game) {
  dispose(); selected = game;
  $('lobby').hidden = true; $('play').hidden = false; $('result').hidden = true; $('launch').hidden = false;
  $('title').textContent = game.name; $('instruction').textContent = game.instruction;
  selectedModule = null; $('start').disabled = true; $('start').textContent = 'Opening...';
  const token = generation;
  try {
    const module = await import(`./games/${game.id}/feel.js`);
    if (token !== generation) return;
    selectedModule = module; $('start').disabled = false; $('start').textContent = 'Start'; $('start').focus();
  } catch (error) {
    if (token !== generation) return;
    $('error').hidden = false; $('error').textContent = 'This room could not open. Return to all games and try again.'; console.error(error);
  }
  history.replaceState(null, '', `#${game.id}`);
}
async function start() {
  if (!selectedModule) return;
  dispose(); $('start').disabled = true; $('start').textContent = 'Opening...'; $('result').hidden = true;
  const token = generation;
  try {
    const module = selectedModule;
    if (token !== generation) return;
    context = {
      root: $('stage'), audioAudible: $('sound').checked,
      motion: { motionLevel: $('motion').checked ? 0 : 2, reducedMotion: $('motion').checked },
      lexicon: (_key, fallback) => fallback,
      platform: { isTouch: matchMedia('(pointer:coarse)').matches, hasHaptics: false, host: 'feel-preview' },
      endClass(report) {
        if (token !== generation || finished) return;
        finished = true; $('result').hidden = false; $('pause').disabled = true;
        $('result').dataset.composite = String(report.metrics?.composite ?? '');
      },
    };
    instance = module.create(context);
    $('launch').hidden = true; $('motion').disabled = true; $('pause').disabled = false;
    await instance.start({ seed: `feel-${selected.id}-${++take}`, tier: 1, devSkipHowto: true });
    if (document.hidden) setPaused(true);
  } catch (error) {
    instance?.destroy(); instance = null;
    $('launch').hidden = false; $('start').disabled = false; $('start').textContent = 'Try again';
    $('error').hidden = false; $('error').textContent = 'This room could not open. Return to all games, or try again.';
    $('pause').disabled = true; $('motion').disabled = false;
    console.error(error);
  }
}
function setPaused(value) {
  if (!instance || finished) return;
  paused = value;
  if (value) instance.pause(); else instance.resume();
  $('pause').textContent = value ? 'Resume' : 'Pause';
}
$('start').addEventListener('click', start);
$('again').addEventListener('click', start);
$('pause').addEventListener('click', () => setPaused(!paused));
$('sound').addEventListener('change', () => {
  if (context) context.audioAudible = $('sound').checked;
  instance?.setAudioAudible?.($('sound').checked);
});
$('back').addEventListener('click', () => {
  dispose(); $('play').hidden = true; $('lobby').hidden = false; history.replaceState(null, '', location.pathname);
});
document.addEventListener('visibilitychange', () => { if (document.hidden) setPaused(true); });
window.addEventListener('pagehide', dispose);
const linked = GAMES.find(game => `#${game.id}` === location.hash);
if (linked) select(linked);
