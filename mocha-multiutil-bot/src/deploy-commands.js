const { REST, Routes } = require('discord.js');
const commands = require('./commands');
const { token, clientId, guildId } = require('./config');

const body = commands.map((command) => command.data.toJSON());
const rest = new REST({ version: '10' }).setToken(token);

async function deploy() {
  console.log(`Deploying ${body.length} slash commands…`);
  if (guildId) {
    await rest.put(Routes.applicationGuildCommands(clientId, guildId), { body });
    console.log(`Deployed guild commands to ${guildId}.`);
  } else {
    await rest.put(Routes.applicationCommands(clientId), { body });
    console.log('Deployed global commands.');
  }
}

deploy().catch((error) => {
  console.error('Command deployment failed:', error);
  process.exitCode = 1;
});
