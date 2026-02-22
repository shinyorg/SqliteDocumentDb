using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Data.Sqlite;
using Shiny.Data.Documents.Internal;

namespace Shiny.Data.Documents;

public class SqliteDocumentStore : IDocumentStore, IDisposable
{
    const string ReflectionMessage = "Use the JsonTypeInfo overload for AOT compatibility.";

    readonly SemaphoreSlim semaphore = new(1, 1);
    readonly SqliteConnection connection;
    readonly DocumentStoreOptions options;
    readonly JsonSerializerOptions jsonOptions;
    bool initialized;

    public SqliteDocumentStore(DocumentStoreOptions options)
    {
        this.options = options;
        this.jsonOptions = options.JsonSerializerOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        this.connection = new SqliteConnection(options.ConnectionString);
    }

    string ResolveTypeName<T>() => TypeNameResolver.Resolve(typeof(T), this.options.TypeNameResolution);

    async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (this.initialized)
            return;

        await this.connection.OpenAsync(ct).ConfigureAwait(false);

        await using var walCmd = this.connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
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

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public Task<string> Set<T>(T document, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            var id = Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(document, this.jsonOptions);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
            return id;
        }, cancellationToken);
    }

    public Task<string> Set<T>(T document, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            var id = Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(document, jsonTypeInfo);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
            return id;
        }, cancellationToken);
    }

    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public Task Set<T>(string id, T document, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            var json = JsonSerializer.Serialize(document, this.jsonOptions);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task Set<T>(string id, T document, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            var json = JsonSerializer.Serialize(document, jsonTypeInfo);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public Task<T?> Get<T>(string id, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = "SELECT Data FROM documents WHERE Id = @id AND TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is string json
                ? JsonSerializer.Deserialize<T>(json, this.jsonOptions)
                : null;
        }, cancellationToken);
    }

    public Task<T?> Get<T>(string id, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = "SELECT Data FROM documents WHERE Id = @id AND TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is string json
                ? JsonSerializer.Deserialize(json, jsonTypeInfo)
                : null;
        }, cancellationToken);
    }

    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public Task<IReadOnlyList<T>> GetAll<T>(CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = "SELECT Data FROM documents WHERE TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            return await ReadListAsync<T>(cmd, json => JsonSerializer.Deserialize<T>(json, this.jsonOptions)!, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<T>> GetAll<T>(JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = "SELECT Data FROM documents WHERE TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            return await ReadListAsync<T>(cmd, json => JsonSerializer.Deserialize(json, jsonTypeInfo)!, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public Task<IReadOnlyList<T>> Query<T>(string whereClause, object? parameters = null, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = $"SELECT Data FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindParameters(cmd, parameters);

            return await ReadListAsync<T>(cmd, json => JsonSerializer.Deserialize<T>(json, this.jsonOptions)!, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<T>> Query<T>(string whereClause, JsonTypeInfo<T> jsonTypeInfo, object? parameters = null, CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = $"SELECT Data FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindParameters(cmd, parameters);

            return await ReadListAsync<T>(cmd, json => JsonSerializer.Deserialize(json, jsonTypeInfo)!, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

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

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<T>> Query<T>(Expression<Func<T, bool>> predicate, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
    {
        var (whereClause, parms) = SqliteJsonExpressionVisitor.Translate(predicate, jsonTypeInfo);
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = $"SELECT Data FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindDictionaryParameters(cmd, parms);

            return await ReadListAsync<T>(cmd, json => JsonSerializer.Deserialize(json, jsonTypeInfo)!, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<int> Count<T>(Expression<Func<T, bool>> predicate, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
    {
        var (whereClause, parms) = SqliteJsonExpressionVisitor.Translate(predicate, jsonTypeInfo);
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindDictionaryParameters(cmd, parms);

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

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rows > 0;
        }, cancellationToken);
    }

    public Task<int> Remove<T>(Expression<Func<T, bool>> predicate, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
    {
        var (whereClause, parms) = SqliteJsonExpressionVisitor.Translate(predicate, jsonTypeInfo);
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindDictionaryParameters(cmd, parms);

            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<int> Clear<T>(CancellationToken cancellationToken = default) where T : class
    {
        return this.ExecuteAsync(async () =>
        {
            await using var cmd = this.connection.CreateCommand();
            cmd.CommandText = "DELETE FROM documents WHERE TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task RunInTransaction(Func<IDocumentStore, Task> operation, CancellationToken cancellationToken = default)
    {
        return this.ExecuteAsync(async () =>
        {
            await using var transaction = await this.connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var txStore = new TransactionalDocumentStore(this.connection, transaction, this.options, this.jsonOptions);
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

    sealed class TransactionalDocumentStore : IDocumentStore
    {
        readonly SqliteConnection connection;
        readonly System.Data.Common.DbTransaction transaction;
        readonly DocumentStoreOptions options;
        readonly JsonSerializerOptions jsonOptions;

        public TransactionalDocumentStore(
            SqliteConnection connection,
            System.Data.Common.DbTransaction transaction,
            DocumentStoreOptions options,
            JsonSerializerOptions jsonOptions)
        {
            this.connection = connection;
            this.transaction = transaction;
            this.options = options;
            this.jsonOptions = jsonOptions;
        }

        string ResolveTypeName<T>() => TypeNameResolver.Resolve(typeof(T), this.options.TypeNameResolution);

        SqliteCommand CreateCommand()
        {
            var cmd = this.connection.CreateCommand();
            cmd.Transaction = (SqliteTransaction)this.transaction;
            return cmd;
        }

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
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        [RequiresUnreferencedCode(ReflectionMessage)]
        [RequiresDynamicCode(ReflectionMessage)]
        public async Task<string> Set<T>(T document, CancellationToken cancellationToken = default) where T : class
        {
            var id = Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(document, this.jsonOptions);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
            return id;
        }

        public async Task<string> Set<T>(T document, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
        {
            var id = Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(document, jsonTypeInfo);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
            return id;
        }

        [RequiresUnreferencedCode(ReflectionMessage)]
        [RequiresDynamicCode(ReflectionMessage)]
        public async Task Set<T>(string id, T document, CancellationToken cancellationToken = default) where T : class
        {
            var json = JsonSerializer.Serialize(document, this.jsonOptions);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
        }

        public async Task Set<T>(string id, T document, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
        {
            var json = JsonSerializer.Serialize(document, jsonTypeInfo);
            await this.UpsertCoreAsync(id, this.ResolveTypeName<T>(), json, cancellationToken).ConfigureAwait(false);
        }

        [RequiresUnreferencedCode(ReflectionMessage)]
        [RequiresDynamicCode(ReflectionMessage)]
        public async Task<T?> Get<T>(string id, CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            cmd.CommandText = "SELECT Data FROM documents WHERE Id = @id AND TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is string json
                ? JsonSerializer.Deserialize<T>(json, this.jsonOptions)
                : null;
        }

        public async Task<T?> Get<T>(string id, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            cmd.CommandText = "SELECT Data FROM documents WHERE Id = @id AND TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is string json
                ? JsonSerializer.Deserialize(json, jsonTypeInfo)
                : null;
        }

        [RequiresUnreferencedCode(ReflectionMessage)]
        [RequiresDynamicCode(ReflectionMessage)]
        public async Task<IReadOnlyList<T>> GetAll<T>(CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            cmd.CommandText = "SELECT Data FROM documents WHERE TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            return await ReadListAsync<T>(cmd, json => JsonSerializer.Deserialize<T>(json, this.jsonOptions)!, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<T>> GetAll<T>(JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            cmd.CommandText = "SELECT Data FROM documents WHERE TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            return await ReadListAsync<T>(cmd, json => JsonSerializer.Deserialize(json, jsonTypeInfo)!, cancellationToken).ConfigureAwait(false);
        }

        [RequiresUnreferencedCode(ReflectionMessage)]
        [RequiresDynamicCode(ReflectionMessage)]
        public async Task<IReadOnlyList<T>> Query<T>(string whereClause, object? parameters = null, CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            cmd.CommandText = $"SELECT Data FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindParameters(cmd, parameters);
            return await ReadListAsync<T>(cmd, json => JsonSerializer.Deserialize<T>(json, this.jsonOptions)!, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<T>> Query<T>(string whereClause, JsonTypeInfo<T> jsonTypeInfo, object? parameters = null, CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            cmd.CommandText = $"SELECT Data FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindParameters(cmd, parameters);
            return await ReadListAsync<T>(cmd, json => JsonSerializer.Deserialize(json, jsonTypeInfo)!, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> Count<T>(string? whereClause = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            var sql = "SELECT COUNT(*) FROM documents WHERE TypeName = @typeName";
            if (!string.IsNullOrWhiteSpace(whereClause))
                sql += $" AND ({whereClause})";
            cmd.CommandText = sql + ";";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindParameters(cmd, parameters);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result);
        }

        public async Task<IReadOnlyList<T>> Query<T>(Expression<Func<T, bool>> predicate, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
        {
            var (whereClause, parms) = SqliteJsonExpressionVisitor.Translate(predicate, jsonTypeInfo);
            await using var cmd = this.CreateCommand();
            cmd.CommandText = $"SELECT Data FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindDictionaryParameters(cmd, parms);
            return await ReadListAsync<T>(cmd, json => JsonSerializer.Deserialize(json, jsonTypeInfo)!, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> Count<T>(Expression<Func<T, bool>> predicate, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
        {
            var (whereClause, parms) = SqliteJsonExpressionVisitor.Translate(predicate, jsonTypeInfo);
            await using var cmd = this.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindDictionaryParameters(cmd, parms);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result);
        }

        public async Task<bool> Remove<T>(string id, CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            cmd.CommandText = "DELETE FROM documents WHERE Id = @id AND TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rows > 0;
        }

        public async Task<int> Remove<T>(Expression<Func<T, bool>> predicate, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class
        {
            var (whereClause, parms) = SqliteJsonExpressionVisitor.Translate(predicate, jsonTypeInfo);
            await using var cmd = this.CreateCommand();
            cmd.CommandText = $"DELETE FROM documents WHERE TypeName = @typeName AND ({whereClause});";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            BindDictionaryParameters(cmd, parms);
            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> Clear<T>(CancellationToken cancellationToken = default) where T : class
        {
            await using var cmd = this.CreateCommand();
            cmd.CommandText = "DELETE FROM documents WHERE TypeName = @typeName;";
            cmd.Parameters.AddWithValue("@typeName", this.ResolveTypeName<T>());
            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task RunInTransaction(Func<IDocumentStore, Task> operation, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Nested transactions are not supported.");
        }
    }
}
