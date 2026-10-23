using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pixely.DependencyInjection.Generator;

namespace Pixely.Ecs.Generator;

// A null component type marks the entities field.
readonly record struct QueryField(string Name, string? ComponentType);

readonly record struct QueryModel(TypeDeclaration Type, EquatableArray<QueryField> Fields, EquatableArray<string> With, EquatableArray<string> Without);

readonly record struct QueryExtraction(QueryModel? Query, DiagnosticInfo? Diagnostic);

[Generator]
public class QueryGenerator : IIncrementalGenerator
{
    private const string QueryAttributeName = "Pixely.Ecs.QueryAttribute";
    private const string SpanName = "System.Span<T>";
    private const string ReadOnlySpanName = "System.ReadOnlySpan<T>";
    private const string WithAttributeName = "Pixely.Ecs.WithAttribute<T>";
    private const string WithoutAttributeName = "Pixely.Ecs.WithoutAttribute<T>";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<QueryExtraction> results = context.SyntaxProvider.ForAttributeWithMetadataName(
            QueryAttributeName,
            predicate: static (node, _) => node is StructDeclarationSyntax,
            transform: static (ctx, ct) => Extract(ctx, ct));

        context.RegisterSourceOutput(results, static (spc, result) =>
        {
            if (result.Diagnostic is { } diagnostic)
            {
                GeneratorSupport.Report(spc, diagnostic);
                return;
            }

            QueryModel query = result.Query!.Value;
            spc.AddSource(query.Type.HintName, Generate(query));
        });
    }

    private static QueryExtraction Extract(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        StructDeclarationSyntax syntax = (StructDeclarationSyntax)context.TargetNode;
        Location typeLocation = syntax.Identifier.GetLocation();

        TypeDeclaration? declaration = GeneratorSupport.GetPartialDeclaration(type, syntax, ".Query.g.cs", cancellationToken);
        if (!type.IsRefLikeType || declaration is null)
        {
            return Fail("PXECS001", $"The query {type.Name} must be a non-generic, non-file-local ref partial struct, and every type that contains it must be partial, non-generic and not file-local.", typeLocation);
        }

        // The generator adds Count, so a member of that name would collide with it.
        if (!type.GetMembers("Count").IsEmpty)
        {
            return Fail("PXECS013", $"The query {type.Name} must not declare a member named Count, which the generator adds.", type.GetMembers("Count")[0].Locations[0]);
        }

        List<QueryField> fields = [];
        foreach (ISymbol member in type.GetMembers())
        {
            if (member is not IFieldSymbol field || field.IsStatic || field.IsConst)
            {
                continue;
            }

            // An auto-property's backing field or a captured primary constructor parameter, which the generated constructor would leave unset.
            if (field.IsImplicitlyDeclared)
            {
                return field.AssociatedSymbol is { } property
                    ? Fail("PXECS002", $"The property {property.Name} of the query {type.Name} must be a field.", property.Locations[0])
                    : Fail("PXECS002", $"The query {type.Name} captures a primary constructor parameter, which would be left unset. A query holds only fields.",
                        syntax.ParameterList?.GetLocation() ?? typeLocation);
            }

            if (field.Type is not INamedTypeSymbol { IsGenericType: true } fieldType)
            {
                return FailField(field, type);
            }

            string span = fieldType.ConstructedFrom.ToDisplayString();
            ITypeSymbol element = fieldType.TypeArguments[0];
            bool isEntity = element.ToDisplayString() == GeneratorSupport.EntityName;
            if (isEntity && span == ReadOnlySpanName)
            {
                fields.Add(new QueryField(GeneratorSupport.Escape(field.Name), null));
            }
            else if (!isEntity && GeneratorSupport.IsTag(element))
            {
                return Fail("PXECS002", $"The field {field.Name} of the query {type.Name} reads {element.Name}, a tag, which has no data. Use [With<{element.Name}>] instead.", field.Locations[0]);
            }
            else if (!isEntity && element.IsValueType && !element.IsRefLikeType && element is not ITypeParameterSymbol && (span == SpanName || span == ReadOnlySpanName))
            {
                string component = element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fields.Any(existing => existing.ComponentType == component))
                {
                    return Fail("PXECS003", $"The query {type.Name} reads {element.Name} through more than one field.", field.Locations[0]);
                }

                fields.Add(new QueryField(GeneratorSupport.Escape(field.Name), component));
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

        return new QueryExtraction(new QueryModel(declaration.Value, new EquatableArray<QueryField>([.. fields]), new EquatableArray<string>([.. with]), new EquatableArray<string>([.. without])), null);
    }

    private static QueryExtraction FailField(IFieldSymbol field, INamedTypeSymbol type)
    {
        return Fail("PXECS002", $"The field {field.Name} of the query {type.Name} must be a Span<T> or ReadOnlySpan<T> of a struct component that is not a ref struct, or ReadOnlySpan<Entity>.", field.Locations[0]);
    }

    private static QueryExtraction Fail(string id, string message, Location location) => new QueryExtraction(null, GeneratorSupport.CreateDiagnostic(id, message, location));

    private static string Generate(QueryModel query)
    {
        string name = query.Type.Name;
        string queryInterface = $"global::Pixely.Ecs.IQuery<{name}>";
        return GeneratorSupport.Generate(query.Type, queryInterface, (sb, indent) =>
        {
            sb.AppendLine($"{indent}[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]");
            sb.AppendLine($"{indent}private {name}(global::Pixely.Ecs.ArchetypeChunk chunk)");
            sb.AppendLine($"{indent}{{");
            foreach (QueryField field in query.Fields)
            {
                string value = field.ComponentType is null ? "chunk.Entities" : $"chunk.GetSpan<{field.ComponentType}>()";
                sb.AppendLine($"{indent}    this.{field.Name} = {value};");
            }

            sb.AppendLine($"{indent}}}");
            sb.AppendLine();
            sb.AppendLine($"{indent}public int Count => this.{query.Fields[0].Name}.Length;");
            sb.AppendLine();
            sb.AppendLine($"{indent}static void {queryInterface}.Describe(global::Pixely.Ecs.QueryDescription description)");
            sb.AppendLine($"{indent}{{");
            foreach (QueryField field in query.Fields)
            {
                if (field.ComponentType is not null)
                {
                    sb.AppendLine($"{indent}    description.With<{field.ComponentType}>();");
                }
            }

            foreach (string component in query.With)
            {
                sb.AppendLine($"{indent}    description.With<{component}>();");
            }

            foreach (string component in query.Without)
            {
                sb.AppendLine($"{indent}    description.Without<{component}>();");
            }

            sb.AppendLine($"{indent}}}");
            sb.AppendLine();
            sb.AppendLine($"{indent}static {name} {queryInterface}.Create(global::Pixely.Ecs.ArchetypeChunk chunk) => new {name}(chunk);");
        });
    }
}
