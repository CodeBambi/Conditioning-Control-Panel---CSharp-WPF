import { chooseMove } from './search.js';
self.onmessage = ({ data }) => {
  try { self.postMessage({ id: data.id, move: chooseMove(data) }); }
  catch { self.postMessage({ id: data.id, error: true }); }
};
