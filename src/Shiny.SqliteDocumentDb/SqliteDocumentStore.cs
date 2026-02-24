using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Data.Sqlite;
using Shiny.SqliteDocumentDb.Internal;

namespace Shiny.SqliteDocumentDb;

public class SqliteDocumentStore : IDocumentStore, IQueryExecutor, IDisposable
{
    readonly SemaphoreSlim semaphore = new(1, 1);
    readonly SqliteConnection connection;
    readonly DocumentStoreOptions options;
    readonly JsonSerializerOptions jsonOptions;
    readonly Action<string>? logging;
    bool initialized;

    public SqliteDocumentStore(DocumentStoreOptions options)
    {
        this.options = options;
        this.jsonOptions = options.JsonSerializerOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        this.logging = options.Logging;
        this.connection = new SqliteConnection(options.ConnectionString);
    }

    void Log(string sql) => this.logging?.Invoke(sql);

    string ResolveTypeName<T>() => TypeNameResolver.Resolve(typeof(T), this.options.TypeNameResolution);

    JsonTypeInfo<T>? FindTypeInfo<T>(JsonTypeInfo<T>? provided)
        => FindTypeInfo(provided, this.jsonOptions, this.options.UseReflectionFallback);

    async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (this.initialized)
            return;

        await this.connection.OpenAsync(ct).ConfigureAwait(false);

        await using var walCmd = this.connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        this.Log(walCmd.CommandText);
        await walCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await using var createCmd = this.connection.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS documents (
                Id TEXT NOT NULL,
                TypeName TEXT NOT NULL,
                Data TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                PRIMARY KEY (Id, TypeName)
            );
            CREATE INDEX IF NOT EXISTS idx_documents_typename ON documents (TypeName);
            """;
        this.Log(createCmd.CommandText);
        await createCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        this.initialized = true;
    }

    async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation, CancellationToken ct)
    {
        await this.semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await this.EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            this.semaphore.Release();
        }
    }

    async Task ExecuteAsync(Func<Task> operation, CancellationToken ct)
    {
        await this.semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await this.EnsureInitializedAsync(ct).ConfigureAwait(false);
            await operation().ConfigureAwait(false);
        }
        finally
        {
            this.semaphore.Release();
        }
    }

    async Task UpsertCoreAsync(string id, string typeName, string json, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var cmd = this.connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents (Id, TypeName, Data, CreatedAt, UpdatedAt)
            VALUES (@id, @typeName, @data, @now, @now)
            ON CONFLICT(Id, TypeName) DO UPDATE SET
                Data = @data,
                UpdatedAt = @now;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@typeName", typeName);
        cmd.Parameters.AddWithValue("@data", json);
        cmd.Parameters.AddWithValue("@now", now);

        this.Log(cmd.CommandText);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    async Task UpsertMergeCoreAsync(string id, string typeName, string json, CancellationToken ct)
    {
        json = StripNullProperties(json);
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var cmd = this.connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents (Id, TypeName, Data, CreatedAt, UpdatedAt)
            VALUES (@id, @typeName, @data, @now, @now)
            ON CONFLICT(Id, TypeName) DO UPDATE SET
                Data = json_patch(documents.Data, @data),
                UpdatedAt = @now;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@typeName", typeName);
        cmd.Parameters.AddWithValue("@data", json);
        cmd.Parameters.AddWithValue("@now", now);

        this.Log(cmd.CommandText);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    async Task<bool> SetPropertyCoreAsync(string id, string typeName, string jsonPath, object? value, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var cmd = this.connection.CreateCommand();
        cmd.CommandText = """
            UPDATE documents
            SET Data = json_set(Data, @path, json(@value)), UpdatedAt = @now
            WHERE Id = @id AND TypeName = @typeName;
            """;
        cmd.Parameters.AddWithValue("@path", "$." + jsonPath);
        cmd.Parameters.AddWithValue("@value", ToJsonLiteral(value));
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@typeName", typeName);

        this.Log(cmd.CommandText);
        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return rows > 0;
    }

    async Task<bool> RemovePropertyCoreAsync(string id, string typeName, string jsonPath, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var cmd = this.connection.CreateCommand();
        cmd.CommandText = """
            UPDATE documents
            SET Data = json_remove(Data, @path), UpdatedAt = @now
            WHERE Id = @id AND TypeName = @typeName;
            """;
        cmd.Parameters.AddWithValue("@path", "$." + jsonPath);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@typeName", typeName);

        this.Log(cmd.CommandText);
        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return rows > 0;
    }

    // ── IQueryExecutor explicit implementation ──────────────────────────

    Task<TResult> IQueryExecutor.ExecuteAsync<TResult>(Func<Task<TResult>> operation, CancellationToken ct)
        => this.ExecuteAsync(operation, ct);

    IAsyncEnumerable<T> IQueryExecutor.ReadStreamAsync<T>(Action<SqliteCommand> configure, Func<string, T> deserialize, CancellationToken ct)
        => this.ReadStreamAsync(configure, deserialize, ct);

    SqliteCommand IQueryExecutor.CreateCommand()
        => this.connection.CreateCommand();

    string IQueryExecutor.ResolveTypeName<T>()
        => this.ResolveTypeName<T>();

    JsonSerializerOptions IQueryExecutor.JsonOptions
        => this.jsonOptions;

    Action<string>? IQueryExecutor.Logging
        => this.logging;

    // ── Query<T>() entry point ──────────────────────────────────────────

    public IDocumentQuery<T> Query<T>(JsonTypeInfo<T>? jsonTypeInfo = null) where T : class
    {
        return new DocumentQuery<T>(this, FindTypeInfo(jsonTypeInfo));
    }

    // ── CRUD ────────────────────────────────────────────────────────────

    public Task<string> Set<T>(T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
    {
        var typeInfo = FindTypeInfo(jsonTypeInfo);
        return this.ExecuteAsync(async () =>
        {
            var id = Guid.NewGuid().ToString("N");
            var json = SerializeDocument(document, typeInfo, this.jsonOptions);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
            return id;
        }, cancellationToken);
    }

    public Task Set<T>(string id, T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
    {
        var typeInfo = FindTypeInfo(jsonTypeInfo);
        return this.ExecuteAsync(async () =>
        {
            var json = SerializeDocument(document, typeInfo, this.jsonOptions);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task Upsert<T>(string id, T patch, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
    {
        var typeInfo = FindTypeInfo(jsonTypeInfo);
        return this.ExecuteAsync(async () =>
        {
            var json = SerializeDocument(patch, typeInfo, this.jsonOptions);
            await this.UpsertMergeCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<bool> SetProperty<T>(string id, Expression<Func<T, object>> property, object? value, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
    {
        var jsonPath = ResolvePropertyPath(property, this.jsonOptions, FindTypeInfo(jsonTypeInfo));
        return this.ExecuteAsync(
            () => this.SetPropertyCoreAsync(id, this.ResolveTypeName<T>(), jsonPath, value, cancellationToken),
            cancellationToken);
    }

    public Task<bool> RemoveProperty<T>(string id, Expression<Func<T, object>> property, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
    {
        var jsonPath = ResolvePropertyPath(property, this.jsonOptions, FindTypeInfo(jsonTypeInfo));
        return this.ExecuteAsync(
            () => this.RemovePropertyCoreAsync(id, this.ResolveTypeName<T>(), jsonPath, cancellationToken),
            cancellationToken);
    }

    public Task<T?> Get<T>(string id, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
    {
        var typeInfo = FindTypeInfo(jsonTypeInfo);
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = "SELECT Data FROM documents WHERE Id = @id AND TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            this.Log(cmd.CommandText);
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is string json
                ? DeserializeDocument(json, typeInfo, this.jsonOptions)
                : null;
        }, cancellationToken);
    }

    // ── String-based query ──────────────────────────────────────────────

    public Task<IReadOnlyList<T>> Query<T>(string whereClause, JsonTypeInfo<T>? jsonTypeInfo = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class
    {
        var typeInfo = FindTypeInfo(jsonTypeInfo);
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = $"SELECT Data FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindParameters(cmd, parameters);

            this.Log(cmd.CommandText);
            return await ReadListAsync<T>(cmd, json => DeserializeDocument(json, typeInfo, this.jsonOptions)!, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    // ── String-based streaming ──────────────────────────────────────────

    async IAsyncEnumerable<T> ReadStreamAsync<T>(
        Action<SqliteCommand> configureCommand,
        Func<string, T> deserialize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await this.semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await this.EnsureInitializedAsync(ct).ConfigureAwait(false);

            await using var cmd = this.connection.CreateCommand();
            configureCommand(cmd);

            this.Log(cmd.CommandText);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var json = reader.GetString(0);
                yield return deserialize(json);
            }
        }
        finally
        {
            this.semaphore.Release();
        }
    }

    public IAsyncEnumerable<T> QueryStream<T>(string whereClause, JsonTypeInfo<T>? jsonTypeInfo = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class
    {
        var typeInfo = FindTypeInfo(jsonTypeInfo);
        var typeName = this.ResolveTypeName<T>();
        return this.ReadStreamAsync<T>(
            cmd =>
            {
                cmd.CommandText = $"SELECT Data FROM documents WHERE TypeName = @typeName AND ({whereClause});";
                cmd.Parameters.AddWithValue("@typeName", typeName);
                BindParameters(cmd, parameters);
            },
            json => DeserializeDocument(json, typeInfo, this.jsonOptions)!,
            cancellationToken);
    }

    // ── Count / Remove / Clear ──────────────────────────────────────────

    public Task<int> Count<T>(string? whereClause = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            var sql = "SELECT COUNT(*) FROM documents WHERE TypeName = @typeName";
            if (!string.IsNullOrWhiteSpace(whereClause))
                sql += $" AND ({whereClause})";
            cmd.CommandText = sql + ";";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindParameters(cmd, parameters);

            this.Log(cmd.CommandText);
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result);
        }, cancellationToken);
    }

    public Task<bool> Remove<T>(string id, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = "DELETE FROM documents WHERE Id = @id AND TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            this.Log(cmd.CommandText);
            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rows > 0;
        }, cancellationToken);
    }

    public Task<int> Clear<T>(CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = "DELETE FROM documents WHERE TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            this.Log(cmd.CommandText);
            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    // ── Transaction ─────────────────────────────────────────────────────

    public Task RunInTransaction(Func<IDocumentStore, Task> operation, CancellationToken cancellationToken = default)
    {
        return this.ExecuteAsync(async () =>
        {
            await using var transaction = await this.connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var txStore = new TransactionalDocumentStore(this.connection, transaction, this.options, this.jsonOptions, this.logging);
                await operation(txStore).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }, cancellationToken);
    }

    // ── Index management ────────────────────────────────────────────────

    public Task CreateIndexAsync<T>(Expression<Func<T, object>> expression, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
    {
        var jsonPath = IndexExpressionHelper.ResolveJsonPath(expression, this.jsonOptions, jsonTypeInfo);
        var typeName = this.ResolveTypeName<T>();
        var indexName = IndexExpressionHelper.BuildIndexName(typeName, jsonPath);

        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = $"CREATE INDEX IF NOT EXISTS {indexName} ON documents (json_extract(Data, '$.{jsonPath}')) WHERE TypeName = '{typeName}';";
            this.Log(cmd.CommandText);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task DropIndexAsync<T>(Expression<Func<T, object>> expression, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
    {
        var jsonPath = IndexExpressionHelper.ResolveJsonPath(expression, this.jsonOptions, jsonTypeInfo);
        var typeName = this.ResolveTypeName<T>();
        var indexName = IndexExpressionHelper.BuildIndexName(typeName, jsonPath);

        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = $"DROP INDEX IF EXISTS {indexName};";
            this.Log(cmd.CommandText);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task DropAllIndexesAsync<T>(CancellationToken cancellationToken = default) where T : class
    {
        var typeName = this.ResolveTypeName<T>();
        var sanitizedType = typeName.Replace('.', '_');
        var prefix = $"idx_json_{sanitizedType}_%";

        return this.ExecuteAsync(async () =>
        {
            await using var queryCmd = this.connection.CreateCommand();
            queryCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'documents' AND name LIKE @prefix;";
            queryCmd.Parameters.AddWithValue("@prefix", prefix);

            this.Log(queryCmd.CommandText);
            var indexNames = new List<string>();
            await using (var reader = await queryCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    indexNames.Add(reader.GetString(0));
            }

            foreach (var indexName in indexNames)
            {
                await using var dropCmd = this.connection.CreateCommand();
                dropCmd.CommandText = $"DROP INDEX IF EXISTS {indexName};";
                this.Log(dropCmd.CommandText);
                await dropCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    // ── Static helpers ──────────────────────────────────────────────────

    static JsonTypeInfo<T>? FindTypeInfo<T>(JsonTypeInfo<T>? provided, JsonSerializerOptions options, bool useReflectionFallback)
    {
        if (provided != null)
            return provided;

        if (options.TryGetTypeInfo(typeof(T), out var info) && info is JsonTypeInfo<T> typed)
            return typed;

        if (!useReflectionFallback)
            throw new InvalidOperationException(
                $"No JsonTypeInfo registered for type '{typeof(T).FullName}'. " +
                $"Register it in your JsonSerializerContext or pass a JsonTypeInfo<{typeof(T).Name}> explicitly.");

        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path only used when typeInfo is null (reflection fallback).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path only used when typeInfo is null (reflection fallback).")]
    static string SerializeDocument<T>(T value, JsonTypeInfo<T>? typeInfo, JsonSerializerOptions options)
        => typeInfo != null ? JsonSerializer.Serialize(value, typeInfo) : JsonSerializer.Serialize(value, options);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path only used when typeInfo is null (reflection fallback).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path only used when typeInfo is null (reflection fallback).")]
    static T? DeserializeDocument<T>(string json, JsonTypeInfo<T>? typeInfo, JsonSerializerOptions options)
        => typeInfo != null ? JsonSerializer.Deserialize(json, typeInfo) : JsonSerializer.Deserialize<T>(json, options);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path only used when typeInfo is null (reflection fallback).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path only used when typeInfo is null (reflection fallback).")]
    static string ResolvePropertyPath<T>(Expression<Func<T, object>> property, JsonSerializerOptions options, JsonTypeInfo<T>? typeInfo)
        => typeInfo != null
            ? IndexExpressionHelper.ResolveJsonPath(property, options, typeInfo)
            : IndexExpressionHelper.ResolveJsonPath(property, options);

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Parameter binding via reflection is intentional; dictionary overload available for AOT.")]
    static void BindParameters(SqliteCommand cmd, object? parameters)
    {
        if (parameters is null)
            return;

        if (parameters is IDictionary<string, object?> dict)
        {
            foreach (var kvp in dict)
            {
                var paramName = kvp.Key.StartsWith('@') ? kvp.Key : "@" + kvp.Key;
                cmd.Parameters.AddWithValue(paramName, kvp.Value ?? DBNull.Value);
            }
            return;
        }

        foreach (var prop in parameters.GetType().GetProperties())
        {
            var value = prop.GetValue(parameters);
            cmd.Parameters.AddWithValue("@" + prop.Name, value ?? DBNull.Value);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only serializes System.String which has a built-in converter.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only serializes System.String which has a built-in converter.")]
    static string ToJsonLiteral(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => JsonSerializer.Serialize(s),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };

    static string StripNullProperties(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj)
            return json;

        foreach (var key in obj.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList())
            obj.Remove(key);

        return obj.ToJsonString();
    }

    static void BindDictionaryParameters(SqliteCommand cmd, Dictionary<string, object?> parameters)
    {
        foreach (var kvp in parameters)
        {
            var paramName = kvp.Key.StartsWith('@') ? kvp.Key : "@" + kvp.Key;
            cmd.Parameters.AddWithValue(paramName, kvp.Value ?? DBNull.Value);
        }
    }

    static async Task<IReadOnlyList<T>> ReadListAsync<T>(SqliteCommand cmd, Func<string, T> deserialize, CancellationToken ct)
    {
        var list = new List<T>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            list.Add(deserialize(json));
        }
        return list;
    }

    public void Dispose()
    {
        this.connection.Dispose();
        this.semaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── TransactionalDocumentStore ──────────────────────────────────────

    sealed class TransactionalDocumentStore : IDocumentStore, IQueryExecutor
    {
        readonly SqliteConnection connection;
        readonly System.Data.Common.DbTransaction transaction;
        readonly DocumentStoreOptions options;
        readonly JsonSerializerOptions jsonOptions;
        readonly Action<string>? logging;

        public TransactionalDocumentStore(
            SqliteConnection connection,
            System.Data.Common.DbTransaction transaction,
            DocumentStoreOptions options,
            JsonSerializerOptions jsonOptions,
            Action<string>? logging)
        {
            this.connection = connection;
            this.transaction = transaction;
            this.options = options;
            this.jsonOptions = jsonOptions;
            this.logging = logging;
        }

        void Log(string sql) => this.logging?.Invoke(sql);

        string ResolveTypeName<T>() => TypeNameResolver.Resolve(typeof(T), this.options.TypeNameResolution);

        JsonTypeInfo<T>? FindTypeInfo<T>(JsonTypeInfo<T>? provided)
            => SqliteDocumentStore.FindTypeInfo(provided, this.jsonOptions, this.options.UseReflectionFallback);

        SqliteCommand CreateCommand()
        {
            var cmd = this.connection.CreateCommand();
            cmd.Transaction = (SqliteTransaction)this.transaction;
            return cmd;
        }

        // ── IQueryExecutor ──────────────────────────────────────────────

        Task<TResult> IQueryExecutor.ExecuteAsync<TResult>(Func<Task<TResult>> operation, CancellationToken ct)
            => operation();

        IAsyncEnumerable<T> IQueryExecutor.ReadStreamAsync<T>(Action<SqliteCommand> configure, Func<string, T> deserialize, CancellationToken ct)
            => ReadStreamInternalAsync(configure, deserialize, ct);

        SqliteCommand IQueryExecutor.CreateCommand() => this.CreateCommand();

        string IQueryExecutor.ResolveTypeName<T>() => this.ResolveTypeName<T>();

        JsonSerializerOptions IQueryExecutor.JsonOptions => this.jsonOptions;

        Action<string>? IQueryExecutor.Logging => this.logging;

        async IAsyncEnumerable<T> ReadStreamInternalAsync<T>(
            Action<SqliteCommand> configure,
            Func<string, T> deserialize,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await using var cmd = this.CreateCommand();
            configure(cmd);
            this.Log(cmd.CommandText);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                yield return deserialize(reader.GetString(0));
        }

        // ── Query<T>() ─────────────────────────────────────────────────

        public IDocumentQuery<T> Query<T>(JsonTypeInfo<T>? jsonTypeInfo = null) where T : class
        {
            return new DocumentQuery<T>(this, FindTypeInfo(jsonTypeInfo));
        }

        // ── CRUD ────────────────────────────────────────────────────────

        async Task UpsertCoreAsync(string id, string typeName, string json, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            await using var cmd = this.CreateCommand();
            cmd.CommandText = """
                INSERT INTO documents (Id, TypeName, Data, CreatedAt, UpdatedAt)
                VALUES (@id, @typeName, @data, @now, @now)
                ON CONFLICT(Id, TypeName) DO UPDATE SET
                    Data = @data,
                    UpdatedAt = @now;
                """;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", typeName);
            cmd.Parameters.AddWithValue("@data", json);
            cmd.Parameters.AddWithValue("@now", now);
            this.Log(cmd.CommandText);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        async Task UpsertMergeCoreAsync(string id, string typeName, string json, CancellationToken ct)
        {
            json = StripNullProperties(json);
            var now = DateTimeOffset.UtcNow.ToString("O");
            await using var cmd = this.CreateCommand();
            cmd.CommandText = """
                INSERT INTO documents (Id, TypeName, Data, CreatedAt, UpdatedAt)
                VALUES (@id, @typeName, @data, @now, @now)
                ON CONFLICT(Id, TypeName) DO UPDATE SET
                    Data = json_patch(documents.Data, @data),
                    UpdatedAt = @now;
                """;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", typeName);
            cmd.Parameters.AddWithValue("@data", json);
            cmd.Parameters.AddWithValue("@now", now);
            this.Log(cmd.CommandText);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        async Task<bool> SetPropertyCoreAsync(string id, string typeName, string jsonPath, object? value, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            await using var cmd = this.CreateCommand();
            cmd.CommandText = """
                UPDATE documents
                SET Data = json_set(Data, @path, json(@value)), UpdatedAt = @now
                WHERE Id = @id AND TypeName = @typeName;
                """;
            cmd.Parameters.AddWithValue("@path", "$." + jsonPath);
            cmd.Parameters.AddWithValue("@value", ToJsonLiteral(value));
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", typeName);
            this.Log(cmd.CommandText);
            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return rows > 0;
        }

        async Task<bool> RemovePropertyCoreAsync(string id, string typeName, string jsonPath, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            await using var cmd = this.CreateCommand();
            cmd.CommandText = """
                UPDATE documents
                SET Data = json_remove(Data, @path), UpdatedAt = @now
                WHERE Id = @id AND TypeName = @typeName;
                """;
            cmd.Parameters.AddWithValue("@path", "$." + jsonPath);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", typeName);
            this.Log(cmd.CommandText);
            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return rows > 0;
        }

        public async Task<string> Set<T>(T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        {
            var typeInfo = FindTypeInfo(jsonTypeInfo);
            var id = Guid.NewGuid().ToString("N");
            var json = SerializeDocument(document, typeInfo, this.jsonOptions);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
            return id;
        }

        public async Task Set<T>(string id, T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        {
            var typeInfo = FindTypeInfo(jsonTypeInfo);
            var json = SerializeDocument(document, typeInfo, this.jsonOptions);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
        }

        public async Task Upsert<T>(string id, T patch, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        {
            var typeInfo = FindTypeInfo(jsonTypeInfo);
            var json = SerializeDocument(patch, typeInfo, this.jsonOptions);
            await this.UpsertMergeCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> SetProperty<T>(string id, Expression<Func<T, object>> property, object? value, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        {
            var jsonPath = ResolvePropertyPath(property, this.jsonOptions, FindTypeInfo(jsonTypeInfo));
            return await this.SetPropertyCoreAsync(id, this.ResolveTypeName<T>(), jsonPath, value, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> RemoveProperty<T>(string id, Expression<Func<T, object>> property, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        {
            var jsonPath = ResolvePropertyPath(property, this.jsonOptions, FindTypeInfo(jsonTypeInfo));
            return await this.RemovePropertyCoreAsync(id, this.ResolveTypeName<T>(), jsonPath, cancellationToken).ConfigureAwait(false);
        }

        public async Task<T?> Get<T>(string id, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        {
            var typeInfo = FindTypeInfo(jsonTypeInfo);
            await using var cmd = this.CreateCommand();
            cmd.CommandText = "SELECT Data FROM documents WHERE Id = @id AND TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            this.Log(cmd.CommandText);
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is string json
                ? DeserializeDocument(json, typeInfo, this.jsonOptions)
                : null;
        }

        // ── String-based query ──────────────────────────────────────────

        public async Task<IReadOnlyList<T>> Query<T>(string whereClause, JsonTypeInfo<T>? jsonTypeInfo = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class
        {
            var typeInfo = FindTypeInfo(jsonTypeInfo);
            await using var cmd = this.CreateCommand();
            cmd.CommandText = $"SELECT Data FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindParameters(cmd, parameters);
            this.Log(cmd.CommandText);
            return await ReadListAsync<T>(cmd, json => DeserializeDocument(json, typeInfo, this.jsonOptions)!, cancellationToken).ConfigureAwait(false);
        }

        // ── String-based streaming ──────────────────────────────────────

        public async IAsyncEnumerable<T> QueryStream<T>(string whereClause, JsonTypeInfo<T>? jsonTypeInfo = null, object? parameters = null, [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
        {
            var typeInfo = FindTypeInfo(jsonTypeInfo);
            await using var cmd = this.CreateCommand();
            cmd.CommandText = $"SELECT Data FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindParameters(cmd, parameters);

            this.Log(cmd.CommandText);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                yield return DeserializeDocument(reader.GetString(0), typeInfo, this.jsonOptions)!;
        }

        // ── Count / Remove / Clear ──────────────────────────────────────

        public async Task<int> Count<T>(string? whereClause = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            var sql = "SELECT COUNT(*) FROM documents WHERE TypeName = @typeName";
            if (!string.IsNullOrWhiteSpace(whereClause))
                sql += $" AND ({whereClause})";
            cmd.CommandText = sql + ";";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindParameters(cmd, parameters);

            this.Log(cmd.CommandText);
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result);
        }

        public async Task<bool> Remove<T>(string id, CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            cmd.CommandText = "DELETE FROM documents WHERE Id = @id AND TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            this.Log(cmd.CommandText);
            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rows > 0;
        }

        public async Task<int> Clear<T>(CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            cmd.CommandText = "DELETE FROM documents WHERE TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            this.Log(cmd.CommandText);
            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task RunInTransaction(Func<IDocumentStore, Task> operation, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Nested transactions are not supported.");
        }
    }
}
