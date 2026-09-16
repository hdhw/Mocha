require('dotenv').config({ quiet: true });

const required = ['DISCORD_TOKEN', 'CLIENT_ID'];
const missing = required.filter((key) => !process.env[key]);

if (missing.length > 0) {
  throw new Error(`Missing environment variable(s): ${missing.join(', ')}. Copy .env.example to .env first.`);
}

module.exports = {
  token: process.env.DISCORD_TOKEN,
  clientId: process.env.CLIENT_ID,
  guildId: process.env.GUILD_ID || null,
  defaultLogChannelId: process.env.LOG_CHANNEL_ID || '1526971616980893947',
  mochaApiUrl: (process.env.MOCHA_API_URL || 'https://playmocha.xyz')
    .replace(/\/+$/, ''),
  mochaRecNetUrl: (process.env.MOCHA_RECNET_URL || process.env.MOCHA_API_URL || 'https://playmocha.xyz')
    .replace(/\/+$/, ''),
  ownerIds: new Set(
    (process.env.OWNER_IDS || '')
      .split(',')
      .map((id) => id.trim())
      .filter(Boolean),
  ),
};
