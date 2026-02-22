namespace Shiny.Data.Documents;

public record DocumentRecord(
    string Id,
    string TypeName,
    string Data,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
