const { MessageFlags } = require('discord.js');
const { errorEmbed, successEmbed } = require('./embeds');

async function replyError(interaction, message) {
  const payload = { embeds: [errorEmbed(message)], flags: MessageFlags.Ephemeral };
  if (interaction.deferred || interaction.replied) return interaction.followUp(payload);
  return interaction.reply(payload);
}

async function replySuccess(interaction, message, ephemeral = true) {
  const payload = { embeds: [successEmbed(message)] };
  if (ephemeral) payload.flags = MessageFlags.Ephemeral;
  if (interaction.deferred || interaction.replied) return interaction.followUp(payload);
  return interaction.reply(payload);
}

module.exports = { replyError, replySuccess };
