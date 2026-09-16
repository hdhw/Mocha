const {
  Client,
  Collection,
  Events,
  GatewayIntentBits,
  Partials,
  ActivityType,
} = require('discord.js');
const commands = require('./commands');
const { token, defaultLogChannelId } = require('./config');
const { registerEvents } = require('./events/registerEvents');
const { startReminderService } = require('./services/reminders');

const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMembers,
    GatewayIntentBits.GuildModeration,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
    GatewayIntentBits.GuildVoiceStates,
  ],
  partials: [
    Partials.Message,
    Partials.Channel,
    Partials.GuildMember,
    Partials.User,
  ],
  presence: {
    status: 'online',
    activities: [{ name: '/help • Multi Utility', type: ActivityType.Watching }],
  },
});

client.commands = new Collection();
for (const command of commands) {
  if (!command?.data?.name || typeof command.execute !== 'function') {
    console.warn('Skipped an invalid command module entry:', command);
    continue;
  }
  if (client.commands.has(command.data.name)) {
    throw new Error(`Duplicate command name: ${command.data.name}`);
  }
  client.commands.set(command.data.name, command);
}

registerEvents(client);

client.once(Events.ClientReady, (readyClient) => {
  console.log(`Logged in as ${readyClient.user.tag}`);
  console.log(`Loaded ${readyClient.commands.size} slash commands.`);
  console.log(`Default log channel: ${defaultLogChannelId}`);
  console.log(`Connected to ${readyClient.guilds.cache.size} server(s).`);
  startReminderService(readyClient);
});

process.on('unhandledRejection', (error) => console.error('Unhandled promise rejection:', error));
process.on('uncaughtException', (error) => console.error('Uncaught exception:', error));

client.login(token);
