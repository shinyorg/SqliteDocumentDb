# Shiny.Data.Documents

A lightweight SQLite-based document store for .NET with JSON querying and full AOT/trimming support.

## Installation

```bash
dotnet add package Shiny.Data.Documents
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
