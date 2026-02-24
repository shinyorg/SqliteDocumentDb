using System.Linq.Expressions;

namespace Shiny.SqliteDocumentDb;

/// <summary>
/// Specifies an ordering for query results based on a JSON property expression.
/// </summary>
public readonly struct OrderBy<T> where T : class
{
    internal Expression<Func<T, object>> Selector { get; }
    internal bool IsDescending { get; }

    OrderBy(Expression<Func<T, object>> selector, bool descending)
    {
        this.Selector = selector;
        this.IsDescending = descending;
    }

    /// <summary>
    /// Creates an ascending order specification.
    /// </summary>
    public static OrderBy<T> Ascending(Expression<Func<T, object>> selector) => new(selector, false);

    /// <summary>
    /// Creates a descending order specification.
    /// </summary>
    public static OrderBy<T> Descending(Expression<Func<T, object>> selector) => new(selector, true);
}
