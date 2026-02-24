#pragma warning disable IL2026, IL3050 // Reflection-based serialization in tests is fine

using System.Text.Json;
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
    public async Task Query_ReturnsAllDocumentsOfType()
    {
        await this.store.Set("u1", new User { Name = "Alice" });
        await this.store.Set("u2", new User { Name = "Bob" });

        var results = await this.store.Query<User>().ToList();

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

        var users = await this.store.Query<User>().ToList();
        Assert.Empty(users);

        // Product should still exist
        var products = await this.store.Query<Product>().ToList();
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
    public async Task Upsert_InsertsNewDocument_WhenIdDoesNotExist()
    {
        await this.store.Upsert("user-1", new User { Name = "Allan", Age = 30 });

        var result = await this.store.Get<User>("user-1");

        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(30, result.Age);
    }

    [Fact]
    public async Task Upsert_MergesPatch_IntoExistingDocument()
    {
        await this.store.Set("user-1", new User { Name = "Allan", Age = 30, Email = "allan@test.com" });

        // Patch name and age; Email is null so it is excluded from the patch and preserved
        await this.store.Upsert("user-1", new User { Name = "Allan", Age = 31 });

        var result = await this.store.Get<User>("user-1");

        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(31, result.Age);
        Assert.Equal("allan@test.com", result.Email);
    }

    [Fact]
    public async Task Upsert_AotOverload_MergesPatch()
    {
        var typeInfo = Fixtures.TestJsonContext.Default.User;
        await this.store.Set("user-1", new User { Name = "Allan", Age = 30, Email = "allan@test.com" }, typeInfo);

        // Patch name and age; Email is null so it is excluded from the patch and preserved
        await this.store.Upsert("user-1", new User { Name = "Allan", Age = 31 }, typeInfo);

        var result = await this.store.Get("user-1", typeInfo);

        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(31, result.Age);
        Assert.Equal("allan@test.com", result.Email);
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

    [Fact]
    public async Task SetProperty_UpdatesScalarField()
    {
        await this.store.Set("user-1", new User { Name = "Allan", Age = 30, Email = "allan@test.com" });

        var updated = await this.store.SetProperty<User>("user-1", u => u.Age, 31);
        Assert.True(updated);

        var result = await this.store.Get<User>("user-1");
        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(31, result.Age);
        Assert.Equal("allan@test.com", result.Email);
    }

    [Fact]
    public async Task SetProperty_ReturnsFalse_WhenDocumentNotFound()
    {
        var updated = await this.store.SetProperty<User>("does-not-exist", u => u.Age, 31);
        Assert.False(updated);
    }

    [Fact]
    public async Task SetProperty_SetsNullValue()
    {
        await this.store.Set("user-1", new User { Name = "Allan", Age = 30, Email = "allan@test.com" });

        var updated = await this.store.SetProperty<User>("user-1", u => u.Email, null);
        Assert.True(updated);

        var result = await this.store.Get<User>("user-1");
        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Null(result.Email);
    }

    [Fact]
    public async Task SetProperty_AotOverload()
    {
        var typeInfo = Fixtures.TestJsonContext.Default.User;
        await this.store.Set("user-1", new User { Name = "Allan", Age = 30, Email = "allan@test.com" }, typeInfo);

        var updated = await this.store.SetProperty("user-1", (User u) => u.Age, 31, typeInfo);
        Assert.True(updated);

        var result = await this.store.Get("user-1", typeInfo);
        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(31, result.Age);
        Assert.Equal("allan@test.com", result.Email);
    }

    [Fact]
    public async Task SetProperty_NestedPath()
    {
        var order = new Order
        {
            CustomerName = "Allan",
            Status = "Pending",
            ShippingAddress = new Address { Street = "123 Main", City = "Springfield", State = "IL", Zip = "62701" }
        };
        await this.store.Set("order-1", order);

        var updated = await this.store.SetProperty<Order>("order-1", o => o.ShippingAddress.City, "Shelbyville");
        Assert.True(updated);

        var result = await this.store.Get<Order>("order-1");
        Assert.NotNull(result);
        Assert.Equal("Allan", result.CustomerName);
        Assert.Equal("Shelbyville", result.ShippingAddress.City);
        Assert.Equal("123 Main", result.ShippingAddress.Street);
    }

    [Fact]
    public async Task RemoveProperty_RemovesField()
    {
        await this.store.Set("user-1", new User { Name = "Allan", Age = 30, Email = "allan@test.com" });

        var updated = await this.store.RemoveProperty<User>("user-1", u => u.Email);
        Assert.True(updated);

        var result = await this.store.Get<User>("user-1");
        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(30, result.Age);
        Assert.Null(result.Email);
    }

    [Fact]
    public async Task RemoveProperty_ReturnsFalse_WhenDocumentNotFound()
    {
        var updated = await this.store.RemoveProperty<User>("does-not-exist", u => u.Email);
        Assert.False(updated);
    }

    [Fact]
    public async Task RemoveProperty_AotOverload()
    {
        var typeInfo = Fixtures.TestJsonContext.Default.User;
        await this.store.Set("user-1", new User { Name = "Allan", Age = 30, Email = "allan@test.com" }, typeInfo);

        var updated = await this.store.RemoveProperty("user-1", (User u) => u.Email, typeInfo);
        Assert.True(updated);

        var result = await this.store.Get("user-1", typeInfo);
        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(30, result.Age);
        Assert.Null(result.Email);
    }

    [Fact]
    public async Task RemoveProperty_NestedPath()
    {
        var order = new Order
        {
            CustomerName = "Allan",
            Status = "Pending",
            ShippingAddress = new Address { Street = "123 Main", City = "Springfield", State = "IL", Zip = "62701" }
        };
        await this.store.Set("order-1", order);

        var updated = await this.store.RemoveProperty<Order>("order-1", o => o.ShippingAddress.City);
        Assert.True(updated);

        var result = await this.store.Get<Order>("order-1");
        Assert.NotNull(result);
        Assert.Equal("Allan", result.CustomerName);
        Assert.Equal("", result.ShippingAddress.City); // default value since field was removed
        Assert.Equal("123 Main", result.ShippingAddress.Street);
    }
}

public class LoggingTests : IDisposable
{
    readonly List<string> loggedSql = [];
    readonly SqliteDocumentStore store;

    public LoggingTests()
    {
        this.store = new SqliteDocumentStore(new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            Logging = sql => this.loggedSql.Add(sql)
        });
    }

    public void Dispose() => this.store.Dispose();

    [Fact]
    public async Task Logging_CapturesSqlStatements()
    {
        var id = await this.store.Set(new User { Name = "Allan", Age = 30 });
        await this.store.Get<User>(id);
        await this.store.Count<User>();
        await this.store.Remove<User>(id);

        Assert.Contains(this.loggedSql, s => s.Contains("PRAGMA journal_mode=WAL"));
        Assert.Contains(this.loggedSql, s => s.Contains("CREATE TABLE IF NOT EXISTS"));
        Assert.Contains(this.loggedSql, s => s.Contains("INSERT INTO documents"));
        Assert.Contains(this.loggedSql, s => s.Contains("SELECT Data FROM documents"));
        Assert.Contains(this.loggedSql, s => s.Contains("SELECT COUNT(*)"));
        Assert.Contains(this.loggedSql, s => s.Contains("DELETE FROM documents"));
    }
}

public class DocumentStoreResolverTests : IDisposable
{
    readonly SqliteDocumentStore store;

    public DocumentStoreResolverTests()
    {
        var ctx = new TestJsonContext(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        this.store = new SqliteDocumentStore(new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            JsonSerializerOptions = ctx.Options
        });
    }

    public void Dispose() => this.store.Dispose();

    [Fact]
    public async Task Set_WithResolver_UsesTypeInfo()
    {
        var id = await this.store.Set(new User { Name = "Allan", Age = 30, Email = "allan@test.com" });

        var result = await this.store.Get<User>(id);

        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(30, result.Age);
        Assert.Equal("allan@test.com", result.Email);
    }

    [Fact]
    public async Task Upsert_WithResolver_UsesTypeInfo()
    {
        await this.store.Set("user-1", new User { Name = "Allan", Age = 30, Email = "allan@test.com" });

        await this.store.Upsert("user-1", new User { Name = "Allan", Age = 31 });

        var result = await this.store.Get<User>("user-1");

        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
        Assert.Equal(31, result.Age);
        Assert.Equal("allan@test.com", result.Email);
    }

    [Fact]
    public async Task Query_ReturnsAllDocumentsOfType()
    {
        await this.store.Set("u1", new User { Name = "Alice" });
        await this.store.Set("u2", new User { Name = "Bob" });

        var results = await this.store.Query<User>().ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Query_WithResolver_UsesTypeInfo()
    {
        await this.store.Set("u1", new User { Name = "Alice", Age = 25 });
        await this.store.Set("u2", new User { Name = "Bob", Age = 35 });

        var results = await this.store.Query<User>(
            "json_extract(Data, '$.age') > @minAge",
            new { minAge = 30 });

        Assert.Single(results);
        Assert.Equal("Bob", results[0].Name);
    }

    [Fact]
    public async Task NoReflectionFallback_Throws_WhenTypeNotRegistered()
    {
        using var strict = new SqliteDocumentStore(new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            UseReflectionFallback = false
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strict.Set(new User { Name = "Allan" }));

        Assert.Contains("No JsonTypeInfo registered", ex.Message);
        Assert.Contains("User", ex.Message);
    }

    [Fact]
    public async Task NoReflectionFallback_WithResolver_Works()
    {
        var ctx = new TestJsonContext(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        using var strict = new SqliteDocumentStore(new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            JsonSerializerOptions = ctx.Options,
            UseReflectionFallback = false
        });

        var id = await strict.Set(new User { Name = "Allan", Age = 30 });
        var result = await strict.Get<User>(id);

        Assert.NotNull(result);
        Assert.Equal("Allan", result.Name);
    }
}
