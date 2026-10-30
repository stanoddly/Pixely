using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Pixely.Ecs.Generator;

enum SpanKind
{
    Entities,
    Span,
    ReadOnlySpan,
    OptionalSpan,
    OptionalReadOnlySpan,
    Tag,
    Invalid
}

readonly record struct DiagnosticInfo(string Id, string Message, string FilePath, int Line, int Column, int EndLine, int EndColumn);

static class GeneratorSupport
{
    public const string EntityName = "Pixely.Ecs.Entity";
    public const string TagName = "Pixely.Ecs.ITag";
    public const string RelationshipName = "Pixely.Ecs.IRelationship";
    public const string QueryName = "Pixely.Ecs.Query";
    public const string SpawnerName = "Pixely.Ecs.Spawner";
    private const string SpanName = "System.Span<T>";
    private const string ReadOnlySpanName = "System.ReadOnlySpan<T>";
    private const string OptionalName = "Pixely.Ecs.Optional<TSpan>";

    public static bool IsTag(ITypeSymbol type) => type.AllInterfaces.Any(implemented => implemented.ToDisplayString() == TagName);

    public static bool IsRelationship(ITypeSymbol type) => type.AllInterfaces.Any(implemented => implemented.ToDisplayString() == RelationshipName);

    // A relationship changes only at sync points, where the world updates its index, so no span or lookup writes one.
    public static string FormatRelationshipWrite(string subject, string relationship, string instead) =>
        $"{subject} writes {relationship}, a relationship, which changes only through ComponentAdder<{relationship}>, ComponentRemover<{relationship}> and spawners. {instead}";

    // What a type argument of Query<...> reads: ReadOnlySpan<Entity>, or Span<T> or ReadOnlySpan<T> of a struct component that is not a ref struct,
    // a type parameter or a tag, or such a span in Optional<...>. element is the T of a span.
    public static SpanKind ClassifySpan(ITypeSymbol type, out ITypeSymbol? element)
    {
        element = null;
        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return SpanKind.Invalid;
        }

        if (named.ConstructedFrom.ToDisplayString() == OptionalName)
        {
            return ClassifyRequiredSpan(named.TypeArguments[0], out element) switch
            {
                SpanKind.Span => SpanKind.OptionalSpan,
                SpanKind.ReadOnlySpan => SpanKind.OptionalReadOnlySpan,
                SpanKind.Tag => SpanKind.Tag,
                _ => SpanKind.Invalid
            };
        }

        return ClassifyRequiredSpan(named, out element);
    }

    private static SpanKind ClassifyRequiredSpan(ITypeSymbol type, out ITypeSymbol? element)
    {
        element = null;
        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return SpanKind.Invalid;
        }

        string span = named.ConstructedFrom.ToDisplayString();
        if (span != SpanName && span != ReadOnlySpanName)
        {
            return SpanKind.Invalid;
        }

        element = named.TypeArguments[0];
        if (element.ToDisplayString() == EntityName)
        {
            return span == ReadOnlySpanName ? SpanKind.Entities : SpanKind.Invalid;
        }

        if (IsTag(element))
        {
            return SpanKind.Tag;
        }

        if (!element.IsValueType || element.IsRefLikeType || ContainsTypeParameter(element))
        {
            return SpanKind.Invalid;
        }

        return span == SpanName ? SpanKind.Span : SpanKind.ReadOnlySpan;
    }

    public static bool IsQueryType(INamedTypeSymbol type) => IsConstructedFrom(type, QueryName);

    // The Query<...> that a query class derives from, with the type arguments of its base classes substituted.
    public static INamedTypeSymbol? GetQueryBase(INamedTypeSymbol type) => GetGenericBase(type, QueryName);

    // The Spawner<...> that a spawner class derives from, with the type arguments of its base classes substituted.
    public static INamedTypeSymbol? GetSpawnerBase(INamedTypeSymbol type) => GetGenericBase(type, SpawnerName);

    private static bool IsConstructedFrom(INamedTypeSymbol type, string name) => type.IsGenericType && type.ConstructedFrom.ToDisplayString().StartsWith(name + "<");

    private static INamedTypeSymbol? GetGenericBase(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (IsConstructedFrom(current, name))
            {
                return current;
            }
        }

        return null;
    }

    public static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter) || (named.ContainingType is not null && ContainsTypeParameter(named.ContainingType)),
        _ => false
    };

    public static DiagnosticInfo CreateDiagnostic(string id, string message, Location location)
    {
        FileLinePositionSpan span = location.GetLineSpan();
        return new DiagnosticInfo(id, message, span.Path, span.StartLinePosition.Line, span.StartLinePosition.Character, span.EndLinePosition.Line, span.EndLinePosition.Character);
    }

    public static void Report(SourceProductionContext context, DiagnosticInfo diagnostic)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor(diagnostic.Id, "Pixely.Ecs", diagnostic.Message, "Pixely.Ecs", DiagnosticSeverity.Error, isEnabledByDefault: true),
            Location.Create(diagnostic.FilePath, default, new LinePositionSpan(new LinePosition(diagnostic.Line, diagnostic.Column), new LinePosition(diagnostic.EndLine, diagnostic.EndColumn)))));
    }
}
