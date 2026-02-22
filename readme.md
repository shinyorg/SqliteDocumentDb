# Shiny.SqliteDocumentDb

A lightweight SQLite-based document store for .NET that turns SQLite into a schema-free JSON document database with LINQ querying and full AOT/trimming support.

## Features

- **Zero schema, zero migrations** — store entire object graphs (nested objects, child collections) as JSON documents. No `CREATE TABLE`, no `ALTER TABLE`, no JOINs.
- **LINQ expression queries** — write `o => o.ShippingAddress.City == "Portland"` and it translates to `json_extract` SQL automatically. Supports nested properties, `Any()`, `Count()`, string methods, null checks, and captured variables.
- **`IAsyncEnumerable<T>` streaming** — yield results one-at-a-time with `GetAllStream` and `QueryStream` instead of buffering into a list. Eliminates Gen1 GC pressure at scale with comparable throughput.
- **Expression-based JSON indexes** — `store.CreateIndexAsync<User>(u => u.Name, ctx.User)` creates a partial `json_extract` index. Up to **30x faster** queries on indexed properties.
- **SQL-level projections** — project into DTOs with `json_object` at the database level. No full document deserialization needed.
- **Full AOT/trimming support** — every API has a `JsonTypeInfo<T>` overload for source-generated JSON serialization. No reflection required.
- **10-30x faster nested inserts** vs sqlite-net — one write per document vs multiple table inserts with foreign keys. 2-10x faster reads on nested data.
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

## Basic CRUD Operations

### Store a document (auto-generated ID)

```csharp
var id = await store.Set(new User { Name = "Alice", Age = 25 }, ctx.User);
```

### Store a document (explicit ID)

```csharp
await store.Set("user-1", new User { Name = "Alice", Age = 25 }, ctx.User);
```

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
