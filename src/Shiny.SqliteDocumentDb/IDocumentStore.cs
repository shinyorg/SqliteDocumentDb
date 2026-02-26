using System.Linq.Expressions;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.SqliteDocumentDb;

public interface IDocumentStore
{
    /// <summary>
    /// Returns a fluent query builder for the specified type.
    /// </summary>
    /// <param name="jsonTypeInfo">Optional type metadata for AOT-safe serialization. When null, resolved from <see cref="DocumentStoreOptions.JsonSerializerOptions"/> or via reflection.</param>
    IDocumentQuery<T> Query<T>(JsonTypeInfo<T>? jsonTypeInfo = null) where T : class;

    /// <summary>
    /// Upserts a document. The document must have a public Id property (Guid, int, long, or string).
    /// If the Id is the default value, it will be auto-generated.
    /// </summary>
    /// <param name="document">The document to store.</param>
    /// <param name="jsonTypeInfo">Optional type metadata for AOT-safe serialization. When null, resolved from <see cref="DocumentStoreOptions.JsonSerializerOptions"/> or via reflection.</param>
    Task Set<T>(T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Upserts a document using RFC 7396 JSON Merge Patch. The document must have a non-default Id.
    /// If the document exists, the patch is deep-merged into the existing JSON; if it doesn't exist, the patch is inserted as-is.
    /// </summary>
    /// <param name="patch">The patch document to merge. Must have a non-default Id.</param>
    /// <param name="jsonTypeInfo">Optional type metadata for AOT-safe serialization. When null, resolved from <see cref="DocumentStoreOptions.JsonSerializerOptions"/> or via reflection.</param>
    Task Upsert<T>(T patch, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Updates a single property on an existing document using json_set.
    /// </summary>
    /// <param name="id">The document ID.</param>
    /// <param name="property">Expression selecting the property to set.</param>
    /// <param name="value">The new value.</param>
    /// <param name="jsonTypeInfo">Optional type metadata for AOT-safe serialization. When null, resolved from <see cref="DocumentStoreOptions.JsonSerializerOptions"/> or via reflection.</param>
    /// <returns>True if a document was updated, false if not found.</returns>
    Task<bool> SetProperty<T>(string id, Expression<Func<T, object>> property, object? value, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes a single property from an existing document using json_remove.
    /// </summary>
    /// <param name="id">The document ID.</param>
    /// <param name="property">Expression selecting the property to remove.</param>
    /// <param name="jsonTypeInfo">Optional type metadata for AOT-safe serialization. When null, resolved from <see cref="DocumentStoreOptions.JsonSerializerOptions"/> or via reflection.</param>
    /// <returns>True if a document was updated, false if not found.</returns>
    Task<bool> RemoveProperty<T>(string id, Expression<Func<T, object>> property, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Gets a document by ID.
    /// </summary>
    /// <param name="id">The document ID.</param>
    /// <param name="jsonTypeInfo">Optional type metadata for AOT-safe serialization. When null, resolved from <see cref="DocumentStoreOptions.JsonSerializerOptions"/> or via reflection.</param>
    Task<T?> Get<T>(string id, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Queries documents using a SQL WHERE clause fragment with json_extract.
    /// </summary>
    /// <param name="whereClause">SQL WHERE clause, e.g. "json_extract(Data, '$.Name') = @name"</param>
    /// <param name="jsonTypeInfo">Optional type metadata for AOT-safe serialization. When null, resolved from <see cref="DocumentStoreOptions.JsonSerializerOptions"/> or via reflection.</param>
    /// <param name="parameters">Anonymous object or dictionary of parameter values.</param>
    Task<IReadOnlyList<T>> Query<T>(string whereClause, JsonTypeInfo<T>? jsonTypeInfo = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Streams documents matching a SQL WHERE clause fragment one-at-a-time.
    /// </summary>
    /// <param name="whereClause">SQL WHERE clause, e.g. "json_extract(Data, '$.Name') = @name"</param>
    /// <param name="jsonTypeInfo">Optional type metadata for AOT-safe serialization. When null, resolved from <see cref="DocumentStoreOptions.JsonSerializerOptions"/> or via reflection.</param>
    /// <param name="parameters">Anonymous object or dictionary of parameter values.</param>
    IAsyncEnumerable<T> QueryStream<T>(string whereClause, JsonTypeInfo<T>? jsonTypeInfo = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Counts documents of the specified type, with an optional WHERE filter.
    /// </summary>
    Task<int> Count<T>(string? whereClause = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes a document by ID.
    /// </summary>
    /// <returns>True if a document was deleted.</returns>
    Task<bool> Remove<T>(string id, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes all documents of the specified type.
    /// </summary>
    /// <returns>The number of documents deleted.</returns>
    Task<int> Clear<T>(CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Executes multiple operations within a single SQLite transaction.
    /// </summary>
    Task RunInTransaction(Func<IDocumentStore, Task> operation, CancellationToken cancellationToken = default);
}
