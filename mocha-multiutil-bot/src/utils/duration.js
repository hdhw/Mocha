const UNIT_MS = {
  s: 1_000,
  sec: 1_000,
  secs: 1_000,
  second: 1_000,
  seconds: 1_000,
  m: 60_000,
  min: 60_000,
  mins: 60_000,
  minute: 60_000,
  minutes: 60_000,
  h: 3_600_000,
  hr: 3_600_000,
  hrs: 3_600_000,
  hour: 3_600_000,
  hours: 3_600_000,
  d: 86_400_000,
  day: 86_400_000,
  days: 86_400_000,
  w: 604_800_000,
  week: 604_800_000,
  weeks: 604_800_000,
};

function parseDuration(input) {
  if (!input || typeof input !== 'string') return null;
  const normalized = input.trim().toLowerCase();
  const matches = [...normalized.matchAll(/(\d+(?:\.\d+)?)\s*([a-z]+)/g)];
  if (matches.length === 0) return null;

  let consumed = '';
  let total = 0;
  for (const match of matches) {
    const [, rawAmount, rawUnit] = match;
    const multiplier = UNIT_MS[rawUnit];
    if (!multiplier) return null;
    total += Number(rawAmount) * multiplier;
    consumed += match[0];
  }

  const compactInput = normalized.replaceAll(/\s+/g, '');
  const compactConsumed = consumed.replaceAll(/\s+/g, '');
  if (compactInput !== compactConsumed || !Number.isFinite(total) || total <= 0) return null;
  return Math.round(total);
}

module.exports = { parseDuration };
