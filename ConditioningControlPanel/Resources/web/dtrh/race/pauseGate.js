/** Pause owners release only their own hold. A batch handoff never briefly resumes. */
export function createPauseGate(onChange = () => {}) {
  const held = new Set();
  return {
    get paused() { return held.size > 0; },
    update(changes) {
      const before = held.size > 0;
      for (const [reason, on] of Object.entries(changes)) {
        if (on) held.add(reason); else held.delete(reason);
      }
      const after = held.size > 0;
      if (before !== after) onChange(after);
      return after;
    },
  };
}
