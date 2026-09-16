using System;
using System.IO;
using System.Text.Json;

namespace Mocha2023.Classes
{
    // The single weekly challenge, loaded from Data/weekly.json and editable from
    // the admin panel (recnet/api/admin/weekly). Missing/invalid file falls back to
    // these defaults, so the endpoint never hard-fails.
    public sealed class WeeklyChallenge
    {
        public int Id { get; set; } = 1;
        public string Title { get; set; } = "Complete Curse of the Crimson Cauldron";
        public string Description { get; set; } = "Complete Curse of the Crimson Cauldron.";
        public string Tooltip { get; set; } = "Complete ^CrimsonCauldron.";
        public string Config { get; set; } = "{\"ct\":6,\"vs\":[2],\"ex\":true,\"in\":true}";
        public int Goal { get; set; } = 1;
        public string RewardEquipmentPrefab { get; set; } = "[Longbow]";
        public string RewardModificationGuid { get; set; } = "XpxqrY6RYkafs-sd3Z0ZLw";
        public int RewardXp { get; set; } = 750;
        public int GiftDropId { get; set; } = 52;
        public string ThemeString { get; set; } = "Mocha Weekly Challenge";

        private static string FilePath => Path.Combine(Program.dataDir, "weekly.json");
        private static readonly object Gate = new();
        private static readonly JsonSerializerOptions ReadOpts =
            new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions WriteOpts =
            new() { WriteIndented = true };

        public static WeeklyChallenge Load()
        {
            try
            {
                lock (Gate)
                {
                    if (File.Exists(FilePath))
                        return JsonSerializer.Deserialize<WeeklyChallenge>(
                            File.ReadAllText(FilePath), ReadOpts) ?? new WeeklyChallenge();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Console.WriteLine("[WeeklyChallenge] load failed, using defaults: " + ex.Message);
            }
            return new WeeklyChallenge();
        }

        public void Save()
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Program.dataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, WriteOpts));
            }
        }
    }
}
