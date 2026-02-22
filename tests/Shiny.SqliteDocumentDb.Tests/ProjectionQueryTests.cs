using System.Text.Json;
using Shiny.SqliteDocumentDb.Tests.Fixtures;
using Xunit;

namespace Shiny.SqliteDocumentDb.Tests;

public class ProjectionQueryTests : IDisposable
{
    readonly SqliteDocumentStore store;
    static readonly TestJsonContext ctx = new(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    public ProjectionQueryTests()
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

        await this.store.Set("o3", new Order
        {
            CustomerName = "Charlie",
            Status = "Shipped",
            ShippingAddress = new Address { Street = "789 Elm Blvd", City = "Portland", State = "OR", Zip = "97205" },
            Lines =
            [
                new OrderLine { ProductName = "Doohickey", Quantity = 3, UnitPrice = 14.99m },
                new OrderLine { ProductName = "Gadget", Quantity = 2, UnitPrice = 24.99m }
            ],
            Tags = ["priority", "retail"]
        }, ctx.Order);
    }

    // ── Flat projection ─────────────────────────────────────────────

    [Fact]
    public async Task Query_FlatProjection()
    {
        await this.SeedUsersAsync();

        var results = await this.store.Query<User, UserSummary>(
            u => u.Age == 25,
            u => new UserSummary { Name = u.Name, Email = u.Email },
            ctx.User,
            ctx.UserSummary);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Name == "Alice" && r.Email == "alice@test.com");
        Assert.Contains(results, r => r.Name == "Charlie" && r.Email == null);
    }

    // ── Nested source property ──────────────────────────────────────

    [Fact]
    public async Task Query_NestedSourceProjection()
    {
        await this.SeedOrdersAsync();

        var results = await this.store.Query<Order, OrderSummary>(
            o => o.Status == "Shipped",
            o => new OrderSummary { Customer = o.CustomerName, City = o.ShippingAddress.City },
            ctx.Order,
            ctx.OrderSummary);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Customer == "Alice" && r.City == "Portland");
        Assert.Contains(results, r => r.Customer == "Charlie" && r.City == "Portland");
    }

    // ── GetAll with projection ──────────────────────────────────────

    [Fact]
    public async Task GetAll_WithProjection()
    {
        await this.SeedUsersAsync();

        var results = await this.store.GetAll<User, UserSummary>(
            u => new UserSummary { Name = u.Name, Email = u.Email },
            ctx.User,
            ctx.UserSummary);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.Name == "Alice");
        Assert.Contains(results, r => r.Name == "Bob");
        Assert.Contains(results, r => r.Name == "Charlie");
    }

    // ── Projection with predicate filtering ─────────────────────────

    [Fact]
    public async Task Query_ProjectionWithPredicate()
    {
        await this.SeedUsersAsync();

        var results = await this.store.Query<User, UserSummary>(
            u => u.Name == "Alice",
            u => new UserSummary { Name = u.Name, Email = u.Email },
            ctx.User,
            ctx.UserSummary);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal("alice@test.com", results[0].Email);
    }

    // ── Empty result set ────────────────────────────────────────────

    [Fact]
    public async Task Query_Projection_EmptyResult()
    {
        await this.SeedUsersAsync();

        var results = await this.store.Query<User, UserSummary>(
            u => u.Name == "Nobody",
            u => new UserSummary { Name = u.Name, Email = u.Email },
            ctx.User,
            ctx.UserSummary);

        Assert.Empty(results);
    }

    // ── Null property in projection ─────────────────────────────────

    [Fact]
    public async Task Query_Projection_NullProperty()
    {
        await this.SeedUsersAsync();

        var results = await this.store.Query<User, UserSummary>(
            u => u.Name == "Bob",
            u => new UserSummary { Name = u.Name, Email = u.Email },
            ctx.User,
            ctx.UserSummary);

        Assert.Single(results);
        Assert.Equal("Bob", results[0].Name);
        Assert.Null(results[0].Email);
    }

    // ── Count() no predicate ────────────────────────────────────────

    [Fact]
    public async Task Query_Projection_CountNoPredicate()
    {
        await this.SeedOrdersAsync();

        var results = await this.store.Query<Order, OrderDetail>(
            o => o.CustomerName == "Alice",
            o => new OrderDetail { Customer = o.CustomerName, LineCount = o.Lines.Count() },
            ctx.Order,
            ctx.OrderDetail);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Customer);
        Assert.Equal(2, results[0].LineCount);
    }

    // ── Count(predicate) ────────────────────────────────────────────

    [Fact]
    public async Task Query_Projection_CountWithPredicate()
    {
        await this.SeedOrdersAsync();

        var results = await this.store.Query<Order, OrderDetail>(
            o => o.Status == "Shipped",
            o => new OrderDetail { Customer = o.CustomerName, LineCount = o.Lines.Count(l => l.ProductName == "Gadget") },
            ctx.Order,
            ctx.OrderDetail);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Customer == "Alice" && r.LineCount == 1);
        Assert.Contains(results, r => r.Customer == "Charlie" && r.LineCount == 1);
    }

    // ── Any() no predicate ──────────────────────────────────────────

    [Fact]
    public async Task Query_Projection_AnyNoPredicate()
    {
        await this.SeedOrdersAsync();

        var results = await this.store.GetAll<Order, OrderDetail>(
            o => new OrderDetail { Customer = o.CustomerName, HasPriority = o.Tags.Any() },
            ctx.Order,
            ctx.OrderDetail);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.HasPriority));
    }

    // ── Any(predicate) ──────────────────────────────────────────────

    [Fact]
    public async Task Query_Projection_AnyWithPredicate()
    {
        await this.SeedOrdersAsync();

        var results = await this.store.GetAll<Order, OrderDetail>(
            o => new OrderDetail { Customer = o.CustomerName, HasPriority = o.Tags.Any(t => t == "priority") },
            ctx.Order,
            ctx.OrderDetail);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.Customer == "Alice" && r.HasPriority);
        Assert.Contains(results, r => r.Customer == "Bob" && !r.HasPriority);
        Assert.Contains(results, r => r.Customer == "Charlie" && r.HasPriority);
    }

    // ── GetAll with Count ───────────────────────────────────────────

    [Fact]
    public async Task GetAll_Projection_WithCount()
    {
        await this.SeedOrdersAsync();

        var results = await this.store.GetAll<Order, OrderDetail>(
            o => new OrderDetail { Customer = o.CustomerName, LineCount = o.Lines.Count() },
            ctx.Order,
            ctx.OrderDetail);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.Customer == "Alice" && r.LineCount == 2);
        Assert.Contains(results, r => r.Customer == "Bob" && r.LineCount == 1);
        Assert.Contains(results, r => r.Customer == "Charlie" && r.LineCount == 2);
    }
}
