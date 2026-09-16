const {
  SlashCommandBuilder,
} = require('discord.js');
const {
  COLORS,
  baseEmbed,
  errorEmbed,
} = require('../utils/embeds');
const { truncate } = require('../utils/text');
const mocha = require('../services/mocha');

const commands = [];

function playerName(player) {
  return player.displayName || player.username || `Player ${player.accountId}`;
}

function roomName(room) {
  return `^${String(room.name || 'UntitledRoom').replace(/^\^/, '')}`;
}

function discordTime(value, style = 'R') {
  const timestamp = Math.floor(new Date(value).getTime() / 1000);
  return Number.isFinite(timestamp) ? `<t:${timestamp}:${style}>` : 'Unknown';
}

async function fail(interaction, error) {
  const message = error instanceof mocha.MochaApiError
    ? error.message
    : 'The Mocha lookup failed unexpectedly.';
  return interaction.editReply({ embeds: [errorEmbed(message)] });
}

function playerEmbed(player) {
  const roles = Array.isArray(player.roles)
    ? player.roles.filter((role) => role !== 'RRPlus').join(', ')
    : '';
  const embed = baseEmbed(
    playerName(player),
    truncate(player.bio || 'This player has not added a bio yet.', 500),
    player.verified ? COLORS.info : COLORS.primary,
  )
    .setURL(mocha.recNetLink(`user/${player.accountId}`))
    .addFields(
      { name: 'Username', value: `@${player.username || 'unknown'}`, inline: true },
      { name: 'Account ID', value: String(player.accountId), inline: true },
      { name: 'Level', value: String(player.level ?? 0), inline: true },
      { name: 'Photos', value: String(player.photoCount ?? 0), inline: true },
      { name: 'RR+', value: player.hasRRPlus ? 'Yes' : 'No', inline: true },
      { name: 'Joined', value: discordTime(player.createdAt), inline: true },
    );

  if (roles) embed.addFields({ name: 'Roles', value: truncate(roles, 1024) });
  if (player.profileImage) embed.setThumbnail(player.profileImage);
  if (player.bannerImage) embed.setImage(player.bannerImage);
  return embed;
}

function roomEmbed(room) {
  const stats = room.stats || {};
  const tags = Array.isArray(room.tags) && room.tags.length
    ? room.tags.slice(0, 15).map((tag) => `\`${tag}\``).join(' ')
    : 'No tags';
  const embed = baseEmbed(
    roomName(room),
    truncate(room.description || 'No room description has been added.', 1000),
    COLORS.primary,
  )
    .setURL(mocha.recNetLink(`room/${room.roomId}`))
    .addFields(
      { name: 'Creator', value: room.creatorName || `Player ${room.creatorAccountId}`, inline: true },
      { name: 'Room ID', value: String(room.roomId), inline: true },
      { name: 'Capacity', value: String(room.maxPlayers ?? 0), inline: true },
      { name: 'Visits', value: Number(stats.visits || 0).toLocaleString(), inline: true },
      { name: 'Visitors', value: Number(stats.visitors || 0).toLocaleString(), inline: true },
      { name: 'Cheers', value: Number(stats.cheers || 0).toLocaleString(), inline: true },
      { name: 'Tags', value: tags },
    );
  if (room.image) embed.setImage(room.image);
  return embed;
}

commands.push({
  category: 'Mocha',
  cooldown: 3,
  data: new SlashCommandBuilder()
    .setName('player')
    .setDescription('Search Mocha player profiles and photos.')
    .addSubcommand((subcommand) => subcommand
      .setName('search')
      .setDescription('Search for a Mocha player by username, display name, or account ID')
      .addStringOption((option) => option
        .setName('username')
        .setDescription('Username, display name, or account ID')
        .setRequired(true)
        .setMaxLength(64)))
    .addSubcommand((subcommand) => subcommand
      .setName('photos')
      .setDescription("Show a Mocha player's newest photos")
      .addStringOption((option) => option
        .setName('username')
        .setDescription('Username, display name, or account ID')
        .setRequired(true)
        .setMaxLength(64))),
  async execute(interaction) {
    await interaction.deferReply();
    const query = interaction.options.getString('username', true).trim();

    try {
      if (interaction.options.getSubcommand() === 'photos') {
        const player = await mocha.getPlayer(query);
        if (!player) return interaction.editReply({ embeds: [errorEmbed('No Mocha player matched that search.')] });
        const photos = Array.isArray(player.photos) ? player.photos.slice(0, 5) : [];
        if (!photos.length) {
          return interaction.editReply({
            embeds: [baseEmbed(
              `${playerName(player)}'s photos`,
              'This player has not shared any public photos yet.',
              COLORS.neutral,
            ).setURL(mocha.recNetLink(`user/${player.accountId}`))],
          });
        }

        const embed = baseEmbed(
          `${playerName(player)}'s newest photos`,
          photos.map((photo, index) => (
            `**${index + 1}.** [Open photo](${photo.url}) · ${discordTime(photo.takenAt)}`
          )).join('\n'),
          COLORS.primary,
        )
          .setURL(mocha.recNetLink(`user/${player.accountId}`))
          .setImage(photos[0].url);
        if (player.profileImage) embed.setThumbnail(player.profileImage);
        return interaction.editReply({ embeds: [embed] });
      }

      const results = await mocha.searchPlayers(query);
      if (!results.length) {
        return interaction.editReply({ embeds: [errorEmbed('No Mocha player matched that search.')] });
      }

      const exact = mocha.exactPlayerMatch(results, query);
      if (exact) {
        const player = await mocha.getPlayer(exact.accountId);
        return interaction.editReply({ embeds: [playerEmbed(player)] });
      }

      const lines = results.slice(0, 10).map((player) => (
        `**[${playerName(player)}](${mocha.recNetLink(`user/${player.accountId}`)})** `
        + `· @${player.username || 'unknown'} · Level ${player.level ?? 0} · ${player.photoCount ?? 0} photos`
      ));
      return interaction.editReply({
        embeds: [baseEmbed(
          `Player results for "${truncate(query, 80)}"`,
          lines.join('\n'),
          COLORS.info,
        ).setFooter({ text: `${results.length} result(s) · showing up to 10` })],
      });
    } catch (error) {
      return fail(interaction, error);
    }
  },
});

commands.push({
  category: 'Mocha',
  cooldown: 3,
  data: new SlashCommandBuilder()
    .setName('room')
    .setDescription('Search and inspect Mocha rooms.')
    .addSubcommand((subcommand) => subcommand
      .setName('search')
      .setDescription('Search Mocha rooms by name, description, or tag')
      .addStringOption((option) => option
        .setName('query')
        .setDescription('Room name, description, or tag')
        .setRequired(true)
        .setMaxLength(100)))
    .addSubcommand((subcommand) => subcommand
      .setName('info')
      .setDescription('Show details for a room name or ID')
      .addStringOption((option) => option
        .setName('room')
        .setDescription('Room name or numeric room ID')
        .setRequired(true)
        .setMaxLength(100)))
    .addSubcommand((subcommand) => subcommand
      .setName('random')
      .setDescription('Pick a random public Mocha room')),
  async execute(interaction) {
    await interaction.deferReply();
    const subcommand = interaction.options.getSubcommand();

    try {
      if (subcommand === 'random') {
        const rooms = await mocha.searchRooms();
        if (!rooms.length) return interaction.editReply({ embeds: [errorEmbed('Mocha has no public rooms right now.')] });
        return interaction.editReply({
          embeds: [roomEmbed(rooms[Math.floor(Math.random() * rooms.length)])],
        });
      }

      const query = interaction.options.getString(subcommand === 'info' ? 'room' : 'query', true).trim();
      if (subcommand === 'info') {
        const room = await mocha.getRoom(query);
        if (!room) return interaction.editReply({ embeds: [errorEmbed('No Mocha room matched that search.')] });
        return interaction.editReply({ embeds: [roomEmbed(room)] });
      }

      const rooms = await mocha.searchRooms(query);
      if (!rooms.length) return interaction.editReply({ embeds: [errorEmbed('No Mocha room matched that search.')] });
      const lines = rooms.slice(0, 10).map((room) => (
        `**[${roomName(room)}](${mocha.recNetLink(`room/${room.roomId}`)})** `
        + `· by ${room.creatorName || 'Unknown'} · ${Number(room.stats?.visits || 0).toLocaleString()} visits`
      ));
      return interaction.editReply({
        embeds: [baseEmbed(
          `Room results for "${truncate(query, 80)}"`,
          lines.join('\n'),
          COLORS.info,
        ).setFooter({ text: `${rooms.length} result(s) · showing up to 10` })],
      });
    } catch (error) {
      return fail(interaction, error);
    }
  },
});

commands.push({
  category: 'Mocha',
  cooldown: 5,
  data: new SlashCommandBuilder()
    .setName('mocha')
    .setDescription('View live Mocha server and community information.')
    .addSubcommand((subcommand) => subcommand
      .setName('status')
      .setDescription('Show live Mocha server statistics'))
    .addSubcommand((subcommand) => subcommand
      .setName('events')
      .setDescription('Show upcoming Mocha community events'))
    .addSubcommand((subcommand) => subcommand
      .setName('photo')
      .setDescription('Show a recent Mocha community photo')
      .addStringOption((option) => option
        .setName('mode')
        .setDescription('Choose the newest photo or a random recent photo')
        .addChoices(
          { name: 'Newest', value: 'newest' },
          { name: 'Random recent', value: 'random' },
        ))),
  async execute(interaction) {
    await interaction.deferReply();
    const subcommand = interaction.options.getSubcommand();

    try {
      if (subcommand === 'status') {
        const status = await mocha.getStatus();
        const embed = baseEmbed(
          'Mocha Server Status',
          status.status === 'online'
            ? 'The Mocha server is online and responding.'
            : `Server status: **${status.status || 'Unknown'}**`,
          status.status === 'online' ? COLORS.success : COLORS.warning,
        )
          .setURL(mocha.recNetLink())
          .addFields(
            { name: 'Online now', value: Number(status.onlinePlayers || 0).toLocaleString(), inline: true },
            { name: 'Live sockets', value: Number(status.connectedSockets || 0).toLocaleString(), inline: true },
            { name: 'Registered players', value: Number(status.registeredPlayers || 0).toLocaleString(), inline: true },
            { name: 'Rooms', value: Number(status.rooms || 0).toLocaleString(), inline: true },
            { name: 'Photos', value: Number(status.photos || 0).toLocaleString(), inline: true },
            { name: 'Updated', value: discordTime(status.generatedAt), inline: true },
          );
        return interaction.editReply({ embeds: [embed] });
      }

      if (subcommand === 'events') {
        const events = await mocha.getEvents();
        if (!events.length) {
          return interaction.editReply({
            embeds: [baseEmbed('Upcoming Mocha Events', 'No upcoming events are scheduled.', COLORS.neutral)],
          });
        }
        const lines = events.slice(0, 10).map((event) => (
          `${event.pinned ? '**Pinned** · ' : ''}**${truncate(event.title, 80)}** · ${discordTime(event.startsAt, 'F')}\n`
          + `${truncate(event.description || 'No description.', 180)}`
        ));
        const embed = baseEmbed('Upcoming Mocha Events', lines.join('\n\n'), COLORS.info)
          .setURL(mocha.recNetLink());
        const eventImage = events.find((event) => event.image)?.image;
        if (eventImage) embed.setThumbnail(eventImage);
        return interaction.editReply({ embeds: [embed] });
      }

      const photos = await mocha.getPhotos(25);
      if (!photos.length) return interaction.editReply({ embeds: [errorEmbed('No public Mocha photos are available.')] });
      const mode = interaction.options.getString('mode') || 'newest';
      const photo = mode === 'random'
        ? photos[Math.floor(Math.random() * photos.length)]
        : photos[0];
      const owner = photo.owner;
      const embed = baseEmbed(
        owner ? `Photo by ${playerName(owner)}` : 'Mocha Community Photo',
        `${discordTime(photo.takenAt)} · [Open full image](${photo.url})`,
        COLORS.primary,
      ).setImage(photo.url);
      if (owner?.accountId) embed.setURL(mocha.recNetLink(`user/${owner.accountId}`));
      if (owner?.profileImage) embed.setThumbnail(owner.profileImage);
      return interaction.editReply({ embeds: [embed] });
    } catch (error) {
      return fail(interaction, error);
    }
  },
});

module.exports = commands;
