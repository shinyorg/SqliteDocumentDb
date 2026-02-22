using System.Text.Json;
using Shiny.SqliteDocumentDb.Tests.Fixtures;
using Xunit;

namespace Shiny.SqliteDocumentDb.Tests;

public class StreamingTests : IDisposable
{
    readonly SqliteDocumentStore store;
    static readonly TestJsonContext ctx = new(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    public StreamingTests()
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
        await this.store.Set("u3", new User { Name = "Charlie", Age = 25 }, ctx.User);
    }

    async Task SeedOrdersAsync()
    {
        await this.store.Set("o1", new Order
        {
            CustomerName = "Alice",
            Status = "Shipped",
            ShippingAddress = new Address { Street = "123 Main St", City = "Portland", State = "OR", Zip = "97201" },
            Lines =
            [
                new OrderLine { ProductName = "Widget", Quantity = 2, UnitPrice = 9.99m },
                new OrderLine { ProductName = "Gadget", Quantity = 1, UnitPrice = 24.99m }
            ],
            Tags = ["priority", "wholesale"]
        }, ctx.Order);

        await this.store.Set("o2", new Order
        {
            CustomerName = "Bob",
            Status = "Pending",
            ShippingAddress = new Address { Street = "456 Oak Ave", City = "Seattle", State = "WA", Zip = "98101" },
            Lines =
            [
                new OrderLine { ProductName = "Widget", Quantity = 5, UnitPrice = 9.99m }
            ],
            Tags = ["retail"]
        }, ctx.Order);
    }

    static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source.ConfigureAwait(false))
            list.Add(item);
        return list;
    }

    [Fact]
    public async Task GetAllStream_ReturnsAllItems()
    {
        await this.SeedUsersAsync();

        var results = await ToListAsync(this.store.GetAllStream<User>(ctx.User));

        Assert.Equal(3, results.Count);
        Assert.Contains(results, u => u.Name == "Alice");
        Assert.Contains(results, u => u.Name == "Bob");
        Assert.Contains(results, u => u.Name == "Charlie");
    }

    [Fact]
    public async Task GetAllStream_Projection()
    {
        await this.SeedOrdersAsync();

        var results = await ToListAsync(
            this.store.GetAllStream<Order, OrderSummary>(
                o => new OrderSummary { Customer = o.CustomerName, City = o.ShippingAddress.City },
                ctx.Order,
                ctx.OrderSummary));

        Assert.Equal(2, results.Count);
        Assert.Contains(results, s => s.Customer == "Alice" && s.City == "Portland");
        Assert.Contains(results, s => s.Customer == "Bob" && s.City == "Seattle");
    }

    [Fact]
    public async Task QueryStream_Expression()
    {
        await this.SeedUsersAsync();

        var results = await ToListAsync(
            this.store.QueryStream<User>(u => u.Age == 25, ctx.User));

        Assert.Equal(2, results.Count);
        Assert.Contains(results, u => u.Name == "Alice");
        Assert.Contains(results, u => u.Name == "Charlie");
    }

    [Fact]
    public async Task QueryStream_RawSql()
    {
        await this.SeedUsersAsync();

        var results = await ToListAsync(
            this.store.QueryStream<User>(
                "json_extract(Data, '$.name') = @name",
                ctx.User,
                new { name = "Alice" }));

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
    }

    [Fact]
    public async Task QueryStream_ExpressionProjection()
    {
        await this.SeedOrdersAsync();

        var results = await ToListAsync(
            this.store.QueryStream<Order, OrderSummary>(
                o => o.Status == "Shipped",
                o => new OrderSummary { Customer = o.CustomerName, City = o.ShippingAddress.City },
                ctx.Order,
                ctx.OrderSummary));

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Customer);
        Assert.Equal("Portland", results[0].City);
    }

    [Fact]
    public async Task QueryStream_EmptyResult()
    {
        await this.SeedUsersAsync();

        var results = await ToListAsync(
            this.store.QueryStream<User>(u => u.Name == "Nobody", ctx.User));

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetAllStream_NestedObjects()
    {
        await this.SeedOrdersAsync();

        var results = await ToListAsync(this.store.GetAllStream<Order>(ctx.Order));

        Assert.Equal(2, results.Count);

        var alice = Assert.Single(results, o => o.CustomerName == "Alice");
        Assert.Equal("Portland", alice.ShippingAddress.City);
        Assert.Equal(2, alice.Lines.Count);
        Assert.Contains(alice.Lines, l => l.ProductName == "Widget");
        Assert.Contains(alice.Tags, t => t == "priority");
    }
}
