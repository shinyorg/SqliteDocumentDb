using Shiny.SqliteDocumentDb.Tests.Fixtures;
using Xunit;

namespace Shiny.SqliteDocumentDb.Tests;

public class TableMappingTests : IDisposable
{
    readonly SqliteDocumentStore store;

    public TableMappingTests()
    {
        this.store = new SqliteDocumentStore(new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        });
    }

    public void Dispose() => this.store.Dispose();

    [Fact]
    public async Task DefaultTableName_CanBeCustomized()
    {
        using var store = new SqliteDocumentStore(new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            TableName = "my_docs"
        });

        await store.Insert(new User { Id = "1", Name = "Alice", Age = 30, Email = "a@test.com" });
        var user = await store.Get<User>("1");
        Assert.NotNull(user);
        Assert.Equal("Alice", user.Name);
    }

    [Fact]
    public async Task MapTypeToTable_AutoDerivedName()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        };
        opts.MapTypeToTable<User>();

        using var store = new SqliteDocumentStore(opts);

        await store.Insert(new User { Id = "1", Name = "Alice", Age = 30, Email = "a@test.com" });
        var user = await store.Get<User>("1");
        Assert.NotNull(user);
        Assert.Equal("Alice", user.Name);
    }

    [Fact]
    public async Task MapTypeToTable_ExplicitName()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        };
        opts.MapTypeToTable<User>("users");

        using var store = new SqliteDocumentStore(opts);

        await store.Insert(new User { Id = "1", Name = "Bob", Age = 25, Email = "b@test.com" });
        var count = await store.Count<User>();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task MappedAndUnmappedTypes_UseCorrectTables()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        };
        opts.MapTypeToTable<User>("users");

        using var store = new SqliteDocumentStore(opts);

        await store.Insert(new User { Id = "1", Name = "Alice", Age = 30, Email = "a@test.com" });
        await store.Insert(new Product { Id = "p1", Title = "Widget", Price = 9.99m });

        // Each type is isolated
        Assert.Equal(1, await store.Count<User>());
        Assert.Equal(1, await store.Count<Product>());

        // Clearing one doesn't affect the other
        await store.Clear<User>();
        Assert.Equal(0, await store.Count<User>());
        Assert.Equal(1, await store.Count<Product>());
    }

    [Fact]
    public void MapTypeToTable_DuplicateTableName_Throws()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        };
        opts.MapTypeToTable<User>("shared");

        Assert.Throws<ArgumentException>(() => opts.MapTypeToTable<Product>("shared"));
    }

    [Fact]
    public async Task MapTypeToTable_CrudOperationsWork()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        };
        opts.MapTypeToTable<User>("users");

        using var store = new SqliteDocumentStore(opts);

        // Insert
        await store.Insert(new User { Id = "1", Name = "Alice", Age = 30, Email = "a@test.com" });

        // Get
        var user = await store.Get<User>("1");
        Assert.NotNull(user);
        Assert.Equal("Alice", user.Name);

        // Update
        user.Name = "Alice Updated";
        await store.Update(user);
        user = await store.Get<User>("1");
        Assert.Equal("Alice Updated", user!.Name);

        // Upsert
        await store.Upsert(new User { Id = "1", Name = "Alice Final", Age = 31, Email = "a@test.com" });
        user = await store.Get<User>("1");
        Assert.Equal("Alice Final", user!.Name);

        // Count
        Assert.Equal(1, await store.Count<User>());

        // Remove
        var removed = await store.Remove<User>("1");
        Assert.True(removed);
        Assert.Equal(0, await store.Count<User>());
    }

    [Fact]
    public async Task MapTypeToTable_QueryBuilderWorks()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        };
        opts.MapTypeToTable<User>("users");

        using var store = new SqliteDocumentStore(opts);

        await store.Insert(new User { Id = "1", Name = "Alice", Age = 30, Email = "a@test.com" });
        await store.Insert(new User { Id = "2", Name = "Bob", Age = 25, Email = "b@test.com" });

        var results = await store.Query<User>()
            .Where(u => u.Age > 20)
            .ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task MapTypeToTable_TransactionsWork()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        };
        opts.MapTypeToTable<User>("users");

        using var store = new SqliteDocumentStore(opts);

        await store.RunInTransaction(async tx =>
        {
            await tx.Insert(new User { Id = "1", Name = "Alice", Age = 30, Email = "a@test.com" });
            await tx.Insert(new User { Id = "2", Name = "Bob", Age = 25, Email = "b@test.com" });
        });

        Assert.Equal(2, await store.Count<User>());
    }

    [Fact]
    public void MapTypeToTable_FluentChaining()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        };

        // Should return the options for chaining
        var result = opts
            .MapTypeToTable<User>("users")
            .MapTypeToTable<Product>("products");

        Assert.Same(opts, result);
    }

    // ── Custom ID property tests ────────────────────────────────────────

    [Fact]
    public async Task MapTypeToTable_CustomIdProperty_InsertAndGet()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        }.MapTypeToTable<CustomIdModel>("custom_users", x => x.UserId);

        using var store = new SqliteDocumentStore(opts);

        await store.Insert(new CustomIdModel { UserId = "u1", Name = "Alice", Age = 30 });
        var doc = await store.Get<CustomIdModel>("u1");

        Assert.NotNull(doc);
        Assert.Equal("Alice", doc.Name);
        Assert.Equal("u1", doc.UserId);
    }

    [Fact]
    public async Task MapTypeToTable_CustomIdProperty_GuidAutoGenerated()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        }.MapTypeToTable<GuidCustomIdModel>("guid_models", x => x.Key);

        using var store = new SqliteDocumentStore(opts);

        var doc = new GuidCustomIdModel { Label = "Test" };
        await store.Insert(doc);

        Assert.NotEqual(Guid.Empty, doc.Key);

        var fetched = await store.Get<GuidCustomIdModel>(doc.Key);
        Assert.NotNull(fetched);
        Assert.Equal("Test", fetched.Label);
    }

    [Fact]
    public async Task MapTypeToTable_CustomIdProperty_Update()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        }.MapTypeToTable<CustomIdModel>("custom_users", x => x.UserId);

        using var store = new SqliteDocumentStore(opts);

        await store.Insert(new CustomIdModel { UserId = "u1", Name = "Alice", Age = 30 });

        var doc = await store.Get<CustomIdModel>("u1");
        doc!.Name = "Alice Updated";
        await store.Update(doc);

        var updated = await store.Get<CustomIdModel>("u1");
        Assert.Equal("Alice Updated", updated!.Name);
    }

    [Fact]
    public async Task MapTypeToTable_CustomIdProperty_Remove()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        }.MapTypeToTable<CustomIdModel>("custom_users", x => x.UserId);

        using var store = new SqliteDocumentStore(opts);

        await store.Insert(new CustomIdModel { UserId = "u1", Name = "Alice", Age = 30 });
        Assert.Equal(1, await store.Count<CustomIdModel>());

        var removed = await store.Remove<CustomIdModel>("u1");
        Assert.True(removed);
        Assert.Equal(0, await store.Count<CustomIdModel>());
    }

    [Fact]
    public async Task MapTypeToTable_CustomIdProperty_QueryBuilder()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        }.MapTypeToTable<CustomIdModel>("custom_users", x => x.UserId);

        using var store = new SqliteDocumentStore(opts);

        await store.Insert(new CustomIdModel { UserId = "u1", Name = "Alice", Age = 30 });
        await store.Insert(new CustomIdModel { UserId = "u2", Name = "Bob", Age = 25 });

        var results = await store.Query<CustomIdModel>()
            .Where(u => u.Age > 20)
            .OrderBy(u => u.Name)
            .ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("Alice", results[0].Name);
    }

    [Fact]
    public async Task MapTypeToTable_CustomIdProperty_Transactions()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        }.MapTypeToTable<CustomIdModel>("custom_users", x => x.UserId);

        using var store = new SqliteDocumentStore(opts);

        await store.RunInTransaction(async tx =>
        {
            await tx.Insert(new CustomIdModel { UserId = "u1", Name = "Alice", Age = 30 });
            await tx.Insert(new CustomIdModel { UserId = "u2", Name = "Bob", Age = 25 });
        });

        Assert.Equal(2, await store.Count<CustomIdModel>());
    }

    [Fact]
    public async Task MapTypeToTable_CustomIdProperty_AutoDerivedTableName()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        }.MapTypeToTable<CustomIdModel>(x => x.UserId);

        using var store = new SqliteDocumentStore(opts);

        await store.Insert(new CustomIdModel { UserId = "u1", Name = "Alice", Age = 30 });
        var doc = await store.Get<CustomIdModel>("u1");
        Assert.NotNull(doc);
        Assert.Equal("Alice", doc.Name);
    }

    [Fact]
    public void MapTypeToTable_CustomIdProperty_FluentChaining()
    {
        var opts = new DocumentStoreOptions
        {
            ConnectionString = "Data Source=:memory:"
        };

        var result = opts
            .MapTypeToTable<CustomIdModel>("custom_users", x => x.UserId)
            .MapTypeToTable<GuidCustomIdModel>("guid_models", x => x.Key);

        Assert.Same(opts, result);
    }
}
