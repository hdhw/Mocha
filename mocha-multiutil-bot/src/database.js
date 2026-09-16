const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { defaultLogChannelId } = require('./config');

const dataDirectory = path.join(__dirname, '..', 'data');
const databasePath = path.join(dataDirectory, 'database.json');
const tempPath = `${databasePath}.tmp`;

const defaultData = {
  guilds: {},
  warnings: {},
  reminders: [],
  afk: {},
  commandStats: {},
};

function defaultGuildConfig() {
  return {
    logChannelId: defaultLogChannelId,
    welcomeChannelId: null,
    welcomeMessage: 'Welcome {user} to **{server}**! You are member **#{count}** 🎉',
    goodbyeChannelId: null,
    goodbyeMessage: '**{tag}** left **{server}**. We now have **{count}** members.',
    autoroleId: null,
    suggestionsChannelId: null,
    supportRoleId: null,
    ticketCategoryId: null,
    automod: {
      invites: false,
      links: false,
      caps: false,
      spam: false,
    },
  };
}

class Database {
  constructor() {
    fs.mkdirSync(dataDirectory, { recursive: true });
    this.data = structuredClone(defaultData);
    this.load();
  }

  load() {
    if (!fs.existsSync(databasePath)) {
      this.save();
      return;
    }

    try {
      const parsed = JSON.parse(fs.readFileSync(databasePath, 'utf8'));
      this.data = {
        ...structuredClone(defaultData),
        ...parsed,
        guilds: parsed.guilds || {},
        warnings: parsed.warnings || {},
        reminders: Array.isArray(parsed.reminders) ? parsed.reminders : [],
        afk: parsed.afk || {},
        commandStats: parsed.commandStats || {},
      };
    } catch (error) {
      const backup = `${databasePath}.broken-${Date.now()}`;
      fs.copyFileSync(databasePath, backup);
      console.error(`Database JSON was invalid. Backed it up to ${backup}`, error);
      this.data = structuredClone(defaultData);
      this.save();
    }
  }

  save() {
    fs.writeFileSync(tempPath, JSON.stringify(this.data, null, 2), 'utf8');
    fs.renameSync(tempPath, databasePath);
  }

  getGuildConfig(guildId) {
    const existing = this.data.guilds[guildId] || {};
    const merged = {
      ...defaultGuildConfig(),
      ...existing,
      automod: {
        ...defaultGuildConfig().automod,
        ...(existing.automod || {}),
      },
    };
    this.data.guilds[guildId] = merged;
    return merged;
  }

  updateGuildConfig(guildId, patch) {
    const current = this.getGuildConfig(guildId);
    this.data.guilds[guildId] = {
      ...current,
      ...patch,
      automod: patch.automod ? { ...current.automod, ...patch.automod } : current.automod,
    };
    this.save();
    return this.data.guilds[guildId];
  }

  addWarning(guildId, userId, moderatorId, reason) {
    this.data.warnings[guildId] ||= {};
    this.data.warnings[guildId][userId] ||= [];
    const warning = {
      id: crypto.randomBytes(3).toString('hex').toUpperCase(),
      moderatorId,
      reason,
      createdAt: Date.now(),
    };
    this.data.warnings[guildId][userId].push(warning);
    this.save();
    return warning;
  }

  getWarnings(guildId, userId) {
    return this.data.warnings[guildId]?.[userId] || [];
  }

  removeWarning(guildId, userId, warningId) {
    const warnings = this.getWarnings(guildId, userId);
    const index = warnings.findIndex((warning) => warning.id === warningId.toUpperCase());
    if (index === -1) return null;
    const [removed] = warnings.splice(index, 1);
    this.save();
    return removed;
  }

  clearWarnings(guildId, userId) {
    const count = this.getWarnings(guildId, userId).length;
    if (this.data.warnings[guildId]) delete this.data.warnings[guildId][userId];
    this.save();
    return count;
  }

  addReminder(reminder) {
    const stored = {
      id: crypto.randomBytes(4).toString('hex').toUpperCase(),
      createdAt: Date.now(),
      ...reminder,
    };
    this.data.reminders.push(stored);
    this.save();
    return stored;
  }

  getUserReminders(userId) {
    return this.data.reminders
      .filter((reminder) => reminder.userId === userId)
      .sort((a, b) => a.dueAt - b.dueAt);
  }

  removeReminder(userId, reminderId) {
    const index = this.data.reminders.findIndex(
      (reminder) => reminder.userId === userId && reminder.id === reminderId.toUpperCase(),
    );
    if (index === -1) return null;
    const [removed] = this.data.reminders.splice(index, 1);
    this.save();
    return removed;
  }

  takeDueReminders(now = Date.now()) {
    const due = this.data.reminders.filter((reminder) => reminder.dueAt <= now);
    if (due.length === 0) return [];
    const dueIds = new Set(due.map((reminder) => reminder.id));
    this.data.reminders = this.data.reminders.filter((reminder) => !dueIds.has(reminder.id));
    this.save();
    return due;
  }

  setAfk(guildId, userId, reason) {
    this.data.afk[guildId] ||= {};
    this.data.afk[guildId][userId] = { reason, since: Date.now() };
    this.save();
    return this.data.afk[guildId][userId];
  }

  getAfk(guildId, userId) {
    return this.data.afk[guildId]?.[userId] || null;
  }

  clearAfk(guildId, userId) {
    if (!this.data.afk[guildId]?.[userId]) return null;
    const old = this.data.afk[guildId][userId];
    delete this.data.afk[guildId][userId];
    this.save();
    return old;
  }

  incrementCommand(guildId, commandName) {
    const scope = guildId || 'DM';
    this.data.commandStats[scope] ||= {};
    this.data.commandStats[scope][commandName] = (this.data.commandStats[scope][commandName] || 0) + 1;
    this.save();
  }

  getCommandStats(guildId) {
    return this.data.commandStats[guildId] || {};
  }
}

module.exports = new Database();
