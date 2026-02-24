using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Shiny.SqliteDocumentDb.Internal;

internal interface IQueryExecutor
{
    Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation, CancellationToken ct);
    IAsyncEnumerable<T> ReadStreamAsync<T>(Action<SqliteCommand> configure, Func<string, T> deserialize, [EnumeratorCancellation] CancellationToken ct = default);
    SqliteCommand CreateCommand();
    string ResolveTypeName<T>();
    JsonSerializerOptions JsonOptions { get; }
    Action<string>? Logging { get; }
}
