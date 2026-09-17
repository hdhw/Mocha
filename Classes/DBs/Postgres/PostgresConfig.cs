namespace Mocha2023.Classes.DBs.Postgres
{
    // Connection settings for the dedicated Postgres instance migrated
    // domains use - a separate cluster/database from any other app on the
    // box, configured once via .env (same KEY=value convention as every
    // other secret in Program.LoadLocalSetting).
    public static class PostgresConfig
    {
        private static readonly string Host = Program.LoadLocalSetting("MOCHA_PG_HOST");
        private static readonly string Port = Program.LoadLocalSetting("MOCHA_PG_PORT");
        private static readonly string Database = Program.LoadLocalSetting("MOCHA_PG_DATABASE");
        private static readonly string User = Program.LoadLocalSetting("MOCHA_PG_USER");
        private static readonly string Password = Program.LoadLocalSetting("MOCHA_PG_PASSWORD");

        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Host) &&
            !string.IsNullOrWhiteSpace(Port) &&
            !string.IsNullOrWhiteSpace(Database) &&
            !string.IsNullOrWhiteSpace(User) &&
            !string.IsNullOrWhiteSpace(Password);

        public static string ConnectionString =>
            $"Host={Host};Port={Port};Database={Database};Username={User};Password={Password};SSL Mode=Disable";
    }
}
