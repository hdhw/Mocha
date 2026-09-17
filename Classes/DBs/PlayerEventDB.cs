using Mocha2023.Classes.DBs.Postgres;

namespace Mocha2023.Classes.DBs
{
    public static class PlayerEventDB
    {
        private static readonly PostgresCollection<PlayerEvent> Events = new(
            PostgresConfig.ConnectionString,
            tableName: "player_events",
            idColumn: "event_id",
            idSelector: e => e.EventId,
            extraColumns: new (string, Func<PlayerEvent, object?>)[]
            {
                ("creator_account_id", e => e.CreatorAccountId),
                ("club_id", e => e.ClubId),
                ("starts_at", e => e.StartsAt)
            });

        private static readonly PostgresCollection<PlayerEventResponse> Responses = new(
            PostgresConfig.ConnectionString,
            tableName: "player_event_responses",
            idColumn: "id",
            idSelector: r => r.Id,
            extraColumns: new (string, Func<PlayerEventResponse, object?>)[]
            {
                ("event_id", r => r.EventId),
                ("account_id", r => r.AccountId)
            });

        private static readonly object Sync = new();

        static PlayerEventDB()
        {
            Events.EnsureIndex("creator_account_id");
            Events.EnsureIndex("club_id");
            Events.EnsureIndex("starts_at");
            Responses.EnsureIndex("event_id");
            Responses.EnsureIndex("account_id");
        }

        public static int MigrateFromLiteDb()
        {
            string path = Path.Combine(Program.dataDir, "DBs", "PlayerEvents.db");
            if (!File.Exists(path))
                return 0;

            using var db = new LiteDB.LiteDatabase($"Filename={path};Connection=shared;ReadOnly=true");
            int count = 0;
            foreach (LiteDB.BsonDocument doc in db.GetCollection("Events").FindAll())
            {
                Events.Upsert(LiteDbMigrationTool.ReadWithRecoveredId<PlayerEvent>(doc, "EventId"));
                count++;
            }
            foreach (LiteDB.BsonDocument doc in db.GetCollection("Responses").FindAll())
            {
                Responses.Upsert(LiteDbMigrationTool.ReadWithRecoveredId<PlayerEventResponse>(doc, "Id"));
                count++;
            }
            return count;
        }

        public sealed class PlayerEvent
        {
            public long EventId { get; set; }
            public long CreatorAccountId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string ImageName { get; set; } = string.Empty;
            public DateTime StartsAt { get; set; }
            public DateTime? EndsAt { get; set; }
            public long? RoomId { get; set; }
            public long? ClubId { get; set; }

            public string Accessibility { get; set; } = "Public";
            public bool MultiInstance { get; set; }
            public List<string> Tags { get; set; } = new();
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public sealed class PlayerEventResponse
        {
            public string Id { get; set; } = string.Empty;
            public long EventId { get; set; }
            public long AccountId { get; set; }

            public string ResponseType { get; set; } = "Going";
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        private static string ResponseId(long eventId, long accountId) => $"{eventId}:{accountId}";

        public static long GetNextEventId()
        {
            lock (Sync)
                return Events.NextId("player_events_event_id_seq");
        }

        public static PlayerEvent Create(PlayerEvent evt)
        {
            lock (Sync)
            {
                evt.EventId = GetNextEventId();
                evt.CreatedAt = DateTime.UtcNow;
                evt.UpdatedAt = evt.CreatedAt;
                Events.Insert(evt);
                return evt;
            }
        }

        public static PlayerEvent? Get(long eventId) => Events.FindById(eventId);

        public static List<PlayerEvent> GetByIds(IEnumerable<long> ids)
        {
            var idSet = ids.ToHashSet();
            return Events.Find(value => idSet.Contains(value.EventId)).ToList();
        }

        public static List<PlayerEvent> GetByCreator(long accountId) =>
            Events.Find(value => value.CreatorAccountId == accountId)
                .OrderByDescending(value => value.StartsAt)
                .ToList();

        public static List<PlayerEvent> GetByClub(long clubId) =>
            Events.Find(value => value.ClubId == clubId)
                .OrderByDescending(value => value.StartsAt)
                .ToList();

        public static bool Update(PlayerEvent evt)
        {
            evt.UpdatedAt = DateTime.UtcNow;
            return Events.Update(evt);
        }

        public static bool Delete(long eventId)
        {
            lock (Sync)
            {
                Responses.DeleteMany(value => value.EventId == eventId);
                return Events.Delete(eventId);
            }
        }

        public static PlayerEventResponse SetResponse(long eventId, long accountId, string responseType)
        {
            var response = new PlayerEventResponse
            {
                Id = ResponseId(eventId, accountId),
                EventId = eventId,
                AccountId = accountId,
                ResponseType = responseType,
                CreatedAt = DateTime.UtcNow
            };
            Responses.Upsert(response);
            return response;
        }

        public static bool DeleteResponse(long eventId, long accountId) =>
            Responses.Delete(ResponseId(eventId, accountId));

        public static List<PlayerEventResponse> GetResponses(long eventId) =>
            Responses.Find(value => value.EventId == eventId).ToList();

        public static PlayerEventResponse? GetResponse(long eventId, long accountId) =>
            Responses.FindById(ResponseId(eventId, accountId));
    }
}
