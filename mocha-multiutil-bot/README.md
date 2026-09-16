# Mocha Multi-Utility Discord Bot 🤖

A modular Node.js Discord bot with slash commands, moderation, persistent warnings/reminders, automod, tickets, welcome messages, autoroles, server configuration, and detailed logging.

The default logging channel is already set to:

```text
1526971616980893947
```

You can change it per server later with `/config logs`.

## Requirements

- Node.js 18 or newer
- A Discord application and bot token
- A server where you can invite the bot

## Fast setup on Windows

1. Open the [Discord Developer Portal](https://discord.com/developers/applications) and create an application.
2. Open **Bot**, create the bot, and copy its token.
3. In **Bot → Privileged Gateway Intents**, enable:
   - **Server Members Intent**
   - **Message Content Intent**
4. Open **OAuth2 → URL Generator**.
5. Select the scopes `bot` and `applications.commands`.
6. Give the bot these permissions:
   - View Channels
   - Send Messages
   - Embed Links
   - Attach Files
   - Read Message History
   - Add Reactions
   - Manage Messages
   - Manage Channels
   - Manage Roles
   - Moderate Members
   - Kick Members
   - Ban Members
   - View Audit Log
   - Move Members
   - Mute Members
   - Deafen Members
7. Invite it and move its role **above every role it needs to manage**.
8. Copy `.env.example` to `.env` and fill in the values.
9. Run:

```bat
npm install
npm run deploy
npm start
```

For testing, put your server ID in `GUILD_ID`. Guild commands update immediately. When ready to deploy globally, clear `GUILD_ID` and run `npm run deploy` again.

## Windows helper files

- `setup.bat` installs dependencies and creates `.env` if missing.
- `deploy.bat` registers slash commands.
- `start.bat` starts the bot.

## Systems included

### Logging

The bot logs slash-command usage plus:

- Deleted, edited, and bulk-deleted messages
- Member joins, leaves, nickname changes, roles, and timeouts
- Bans and unbans
- Channel creation, deletion, and updates
- Role creation, deletion, permission changes, and updates
- Voice joins, leaves, moves, camera/stream changes, server mutes, and server deafens
- Moderation actions, configuration changes, automod actions, reports, and tickets

### General, information, community, and fun

- `/ping`
- `/help`
- `/botinfo`
- `/serverinfo`
- `/userinfo`
- `/avatar`
- `/banner`
- `/roleinfo`
- `/channelinfo`
- `/membercount`
- `/uptime`
- `/timestamp`
- `/calculate`
- `/choose`
- `/coinflip`
- `/roll`
- `/eightball`
- `/poll`
- `/remind`
- `/reminders`
- `/cancelreminder`
- `/afk`
- `/report`
- `/suggest`

### Mocha / RecNet

- `/player search username:<name or ID>` — search player profiles
- `/player photos username:<name or ID>` — show a player's newest public photos
- `/room search query:<text>` — search public rooms by name, description, or tag
- `/room info room:<name or ID>` — show room details and statistics
- `/room random` — pick a random public room
- `/mocha status` — show live server, player, room, and photo totals
- `/mocha events` — show upcoming community events
- `/mocha photo [mode]` — show the newest or a random recent community photo

Set `MOCHA_API_URL` in `.env` to the public URL of the Mocha server. If RecNet
uses a different public URL, set `MOCHA_RECNET_URL` too. These commands only
read the public `/recnet/api` endpoints and do not require an admin login.

### Moderation

- `/kick`
- `/ban`
- `/unban`
- `/timeout`
- `/untimeout`
- `/warn`
- `/warnings`
- `/removewarning`
- `/clearwarnings`
- `/purge`
- `/slowmode`
- `/lock`
- `/unlock`
- `/nickname`
- `/role add`
- `/role remove`
- `/move`
- `/disconnect`
- `/voicemute mute`
- `/voicemute unmute`
- `/voicedeafen deafen`
- `/voicedeafen undeafen`

### Administration

- `/announce`
- `/say`
- `/embed`
- `/config view`
- `/config logs`
- `/config welcome`
- `/config goodbye`
- `/config autorole`
- `/config suggestions`
- `/config support-role`
- `/config ticket-category`
- `/config clear`
- `/automod view`
- `/automod set`
- `/ticketpanel`
- `/audit`
- `/commandstats`

### Bot-owner commands

- `/botstatus`
- `/reload-data`

Bot-owner access uses `OWNER_IDS` from `.env`. The application owner is also recognized automatically.

## Welcome and goodbye placeholders

Messages can contain:

- `{user}` — member mention
- `{tag}` — username/tag
- `{server}` — server name
- `{count}` — current member count

Example:

```text
Welcome {user} to **{server}**! You are member #{count} 🎉
```

## Automod modules

Use `/automod set` to independently enable:

- Discord invite filtering
- External link filtering
- Excessive-capital filtering
- Basic rapid-message spam filtering

Administrators and members with **Manage Messages** bypass automod.

## Tickets

1. Run `/config ticket-category`.
2. Optionally run `/config support-role`.
3. Run `/ticketpanel` and choose the panel channel.
4. Members can create one open private ticket at a time.
5. The ticket owner or staff can close it; staff can then delete it.

## Persistent data

The bot stores data in `data/database.json`:

- Per-server configuration
- Warnings
- Reminders
- AFK states
- Command statistics

Do not commit `.env` or `data/database.json` to a public repository.

## Useful scripts

```bash
npm run deploy   # Register slash commands
npm start        # Start normally
npm run dev      # Start with Node watch mode
npm run check    # Syntax-check every source file
```
