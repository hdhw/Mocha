using Mocha2023.Classes.DBs.Postgres;

namespace Mocha2023.Classes.DBs;

public static class ScannerLogDB
{
    public class ScannerAttempt
    {
        public long Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string IpAddress { get; set; } = "unknown";
        public string Method { get; set; } = "";
        public string Path { get; set; } = "";
        public string? QueryString { get; set; }
        public string? UserAgent { get; set; }

        public string? MatchedPattern { get; set; }
    }

    public static readonly PostgresCollection<ScannerAttempt> Attempts = new(
        PostgresConfig.ConnectionString,
        tableName: "scanner_attempts",
        idColumn: "id",
        idSelector: a => a.Id,
        extraColumns: new (string, Func<ScannerAttempt, object?>)[]
        {
            ("ip_address", a => a.IpAddress)
        });

    private const int MaxStored = 20_000;
    private const int TrimBatchSize = 1_000;

    static ScannerLogDB()
    {
        Attempts.EnsureIndex("ip_address");
    }

    public static void Log(
        string ipAddress,
        string method,
        string path,
        string? queryString,
        string? userAgent,
        string? matchedPattern)
    {
        var attempt = new ScannerAttempt
        {
            Id = Attempts.NextId("scanner_attempts_id_seq"),
            IpAddress = ipAddress,
            Method = method,
            Path = path,
            QueryString = queryString,
            UserAgent = userAgent,
            MatchedPattern = matchedPattern
        };
        Attempts.Insert(attempt);

        long count = Attempts.LongCount();
        if (count > MaxStored)
        {
            List<long> oldestIds = Attempts.Query()
                .OrderBy(a => a.Id)
                .Limit(TrimBatchSize)
                .ToList()
                .Select(a => a.Id)
                .ToList();

            foreach (long id in oldestIds)
                Attempts.Delete(id);
        }
    }
}
