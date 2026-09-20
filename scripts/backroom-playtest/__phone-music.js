// The phone shell and desktop room share one soundtrack and saved volume.
const path = typeof window === 'undefined'
  ? '../../ConditioningControlPanel/Resources/web/backroom/shared/sound/music.js'
  : '/backroom/shared/sound/music.js';
export const { createRotation, createMusic, getMusic } = await import(path);
