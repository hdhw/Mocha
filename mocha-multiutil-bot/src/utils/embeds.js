const { EmbedBuilder } = require('discord.js');

const COLORS = {
  primary: 0x8b5cf6,
  success: 0x22c55e,
  warning: 0xf59e0b,
  danger: 0xef4444,
  info: 0x3b82f6,
  neutral: 0x64748b,
};

function baseEmbed(title, description, color = COLORS.primary) {
  const embed = new EmbedBuilder().setColor(color).setTimestamp();
  if (title) embed.setTitle(title);
  if (description) embed.setDescription(description);
  return embed;
}

function successEmbed(description) {
  return baseEmbed('✅ Success', description, COLORS.success);
}

function errorEmbed(description) {
  return baseEmbed('❌ Error', description, COLORS.danger);
}

module.exports = { COLORS, baseEmbed, successEmbed, errorEmbed };
