using System.Security.Cryptography;
using Mocha2023.Classes.DBs.Postgres;

namespace Mocha2023.Classes.DBs
{
    public static class AgeVerificationDB
    {
        private static readonly PostgresCollection<AgeVerificationRequest> Requests = new(
            PostgresConfig.ConnectionString,
            tableName: "age_verification_requests",
            idColumn: "code",
            idSelector: r => r.Code,
            extraColumns: new (string, Func<AgeVerificationRequest, object?>)[]
            {
                ("account_id", r => r.AccountId),
                ("status", r => r.Status)
            });

        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private static readonly object Sync = new();

        static AgeVerificationDB()
        {
            Requests.EnsureIndex("account_id");
            Requests.EnsureIndex("status");
        }

        public static int MigrateFromLiteDb()
        {
            string path = Path.Combine(Program.dataDir, "DBs", "AgeVerification.db");
            if (!File.Exists(path))
                return 0;

            using var db = new LiteDB.LiteDatabase($"Filename={path};Connection=shared;ReadOnly=true");
            int count = 0;
            foreach (LiteDB.BsonDocument doc in db.GetCollection("Requests").FindAll())
            {
                Requests.Upsert(LiteDbMigrationTool.ReadWithRecoveredId<AgeVerificationRequest>(doc, "Code"));
                count++;
            }
            return count;
        }

        public sealed class AgeVerificationRequest
        {
            public string Code { get; set; } = string.Empty;
            public long AccountId { get; set; }

            public string Method { get; set; } = string.Empty;

            public string Status { get; set; } = "Pending";

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? SubmittedAt { get; set; }
            public DateTime? ReviewedAt { get; set; }
            public long? ReviewedByAccountId { get; set; }
            public string? RejectionReason { get; set; }
        }

        public static string GenerateCode(long accountId)
        {
            lock (Sync)
            {
                foreach (AgeVerificationRequest stale in Requests
                             .Find(value => value.AccountId == accountId &&
                                 (value.Status == "Pending" || value.Status == "UnderReview"))
                             .ToList())
                {
                    Requests.Delete(stale.Code);
                }

                string code;
                do
                {
                    code = NewCode();
                } while (Requests.Exists(value => value.Code == code));

                Requests.Insert(new AgeVerificationRequest
                {
                    Code = code,
                    AccountId = accountId,
                    Status = "Pending"
                });

                return code;
            }
        }

        private static string NewCode()
        {
            // CSPRNG + wider code space (this code is the sole lookup key for an
            // age-verification request - a 6-char/32-symbol code is brute-forceable).
            byte[] bytes = RandomNumberGenerator.GetBytes(8);
            char[] buffer = new char[8];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
            return new string(buffer);
        }

        public static AgeVerificationRequest? GetByCode(string? code)
        {
            string normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
            return normalized.Length == 0 ? null : Requests.FindById(normalized);
        }

        public static AgeVerificationRequest? GetActiveForAccount(long accountId)
        {
            return Requests.FindAll()
                .Where(value => value.AccountId == accountId &&
                    (value.Status == "Pending" || value.Status == "UnderReview"))
                .OrderByDescending(value => value.CreatedAt)
                .FirstOrDefault();
        }

        public static bool MarkUnderReview(string code, long accountId, string method)
        {
            lock (Sync)
            {
                AgeVerificationRequest? request = GetByCode(code);
                if (request == null ||
                    request.AccountId != accountId ||
                    request.Status != "Pending")
                {
                    return false;
                }

                request.Method = method;
                request.Status = "UnderReview";
                request.SubmittedAt = DateTime.UtcNow;
                return Requests.Update(request);
            }
        }

        public static List<AgeVerificationRequest> GetForReview()
        {
            return Requests.Find(value => value.Status == "UnderReview")
                .OrderBy(value => value.SubmittedAt)
                .ToList();
        }

        public static bool Review(string code, bool approve, long reviewerId, string? reason)
        {
            lock (Sync)
            {
                AgeVerificationRequest? request = GetByCode(code);
                if (request == null || request.Status != "UnderReview")
                    return false;

                request.Status = approve ? "Approved" : "Rejected";
                request.ReviewedAt = DateTime.UtcNow;
                request.ReviewedByAccountId = reviewerId;
                request.RejectionReason = reason;
                return Requests.Update(request);
            }
        }
    }
}
