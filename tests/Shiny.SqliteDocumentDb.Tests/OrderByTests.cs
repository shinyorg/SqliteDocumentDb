using System.Text.Json;
using Shiny.SqliteDocumentDb.Tests.Fixtures;
using Xunit;

namespace Shiny.SqliteDocumentDb.Tests;

public class OrderByTests : IDisposable
{
    readonly SqliteDocumentStore store;
    static readonly TestJsonContext ctx = new(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    public OrderByTests()
    {
        this.store = new SqliteDocumentStore(new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            JsonSerializerOptions = ctx.Options
        });
    }

    public void Dispose() => this.store.Dispose();

    async Task SeedUsersAsync()
    {
        await this.store.Set("u1", new User { Name = "Alice", Age = 25, Email = "alice@test.com" }, ctx.User);
        await this.store.Set("u2", new User { Name = "Bob", Age = 35 }, ctx.User);
        await this.store.Set("u3", new User { Name = "Charlie", Age = 30 }, ctx.User);
    }

    // ── GetAll ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_OrderByAscending()
    {
        await this.SeedUsersAsync();
        var results = await this.store.GetAll(ctx.User, OrderBy<User>.Ascending(u => u.Age));
        Assert.Equal(3, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal("Charlie", results[1].Name);
        Assert.Equal("Bob", results[2].Name);
    }

    [Fact]
    public async Task GetAll_OrderByDescending()
    {
        await this.SeedUsersAsync();
        var results = await this.store.GetAll(ctx.User, OrderBy<User>.Descending(u => u.Age));
        Assert.Equal(3, results.Count);
        Assert.Equal("Bob", results[0].Name);
        Assert.Equal("Charlie", results[1].Name);
        Assert.Equal("Alice", results[2].Name);
    }

    [Fact]
    public async Task GetAll_OrderByString()
    {
        await this.SeedUsersAsync();
        var results = await this.store.GetAll(ctx.User, OrderBy<User>.Ascending(u => u.Name));
        Assert.Equal(3, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal("Charlie", results[2].Name);
    }

    [Fact]
    public async Task GetAll_NoOrderBy_ReturnsAllDocuments()
    {
        await this.SeedUsersAsync();
        var results = await this.store.GetAll(ctx.User);
        Assert.Equal(3, results.Count);
    }

    // ── Query with expression predicate ─────────────────────────

    [Fact]
    public async Task Query_Expression_OrderByAscending()
    {
        await this.SeedUsersAsync();
        var results = await this.store.Query<User>(u => u.Age >= 25, ctx.User, OrderBy<User>.Ascending(u => u.Age));
        Assert.Equal(3, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal("Charlie", results[1].Name);
        Assert.Equal("Bob", results[2].Name);
    }

    [Fact]
    public async Task Query_Expression_OrderByDescending()
    {
        await this.SeedUsersAsync();
        var results = await this.store.Query<User>(u => u.Age > 25, ctx.User, OrderBy<User>.Descending(u => u.Age));
        Assert.Equal(2, results.Count);
        Assert.Equal("Bob", results[0].Name);
        Assert.Equal("Charlie", results[1].Name);
    }

    // ── GetAllStream ────────────────────────────────────────────

    [Fact]
    public async Task GetAllStream_OrderByAscending()
    {
        await this.SeedUsersAsync();
        var results = new List<User>();
        await foreach (var user in this.store.GetAllStream(ctx.User, OrderBy<User>.Ascending(u => u.Age)))
            results.Add(user);

        Assert.Equal(3, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal("Charlie", results[1].Name);
        Assert.Equal("Bob", results[2].Name);
    }

    [Fact]
    public async Task GetAllStream_OrderByDescending()
    {
        await this.SeedUsersAsync();
        var results = new List<User>();
        await foreach (var user in this.store.GetAllStream(ctx.User, OrderBy<User>.Descending(u => u.Age)))
            results.Add(user);

        Assert.Equal(3, results.Count);
        Assert.Equal("Bob", results[0].Name);
        Assert.Equal("Charlie", results[1].Name);
        Assert.Equal("Alice", results[2].Name);
    }

    // ── QueryStream with expression predicate ───────────────────

    [Fact]
    public async Task QueryStream_Expression_OrderByAscending()
    {
        await this.SeedUsersAsync();
        var results = new List<User>();
        await foreach (var user in this.store.QueryStream<User>(u => u.Age > 25, ctx.User, OrderBy<User>.Ascending(u => u.Age)))
            results.Add(user);

        Assert.Equal(2, results.Count);
        Assert.Equal("Charlie", results[0].Name);
        Assert.Equal("Bob", results[1].Name);
    }

    // ── Projection + OrderBy ────────────────────────────────────

    [Fact]
    public async Task GetAll_Projection_OrderBy()
    {
        await this.SeedUsersAsync();
        var results = await this.store.GetAll<User, UserSummary>(
            u => new UserSummary { Name = u.Name, Email = u.Email },
            ctx.User,
            ctx.UserSummary,
            OrderBy<User>.Ascending(u => u.Name));

        Assert.Equal(3, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal("Charlie", results[2].Name);
    }

    [Fact]
    public async Task Query_Projection_OrderBy()
    {
        await this.SeedUsersAsync();
        var results = await this.store.Query<User, UserSummary>(
            u => u.Age >= 25,
            u => new UserSummary { Name = u.Name, Email = u.Email },
            ctx.User,
            ctx.UserSummary,
            OrderBy<User>.Descending(u => u.Name));

        Assert.Equal(3, results.Count);
        Assert.Equal("Charlie", results[0].Name);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal("Alice", results[2].Name);
    }
}
