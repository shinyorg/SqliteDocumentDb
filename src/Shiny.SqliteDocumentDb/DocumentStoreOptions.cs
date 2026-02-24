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

    /// <summary>
    /// When false, calling a reflection-based overload (without JsonTypeInfo&lt;T&gt;) throws an
    /// InvalidOperationException if the type cannot be resolved from the configured TypeInfoResolver.
    /// Set to false in AOT deployments to get clear errors instead of hard-to-diagnose trimming failures.
    /// Defaults to true.
    /// </summary>
    public bool UseReflectionFallback { get; set; } = true;

    /// <summary>
    /// Optional callback invoked with every SQL statement the store executes.
    /// Useful for debugging and diagnostics.
    /// </summary>
    public Action<string>? Logging { get; set; }
}
