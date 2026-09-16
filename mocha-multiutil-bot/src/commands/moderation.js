const {
  SlashCommandBuilder,
  PermissionFlagsBits,
  MessageFlags,
  ChannelType,
} = require('discord.js');
const db = require('../database');
const { parseDuration } = require('../utils/duration');
const { baseEmbed, COLORS } = require('../utils/embeds');
const { replyError, replySuccess } = require('../utils/replies');
const { canActOn, botCanActOn, canManageRole, botCanManageRole } = require('../utils/permissions');
const { sendLog } = require('../utils/logger');
const { truncate } = require('../utils/text');

const commands = [];

async function fetchExecutor(interaction) {
  return interaction.guild.members.fetch(interaction.user.id);
}

async function fetchTargetMember(interaction, option = 'member') {
  const user = interaction.options.getUser(option, true);
  return interaction.guild.members.fetch(user.id).catch(() => null);
}

async function validateTarget(interaction, target, action) {
  if (!target) {
    await replyError(interaction, 'That user is not currently in this server.');
    return false;
  }
  if (target.id === interaction.user.id) {
    await replyError(interaction, `You cannot ${action} yourself.`);
    return false;
  }
  if (target.id === interaction.client.user.id) {
    await replyError(interaction, `I am not going to ${action} myself 😭`);
    return false;
  }
  const executor = await fetchExecutor(interaction);
  if (!canActOn(executor, target)) {
    await replyError(interaction, `Your highest role must be above ${target}.`);
    return false;
  }
  if (!botCanActOn(interaction.guild, target)) {
    await replyError(interaction, `My highest role must be above ${target}.`);
    return false;
  }
  return true;
}

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('kick').setDescription('Kick a member from the server.')
    .setDefaultMemberPermissions(PermissionFlagsBits.KickMembers)
    .addUserOption((option) => option.setName('member').setDescription('Member to kick').setRequired(true))
    .addStringOption((option) => option.setName('reason').setDescription('Reason for the kick').setMaxLength(500)),
  guildOnly: true,
  async execute(interaction) {
    const target = await fetchTargetMember(interaction);
    if (!await validateTarget(interaction, target, 'kick')) return;
    if (!target.kickable) return replyError(interaction, 'Discord says I cannot kick that member. Check my role and permissions.');
    const reason = interaction.options.getString('reason') || 'No reason provided';
    await target.send(`You were kicked from **${interaction.guild.name}**.\nReason: ${reason}`).catch(() => null);
    await target.kick(`${reason} | Moderator: ${interaction.user.tag}`);
    await sendLog(interaction.guild, {
      title: '🥾 Member Kicked', color: COLORS.warning,
      fields: [
        { name: 'Member', value: `${target.user.tag} (${target.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Reason', value: reason },
      ],
    });
    return replySuccess(interaction, `Kicked **${target.user.tag}**.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('ban').setDescription('Ban a member from the server.')
    .setDefaultMemberPermissions(PermissionFlagsBits.BanMembers)
    .addUserOption((option) => option.setName('user').setDescription('User to ban').setRequired(true))
    .addStringOption((option) => option.setName('reason').setDescription('Reason for the ban').setMaxLength(500))
    .addIntegerOption((option) => option.setName('delete_days').setDescription('Delete recent messages from 0-7 days').setMinValue(0).setMaxValue(7)),
  guildOnly: true,
  async execute(interaction) {
    const user = interaction.options.getUser('user', true);
    const member = await interaction.guild.members.fetch(user.id).catch(() => null);
    if (member && !await validateTarget(interaction, member, 'ban')) return;
    if (user.id === interaction.user.id || user.id === interaction.client.user.id || user.id === interaction.guild.ownerId) {
      return replyError(interaction, 'That user cannot be banned by this command.');
    }
    const reason = interaction.options.getString('reason') || 'No reason provided';
    const days = interaction.options.getInteger('delete_days') || 0;
    await user.send(`You were banned from **${interaction.guild.name}**.\nReason: ${reason}`).catch(() => null);
    await interaction.guild.members.ban(user.id, {
      deleteMessageSeconds: days * 86_400,
      reason: `${reason} | Moderator: ${interaction.user.tag}`,
    });
    await sendLog(interaction.guild, {
      title: '🔨 User Banned', color: COLORS.danger,
      fields: [
        { name: 'User', value: `${user.tag} (${user.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Messages deleted', value: `${days} day(s)` },
        { name: 'Reason', value: reason },
      ],
    });
    return replySuccess(interaction, `Banned **${user.tag}**.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('unban').setDescription('Unban a user by ID.')
    .setDefaultMemberPermissions(PermissionFlagsBits.BanMembers)
    .addStringOption((option) => option.setName('user_id').setDescription('Discord user ID').setRequired(true))
    .addStringOption((option) => option.setName('reason').setDescription('Reason for the unban').setMaxLength(500)),
  guildOnly: true,
  async execute(interaction) {
    const userId = interaction.options.getString('user_id', true).trim();
    if (!/^\d{17,20}$/.test(userId)) return replyError(interaction, 'That does not look like a valid Discord user ID.');
    const ban = await interaction.guild.bans.fetch(userId).catch(() => null);
    if (!ban) return replyError(interaction, 'That user is not banned from this server.');
    const reason = interaction.options.getString('reason') || 'No reason provided';
    await interaction.guild.members.unban(userId, `${reason} | Moderator: ${interaction.user.tag}`);
    await sendLog(interaction.guild, {
      title: '🕊️ User Unbanned', color: COLORS.success,
      fields: [
        { name: 'User', value: `${ban.user.tag} (${userId})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Reason', value: reason },
      ],
    });
    return replySuccess(interaction, `Unbanned **${ban.user.tag}**.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('timeout').setDescription('Temporarily timeout a member.')
    .setDefaultMemberPermissions(PermissionFlagsBits.ModerateMembers)
    .addUserOption((option) => option.setName('member').setDescription('Member to timeout').setRequired(true))
    .addStringOption((option) => option.setName('duration').setDescription('Examples: 10m, 2h, 3d').setRequired(true))
    .addStringOption((option) => option.setName('reason').setDescription('Reason for the timeout').setMaxLength(500)),
  guildOnly: true,
  async execute(interaction) {
    const target = await fetchTargetMember(interaction);
    if (!await validateTarget(interaction, target, 'timeout')) return;
    const durationText = interaction.options.getString('duration', true);
    const duration = parseDuration(durationText);
    if (!duration || duration < 5_000 || duration > 28 * 86_400_000) {
      return replyError(interaction, 'Use a duration from **5 seconds** to **28 days**, like `10m`, `2h`, or `3d`.');
    }
    if (!target.moderatable) return replyError(interaction, 'Discord says I cannot timeout that member.');
    const reason = interaction.options.getString('reason') || 'No reason provided';
    await target.timeout(duration, `${reason} | Moderator: ${interaction.user.tag}`);
    await sendLog(interaction.guild, {
      title: '⏳ Member Timed Out', color: COLORS.warning,
      fields: [
        { name: 'Member', value: `${target.user.tag} (${target.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Until', value: `<t:${Math.floor((Date.now() + duration) / 1000)}:F>` },
        { name: 'Reason', value: reason },
      ],
    });
    return replySuccess(interaction, `Timed out **${target.user.tag}** until <t:${Math.floor((Date.now() + duration) / 1000)}:R>.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('untimeout').setDescription("Remove a member's timeout.")
    .setDefaultMemberPermissions(PermissionFlagsBits.ModerateMembers)
    .addUserOption((option) => option.setName('member').setDescription('Member to remove timeout from').setRequired(true))
    .addStringOption((option) => option.setName('reason').setDescription('Reason').setMaxLength(500)),
  guildOnly: true,
  async execute(interaction) {
    const target = await fetchTargetMember(interaction);
    if (!await validateTarget(interaction, target, 'untimeout')) return;
    if (!target.communicationDisabledUntilTimestamp) return replyError(interaction, 'That member is not timed out.');
    const reason = interaction.options.getString('reason') || 'No reason provided';
    await target.timeout(null, `${reason} | Moderator: ${interaction.user.tag}`);
    await sendLog(interaction.guild, {
      title: '⌛ Timeout Removed', color: COLORS.success,
      fields: [
        { name: 'Member', value: `${target.user.tag} (${target.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Reason', value: reason },
      ],
    });
    return replySuccess(interaction, `Removed **${target.user.tag}**'s timeout.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('warn').setDescription('Add a persistent warning to a member.')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageMessages)
    .addUserOption((option) => option.setName('member').setDescription('Member to warn').setRequired(true))
    .addStringOption((option) => option.setName('reason').setDescription('Warning reason').setRequired(true).setMaxLength(1000)),
  guildOnly: true,
  async execute(interaction) {
    const target = await fetchTargetMember(interaction);
    if (!await validateTarget(interaction, target, 'warn')) return;
    const reason = interaction.options.getString('reason', true);
    const warning = db.addWarning(interaction.guildId, target.id, interaction.user.id, reason);
    await target.send(`You were warned in **${interaction.guild.name}**.\nWarning ID: ${warning.id}\nReason: ${reason}`).catch(() => null);
    await sendLog(interaction.guild, {
      title: '⚠️ Member Warned', color: COLORS.warning,
      fields: [
        { name: 'Member', value: `${target.user.tag} (${target.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Warning ID', value: warning.id },
        { name: 'Reason', value: reason },
      ],
    });
    return replySuccess(interaction, `Warned **${target.user.tag}**. Warning ID: **${warning.id}**.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('warnings').setDescription("View a member's warnings.")
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageMessages)
    .addUserOption((option) => option.setName('member').setDescription('Member to inspect').setRequired(true)),
  guildOnly: true,
  async execute(interaction) {
    const user = interaction.options.getUser('member', true);
    const warnings = db.getWarnings(interaction.guildId, user.id);
    if (warnings.length === 0) return replyError(interaction, `${user.tag} has no warnings.`);
    const lines = warnings.slice(-15).map((warning, index) => (
      `**${index + 1}. ${warning.id}** • <t:${Math.floor(warning.createdAt / 1000)}:R> • by <@${warning.moderatorId}>\n${truncate(warning.reason, 250)}`
    ));
    const embed = baseEmbed(`⚠️ Warnings for ${user.tag}`, lines.join('\n\n'), COLORS.warning)
      .setFooter({ text: `${warnings.length} total warning(s)` });
    return interaction.reply({ embeds: [embed], flags: MessageFlags.Ephemeral });
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('removewarning').setDescription('Remove one warning by ID.')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageMessages)
    .addUserOption((option) => option.setName('member').setDescription('Member whose warning will be removed').setRequired(true))
    .addStringOption((option) => option.setName('warning_id').setDescription('Warning ID from /warnings').setRequired(true)),
  guildOnly: true,
  async execute(interaction) {
    const user = interaction.options.getUser('member', true);
    const removed = db.removeWarning(interaction.guildId, user.id, interaction.options.getString('warning_id', true));
    if (!removed) return replyError(interaction, 'That warning ID was not found for this member.');
    await sendLog(interaction.guild, {
      title: '🧽 Warning Removed', color: COLORS.success,
      fields: [
        { name: 'Member', value: `${user.tag} (${user.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Warning ID', value: removed.id },
        { name: 'Old reason', value: removed.reason },
      ],
    });
    return replySuccess(interaction, `Removed warning **${removed.id}** from **${user.tag}**.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('clearwarnings').setDescription("Clear all of a member's warnings.")
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageMessages)
    .addUserOption((option) => option.setName('member').setDescription('Member whose warnings will be cleared').setRequired(true)),
  guildOnly: true,
  async execute(interaction) {
    const user = interaction.options.getUser('member', true);
    const count = db.clearWarnings(interaction.guildId, user.id);
    if (count === 0) return replyError(interaction, `${user.tag} has no warnings to clear.`);
    await sendLog(interaction.guild, {
      title: '🧹 Warnings Cleared', color: COLORS.success,
      fields: [
        { name: 'Member', value: `${user.tag} (${user.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Warnings removed', value: String(count) },
      ],
    });
    return replySuccess(interaction, `Cleared **${count}** warning(s) from **${user.tag}**.`);
  },
});

commands.push({
  category: 'Moderation',
  cooldown: 5,
  data: new SlashCommandBuilder().setName('purge').setDescription('Bulk-delete recent messages in this channel.')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageMessages)
    .addIntegerOption((option) => option.setName('amount').setDescription('Number of messages, 1-100').setRequired(true).setMinValue(1).setMaxValue(100))
    .addUserOption((option) => option.setName('member').setDescription('Only delete messages from this member')),
  guildOnly: true,
  async execute(interaction) {
    if (!interaction.channel?.isTextBased() || typeof interaction.channel.bulkDelete !== 'function') {
      return replyError(interaction, 'This command only works in standard text channels.');
    }
    await interaction.deferReply({ flags: MessageFlags.Ephemeral });
    const amount = interaction.options.getInteger('amount', true);
    const user = interaction.options.getUser('member');
    let deleted;
    if (user) {
      const fetched = await interaction.channel.messages.fetch({ limit: 100 });
      const selected = fetched.filter((message) => message.author.id === user.id).first(amount);
      deleted = await interaction.channel.bulkDelete(selected, true);
    } else {
      deleted = await interaction.channel.bulkDelete(amount, true);
    }
    await sendLog(interaction.guild, {
      title: '🧹 Messages Purged', color: COLORS.warning,
      fields: [
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Channel', value: `${interaction.channel} (${interaction.channelId})` },
        { name: 'Deleted', value: String(deleted.size) },
        { name: 'Member filter', value: user ? `${user.tag} (${user.id})` : 'None' },
      ],
    });
    return interaction.editReply(`🧹 Deleted **${deleted.size}** recent message(s). Messages older than 14 days cannot be bulk-deleted.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('slowmode').setDescription('Set slowmode in a text channel.')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels)
    .addIntegerOption((option) => option.setName('seconds').setDescription('0 disables slowmode; max 21600').setRequired(true).setMinValue(0).setMaxValue(21_600))
    .addChannelOption((option) => option.setName('channel').setDescription('Text channel').addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement)),
  guildOnly: true,
  async execute(interaction) {
    const channel = interaction.options.getChannel('channel') || interaction.channel;
    if (typeof channel.setRateLimitPerUser !== 'function') return replyError(interaction, 'That channel does not support slowmode.');
    const seconds = interaction.options.getInteger('seconds', true);
    await channel.setRateLimitPerUser(seconds, `Changed by ${interaction.user.tag}`);
    await sendLog(interaction.guild, {
      title: '🐢 Slowmode Updated', color: COLORS.info,
      fields: [
        { name: 'Channel', value: `${channel} (${channel.id})` },
        { name: 'Seconds', value: String(seconds) },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
      ],
    });
    return replySuccess(interaction, seconds === 0 ? `Disabled slowmode in ${channel}.` : `Set slowmode in ${channel} to **${seconds}s**.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('lock').setDescription('Prevent @everyone from sending messages in a channel.')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels)
    .addChannelOption((option) => option.setName('channel').setDescription('Channel to lock').addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement)),
  guildOnly: true,
  async execute(interaction) {
    const channel = interaction.options.getChannel('channel') || interaction.channel;
    if (!channel.permissionOverwrites) return replyError(interaction, 'That channel cannot be locked.');
    await channel.permissionOverwrites.edit(interaction.guild.roles.everyone, { SendMessages: false }, { reason: `Locked by ${interaction.user.tag}` });
    await sendLog(interaction.guild, {
      title: '🔒 Channel Locked', color: COLORS.warning,
      fields: [
        { name: 'Channel', value: `${channel} (${channel.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
      ],
    });
    return replySuccess(interaction, `Locked ${channel}.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('unlock').setDescription('Restore @everyone sending permissions in a channel.')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels)
    .addChannelOption((option) => option.setName('channel').setDescription('Channel to unlock').addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement)),
  guildOnly: true,
  async execute(interaction) {
    const channel = interaction.options.getChannel('channel') || interaction.channel;
    if (!channel.permissionOverwrites) return replyError(interaction, 'That channel cannot be unlocked.');
    await channel.permissionOverwrites.edit(interaction.guild.roles.everyone, { SendMessages: null }, { reason: `Unlocked by ${interaction.user.tag}` });
    await sendLog(interaction.guild, {
      title: '🔓 Channel Unlocked', color: COLORS.success,
      fields: [
        { name: 'Channel', value: `${channel} (${channel.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
      ],
    });
    return replySuccess(interaction, `Unlocked ${channel}.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('nickname').setDescription("Change or reset a member's nickname.")
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageNicknames)
    .addUserOption((option) => option.setName('member').setDescription('Member').setRequired(true))
    .addStringOption((option) => option.setName('nickname').setDescription('Leave blank to reset').setMaxLength(32)),
  guildOnly: true,
  async execute(interaction) {
    const target = await fetchTargetMember(interaction);
    if (!await validateTarget(interaction, target, 'change the nickname of')) return;
    if (!target.manageable) return replyError(interaction, 'Discord says I cannot manage that member.');
    const nickname = interaction.options.getString('nickname');
    const oldNickname = target.nickname || 'None';
    await target.setNickname(nickname, `Changed by ${interaction.user.tag}`);
    await sendLog(interaction.guild, {
      title: '✏️ Nickname Changed', color: COLORS.info,
      fields: [
        { name: 'Member', value: `${target.user.tag} (${target.id})` },
        { name: 'Old nickname', value: oldNickname },
        { name: 'New nickname', value: nickname || 'Reset' },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
      ],
    });
    return replySuccess(interaction, nickname ? `Set ${target}'s nickname to **${nickname}**.` : `Reset ${target}'s nickname.`);
  },
});

commands.push({
  category: 'Moderation',
  data: new SlashCommandBuilder().setName('role').setDescription('Add or remove a role from a member.')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageRoles)
    .addSubcommand((sub) => sub.setName('add').setDescription('Add a role')
      .addUserOption((option) => option.setName('member').setDescription('Member').setRequired(true))
      .addRoleOption((option) => option.setName('role').setDescription('Role').setRequired(true)))
    .addSubcommand((sub) => sub.setName('remove').setDescription('Remove a role')
      .addUserOption((option) => option.setName('member').setDescription('Member').setRequired(true))
      .addRoleOption((option) => option.setName('role').setDescription('Role').setRequired(true))),
  guildOnly: true,
  async execute(interaction) {
    const action = interaction.options.getSubcommand();
    const target = await fetchTargetMember(interaction);
    const role = interaction.options.getRole('role', true);
    if (!target) return replyError(interaction, 'That member is not in this server.');
    const executor = await fetchExecutor(interaction);
    if (!canActOn(executor, target)) return replyError(interaction, 'Your highest role must be above that member.');
    if (!botCanActOn(interaction.guild, target)) return replyError(interaction, 'My highest role must be above that member.');
    if (!canManageRole(executor, role)) return replyError(interaction, 'Your highest role must be above the selected role.');
    if (!botCanManageRole(interaction.guild, role)) return replyError(interaction, 'My highest role must be above the selected role.');

    if (action === 'add') {
      if (target.roles.cache.has(role.id)) return replyError(interaction, `${target} already has ${role}.`);
      await target.roles.add(role, `Added by ${interaction.user.tag}`);
    } else {
      if (!target.roles.cache.has(role.id)) return replyError(interaction, `${target} does not have ${role}.`);
      await target.roles.remove(role, `Removed by ${interaction.user.tag}`);
    }

    await sendLog(interaction.guild, {
      title: action === 'add' ? '➕ Role Added' : '➖ Role Removed',
      color: action === 'add' ? COLORS.success : COLORS.warning,
      fields: [
        { name: 'Member', value: `${target.user.tag} (${target.id})` },
        { name: 'Role', value: `${role.name} (${role.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
      ],
    });
    return replySuccess(interaction, `${action === 'add' ? 'Added' : 'Removed'} ${role} ${action === 'add' ? 'to' : 'from'} ${target}.`);
  },
});

commands.push({
  category: 'Voice Moderation',
  data: new SlashCommandBuilder().setName('move').setDescription('Move a member to another voice channel.')
    .setDefaultMemberPermissions(PermissionFlagsBits.MoveMembers)
    .addUserOption((option) => option.setName('member').setDescription('Member to move').setRequired(true))
    .addChannelOption((option) => option.setName('channel').setDescription('Destination voice channel').setRequired(true).addChannelTypes(ChannelType.GuildVoice, ChannelType.GuildStageVoice)),
  guildOnly: true,
  async execute(interaction) {
    const target = await fetchTargetMember(interaction);
    const channel = interaction.options.getChannel('channel', true);
    if (!target?.voice.channel) return replyError(interaction, 'That member is not in a voice channel.');
    await target.voice.setChannel(channel, `Moved by ${interaction.user.tag}`);
    await sendLog(interaction.guild, {
      title: '🔀 Voice Member Moved', color: COLORS.info,
      fields: [
        { name: 'Member', value: `${target.user.tag} (${target.id})` },
        { name: 'Destination', value: `${channel.name} (${channel.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
      ],
    });
    return replySuccess(interaction, `Moved ${target} to **${channel.name}**.`);
  },
});

commands.push({
  category: 'Voice Moderation',
  data: new SlashCommandBuilder().setName('disconnect').setDescription('Disconnect a member from voice.')
    .setDefaultMemberPermissions(PermissionFlagsBits.MoveMembers)
    .addUserOption((option) => option.setName('member').setDescription('Member to disconnect').setRequired(true)),
  guildOnly: true,
  async execute(interaction) {
    const target = await fetchTargetMember(interaction);
    if (!target?.voice.channel) return replyError(interaction, 'That member is not in a voice channel.');
    await target.voice.disconnect(`Disconnected by ${interaction.user.tag}`);
    await sendLog(interaction.guild, {
      title: '📴 Voice Member Disconnected', color: COLORS.warning,
      fields: [
        { name: 'Member', value: `${target.user.tag} (${target.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
      ],
    });
    return replySuccess(interaction, `Disconnected **${target.user.tag}** from voice.`);
  },
});

commands.push({
  category: 'Voice Moderation',
  data: new SlashCommandBuilder().setName('voicemute').setDescription('Server-mute or unmute a voice member.')
    .setDefaultMemberPermissions(PermissionFlagsBits.MuteMembers)
    .addSubcommand((sub) => sub.setName('mute').setDescription('Server-mute a member')
      .addUserOption((option) => option.setName('member').setDescription('Member').setRequired(true)))
    .addSubcommand((sub) => sub.setName('unmute').setDescription('Remove server mute')
      .addUserOption((option) => option.setName('member').setDescription('Member').setRequired(true))),
  guildOnly: true,
  async execute(interaction) {
    const target = await fetchTargetMember(interaction);
    if (!target?.voice.channel) return replyError(interaction, 'That member is not in a voice channel.');
    const mute = interaction.options.getSubcommand() === 'mute';
    await target.voice.setMute(mute, `${mute ? 'Muted' : 'Unmuted'} by ${interaction.user.tag}`);
    await sendLog(interaction.guild, {
      title: mute ? '🔇 Voice Member Muted' : '🔊 Voice Member Unmuted',
      color: mute ? COLORS.warning : COLORS.success,
      fields: [
        { name: 'Member', value: `${target.user.tag} (${target.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
      ],
    });
    return replySuccess(interaction, `${mute ? 'Muted' : 'Unmuted'} **${target.user.tag}** in voice.`);
  },
});

commands.push({
  category: 'Voice Moderation',
  data: new SlashCommandBuilder().setName('voicedeafen').setDescription('Server-deafen or undeafen a voice member.')
    .setDefaultMemberPermissions(PermissionFlagsBits.DeafenMembers)
    .addSubcommand((sub) => sub.setName('deafen').setDescription('Server-deafen a member')
      .addUserOption((option) => option.setName('member').setDescription('Member').setRequired(true)))
    .addSubcommand((sub) => sub.setName('undeafen').setDescription('Remove server deafen')
      .addUserOption((option) => option.setName('member').setDescription('Member').setRequired(true))),
  guildOnly: true,
  async execute(interaction) {
    const target = await fetchTargetMember(interaction);
    if (!target?.voice.channel) return replyError(interaction, 'That member is not in a voice channel.');
    const deafen = interaction.options.getSubcommand() === 'deafen';
    await target.voice.setDeaf(deafen, `${deafen ? 'Deafened' : 'Undeafened'} by ${interaction.user.tag}`);
    await sendLog(interaction.guild, {
      title: deafen ? '🙉 Voice Member Deafened' : '👂 Voice Member Undeafened',
      color: deafen ? COLORS.warning : COLORS.success,
      fields: [
        { name: 'Member', value: `${target.user.tag} (${target.id})` },
        { name: 'Moderator', value: `${interaction.user.tag} (${interaction.user.id})` },
      ],
    });
    return replySuccess(interaction, `${deafen ? 'Deafened' : 'Undeafened'} **${target.user.tag}** in voice.`);
  },
});

module.exports = commands;
