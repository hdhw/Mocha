using System.Linq.Expressions;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace Mocha2023.Classes.DBs.Postgres
{
    // Drop-in replacement for the subset of LiteDB's ILiteCollection<T>
    // surface actually used across the codebase, backed by a real Postgres
    // table shaped (<id column>, <optional extra indexed columns>, data
    // JSONB NOT NULL). The full record always round-trips through the JSONB
    // column; extra columns exist only so real indexes can back hot lookups -
    // they are kept in sync from the same object on every write.
    //
    // Find()/DeleteMany() fetch the full table and filter in-memory with the
    // exact same C# predicate the caller already had - at this app's data
    // scale (tens of thousands of rows per table at most) that is simpler
    // and just as correct as a general expression-to-SQL translator, without
    // the risk of a translator silently mishandling an edge case LiteDB
    // handled differently.
    public sealed class PostgresCollection<T>
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = null,
            MaxDepth = 64
        };

        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly string _idColumn;
        private readonly Func<T, object> _idSelector;
        private readonly (string Column, Func<T, object?> Extract)[] _extraColumns;

        public PostgresCollection(
            string connectionString,
            string tableName,
            string idColumn,
            Func<T, object> idSelector,
            (string Column, Func<T, object?> Extract)[]? extraColumns = null)
        {
            _connectionString = connectionString;
            _tableName = tableName;
            _idColumn = idColumn;
            _idSelector = idSelector;
            _extraColumns = extraColumns ?? Array.Empty<(string, Func<T, object?>)>();
        }

        private NpgsqlConnection Open()
        {
            var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static T Deserialize(string json) =>
            JsonSerializer.Deserialize<T>(json, JsonOptions)!;

        public T? FindById(object id)
        {
            using NpgsqlConnection connection = Open();
            string? json = connection.QuerySingleOrDefault<string?>(
                $"SELECT data::text FROM {_tableName} WHERE {_idColumn} = @id",
                new { id });
            return json == null ? default : Deserialize(json);
        }

        public List<T> FindAll()
        {
            using NpgsqlConnection connection = Open();
            return connection.Query<string>($"SELECT data::text FROM {_tableName}")
                .Select(Deserialize)
                .ToList();
        }

        public List<T> Find(Func<T, bool> predicate) =>
            FindAll().Where(predicate).ToList();

        public List<T> Find(Expression<Func<T, bool>> predicate) =>
            Find(predicate.Compile());

        public bool Exists(Expression<Func<T, bool>> predicate) =>
            Find(predicate).Count > 0;

        public long LongCount()
        {
            using NpgsqlConnection connection = Open();
            return connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM {_tableName}");
        }

        public int Count() => (int)LongCount();
        public int Count(Expression<Func<T, bool>> predicate) => Find(predicate).Count;
        public long LongCount(Expression<Func<T, bool>> predicate) => Find(predicate).Count;

        public TResult Max<TResult>(Func<T, TResult> selector) =>
            FindAll().Select(selector).Max()!;

        private string BuildInsertOrUpsertSql(bool upsert)
        {
            var columns = new List<string> { _idColumn };
            columns.AddRange(_extraColumns.Select(c => c.Column));
            columns.Add("data");

            var paramNames = new List<string> { "@id" };
            paramNames.AddRange(_extraColumns.Select(c => $"@{c.Column}"));
            paramNames.Add("@data::jsonb");

            string columnList = string.Join(", ", columns);
            string valueList = string.Join(", ", paramNames);

            if (!upsert)
                return $"INSERT INTO {_tableName} ({columnList}) VALUES ({valueList})";

            string updateAssignments = string.Join(", ",
                _extraColumns.Select(c => $"{c.Column} = EXCLUDED.{c.Column}")
                    .Append("data = EXCLUDED.data"));

            return $"INSERT INTO {_tableName} ({columnList}) VALUES ({valueList}) " +
                   $"ON CONFLICT ({_idColumn}) DO UPDATE SET {updateAssignments}";
        }

        private DynamicParameters BuildParameters(T entity)
        {
            var parameters = new DynamicParameters();
            parameters.Add("id", _idSelector(entity));
            foreach ((string column, Func<T, object?> extract) in _extraColumns)
                parameters.Add(column, extract(entity));
            parameters.Add("data", JsonSerializer.Serialize(entity, JsonOptions));
            return parameters;
        }

        public bool Insert(T entity)
        {
            using NpgsqlConnection connection = Open();
            int rows = connection.Execute(BuildInsertOrUpsertSql(upsert: false), BuildParameters(entity));
            return rows > 0;
        }

        public bool Update(T entity)
        {
            using NpgsqlConnection connection = Open();
            var setColumns = _extraColumns.Select(c => $"{c.Column} = @{c.Column}")
                .Append("data = @data::jsonb");
            string sql = $"UPDATE {_tableName} SET {string.Join(", ", setColumns)} WHERE {_idColumn} = @id";
            int rows = connection.Execute(sql, BuildParameters(entity));
            return rows > 0;
        }

        public bool Upsert(T entity)
        {
            using NpgsqlConnection connection = Open();
            int rows = connection.Execute(BuildInsertOrUpsertSql(upsert: true), BuildParameters(entity));
            return rows > 0;
        }

        public bool Delete(object id)
        {
            using NpgsqlConnection connection = Open();
            return connection.Execute(
                $"DELETE FROM {_tableName} WHERE {_idColumn} = @id", new { id }) > 0;
        }

        public int DeleteMany(Expression<Func<T, bool>> predicate)
        {
            List<object> ids = Find(predicate).Select(_idSelector).ToList();
            if (ids.Count == 0)
                return 0;

            using NpgsqlConnection connection = Open();
            return connection.Execute(
                $"DELETE FROM {_tableName} WHERE {_idColumn} = ANY(@ids)", new { ids });
        }

        // Replaces the race-prone "Max(id)+1 under a lock" pattern several
        // domains use today - a real Postgres sequence, called explicitly so
        // Insert() never has to guess whether an id was already assigned
        // (natural keys, GUIDs) or needs generating (auto-increment domains
        // like this one).
        public long NextId(string sequenceName)
        {
            using NpgsqlConnection connection = Open();
            return connection.ExecuteScalar<long>($"SELECT nextval('{sequenceName}')");
        }

        // Real, idempotent index creation - called once from each domain's
        // static constructor, same call sites LiteDB's EnsureIndex used.
        public void EnsureIndex(string columnName, bool unique = false)
        {
            using NpgsqlConnection connection = Open();
            string indexName = $"ix_{_tableName}_{columnName}";
            string uniqueSql = unique ? "UNIQUE" : "";
            connection.Execute(
                $"CREATE {uniqueSql} INDEX IF NOT EXISTS {indexName} ON {_tableName} ({columnName})");
        }

        // Fluent chain covering ScannerLogDB/AnticheatLogDB's Query() usage -
        // in-memory after a full fetch, for the same reason Find() is.
        public PostgresQueryable<T> Query() => new(FindAll());
    }

    public sealed class PostgresQueryable<T>
    {
        private IEnumerable<T> _source;

        internal PostgresQueryable(IEnumerable<T> source) => _source = source;

        public PostgresQueryable<T> Where(Func<T, bool> predicate)
        {
            _source = _source.Where(predicate);
            return this;
        }

        public PostgresQueryable<T> OrderBy<TKey>(Func<T, TKey> keySelector)
        {
            _source = _source.OrderBy(keySelector);
            return this;
        }

        public PostgresQueryable<T> OrderByDescending<TKey>(Func<T, TKey> keySelector)
        {
            _source = _source.OrderByDescending(keySelector);
            return this;
        }

        public PostgresQueryable<T> Skip(int count)
        {
            _source = _source.Skip(count);
            return this;
        }

        public PostgresQueryable<T> Limit(int count)
        {
            _source = _source.Take(count);
            return this;
        }

        public List<T> ToList() => _source.ToList();
    }
}
