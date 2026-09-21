using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace Mocha2023.Classes.DBs.Postgres
{
    // Marks a property that must round-trip through the client's JSON
    // contract but never be written to the JSONB storage column - the
    // Postgres equivalent of LiteDB's [BsonIgnore] (which only ever
    // applied to storage, never to the client-facing System.Text.Json
    // contract those same models also carry).
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class PostgresIgnoreAttribute : Attribute
    {
    }

    // Drop-in replacement for the subset of LiteDB's ILiteCollection<T>
    // surface actually used across the codebase, backed by a real Postgres
    // table shaped (<id column>, <optional extra indexed columns>, data
    // JSONB NOT NULL). The full record always round-trips through the JSONB
    // column; extra columns exist only so real indexes can back hot lookups -
    // they are kept in sync from the same object on every write.
    //
    // Find()/DeleteMany() filter in-memory with the exact same C# predicate
    // the caller already had - at this app's data scale (tens of thousands
    // of rows per table at most) that is simpler and just as correct as a
    // general expression-to-SQL translator, without the risk of a
    // translator silently mishandling an edge case LiteDB handled
    // differently. See the cache below for how "in-memory" is kept cheap.
    public sealed class PostgresCollection<T>
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = null,
            MaxDepth = 64,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { IgnorePostgresIgnoreProperties, RestoreJsonIgnoredProperties }
            }
        };

        private static void IgnorePostgresIgnoreProperties(JsonTypeInfo typeInfo)
        {
            // Removed outright, not just ShouldSerialize = false - a
            // property left in the list still claims its JSON name, which
            // collides with the real field RestoreJsonIgnoredProperties
            // below re-adds under that same name (e.g. an alias explicitly
            // named after the field it mirrors).
            for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
            {
                if (typeInfo.Properties[i].AttributeProvider?.IsDefined(typeof(PostgresIgnoreAttribute), true) == true)
                    typeInfo.Properties.RemoveAt(i);
            }
        }

        // These models double as the client's System.Text.Json contract, so
        // [JsonIgnore] on them only ever meant "don't send this to the
        // client" - LiteDB's BSON mapper never looked at that attribute, so
        // every [JsonIgnore] field (passwords, tokens, IPs, internal ids...)
        // was still persisted. Restore that same field into the storage
        // round-trip here; [PostgresIgnore] is the only attribute that
        // actually means "don't persist this."
        private static void RestoreJsonIgnoredProperties(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
                return;

            foreach (PropertyInfo clrProperty in typeInfo.Type.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                JsonIgnoreAttribute? jsonIgnore = clrProperty.GetCustomAttribute<JsonIgnoreAttribute>();
                if (jsonIgnore == null || jsonIgnore.Condition != JsonIgnoreCondition.Always)
                    continue;
                if (clrProperty.GetCustomAttribute<PostgresIgnoreAttribute>() != null)
                    continue;

                // The default resolver may already have added this property
                // with serialization suppressed (because of [JsonIgnore]) -
                // remove that entry rather than trust an equality check
                // against it, then always add a fresh, unsuppressed one.
                for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
                {
                    if (Equals(typeInfo.Properties[i].AttributeProvider, clrProperty))
                        typeInfo.Properties.RemoveAt(i);
                }

                JsonPropertyInfo propertyInfo = typeInfo.CreateJsonPropertyInfo(
                    clrProperty.PropertyType,
                    clrProperty.Name);
                propertyInfo.AttributeProvider = clrProperty;
                if (clrProperty.CanRead)
                    propertyInfo.Get = obj => clrProperty.GetValue(obj);
                if (clrProperty.CanWrite)
                    propertyInfo.Set = (obj, value) => clrProperty.SetValue(obj, value);
                typeInfo.Properties.Add(propertyInfo);
            }
        }

        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly string _idColumn;
        private readonly Func<T, object> _idSelector;
        private readonly (string Column, Func<T, object?> Extract)[] _extraColumns;

        // Find()/FindAll()/FindById() used to hit Postgres on every single
        // call - a full "SELECT data::text FROM table" + JSON-deserialize
        // every row, even for a single-record lookup, since the in-memory
        // predicate filtering that replaced LiteDB's queries only runs
        // after the whole table is already in hand. That was fine under
        // LiteDB (a memory-mapped local file, no network round trip, no
        // JSON parsing cost) but is a full table scan over the network on
        // every read under Postgres, and got slower as tables grew - this
        // is what actually made things slow after the migration, not
        // Postgres itself. This cache restores LiteDB's real behavior (the
        // whole table resident in memory) while keeping Postgres as the
        // durable store: reads never touch the database, writes update
        // both. Safe because this app is a single process - nothing else
        // writes to these tables outside this class.
        private readonly object _cacheLock = new();
        private Dictionary<object, T>? _cache;

        public PostgresCollection(
            string connectionString,
            string tableName,
            string idColumn,
            Func<T, object> idSelector,
            (string Column, Func<T, object?> Extract)[]? extraColumns = null)
        {
            _connectionString = connectionString;
            _tableName = ValidateIdentifier(tableName);
            _idColumn = ValidateIdentifier(idColumn);
            _idSelector = idSelector;
            _extraColumns = extraColumns ?? Array.Empty<(string, Func<T, object?>)>();
            foreach ((string column, _) in _extraColumns)
                ValidateIdentifier(column);
        }

        // Boxed ints and boxed longs never compare equal as dictionary keys
        // even when the value matches, and call sites pass whichever
        // integral type they happen to have on hand for an id. Normalize so
        // a lookup always lands on the same key the row was cached under.
        private static object CanonicalKey(object id) => id switch
        {
            int i => (long)i,
            short s => (long)s,
            byte b => (long)b,
            uint ui => (long)ui,
            _ => id,
        };

        private Dictionary<object, T> Cache
        {
            get
            {
                lock (_cacheLock)
                {
                    if (_cache != null)
                        return _cache;

                    using NpgsqlConnection connection = Open();
                    var built = new Dictionary<object, T>();
                    foreach (string json in connection.Query<string>($"SELECT data::text FROM {_tableName}"))
                    {
                        T row = Deserialize(json);
                        built[CanonicalKey(_idSelector(row))] = row;
                    }
                    _cache = built;
                    return _cache;
                }
            }
        }

        // Table/column names can never be parameterized (Postgres has no
        // bind-parameter syntax for identifiers), so every one that reaches
        // an interpolated SQL string goes through this whitelist first.
        // Every call site today passes a hardcoded literal, but this makes
        // that a hard guarantee instead of just current practice.
        private static string ValidateIdentifier(string identifier)
        {
            if (!Regex.IsMatch(identifier, "^[a-z_][a-z0-9_]*$"))
                throw new ArgumentException($"'{identifier}' is not a safe SQL identifier.", nameof(identifier));
            return identifier;
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
            lock (_cacheLock)
                return Cache.TryGetValue(CanonicalKey(id), out T? value) ? value : default;
        }

        public List<T> FindAll()
        {
            lock (_cacheLock)
                return Cache.Values.ToList();
        }

        public List<T> Find(Func<T, bool> predicate) =>
            FindAll().Where(predicate).ToList();

        public T? FindOne(Func<T, bool> predicate) =>
            Find(predicate).FirstOrDefault();

        public bool Exists(Func<T, bool> predicate) =>
            Find(predicate).Count > 0;

        public long LongCount()
        {
            lock (_cacheLock)
                return Cache.Count;
        }

        public int Count() => (int)LongCount();
        public int Count(Func<T, bool> predicate) => Find(predicate).Count;
        public long LongCount(Func<T, bool> predicate) => Find(predicate).Count;

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
            if (rows > 0)
                lock (_cacheLock)
                    Cache[CanonicalKey(_idSelector(entity))] = entity;
            return rows > 0;
        }

        public bool Update(T entity)
        {
            using NpgsqlConnection connection = Open();
            var setColumns = _extraColumns.Select(c => $"{c.Column} = @{c.Column}")
                .Append("data = @data::jsonb");
            string sql = $"UPDATE {_tableName} SET {string.Join(", ", setColumns)} WHERE {_idColumn} = @id";
            int rows = connection.Execute(sql, BuildParameters(entity));
            if (rows > 0)
                lock (_cacheLock)
                    Cache[CanonicalKey(_idSelector(entity))] = entity;
            return rows > 0;
        }

        // Mirrors LiteDB's bulk Update(IEnumerable<T>) overload, which
        // returns the count of rows actually updated - not each row's
        // individual success, so a partial failure isn't silently masked.
        public int Update(IEnumerable<T> entities) =>
            entities.Count(Update);

        public bool Upsert(T entity)
        {
            using NpgsqlConnection connection = Open();
            int rows = connection.Execute(BuildInsertOrUpsertSql(upsert: true), BuildParameters(entity));
            if (rows > 0)
                lock (_cacheLock)
                    Cache[CanonicalKey(_idSelector(entity))] = entity;
            return rows > 0;
        }

        public bool Delete(object id)
        {
            using NpgsqlConnection connection = Open();
            bool deleted = connection.Execute(
                $"DELETE FROM {_tableName} WHERE {_idColumn} = @id", new { id }) > 0;
            if (deleted)
                lock (_cacheLock)
                    Cache.Remove(CanonicalKey(id));
            return deleted;
        }

        public int DeleteMany(Func<T, bool> predicate)
        {
            // A boxed List<object> can't be bound as a typed Postgres array
            // parameter (Npgsql needs a concrete element type), so this
            // deletes one row at a time through the already-reliable
            // Delete(object) path instead of a single ANY(@ids) statement.
            int deleted = 0;
            foreach (object id in Find(predicate).Select(_idSelector))
            {
                if (Delete(id))
                    deleted++;
            }
            return deleted;
        }

        // Replaces the race-prone "Max(id)+1 under a lock" pattern several
        // domains use today - a real Postgres sequence, called explicitly so
        // Insert() never has to guess whether an id was already assigned
        // (natural keys, GUIDs) or needs generating (auto-increment domains
        // like this one).
        public long NextId(string sequenceName)
        {
            ValidateIdentifier(sequenceName);
            using NpgsqlConnection connection = Open();
            return connection.ExecuteScalar<long>($"SELECT nextval('{sequenceName}')");
        }

        // Real, idempotent index creation - called once from each domain's
        // static constructor, same call sites LiteDB's EnsureIndex used.
        public void EnsureIndex(string columnName, bool unique = false)
        {
            ValidateIdentifier(columnName);
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
