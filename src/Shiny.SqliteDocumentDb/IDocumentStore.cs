using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.SqliteDocumentDb;

public interface IDocumentStore
{
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
    /// Gets all documents of the specified type.
    /// </summary>
    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for AOT compatibility.")]
    Task<IReadOnlyList<T>> GetAll<T>(CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Gets all documents of the specified type (AOT-safe).
    /// </summary>
    Task<IReadOnlyList<T>> GetAll<T>(JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Gets all documents of the specified type, projected to a different type at the SQL level (AOT-safe).
    /// Only the fields referenced in the selector are extracted from the stored JSON.
    /// </summary>
    Task<IReadOnlyList<TResult>> GetAll<T, TResult>(
        Expression<Func<T, TResult>> selector,
        JsonTypeInfo<T> sourceTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        CancellationToken cancellationToken = default)
        where T : class where TResult : class;

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
    /// Queries documents using a LINQ expression predicate (AOT-safe).
    /// </summary>
    Task<IReadOnlyList<T>> Query<T>(Expression<Func<T, bool>> predicate, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Queries documents using a LINQ expression predicate and projects to a different type at the SQL level (AOT-safe).
    /// Only the fields referenced in the selector are extracted from the stored JSON.
    /// </summary>
    Task<IReadOnlyList<TResult>> Query<T, TResult>(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, TResult>> selector,
        JsonTypeInfo<T> sourceTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        CancellationToken cancellationToken = default)
        where T : class where TResult : class;

    /// <summary>
    /// Counts documents of the specified type, with an optional WHERE filter.
    /// </summary>
    Task<int> Count<T>(string? whereClause = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Counts documents using a LINQ expression predicate (AOT-safe).
    /// </summary>
    Task<int> Count<T>(Expression<Func<T, bool>> predicate, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes a document by ID.
    /// </summary>
    /// <returns>True if a document was deleted.</returns>
    Task<bool> Remove<T>(string id, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes documents matching a LINQ expression predicate (AOT-safe).
    /// </summary>
    /// <returns>The number of documents deleted.</returns>
    Task<int> Remove<T>(Expression<Func<T, bool>> predicate, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default) where T : class;

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
