const { PermissionFlagsBits } = require('discord.js');
const db = require('../database');
const { sendLog } = require('../utils/logger');
const { COLORS } = require('../utils/embeds');

const spamTracker = new Map();
const spamLogCooldown = new Map();

function getSpamKey(message) {
  return `${message.guildId}:${message.author.id}`;
}

function detectViolation(message, settings) {
  const content = message.content || '';
  if (settings.invites && /(?:discord\.gg|discord(?:app)?\.com\/invite)\/[a-z0-9-]+/i.test(content)) {
    return 'Discord invite links are not allowed.';
  }
  if (settings.links && /https?:\/\/\S+/i.test(content)) {
    return 'External links are not allowed.';
  }
  if (settings.caps) {
    const letters = content.match(/[a-z]/gi) || [];
    const uppercase = content.match(/[A-Z]/g) || [];
    if (letters.length >= 12 && uppercase.length / letters.length >= 0.75) {
      return 'Please avoid excessive capital letters.';
    }
  }
  if (settings.spam) {
    const key = getSpamKey(message);
    const now = Date.now();
    const timestamps = (spamTracker.get(key) || []).filter((time) => now - time < 7_000);
    timestamps.push(now);
    spamTracker.set(key, timestamps);
    if (timestamps.length >= 6) return 'Please stop sending messages so quickly.';
  }
  return null;
}

async function processAutomod(message) {
  if (!message.guild || message.author.bot || !message.member) return false;
  if (message.member.permissions.has(PermissionFlagsBits.ManageMessages)) return false;
  const settings = db.getGuildConfig(message.guildId).automod;
  if (!Object.values(settings).some(Boolean)) return false;

  const reason = detectViolation(message, settings);
  if (!reason) return false;
  await message.delete().catch(() => null);
  const notice = await message.channel.send({
    content: `${message.author}, ${reason}`,
    allowedMentions: { users: [message.author.id] },
  }).catch(() => null);
  if (notice) setTimeout(() => notice.delete().catch(() => null), 5_000).unref();

  const key = getSpamKey(message);
  const lastLog = spamLogCooldown.get(key) || 0;
  if (Date.now() - lastLog > 5_000) {
    spamLogCooldown.set(key, Date.now());
    await sendLog(message.guild, {
      title: '🛡️ Automod Action',
      color: COLORS.warning,
      fields: [
        { name: 'Member', value: `${message.author.tag} (${message.author.id})` },
        { name: 'Channel', value: `${message.channel} (${message.channelId})` },
        { name: 'Reason', value: reason },
        { name: 'Deleted content', value: message.content || '[No text content]' },
      ],
    });
  }
  return true;
}

module.exports = { processAutomod };
