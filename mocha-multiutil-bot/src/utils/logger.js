const { ChannelType, EmbedBuilder } = require('discord.js');
const db = require('../database');
const { COLORS } = require('./embeds');
const { truncate } = require('./text');

async function getLogChannel(guild) {
  if (!guild) return null;
  const config = db.getGuildConfig(guild.id);
  if (!config.logChannelId) return null;

  const cached = guild.channels.cache.get(config.logChannelId);
  const channel = cached || await guild.channels.fetch(config.logChannelId).catch(() => null);
  if (!channel || !channel.isTextBased() || channel.type === ChannelType.DM) return null;
  return channel;
}

async function sendLog(guild, options) {
  const channel = await getLogChannel(guild);
  if (!channel) return false;

  const embed = new EmbedBuilder()
    .setColor(options.color ?? COLORS.neutral)
    .setTitle(truncate(options.title || 'Server Log', 256))
    .setTimestamp();

  if (options.description) embed.setDescription(truncate(options.description, 4096));
  if (options.thumbnail) embed.setThumbnail(options.thumbnail);
  if (options.footer) embed.setFooter({ text: truncate(options.footer, 2048) });
  if (Array.isArray(options.fields) && options.fields.length > 0) {
    embed.addFields(options.fields.slice(0, 25).map((field) => ({
      name: truncate(field.name, 256),
      value: truncate(field.value, 1024),
      inline: Boolean(field.inline),
    })));
  }

  await channel.send({ embeds: [embed], allowedMentions: { parse: [] } }).catch((error) => {
    console.error(`Could not send log in guild ${guild.id}:`, error.message);
  });
  return true;
}

module.exports = { getLogChannel, sendLog };
