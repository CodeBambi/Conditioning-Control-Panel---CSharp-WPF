/** Mutable preview assets can be stored, but must validate before reuse. */
export function withStaticCaching(config = {}) {
  const sources = ['/backroom/(.*)', '/(__phone-.*)', '/vendor/(.*)', '/arcademy/(.*)', '/dtrh/(.*)'];
  const policy = { key: 'Cache-Control', value: 'public, max-age=0, must-revalidate' };
  const headers = (config.headers || []).map(rule => sources.includes(rule.source)
    ? { ...rule, headers: [...(rule.headers || []).filter(header => header.key.toLowerCase() !== 'cache-control'), { ...policy }] }
    : rule);
  for (const source of sources) {
    if (!headers.some(rule => rule.source === source)) headers.push({ source, headers: [{ ...policy }] });
  }
  // API, ledger and private server headers are deliberately outside this list.
  return { ...config, headers };
}
