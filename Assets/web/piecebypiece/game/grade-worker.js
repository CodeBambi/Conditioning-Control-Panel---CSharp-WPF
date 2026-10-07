// The IQ grader off the main thread (game/iq.js): one move in, { best, cp } out.
import { gradeMove } from './search.js';
self.onmessage = ({ data }) => {
  try { self.postMessage({ id: data.id, ...(gradeMove(data) || { error: true }) }); }
  catch { self.postMessage({ id: data.id, error: true }); }
};
