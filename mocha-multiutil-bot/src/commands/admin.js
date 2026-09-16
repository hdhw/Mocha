const {
  SlashCommandBuilder,
  PermissionFlagsBits,
  ChannelType,
  MessageFlags,
  ButtonBuilder,
  ButtonStyle,
  ActionRowBuilder,
  ActivityType,
  AuditLogEvent,
} = require('discord.js');
const db = require('../database');
const config = require('../config');
const { baseEmbed, COLORS } = require('../utils/embeds');
const { replyError, replySuccess } = require('../utils/replies');
const { sendLog } = require('../utils/logger');
const { truncate } = require('../utils/text');

const commands = [];

function channelValue(guild, id) {
  return id ? `${guild.channels.cache.get(id) || `<#${id}>`} (${id})` : 'Disabled';
}

function roleValue(guild, id) {
  return id ? `${guild.roles.cache.get(id) || `<@&${id}>`} (${id})` : 'Disabled';
}

async function isOwner(interaction) {
  if (config.ownerIds.has(interaction.user.id)) return true;
  const application = await interaction.client.application.fetch().catch(() => null);
  const owner = application?.owner;
  if (!owner) return false;
  if (owner.id === interaction.user.id) return true;
  return Boolean(owner.members?.has?.(interaction.user.id));
}

commands.push({
  category: 'Administration',
  data: new SlashCommandBuilder().setName('announce').setDescription('Send a formatted announcement.')
    .setDefaultMemberPermissions(PermissionFlagsBits.Administrator)
    .addChannelOption((option) => option.setName('channel').setDescription('Announcement destination').setRequired(true).addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement))
    .addStringOption((option) => option.setName('message').setDescription('Announcement text').setRequired(true).setMaxLength(4000))
    .addStringOption((option) => option.setName('title').setDescription('Embed title').setMaxLength(256))
    .addStringOption((option) => option.setName('color').setDescription('Hex color like #8B5CF6').setMaxLength(7))
    .addBooleanOption((option) => option.setName('ping_everyone').setDescription('Mention @everyone with the announcement')),
  guildOnly: true,
  async execute(interaction) {
    const channel = interaction.options.getChannel('channel', true);
    const message = interaction.options.getString('message', true);
    const title = interaction.options.getString('title') || '📢 Announcement';
    const colorInput = interaction.options.getString('color');
    const pingEveryone = interaction.options.getBoolean('ping_everyone') || false;
    let color = COLORS.primary;
    if (colorInput) {
      if (!/^#[0-9a-f]{6}$/i.test(colorInput)) return replyError(interaction, 'Color must look like `#8B5CF6`.');
      color = Number.parseInt(colorInput.slice(1), 16);
    }
    const embed = baseEmbed(title, message, color)
      .setFooter({ text: `Posted by ${interaction.user.tag}` });
    const sent = await channel.send({
      content: pingEveryone ? '@everyone' : undefined,
      embeds: [embed],
      allowedMentions: { parse: pingEveryone ? ['everyone'] : [] },
    });
    await sendLog(interaction.guild, {
      title: '📢 Announcement Sent', color: COLORS.info,
      fields: [
        { name: 'Administrator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Channel', value: `${channel} (${channel.id})` },
        { name: 'Message', value: message },
      ],
    });
    return replySuccess(interaction, `Announcement sent: ${sent.url}`);
  },
});

commands.push({
  category: 'Administration',
  data: new SlashCommandBuilder().setName('say').setDescription('Make the bot send a plain message.')
    .setDefaultMemberPermissions(PermissionFlagsBits.Administrator)
    .addChannelOption((option) => option.setName('channel').setDescription('Destination').setRequired(true).addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement))
    .addStringOption((option) => option.setName('message').setDescription('Message text').setRequired(true).setMaxLength(2000)),
  guildOnly: true,
  async execute(interaction) {
    const channel = interaction.options.getChannel('channel', true);
    const message = interaction.options.getString('message', true);
    const sent = await channel.send({ content: message, allowedMentions: { parse: [] } });
    await sendLog(interaction.guild, {
      title: '💬 Bot Message Sent', color: COLORS.info,
      fields: [
        { name: 'Administrator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Channel', value: `${channel} (${channel.id})` },
        { name: 'Message', value: message },
      ],
    });
    return replySuccess(interaction, `Message sent: ${sent.url}`);
  },
});

commands.push({
  category: 'Administration',
  data: new SlashCommandBuilder().setName('embed').setDescription('Create a custom embed message.')
    .setDefaultMemberPermissions(PermissionFlagsBits.Administrator)
    .addChannelOption((option) => option.setName('channel').setDescription('Destination').setRequired(true).addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement))
    .addStringOption((option) => option.setName('description').setDescription('Embed description').setRequired(true).setMaxLength(4000))
    .addStringOption((option) => option.setName('title').setDescription('Embed title').setMaxLength(256))
    .addStringOption((option) => option.setName('color').setDescription('Hex color like #8B5CF6').setMaxLength(7))
    .addStringOption((option) => option.setName('image').setDescription('Image URL').setMaxLength(1000))
    .addStringOption((option) => option.setName('thumbnail').setDescription('Thumbnail URL').setMaxLength(1000))
    .addStringOption((option) => option.setName('footer').setDescription('Footer text').setMaxLength(1000)),
  guildOnly: true,
  async execute(interaction) {
    const channel = interaction.options.getChannel('channel', true);
    const colorInput = interaction.options.getString('color');
    let color = COLORS.primary;
    if (colorInput) {
      if (!/^#[0-9a-f]{6}$/i.test(colorInput)) return replyError(interaction, 'Color must look like `#8B5CF6`.');
      color = Number.parseInt(colorInput.slice(1), 16);
    }
    const embed = baseEmbed(
      interaction.options.getString('title'),
      interaction.options.getString('description', true),
      color,
    );
    const image = interaction.options.getString('image');
    const thumbnail = interaction.options.getString('thumbnail');
    const footer = interaction.options.getString('footer');
    try {
      if (image) embed.setImage(image);
      if (thumbnail) embed.setThumbnail(thumbnail);
      if (footer) embed.setFooter({ text: footer });
    } catch {
      return replyError(interaction, 'One of the image URLs is invalid. It must be a complete `https://` URL.');
    }
    const sent = await channel.send({ embeds: [embed], allowedMentions: { parse: [] } });
    await sendLog(interaction.guild, {
      title: '🧱 Custom Embed Sent', color: COLORS.info,
      fields: [
        { name: 'Administrator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Channel', value: `${channel} (${channel.id})` },
        { name: 'Description', value: interaction.options.getString('description', true) },
      ],
    });
    return replySuccess(interaction, `Embed sent: ${sent.url}`);
  },
});

commands.push({
  category: 'Administration',
  data: new SlashCommandBuilder().setName('config').setDescription('Configure server systems.')
    .setDefaultMemberPermissions(PermissionFlagsBits.Administrator)
    .addSubcommand((sub) => sub.setName('view').setDescription('View the current server configuration'))
    .addSubcommand((sub) => sub.setName('logs').setDescription('Set the logging channel')
      .addChannelOption((option) => option.setName('channel').setDescription('Logging channel').setRequired(true).addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement)))
    .addSubcommand((sub) => sub.setName('welcome').setDescription('Configure welcome messages')
      .addChannelOption((option) => option.setName('channel').setDescription('Welcome channel').setRequired(true).addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement))
      .addStringOption((option) => option.setName('message').setDescription('Supports {user}, {tag}, {server}, {count}').setMaxLength(1000)))
    .addSubcommand((sub) => sub.setName('goodbye').setDescription('Configure goodbye messages')
      .addChannelOption((option) => option.setName('channel').setDescription('Goodbye channel').setRequired(true).addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement))
      .addStringOption((option) => option.setName('message').setDescription('Supports {user}, {tag}, {server}, {count}').setMaxLength(1000)))
    .addSubcommand((sub) => sub.setName('autorole').setDescription('Set the role automatically given to new members')
      .addRoleOption((option) => option.setName('role').setDescription('Autorole').setRequired(true)))
    .addSubcommand((sub) => sub.setName('suggestions').setDescription('Set the suggestions channel')
      .addChannelOption((option) => option.setName('channel').setDescription('Suggestions channel').setRequired(true).addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement)))
    .addSubcommand((sub) => sub.setName('support-role').setDescription('Set the staff role that can access tickets')
      .addRoleOption((option) => option.setName('role').setDescription('Support role').setRequired(true)))
    .addSubcommand((sub) => sub.setName('ticket-category').setDescription('Set the category used for ticket channels')
      .addChannelOption((option) => option.setName('category').setDescription('Ticket category').setRequired(true).addChannelTypes(ChannelType.GuildCategory)))
    .addSubcommand((sub) => sub.setName('clear').setDescription('Disable or clear one configured system')
      .addStringOption((option) => option.setName('setting').setDescription('Setting to clear').setRequired(true)
        .addChoices(
          { name: 'Logging channel', value: 'logChannelId' },
          { name: 'Welcome messages', value: 'welcomeChannelId' },
          { name: 'Goodbye messages', value: 'goodbyeChannelId' },
          { name: 'Autorole', value: 'autoroleId' },
          { name: 'Suggestions channel', value: 'suggestionsChannelId' },
          { name: 'Support role', value: 'supportRoleId' },
          { name: 'Ticket category', value: 'ticketCategoryId' },
        ))),
  guildOnly: true,
  async execute(interaction) {
    const subcommand = interaction.options.getSubcommand();
    const current = db.getGuildConfig(interaction.guildId);

    if (subcommand === 'view') {
      const embed = baseEmbed('⚙️ Server Configuration', null, COLORS.info)
        .addFields(
          { name: 'Logs', value: channelValue(interaction.guild, current.logChannelId), inline: false },
          { name: 'Welcome', value: channelValue(interaction.guild, current.welcomeChannelId), inline: false },
          { name: 'Goodbye', value: channelValue(interaction.guild, current.goodbyeChannelId), inline: false },
          { name: 'Autorole', value: roleValue(interaction.guild, current.autoroleId), inline: false },
          { name: 'Suggestions', value: channelValue(interaction.guild, current.suggestionsChannelId), inline: false },
          { name: 'Support role', value: roleValue(interaction.guild, current.supportRoleId), inline: false },
          { name: 'Ticket category', value: channelValue(interaction.guild, current.ticketCategoryId), inline: false },
          { name: 'Automod', value: Object.entries(current.automod).map(([name, enabled]) => `${enabled ? '✅' : '❌'} ${name}`).join('\n'), inline: false },
        );
      return interaction.reply({ embeds: [embed], flags: MessageFlags.Ephemeral });
    }

    let patch = {};
    let description = '';
    if (subcommand === 'logs') {
      const channel = interaction.options.getChannel('channel', true);
      patch = { logChannelId: channel.id };
      description = `Logging channel set to ${channel}.`;
    } else if (subcommand === 'welcome') {
      const channel = interaction.options.getChannel('channel', true);
      const message = interaction.options.getString('message');
      patch = { welcomeChannelId: channel.id, ...(message ? { welcomeMessage: message } : {}) };
      description = `Welcome messages will be sent in ${channel}.`;
    } else if (subcommand === 'goodbye') {
      const channel = interaction.options.getChannel('channel', true);
      const message = interaction.options.getString('message');
      patch = { goodbyeChannelId: channel.id, ...(message ? { goodbyeMessage: message } : {}) };
      description = `Goodbye messages will be sent in ${channel}.`;
    } else if (subcommand === 'autorole') {
      const role = interaction.options.getRole('role', true);
      if (role.managed || role.id === interaction.guild.id) return replyError(interaction, 'That role cannot be used as an autorole.');
      if (interaction.guild.members.me.roles.highest.comparePositionTo(role) <= 0) return replyError(interaction, 'My highest role must be above the autorole.');
      patch = { autoroleId: role.id };
      description = `New members will receive ${role}.`;
    } else if (subcommand === 'suggestions') {
      const channel = interaction.options.getChannel('channel', true);
      patch = { suggestionsChannelId: channel.id };
      description = `Suggestions channel set to ${channel}.`;
    } else if (subcommand === 'support-role') {
      const role = interaction.options.getRole('role', true);
      patch = { supportRoleId: role.id };
      description = `Ticket support role set to ${role}.`;
    } else if (subcommand === 'ticket-category') {
      const category = interaction.options.getChannel('category', true);
      patch = { ticketCategoryId: category.id };
      description = `Ticket category set to **${category.name}**.`;
    } else if (subcommand === 'clear') {
      const setting = interaction.options.getString('setting', true);
      patch = { [setting]: null };
      description = `Cleared **${setting}**.`;
    }

    db.updateGuildConfig(interaction.guildId, patch);
    await sendLog(interaction.guild, {
      title: '⚙️ Configuration Updated', color: COLORS.info,
      fields: [
        { name: 'Administrator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Change', value: description },
      ],
    });
    return replySuccess(interaction, description);
  },
});

commands.push({
  category: 'Administration',
  data: new SlashCommandBuilder().setName('automod').setDescription('Configure lightweight automatic moderation.')
    .setDefaultMemberPermissions(PermissionFlagsBits.Administrator)
    .addSubcommand((sub) => sub.setName('view').setDescription('View automod modules'))
    .addSubcommand((sub) => sub.setName('set').setDescription('Enable or disable an automod module')
      .addStringOption((option) => option.setName('module').setDescription('Automod module').setRequired(true)
        .addChoices(
          { name: 'Discord invites', value: 'invites' },
          { name: 'External links', value: 'links' },
          { name: 'Excessive caps', value: 'caps' },
          { name: 'Message spam', value: 'spam' },
        ))
      .addBooleanOption((option) => option.setName('enabled').setDescription('Whether the module is enabled').setRequired(true))),
  guildOnly: true,
  async execute(interaction) {
    const current = db.getGuildConfig(interaction.guildId);
    if (interaction.options.getSubcommand() === 'view') {
      const status = Object.entries(current.automod).map(([name, enabled]) => `${enabled ? '✅' : '❌'} **${name}**`).join('\n');
      return interaction.reply({ embeds: [baseEmbed('🛡️ Automod Status', status, COLORS.info)], flags: MessageFlags.Ephemeral });
    }
    const moduleName = interaction.options.getString('module', true);
    const enabled = interaction.options.getBoolean('enabled', true);
    db.updateGuildConfig(interaction.guildId, { automod: { [moduleName]: enabled } });
    await sendLog(interaction.guild, {
      title: '🛡️ Automod Updated', color: COLORS.info,
      fields: [
        { name: 'Administrator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Module', value: moduleName },
        { name: 'Enabled', value: enabled ? 'Yes' : 'No' },
      ],
    });
    return replySuccess(interaction, `Automod module **${moduleName}** is now **${enabled ? 'enabled' : 'disabled'}**.`);
  },
});

commands.push({
  category: 'Administration',
  data: new SlashCommandBuilder().setName('ticketpanel').setDescription('Post a button that members can use to create support tickets.')
    .setDefaultMemberPermissions(PermissionFlagsBits.Administrator)
    .addChannelOption((option) => option.setName('channel').setDescription('Channel for the ticket panel').setRequired(true).addChannelTypes(ChannelType.GuildText, ChannelType.GuildAnnouncement))
    .addStringOption((option) => option.setName('title').setDescription('Panel title').setMaxLength(256))
    .addStringOption((option) => option.setName('description').setDescription('Panel description').setMaxLength(2000)),
  guildOnly: true,
  async execute(interaction) {
    const serverConfig = db.getGuildConfig(interaction.guildId);
    if (!serverConfig.ticketCategoryId) return replyError(interaction, 'Set a ticket category first with `/config ticket-category`.');
    const channel = interaction.options.getChannel('channel', true);
    const embed = baseEmbed(
      interaction.options.getString('title') || '🎫 Support Tickets',
      interaction.options.getString('description') || 'Need help? Press the button below to open a private support ticket.',
      COLORS.primary,
    );
    const row = new ActionRowBuilder().addComponents(
      new ButtonBuilder().setCustomId('ticket:create').setLabel('Open Ticket').setEmoji('🎫').setStyle(ButtonStyle.Primary),
    );
    const sent = await channel.send({ embeds: [embed], components: [row] });
    await sendLog(interaction.guild, {
      title: '🎫 Ticket Panel Posted', color: COLORS.info,
      fields: [
        { name: 'Administrator', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Channel', value: `${channel} (${channel.id})` },
      ],
    });
    return replySuccess(interaction, `Ticket panel posted: ${sent.url}`);
  },
});

commands.push({
  category: 'Administration',
  data: new SlashCommandBuilder().setName('audit').setDescription('Show recent Discord audit log entries.')
    .setDefaultMemberPermissions(PermissionFlagsBits.ViewAuditLog)
    .addIntegerOption((option) => option.setName('limit').setDescription('Number of entries, 1-15').setMinValue(1).setMaxValue(15)),
  guildOnly: true,
  async execute(interaction) {
    const limit = interaction.options.getInteger('limit') || 10;
    const logs = await interaction.guild.fetchAuditLogs({ limit });
    if (logs.entries.size === 0) return replyError(interaction, 'No audit log entries were found.');
    const actionName = (action) => Object.entries(AuditLogEvent).find(([, value]) => value === action)?.[0] || `Action ${action}`;
    const lines = logs.entries.map((entry) => {
      const executor = entry.executor ? `${entry.executor.tag} (${entry.executor.id})` : 'Unknown';
      const target = entry.target?.tag || entry.target?.name || entry.target?.id || 'Unknown';
      return `**${actionName(entry.action)}** • <t:${Math.floor(entry.createdTimestamp / 1000)}:R>\nBy ${executor} • Target: ${truncate(target, 100)}${entry.reason ? ` • ${truncate(entry.reason, 120)}` : ''}`;
    });
    return interaction.reply({ embeds: [baseEmbed('📜 Recent Audit Log', lines.join('\n\n'), COLORS.info)], flags: MessageFlags.Ephemeral });
  },
});

commands.push({
  category: 'Administration',
  data: new SlashCommandBuilder().setName('commandstats').setDescription('Show the most-used bot commands in this server.')
    .setDefaultMemberPermissions(PermissionFlagsBits.Administrator),
  guildOnly: true,
  async execute(interaction) {
    const stats = db.getCommandStats(interaction.guildId);
    const sorted = Object.entries(stats).sort(([, a], [, b]) => b - a).slice(0, 20);
    if (sorted.length === 0) return replyError(interaction, 'No command usage has been recorded yet.');
    const total = Object.values(stats).reduce((sum, count) => sum + count, 0);
    const lines = sorted.map(([name, count], index) => `**${index + 1}.** \`/${name}\` — **${count}** use(s)`);
    const embed = baseEmbed('📈 Command Usage', lines.join('\n'), COLORS.info)
      .setFooter({ text: `${total} total command uses` });
    return interaction.reply({ embeds: [embed], flags: MessageFlags.Ephemeral });
  },
});

commands.push({
  category: 'Bot Owner',
  data: new SlashCommandBuilder().setName('botstatus').setDescription('Change the bot presence. Bot owner only.')
    .setDefaultMemberPermissions(PermissionFlagsBits.Administrator)
    .addStringOption((option) => option.setName('status').setDescription('Online status').setRequired(true)
      .addChoices(
        { name: 'Online', value: 'online' },
        { name: 'Idle', value: 'idle' },
        { name: 'Do Not Disturb', value: 'dnd' },
        { name: 'Invisible', value: 'invisible' },
      ))
    .addStringOption((option) => option.setName('activity').setDescription('Activity text').setRequired(true).setMaxLength(128))
    .addStringOption((option) => option.setName('type').setDescription('Activity type').setRequired(true)
      .addChoices(
        { name: 'Playing', value: 'Playing' },
        { name: 'Watching', value: 'Watching' },
        { name: 'Listening', value: 'Listening' },
        { name: 'Competing', value: 'Competing' },
      )),
  async execute(interaction, client) {
    if (!await isOwner(interaction)) return replyError(interaction, 'Only the bot owner can use this command.');
    const typeName = interaction.options.getString('type', true);
    client.user.setPresence({
      status: interaction.options.getString('status', true),
      activities: [{ name: interaction.options.getString('activity', true), type: ActivityType[typeName] }],
    });
    return replySuccess(interaction, 'Bot presence updated.');
  },
});

commands.push({
  category: 'Bot Owner',
  data: new SlashCommandBuilder().setName('reload-data').setDescription('Reload the JSON database from disk. Bot owner only.')
    .setDefaultMemberPermissions(PermissionFlagsBits.Administrator),
  async execute(interaction) {
    if (!await isOwner(interaction)) return replyError(interaction, 'Only the bot owner can use this command.');
    db.load();
    return replySuccess(interaction, 'Database reloaded from disk.');
  },
});

module.exports = commands;
