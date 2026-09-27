// A rematch is another accepted challenge, never a local replay of a finished id.
export async function requestRematch(lobby, previous) {
  if (!lobby?.challenge || !previous?.id || !previous.opponent?.id) throw new Error('left');
  const timeControl = previous.state?.time_control || {
    initial_ms: previous.clockMs, increment_ms: 0,
  };
  const match = await lobby.challenge(previous.opponent.id, {
    previousMatchId: previous.id,
    timeControl,
    color: previous.side === 'w' ? 'b' : previous.side === 'b' ? 'w' : 'random',
  });
  if (!match?.id || match.id === previous.id || !['w', 'b'].includes(match.side)) {
    throw new Error('not ready');
  }
  return match;
}
