function truncate(value, max = 1024) {
  const text = String(value ?? 'None');
  if (text.length <= max) return text;
  return `${text.slice(0, Math.max(0, max - 3))}...`;
}

function codeBlock(value, language = '') {
  const cleaned = String(value ?? '').replaceAll('```', 'ˋˋˋ');
  return `\`\`\`${language}\n${cleaned}\n\`\`\``;
}

function escapeMarkdown(value) {
  return String(value ?? '').replace(/[\\`*_{}\[\]()#+\-.!|>~]/g, '\\$&');
}

function formatDuration(ms) {
  if (!Number.isFinite(ms) || ms < 0) return 'unknown';
  const totalSeconds = Math.floor(ms / 1000);
  const days = Math.floor(totalSeconds / 86400);
  const hours = Math.floor((totalSeconds % 86400) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const parts = [];
  if (days) parts.push(`${days}d`);
  if (hours) parts.push(`${hours}h`);
  if (minutes) parts.push(`${minutes}m`);
  if (seconds || parts.length === 0) parts.push(`${seconds}s`);
  return parts.join(' ');
}

module.exports = { truncate, codeBlock, escapeMarkdown, formatDuration };
