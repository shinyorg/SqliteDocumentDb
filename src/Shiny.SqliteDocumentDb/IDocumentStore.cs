using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.SqliteDocumentDb;

public interface IDocumentStore
{
    /// <summary>
    /// Returns a fluent query builder for the specified type (AOT-safe).
    /// </summary>
    IDocumentQuery<T> Query<T>(JsonTypeInfo<T> jsonTypeInfo) where T : class;

    /// <summary>
    /// Returns a fluent query builder for the specified type.
    /// </summary>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    IDocumentQuery<T> Query<T>() where T : class;

    /// <summary>
    /// Upserts a document with an auto-generated GUID key.
    /// </summary>
    /// <returns>The generated document ID.</returns>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    Task<string> Set<T>(T document, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Upserts a document with an auto-generated GUID key (AOT-safe).
    /// </summary>
    /// <returns>The generated document ID.</returns>
    Task<string> Set<T>(T document, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Upserts a document with a user-provided key.
    /// </summary>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    Task Set<T>(string id, T document, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Upserts a document with a user-provided key (AOT-safe).
    /// </summary>
    Task Set<T>(string id, T document, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Upserts a document using RFC 7396 JSON Merge Patch. If the document exists, the patch
    /// is deep-merged into the existing JSON; if it doesn't exist, the patch is inserted as-is.
    /// </summary>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    Task Upsert<T>(string id, T patch, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Upserts a document using RFC 7396 JSON Merge Patch (AOT-safe). If the document exists, the patch
    /// is deep-merged into the existing JSON; if it doesn't exist, the patch is inserted as-is.
    /// </summary>
    Task Upsert<T>(string id, T patch, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Updates a single property on an existing document using json_set.
    /// </summary>
    /// <returns>True if a document was updated, false if not found.</returns>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    Task<bool> SetProperty<T>(string id, Expression<Func<T, object>> property, object? value, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Updates a single property on an existing document using json_set (AOT-safe).
    /// </summary>
    /// <returns>True if a document was updated, false if not found.</returns>
    Task<bool> SetProperty<T>(string id, Expression<Func<T, object>> property, object? value, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes a single property from an existing document using json_remove.
    /// </summary>
    /// <returns>True if a document was updated, false if not found.</returns>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    Task<bool> RemoveProperty<T>(string id, Expression<Func<T, object>> property, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes a single property from an existing document using json_remove (AOT-safe).
    /// </summary>
    /// <returns>True if a document was updated, false if not found.</returns>
    Task<bool> RemoveProperty<T>(string id, Expression<Func<T, object>> property, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Gets a document by ID.
    /// </summary>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    Task<T?> Get<T>(string id, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Gets a document by ID (AOT-safe).
    /// </summary>
    Task<T?> Get<T>(string id, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Queries documents using a SQL WHERE clause fragment with json_extract.
    /// </summary>
    /// <param name="whereClause">SQL WHERE clause, e.g. "json_extract(Data, '$.Name') = @name"</param>
    /// <param name="parameters">Anonymous object or dictionary of parameter values.</param>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    Task<IReadOnlyList<T>> Query<T>(string whereClause, object? parameters = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Queries documents using a SQL WHERE clause fragment with json_extract (AOT-safe).
    /// </summary>
    Task<IReadOnlyList<T>> Query<T>(string whereClause, JsonTypeInfo<T> jsonTypeInfo, object? parameters = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Streams documents matching a SQL WHERE clause fragment one-at-a-time.
    /// </summary>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    IAsyncEnumerable<T> QueryStream<T>(string whereClause, object? parameters = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Streams documents matching a SQL WHERE clause fragment one-at-a-time (AOT-safe).
    /// </summary>
    IAsyncEnumerable<T> QueryStream<T>(string whereClause, JsonTypeInfo<T> jsonTypeInfo, object? parameters = null, CancellationToken cancellationToken = default) where T : class;

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
