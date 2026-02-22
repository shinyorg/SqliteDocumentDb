using Microsoft.Extensions.DependencyInjection;

namespace Shiny.SqliteDocumentDb;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqliteDocumentStore(this IServiceCollection services, string connectionString)
    {
        return services.AddSqliteDocumentStore(opts => opts.ConnectionString = connectionString);
    }

    public static IServiceCollection AddSqliteDocumentStore(this IServiceCollection services, Action<DocumentStoreOptions> configure)
    {
        var options = new DocumentStoreOptions { ConnectionString = null! };
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("ConnectionString must be set.", nameof(configure));

        services.AddSingleton(options);
        services.AddSingleton<IDocumentStore, SqliteDocumentStore>();
        return services;
    }
}
