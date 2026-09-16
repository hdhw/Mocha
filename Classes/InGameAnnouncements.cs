using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mocha2023.Classes
{
    // The real in-game announcement feed - read by /api/announcement/v1/get and
    // watched live by NotiController's FileSystemWatcher, so any write here
    // pushes to connected clients immediately. Distinct from the Community
    // Board announcements system (RecNetDB.Announcement), which is a separate,
    // site-wide feed.
    public sealed class InGameAnnouncement
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        // Relative path under Images/, e.g. "AnnouncementImages/xyz.png" -
        // matches ClubAnnouncementRecord's ImageName convention.
        public string ImageName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class InGameAnnouncements
    {
        private static string FilePath => Path.Combine(Program.dataDir, "announcements.json");
        private static readonly object Gate = new();
        private static readonly JsonSerializerOptions ReadOpts =
            new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions WriteOpts =
            new() { WriteIndented = true };

        public static List<InGameAnnouncement> Load()
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(FilePath))
                        return new List<InGameAnnouncement>();
                    string json = File.ReadAllText(FilePath);
                    if (string.IsNullOrWhiteSpace(json))
                        return new List<InGameAnnouncement>();
                    return JsonSerializer.Deserialize<List<InGameAnnouncement>>(json, ReadOpts)
                           ?? new List<InGameAnnouncement>();
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    Console.WriteLine("[InGameAnnouncements] load failed: " + ex.Message);
                    return new List<InGameAnnouncement>();
                }
            }
        }

        private static void Save(List<InGameAnnouncement> announcements)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Program.dataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(announcements, WriteOpts));
            }
        }

        public static InGameAnnouncement Create(string title, string body, string imageName)
        {
            lock (Gate)
            {
                List<InGameAnnouncement> all = Load();
                long nextId = all.Count == 0 ? 1 : all.Max(a => a.Id) + 1;
                var entry = new InGameAnnouncement
                {
                    Id = nextId,
                    Title = title,
                    Body = body,
                    ImageName = imageName,
                    CreatedAt = DateTime.UtcNow
                };
                all.Add(entry);
                Save(all);
                return entry;
            }
        }

        public static InGameAnnouncement? Update(long id, string title, string body, string imageName)
        {
            lock (Gate)
            {
                List<InGameAnnouncement> all = Load();
                InGameAnnouncement? entry = all.FirstOrDefault(a => a.Id == id);
                if (entry == null)
                    return null;
                entry.Title = title;
                entry.Body = body;
                entry.ImageName = imageName;
                Save(all);
                return entry;
            }
        }

        public static bool Delete(long id)
        {
            lock (Gate)
            {
                List<InGameAnnouncement> all = Load();
                int removed = all.RemoveAll(a => a.Id == id);
                if (removed == 0)
                    return false;
                Save(all);
                return true;
            }
        }
    }
}
