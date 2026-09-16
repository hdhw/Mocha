const { PermissionFlagsBits } = require('discord.js');

function isGuildOwner(member) {
  return member?.guild?.ownerId === member?.id;
}

function isBotOwner(userId, ownerIds) {
  return ownerIds.has(userId);
}

function canActOn(executor, target) {
  if (!executor || !target) return false;
  if (executor.id === executor.guild.ownerId) return true;
  if (target.id === target.guild.ownerId) return false;
  return executor.roles.highest.comparePositionTo(target.roles.highest) > 0;
}

function botCanActOn(guild, target) {
  const me = guild.members.me;
  if (!me || !target) return false;
  if (target.id === guild.ownerId) return false;
  return me.roles.highest.comparePositionTo(target.roles.highest) > 0;
}

function canManageRole(executor, role) {
  if (!executor || !role || role.managed || role.id === executor.guild.id) return false;
  if (executor.id === executor.guild.ownerId) return true;
  return executor.roles.highest.comparePositionTo(role) > 0;
}

function botCanManageRole(guild, role) {
  const me = guild.members.me;
  if (!me || !role || role.managed || role.id === guild.id) return false;
  return me.roles.highest.comparePositionTo(role) > 0;
}

function isModerator(member) {
  return member.permissions.has([
    PermissionFlagsBits.ManageMessages,
    PermissionFlagsBits.ModerateMembers,
  ], false);
}

module.exports = {
  isGuildOwner,
  isBotOwner,
  canActOn,
  botCanActOn,
  canManageRole,
  botCanManageRole,
  isModerator,
};
