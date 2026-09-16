const db = require('../database');
const { baseEmbed, COLORS } = require('../utils/embeds');

async function deliverReminder(client, reminder) {
  const embed = baseEmbed('⏰ Reminder', reminder.text, COLORS.primary)
    .setFooter({ text: `Reminder ${reminder.id}` });

  if (reminder.guildId && reminder.channelId) {
    const guild = await client.guilds.fetch(reminder.guildId).catch(() => null);
    const channel = guild ? await guild.channels.fetch(reminder.channelId).catch(() => null) : null;
    if (channel?.isTextBased()) {
      const sent = await channel.send({
        content: `<@${reminder.userId}>`,
        embeds: [embed],
        allowedMentions: { users: [reminder.userId] },
      }).then(() => true).catch(() => false);
      if (sent) return;
    }
  }

  const user = await client.users.fetch(reminder.userId).catch(() => null);
  if (user) await user.send({ embeds: [embed] }).catch(() => null);
}

function startReminderService(client) {
  const run = async () => {
    const due = db.takeDueReminders();
    for (const reminder of due) await deliverReminder(client, reminder);
  };
  run().catch(console.error);
  const interval = setInterval(() => run().catch(console.error), 15_000);
  interval.unref();
}

module.exports = { startReminderService };
