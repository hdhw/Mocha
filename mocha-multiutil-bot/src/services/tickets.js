const {
  ChannelType,
  PermissionFlagsBits,
  MessageFlags,
  ButtonBuilder,
  ButtonStyle,
  ActionRowBuilder,
} = require('discord.js');
const db = require('../database');
const { baseEmbed, COLORS } = require('../utils/embeds');
const { sendLog } = require('../utils/logger');

function cleanChannelName(value) {
  const cleaned = value.toLowerCase().replace(/[^a-z0-9-]/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '');
  return (cleaned || 'member').slice(0, 70);
}

function getTicketOwnerId(channel) {
  const match = channel.topic?.match(/(?:^|\s)ticket-owner:(\d{17,20})(?:\s|$)/);
  return match?.[1] || null;
}

function hasTicketAccess(interaction, config, ownerId) {
  if (interaction.user.id === ownerId) return true;
  if (interaction.memberPermissions?.has(PermissionFlagsBits.Administrator)) return true;
  return Boolean(config.supportRoleId && interaction.member?.roles?.cache?.has(config.supportRoleId));
}

async function handleCreate(interaction) {
  const config = db.getGuildConfig(interaction.guildId);
  const category = config.ticketCategoryId
    ? await interaction.guild.channels.fetch(config.ticketCategoryId).catch(() => null)
    : null;
  if (!category || category.type !== ChannelType.GuildCategory) {
    return interaction.reply({
      content: '❌ The ticket category is missing. An administrator needs to run `/config ticket-category`.',
      flags: MessageFlags.Ephemeral,
    });
  }

  const existing = interaction.guild.channels.cache.find(
    (channel) => channel.type === ChannelType.GuildText && getTicketOwnerId(channel) === interaction.user.id && !channel.topic?.includes('ticket-closed:true'),
  );
  if (existing) {
    return interaction.reply({ content: `You already have an open ticket: ${existing}`, flags: MessageFlags.Ephemeral });
  }

  await interaction.deferReply({ flags: MessageFlags.Ephemeral });
  const permissionOverwrites = [
    {
      id: interaction.guild.roles.everyone.id,
      deny: [PermissionFlagsBits.ViewChannel],
    },
    {
      id: interaction.user.id,
      allow: [
        PermissionFlagsBits.ViewChannel,
        PermissionFlagsBits.SendMessages,
        PermissionFlagsBits.ReadMessageHistory,
        PermissionFlagsBits.AttachFiles,
        PermissionFlagsBits.EmbedLinks,
      ],
    },
    {
      id: interaction.client.user.id,
      allow: [
        PermissionFlagsBits.ViewChannel,
        PermissionFlagsBits.SendMessages,
        PermissionFlagsBits.ReadMessageHistory,
        PermissionFlagsBits.ManageChannels,
      ],
    },
  ];

  if (config.supportRoleId && interaction.guild.roles.cache.has(config.supportRoleId)) {
    permissionOverwrites.push({
      id: config.supportRoleId,
      allow: [
        PermissionFlagsBits.ViewChannel,
        PermissionFlagsBits.SendMessages,
        PermissionFlagsBits.ReadMessageHistory,
        PermissionFlagsBits.ManageMessages,
      ],
    });
  }

  const channel = await interaction.guild.channels.create({
    name: `ticket-${cleanChannelName(interaction.user.username)}`,
    type: ChannelType.GuildText,
    parent: category.id,
    topic: `ticket-owner:${interaction.user.id} ticket-closed:false`,
    permissionOverwrites,
    reason: `Ticket opened by ${interaction.user.tag}`,
  });

  const row = new ActionRowBuilder().addComponents(
    new ButtonBuilder().setCustomId('ticket:close').setLabel('Close Ticket').setEmoji('🔒').setStyle(ButtonStyle.Danger),
  );
  const supportMention = config.supportRoleId ? `<@&${config.supportRoleId}>` : 'Server administrators';
  await channel.send({
    content: `${interaction.user} ${supportMention}`,
    embeds: [baseEmbed(
      '🎫 Support Ticket Opened',
      'Explain what you need help with and include any useful screenshots or message links. Staff will respond here.',
      COLORS.primary,
    ).setFooter({ text: `Ticket owner: ${interaction.user.id}` })],
    components: [row],
    allowedMentions: { users: [interaction.user.id], roles: config.supportRoleId ? [config.supportRoleId] : [] },
  });

  await sendLog(interaction.guild, {
    title: '🎫 Ticket Opened',
    color: COLORS.success,
    fields: [
      { name: 'Member', value: `${interaction.user.tag} (${interaction.user.id})` },
      { name: 'Channel', value: `${channel} (${channel.id})` },
    ],
  });
  return interaction.editReply(`✅ Your private ticket is ready: ${channel}`);
}

async function handleClose(interaction) {
  const ownerId = getTicketOwnerId(interaction.channel);
  const config = db.getGuildConfig(interaction.guildId);
  if (!ownerId) return interaction.reply({ content: '❌ This is not a ticket channel.', flags: MessageFlags.Ephemeral });
  if (!hasTicketAccess(interaction, config, ownerId)) {
    return interaction.reply({ content: '❌ You do not have permission to close this ticket.', flags: MessageFlags.Ephemeral });
  }
  if (interaction.channel.topic?.includes('ticket-closed:true')) {
    return interaction.reply({ content: 'This ticket is already closed.', flags: MessageFlags.Ephemeral });
  }

  await interaction.deferReply({ flags: MessageFlags.Ephemeral });
  await interaction.channel.permissionOverwrites.edit(ownerId, { SendMessages: false, ViewChannel: false });
  await interaction.channel.setTopic(`ticket-owner:${ownerId} ticket-closed:true`, `Closed by ${interaction.user.tag}`);
  if (!interaction.channel.name.startsWith('closed-')) {
    await interaction.channel.setName(`closed-${interaction.channel.name}`.slice(0, 100), `Closed by ${interaction.user.tag}`);
  }

  const row = new ActionRowBuilder().addComponents(
    new ButtonBuilder().setCustomId('ticket:delete').setLabel('Delete Ticket').setEmoji('🗑️').setStyle(ButtonStyle.Danger),
  );
  await interaction.channel.send({
    embeds: [baseEmbed('🔒 Ticket Closed', `Closed by ${interaction.user}. Staff can delete the channel using the button below.`, COLORS.warning)],
    components: [row],
    allowedMentions: { users: [] },
  });
  await sendLog(interaction.guild, {
    title: '🔒 Ticket Closed',
    color: COLORS.warning,
    fields: [
      { name: 'Closed by', value: `${interaction.user.tag} (${interaction.user.id})` },
      { name: 'Owner', value: `<@${ownerId}> (${ownerId})` },
      { name: 'Channel', value: `${interaction.channel.name} (${interaction.channelId})` },
    ],
  });
  return interaction.editReply('✅ Ticket closed.');
}

async function handleDelete(interaction) {
  const ownerId = getTicketOwnerId(interaction.channel);
  const config = db.getGuildConfig(interaction.guildId);
  const isStaff = interaction.memberPermissions?.has(PermissionFlagsBits.Administrator)
    || (config.supportRoleId && interaction.member?.roles?.cache?.has(config.supportRoleId));
  if (!ownerId || !isStaff) {
    return interaction.reply({ content: '❌ Only ticket staff can delete this channel.', flags: MessageFlags.Ephemeral });
  }
  await interaction.reply({ content: '🗑️ Deleting this ticket channel…', flags: MessageFlags.Ephemeral });
  await sendLog(interaction.guild, {
    title: '🗑️ Ticket Deleted',
    color: COLORS.danger,
    fields: [
      { name: 'Deleted by', value: `${interaction.user.tag} (${interaction.user.id})` },
      { name: 'Owner', value: `<@${ownerId}> (${ownerId})` },
      { name: 'Channel', value: `${interaction.channel.name} (${interaction.channelId})` },
    ],
  });
  await interaction.channel.delete(`Ticket deleted by ${interaction.user.tag}`);
}

async function handleTicketButton(interaction) {
  if (interaction.customId === 'ticket:create') return handleCreate(interaction);
  if (interaction.customId === 'ticket:close') return handleClose(interaction);
  if (interaction.customId === 'ticket:delete') return handleDelete(interaction);
  return null;
}

module.exports = { handleTicketButton, getTicketOwnerId };
