using Mocha2023;

namespace Mocha2023.Classes.DBs
{
    public static class LiteDbMigrationTool
    {
        public static void RunAll()
        {
            Run("ReportsDB", ReportsDB.MigrateFromLiteDb);
            Run("AnticheatLogDB", AnticheatLogDB.MigrateFromLiteDb);
            Run("AgeVerificationDB", AgeVerificationDB.MigrateFromLiteDb);
            Run("PlayerEventDB", PlayerEventDB.MigrateFromLiteDb);
            Run("NotificationDB", NotificationDB.MigrateFromLiteDb);
            Run("RelationshipDB", RelationshipDB.MigrateFromLiteDb);
            Run("ChatDB", ChatDB.MigrateFromLiteDb);
            Run("ClubDB", ClubDB.MigrateFromLiteDb);
            Run("CreatorFeatureDB", CreatorFeatureDB.MigrateFromLiteDb);
            Run("RecNetDB", RecNetDB.MigrateFromLiteDb);
            Run("LegacyLeaderboardController", Mocha2023.Controllers.LegacyLeaderboardController.MigrateFromLiteDb);
            Run("RoomDB", RoomDB.MigrateFromLiteDb);
            Run("PlayerDB", PlayerDB.MigrateFromLiteDb);

            ServerLog.Success("[MIGRATION] All LiteDB collections migrated to Postgres.");
        }

        private static void Run(string name, Func<int> migrate)
        {
            try
            {
                int count = migrate();
                ServerLog.Success($"[MIGRATION] {name}: {count} row(s) imported.");
            }
            catch (Exception exception)
            {
                ServerLog.Error($"[MIGRATION] {name} failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        // LiteDB auto-id fields (BsonAutoId): the id is only ever written
        // into the document's special _id slot at insert time - the
        // in-memory object's property gets mutated to match afterward, but
        // the already-serialized document body never does, so it can still
        // read back as 0/default. Reading through the typed collection API
        // trusts that body value, so it silently comes back wrong for any
        // collection where the id was never explicitly assigned by app code
        // before insert. Recovering _id into the body before mapping fixes
        // that, and is a harmless no-op for collections that already had it
        // right (composite string keys, or ids the app assigned itself).
        public static T ReadWithRecoveredId<T>(LiteDB.BsonDocument doc, string idPropertyName)
        {
            doc[idPropertyName] = doc["_id"];
            return LiteDB.BsonMapper.Global.ToObject<T>(doc);
        }
    }
}
