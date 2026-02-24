using System.Linq.Expressions;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.SqliteDocumentDb;

public interface IDocumentQuery<T> where T : class
{
    /// <summary>
    /// Filters documents matching the given predicate. Multiple calls are combined with AND.
    /// </summary>
    IDocumentQuery<T> Where(Expression<Func<T, bool>> predicate);

    /// <summary>
    /// Sorts results by the selected property in ascending order.
    /// </summary>
    IDocumentQuery<T> OrderBy(Expression<Func<T, object>> selector);

    /// <summary>
    /// Sorts results by the selected property in descending order.
    /// </summary>
    IDocumentQuery<T> OrderByDescending(Expression<Func<T, object>> selector);

    /// <summary>
    /// Groups results by the selected property.
    /// </summary>
    IDocumentQuery<T> GroupBy(Expression<Func<T, object>> selector);

    /// <summary>
    /// Projects each document into a new shape using a server-side SQL projection.
    /// </summary>
    /// <param name="selector">Expression defining the projection.</param>
    /// <param name="resultTypeInfo">Optional type metadata for AOT-safe serialization. When null, resolved from <see cref="DocumentStoreOptions.JsonSerializerOptions"/> or via reflection.</param>
    IDocumentQuery<TResult> Select<TResult>(
        Expression<Func<T, TResult>> selector,
        JsonTypeInfo<TResult>? resultTypeInfo = null) where TResult : class;

    /// <summary>
    /// Materializes all matching documents into a list.
    /// </summary>
    Task<IReadOnlyList<T>> ToList(CancellationToken ct = default);

    /// <summary>
    /// Streams matching documents one at a time without buffering.
    /// </summary>
    IAsyncEnumerable<T> ToAsyncEnumerable(CancellationToken ct = default);

    /// <summary>
    /// Returns the number of matching documents.
    /// </summary>
    Task<long> Count(CancellationToken ct = default);

    /// <summary>
    /// Returns true if at least one document matches the current filters.
    /// </summary>
    Task<bool> Any(CancellationToken ct = default);

    /// <summary>
    /// Deletes all documents matching the current filters and returns the number deleted.
    /// </summary>
    Task<int> Remove(CancellationToken ct = default);

    /// <summary>
    /// Returns the maximum value of the selected property across matching documents.
    /// </summary>
    Task<TValue> Max<TValue>(Expression<Func<T, TValue>> selector, CancellationToken ct = default);

    /// <summary>
    /// Returns the minimum value of the selected property across matching documents.
    /// </summary>
    Task<TValue> Min<TValue>(Expression<Func<T, TValue>> selector, CancellationToken ct = default);

    /// <summary>
    /// Returns the sum of the selected property across matching documents.
    /// </summary>
    Task<TValue> Sum<TValue>(Expression<Func<T, TValue>> selector, CancellationToken ct = default);

    /// <summary>
    /// Returns the average of the selected numeric property across matching documents.
    /// </summary>
    Task<double> Average(Expression<Func<T, object>> selector, CancellationToken ct = default);
}
