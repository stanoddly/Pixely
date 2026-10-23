using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pixely.DependencyInjection.Generator;

namespace Pixely.Ecs.Generator;

// Member is the field, or the auto-property whose backing field it is.
readonly record struct BlueprintField(string Member, string ComponentType);

readonly record struct BlueprintModel(TypeDeclaration Type, EquatableArray<BlueprintField> Fields);

readonly record struct BlueprintExtraction(BlueprintModel? Blueprint, DiagnosticInfo? Diagnostic);

[Generator]
public class BlueprintGenerator : IIncrementalGenerator
{
    private const string BlueprintAttributeName = "Pixely.Ecs.BlueprintAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<BlueprintExtraction> results = context.SyntaxProvider.ForAttributeWithMetadataName(
            BlueprintAttributeName,
            predicate: static (node, _) => node is StructDeclarationSyntax or RecordDeclarationSyntax,
            transform: static (ctx, ct) => Extract(ctx, ct));

        context.RegisterSourceOutput(results, static (spc, result) =>
        {
            if (result.Diagnostic is { } diagnostic)
            {
                GeneratorSupport.Report(spc, diagnostic);
                return;
            }

            BlueprintModel blueprint = result.Blueprint!.Value;
            spc.AddSource(blueprint.Type.HintName, Generate(blueprint));
        });
    }

    private static BlueprintExtraction Extract(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        TypeDeclarationSyntax syntax = (TypeDeclarationSyntax)context.TargetNode;
        Location typeLocation = syntax.Identifier.GetLocation();

        TypeDeclaration? declaration = GeneratorSupport.GetPartialDeclaration(type, syntax, ".Blueprint.g.cs", cancellationToken);
        if (type.TypeKind != TypeKind.Struct || type.IsRefLikeType || declaration is null)
        {
            return Fail("PXECS006", $"The blueprint {type.Name} must be a non-generic, non-file-local partial struct that is not a ref struct, and every type that contains it must be partial, non-generic and not file-local.", typeLocation);
        }

        List<BlueprintField> fields = [];
        foreach (ISymbol member in type.GetMembers())
        {
            if (member is not IFieldSymbol field || field.IsStatic || field.IsConst)
            {
                continue;
            }

            ISymbol? declared = field.IsImplicitlyDeclared ? field.AssociatedSymbol : field;
            if (declared is null)
            {
                continue;
            }

            ITypeSymbol component = field.Type;
            if (!component.IsValueType || component.IsRefLikeType || component is ITypeParameterSymbol || component.ToDisplayString() == GeneratorSupport.EntityName)
            {
                return Fail("PXECS007", $"The member {declared.Name} of the blueprint {type.Name} must be a struct component, not Entity, a ref struct or a type parameter.", declared.Locations[0]);
            }

            string componentName = component.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (fields.Any(existing => existing.ComponentType == componentName))
            {
                return Fail("PXECS008", $"The blueprint {type.Name} holds {component.Name} in more than one member.", declared.Locations[0]);
            }

            fields.Add(new BlueprintField(GeneratorSupport.Escape(declared.Name), componentName));
        }

        if (fields.Count == 0)
        {
            return Fail("PXECS009", $"The blueprint {type.Name} must hold at least one component.", typeLocation);
        }

        return new BlueprintExtraction(new BlueprintModel(declaration.Value, new EquatableArray<BlueprintField>([.. fields])), null);
    }

    private static BlueprintExtraction Fail(string id, string message, Location location) => new BlueprintExtraction(null, GeneratorSupport.CreateDiagnostic(id, message, location));

    private static string Generate(BlueprintModel blueprint)
    {
        string name = blueprint.Type.Name;
        string blueprintInterface = $"global::Pixely.Ecs.IBlueprint<{name}>";
        return GeneratorSupport.Generate(blueprint.Type, blueprintInterface, (sb, indent) =>
        {
            sb.AppendLine($"{indent}static void {blueprintInterface}.Describe(global::Pixely.Ecs.BlueprintDescription description)");
            sb.AppendLine($"{indent}{{");
            foreach (BlueprintField field in blueprint.Fields)
            {
                sb.AppendLine($"{indent}    description.Add<{field.ComponentType}>();");
            }

            sb.AppendLine($"{indent}}}");
            sb.AppendLine();
            sb.AppendLine($"{indent}static void {blueprintInterface}.Write(global::System.ReadOnlySpan<{name}> blueprints, global::Pixely.Ecs.BlueprintColumns columns)");
            sb.AppendLine($"{indent}{{");
            for (int i = 0; i < blueprint.Fields.Length; i++)
            {
                sb.AppendLine($"{indent}    global::System.Span<{blueprint.Fields[i].ComponentType}> column{i} = columns.Get<{blueprint.Fields[i].ComponentType}>({i});");
            }

            sb.AppendLine($"{indent}    for (int i = 0; i < blueprints.Length; i++)");
            sb.AppendLine($"{indent}    {{");
            for (int i = 0; i < blueprint.Fields.Length; i++)
            {
                sb.AppendLine($"{indent}        column{i}[i] = blueprints[i].{blueprint.Fields[i].Member};");
            }

            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");
        });
    }
}
