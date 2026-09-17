using Mocha2023.Classes.DBs.Postgres;
using Mocha2023.Classes;

namespace Mocha2023.Classes.DBs;

public static class AnticheatLogDB
{
    public class AnticheatEntry
    {
        public long Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string IpAddress { get; set; } = "unknown";
        public long? AccountId { get; set; }
        public string? SteamId { get; set; }
        public string? Build { get; set; }
        public string Flags { get; set; } = "";
        public string? UserAgent { get; set; }
    }

    public static readonly PostgresCollection<AnticheatEntry> Entries = new(
        PostgresConfig.ConnectionString,
        tableName: "anticheat_entries",
        idColumn: "id",
        idSelector: e => e.Id,
        extraColumns: new (string, Func<AnticheatEntry, object?>)[]
        {
            ("account_id", e => e.AccountId)
        });

    private const int MaxStored = 20_000;
    private const int TrimBatchSize = 1_000;

    static AnticheatLogDB()
    {
        Entries.EnsureIndex("account_id");
    }

    public static int MigrateFromLiteDb()
    {
        string path = Path.Combine(Program.dataDir, "DBs", "AnticheatLogs.db");
        if (!File.Exists(path))
            return 0;

        using var db = new LiteDB.LiteDatabase($"Filename={path};Connection=shared;ReadOnly=true");
        int count = 0;
        foreach (LiteDB.BsonDocument doc in db.GetCollection("Entries").FindAll())
        {
            Entries.Upsert(LiteDbMigrationTool.ReadWithRecoveredId<AnticheatEntry>(doc, "Id"));
            count++;
        }
        return count;
    }

    public static void Log(
        string ipAddress,
        long? accountId,
        string? steamId,
        string? build,
        string flags,
        string? userAgent)
    {
        var entry = new AnticheatEntry
        {
            Id = Entries.NextId("anticheat_entries_id_seq"),
            IpAddress = IpHasher.Hash(ipAddress),
            AccountId = accountId,
            SteamId = steamId,
            Build = build,
            Flags = flags,
            UserAgent = userAgent
        };
        Entries.Insert(entry);

        long count = Entries.LongCount();
        if (count > MaxStored)
        {
            List<long> oldestIds = Entries.Query()
                .OrderBy(a => a.Id)
                .Limit(TrimBatchSize)
                .ToList()
                .Select(a => a.Id)
                .ToList();

            foreach (long id in oldestIds)
                Entries.Delete(id);
        }
    }
}
