const {
  SlashCommandBuilder,
  EmbedBuilder,
  MessageFlags,
  ChannelType,
} = require('discord.js');
const { evaluate } = require('mathjs');
const db = require('../database');
const { parseDuration } = require('../utils/duration');
const { baseEmbed, COLORS } = require('../utils/embeds');
const { replyError, replySuccess } = require('../utils/replies');
const { formatDuration, truncate } = require('../utils/text');
const { sendLog } = require('../utils/logger');

const commands = [];

commands.push({
  category: 'General',
  cooldown: 3,
  data: new SlashCommandBuilder().setName('ping').setDescription('Check the bot and Discord API latency.'),
  async execute(interaction, client) {
    await interaction.reply({ content: 'Pinging…' });
    const sent = await interaction.fetchReply();
    const roundTrip = sent.createdTimestamp - interaction.createdTimestamp;
    await interaction.editReply(`🏓 Pong! **Bot:** ${roundTrip}ms • **Gateway:** ${Math.round(client.ws.ping)}ms`);
  },
});

commands.push({
  category: 'General',
  cooldown: 3,
  data: new SlashCommandBuilder().setName('help').setDescription('Show every bot command.')
    .addStringOption((option) => option.setName('command').setDescription('Get details about one command')),
  async execute(interaction, client) {
    const commandName = interaction.options.getString('command')?.toLowerCase();
    if (commandName) {
      const command = client.commands.get(commandName);
      if (!command) return replyError(interaction, `I could not find \`/${commandName}\`.`);
      const json = command.data.toJSON();
      const subcommands = (json.options || [])
        .filter((option) => option.type === 1 || option.type === 2)
        .map((option) => `\`${option.name}\``)
        .join(', ');
      const embed = baseEmbed(`/${json.name}`, json.description, COLORS.primary)
        .addFields(
          { name: 'Category', value: command.category || 'Other', inline: true },
          { name: 'Cooldown', value: `${command.cooldown || 2}s`, inline: true },
          { name: 'Subcommands', value: subcommands || 'None', inline: false },
        );
      return interaction.reply({ embeds: [embed], flags: MessageFlags.Ephemeral });
    }

    const grouped = new Map();
    for (const command of client.commands.values()) {
      const category = command.category || 'Other';
      if (!grouped.has(category)) grouped.set(category, []);
      grouped.get(category).push(`\`/${command.data.name}\``);
    }

    const embed = baseEmbed(
      '🤖 Multi-Utility Help',
      `Loaded **${client.commands.size}** slash commands. Use \`/help command:<name>\` for details.`,
      COLORS.primary,
    );
    for (const [category, names] of [...grouped.entries()].sort(([a], [b]) => a.localeCompare(b))) {
      embed.addFields({ name: category, value: names.sort().join(' • ') });
    }
    return interaction.reply({ embeds: [embed], flags: MessageFlags.Ephemeral });
  },
});

commands.push({
  category: 'Information',
  data: new SlashCommandBuilder().setName('botinfo').setDescription('Show information about the bot.'),
  async execute(interaction, client) {
    const uptime = formatDuration(client.uptime || 0);
    const embed = baseEmbed('🤖 Bot Information', null, COLORS.info)
      .setThumbnail(client.user.displayAvatarURL())
      .addFields(
        { name: 'Servers', value: String(client.guilds.cache.size), inline: true },
        { name: 'Users cached', value: String(client.users.cache.size), inline: true },
        { name: 'Commands', value: String(client.commands.size), inline: true },
        { name: 'Uptime', value: uptime, inline: true },
        { name: 'Node.js', value: process.version, inline: true },
        { name: 'discord.js', value: require('discord.js').version, inline: true },
      );
    return interaction.reply({ embeds: [embed] });
  },
});

commands.push({
  category: 'Information',
  data: new SlashCommandBuilder().setName('serverinfo').setDescription('Show information about this server.'),
  guildOnly: true,
  async execute(interaction) {
    const guild = interaction.guild;
    await guild.members.fetch({ withPresences: false }).catch(() => null);
    const owner = await guild.fetchOwner().catch(() => null);
    const textChannels = guild.channels.cache.filter((c) => c.type === ChannelType.GuildText).size;
    const voiceChannels = guild.channels.cache.filter((c) => c.type === ChannelType.GuildVoice).size;
    const embed = baseEmbed(`🏠 ${guild.name}`, guild.description || 'No server description.', COLORS.info)
      .setThumbnail(guild.iconURL({ size: 256 }))
      .addFields(
        { name: 'Owner', value: owner ? `${owner.user.tag} (${owner.id})` : guild.ownerId, inline: true },
        { name: 'Members', value: String(guild.memberCount), inline: true },
        { name: 'Roles', value: String(guild.roles.cache.size), inline: true },
        { name: 'Text channels', value: String(textChannels), inline: true },
        { name: 'Voice channels', value: String(voiceChannels), inline: true },
        { name: 'Boosts', value: `${guild.premiumSubscriptionCount || 0} (Tier ${guild.premiumTier})`, inline: true },
        { name: 'Created', value: `<t:${Math.floor(guild.createdTimestamp / 1000)}:F>`, inline: false },
        { name: 'Server ID', value: guild.id, inline: false },
      );
    return interaction.reply({ embeds: [embed] });
  },
});

commands.push({
  category: 'Information',
  data: new SlashCommandBuilder().setName('userinfo').setDescription('Show information about a member.')
    .addUserOption((option) => option.setName('user').setDescription('Member to inspect')),
  guildOnly: true,
  async execute(interaction) {
    const user = interaction.options.getUser('user') || interaction.user;
    const member = await interaction.guild.members.fetch(user.id).catch(() => null);
    const roles = member
      ? member.roles.cache.filter((role) => role.id !== interaction.guild.id).sort((a, b) => b.position - a.position)
      : null;
    const embed = baseEmbed(`👤 ${user.tag}`, null, COLORS.info)
      .setThumbnail(user.displayAvatarURL({ size: 256 }))
      .addFields(
        { name: 'User ID', value: user.id, inline: true },
        { name: 'Bot?', value: user.bot ? 'Yes' : 'No', inline: true },
        { name: 'Account created', value: `<t:${Math.floor(user.createdTimestamp / 1000)}:R>`, inline: true },
        { name: 'Joined server', value: member?.joinedTimestamp ? `<t:${Math.floor(member.joinedTimestamp / 1000)}:R>` : 'Not in server', inline: true },
        { name: 'Nickname', value: member?.nickname || 'None', inline: true },
        { name: 'Highest role', value: member ? `${member.roles.highest}` : 'None', inline: true },
        { name: `Roles (${roles?.size || 0})`, value: roles?.size ? truncate(roles.map(String).join(' '), 1024) : 'None', inline: false },
      );
    return interaction.reply({ embeds: [embed] });
  },
});

commands.push({
  category: 'Information',
  data: new SlashCommandBuilder().setName('avatar').setDescription("Show a user's avatar.")
    .addUserOption((option) => option.setName('user').setDescription('User whose avatar you want')),
  async execute(interaction) {
    const user = interaction.options.getUser('user') || interaction.user;
    const url = user.displayAvatarURL({ size: 4096, extension: 'png' });
    const embed = baseEmbed(`🖼️ ${user.username}'s avatar`, `[Open full-size image](${url})`, COLORS.primary).setImage(url);
    return interaction.reply({ embeds: [embed] });
  },
});

commands.push({
  category: 'Information',
  data: new SlashCommandBuilder().setName('banner').setDescription("Show a user's profile banner.")
    .addUserOption((option) => option.setName('user').setDescription('User whose banner you want')),
  async execute(interaction, client) {
    const selected = interaction.options.getUser('user') || interaction.user;
    const user = await client.users.fetch(selected.id, { force: true });
    const url = user.bannerURL({ size: 4096, extension: 'png' });
    if (!url) return replyError(interaction, `${user.tag} does not have a profile banner.`);
    return interaction.reply({ embeds: [baseEmbed(`🎨 ${user.username}'s banner`, `[Open full-size image](${url})`).setImage(url)] });
  },
});

commands.push({
  category: 'Information',
  data: new SlashCommandBuilder().setName('roleinfo').setDescription('Show information about a role.')
    .addRoleOption((option) => option.setName('role').setDescription('Role to inspect').setRequired(true)),
  guildOnly: true,
  async execute(interaction) {
    const role = interaction.options.getRole('role');
    const embed = baseEmbed(`🏷️ ${role.name}`, null, role.color || COLORS.info)
      .addFields(
        { name: 'Role ID', value: role.id, inline: true },
        { name: 'Members', value: String(role.members.size), inline: true },
        { name: 'Position', value: String(role.position), inline: true },
        { name: 'Color', value: role.hexColor, inline: true },
        { name: 'Mentionable', value: role.mentionable ? 'Yes' : 'No', inline: true },
        { name: 'Managed', value: role.managed ? 'Yes' : 'No', inline: true },
        { name: 'Created', value: `<t:${Math.floor(role.createdTimestamp / 1000)}:F>`, inline: false },
      );
    return interaction.reply({ embeds: [embed] });
  },
});

commands.push({
  category: 'Information',
  data: new SlashCommandBuilder().setName('channelinfo').setDescription('Show information about a channel.')
    .addChannelOption((option) => option.setName('channel').setDescription('Channel to inspect')),
  guildOnly: true,
  async execute(interaction) {
    const channel = interaction.options.getChannel('channel') || interaction.channel;
    const embed = baseEmbed(`📺 #${channel.name || 'unknown'}`, null, COLORS.info)
      .addFields(
        { name: 'Channel ID', value: channel.id, inline: true },
        { name: 'Type', value: ChannelType[channel.type] || String(channel.type), inline: true },
        { name: 'Position', value: String(channel.position ?? 'N/A'), inline: true },
        { name: 'Category', value: channel.parent?.name || 'None', inline: true },
        { name: 'NSFW', value: channel.nsfw ? 'Yes' : 'No', inline: true },
        { name: 'Slowmode', value: `${channel.rateLimitPerUser || 0}s`, inline: true },
        { name: 'Created', value: `<t:${Math.floor(channel.createdTimestamp / 1000)}:F>`, inline: false },
      );
    return interaction.reply({ embeds: [embed] });
  },
});

commands.push({
  category: 'Information',
  data: new SlashCommandBuilder().setName('membercount').setDescription('Show the server member count.'),
  guildOnly: true,
  async execute(interaction) {
    await interaction.guild.members.fetch().catch(() => null);
    const members = interaction.guild.members.cache;
    const humans = members.filter((member) => !member.user.bot).size;
    const bots = members.filter((member) => member.user.bot).size;
    return interaction.reply(`👥 **${interaction.guild.memberCount}** total • **${humans}** humans • **${bots}** bots`);
  },
});

commands.push({
  category: 'Information',
  data: new SlashCommandBuilder().setName('uptime').setDescription('Show how long the bot has been online.'),
  async execute(interaction, client) {
    return interaction.reply(`⏱️ I have been online for **${formatDuration(client.uptime || 0)}**.`);
  },
});

commands.push({
  category: 'Utility',
  data: new SlashCommandBuilder().setName('timestamp').setDescription('Turn a date into Discord timestamp formats.')
    .addStringOption((option) => option.setName('date').setDescription('ISO date, e.g. 2026-07-15T17:00:00-03:00').setRequired(true)),
  async execute(interaction) {
    const input = interaction.options.getString('date', true);
    const parsed = Date.parse(input);
    if (!Number.isFinite(parsed)) return replyError(interaction, 'That date could not be parsed. Use an ISO date with an offset, like `2026-07-15T17:00:00-03:00`.');
    const unix = Math.floor(parsed / 1000);
    const formats = ['t', 'T', 'd', 'D', 'f', 'F', 'R'].map((format) => `\`<t:${unix}:${format}>\` → <t:${unix}:${format}>`).join('\n');
    return interaction.reply({ embeds: [baseEmbed('🕒 Discord Timestamp', formats, COLORS.info)] });
  },
});

commands.push({
  category: 'Utility',
  cooldown: 2,
  data: new SlashCommandBuilder().setName('calculate').setDescription('Calculate a basic arithmetic expression.')
    .addStringOption((option) => option.setName('expression').setDescription('Numbers and arithmetic operators only').setRequired(true).setMaxLength(200)),
  async execute(interaction) {
    const expression = interaction.options.getString('expression', true);
    if (!/^[\d\s()+\-*/%.^]+$/.test(expression)) {
      return replyError(interaction, 'Only numbers, spaces, parentheses, decimals, and `+ - * / % ^` are allowed.');
    }
    try {
      const result = evaluate(expression);
      if (typeof result !== 'number' || !Number.isFinite(result)) throw new Error('Result was not finite');
      return interaction.reply(`🧮 \`${expression}\` = **${result}**`);
    } catch {
      return replyError(interaction, 'That expression could not be calculated.');
    }
  },
});

commands.push({
  category: 'Fun',
  data: new SlashCommandBuilder().setName('choose').setDescription('Choose one item from a pipe-separated list.')
    .addStringOption((option) => option.setName('options').setDescription('Example: pizza | tacos | noodles').setRequired(true).setMaxLength(1000)),
  async execute(interaction) {
    const options = interaction.options.getString('options', true).split('|').map((item) => item.trim()).filter(Boolean);
    if (options.length < 2 || options.length > 25) return replyError(interaction, 'Give me between 2 and 25 options separated by `|`.');
    const choice = options[Math.floor(Math.random() * options.length)];
    return interaction.reply(`🎯 I choose: **${truncate(choice, 500)}**`);
  },
});

commands.push({
  category: 'Fun',
  data: new SlashCommandBuilder().setName('coinflip').setDescription('Flip a coin.'),
  async execute(interaction) {
    return interaction.reply(Math.random() < 0.5 ? '🪙 **Heads!**' : '🪙 **Tails!**');
  },
});

commands.push({
  category: 'Fun',
  data: new SlashCommandBuilder().setName('roll').setDescription('Roll dice.')
    .addIntegerOption((option) => option.setName('sides').setDescription('Number of sides').setMinValue(2).setMaxValue(1_000_000))
    .addIntegerOption((option) => option.setName('dice').setDescription('Number of dice').setMinValue(1).setMaxValue(20)),
  async execute(interaction) {
    const sides = interaction.options.getInteger('sides') || 6;
    const dice = interaction.options.getInteger('dice') || 1;
    const rolls = Array.from({ length: dice }, () => Math.floor(Math.random() * sides) + 1);
    const total = rolls.reduce((sum, value) => sum + value, 0);
    return interaction.reply(`🎲 Rolled **${dice}d${sides}**: ${rolls.map((roll) => `\`${roll}\``).join(' ')}${dice > 1 ? ` • Total: **${total}**` : ''}`);
  },
});

commands.push({
  category: 'Fun',
  data: new SlashCommandBuilder().setName('eightball').setDescription('Ask the magic 8-ball a question.')
    .addStringOption((option) => option.setName('question').setDescription('Your question').setRequired(true).setMaxLength(500)),
  async execute(interaction) {
    const answers = [
      'Absolutely.', 'Very likely.', 'Signs point to yes.', 'Maybe 👀', 'Ask again later.',
      'I would not count on it.', 'Probably not.', 'Nope.', 'The Wi-Fi spirits said yes.', 'The code compiles, so yes.',
    ];
    const answer = answers[Math.floor(Math.random() * answers.length)];
    return interaction.reply(`🎱 **${answer}**`);
  },
});

commands.push({
  category: 'Utility',
  cooldown: 10,
  data: new SlashCommandBuilder().setName('poll').setDescription('Create a reaction poll.')
    .addStringOption((option) => option.setName('question').setDescription('Poll question').setRequired(true).setMaxLength(256))
    .addStringOption((option) => option.setName('options').setDescription('2-10 options separated by |').setRequired(true).setMaxLength(1500)),
  guildOnly: true,
  async execute(interaction) {
    const question = interaction.options.getString('question', true);
    const options = interaction.options.getString('options', true).split('|').map((item) => item.trim()).filter(Boolean);
    if (options.length < 2 || options.length > 10) return replyError(interaction, 'A poll needs 2 to 10 options separated by `|`.');
    const emojis = ['1️⃣', '2️⃣', '3️⃣', '4️⃣', '5️⃣', '6️⃣', '7️⃣', '8️⃣', '9️⃣', '🔟'];
    const description = options.map((option, index) => `${emojis[index]} ${truncate(option, 150)}`).join('\n');
    const embed = baseEmbed(`📊 ${question}`, description, COLORS.primary)
      .setFooter({ text: `Poll by ${interaction.user.tag}` });
    const message = await interaction.channel.send({ embeds: [embed], allowedMentions: { parse: [] } });
    for (let index = 0; index < options.length; index += 1) await message.react(emojis[index]);
    return replySuccess(interaction, `Poll created: ${message.url}`);
  },
});

commands.push({
  category: 'Utility',
  cooldown: 3,
  data: new SlashCommandBuilder().setName('remind').setDescription('Set a persistent reminder.')
    .addStringOption((option) => option.setName('duration').setDescription('Examples: 10m, 2h30m, 3d').setRequired(true))
    .addStringOption((option) => option.setName('text').setDescription('What to remind you about').setRequired(true).setMaxLength(1000)),
  async execute(interaction) {
    const durationText = interaction.options.getString('duration', true);
    const duration = parseDuration(durationText);
    if (!duration || duration < 10_000 || duration > 30 * 86_400_000) {
      return replyError(interaction, 'Use a duration from **10 seconds** to **30 days**, like `20m`, `2h30m`, or `3d`.');
    }
    const reminder = db.addReminder({
      userId: interaction.user.id,
      guildId: interaction.guildId,
      channelId: interaction.channelId,
      text: interaction.options.getString('text', true),
      dueAt: Date.now() + duration,
    });
    return replySuccess(interaction, `Reminder **${reminder.id}** set for <t:${Math.floor(reminder.dueAt / 1000)}:F> (<t:${Math.floor(reminder.dueAt / 1000)}:R>).`);
  },
});

commands.push({
  category: 'Utility',
  data: new SlashCommandBuilder().setName('reminders').setDescription('List your active reminders.'),
  async execute(interaction) {
    const reminders = db.getUserReminders(interaction.user.id).slice(0, 15);
    if (reminders.length === 0) return replyError(interaction, 'You have no active reminders.');
    const lines = reminders.map((reminder) => `**${reminder.id}** • <t:${Math.floor(reminder.dueAt / 1000)}:R> • ${truncate(reminder.text, 120)}`);
    return interaction.reply({ embeds: [baseEmbed('⏰ Your Reminders', lines.join('\n'), COLORS.info)], flags: MessageFlags.Ephemeral });
  },
});

commands.push({
  category: 'Utility',
  data: new SlashCommandBuilder().setName('cancelreminder').setDescription('Cancel one of your reminders.')
    .addStringOption((option) => option.setName('id').setDescription('Reminder ID from /reminders').setRequired(true)),
  async execute(interaction) {
    const removed = db.removeReminder(interaction.user.id, interaction.options.getString('id', true));
    if (!removed) return replyError(interaction, 'I could not find that reminder ID.');
    return replySuccess(interaction, `Cancelled reminder **${removed.id}**.`);
  },
});

commands.push({
  category: 'Utility',
  data: new SlashCommandBuilder().setName('afk').setDescription('Set your AFK status.')
    .addStringOption((option) => option.setName('reason').setDescription('Why you are AFK').setMaxLength(300)),
  guildOnly: true,
  async execute(interaction) {
    const reason = interaction.options.getString('reason') || 'AFK';
    db.setAfk(interaction.guildId, interaction.user.id, reason);
    return interaction.reply(`💤 ${interaction.user}, you are now AFK: **${reason}**`);
  },
});

commands.push({
  category: 'Community',
  cooldown: 30,
  data: new SlashCommandBuilder().setName('report').setDescription('Privately report a server member to staff.')
    .addUserOption((option) => option.setName('member').setDescription('Member to report').setRequired(true))
    .addStringOption((option) => option.setName('reason').setDescription('What happened').setRequired(true).setMaxLength(1000)),
  guildOnly: true,
  async execute(interaction) {
    const user = interaction.options.getUser('member', true);
    if (user.id === interaction.user.id) return replyError(interaction, 'You cannot report yourself.');
    const reason = interaction.options.getString('reason', true);
    const sent = await sendLog(interaction.guild, {
      title: '🚩 Member Report',
      color: COLORS.warning,
      fields: [
        { name: 'Reported member', value: `${user.tag} (${user.id})` },
        { name: 'Reported by', value: `${interaction.user.tag} (${interaction.user.id})` },
        { name: 'Channel', value: `${interaction.channel}` },
        { name: 'Reason', value: reason },
      ],
    });
    if (!sent) return replyError(interaction, 'The staff log channel is not configured or cannot be accessed.');
    return replySuccess(interaction, 'Your report was sent privately to staff. Please do not repeatedly submit the same report.');
  },
});

commands.push({
  category: 'Community',
  cooldown: 20,
  data: new SlashCommandBuilder().setName('suggest').setDescription('Send a suggestion for the server.')
    .addStringOption((option) => option.setName('suggestion').setDescription('Your suggestion').setRequired(true).setMaxLength(1500)),
  guildOnly: true,
  async execute(interaction) {
    const config = db.getGuildConfig(interaction.guildId);
    const channelId = config.suggestionsChannelId || config.logChannelId;
    const channel = await interaction.guild.channels.fetch(channelId).catch(() => null);
    if (!channel?.isTextBased()) return replyError(interaction, 'No suggestions channel is configured.');
    const embed = baseEmbed('💡 New Suggestion', interaction.options.getString('suggestion', true), COLORS.primary)
      .setAuthor({ name: interaction.user.tag, iconURL: interaction.user.displayAvatarURL() })
      .setFooter({ text: `User ID: ${interaction.user.id}` });
    const message = await channel.send({ embeds: [embed], allowedMentions: { parse: [] } });
    await message.react('👍').catch(() => null);
    await message.react('👎').catch(() => null);
    return replySuccess(interaction, `Suggestion posted: ${message.url}`);
  },
});

module.exports = commands;
