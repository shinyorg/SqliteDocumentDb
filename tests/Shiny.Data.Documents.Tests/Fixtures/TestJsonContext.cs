using System.Text.Json.Serialization;

namespace Shiny.Data.Documents.Tests.Fixtures;

[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(Event))]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(Address))]
[JsonSerializable(typeof(OrderLine))]
public partial class TestJsonContext : JsonSerializerContext;
