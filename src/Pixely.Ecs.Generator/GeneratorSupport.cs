using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Pixely.DependencyInjection.Generator;

namespace Pixely.Ecs.Generator;

enum SpanKind
{
    Entities,
    Span,
    ReadOnlySpan,
    Tag,
    Invalid
}

readonly record struct DiagnosticInfo(string Id, string Message, string FilePath, int Line, int Column, int EndLine, int EndColumn);

// Where a generated partial type goes: its namespace, the types that contain it and its own declaration.
readonly record struct TypeDeclaration(string? Namespace, EquatableArray<string> ContainingTypeDeclarations, string Declaration, string Name, string HintName);

static class GeneratorSupport
{
    public const string EntityName = "Pixely.Ecs.Entity";
    public const string TagName = "Pixely.Ecs.ITag";
    public const string QueryName = "Pixely.Ecs.Query";
    private const string SpanName = "System.Span<T>";
    private const string ReadOnlySpanName = "System.ReadOnlySpan<T>";

    public static bool IsTag(ITypeSymbol type) => type.AllInterfaces.Any(implemented => implemented.ToDisplayString() == TagName);

    // What a type argument of Query<...> reads: ReadOnlySpan<Entity>, or Span<T> or ReadOnlySpan<T> of a struct component that is not a ref struct,
    // a type parameter or a tag. element is the T of a span.
    public static SpanKind ClassifySpan(ITypeSymbol type, out ITypeSymbol? element)
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

    public static bool IsQueryType(INamedTypeSymbol type) => type.IsGenericType && type.ConstructedFrom.ToDisplayString().StartsWith(QueryName + "<");

    // The Query<...> that a query class derives from, with the type arguments of its base classes substituted.
    public static INamedTypeSymbol? GetQueryBase(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (IsQueryType(current))
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

    private static readonly SymbolDisplayFormat HintFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)
        .RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    // Null when the type or a type that contains it is not partial, or a containing type is generic. A file-local type is null too,
    // because the generated part would be another type in another file.
    public static TypeDeclaration? GetPartialDeclaration(INamedTypeSymbol type, TypeDeclarationSyntax syntax, string hintSuffix, CancellationToken cancellationToken)
    {
        List<INamedTypeSymbol> containingTypes = [];
        for (INamedTypeSymbol? containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            containingTypes.Insert(0, containing);
        }

        bool containingTypesArePartial = containingTypes.All(containing => !containing.IsGenericType && !containing.IsFileLocal && containing.DeclaringSyntaxReferences
            .Any(reference => reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)));
        if (type.IsGenericType || type.IsFileLocal || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword) || !containingTypesArePartial)
        {
            return null;
        }

        string? ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        string hintName = type.ToDisplayString(HintFormat) + hintSuffix;
        return new TypeDeclaration(ns, new EquatableArray<string>([.. containingTypes.Select(Declare)]), Declare(type), Escape(type.Name), hintName);
    }

    // Prefixes a keyword with @, so a declared name such as @event stays an identifier in the generated code.
    public static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    // Writes the namespace and the containing types around the generated part of the type, whose members body writes.
    public static string Generate(TypeDeclaration type, string baseList, Action<StringBuilder, string> body)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (type.Namespace is not null)
        {
            sb.AppendLine($"namespace {type.Namespace};");
            sb.AppendLine();
        }

        string indent = "";
        foreach (string declaration in type.ContainingTypeDeclarations)
        {
            sb.AppendLine($"{indent}{declaration}");
            sb.AppendLine($"{indent}{{");
            indent += "    ";
        }

        sb.AppendLine($"{indent}{type.Declaration} : {baseList}");
        sb.AppendLine($"{indent}{{");
        body(sb, indent + "    ");
        sb.AppendLine($"{indent}}}");

        while (indent.Length > 0)
        {
            indent = indent.Substring(4);
            sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }

    private static string Declare(INamedTypeSymbol type)
    {
        string keyword = (type.IsRecord, type.TypeKind) switch
        {
            (true, TypeKind.Struct) => "record struct",
            (true, _) => "record",
            (_, TypeKind.Struct) => "struct",
            (_, TypeKind.Interface) => "interface",
            _ => "class"
        };

        string readOnly = type.TypeKind == TypeKind.Struct && type.IsReadOnly ? "readonly " : "";
        string refLike = type.IsRefLikeType ? "ref " : "";
        return $"{readOnly}{refLike}partial {keyword} {Escape(type.Name)}";
    }
}
