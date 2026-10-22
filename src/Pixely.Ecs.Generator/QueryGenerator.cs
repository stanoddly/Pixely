using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Pixely.DependencyInjection.Generator;

namespace Pixely.Ecs.Generator;

readonly record struct DiagnosticInfo(string Id, string Message, string FilePath, int Line, int Column, int EndLine, int EndColumn);

// A null component type marks the entities field.
readonly record struct QueryField(string Name, string? ComponentType);

readonly record struct QueryModel(
    string HintName,
    string? Namespace,
    EquatableArray<string> ContainingTypeDeclarations,
    string Declaration,
    string Name,
    EquatableArray<QueryField> Fields,
    EquatableArray<string> With,
    EquatableArray<string> Without);

readonly record struct ExtractionResult(QueryModel? Query, DiagnosticInfo? Diagnostic);

[Generator]
public class QueryGenerator : IIncrementalGenerator
{
    private const string QueryAttributeName = "Pixely.Ecs.QueryAttribute";
    private const string EntityName = "Pixely.Ecs.Entity";
    private const string SpanName = "System.Span<T>";
    private const string ReadOnlySpanName = "System.ReadOnlySpan<T>";
    private const string WithAttributeName = "Pixely.Ecs.WithAttribute<T>";
    private const string WithoutAttributeName = "Pixely.Ecs.WithoutAttribute<T>";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ExtractionResult> results = context.SyntaxProvider.ForAttributeWithMetadataName(
            QueryAttributeName,
            predicate: static (node, _) => node is StructDeclarationSyntax,
            transform: static (ctx, ct) => Extract(ctx, ct));

        context.RegisterSourceOutput(results, static (spc, result) =>
        {
            if (result.Diagnostic is { } diagnostic)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(diagnostic.Id, "Pixely.Ecs", diagnostic.Message, "Pixely.Ecs", DiagnosticSeverity.Error, isEnabledByDefault: true),
                    Location.Create(diagnostic.FilePath, default, new LinePositionSpan(new LinePosition(diagnostic.Line, diagnostic.Column), new LinePosition(diagnostic.EndLine, diagnostic.EndColumn)))));
                return;
            }

            QueryModel query = result.Query!.Value;
            spc.AddSource(query.HintName, Generate(query));
        });
    }

    private static ExtractionResult Extract(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        StructDeclarationSyntax syntax = (StructDeclarationSyntax)context.TargetNode;
        Location typeLocation = syntax.Identifier.GetLocation();

        List<INamedTypeSymbol> containingTypes = [];
        for (INamedTypeSymbol? containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            containingTypes.Insert(0, containing);
        }

        bool containingTypesArePartial = containingTypes.All(containing => !containing.IsGenericType && containing.DeclaringSyntaxReferences
            .Any(reference => reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)));
        if (!type.IsRefLikeType || type.IsGenericType || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword) || !containingTypesArePartial)
        {
            return Fail("PXECS001", $"The query {type.Name} must be a non-generic ref partial struct, and every type that contains it must be partial and non-generic.", typeLocation);
        }

        List<QueryField> fields = [];
        foreach (ISymbol member in type.GetMembers())
        {
            if (member is not IFieldSymbol field || field.IsStatic || field.IsConst || field.IsImplicitlyDeclared)
            {
                continue;
            }

            if (field.Type is not INamedTypeSymbol { IsGenericType: true } fieldType)
            {
                return FailField(field, type);
            }

            string span = fieldType.ConstructedFrom.ToDisplayString();
            ITypeSymbol element = fieldType.TypeArguments[0];
            bool isEntity = element.ToDisplayString() == EntityName;
            if (isEntity && span == ReadOnlySpanName)
            {
                fields.Add(new QueryField(field.Name, null));
            }
            else if (!isEntity && element.IsValueType && element is not ITypeParameterSymbol && (span == SpanName || span == ReadOnlySpanName))
            {
                string component = element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fields.Any(existing => existing.ComponentType == component))
                {
                    return Fail("PXECS003", $"The query {type.Name} reads {element.Name} through more than one field.", field.Locations[0]);
                }

                fields.Add(new QueryField(field.Name, component));
            }
            else
            {
                return FailField(field, type);
            }
        }

        List<string> with = [];
        List<string> without = [];
        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is not { IsGenericType: true } attributeClass)
            {
                continue;
            }

            string attributeName = attributeClass.ConstructedFrom.ToDisplayString();
            string component = attributeClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (attributeName == WithAttributeName)
            {
                with.Add(component);
            }
            else if (attributeName == WithoutAttributeName)
            {
                without.Add(component);
            }
        }

        List<string> required = fields.Where(field => field.ComponentType is not null).Select(field => field.ComponentType!).Concat(with).ToList();
        // Count reads the first field's length, so a query without fields would need a field of its own, and a partial struct must keep its fields in one declaration.
        if (fields.Count == 0 || required.Count == 0)
        {
            return Fail("PXECS004", $"The query {type.Name} must declare at least one field and require at least one component through a Span<T> or ReadOnlySpan<T> field or [With<T>].", typeLocation);
        }

        string? contradiction = without.FirstOrDefault(required.Contains);
        if (contradiction is not null)
        {
            return Fail("PXECS005", $"The query {type.Name} both requires and excludes {contradiction}, so it matches no entity.", typeLocation);
        }

        string? ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        string hintName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "") + ".Query.g.cs";

        return new ExtractionResult(new QueryModel(
            hintName,
            ns,
            new EquatableArray<string>([.. containingTypes.Select(Declare)]),
            Declare(type),
            type.Name,
            new EquatableArray<QueryField>([.. fields]),
            new EquatableArray<string>([.. with]),
            new EquatableArray<string>([.. without])), null);
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
        return $"{readOnly}{refLike}partial {keyword} {type.Name}";
    }

    private static ExtractionResult FailField(IFieldSymbol field, INamedTypeSymbol type)
    {
        return Fail("PXECS002", $"The field {field.Name} of the query {type.Name} must be a Span<T> or ReadOnlySpan<T> of a struct component, or ReadOnlySpan<Entity>.", field.Locations[0]);
    }

    private static ExtractionResult Fail(string id, string message, Location location)
    {
        FileLinePositionSpan span = location.GetLineSpan();
        return new ExtractionResult(null, new DiagnosticInfo(id, message, span.Path, span.StartLinePosition.Line, span.StartLinePosition.Character, span.EndLinePosition.Line, span.EndLinePosition.Character));
    }

    private static string Generate(QueryModel query)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (query.Namespace is not null)
        {
            sb.AppendLine($"namespace {query.Namespace};");
            sb.AppendLine();
        }

        string indent = "";
        foreach (string declaration in query.ContainingTypeDeclarations)
        {
            sb.AppendLine($"{indent}{declaration}");
            sb.AppendLine($"{indent}{{");
            indent += "    ";
        }

        string name = query.Name;
        string queryInterface = $"global::Pixely.Ecs.IQuery<{name}>";
        sb.AppendLine($"{indent}{query.Declaration} : {queryInterface}");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    private {name}(global::Pixely.Ecs.Archetype archetype)");
        sb.AppendLine($"{indent}    {{");
        foreach (QueryField field in query.Fields)
        {
            string value = field.ComponentType is null ? "archetype.Entities" : $"archetype.GetSpan<{field.ComponentType}>()";
            sb.AppendLine($"{indent}        {field.Name} = {value};");
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();
        sb.AppendLine($"{indent}    public int Count => {query.Fields[0].Name}.Length;");
        sb.AppendLine();
        sb.AppendLine($"{indent}    static void {queryInterface}.Describe(global::Pixely.Ecs.QueryDescription description)");
        sb.AppendLine($"{indent}    {{");
        foreach (QueryField field in query.Fields)
        {
            if (field.ComponentType is not null)
            {
                sb.AppendLine($"{indent}        description.With<{field.ComponentType}>();");
            }
        }

        foreach (string component in query.With)
        {
            sb.AppendLine($"{indent}        description.With<{component}>();");
        }

        foreach (string component in query.Without)
        {
            sb.AppendLine($"{indent}        description.Without<{component}>();");
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();
        sb.AppendLine($"{indent}    static {name} {queryInterface}.Create(global::Pixely.Ecs.Archetype archetype) => new {name}(archetype);");
        sb.AppendLine($"{indent}}}");

        while (indent.Length > 0)
        {
            indent = indent.Substring(4);
            sb.AppendLine($"{indent}}}");
        }

        return sb.ToString();
    }
}
