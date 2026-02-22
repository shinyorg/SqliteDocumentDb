using Shiny.Data.Documents.Tests.Fixtures;
using Xunit;

namespace Shiny.Data.Documents.Tests;

public class AotSerializationTests : IDisposable
{
    readonly SqliteDocumentStore store;

    public AotSerializationTests()
    {
        this.store = new SqliteDocumentStore(new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        });
    }

    public void Dispose() => this.store.Dispose();

    [Fact]
    public async Task Set_And_Get_WithJsonTypeInfo()
    {
        var user = new User { Name = "Allan", Age = 30, Email = "allan@test.com" };
        var id = await this.store.Set(user, TestJsonContext.Default.User);

        var result = await this.store.Get(id, TestJsonContext.Default.User);

        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(30, result.Age);
    }

    [Fact]
    public async Task Set_WithExplicitId_And_Get_WithJsonTypeInfo()
    {
        await this.store.Set("user-aot", new User { Name = "AOT User" }, TestJsonContext.Default.User);

        var result = await this.store.Get("user-aot", TestJsonContext.Default.User);

        Assert.NotNull(result);
        Assert.Equal("AOT User", result.Name);
    }

    [Fact]
    public async Task GetAll_WithJsonTypeInfo()
    {
        await this.store.Set("u1", new User { Name = "Alice" }, TestJsonContext.Default.User);
        await this.store.Set("u2", new User { Name = "Bob" }, TestJsonContext.Default.User);

        var results = await this.store.GetAll(TestJsonContext.Default.User);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Query_WithJsonTypeInfo()
    {
        await this.store.Set("u1", new User { Name = "Alice", Age = 25 }, TestJsonContext.Default.User);
        await this.store.Set("u2", new User { Name = "Bob", Age = 35 }, TestJsonContext.Default.User);

        var results = await this.store.Query(
            "json_extract(Data, '$.Name') = @name",
            TestJsonContext.Default.User,
            new { name = "Alice" });

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
    }

    [Fact]
    public async Task Product_WithJsonTypeInfo()
    {
        await this.store.Set("p1", new Product { Title = "Widget", Price = 9.99m }, TestJsonContext.Default.Product);

        var result = await this.store.Get("p1", TestJsonContext.Default.Product);

        Assert.NotNull(result);
        Assert.Equal("Widget", result.Title);
        Assert.Equal(9.99m, result.Price);
    }
}
