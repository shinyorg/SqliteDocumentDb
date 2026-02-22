#pragma warning disable IL2026, IL3050 // Reflection-based serialization in tests is fine

using Shiny.SqliteDocumentDb.Tests.Fixtures;
using Xunit;

namespace Shiny.SqliteDocumentDb.Tests;

public class DocumentStoreTests : IDisposable
{
    readonly SqliteDocumentStore store;

    public DocumentStoreTests()
    {
        this.store = new SqliteDocumentStore(new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        });
    }

    public void Dispose() => this.store.Dispose();

    [Fact]
    public async Task Set_WithAutoId_ReturnsNonEmptyId()
    {
        var id = await this.store.Set(new User { Name = "Allan", Age = 30 });
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public async Task Set_And_Get_RoundTrips()
    {
        var user = new User { Name = "Allan", Age = 30, Email = "allan@test.com" };
        var id = await this.store.Set(user);

        var result = await this.store.Get<User>(id);

        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(30, result.Age);
        Assert.Equal("allan@test.com", result.Email);
    }

    [Fact]
    public async Task Set_WithExplicitId_And_Get()
    {
        await this.store.Set("user-1", new User { Name = "Allan" });

        var result = await this.store.Get<User>("user-1");

        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
    }

    [Fact]
    public async Task Set_Upserts_ExistingDocument()
    {
        await this.store.Set("user-1", new User { Name = "Allan", Age = 30 });
        await this.store.Set("user-1", new User { Name = "Updated", Age = 31 });

        var result = await this.store.Get<User>("user-1");

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Name);
        Assert.Equal(31, result.Age);
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        var result = await this.store.Get<User>("does-not-exist");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAll_ReturnsAllDocumentsOfType()
    {
        await this.store.Set("u1", new User { Name = "Alice" });
        await this.store.Set("u2", new User { Name = "Bob" });

        var results = await this.store.GetAll<User>();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Remove_DeletesDocument()
    {
        await this.store.Set("user-1", new User { Name = "Allan" });

        var deleted = await this.store.Remove<User>("user-1");
        Assert.True(deleted);

        var result = await this.store.Get<User>("user-1");
        Assert.Null(result);
    }

    [Fact]
    public async Task Remove_NonExistent_ReturnsFalse()
    {
        var deleted = await this.store.Remove<User>("nope");
        Assert.False(deleted);
    }

    [Fact]
    public async Task Clear_RemovesAllOfType()
    {
        await this.store.Set("u1", new User { Name = "Alice" });
        await this.store.Set("u2", new User { Name = "Bob" });
        await this.store.Set("p1", new Product { Title = "Widget", Price = 9.99m });

        var cleared = await this.store.Clear<User>();
        Assert.Equal(2, cleared);

        var users = await this.store.GetAll<User>();
        Assert.Empty(users);

        // Product should still exist
        var products = await this.store.GetAll<Product>();
        Assert.Single(products);
    }

    [Fact]
    public async Task Count_ReturnsCorrectCount()
    {
        await this.store.Set("u1", new User { Name = "Alice" });
        await this.store.Set("u2", new User { Name = "Bob" });
        await this.store.Set("p1", new Product { Title = "Widget" });

        Assert.Equal(2, await this.store.Count<User>());
        Assert.Equal(1, await this.store.Count<Product>());
    }

    [Fact]
    public async Task TypeIsolation_SameIdDifferentTypes()
    {
        await this.store.Set("id-1", new User { Name = "Allan" });
        await this.store.Set("id-1", new Product { Title = "Widget" });

        var user = await this.store.Get<User>("id-1");
        var product = await this.store.Get<Product>("id-1");

        Assert.NotNull(user);
        Assert.Equal("Allan", user.Name);
        Assert.NotNull(product);
        Assert.Equal("Widget", product.Title);
    }

    [Fact]
    public async Task RunInTransaction_CommitsOnSuccess()
    {
        await this.store.RunInTransaction(async tx =>
        {
            await tx.Set("u1", new User { Name = "Alice" });
            await tx.Set("u2", new User { Name = "Bob" });
        });

        var count = await this.store.Count<User>();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RunInTransaction_RollsBackOnFailure()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await this.store.RunInTransaction(async tx =>
            {
                await tx.Set("u1", new User { Name = "Alice" });
                throw new InvalidOperationException("Simulated failure");
            });
        });

        var count = await this.store.Count<User>();
        Assert.Equal(0, count);
    }
}
