using System.Text.Json;

namespace Shiny.SqliteDocumentDb;

public enum TypeNameResolution
{
    ShortName,
    FullName
}

public class DocumentStoreOptions
{
    public required string ConnectionString { get; set; }
    public TypeNameResolution TypeNameResolution { get; set; } = TypeNameResolution.ShortName;
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
