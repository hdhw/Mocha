const {
  Events,
  MessageFlags,
  PermissionFlagsBits,
  ChannelType,
} = require('discord.js');
const db = require('../database');
const { handleTicketButton } = require('../services/tickets');
const { processAutomod } = require('../services/automod');
const { sendLog } = require('../utils/logger');
const { COLORS } = require('../utils/embeds');
const { truncate } = require('../utils/text');

const cooldowns = new Map();

function renderMemberMessage(template, member) {
  return template
    .replaceAll('{user}', `<@${member.id}>`)
    .replaceAll('{tag}', member.user.tag)
    .replaceAll('{server}', member.guild.name)
    .replaceAll('{count}', String(member.guild.memberCount));
}

function channelLabel(channel) {
  return channel ? `${channel.name || 'Unknown'} (${channel.id})` : 'None';
}

async function handleInteraction(interaction, client) {
  if (interaction.isButton() && interaction.customId.startsWith('ticket:')) {
    try {
      await handleTicketButton(interaction);
    } catch (error) {
      console.error('Ticket interaction failed:', error);
      const payload = { content: '❌ The ticket action failed. Check my channel and role permissions.', flags: MessageFlags.Ephemeral };
      if (interaction.deferred || interaction.replied) await interaction.followUp(payload).catch(() => null);
      else await interaction.reply(payload).catch(() => null);
    }
    return;
  }

  if (!interaction.isChatInputCommand()) return;
  const command = client.commands.get(interaction.commandName);
  if (!command) return;

  if (command.guildOnly && !interaction.inGuild()) {
    await interaction.reply({ content: '❌ This command only works inside a server.', flags: MessageFlags.Ephemeral });
    return;
  }

  const cooldownSeconds = command.cooldown ?? 2;
  const key = `${interaction.user.id}:${interaction.commandName}`;
  const expiresAt = cooldowns.get(key) || 0;
  if (expiresAt > Date.now()) {
    await interaction.reply({
      content: `⏳ Try that command again <t:${Math.ceil(expiresAt / 1000)}:R>.`,
      flags: MessageFlags.Ephemeral,
    });
    return;
  }
  cooldowns.set(key, Date.now() + cooldownSeconds * 1000);
  setTimeout(() => cooldowns.delete(key), cooldownSeconds * 1000).unref();

  db.incrementCommand(interaction.guildId, interaction.commandName);
  if (interaction.guild) {
    sendLog(interaction.guild, {
      title: '⌨️ Slash Command Used',
      color: COLORS.neutral,
      fields: [
        { name: 'User', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Command', value: `/${interaction.commandName}` },
        { name: 'Channel', value: `${interaction.channel || 'Unknown'} (${interaction.channelId || 'DM'})` },
      ],
    }).catch(() => null);
  }

  try {
    await command.execute(interaction, client);
  } catch (error) {
    console.error(`Command /${interaction.commandName} failed:`, error);
    const payload = {
      content: '❌ That command crashed. Check the bot console and confirm my permissions/role position.',
      flags: MessageFlags.Ephemeral,
    };
    if (interaction.deferred || interaction.replied) await interaction.followUp(payload).catch(() => null);
    else await interaction.reply(payload).catch(() => null);
  }
}

async function handleMessageCreate(message) {
  if (!message.guild || message.author.bot) return;

  const oldAfk = db.clearAfk(message.guildId, message.author.id);
  if (oldAfk) {
    const reply = await message.reply({ content: `👋 Welcome back! I removed your AFK status after <t:${Math.floor(oldAfk.since / 1000)}:R>.`, allowedMentions: { repliedUser: false } }).catch(() => null);
    if (reply) setTimeout(() => reply.delete().catch(() => null), 7_000).unref();
  }

  const mentionedAfk = [];
  for (const user of message.mentions.users.values()) {
    if (user.id === message.author.id || user.bot) continue;
    const afk = db.getAfk(message.guildId, user.id);
    if (afk) mentionedAfk.push(`${user.tag} is AFK: **${truncate(afk.reason, 200)}** (since <t:${Math.floor(afk.since / 1000)}:R>)`);
    if (mentionedAfk.length >= 3) break;
  }
  if (mentionedAfk.length > 0) {
    await message.reply({ content: `💤 ${mentionedAfk.join('\n')}`, allowedMentions: { repliedUser: false, parse: [] } }).catch(() => null);
  }

  await processAutomod(message);
}

async function handleMemberAdd(member) {
  const config = db.getGuildConfig(member.guild.id);
  if (config.autoroleId) {
    const role = member.guild.roles.cache.get(config.autoroleId);
    if (role && member.guild.members.me?.permissions.has(PermissionFlagsBits.ManageRoles)
      && member.guild.members.me.roles.highest.comparePositionTo(role) > 0) {
      await member.roles.add(role, 'Configured autorole').catch(() => null);
    }
  }

  if (config.welcomeChannelId) {
    const channel = await member.guild.channels.fetch(config.welcomeChannelId).catch(() => null);
    if (channel?.isTextBased()) {
      await channel.send({
        content: renderMemberMessage(config.welcomeMessage, member),
        allowedMentions: { users: [member.id], parse: [] },
      }).catch(() => null);
    }
  }

  await sendLog(member.guild, {
    title: '📥 Member Joined',
    color: COLORS.success,
    thumbnail: member.user.displayAvatarURL(),
    fields: [
      { name: 'Member', value: `${member.user.tag} (${member.id})` },
      { name: 'Account created', value: `<t:${Math.floor(member.user.createdTimestamp / 1000)}:F>` },
      { name: 'Member count', value: String(member.guild.memberCount) },
    ],
  });
}

async function handleMemberRemove(member) {
  const config = db.getGuildConfig(member.guild.id);
  if (config.goodbyeChannelId) {
    const channel = await member.guild.channels.fetch(config.goodbyeChannelId).catch(() => null);
    if (channel?.isTextBased()) {
      await channel.send({
        content: renderMemberMessage(config.goodbyeMessage, member),
        allowedMentions: { parse: [] },
      }).catch(() => null);
    }
  }

  const roles = member.roles?.cache
    ?.filter((role) => role.id !== member.guild.id)
    .sort((a, b) => b.position - a.position)
    .map((role) => role.name)
    .join(', ') || 'Unknown';
  await sendLog(member.guild, {
    title: '📤 Member Left',
    color: COLORS.warning,
    thumbnail: member.user.displayAvatarURL(),
    fields: [
      { name: 'Member', value: `${member.user.tag} (${member.id})` },
      { name: 'Joined', value: member.joinedTimestamp ? `<t:${Math.floor(member.joinedTimestamp / 1000)}:F>` : 'Unknown' },
      { name: 'Roles', value: roles },
      { name: 'Member count', value: String(member.guild.memberCount) },
    ],
  });
}

async function handleMemberUpdate(oldMember, newMember) {
  const fields = [];
  if (oldMember.nickname !== newMember.nickname) {
    fields.push({ name: 'Nickname', value: `${oldMember.nickname || 'None'} → ${newMember.nickname || 'None'}` });
  }
  const addedRoles = newMember.roles.cache.filter((role) => !oldMember.roles.cache.has(role.id));
  const removedRoles = oldMember.roles.cache.filter((role) => !newMember.roles.cache.has(role.id));
  if (addedRoles.size) fields.push({ name: 'Roles added', value: addedRoles.map((role) => `${role.name} (${role.id})`).join(', ') });
  if (removedRoles.size) fields.push({ name: 'Roles removed', value: removedRoles.map((role) => `${role.name} (${role.id})`).join(', ') });
  if (oldMember.communicationDisabledUntilTimestamp !== newMember.communicationDisabledUntilTimestamp) {
    fields.push({
      name: 'Timeout',
      value: newMember.communicationDisabledUntilTimestamp
        ? `Until <t:${Math.floor(newMember.communicationDisabledUntilTimestamp / 1000)}:F>`
        : 'Removed',
    });
  }
  if (fields.length === 0) return;
  fields.unshift({ name: 'Member', value: `${newMember.user.tag} (${newMember.id})` });
  await sendLog(newMember.guild, { title: '👤 Member Updated', color: COLORS.info, fields });
}

async function handleMessageDelete(message) {
  if (!message.guild || message.author?.id === message.client.user.id) return;
  const attachments = message.attachments?.map((attachment) => attachment.url).join('\n') || 'None';
  await sendLog(message.guild, {
    title: '🗑️ Message Deleted',
    color: COLORS.danger,
    thumbnail: message.author?.displayAvatarURL?.(),
    fields: [
      { name: 'Author', value: message.author ? `${message.author.tag} (${message.author.id})` : 'Unknown' },
      { name: 'Channel', value: `${message.channel} (${message.channelId})` },
      { name: 'Content', value: message.content || '[Content unavailable or embed-only message]' },
      { name: 'Attachments', value: attachments },
      { name: 'Message ID', value: message.id },
    ],
  });
}

async function handleMessageUpdate(oldMessage, newMessage) {
  if (!newMessage.guild || newMessage.author?.bot) return;
  if (oldMessage.partial) await oldMessage.fetch().catch(() => null);
  if (newMessage.partial) await newMessage.fetch().catch(() => null);
  if (oldMessage.content === newMessage.content) return;
  await sendLog(newMessage.guild, {
    title: '✏️ Message Edited',
    color: COLORS.info,
    thumbnail: newMessage.author?.displayAvatarURL?.(),
    fields: [
      { name: 'Author', value: newMessage.author ? `${newMessage.author.tag} (${newMessage.author.id})` : 'Unknown' },
      { name: 'Channel', value: `${newMessage.channel} (${newMessage.channelId})` },
      { name: 'Before', value: oldMessage.content || '[Unavailable]' },
      { name: 'After', value: newMessage.content || '[Empty]' },
      { name: 'Jump', value: newMessage.url || 'Unavailable' },
    ],
  });
}

async function handleChannelUpdate(oldChannel, newChannel) {
  if (!newChannel.guild) return;
  const fields = [{ name: 'Channel', value: `${newChannel.name || 'Unknown'} (${newChannel.id})` }];
  if (oldChannel.name !== newChannel.name) fields.push({ name: 'Name', value: `${oldChannel.name} → ${newChannel.name}` });
  if ('topic' in oldChannel && oldChannel.topic !== newChannel.topic) fields.push({ name: 'Topic', value: `${oldChannel.topic || 'None'} → ${newChannel.topic || 'None'}` });
  if ('nsfw' in oldChannel && oldChannel.nsfw !== newChannel.nsfw) fields.push({ name: 'NSFW', value: `${oldChannel.nsfw} → ${newChannel.nsfw}` });
  if ('rateLimitPerUser' in oldChannel && oldChannel.rateLimitPerUser !== newChannel.rateLimitPerUser) {
    fields.push({ name: 'Slowmode', value: `${oldChannel.rateLimitPerUser}s → ${newChannel.rateLimitPerUser}s` });
  }
  if (oldChannel.parentId !== newChannel.parentId) fields.push({ name: 'Category', value: `${oldChannel.parent?.name || 'None'} → ${newChannel.parent?.name || 'None'}` });
  if (fields.length === 1) return;
  await sendLog(newChannel.guild, { title: '🛠️ Channel Updated', color: COLORS.info, fields });
}

async function handleRoleUpdate(oldRole, newRole) {
  const fields = [{ name: 'Role', value: `${newRole.name} (${newRole.id})` }];
  if (oldRole.name !== newRole.name) fields.push({ name: 'Name', value: `${oldRole.name} → ${newRole.name}` });
  if (oldRole.hexColor !== newRole.hexColor) fields.push({ name: 'Color', value: `${oldRole.hexColor} → ${newRole.hexColor}` });
  if (oldRole.mentionable !== newRole.mentionable) fields.push({ name: 'Mentionable', value: `${oldRole.mentionable} → ${newRole.mentionable}` });
  if (oldRole.hoist !== newRole.hoist) fields.push({ name: 'Displayed separately', value: `${oldRole.hoist} → ${newRole.hoist}` });
  if (oldRole.permissions.bitfield !== newRole.permissions.bitfield) {
    fields.push({ name: 'Permissions before', value: oldRole.permissions.toArray().join(', ') || 'None' });
    fields.push({ name: 'Permissions after', value: newRole.permissions.toArray().join(', ') || 'None' });
  }
  if (fields.length === 1) return;
  await sendLog(newRole.guild, { title: '🛠️ Role Updated', color: COLORS.info, fields });
}

async function handleVoiceUpdate(oldState, newState) {
  const fields = [{ name: 'Member', value: `${newState.member?.user.tag || oldState.member?.user.tag} (${newState.id})` }];
  let title = '🎙️ Voice State Updated';
  if (oldState.channelId !== newState.channelId) {
    if (!oldState.channelId) title = '🔊 Joined Voice';
    else if (!newState.channelId) title = '🔇 Left Voice';
    else title = '🔀 Moved Voice Channels';
    fields.push({ name: 'From', value: channelLabel(oldState.channel) });
    fields.push({ name: 'To', value: channelLabel(newState.channel) });
  }
  if (oldState.serverMute !== newState.serverMute) fields.push({ name: 'Server mute', value: `${oldState.serverMute} → ${newState.serverMute}` });
  if (oldState.serverDeaf !== newState.serverDeaf) fields.push({ name: 'Server deafen', value: `${oldState.serverDeaf} → ${newState.serverDeaf}` });
  if (oldState.streaming !== newState.streaming) fields.push({ name: 'Streaming', value: `${oldState.streaming} → ${newState.streaming}` });
  if (oldState.selfVideo !== newState.selfVideo) fields.push({ name: 'Camera', value: `${oldState.selfVideo} → ${newState.selfVideo}` });
  if (fields.length === 1) return;
  await sendLog(newState.guild, { title, color: COLORS.info, fields });
}

function registerEvents(client) {
  client.on(Events.InteractionCreate, (interaction) => handleInteraction(interaction, client));
  client.on(Events.MessageCreate, handleMessageCreate);
  client.on(Events.MessageDelete, handleMessageDelete);
  client.on(Events.MessageUpdate, handleMessageUpdate);
  client.on(Events.MessageBulkDelete, async (messages, channel) => {
    await sendLog(channel.guild, {
      title: '🧹 Messages Bulk Deleted', color: COLORS.danger,
      fields: [
        { name: 'Channel', value: `${channel} (${channel.id})` },
        { name: 'Count', value: String(messages.size) },
      ],
    });
  });
  client.on(Events.GuildMemberAdd, handleMemberAdd);
  client.on(Events.GuildMemberRemove, handleMemberRemove);
  client.on(Events.GuildMemberUpdate, handleMemberUpdate);
  client.on(Events.GuildBanAdd, (ban) => sendLog(ban.guild, {
    title: '🔨 Ban Added', color: COLORS.danger,
    fields: [
      { name: 'User', value: `${ban.user.tag} (${ban.user.id})` },
      { name: 'Reason', value: ban.reason || 'No reason available' },
    ],
  }));
  client.on(Events.GuildBanRemove, (ban) => sendLog(ban.guild, {
    title: '🕊️ Ban Removed', color: COLORS.success,
    fields: [{ name: 'User', value: `${ban.user.tag} (${ban.user.id})` }],
  }));
  client.on(Events.ChannelCreate, (channel) => {
    if (!channel.guild) return;
    sendLog(channel.guild, {
      title: '➕ Channel Created', color: COLORS.success,
      fields: [
        { name: 'Channel', value: `${channel.name || 'Unknown'} (${channel.id})` },
        { name: 'Type', value: ChannelType[channel.type] || String(channel.type) },
      ],
    });
  });
  client.on(Events.ChannelDelete, (channel) => {
    if (!channel.guild) return;
    sendLog(channel.guild, {
      title: '➖ Channel Deleted', color: COLORS.danger,
      fields: [
        { name: 'Channel', value: `${channel.name || 'Unknown'} (${channel.id})` },
        { name: 'Type', value: ChannelType[channel.type] || String(channel.type) },
      ],
    });
  });
  client.on(Events.ChannelUpdate, handleChannelUpdate);
  client.on(Events.GuildRoleCreate, (role) => sendLog(role.guild, {
    title: '➕ Role Created', color: COLORS.success,
    fields: [{ name: 'Role', value: `${role.name} (${role.id})` }],
  }));
  client.on(Events.GuildRoleDelete, (role) => sendLog(role.guild, {
    title: '➖ Role Deleted', color: COLORS.danger,
    fields: [{ name: 'Role', value: `${role.name} (${role.id})` }],
  }));
  client.on(Events.GuildRoleUpdate, handleRoleUpdate);
  client.on(Events.VoiceStateUpdate, handleVoiceUpdate);
  client.on(Events.Error, (error) => console.error('Discord client error:', error));
  client.on(Events.Warn, (warning) => console.warn('Discord warning:', warning));
}

module.exports = { registerEvents };
