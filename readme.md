# Shiny.SqliteDocumentDb

[![NuGet](https://img.shields.io/nuget/v/Shiny.SqliteDocumentDb.svg)](https://www.nuget.org/packages/Shiny.SqliteDocumentDb/)

A lightweight SQLite-based document store for .NET that turns SQLite into a schema-free JSON document database with LINQ querying and full AOT/trimming support.

**[Documentation](https://shinylib.net/sqlite-docdb)**

## Features

- **Zero schema, zero migrations** — store entire object graphs (nested objects, child collections) as JSON documents. No `CREATE TABLE`, no `ALTER TABLE`, no JOINs.
- **LINQ expression queries** — write `o => o.ShippingAddress.City == "Portland"` and it translates to `json_extract` SQL automatically. Supports nested properties, `Any()`, `Count()`, string methods, null checks, and captured variables.
- **`IAsyncEnumerable<T>` streaming** — yield results one-at-a-time with `GetAllStream` and `QueryStream` instead of buffering into a list. Eliminates Gen1 GC pressure at scale with comparable throughput.
- **Expression-based JSON indexes** — `store.CreateIndexAsync<User>(u => u.Name, ctx.User)` creates a partial `json_extract` index. Up to **30x faster** queries on indexed properties.
- **SQL-level projections** — project into DTOs with `json_object` at the database level. No full document deserialization needed.
- **Full AOT/trimming support** — every API has a `JsonTypeInfo<T>` overload for source-generated JSON serialization. No reflection required. Configure a `JsonSerializerContext` once and all overloads auto-resolve type info — no per-call `JsonTypeInfo<T>` needed. Set `UseReflectionFallback = false` to catch missing type registrations with clear exceptions instead of opaque AOT failures.
- **10-30x faster nested inserts** vs sqlite-net — one write per document vs multiple table inserts with foreign keys. 2-10x faster reads on nested data.
- **JSON Merge Patch (Upsert)** — `store.Upsert("id", patch)` deep-merges a partial object into an existing document using SQLite's `json_patch()` (RFC 7396). Only patched fields are overwritten; unset nullable fields are preserved.
- **Surgical field updates** — `store.SetProperty<User>("id", u => u.Age, 31)` updates a single JSON field via `json_set()` without deserializing the document. `store.RemoveProperty<User>("id", u => u.Email)` strips a field via `json_remove()`. Both support nested paths like `o => o.ShippingAddress.City`.
- **Transactions** — `store.RunInTransaction(async tx => { ... })` with automatic commit/rollback.

## Comparison with alternatives

| | Shiny.SqliteDocumentDb | Microsoft.Data.Sqlite (raw ADO.NET) | sqlite-net-pcl |
|---|---|---|---|
| **Schema management** | Zero — just store objects | You write every `CREATE TABLE`, `ALTER TABLE`, migration | Auto-creates flat tables from POCOs |
| **Nested objects & child collections** | Stored and queried as a single JSON document | Must design normalized tables, write JOINs, manage foreign keys | No support — flat columns only, child collections require separate tables + manual joins |
| **LINQ queries on nested data** | `store.Query(o => o.Lines.Any(l => l.Price > 10))` | Hand-written `json_extract` SQL | Not possible on nested data |
| **AOT / trimming** | First-class `JsonTypeInfo<T>` overloads on every API | Manual — you control all SQL | Relies on reflection; no AOT support |
| **Migrations** | Not needed — schema-free JSON | You own every migration | You own every migration |
| **Projections** | SQL-level `json_object` projections | Manual SQL | Not available |
| **Transactions** | `store.RunInTransaction(async tx => ...)` | Manual `BeginTransaction` + `Commit`/`Rollback` | `RunInTransactionAsync` available |
| **JSON property indexes** | `store.CreateIndexAsync<User>(u => u.Name, ctx.User)` — LINQ expression indexes on `json_extract` | Manual `CREATE INDEX` on `json_extract` | Column indexes only |
| **Best fit** | Object graphs, nested data, rapid prototyping, settings stores, caches | Full SQL control, complex reporting queries, performance-critical bulk ops | Simple flat-table CRUD |

**In short:** If your data has nested objects or child collections (orders with line items, users with addresses, configs with nested sections), this library lets you store and query the entire object graph with a single call — no table design, no JOINs, no migrations. For flat, single-table CRUD on simple POCOs, sqlite-net-pcl or raw ADO.NET may be simpler.

## Replacing EF Core on .NET MAUI

Entity Framework Core is a natural choice for server-side .NET, but it becomes a liability on .NET MAUI platforms (iOS, Android, Mac Catalyst). This library is purpose-built for the constraints mobile and desktop apps actually face.

### Why EF Core is a poor fit for MAUI

- **No AOT support.** EF Core relies heavily on runtime reflection and dynamic code generation for change tracking, query compilation, and model building. It carries `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]` attributes throughout its public API. On iOS, where Apple prohibits JIT compilation entirely, this is a non-starter for fully native AOT deployments.
- **Migrations are friction, not value.** On a server you run migrations against a shared database with a known lifecycle. On a mobile device, the database ships inside the app or is created on first launch. EF Core's migration pipeline (`Add-Migration`, `Update-Database`, `__EFMigrationsHistory`) adds complexity with no real benefit — there is no DBA, no staging environment, no rollback plan. A schema-free document store eliminates migrations entirely.
- **Heavy dependency graph.** EF Core pulls in `Microsoft.EntityFrameworkCore`, its SQLite provider, design-time packages, and their transitive dependencies. This increases app bundle size — a real concern when app stores enforce download size limits and users expect fast installs.
- **Relational overhead for non-relational data.** Mobile apps typically store user preferences, cached API responses, offline data queues, and local state. This data is naturally document-shaped (nested objects, variable structure). Forcing it into normalized tables with foreign keys and JOINs adds accidental complexity.

### Why this library fits

| Concern | EF Core | Shiny.SqliteDocumentDb |
|---|---|---|
| **AOT / trimming** | Reflection-heavy; no AOT support | Every API has a `JsonTypeInfo<T>` overload; zero reflection required |
| **Migrations** | Required for every schema change | Not needed — schema-free JSON storage |
| **Nested objects** | Normalized tables, foreign keys, JOINs | Single document, single write, single read |
| **App bundle size** | Large dependency tree | Single dependency on `Microsoft.Data.Sqlite` |
| **Startup time** | DbContext model building, migration checks | Open connection and go |
| **Offline / sync patterns** | Complex change tracking | Store and retrieve document snapshots directly |

### AOT and trimming on mobile platforms

Ahead-of-Time compilation is not optional on Apple platforms — iOS, iPadOS, tvOS, and Mac Catalyst all prohibit JIT at the OS level. Android does not prohibit JIT, but AOT deployment (`PublishAot` or `AndroidEnableProfiledAot`) delivers measurably faster startup and lower memory usage, both of which directly affect user experience.

The .NET trimmer removes unreferenced code to shrink the app binary. Libraries that depend on reflection break under trimming because the trimmer cannot statically determine which types and members are accessed at runtime. This forces developers to either disable trimming (larger binaries) or maintain complex trimmer XML files.

This library avoids both problems:

- **Source-generated JSON serialization.** The `JsonSerializerContext` pattern generates serialization code at compile time. The trimmer can see every type that will be serialized, and the AOT compiler can compile every code path ahead of time.
- **No runtime expression compilation.** LINQ expressions are translated to SQL strings by a visitor — no `Expression.Compile()`, no `Reflection.Emit`, no dynamic delegates.
- **No model building.** There is no equivalent of EF Core's `OnModelCreating` that discovers entity types and relationships through reflection at startup.

If you are building a .NET MAUI app and need local data persistence, this library gives you a queryable document store that works under full AOT and trimming without compromise.

## Benchmarks

Measured with [BenchmarkDotNet](https://benchmarkdotnet.org/) v0.14.0 on Apple M2, .NET 10.0.3, macOS. Full source in [`benchmarks/`](benchmarks/).

### Flat POCO (single table)

#### Insert

| Method | Count | Mean |
|---|---|---|
| DocumentStore Insert | 10 | 652 us |
| sqlite-net Insert | 10 | 2,256 us |
| DocumentStore Insert | 100 | 4.6 ms |
| sqlite-net Insert | 100 | 23.9 ms |
| DocumentStore Insert | 1000 | 50.0 ms |
| sqlite-net Insert | 1000 | 533 ms |

#### Get by ID

| Method | Mean | Allocated |
|---|---|---|
| DocumentStore GetById | 3.70 us | 1.8 KB |
| sqlite-net GetById | 16.20 us | 3.73 KB |

#### Get all

| Method | Count | Mean | Allocated |
|---|---|---|---|
| DocumentStore GetAll | 100 | 40.5 us | 29.4 KB |
| sqlite-net GetAll | 100 | 73.1 us | 28.4 KB |
| DocumentStore GetAll | 1000 | 377 us | 283 KB |
| sqlite-net GetAll | 1000 | 457 us | 246 KB |

#### Query (filter by name, 1000 records)

| Method | Mean | Allocated |
|---|---|---|
| DocumentStore Query | 243 us | 4.1 KB |
| sqlite-net Query | 59 us | 5.3 KB |

> sqlite-net is faster for simple indexed-column queries because it queries column values directly, while the document store must use `json_extract`. The document store shines with nested data (see below).

### Nested objects with child collections (Order + Address + OrderLines + Tags)

This is where the document store architecture pays off. sqlite-net requires 3 tables, 6 inserts per order, and 3 queries per read with manual rehydration.

#### Insert (nested)

| Method | Count | Mean |
|---|---|---|
| DocumentStore Insert (nested) | 10 | 1.3 ms |
| sqlite-net Insert (3 tables) | 10 | 15.2 ms |
| DocumentStore Insert (nested) | 100 | 5.0 ms |
| sqlite-net Insert (3 tables) | 100 | 170 ms |
| DocumentStore Insert (nested) | 1000 | 51.7 ms |
| sqlite-net Insert (3 tables) | 1000 | 1,638 ms |

#### Get by ID (nested)

| Method | Mean | Allocated |
|---|---|---|
| DocumentStore GetById (nested) | 4.8 us | 3.7 KB |
| sqlite-net GetById (3 queries) | 48.6 us | 16.1 KB |

#### Get all (nested)

| Method | Count | Mean | Allocated |
|---|---|---|---|
| DocumentStore GetAll (nested) | 100 | 141 us | 218 KB |
| sqlite-net GetAll (3 tables + rehydrate) | 100 | 329 us | 159 KB |
| DocumentStore GetAll (nested) | 1000 | 1,530 us | 2,165 KB |
| sqlite-net GetAll (3 tables + rehydrate) | 1000 | 2,700 us | 1,438 KB |

#### Query (nested, filter by status)

| Method | Mean | Allocated |
|---|---|---|
| DocumentStore Query (nested, by status) | 1.38 ms | 1,086 KB |
| sqlite-net Query (3 tables + rehydrate) | 2.23 ms | 1,013 KB |

> For nested data, the document store is **10-30x faster on inserts** and **2-10x faster on reads** because it stores/retrieves the entire object graph in a single operation vs. multiple table writes and JOINs.

### Index impact

JSON property indexes (`CreateIndexAsync`) dramatically speed up equality queries by letting SQLite use a B-tree lookup instead of scanning every row with `json_extract`.

#### Flat POCO query (filter by name, 1000 records)

| Method | Mean | Allocated |
|---|---|---|
| Query without index | 274 us | 4.2 KB |
| Query with index | 9.2 us | 4.1 KB |

> **~30x faster** — the indexed query resolves in microseconds because SQLite uses the partial index directly.

#### Nested query (filter by ShippingAddress.City, 1000 records, ~200 matches)

| Method | Mean | Allocated |
|---|---|---|
| Nested query without index | 971 us | 435 KB |
| Nested query with index | 310 us | 435 KB |

> **~3x faster** — the index eliminates the full table scan, but read + deserialize time for ~200 matching documents dominates. Indexes give the biggest wins on selective queries that return few results.

### Streaming (IAsyncEnumerable) vs buffered

Streaming yields results one-at-a-time without building an intermediate `List<T>`. Throughput is comparable; the benefit is reduced peak memory and eliminating Gen1 GC pressure at larger scales.

#### Flat POCO

| Method | Count | Mean | Gen1 | Allocated |
|---|---|---|---|---|
| GetAll (buffered) | 100 | 44.1 us | 0.18 | 29.4 KB |
| GetAllStream (IAsyncEnumerable) | 100 | 42.7 us | — | 27.3 KB |
| GetAll (buffered) | 1000 | 384 us | 12.2 | 283 KB |
| GetAllStream (IAsyncEnumerable) | 1000 | 393 us | — | 266 KB |

#### Nested objects

| Method | Count | Mean | Gen1 | Allocated |
|---|---|---|---|---|
| GetAll nested (buffered) | 100 | 154 us | 6.1 | 218 KB |
| GetAllStream nested (IAsyncEnumerable) | 100 | 156 us | — | 216 KB |
| GetAll nested (buffered) | 1000 | 1,541 us | 130.9 | 2,165 KB |
| GetAllStream nested (IAsyncEnumerable) | 1000 | 1,512 us | 2.0 | 2,149 KB |

#### Nested query (filter by status, ~500 matches from 1000)

| Method | Mean | Gen1 | Allocated |
|---|---|---|---|
| Query nested (buffered) | 1.49 ms | 58.6 | 1.06 MB |
| QueryStream nested (IAsyncEnumerable) | 1.43 ms | — | 1.05 MB |

> Streaming eliminates Gen1 GC collections entirely at scale. Throughput is within ~2% of buffered. Use streaming when you process results incrementally rather than needing the full list upfront.

## Installation

```bash
dotnet add package Shiny.SqliteDocumentDb
```

## Setup

### Direct instantiation

```csharp
var store = new SqliteDocumentStore(new DocumentStoreOptions
{
    ConnectionString = "Data Source=mydata.db"
});
```

### Options reference

| Property | Type | Default | Description |
|---|---|---|---|
| `ConnectionString` | `string` | (required) | SQLite connection string |
| `TypeNameResolution` | `TypeNameResolution` | `ShortName` | How type names are stored — `ShortName` (e.g. `User`) or `FullName` (e.g. `MyApp.Models.User`) |
| `JsonSerializerOptions` | `JsonSerializerOptions?` | `null` | JSON serialization settings. When a `JsonSerializerContext` is attached as the `TypeInfoResolver`, overloads without `JsonTypeInfo<T>` auto-resolve type info from the context |
| `UseReflectionFallback` | `bool` | `true` | When `false`, throws `InvalidOperationException` if a type can't be resolved from the configured `TypeInfoResolver` instead of falling back to reflection. Recommended for AOT deployments |

### Dependency injection

```csharp
services.AddSqliteDocumentStore("Data Source=mydata.db");

// or with full options
services.AddSqliteDocumentStore(opts =>
{
    opts.ConnectionString = "Data Source=mydata.db";
    opts.TypeNameResolution = TypeNameResolution.FullName;
    opts.JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
});

// AOT-safe — attach a JsonSerializerContext so all overloads auto-resolve type info
var ctx = new AppJsonContext(new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});
services.AddSqliteDocumentStore(opts =>
{
    opts.ConnectionString = "Data Source=mydata.db";
    opts.JsonSerializerOptions = ctx.Options;
    opts.UseReflectionFallback = false; // throw instead of using reflection for unregistered types
});
```

## AOT Setup

For AOT/trimming compatibility, create a source-generated JSON context:

```csharp
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(Address))]
[JsonSerializable(typeof(OrderLine))]
public partial class AppJsonContext : JsonSerializerContext;
```

Then create an instance with your desired options:

```csharp
var ctx = new AppJsonContext(new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});
```

Pass `ctx.Options` to `DocumentStoreOptions.JsonSerializerOptions` so that the expression visitor and serializer share the same configuration.

### Using the resolver for cleaner API calls

When a `JsonSerializerContext` is attached to `DocumentStoreOptions.JsonSerializerOptions`, the reflection-marked overloads (without `JsonTypeInfo<T>`) automatically resolve type info from the configured resolver. This means you can configure the context once and skip passing `JsonTypeInfo<T>` on every call — while retaining full AOT safety.

#### Setup

```csharp
var ctx = new AppJsonContext(new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});

var store = new SqliteDocumentStore(new DocumentStoreOptions
{
    ConnectionString = "Data Source=mydata.db",
    JsonSerializerOptions = ctx.Options,
    UseReflectionFallback = false // recommended for AOT
});
```

#### Multiple JSON contexts

If your types are spread across multiple `JsonSerializerContext` classes, use `TypeInfoResolverChain` to combine them. The chain is tried in order — the first context that knows about the requested type wins.

```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
options.TypeInfoResolverChain.Add(UserJsonContext.Default);
options.TypeInfoResolverChain.Add(OrderJsonContext.Default);

var store = new SqliteDocumentStore(new DocumentStoreOptions
{
    ConnectionString = "Data Source=mydata.db",
    JsonSerializerOptions = options,
    UseReflectionFallback = false
});
```

#### Before vs after

| Without resolver (explicit `JsonTypeInfo<T>`) | With resolver (auto-resolved) |
|---|---|
| `store.Set(user, ctx.User)` | `store.Set(user)` |
| `store.Set("id", user, ctx.User)` | `store.Set("id", user)` |
| `store.Get<User>("id", ctx.User)` | `store.Get<User>("id")` |
| `store.GetAll<User>(ctx.User)` | `store.GetAll<User>()` |
| `store.Upsert("id", patch, ctx.User)` | `store.Upsert("id", patch)` |
| `store.SetProperty("id", (User u) => u.Age, 31, ctx.User)` | `store.SetProperty<User>("id", u => u.Age, 31)` |
| `store.RemoveProperty("id", (User u) => u.Email, ctx.User)` | `store.RemoveProperty<User>("id", u => u.Email)` |
| `store.Query<User>(sql, ctx.User, parms)` | `store.Query<User>(sql, parms)` |
| `store.GetAllStream<User>(ctx.User)` | `store.GetAllStream<User>()` |
| `store.QueryStream<User>(sql, ctx.User, parms)` | `store.QueryStream<User>(sql, parms)` |

#### Example

```csharp
// All of these are AOT-safe when ctx.Options is configured
var id = await store.Set(new User { Name = "Alice", Age = 25 });
var user = await store.Get<User>(id);
var all = await store.GetAll<User>();
await store.Upsert("user-1", new User { Name = "Alice", Age = 30 });

var results = await store.Query<User>(
    "json_extract(Data, '$.age') > @minAge",
    new { minAge = 30 });

await foreach (var u in store.GetAllStream<User>())
    Console.WriteLine(u.Name);
```

#### How it works

Each reflection-marked overload checks `JsonSerializerOptions.TryGetTypeInfo(typeof(T))` before falling back to reflection. If the resolver returns a `JsonTypeInfo<T>`, the call is delegated to the corresponding AOT overload internally. The `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` attributes remain on these methods since they can still use reflection when no resolver is configured.

#### Reflection fallback behavior

By default (`UseReflectionFallback = true`), if no `TypeInfoResolver` is configured or the type isn't registered in the context, these methods fall back to reflection-based serialization. Existing code without a `JsonSerializerContext` continues to work unchanged.

**For AOT deployments, set `UseReflectionFallback = false`.** Reflection-based serialization produces hard-to-diagnose errors under trimming and AOT. With this flag disabled, you get a clear `InvalidOperationException` at the point of use:

```
InvalidOperationException: No JsonTypeInfo registered for type 'MyApp.UnregisteredType'.
Register it in your JsonSerializerContext or pass a JsonTypeInfo<UnregisteredType> explicitly.
```

This tells you exactly which type is missing and what to do about it. Every type must either be registered in your `JsonSerializerContext` via `[JsonSerializable(typeof(T))]` or passed with an explicit `JsonTypeInfo<T>` parameter.

> **Note:** Expression-based queries (`store.Query(u => u.Age > 30, ctx.User)`) and projections always require explicit `JsonTypeInfo<T>` because the LINQ expression visitor needs type metadata to resolve JSON property names. Auto-resolution applies to the 8 methods in the table above.

## Basic CRUD Operations

### Store a document (auto-generated ID)

```csharp
var id = await store.Set(new User { Name = "Alice", Age = 25 }, ctx.User);
```

### Store a document (explicit ID)

```csharp
await store.Set("user-1", new User { Name = "Alice", Age = 25 }, ctx.User);
```

### Upsert with JSON Merge Patch

`Upsert` uses SQLite's `json_patch()` (RFC 7396 JSON Merge Patch) to deep-merge a partial patch into an existing document. If the document doesn't exist, it is inserted as-is. Unlike `Set`, which replaces the entire document, `Upsert` only overwrites the fields present in the patch.

```csharp
// Insert a full document
await store.Set("user-1", new User { Name = "Alice", Age = 25, Email = "alice@test.com" }, ctx.User);

// Merge patch — only update Name and Age, preserve Email
await store.Upsert("user-1", new User { Name = "Alice", Age = 30 }, ctx.User);

var user = await store.Get<User>("user-1", ctx.User);
// user.Name == "Alice", user.Age == 30, user.Email == "alice@test.com" (preserved)
```

**How it works:**
- On **insert** (new ID): the patch is stored as the full document.
- On **conflict** (existing ID): `json_patch(existing, patch)` deep-merges the patch into the stored JSON. Objects are recursively merged; scalars and arrays are replaced.
- **Null properties are excluded** from the patch automatically. In C#, unset nullable properties (e.g. `string? Email`) serialize as `null`, which would remove the key under RFC 7396. The library strips these so that unset fields are preserved rather than deleted.

> **Tip:** For true partial updates, use nullable properties in your patch type so that unset fields are `null` and excluded from the merge. Non-nullable properties with default initializers (e.g. `string Name = ""`) will always be included in the patch.

### Update a single property (SetProperty)

`SetProperty` updates a single scalar field in-place using SQLite's `json_set()` — no deserialization, no full document replacement. Returns `true` if the document was found and updated, `false` if not found.

```csharp
// Update a scalar field
await store.SetProperty<User>("user-1", u => u.Age, 31, ctx.User);

// Update a string field
await store.SetProperty<User>("user-1", u => u.Email, "newemail@test.com", ctx.User);

// Set a field to null
await store.SetProperty<User>("user-1", u => u.Email, null, ctx.User);

// Nested property — update a city within a shipping address
await store.SetProperty<Order>("order-1", o => o.ShippingAddress.City, "Portland", ctx.Order);

// Check if the document existed
bool updated = await store.SetProperty<User>("user-1", u => u.Age, 31, ctx.User);
if (!updated)
    Console.WriteLine("Document not found");
```

**How it works:** The expression `u => u.Age` is resolved to the JSON path `$.age` (respecting `[JsonPropertyName]` attributes and naming policies). The SQL executed is:

```sql
UPDATE documents
SET Data = json_set(Data, '$.age', json('31')), UpdatedAt = @now
WHERE Id = @id AND TypeName = @typeName;
```

**Supported value types:** `SetProperty` is designed for scalar values — `string`, `int`, `long`, `double`, `float`, `decimal`, `bool`, and `null`. It does not support setting collection or complex object values. To replace a nested object or array, use `Set` (full replacement) or `Upsert` (merge patch).

### Remove a single property (RemoveProperty)

`RemoveProperty` strips a field from the stored JSON using SQLite's `json_remove()`. Returns `true` if the document was found and updated, `false` if not found. When the document is later deserialized, the removed field will have its C# default value.

```csharp
// Remove a nullable field
await store.RemoveProperty<User>("user-1", u => u.Email, ctx.User);

// Remove a nested property
await store.RemoveProperty<Order>("order-1", o => o.ShippingAddress.City, ctx.Order);

// Remove a collection property (removes the entire array from the JSON)
await store.RemoveProperty<Order>("order-1", o => o.Tags, ctx.Order);

// Check if the document existed
bool updated = await store.RemoveProperty<User>("user-1", u => u.Email, ctx.User);
```

**How it works:** The SQL executed is:

```sql
UPDATE documents
SET Data = json_remove(Data, '$.email'), UpdatedAt = @now
WHERE Id = @id AND TypeName = @typeName;
```

Unlike `SetProperty`, `RemoveProperty` works on any property type — scalar, nested object, or collection — because it simply removes the key from the JSON regardless of the value's shape.

### SetProperty vs RemoveProperty vs Upsert vs Set

| Operation | Use when | Scope | Collections |
|---|---|---|---|
| `SetProperty` | Changing one scalar field | Single field, in-place `json_set` | Scalar values only |
| `RemoveProperty` | Stripping a field from the document | Single field, in-place `json_remove` | Works on any property type |
| `Upsert` | Patching multiple fields at once | Deep merge via `json_patch` | Replaces arrays entirely (RFC 7396) |
| `Set` | Replacing the entire document | Full replacement | Full control |

### Get a document by ID

```csharp
var user = await store.Get<User>("user-1", ctx.User);
```

### Get all documents of a type

```csharp
var users = await store.GetAll<User>(ctx.User);
```

### Remove a document

```csharp
bool deleted = await store.Remove<User>("user-1");
```

### Remove documents matching a predicate (AOT-safe)

```csharp
int deletedCount = await store.Remove<User>(u => u.Age < 18, ctx.User);
```

See [Removing with expressions](#removing-with-expressions) for more examples.

### Clear all documents of a type

```csharp
int deletedCount = await store.Clear<User>();
```

## Querying

### Expression-based queries (AOT-safe)

The preferred way to query. Property names are resolved from `JsonTypeInfo` metadata, so `[JsonPropertyName]` attributes and naming policies are respected automatically.

#### Equality and comparisons

```csharp
var results = await store.Query<User>(u => u.Name == "Alice", ctx.User);
var older = await store.Query<User>(u => u.Age > 30, ctx.User);
var young = await store.Query<User>(u => u.Age <= 25, ctx.User);
```

#### Logical operators

```csharp
var results = await store.Query<User>(u => u.Age == 25 && u.Name == "Alice", ctx.User);
var results = await store.Query<User>(u => u.Name == "Alice" || u.Name == "Bob", ctx.User);
var results = await store.Query<User>(u => !(u.Name == "Alice"), ctx.User);
```

#### Null checks

```csharp
var noEmail = await store.Query<User>(u => u.Email == null, ctx.User);
var hasEmail = await store.Query<User>(u => u.Email != null, ctx.User);
```

#### String methods

```csharp
var results = await store.Query<User>(u => u.Name.Contains("li"), ctx.User);
var results = await store.Query<User>(u => u.Name.StartsWith("Al"), ctx.User);
var results = await store.Query<User>(u => u.Name.EndsWith("ob"), ctx.User);
```

#### Nested object properties

```csharp
var results = await store.Query<Order>(o => o.ShippingAddress.City == "Portland", ctx.Order);
```

#### Collection queries with Any()

```csharp
// Object collection — filter by child property
var results = await store.Query<Order>(
    o => o.Lines.Any(l => l.ProductName == "Widget"), ctx.Order);

// Primitive collection — filter by value
var results = await store.Query<Order>(
    o => o.Tags.Any(t => t == "priority"), ctx.Order);

// Check if a collection has any elements
var results = await store.Query<Order>(o => o.Tags.Any(), ctx.Order);
```

#### Collection queries with Count()

```csharp
// Count elements (no predicate)
var results = await store.Query<Order>(o => o.Lines.Count() > 1, ctx.Order);

// Count matching elements (with predicate)
var results = await store.Query<Order>(
    o => o.Lines.Count(l => l.Quantity >= 3) >= 1, ctx.Order);
```

#### DateTime and DateTimeOffset queries

DateTime and DateTimeOffset values are formatted to match System.Text.Json's default ISO 8601 output, so comparisons work correctly with stored JSON.

```csharp
var cutoff = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
var upcoming = await store.Query<Event>(e => e.StartDate > cutoff, ctx.Event);

var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
var end = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
var inRange = await store.Query<Event>(
    e => e.CreatedAt >= start && e.CreatedAt < end, ctx.Event);
```

#### Captured variables

```csharp
var targetName = "Alice";
var results = await store.Query<User>(u => u.Name == targetName, ctx.User);
```

### Counting with expressions

```csharp
var count = await store.Count<User>(u => u.Age == 25, ctx.User);

// With collection predicates
var count = await store.Count<Order>(
    o => o.Lines.Any(l => l.ProductName == "Gadget"), ctx.Order);

var count = await store.Count<Order>(o => o.Lines.Count() > 1, ctx.Order);
```

### Removing with expressions

Delete documents matching a predicate in a single SQL DELETE — no need to query first.

```csharp
// Simple predicate — returns number of deleted rows
int deleted = await store.Remove<User>(u => u.Age < 18, ctx.User);

// Complex predicates with && and ||
int deleted = await store.Remove<Order>(
    o => o.ShippingAddress.City == "Portland" || o.Status == "Cancelled", ctx.Order);

// Nested properties
int deleted = await store.Remove<Order>(
    o => o.ShippingAddress.State == "OR", ctx.Order);

// Captured variables
var cutoffAge = 65;
int deleted = await store.Remove<User>(u => u.Age > cutoffAge, ctx.User);
```

### Projections

Project query results into a different shape using a selector expression. Only the selected properties are extracted at the SQL level via `json_object` — no full document deserialization needed.

#### Flat projection

```csharp
var results = await store.Query<User, UserSummary>(
    u => u.Age == 25,
    u => new UserSummary { Name = u.Name, Email = u.Email },
    ctx.User,
    ctx.UserSummary);
```

#### Nested source properties

```csharp
var results = await store.Query<Order, OrderSummary>(
    o => o.Status == "Shipped",
    o => new OrderSummary { Customer = o.CustomerName, City = o.ShippingAddress.City },
    ctx.Order,
    ctx.OrderSummary);
```

#### GetAll with projection

```csharp
var results = await store.GetAll<Order, OrderDetail>(
    o => new OrderDetail { Customer = o.CustomerName, LineCount = o.Lines.Count() },
    ctx.Order,
    ctx.OrderDetail);
```

#### Collection methods in projections

Use `Count()`, `Count(predicate)`, `Any()`, and `Any(predicate)` inside projection selectors:

```csharp
// Count() — total number of elements
o => new OrderDetail { Customer = o.CustomerName, LineCount = o.Lines.Count() }
// SQL: json_array_length(Data, '$.lines')

// Count(predicate) — filtered count
o => new OrderDetail { Customer = o.CustomerName, GadgetCount = o.Lines.Count(l => l.ProductName == "Gadget") }
// SQL: (SELECT COUNT(*) FROM json_each(Data, '$.lines') WHERE json_extract(value, '$.productName') = @pp0)

// Any() — has any elements
o => new OrderDetail { Customer = o.CustomerName, HasLines = o.Lines.Any() }
// SQL: CASE WHEN json_array_length(Data, '$.lines') > 0 THEN json('true') ELSE json('false') END

// Any(predicate) — any element matches
o => new OrderDetail { Customer = o.CustomerName, HasPriority = o.Tags.Any(t => t == "priority") }
// SQL: CASE WHEN EXISTS (SELECT 1 FROM json_each(Data, '$.tags') WHERE value = @pp0) THEN json('true') ELSE json('false') END
```

Inner predicates support the same operators as WHERE clause expressions: comparisons, logical operators, null checks, string methods (`Contains`, `StartsWith`, `EndsWith`), and captured variables.

### Streaming Queries

All query methods that return `IReadOnlyList<T>` have streaming counterparts that return `IAsyncEnumerable<T>`, yielding results one-at-a-time without buffering the entire result set into memory.

#### GetAllStream

```csharp
await foreach (var user in store.GetAllStream<User>(ctx.User))
{
    Console.WriteLine(user.Name);
}
```

#### GetAllStream with projection

```csharp
await foreach (var summary in store.GetAllStream<Order, OrderSummary>(
    o => new OrderSummary { Customer = o.CustomerName, City = o.ShippingAddress.City },
    ctx.Order,
    ctx.OrderSummary))
{
    Console.WriteLine($"{summary.Customer} in {summary.City}");
}
```

#### QueryStream with expression

```csharp
await foreach (var user in store.QueryStream<User>(u => u.Age > 30, ctx.User))
{
    Console.WriteLine(user.Name);
}
```

#### QueryStream with raw SQL

```csharp
await foreach (var user in store.QueryStream<User>(
    "json_extract(Data, '$.name') = @name",
    ctx.User,
    new { name = "Alice" }))
{
    Console.WriteLine(user.Name);
}
```

#### QueryStream with projection

```csharp
await foreach (var summary in store.QueryStream<Order, OrderSummary>(
    o => o.Status == "Shipped",
    o => new OrderSummary { Customer = o.CustomerName, City = o.ShippingAddress.City },
    ctx.Order,
    ctx.OrderSummary))
{
    Console.WriteLine(summary.Customer);
}
```

> **Note:** The streaming methods hold the internal semaphore (or database reader) for the duration of enumeration. Consume the results promptly and avoid interleaving other store operations within the same `await foreach` loop.

### Raw SQL queries

For advanced queries not covered by expressions, use raw SQL with `json_extract`:

```csharp
var results = await store.Query<User>(
    "json_extract(Data, '$.name') = @name",
    ctx.User,
    new { name = "Alice" });

// With dictionary parameters (AOT-safe)
var parms = new Dictionary<string, object?> { ["name"] = "Alice" };
var results = await store.Query<User>(
    "json_extract(Data, '$.name') = @name",
    ctx.User,
    parms);

// Count with raw SQL
var count = await store.Count<User>(
    "json_extract(Data, '$.age') > @minAge",
    new { minAge = 30 });
```

## Transactions

```csharp
await store.RunInTransaction(async tx =>
{
    await tx.Set("u1", new User { Name = "Alice", Age = 25 }, ctx.User);
    await tx.Set("u2", new User { Name = "Bob", Age = 30 }, ctx.User);
    // Commits on success, rolls back on exception
});
```

## Index Management

For frequently queried JSON properties, you can create expression indexes on `json_extract` to speed up lookups. These methods are on `SqliteDocumentStore` directly (not on `IDocumentStore`) since index management is DDL, not document CRUD.

### Create an index on a property

```csharp
await store.CreateIndexAsync<User>(u => u.Name, ctx.User);
```

This generates a partial index scoped to the document type:

```sql
CREATE INDEX IF NOT EXISTS idx_json_User_name
ON documents (json_extract(Data, '$.name'))
WHERE TypeName = 'User';
```

### Nested properties

```csharp
await store.CreateIndexAsync<Order>(o => o.ShippingAddress.City, ctx.Order);
```

### Drop a specific index

```csharp
await store.DropIndexAsync<User>(u => u.Name, ctx.User);
```

### Drop all JSON indexes for a type

Removes all `idx_json_` indexes for the given type while preserving built-in indexes and indexes on other types.

```csharp
await store.DropAllIndexesAsync<User>();
```

Index names are deterministic (`idx_json_{typeName}_{jsonPath}` with dots replaced by underscores), so `CreateIndexAsync` and `DropIndexAsync` always agree on the name for a given expression. `CreateIndexAsync` uses `IF NOT EXISTS`, so calling it multiple times is safe.

## Supported Expression Reference

| Expression | SQL Output |
|---|---|
| `u.Name == "Alice"` | `json_extract(Data, '$.name') = @p0` |
| `u.Age > 25` | `json_extract(Data, '$.age') > @p0` |
| `u.Age == 25 && u.Name == "Alice"` | `(... AND ...)` |
| `u.Name == "A" \|\| u.Name == "B"` | `(... OR ...)` |
| `!(u.Name == "Alice")` | `NOT (...)` |
| `u.Email == null` | `... IS NULL` |
| `u.Email != null` | `... IS NOT NULL` |
| `u.Name.Contains("li")` | `... LIKE '%' \|\| @p0 \|\| '%'` |
| `u.Name.StartsWith("Al")` | `... LIKE @p0 \|\| '%'` |
| `u.Name.EndsWith("ob")` | `... LIKE '%' \|\| @p0` |
| `o.ShippingAddress.City == "X"` | `json_extract(Data, '$.shippingAddress.city') = @p0` |
| `o.Lines.Any(l => l.Name == "X")` | `EXISTS (SELECT 1 FROM json_each(...) WHERE ...)` |
| `o.Tags.Any(t => t == "priority")` | `EXISTS (SELECT 1 FROM json_each(...) WHERE value = @p0)` |
| `o.Tags.Any()` | `json_array_length(Data, '$.tags') > 0` |
| `o.Lines.Count() > 1` | `json_array_length(Data, '$.lines') > 1` |
| `o.Lines.Count(l => l.Qty > 2)` | `(SELECT COUNT(*) FROM json_each(...) WHERE ...) > 2` |
| `e.StartDate > cutoff` | `json_extract(Data, '$.startDate') > @p0` (ISO 8601 formatted) |
| `e.CreatedAt >= start` | `json_extract(Data, '$.createdAt') > @p0` (DateTimeOffset supported) |
| Captured variables | Extracted from closure at translate time |

### Projection expressions

| Expression | SQL Output |
|---|---|
| `x => new R { A = x.Name }` | `json_object('name', json_extract(Data, '$.name'))` |
| `x => new R { C = x.Nav.Prop }` | `json_object('c', json_extract(Data, '$.nav.prop'))` |
| `x => new R { N = x.Lines.Count() }` | `json_array_length(Data, '$.lines')` |
| `x => new R { N = x.Lines.Count(l => ...) }` | `(SELECT COUNT(*) FROM json_each(Data, '$.lines') WHERE ...)` |
| `x => new R { B = x.Tags.Any() }` | `CASE WHEN json_array_length(...) > 0 THEN json('true') ELSE json('false') END` |
| `x => new R { B = x.Tags.Any(t => ...) }` | `CASE WHEN EXISTS (SELECT 1 FROM json_each(...) WHERE ...) THEN json('true') ELSE json('false') END` |
