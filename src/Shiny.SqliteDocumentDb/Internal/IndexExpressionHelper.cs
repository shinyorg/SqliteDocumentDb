using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.SqliteDocumentDb.Internal;

static class IndexExpressionHelper
{
    public static string ResolveJsonPath<T>(
        Expression<Func<T, object>> expression,
        JsonSerializerOptions jsonOptions,
        JsonTypeInfo<T> jsonTypeInfo)
    {
        var body = expression.Body;

        // Unwrap Convert (value-type boxing)
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        if (body is not MemberExpression member)
            throw new ArgumentException("Expression must be a property access.", nameof(expression));

        var chain = new List<string>();
        Expression? current = member;
        while (current is MemberExpression m)
        {
            chain.Insert(0, m.Member.Name);
            current = m.Expression;
        }

        if (current is not ParameterExpression)
            throw new ArgumentException("Expression must be a simple property chain on the parameter.", nameof(expression));

        return JsonPropertyNameResolver.BuildJsonPath(jsonOptions, jsonTypeInfo, chain);
    }

    public static string BuildIndexName(string typeName, string jsonPath)
    {
        var sanitizedType = typeName.Replace('.', '_');
        var sanitizedPath = jsonPath.Replace('.', '_');
        return $"idx_json_{sanitizedType}_{sanitizedPath}";
    }
}
